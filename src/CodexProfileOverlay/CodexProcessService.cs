using System.Diagnostics;
using System.IO;
using CodexProfileOverlay.Core.Services;

namespace CodexProfileOverlay;

internal sealed class CodexProcessService
{
    private readonly SafeLogger logger;

    public CodexProcessService(SafeLogger logger)
    {
        this.logger = logger;
    }

    public async Task CloseCodexAsync(int gracefulTimeoutSeconds, bool allowForceClose, CancellationToken cancellationToken)
    {
        var processes = FindCodexProcesses().ToArray();
        foreach (Process process in processes)
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        logger.Info($"Requesting Codex close for process {process.Id}.");
                        _ = process.CloseMainWindow();
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    logger.Error("Failed to request Codex close.", exception);
                }
            }
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(gracefulTimeoutSeconds, 1, 60));
        while (DateTimeOffset.UtcNow < deadline && FindCodexProcesses().Any())
        {
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        if (!allowForceClose)
        {
            return;
        }

        foreach (Process process in FindCodexProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        logger.Info($"Terminating Codex process {process.Id} after graceful timeout.");
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    logger.Error("Failed to terminate Codex process.", exception);
                }
            }
        }
    }

    public void LaunchCodex()
    {
        if (TryLaunchStartMenuApp())
        {
            return;
        }

        LaunchCodexFromCli();
    }

    public void LaunchCodexFromCli()
    {
        logger.Info("Falling back to 'codex app'.");
        var startInfo = new ProcessStartInfo
        {
            FileName = "codex",
            Arguments = "app",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment.Remove("CODEX_HOME");
        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start 'codex app'.");
    }

    public async Task<bool> LoginProfileAsync(string profileDirectory, CancellationToken cancellationToken)
    {
        string fullProfileDirectory = Path.GetFullPath(profileDirectory);
        Directory.CreateDirectory(fullProfileDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /c codex login",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = fullProfileDirectory,
        };
        startInfo.Environment["CODEX_HOME"] = fullProfileDirectory;

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start codex login.");
        int loginUrlOpened = 0;
        Task standardOutputTask = ReadLoginOutputAsync(process.StandardOutput, OpenLoginUrl);
        Task standardErrorTask = ReadLoginOutputAsync(process.StandardError, OpenLoginUrl);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return process.ExitCode == 0 && File.Exists(Path.Combine(fullProfileDirectory, "auth.json"));

        void OpenLoginUrl(string line)
        {
            string? url = FindLoginUrl(line);
            if (url is null || Interlocked.Exchange(ref loginUrlOpened, 1) != 0)
            {
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true,
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                logger.Error("Could not open browser for Codex login.", exception);
            }
        }
    }

    private static async Task ReadLoginOutputAsync(TextReader reader, Action<string> onLine)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            onLine(line);
        }
    }

    private static string? FindLoginUrl(string line)
    {
        const string prefix = "https://auth.openai.com/oauth/authorize";
        int start = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        int end = line.IndexOfAny([' ', '\t', '\r', '\n'], start);
        string url = end < 0 ? line[start..] : line[start..end];
        return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.AbsoluteUri : null;
    }

    private bool TryLaunchStartMenuApp()
    {
        string? appId = ResolveStartAppId();
        if (!string.IsNullOrWhiteSpace(appId))
        {
            logger.Info("Launching Codex through Start menu AppUserModelID.");
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\{appId}",
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException("Could not start Codex through Start menu AppUserModelID.");
            return true;
        }

        string? shortcut = FindStartMenuShortcut();
        if (!string.IsNullOrWhiteSpace(shortcut))
        {
            logger.Info("Launching Codex through Start menu shortcut.");
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = shortcut,
                UseShellExecute = true,
            }) ?? throw new InvalidOperationException("Could not start Codex through Start menu shortcut.");
            return true;
        }

        return false;
    }

    private static string? ResolveStartAppId()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-StartApps | Where-Object { $_.Name -eq 'Codex' -or $_.Name -like '*Codex*' } | Select-Object -First 1 -ExpandProperty AppID)\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = false,
                CreateNoWindow = true,
            });

            string? output = process?.StandardOutput.ReadLine();
            process?.WaitForExit(3000);
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static string? FindStartMenuShortcut()
    {
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        ];

        foreach (string root in roots.Where(Directory.Exists))
        {
            string? shortcut = Directory.EnumerateFiles(root, "*Codex*.lnk", SearchOption.AllDirectories)
                .OrderBy(static path => path.Length)
                .FirstOrDefault();
            if (shortcut is not null)
            {
                return shortcut;
            }
        }

        return null;
    }

    private static IEnumerable<Process> FindCodexProcesses()
    {
        return Process.GetProcesses()
            .Where(static process =>
            {
                try
                {
                    if (process.Id == Environment.ProcessId)
                    {
                        return false;
                    }

                    return process.ProcessName.Contains("Codex", StringComparison.OrdinalIgnoreCase)
                        && !process.ProcessName.Contains("Overlay", StringComparison.OrdinalIgnoreCase);
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            });
    }
}
