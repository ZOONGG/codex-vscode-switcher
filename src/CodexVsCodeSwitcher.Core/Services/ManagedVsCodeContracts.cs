using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public interface IVsCodeExecutableLocator
{
    string? Locate(string? configuredExecutablePath);
}

public interface IManagedVsCodeRuntime
{
    ManagedVsCodeObservation? Observe(ManagedVsCodeInstanceState state);

    Task<ManagedShutdownResult> RequestCloseAsync(
        ManagedVsCodeObservation instance,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    ManagedProcessIdentity Launch(VsCodeProcessStartSpec startSpec);

    Task<ManagedWindowWaitResult> WaitForWindowAsync(
        ManagedVsCodeInstanceState state,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    void ForceClose(ManagedVsCodeObservation instance);
}

public interface ICodexExtensionManager
{
    CodexExtensionStatus Detect(string extensionsDirectory);

    Task<CodexExtensionStatus> InstallAsync(
        string executablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string sharedDataDirectory,
        CancellationToken cancellationToken);

    void ConfigureDedicatedSettings(string userDataDirectory, bool openOnStartup);
}

public interface IProcessCommandRunner
{
    Task<int> RunAsync(VsCodeProcessStartSpec startSpec, CancellationToken cancellationToken);
}

public interface IManagedInstanceStore
{
    ManagedVsCodeInstanceState? Read();

    void Write(ManagedVsCodeInstanceState state);

    void Clear();
}
