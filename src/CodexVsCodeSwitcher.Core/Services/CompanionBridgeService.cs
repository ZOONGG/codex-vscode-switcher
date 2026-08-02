using System.Text.Json;
using System.Text.Json.Serialization;
using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class CompanionBridgeService
{
    public const int ProtocolVersion = 1;
    public const string BridgePathEnvironmentVariable = "CODEX_VSCODE_SWITCHER_BRIDGE_PATH";
    public const string SessionEnvironmentVariable = "CODEX_VSCODE_SWITCHER_SESSION_ID";
    public const string OpenCodexEnvironmentVariable = "CODEX_VSCODE_SWITCHER_OPEN_CODEX";
    public const string UserDataEnvironmentVariable = "CODEX_VSCODE_SWITCHER_USER_DATA_DIR";
    public const string StateFileName = "workspace-state.json";
    public const string CommandFileName = "command-request.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string bridgeDirectory;
    private readonly string companionExtensionDirectory;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly IWorkspaceHistoryService workspaceHistory;
    private string? activeSessionId;
    private DateTimeOffset lastProcessedTimestampUtc;

    public CompanionBridgeService(
        string bridgeDirectory,
        string companionExtensionDirectory,
        IProtectedPathPolicy protectedPaths,
        IWorkspaceHistoryService workspaceHistory)
    {
        this.bridgeDirectory = Path.GetFullPath(bridgeDirectory);
        this.companionExtensionDirectory = Path.GetFullPath(companionExtensionDirectory);
        this.protectedPaths = protectedPaths;
        this.workspaceHistory = workspaceHistory;
        protectedPaths.AssertCanWrite(this.bridgeDirectory);
        protectedPaths.AssertCanRead(this.companionExtensionDirectory);
    }

    public ManagedCompanionLaunchOptions BeginSession(bool openCodexAutomatically)
    {
        if (!File.Exists(Path.Combine(companionExtensionDirectory, "package.json"))
            || !File.Exists(Path.Combine(companionExtensionDirectory, "extension.js")))
        {
            throw new DirectoryNotFoundException("The bundled Codex VS Code companion extension is unavailable.");
        }

        Directory.CreateDirectory(bridgeDirectory);
        activeSessionId = Guid.NewGuid().ToString("N");
        lastProcessedTimestampUtc = DateTimeOffset.MinValue;
        DeleteBridgeFile(StateFileName);
        DeleteBridgeFile(CommandFileName);
        return new ManagedCompanionLaunchOptions(
            companionExtensionDirectory,
            bridgeDirectory,
            activeSessionId,
            openCodexAutomatically);
    }

    public bool TryResumeSession()
    {
        string path = GetBridgeFile(StateFileName);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            protectedPaths.AssertCanRead(path);
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > 256 * 1024)
            {
                return false;
            }

            using JsonDocument json = JsonDocument.Parse(stream);
            string? session = json.RootElement.GetProperty("sessionId").GetString();
            if (session is null
                || session.Length != 32
                || session.Any(static character => !Uri.IsHexDigit(character)))
            {
                return false;
            }

            activeSessionId = session;
            lastProcessedTimestampUtc = DateTimeOffset.MinValue;
            return true;
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return false;
        }
    }

    public CompanionBridgeState? TryReadAndApplyLatest()
    {
        string path = GetBridgeFile(StateFileName);
        if (activeSessionId is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            protectedPaths.AssertCanRead(path);
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length is <= 0 or > 256 * 1024)
            {
                return null;
            }

            CompanionBridgeState? state = JsonSerializer.Deserialize<CompanionBridgeState>(stream, SerializerOptions);
            if (!IsValidEnvelope(state) || state!.TimestampUtc <= lastProcessedTimestampUtc)
            {
                return null;
            }

            WorkspaceDescriptor descriptor = ValidateWorkspace(state);
            lastProcessedTimestampUtc = state.TimestampUtc;
            if (!descriptor.IsEmpty && descriptor.PathExists)
            {
                workspaceHistory.ObserveWorkspace(descriptor);
            }

            return state;
        }
        catch (Exception exception) when (
            exception is JsonException
                or IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
        {
            return null;
        }
    }

    public void RequestOpenCodex() => WriteCommand("openCodex");

    public void RequestConfigureShortcut() => WriteCommand("configureShortcut");

    private bool IsValidEnvelope(CompanionBridgeState? state)
        => state is not null
            && state.ProtocolVersion == ProtocolVersion
            && state.SessionId.Equals(activeSessionId, StringComparison.Ordinal)
            && state.WindowId.Length is > 0 and <= 128
            && state.TimestampUtc <= DateTimeOffset.UtcNow.AddMinutes(1)
            && state.TimestampUtc >= DateTimeOffset.UtcNow.AddDays(-1)
            && state.SidebarFailureCode is null or { Length: <= 128 };

    private WorkspaceDescriptor ValidateWorkspace(CompanionBridgeState state)
    {
        if (state.IsEmpty || state.WorkspaceType == WorkspaceType.Empty)
        {
            if (!state.IsEmpty
                || state.WorkspaceType != WorkspaceType.Empty
                || !string.IsNullOrWhiteSpace(state.WorkspacePath)
                || state.WorkspaceFolders.Count != 0)
            {
                throw new ArgumentException("The empty workspace report is inconsistent.");
            }

            return new WorkspaceDescriptor(
                WorkspaceType.Empty,
                string.Empty,
                null,
                [],
                state.TimestampUtc,
                null,
                false);
        }

        string path = ValidateReportedPath(state.WorkspacePath ?? string.Empty);
        string[] folders = state.WorkspaceFolders
            .Select(ValidateReportedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray();
        bool exists = state.WorkspaceType switch
        {
            WorkspaceType.Folder => Directory.Exists(path),
            WorkspaceType.WorkspaceFile => File.Exists(path)
                && Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase),
            WorkspaceType.MultiRoot => (File.Exists(path)
                    && Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase))
                || folders.Count(Directory.Exists) > 1,
            _ => false,
        };
        if (!exists)
        {
            throw new ArgumentException("The reported workspace path is missing or invalid.");
        }

        string displayName = Path.GetExtension(path).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(path)
            : Path.GetFileName(path);
        return new WorkspaceDescriptor(
            state.WorkspaceType,
            displayName,
            path,
            folders,
            state.TimestampUtc,
            null,
            true);
    }

    private string ValidateReportedPath(string reportedPath)
    {
        if (string.IsNullOrWhiteSpace(reportedPath)
            || reportedPath.IndexOf('\0') >= 0
            || reportedPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Any(static segment => segment == ".."))
        {
            throw new ArgumentException("The bridge path is invalid.", nameof(reportedPath));
        }

        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportedPath));
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The bridge path must be fully qualified.", nameof(reportedPath));
        }

        protectedPaths.AssertCanRead(path);
        return path;
    }

    private void WriteCommand(string action)
    {
        if (activeSessionId is null)
        {
            throw new InvalidOperationException("No managed companion session is active.");
        }

        AtomicJsonFile.Write(
            GetBridgeFile(CommandFileName),
            new CompanionCommand(
                ProtocolVersion,
                activeSessionId,
                Guid.NewGuid().ToString("N"),
                action,
                DateTimeOffset.UtcNow),
            SerializerOptions,
            protectedPaths);
    }

    private void DeleteBridgeFile(string fileName)
    {
        string path = GetBridgeFile(fileName);
        protectedPaths.AssertCanWrite(path);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetBridgeFile(string fileName)
        => Path.Combine(bridgeDirectory, fileName);

    private sealed record CompanionCommand(
        int ProtocolVersion,
        string SessionId,
        string RequestId,
        string Action,
        DateTimeOffset TimestampUtc);
}
