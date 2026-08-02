namespace CodexVsCodeSwitcher.Core.Models;

public sealed record VsCodeProcessStartSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentOverrides);

public enum VsCodeExtensionMode
{
    Isolated,
    Shared,
}

public sealed record ManagedProcessIdentity(
    int ProcessId,
    DateTimeOffset StartTimeUtc);

public sealed record ProcessIdentityEvidence(
    int ProcessId,
    DateTimeOffset StartTimeUtc,
    string ExecutablePath,
    IReadOnlyList<string> CommandLineArguments);

public sealed record ManagedVsCodeWindow(
    nint Handle,
    int ProcessId,
    DateTimeOffset ProcessStartTimeUtc,
    bool IsVisible,
    bool IsMinimized);

public sealed record ManagedVsCodeInstanceState(
    int RootProcessId,
    DateTimeOffset RootProcessStartTimeUtc,
    string SelectedProfileId,
    string? WorkspacePath,
    string ExecutablePath,
    string UserDataDirectory,
    string ExtensionsDirectory,
    string SharedDataDirectory,
    long LastVerifiedWindowHandle,
    DateTimeOffset LaunchTimestampUtc,
    VsCodeExtensionMode ExtensionMode = VsCodeExtensionMode.Isolated);

public sealed record ManagedVsCodeObservation(
    ManagedVsCodeInstanceState State,
    IReadOnlySet<int> VerifiedProcessIds,
    ManagedVsCodeWindow? Window);

public enum ManagedWindowWaitStatus
{
    WindowFound,
    ProcessExited,
    MatchingProcessWithoutWindow,
    WindowDetectionTimeout,
}

public sealed record ManagedWindowWaitResult(
    ManagedWindowWaitStatus Status,
    ManagedVsCodeObservation? Observation,
    TimeSpan Elapsed);

public enum ManagedShutdownStatus
{
    NotRunning,
    Closed,
    Blocked,
    InvalidTarget,
}

public sealed record ManagedShutdownResult(
    ManagedShutdownStatus Status,
    IReadOnlySet<int> VerifiedProcessIds);

public enum ProfileActivationStatus
{
    Succeeded,
    AlreadyActive,
    Busy,
    InvalidProfile,
    ExecutableMissing,
    InvalidDedicatedPath,
    WorkspaceMissing,
    ExtensionMissing,
    ShutdownBlocked,
    LaunchFailed,
    WindowNotFound,
    RolledBack,
    RollbackFailed,
}

public enum ActivationFailureCategory
{
    None,
    ExecutableNotFound,
    ExecutableCouldNotStart,
    AccessDenied,
    ProfileInvalid,
    DedicatedDataDirectoryUnavailable,
    ExtensionMissing,
    ExtensionInstallationFailed,
    ProcessExitedImmediately,
    MatchingProcessFoundWithoutWindow,
    WindowDetectionTimeout,
    WorkspaceMissing,
    PreviousVsCodeDidNotClose,
    StaleManagedInstanceState,
    RollbackFailed,
    Canceled,
    Unexpected,
}

public sealed record ManagedProcessDiagnostic(
    int ProcessId,
    DateTimeOffset StartTimeUtc);

public sealed record ProfileActivationResult(
    ProfileActivationStatus Status,
    string MessageKey,
    string? ActiveProfileId = null,
    string? FailureDetailKey = null,
    bool RollbackAttempted = false,
    bool RollbackSucceeded = false,
    bool SafeToRollback = true,
    ActivationFailureCategory FailureCategory = ActivationFailureCategory.None,
    string? TimeoutStage = null,
    string? ExceptionType = null,
    string? SanitizedExceptionMessage = null,
    IReadOnlyList<ManagedProcessDiagnostic>? Processes = null);

public enum CodexExtensionState
{
    Installed,
    Missing,
    InstallationFailed,
    VsCodeCliUnavailable,
}

public sealed record CodexExtensionStatus(
    CodexExtensionState State,
    string? Version = null,
    string? DetailKey = null);

public enum ManagedInstanceStatus
{
    NotRunning,
    Launching,
    Running,
    WaitingForShutdown,
    Switching,
    Failed,
}

public sealed record WorkspaceSelection(string? Path)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Path);
}

public sealed record VsCodeIntegrationSnapshot(
    string? DetectedExecutablePath,
    ManagedInstanceStatus ManagedStatus,
    string? ActiveProfileId,
    CodexExtensionStatus ExtensionStatus,
    string? WorkspacePath,
    string? ExtensionsDirectory = null,
    VsCodeExtensionMode ExtensionMode = VsCodeExtensionMode.Isolated);
