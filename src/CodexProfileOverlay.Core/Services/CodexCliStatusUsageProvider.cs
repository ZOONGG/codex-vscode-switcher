using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using CodexProfileOverlay.Core.Models;

namespace CodexProfileOverlay.Core.Services;

public sealed class CodexCliStatusUsageProvider : IUsageProvider
{
    public const string SourceIdentifier = "codex-cli-status";

    private readonly ICodexStatusTerminal terminal;
    private readonly TimeZoneInfo localTimeZone;

    public CodexCliStatusUsageProvider()
        : this(new WindowsConPtyCodexStatusTerminal(), TimeZoneInfo.Local)
    {
    }

    public CodexCliStatusUsageProvider(ICodexStatusTerminal terminal, TimeZoneInfo? localTimeZone = null)
    {
        this.terminal = terminal;
        this.localTimeZone = localTimeZone ?? TimeZoneInfo.Local;
    }

    public UsageProviderCapability Capability => terminal.IsAvailable ? UsageProviderCapability.Supported : UsageProviderCapability.Unavailable;

    public async Task<UsageSnapshot?> GetUsageAsync(string profileDirectory, CancellationToken cancellationToken)
    {
        if (!terminal.IsAvailable)
        {
            return null;
        }

        CodexStatusCapture? capture = await terminal.CaptureStatusAsync(profileDirectory, cancellationToken).ConfigureAwait(false);
        return capture is null
            ? null
            : CodexStatusParser.Parse(capture.StatusOutput, capture.CapturedAt, capture.CodexCliVersion, localTimeZone);
    }
}

public sealed record CodexStatusCapture(string StatusOutput, DateTimeOffset CapturedAt, string? CodexCliVersion);

public interface ICodexStatusTerminal
{
    bool IsAvailable { get; }

    Task<CodexStatusCapture?> CaptureStatusAsync(string profileDirectory, CancellationToken cancellationToken);
}

