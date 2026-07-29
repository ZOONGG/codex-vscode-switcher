using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class ProfileActivationService
{
    private static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim ApplicationSwitchLock = new(1, 1);
    private readonly string profileRoot;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly ProfileStorageAuditService profileAudit;
    private readonly IVsCodeExecutableLocator executableLocator;
    private readonly IManagedVsCodeRuntime runtime;
    private readonly IManagedInstanceStore instanceStore;
    private readonly ActiveProfileStore activeProfileStore;
    private readonly IWorkspaceHistoryService workspaceHistory;
    private readonly ICodexExtensionManager extensionManager;
    private readonly VsCodeLaunchPlanBuilder launchPlanBuilder;

    public ProfileActivationService(
        string profileRoot,
        IProtectedPathPolicy protectedPaths,
        IVsCodeExecutableLocator executableLocator,
        IManagedVsCodeRuntime runtime,
        IManagedInstanceStore instanceStore,
        ActiveProfileStore activeProfileStore,
        IWorkspaceHistoryService workspaceHistory,
        ICodexExtensionManager extensionManager,
        VsCodeLaunchPlanBuilder launchPlanBuilder)
    {
        this.profileRoot = Path.GetFullPath(profileRoot);
        this.protectedPaths = protectedPaths;
        profileAudit = new ProfileStorageAuditService(this.profileRoot, protectedPaths);
        this.executableLocator = executableLocator;
        this.runtime = runtime;
        this.instanceStore = instanceStore;
        this.activeProfileStore = activeProfileStore;
        this.workspaceHistory = workspaceHistory;
        this.extensionManager = extensionManager;
        this.launchPlanBuilder = launchPlanBuilder;
    }

    public ManagedVsCodeObservation? ObserveCurrent()
    {
        ManagedVsCodeInstanceState? state = instanceStore.Read();
        if (state is null)
        {
            return null;
        }

        ManagedVsCodeObservation? observation = runtime.Observe(state);
        if (observation is null)
        {
            instanceStore.Clear();
        }

        return observation;
    }

    public async Task<ProfileActivationResult> ActivateAsync(
        string profileId,
        string? configuredExecutablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string? workspacePath,
        TimeSpan gracefulCloseTimeout,
        bool restartIfAlreadyActive,
        bool requireExtension,
        bool openCodexOnStartup,
        Action<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await ApplicationSwitchLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return new ProfileActivationResult(ProfileActivationStatus.Busy, "ProfileSwitchBusy");
        }

        try
        {
            return await ActivateLockedAsync(
                profileId,
                configuredExecutablePath,
                userDataDirectory,
                extensionsDirectory,
                workspacePath,
                gracefulCloseTimeout,
                restartIfAlreadyActive,
                requireExtension,
                openCodexOnStartup,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ApplicationSwitchLock.Release();
        }
    }

    public void ForceCloseCurrent()
    {
        ManagedVsCodeObservation? observation = ObserveCurrent();
        if (observation is null || observation.VerifiedProcessIds.Count == 0)
        {
            throw new InvalidOperationException("No verified managed VS Code process is running.");
        }

        runtime.ForceClose(observation);
    }

    private async Task<ProfileActivationResult> ActivateLockedAsync(
        string profileId,
        string? configuredExecutablePath,
        string userDataDirectory,
        string extensionsDirectory,
        string? workspacePath,
        TimeSpan gracefulCloseTimeout,
        bool restartIfAlreadyActive,
        bool requireExtension,
        bool openCodexOnStartup,
        Action<string>? progress,
        CancellationToken cancellationToken)
    {
        ProfileStorageAudit selectedProfile;
        try
        {
            selectedProfile = profileAudit.AuditRequiredProfile(profileId);
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ProfileActivationResult(ProfileActivationStatus.InvalidProfile, "ProfileInvalid");
        }

        if (selectedProfile.Status != ProfileValidationStatus.Valid)
        {
            return new ProfileActivationResult(ProfileActivationStatus.InvalidProfile, "ProfileInvalid");
        }

        string? executable = executableLocator.Locate(configuredExecutablePath);
        if (executable is null)
        {
            return new ProfileActivationResult(ProfileActivationStatus.ExecutableMissing, "VsCodeExecutableMissing");
        }
        try
        {
            protectedPaths.AssertCanRead(executable);
        }
        catch (UnauthorizedAccessException)
        {
            return new ProfileActivationResult(ProfileActivationStatus.ExecutableMissing, "VsCodeExecutableMissing");
        }

        string userData;
        string extensions;
        try
        {
            userData = Path.GetFullPath(userDataDirectory);
            extensions = Path.GetFullPath(extensionsDirectory);
            protectedPaths.AssertCanWrite(userData);
            protectedPaths.AssertCanWrite(extensions);
            Directory.CreateDirectory(userData);
            Directory.CreateDirectory(extensions);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new ProfileActivationResult(ProfileActivationStatus.InvalidDedicatedPath, "DedicatedPathInvalid");
        }

        string? workspace;
        try
        {
            workspace = NormalizeWorkspace(workspacePath);
            if (workspace is not null)
            {
                protectedPaths.AssertCanRead(workspace);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            return new ProfileActivationResult(ProfileActivationStatus.WorkspaceMissing, "WorkspaceMissing");
        }

        if (workspace is not null && !WorkspaceExists(workspace))
        {
            return new ProfileActivationResult(ProfileActivationStatus.WorkspaceMissing, "WorkspaceMissing");
        }

        CodexExtensionStatus extension = extensionManager.Detect(extensions);
        if (requireExtension && extension.State != CodexExtensionState.Installed)
        {
            return new ProfileActivationResult(ProfileActivationStatus.ExtensionMissing, "CodexExtensionMissing");
        }

        ManagedVsCodeInstanceState? previousState = instanceStore.Read();
        ManagedVsCodeObservation? previousInstance =
            previousState is null ? null : runtime.Observe(previousState);
        string? previousActiveProfile = activeProfileStore.Read();
        if (previousInstance is not null
            && !restartIfAlreadyActive
            && previousState!.SelectedProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileActivationResult(
                ProfileActivationStatus.AlreadyActive,
                "ProfileAlreadyActive",
                previousState.SelectedProfileId);
        }

        if (previousInstance is not null)
        {
            progress?.Invoke("WaitingForVsCodeClose");
            ManagedShutdownResult shutdown = await runtime.RequestCloseAsync(
                previousInstance,
                gracefulCloseTimeout,
                cancellationToken).ConfigureAwait(false);
            if (shutdown.Status is not (ManagedShutdownStatus.Closed or ManagedShutdownStatus.NotRunning))
            {
                return new ProfileActivationResult(
                    ProfileActivationStatus.ShutdownBlocked,
                    "VsCodeShutdownBlocked",
                    previousActiveProfile);
            }
        }

        progress?.Invoke("LaunchingManagedVsCode");
        ProfileActivationResult launchResult = await LaunchAndCommitAsync(
            selectedProfile,
            executable,
            userData,
            extensions,
            workspace,
            openCodexOnStartup,
            cancellationToken).ConfigureAwait(false);
        if (launchResult.Status == ProfileActivationStatus.Succeeded
            || previousState is null
            || string.IsNullOrWhiteSpace(previousActiveProfile))
        {
            return launchResult;
        }

        ProfileActivationResult rollback = await TryRollbackAsync(
            previousState,
            previousActiveProfile,
            openCodexOnStartup,
            cancellationToken).ConfigureAwait(false);
        return rollback.Status == ProfileActivationStatus.Succeeded
            ? new ProfileActivationResult(
                ProfileActivationStatus.RolledBack,
                "ProfileSwitchFailedRolledBack",
                previousActiveProfile,
                launchResult.MessageKey,
                RollbackAttempted: true,
                RollbackSucceeded: true)
            : new ProfileActivationResult(
                ProfileActivationStatus.RollbackFailed,
                "ProfileSwitchAndRollbackFailed",
                previousActiveProfile,
                launchResult.MessageKey,
                RollbackAttempted: true,
                RollbackSucceeded: false);
    }

    private async Task<ProfileActivationResult> LaunchAndCommitAsync(
        ProfileStorageAudit profile,
        string executable,
        string userData,
        string extensions,
        string? workspace,
        bool openCodexOnStartup,
        CancellationToken cancellationToken)
    {
        try
        {
            extensionManager.ConfigureDedicatedSettings(userData, openCodexOnStartup);
            VsCodeProcessStartSpec plan = launchPlanBuilder.Build(
                executable,
                userData,
                extensions,
                profile.DirectoryPath,
                workspace);
            ManagedProcessIdentity launched = runtime.Launch(plan);
            var pendingState = new ManagedVsCodeInstanceState(
                launched.ProcessId,
                launched.StartTimeUtc,
                profile.ProfileName,
                workspace,
                executable,
                userData,
                extensions,
                0,
                DateTimeOffset.UtcNow);
            instanceStore.Write(pendingState);
            ManagedVsCodeObservation? observed = await runtime.WaitForWindowAsync(
                pendingState,
                LaunchTimeout,
                cancellationToken).ConfigureAwait(false);
            if (observed?.Window is null)
            {
                ManagedVsCodeObservation? failedInstance = runtime.Observe(pendingState);
                if (failedInstance is not null)
                {
                    _ = await runtime.RequestCloseAsync(
                        failedInstance,
                        TimeSpan.FromSeconds(5),
                        cancellationToken).ConfigureAwait(false);
                }

                instanceStore.Clear();
                return new ProfileActivationResult(ProfileActivationStatus.WindowNotFound, "ManagedWindowNotFound");
            }

            ManagedVsCodeInstanceState committed = observed.State with
            {
                LastVerifiedWindowHandle = observed.Window.Handle,
            };
            instanceStore.Write(committed);
            activeProfileStore.Write(profile.ProfileName);
            workspaceHistory.SaveLastWorkspace(workspace);
            return new ProfileActivationResult(
                ProfileActivationStatus.Succeeded,
                "ActiveInVsCode",
                profile.ProfileName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            instanceStore.Clear();
            return new ProfileActivationResult(ProfileActivationStatus.LaunchFailed, "VsCodeLaunchFailed");
        }
    }

    private async Task<ProfileActivationResult> TryRollbackAsync(
        ManagedVsCodeInstanceState previousState,
        string previousProfileId,
        bool openCodexOnStartup,
        CancellationToken cancellationToken)
    {
        ProfileStorageAudit previousProfile;
        try
        {
            previousProfile = profileAudit.AuditRequiredProfile(previousProfileId);
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new ProfileActivationResult(ProfileActivationStatus.RollbackFailed, "ProfileRollbackFailed");
        }

        if (previousProfile.Status != ProfileValidationStatus.Valid
            || !File.Exists(previousState.ExecutablePath)
            || (previousState.WorkspacePath is not null && !WorkspaceExists(previousState.WorkspacePath)))
        {
            return new ProfileActivationResult(ProfileActivationStatus.RollbackFailed, "ProfileRollbackFailed");
        }

        return await LaunchAndCommitAsync(
            previousProfile,
            previousState.ExecutablePath,
            previousState.UserDataDirectory,
            previousState.ExtensionsDirectory,
            previousState.WorkspacePath,
            openCodexOnStartup,
            cancellationToken).ConfigureAwait(false);
    }

    private static string? NormalizeWorkspace(string? workspacePath)
        => string.IsNullOrWhiteSpace(workspacePath)
            ? null
            : Path.GetFullPath(workspacePath.Trim());

    private static bool WorkspaceExists(string workspacePath)
        => Directory.Exists(workspacePath)
            || (File.Exists(workspacePath)
                && Path.GetExtension(workspacePath).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase));
}
