namespace CodexVsCodeSwitcher.Core.Models;

public enum WorkspaceType
{
    Empty,
    Folder,
    WorkspaceFile,
    MultiRoot,
}

public sealed record WorkspaceDescriptor(
    WorkspaceType Type,
    string DisplayName,
    string? Path,
    IReadOnlyList<string> FolderPaths,
    DateTimeOffset LastSuccessfullyObservedAtUtc,
    DateTimeOffset? LastSuccessfullyLaunchedAtUtc,
    bool PathExists)
{
    public bool IsEmpty => Type == WorkspaceType.Empty;
}

public sealed record WorkspaceHistorySnapshot(
    WorkspaceDescriptor? CurrentProject,
    IReadOnlyList<WorkspaceDescriptor> RecentProjects);

public enum SidebarOpenStatus
{
    NotRequested,
    Pending,
    Succeeded,
    Failed,
}

public sealed record CompanionBridgeState(
    int ProtocolVersion,
    string SessionId,
    string WindowId,
    DateTimeOffset TimestampUtc,
    WorkspaceType WorkspaceType,
    string? WorkspacePath,
    IReadOnlyList<string> WorkspaceFolders,
    bool IsEmpty,
    bool CodexExtensionInstalled,
    SidebarOpenStatus SidebarStatus,
    bool ShortcutConflict,
    string? SidebarFailureCode = null);

public sealed record ManagedCompanionLaunchOptions(
    string ExtensionDirectory,
    string BridgeDirectory,
    string SessionId,
    bool OpenCodexAutomatically);