public static class CodexStatusParser
{
    private static readonly Regex AnsiPattern = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\a]*(?:\a|\x1B\\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex LimitRowPattern = new(
        @"(?im)^\s*(?<label>5h\s+limit|Weekly\s+limit)\s*:\s*(?:[^\r\n%]*?)?(?<percent>\d{1,3})\s*%\s+left\s*\(\s*resets\s+(?<reset>[^)\r\n]+)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string StripTerminalControlSequences(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string withoutAnsi = AnsiPattern.Replace(value, string.Empty);
        var builder = new StringBuilder(withoutAnsi.Length);
        foreach (char character in withoutAnsi)
        {
            if (IsTerminalDrawingCharacter(character))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    public static UsageSnapshot? Parse(
        string terminalOutput,
        DateTimeOffset capturedAt,
        string? codexCliVersion,
        TimeZoneInfo? localTimeZone = null)
    {
        string sanitized = StripTerminalControlSequences(terminalOutput);
        MatchCollection matches = LimitRowPattern.Matches(sanitized);
        if (matches.Count == 0)
        {
            return null;
        }

        localTimeZone ??= TimeZoneInfo.Local;
        var windows = new List<UsageLimitWindow>();
        foreach (Match match in matches.Cast<Match>())
        {
            if (!int.TryParse(match.Groups["percent"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int percent)
                || percent is < 0 or > 100)
            {
                continue;
            }

            string label = NormalizeLabel(match.Groups["label"].Value);
            windows.Add(new UsageLimitWindow
            {
                Name = label,
                Duration = label.Equals("5h", StringComparison.OrdinalIgnoreCase) ? TimeSpan.FromHours(5) : null,
                RemainingPercent = percent,
                ResetAt = ParseResetTime(match.Groups["reset"].Value, capturedAt, localTimeZone),
            });
        }

        if (windows.Count == 0)
        {
            return null;
        }

        return new UsageSnapshot
        {
            Windows = windows,
            CapturedAt = capturedAt.ToUniversalTime(),
            Source = CodexCliStatusUsageProvider.SourceIdentifier,
            CodexCliVersion = string.IsNullOrWhiteSpace(codexCliVersion) ? null : codexCliVersion.Trim(),
            IsExhausted = windows.Any(window => window.RemainingPercent == 0),
        };
    }

    private static DateTimeOffset? ParseResetTime(string value, DateTimeOffset capturedAt, TimeZoneInfo localTimeZone)
    {
        string text = Regex.Replace(value.Trim(), @"\s+", " ");
        DateTimeOffset localCapture = TimeZoneInfo.ConvertTime(capturedAt, localTimeZone);

        Match timeOnly = Regex.Match(text, @"^(?<hour>\d{1,2}):(?<minute>\d{2})$", RegexOptions.CultureInvariant);
        if (timeOnly.Success
            && int.TryParse(timeOnly.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int hour)
            && int.TryParse(timeOnly.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minute)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59)
        {
            DateTime localDateTime = new(localCapture.Year, localCapture.Month, localCapture.Day, hour, minute, 0, DateTimeKind.Unspecified);
            if (localDateTime <= localCapture.DateTime.AddMinutes(-1))
            {
                localDateTime = localDateTime.AddDays(1);
            }

            return ToOffset(localDateTime, localTimeZone);
        }

        Match dateTime = Regex.Match(
            text,
            @"^(?<hour>\d{1,2}):(?<minute>\d{2})\s+on\s+(?<day>\d{1,2})\s+(?<month>[A-Za-z]{3,})$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (dateTime.Success
            && int.TryParse(dateTime.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out hour)
            && int.TryParse(dateTime.Groups["minute"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out minute)
            && int.TryParse(dateTime.Groups["day"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            && TryParseMonth(dateTime.Groups["month"].Value, out int month)
            && hour is >= 0 and <= 23
            && minute is >= 0 and <= 59)
        {
            int year = localCapture.Year;
            if (month < localCapture.Month - 6)
            {
                year++;
            }

            if (DateTime.DaysInMonth(year, month) >= day)
            {
                return ToOffset(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), localTimeZone);
            }
        }

        return null;
    }

    private static DateTimeOffset ToOffset(DateTime localDateTime, TimeZoneInfo localTimeZone)
    {
        TimeSpan offset = localTimeZone.GetUtcOffset(localDateTime);
        return new DateTimeOffset(localDateTime, offset).ToUniversalTime();
    }

    private static bool TryParseMonth(string value, out int month)
    {
        for (int index = 1; index <= 12; index++)
        {
            string abbreviated = CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(index);
            string full = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(index);
            if (value.Equals(abbreviated, StringComparison.OrdinalIgnoreCase)
                || value.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                month = index;
                return true;
            }
        }

        month = 0;
        return false;
    }

    private static string NormalizeLabel(string label)
        => label.StartsWith("5h", StringComparison.OrdinalIgnoreCase) ? "5h" : "Weekly";

    private static bool IsTerminalDrawingCharacter(char character)
        => character is >= '\u2500' and <= '\u259f'
            || character is >= '\u2800' and <= '\u28ff'
            || character is '╭' or '╮' or '╰' or '╯' or '│' or '┃' or '─' or '━';
}

public sealed class WindowsConPtyCodexStatusTerminal : ICodexStatusTerminal
{
    private const int MaxCapturedCharacters = 20000;
    private const uint CreateUnicodeEnvironment = 0x00000400;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint ProcThreadAttributePseudoConsole = 0x00020016;

    public bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && FindCodexCommand() is not null;

    public async Task<CodexStatusCapture?> CaptureStatusAsync(string profileDirectory, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        string fullProfileDirectory = Path.GetFullPath(profileDirectory);
        Directory.CreateDirectory(fullProfileDirectory);
        string codexCliVersion = await ReadCodexVersionAsync(cancellationToken).ConfigureAwait(false);
        string workingDirectory = Path.Combine(Path.GetTempPath(), "codex-profile-overlay-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(workingDirectory);

        try
        {
            return await CaptureWithConPtyAsync(fullProfileDirectory, workingDirectory, codexCliVersion, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(workingDirectory);
        }
    }

    private static async Task<CodexStatusCapture?> CaptureWithConPtyAsync(
        string profileDirectory,
        string workingDirectory,
        string codexCliVersion,
        CancellationToken cancellationToken)
    {
        CreatePipePair(out SafeFileHandle pseudoConsoleInputRead, out SafeFileHandle inputWrite);
        using (pseudoConsoleInputRead)
        using (inputWrite)
        {
            CreatePipePair(out SafeFileHandle outputRead, out SafeFileHandle pseudoConsoleOutputWrite);
            using (outputRead)
            using (pseudoConsoleOutputWrite)
        {
            IntPtr pseudoConsole = CreatePseudoConsoleOrThrow(pseudoConsoleInputRead, pseudoConsoleOutputWrite);
            IntPtr attributeList = IntPtr.Zero;
            IntPtr pseudoConsolePointer = IntPtr.Zero;
            ProcessInformation processInformation = default;
            try
            {
                StartupInfoEx startupInfo = CreateStartupInfo(pseudoConsole, out attributeList, out pseudoConsolePointer);
                string commandPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                string commandLine = $"\"{commandPath}\" /d /s /c \"codex --no-alt-screen\"";
                string environmentBlock = BuildEnvironmentBlock(profileDirectory);

                if (!CreateProcessW(
                    null,
                    new StringBuilder(commandLine),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                    environmentBlock,
                    workingDirectory,
                    ref startupInfo,
                    out processInformation))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                pseudoConsoleInputRead.Dispose();
                pseudoConsoleOutputWrite.Dispose();

                using var inputStream = new FileStream(inputWrite, FileAccess.Write, 4096, isAsync: false);
                using var outputStream = new FileStream(outputRead, FileAccess.Read, 4096, isAsync: false);
                var output = new StringBuilder();
                _ = ReadOutputAsync(outputStream, output, cancellationToken);

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                await TryCaptureStatusRowsAsync(inputStream, output, cancellationToken).ConfigureAwait(false);
                DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
                await WriteTerminalCommandAsync(inputStream, "/quit", CancellationToken.None).ConfigureAwait(false);
                _ = WaitForSingleObject(processInformation.Process, 3000);
                await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None).ConfigureAwait(false);

                return new CodexStatusCapture(output.ToString(), capturedAt, codexCliVersion);
            }
            finally
            {
                if (processInformation.Thread != IntPtr.Zero)
                {
                    _ = CloseHandle(processInformation.Thread);
                }

                if (processInformation.Process != IntPtr.Zero)
                {
                    if (WaitForSingleObject(processInformation.Process, 0) == 0x00000102)
                    {
                        _ = TerminateProcess(processInformation.Process, 1);
                    }

                    _ = CloseHandle(processInformation.Process);
                }

                if (attributeList != IntPtr.Zero)
                {
                    DeleteProcThreadAttributeList(attributeList);
                    Marshal.FreeHGlobal(attributeList);
                }

                if (pseudoConsolePointer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(pseudoConsolePointer);
                }

                if (pseudoConsole != IntPtr.Zero)
                {
                    ClosePseudoConsole(pseudoConsole);
                }
            }
        }
        }
    }

    private static Task ReadOutputAsync(Stream stream, StringBuilder output, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var buffer = new byte[2048];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[4096];
            while (!cancellationToken.IsCancellationRequested)
            {
                int read;
                try
                {
                    read = stream.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                if (read <= 0)
                {
                    return;
                }

                int charCount = decoder.GetChars(buffer, 0, read, chars, 0);
                lock (output)
                {
                    output.Append(chars, 0, charCount);
                    if (output.Length > MaxCapturedCharacters)
                    {
                        output.Remove(0, output.Length - MaxCapturedCharacters);
                    }
                }
            }
        }, CancellationToken.None);

    private static async Task<bool> TryCaptureStatusRowsAsync(Stream inputStream, StringBuilder output, CancellationToken cancellationToken)
    {
        DateTimeOffset nextStatusRequest = DateTimeOffset.MinValue;
        DateTimeOffset stopAt = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < stopAt)
        {
            string current;
            lock (output)
            {
                current = output.ToString();
            }

            UsageSnapshot? parsed = CodexStatusParser.Parse(current, DateTimeOffset.UtcNow, null);
            if (parsed?.Windows.Count > 0)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= nextStatusRequest)
            {
                await WriteTerminalCommandAsync(inputStream, "/status", cancellationToken).ConfigureAwait(false);
                nextStatusRequest = DateTimeOffset.UtcNow.AddSeconds(2);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private static Task WriteTerminalCommandAsync(Stream stream, string command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = Encoding.UTF8.GetBytes(command + "\r");
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
        return Task.CompletedTask;
    }

    private static async Task<string> ReadCodexVersionAsync(CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /s /c codex --version",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return output.Trim();
    }

    private static StartupInfoEx CreateStartupInfo(IntPtr pseudoConsole, out IntPtr attributeList, out IntPtr pseudoConsolePointer)
    {
        IntPtr size = IntPtr.Zero;
        _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        attributeList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        pseudoConsolePointer = Marshal.AllocHGlobal(IntPtr.Size);
        Marshal.WriteIntPtr(pseudoConsolePointer, pseudoConsole);
        if (!UpdateProcThreadAttribute(
            attributeList,
            0,
            (IntPtr)ProcThreadAttributePseudoConsole,
            pseudoConsolePointer,
            (IntPtr)IntPtr.Size,
            IntPtr.Zero,
            IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var startupInfo = new StartupInfoEx
        {
            StartupInfo = new StartupInfo
            {
                Cb = Marshal.SizeOf<StartupInfoEx>(),
            },
            AttributeList = attributeList,
        };
        return startupInfo;
    }

    private static IntPtr CreatePseudoConsoleOrThrow(SafeFileHandle inputRead, SafeFileHandle outputWrite)
    {
        int result = CreatePseudoConsole(new Coord(120, 40), inputRead, outputWrite, 0, out IntPtr pseudoConsole);
        if (result != 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        return pseudoConsole;
    }

    private static void CreatePipePair(out SafeFileHandle read, out SafeFileHandle write)
    {
        if (!CreatePipe(out read, out write, IntPtr.Zero, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static string BuildEnvironmentBlock(string profileDirectory)
    {
        var values = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value && !string.IsNullOrWhiteSpace(key))
            {
                values[key] = value;
            }
        }

        values["CODEX_HOME"] = profileDirectory;
        var builder = new StringBuilder();
        foreach ((string key, string value) in values)
        {
            builder.Append(key).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString();
    }

    private static string? FindCodexCommand()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string[] extensions = [".exe", ".cmd", ".bat"];
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory, "codex" + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        string lpEnvironment,
        string lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Coord(short X, short Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int Cb;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }
}
