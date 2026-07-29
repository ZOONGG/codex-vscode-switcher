using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public interface IVsCodeLocator
{
    Task<string?> LocateAsync(CancellationToken cancellationToken);
}

public interface IVsCodeLauncher
{
    Task LaunchAsync(VsCodeLaunchOptions options, CancellationToken cancellationToken);
}

public interface IVsCodeProcessService
{
    Task<IReadOnlyList<int>> FindDedicatedProcessesAsync(CancellationToken cancellationToken);
}

public interface IVsCodeWindowLocator
{
    Task<nint?> FindWindowAsync(int processId, CancellationToken cancellationToken);
}

public interface ICodexProfileHomeService
{
    IReadOnlyList<CodexVsCodeProfile> Discover();
}

public interface IWorkspaceHistoryService
{
    string? ReadLastWorkspace();
    void SaveLastWorkspace(string? workspacePath);
}

public sealed record VsCodeIntegrationStatus(bool IsPrepared, bool IsImplemented, string MessageKey)
{
    public static VsCodeIntegrationStatus Bootstrap { get; } =
        new(true, false, "VsCodeIntegrationPrepared");
}
