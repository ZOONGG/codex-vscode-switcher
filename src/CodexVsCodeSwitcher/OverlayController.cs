using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher;

internal sealed class OverlayController : IDisposable
{
    private readonly CodexVsCodeStorageLayout paths;
    private readonly IProtectedPathPolicy protectedPaths;
    private readonly ProfileManagerService profileManager;
    private readonly ActiveProfileStore activeProfileStore;
    private readonly SettingsService settingsService;
    private readonly IVsCodeExecutableLocator executableLocator;
    private readonly IManagedVsCodeRuntime managedRuntime;
    private readonly IManagedInstanceStore managedInstanceStore;
    private readonly IWorkspaceHistoryService workspaceHistory;
    private readonly CompanionBridgeService companionBridge;
    private readonly ICodexExtensionManager extensionManager;
    private readonly VsCodeLaunchPlanBuilder launchPlanBuilder;
    private readonly ProjectLaunchContinuation projectLaunchContinuation;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly SafeLogger logger;
    private readonly MinimalBackupService backupMaintenance;
    private readonly VsCodeSetupImportService setupImportService;
    private readonly VsCodeProxySettingsService proxySettingsService;
    private readonly CodexExtensionInstallationLocator extensionInstallationLocator;
    private readonly CodexNetworkDiagnosticService networkDiagnosticService;
    private readonly VsCodeEnvironmentComparisonService environmentComparisonService;
    private readonly ProfileStatusService statusService;
    private readonly DispatcherTimer windowTrackingTimer;
    private readonly CancellationTokenSource disposalTokenSource = new();
    private readonly SemaphoreSlim launchWorkflowLock = new(1, 1);
    private readonly OverlayVisibilityLeaseManager visibilityLeases = new();
    private OverlaySettings settings;
    private Localizer localizer;
    private TrayIconService? trayIcon;
    private OverlayWindow? overlayWindow;
    private SettingsWindow? settingsWindow;
    private ProfileManagerWindow? profileManagerWindow;
    private HotkeyManager? hotkeyManager;
    private ManagedVsCodeWindowTracker? managedWindowTracker;
    private IReadOnlyList<ProfileInfo> profiles = [];
    private IntPtr attachedVsCodeWindow;
    private bool overlayRequestedVisible;
    private ManagedVsCodeObservation? managedObservation;
    private CodexExtensionStatus? lastExtensionActionStatus;
    private ActivationDiagnostic? lastActivationDiagnostic;
    private IDisposable? settingsPreviewLease;
    private SidebarOpenStatus lastSidebarStatus;
    private bool shortcutConflictReported;
    private DateTimeOffset? companionSessionStartedAtUtc;
    private bool companionStateWarningShown;

    public OverlayController(
        CodexVsCodeStorageLayout paths,
        IProtectedPathPolicy protectedPaths,
        ProfileManagerService profileManager,
        ActiveProfileStore activeProfileStore,
        SettingsService settingsService,
        IVsCodeExecutableLocator executableLocator,
        IManagedVsCodeRuntime managedRuntime,
        IManagedInstanceStore managedInstanceStore,
        IWorkspaceHistoryService workspaceHistory,
        CompanionBridgeService companionBridge,
        ICodexExtensionManager extensionManager,
        VsCodeLaunchPlanBuilder launchPlanBuilder,
        IStartupRegistrationService startupRegistrationService,
        SafeLogger logger,
        MinimalBackupService backupMaintenance,
        VsCodeSetupImportService setupImportService)
    {
        this.paths = paths;
        this.protectedPaths = protectedPaths;
        this.profileManager = profileManager;
        this.activeProfileStore = activeProfileStore;
        this.settingsService = settingsService;
        this.executableLocator = executableLocator;
        this.managedRuntime = managedRuntime;
        this.managedInstanceStore = managedInstanceStore;
        this.workspaceHistory = workspaceHistory;
        this.companionBridge = companionBridge;
        this.extensionManager = extensionManager;
        this.launchPlanBuilder = launchPlanBuilder;
        projectLaunchContinuation = new ProjectLaunchContinuation(protectedPaths);
        this.startupRegistrationService = startupRegistrationService;
        this.logger = logger;
        this.backupMaintenance = backupMaintenance;
        this.setupImportService = setupImportService;
        proxySettingsService = new VsCodeProxySettingsService(protectedPaths);
        extensionInstallationLocator = new CodexExtensionInstallationLocator(protectedPaths);
        networkDiagnosticService = new CodexNetworkDiagnosticService(new SystemNetworkDiagnosticProbe());
        environmentComparisonService = new VsCodeEnvironmentComparisonService(protectedPaths);
        settings = settingsService.Load();
        localizer = new Localizer(settings.Language);
        App.ApplyTheme(settings.Theme);
        var statusStore = new ProfileStatusStore(paths.ProfileStatusFile, protectedPaths);
        statusService = new ProfileStatusService(statusStore, new UnavailableUsageProvider(), logger);
        statusService.SetStaleThreshold(TimeSpan.FromMinutes(settings.StaleDataThresholdMinutes));
        windowTrackingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        windowTrackingTimer.Tick += (_, _) =>
        {
            managedWindowTracker?.Reconcile();
            SynchronizeCompanionState();
        };
    }

    public void Start()
    {
        EnsureOverlay();
        EnsureTray();
        trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
        RefreshProfiles();
        ApplySettings();
        overlayRequestedVisible = settings.ShowOverlayOnStart;
        managedWindowTracker = new ManagedVsCodeWindowTracker(
            Application.Current.Dispatcher,
            ObserveManagedInstanceSafely)
        {
            OverlayHandle = overlayWindow!.Handle,
            ShowOnlyWithManagedVsCode = settings.ShowOverlayOnlyWithManagedVsCode,
            ManualVisibilityRequested = overlayRequestedVisible,
            ExplicitVisibilityReasons = visibilityLeases.ActiveReasons,
        };
        managedWindowTracker.StateChanged += OnManagedWindowStateChanged;
        managedWindowTracker.Start();
        windowTrackingTimer.Start();
        ManagedVsCodeInstanceState? startupRuntimeState = managedInstanceStore.Read();
        ManagedVsCodeObservation? startupObservation = ObserveManagedInstanceSafely();
        if (startupRuntimeState is not null && startupObservation is null)
        {
            logger.Info("A stale managed launch state was discarded during startup.");
            overlayWindow?.ShowError(localizer["StaleLaunchStateReset"]);
        }

        if (startupObservation is not null && companionBridge.TryResumeSession())
        {
            SynchronizeCompanionState();
        }
        if (settingsService.LastLoadWarningKey is string warningKey)
        {
            overlayWindow?.ShowError(localizer[warningKey]);
        }

        logger.Info("Switcher shell started with isolated managed VS Code activation enabled.");
    }

    public void Dispose()
    {
        disposalTokenSource.Cancel();
        settingsPreviewLease?.Dispose();
        settingsPreviewLease = null;
        windowTrackingTimer.Stop();
        managedWindowTracker?.Dispose();
        statusService.Dispose();
        hotkeyManager?.Dispose();
        trayIcon?.Dispose();
        settingsWindow?.Close();
        profileManagerWindow?.Close();
        overlayWindow?.Close();
        disposalTokenSource.Dispose();
    }

    public void RevealFromSecondInstance()
        => Application.Current.Dispatcher.Invoke(ShowOverlay);

    public void LaunchLastProfileOrChoose()
        => Application.Current.Dispatcher.BeginInvoke(LaunchManagedVsCode);

    private void EnsureOverlay()
    {
        if (overlayWindow is not null)
        {
            return;
        }

        overlayWindow = new OverlayWindow(settings, logger)
        {
            Topmost = false,
            OnSwitchProfile = profile => _ = SwitchProfileAsync(profile, restartIfActive: false),
            OnRefreshProfiles = RefreshProfiles,
            OnOpenProfilesFolder = () => OpenFolder(paths.ProfilesDirectory),
            OnOpenApplicationDataFolder = () => OpenFolder(paths.ApplicationDataDirectory),
            OnOpenSettings = ShowSettingsWindow,
            OnManageProfiles = ShowProfileManager,
            OnAddProfile = ShowProfileManager,
            OnHideOverlay = HideOverlay,
            OnExit = () => Application.Current.Shutdown(),
            OnSettingsChanged = SaveSettings,
            Localizer = localizer,
        };

        hotkeyManager = new HotkeyManager(overlayWindow.Handle);
        hotkeyManager.ToggleOverlayRequested += ToggleOverlay;
        hotkeyManager.ProfileHotkeyRequested += index =>
        {
            if (index >= 0 && index < profiles.Count && profiles[index].IsEligibleForSwitching)
            {
                _ = SwitchProfileAsync(profiles[index].Name, restartIfActive: false);
            }
        };
    }

    private void EnsureTray()
    {
        if (trayIcon is not null)
        {
            return;
        }

        trayIcon = new TrayIconService(localizer);
        trayIcon.ToggleOverlayRequested += ToggleOverlay;
        trayIcon.OpenCodexRequested += OpenCodexNow;
        trayIcon.SettingsRequested += ShowSettingsWindow;
        trayIcon.ProfileSelected += profile => _ = SwitchProfileAsync(profile, restartIfActive: false);
        trayIcon.ProjectSelected += SelectRecentProject;
        trayIcon.RemoveRecentProjectRequested += RemoveRecentProject;
        trayIcon.ClearRecentProjectsRequested += ClearRecentProjects;
        trayIcon.LaunchManagedVsCodeRequested += LaunchManagedVsCode;
        trayIcon.RestartManagedVsCodeRequested += RestartManagedVsCode;
        trayIcon.InstallCodexExtensionRequested += () => _ = InstallCodexExtensionAsync();
        trayIcon.StartWithWindowsChanged += enabled =>
        {
            settings.StartWithWindows = enabled;
            SaveSettings(settings);
        };
        trayIcon.ExitRequested += () => Application.Current.Shutdown();
    }

    private async Task SwitchProfileAsync(string profile, bool restartIfActive)
    {
        if (!launchWorkflowLock.Wait(0))
        {
            ShowIntegrationError("ProfileSwitchBusy");
            return;
        }

        ProfileInfo? selected = profiles.FirstOrDefault(
            item => item.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
        string displayName = selected?.DisplayName ?? profile;
        IDisposable switchingVisibility =
            visibilityLeases.Acquire(OverlayVisibilityReason.ProfileSwitchingStatus);
        ReconcileOverlayVisibility();
        overlayWindow?.SetSwitching(true, profile, displayName);
        string? workspace = null;
        string activationStage = "profile-preflight";
        bool companionUnavailable = false;
        try
        {
            if (!await EnsureCodexExtensionForLaunchAsync(displayName).ConfigureAwait(true))
            {
                return;
            }

            SynchronizeCompanionState();
            workspace = settings.ReopenLastWorkspaceAfterSwitch
                ? FirstNonEmpty(workspaceHistory.ReadLastWorkspace(), settings.LastOpenedWorkspace)
                : null;
            activationStage = "companion-startup";
            ManagedCompanionLaunchOptions? companion;
            if (companionBridge.TryBeginSession(
                settings.LaunchCodexSidebarOnStartup,
                out companion,
                out Exception? companionFailure))
            {
                CodexExtensionInstallationInfo? officialCodex =
                    extensionInstallationLocator.Locate(SelectedExtensionsDirectory);
                if (officialCodex is null)
                {
                    ShowIntegrationError(SelectedExtensionMode == VsCodeExtensionMode.Shared
                        ? "SharedCodexExtensionMissing"
                        : "CodexExtensionMissing");
                    return;
                }

                companion = companion! with
                {
                    OfficialCodexExtensionDirectory = officialCodex.ExtensionPath,
                };
                companionSessionStartedAtUtc = DateTimeOffset.UtcNow;
                companionStateWarningShown = false;
            }
            else
            {
                companionUnavailable = true;
                companionSessionStartedAtUtc = null;
                companionStateWarningShown = true;
                logger.Info(
                    $"Bundled companion startup is unavailable; managed VS Code launch will continue. " +
                    $"{companionFailure!.GetType().Name}: {DiagnosticTextSanitizer.Sanitize(companionFailure.Message)}");
            }

            activationStage = "managed-vscode-activation";
            ProfileActivationResult result = await CreateActivationService()
                .ActivateAsync(
                    profile,
                    settings.CustomVsCodeExecutablePath,
                    settings.DedicatedVsCodeUserDataDirectory,
                    SelectedExtensionsDirectory,
                    settings.DedicatedVsCodeSharedDataDirectory,
                    workspace,
                    TimeSpan.FromSeconds(settings.GracefulCloseTimeoutSeconds),
                    restartIfActive,
                    requireExtension: true,
                    openCodexOnStartup: settings.LaunchCodexSidebarOnStartup,
                    extensionMode: SelectedExtensionMode,
                    customCaVariable: settings.CustomCaEnvironmentVariable,
                    customCaCertificatePath: settings.CustomCaCertificatePath,
                    progress: new Progress<string>(
                        key => overlayWindow?.SetSwitchingStatus(localizer[key])),
                    cancellationToken: disposalTokenSource.Token,
                    companion: companion)
                .ConfigureAwait(true);
            string message = localizer[result.MessageKey];
            if (result.Status is ProfileActivationStatus.Succeeded or ProfileActivationStatus.AlreadyActive)
            {
                overlayWindow?.ShowNotification(message);
                logger.Info($"Managed VS Code activated for profile '{profile}'.");
                RefreshProfiles();
                managedWindowTracker?.Reconcile();
                settingsWindow?.RefreshIntegrationPage();
                SynchronizeCompanionState();
                if (companionUnavailable)
                {
                    ShowIntegrationNotice("ManagedVsCodeLaunchedCompanionUnavailable");
                }
            }
            else
            {
                lastActivationDiagnostic = CreateActivationDiagnostic(
                    result,
                    profile,
                    workspace);
                overlayWindow?.SetProfileActivationStatus(profile, result.MessageKey);
                string failureMessage = BuildActivationFailureMessage(result, message);
                overlayWindow?.ShowError(failureMessage);
                trayIcon?.ShowBalloon("Codex VS Code Switcher", failureMessage);
                logger.Info($"Managed VS Code activation for profile '{profile}' ended with {result.Status}.");
                settingsWindow?.RefreshIntegrationPage();
                QueueLaunchRecovery(profile, failureMessage);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logger.Error("Managed VS Code profile activation failed unexpectedly.", exception);
            lastActivationDiagnostic = new ActivationDiagnostic(
                ActivationFailureCategory.Unexpected,
                DateTimeOffset.UtcNow,
                GetAppVersion(),
                executableLocator.Locate(settings.CustomVsCodeExecutablePath)
                    ?? settings.CustomVsCodeExecutablePath,
                settings.DedicatedVsCodeUserDataDirectory,
                SelectedExtensionsDirectory,
                settings.DedicatedVsCodeSharedDataDirectory,
                profile,
                workspace,
                [],
                activationStage,
                exception.GetType().Name,
                DiagnosticTextSanitizer.Sanitize(exception.Message));
            ShowIntegrationError("ProjectLaunchInterrupted");
            QueueLaunchRecovery(profile, localizer["ProjectLaunchInterrupted"]);
        }
        finally
        {
            overlayWindow?.SetSwitching(false, null, null);
            launchWorkflowLock.Release();
            switchingVisibility.Dispose();
            ReconcileOverlayVisibility();
        }
    }

    private void LaunchManagedVsCode()
        => _ = LaunchManagedVsCodeAsync();

    private async Task LaunchManagedVsCodeAsync()
    {
        string? active = activeProfileStore.Read();
        if (string.IsNullOrWhiteSpace(active))
        {
            IReadOnlyList<ProfileInfo> candidates = profiles
                .Where(static profile => profile.IsEligibleForSwitching)
                .ToArray();
            if (candidates.Count == 0)
            {
                ShowIntegrationError("NoReadyProfiles");
                return;
            }

            Window? owner = settingsWindow?.IsVisible == true ? settingsWindow : null;
            IntPtr nativeOwner = owner is null && overlayWindow?.IsVisible == true
                ? overlayWindow.Handle
                : IntPtr.Zero;
            active = ProfileSelectionDialog.Show(owner, nativeOwner, candidates, localizer);
            if (string.IsNullOrWhiteSpace(active))
            {
                return;
            }
        }

        PendingProjectActivation pending = projectLaunchContinuation.Begin(active);
        try
        {
            string? rememberedProject = FirstNonEmpty(
                workspaceHistory.ReadLastWorkspace(),
                settings.LastOpenedWorkspace);
            if (!string.IsNullOrWhiteSpace(rememberedProject)
                && !Directory.Exists(rememberedProject)
                && !(File.Exists(rememberedProject)
                    && Path.GetExtension(rememberedProject).Equals(
                        ".code-workspace",
                        StringComparison.OrdinalIgnoreCase)))
            {
                if (!ResolveMissingProject(rememberedProject))
                {
                    return;
                }

                rememberedProject = FirstNonEmpty(
                    workspaceHistory.ReadLastWorkspace(),
                    settings.LastOpenedWorkspace);
            }

            ResolvedProjectActivation selection;
            if (string.IsNullOrWhiteSpace(rememberedProject))
            {
                Window? owner = settingsWindow?.IsVisible == true ? settingsWindow : null;
                IntPtr nativeOwner = owner is null && overlayWindow?.IsVisible == true
                    ? overlayWindow.Handle
                    : IntPtr.Zero;
                FirstLaunchProjectAction projectAction = FirstLaunchProjectDialog.Show(
                    owner,
                    nativeOwner,
                    hasLastProject: false,
                    localizer);
                switch (projectAction)
                {
                    case FirstLaunchProjectAction.ChooseFolder
                        when TryChooseWorkspaceFolder(out string? folder):
                        selection = projectLaunchContinuation.SelectFolder(pending, folder!);
                        break;
                    case FirstLaunchProjectAction.ChooseWorkspaceFile
                        when TryChooseWorkspaceFile(out string? workspaceFile):
                        selection = projectLaunchContinuation.SelectWorkspaceFile(pending, workspaceFile!);
                        break;
                    case FirstLaunchProjectAction.OpenEmpty:
                        selection = projectLaunchContinuation.OpenWithoutProject(pending);
                        break;
                    default:
                        return;
                }
            }
            else if (Directory.Exists(rememberedProject))
            {
                selection = projectLaunchContinuation.SelectFolder(pending, rememberedProject);
            }
            else
            {
                selection = projectLaunchContinuation.SelectWorkspaceFile(pending, rememberedProject);
            }

            await projectLaunchContinuation.ResumeAsync(
                selection,
                PersistProjectSelection,
                selectedProfile => SwitchProfileAsync(selectedProfile, restartIfActive: false));
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            logger.Error("The selected project could not continue the pending activation.", exception);
            lastActivationDiagnostic = new ActivationDiagnostic(
                ActivationFailureCategory.Unexpected,
                DateTimeOffset.UtcNow,
                GetAppVersion(),
                executableLocator.Locate(settings.CustomVsCodeExecutablePath)
                    ?? settings.CustomVsCodeExecutablePath,
                settings.DedicatedVsCodeUserDataDirectory,
                SelectedExtensionsDirectory,
                settings.DedicatedVsCodeSharedDataDirectory,
                pending.ProfileId,
                workspaceHistory.ReadLastWorkspace(),
                [],
                "project-selection-continuation",
                exception.GetType().Name,
                DiagnosticTextSanitizer.Sanitize(exception.Message));
            ShowIntegrationError("ProjectLaunchInterrupted");
            QueueLaunchRecovery(pending.ProfileId, localizer["ProjectLaunchInterrupted"]);
        }
    }

    private void RestartManagedVsCode()
    {
        string? active = activeProfileStore.Read();
        if (string.IsNullOrWhiteSpace(active))
        {
            ShowIntegrationError("SelectProfileFirst");
            return;
        }

        _ = SwitchProfileAsync(active, restartIfActive: true);
    }

    private void OpenCodexNow()
    {
        ManagedVsCodeInstanceState? state = managedInstanceStore.Read();
        if (state is null || managedRuntime.Observe(state) is null)
        {
            LaunchManagedVsCode();
            return;
        }

        try
        {
            companionBridge.RequestOpenCodex();
            ShowIntegrationNotice("CodexStillLoading");
        }
        catch (InvalidOperationException)
        {
            ShowIntegrationError("CodexSidebarUnavailable");
        }
    }

    private void ConfigureCodexShortcut()
    {
        try
        {
            companionBridge.RequestConfigureShortcut();
        }
        catch (InvalidOperationException)
        {
            ShowIntegrationError("ManagedVsCodeNotRunning");
        }
    }

    private void SynchronizeCompanionState()
    {
        CompanionBridgeState? bridgeState = companionBridge.TryReadAndApplyLatest();
        if (bridgeState is null)
        {
            if (!companionStateWarningShown
                && companionSessionStartedAtUtc is DateTimeOffset started
                && DateTimeOffset.UtcNow - started >= TimeSpan.FromSeconds(25)
                && managedObservation?.Window is not null)
            {
                companionStateWarningShown = true;
                ShowIntegrationNotice("CompanionStateUnavailableWarning");
            }

            return;
        }

        companionSessionStartedAtUtc = null;

        string? current = workspaceHistory.ReadLastWorkspace();
        if (!string.IsNullOrWhiteSpace(current)
            && !current.Equals(settings.LastOpenedWorkspace, StringComparison.OrdinalIgnoreCase))
        {
            settings.LastOpenedWorkspace = current;
            SaveSettings(settings);
            trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
            settingsWindow?.RefreshIntegrationPage();
        }

        if (bridgeState.ShortcutConflict && !shortcutConflictReported)
        {
            shortcutConflictReported = true;
            ShowIntegrationNotice("CodexShortcutConflict");
        }

        if (bridgeState.SidebarStatus == lastSidebarStatus)
        {
            return;
        }

        lastSidebarStatus = bridgeState.SidebarStatus;
        if (bridgeState.SidebarStatus == SidebarOpenStatus.Succeeded)
        {
            ShowIntegrationNotice("CodexReady");
        }
        else if (bridgeState.SidebarStatus == SidebarOpenStatus.Failed)
        {
            ShowIntegrationNotice("CodexSidebarOpenFailed");
        }
    }

    private async Task InstallCodexExtensionAsync()
        => _ = await InstallCodexExtensionCoreAsync().ConfigureAwait(true);

    private async Task<CodexExtensionStatus> InstallCodexExtensionCoreAsync()
    {
        if (SelectedExtensionMode == VsCodeExtensionMode.Shared)
        {
            ShowIntegrationNotice("SharedExtensionsInstallDisabled");
            return new CodexExtensionStatus(
                CodexExtensionState.Missing,
                DetailKey: "SharedExtensionsInstallDisabled");
        }

        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        if (executable is null)
        {
            ShowIntegrationError("VsCodeExecutableMissing");
            return new CodexExtensionStatus(
                CodexExtensionState.VsCodeCliUnavailable,
                DetailKey: "VsCodeExecutableMissing");
        }

        CodexExtensionStatus result = await extensionManager.InstallAsync(
            executable,
            settings.DedicatedVsCodeUserDataDirectory,
            settings.DedicatedVsCodeExtensionsDirectory,
            settings.DedicatedVsCodeSharedDataDirectory,
            disposalTokenSource.Token).ConfigureAwait(true);
        lastExtensionActionStatus = result;
        string key = result.State == CodexExtensionState.Installed
            ? "CodexExtensionInstalled"
            : result.DetailKey ?? "CodexExtensionInstallFailed";
        if (result.State == CodexExtensionState.Installed)
        {
            overlayWindow?.ShowNotification(localizer[key]);
        }
        else
        {
            lastActivationDiagnostic = new ActivationDiagnostic(
                ActivationFailureCategory.ExtensionInstallationFailed,
                DateTimeOffset.UtcNow,
                GetAppVersion(),
                executable,
                settings.DedicatedVsCodeUserDataDirectory,
                settings.DedicatedVsCodeExtensionsDirectory,
                settings.DedicatedVsCodeSharedDataDirectory,
                activeProfileStore.Read() ?? "none",
                settings.LastOpenedWorkspace,
                [],
                "extension-installation",
                null,
                result.DetailKey);
            ShowIntegrationError(key);
        }

        settingsWindow?.RefreshIntegrationPage();
        return result;
    }

    private async Task<bool> EnsureCodexExtensionForLaunchAsync(string profileDisplayName)
    {
        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        if (executable is null)
        {
            ShowIntegrationError("VsCodeExecutableMissing");
            return false;
        }

        CodexExtensionStatus current = extensionManager.Detect(SelectedExtensionsDirectory);
        if (current.State == CodexExtensionState.Installed)
        {
            return true;
        }

        Window? owner = settingsWindow?.IsVisible == true ? settingsWindow : null;
        IntPtr nativeOwner = owner is null && overlayWindow?.IsVisible == true
            ? overlayWindow.Handle
            : IntPtr.Zero;
        if (SelectedExtensionMode == VsCodeExtensionMode.Shared)
        {
            ShowIntegrationError("SharedCodexExtensionMissing");
            return false;
        }

        bool approved = ConfirmDialog.Show(
            owner,
            nativeOwner,
            localizer["CodexExtensionRequiredTitle"],
            localizer.Format("CodexExtensionRequiredForLaunch", profileDisplayName),
            localizer["InstallAndContinue"],
            localizer["Cancel"]);
        if (!approved)
        {
            logger.Info("Managed VS Code launch was canceled before Codex extension installation.");
            return false;
        }

        overlayWindow?.SetSwitchingStatus(localizer["InstallingCodexExtension"]);
        CodexExtensionStatus installed = await InstallCodexExtensionCoreAsync().ConfigureAwait(true);
        return installed.State == CodexExtensionState.Installed;
    }

    private void RefreshProfiles()
    {
        try
        {
            profileManager.EnsureMetadata();
            profiles = profileManager.ListProfiles();
            string? activeProfile = activeProfileStore.Read();
            overlayWindow?.SetProfiles(profiles, activeProfile);
            trayIcon?.UpdateProfiles(profiles, activeProfile);
            settingsWindow?.UpdateProfiles(profiles);
            profileManagerWindow?.UpdateProfiles(profiles, activeProfile);
            RefreshStatusIndicators();
        }
        catch (Exception exception)
        {
            logger.Error("Could not refresh isolated VS Code profiles.", exception);
            overlayWindow?.ShowError(localizer["CouldNotRefreshProfiles"]);
        }
    }

    private void RefreshStatusIndicators()
    {
        ProfileStatusDocument document = statusService.Load();
        string? recommended = statusService.FindRecommendedProfile(profiles.Select(static profile => profile.Name).ToArray(), document);
        overlayWindow?.SetStatusDocument(document, recommended);
    }

    private async Task RefreshUsageForProfileAsync(string profileName)
    {
        ProfileInfo? profile = profiles.FirstOrDefault(item => item.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase));
        if (profile is null || !profile.IsEligibleForSwitching)
        {
            return;
        }

        ProfileStatusDocument document = statusService.Load();
        await statusService.RefreshUsageAsync(
            profile.Name,
            profile.DirectoryPath,
            document,
            disposalTokenSource.Token).ConfigureAwait(true);
        RefreshStatusIndicators();
    }

    private void ShowSettingsWindow()
    {
        if (settingsWindow is { IsVisible: true })
        {
            BringToFront(settingsWindow);
            return;
        }

        settingsPreviewLease?.Dispose();
        settingsPreviewLease = visibilityLeases.Acquire(OverlayVisibilityReason.SettingsPreview);
        ReconcileOverlayVisibility();
        settingsWindow = new SettingsWindow(
            settings,
            profiles,
            localizer,
            statusService,
            backupMaintenance,
            paths.SettingsFile,
            protectedPaths,
            SaveSettings,
            RefreshStatusIndicators,
            RefreshUsageForProfileAsync,
            ShowProfileManager,
            ShowProfileManager,
            () => OpenFolder(settings.CodexProfileRoot),
            () => OpenFolder(paths.RemovedProfilesDirectory),
            () => OpenFolder(paths.ApplicationDataDirectory),
            () => OpenFolder(paths.BackupDirectory),
            () => OpenFolder(paths.LogDirectory),
            ResetPosition,
            ResetSettings,
            LaunchManagedVsCode,
            RestartManagedVsCode,
            () => _ = InstallCodexExtensionAsync(),
            CopySafeProxySettings,
            SelectWorkspaceFolder,
            SelectWorkspaceFile,
            ClearWorkspaceForNextLaunch,
            OpenCodexNow,
            ConfigureCodexShortcut,
            workspaceHistory.ReadSnapshot,
            SelectRecentProject,
            RemoveRecentProject,
            ClearRecentProjects,
            GetIntegrationSnapshot,
            () => OpenFolder(settings.DedicatedVsCodeUserDataDirectory),
            () => _ = ImportVsCodeSetupAsync(),
            CreateCodexVsCodeShortcut,
            CopyLastDiagnostics,
            () => _ = RunNetworkDiagnosticsAsync(),
            OpenCodexLogs,
            ResetNetworkingCache,
            CompareVsCodeEnvironments,
            RestartCodexExtensionHost,
            ResetManagedRuntimeState,
            () => Application.Current.Shutdown());
        settingsWindow.Closed += (_, _) =>
        {
            settingsWindow = null;
            settingsPreviewLease?.Dispose();
            settingsPreviewLease = null;
            ReconcileOverlayVisibility();
        };
        settingsWindow.Show();
        BringToFront(settingsWindow);
        settingsWindow.SetConflicts(RegisterHotkeys());
    }

    private void ShowProfileManager()
    {
        if (profileManagerWindow is { IsVisible: true })
        {
            BringToFront(profileManagerWindow);
            return;
        }

        profileManagerWindow = new ProfileManagerWindow(
            profiles,
            activeProfileStore.Read(),
            localizer,
            ShowProfileManager,
            RenameDisplayName,
            RemoveProfile,
            ReorderProfiles,
            profile => OpenFolder(profile.DirectoryPath),
            RefreshProfiles);
        profileManagerWindow.Closed += (_, _) => profileManagerWindow = null;
        profileManagerWindow.Show();
        BringToFront(profileManagerWindow);
    }

    private void RenameDisplayName(ProfileInfo profile)
    {
        string? name = PromptDialog.Show(
            profileManagerWindow,
            IntPtr.Zero,
            localizer["RenameDisplayName"],
            localizer["DisplayName"],
            profile.DisplayName,
            localizer["Save"],
            localizer["Cancel"]);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        profileManager.RenameDisplayName(profile.Name, name);
        RefreshProfiles();
    }

    private void RemoveProfile(ProfileInfo profile)
    {
        if (!ConfirmDialog.Show(
            profileManagerWindow,
            IntPtr.Zero,
            localizer["Remove"],
            localizer["RemoveProfilePrompt"],
            localizer["Remove"],
            localizer["Cancel"],
            danger: true))
        {
            return;
        }

        profileManager.RemoveProfile(profile.Name, activeProfileStore.Read());
        RefreshProfiles();
    }

    private void ReorderProfiles(IReadOnlyList<string> orderedNames)
    {
        profileManager.Reorder(orderedNames);
        RefreshProfiles();
    }

    private void ToggleOverlay()
    {
        if (overlayWindow?.IsVisible == true)
        {
            HideOverlay();
        }
        else
        {
            ShowOverlay();
        }
    }

    private void ShowOverlay()
    {
        EnsureOverlay();
        overlayRequestedVisible = true;
        if (managedWindowTracker is not null)
        {
            managedWindowTracker.ManualVisibilityRequested = true;
            managedWindowTracker.Reconcile();
        }
        else if (!settings.ShowOverlayOnlyWithManagedVsCode)
        {
            overlayWindow?.ShowFloating();
        }

        string? guidanceKey = ManualOverlayRevealGuidance.ResolveMessageKey(
            settings.ShowOverlayOnlyWithManagedVsCode,
            overlayWindow?.IsVisible == true,
            managedObservation?.Window is not null,
            !string.IsNullOrWhiteSpace(activeProfileStore.Read()));
        if (guidanceKey is not null)
        {
            if (managedObservation?.Window is null)
            {
                logger.Info("Manual overlay reveal is starting the managed VS Code onboarding flow.");
                LaunchManagedVsCode();
            }
            else
            {
                ShowIntegrationNotice(guidanceKey);
                logger.Info("Manual overlay reveal was blocked until managed VS Code is focused.");
            }
        }
    }

    private void HideOverlay()
    {
        overlayRequestedVisible = false;
        if (managedWindowTracker is not null)
        {
            managedWindowTracker.ManualVisibilityRequested = false;
        }
        overlayWindow?.Hide();
        trayIcon?.UpdateOverlayState(false);
    }

    private void ApplySettings()
    {
        App.ApplyTheme(settings.Theme);
        localizer.SetLanguage(settings.Language);
        overlayWindow?.ApplySettings();
        if (!settings.AttachOverlayToVsCode || managedObservation?.Window is null)
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow?.Detach();
        }

        if (managedWindowTracker is not null)
        {
            managedWindowTracker.ShowOnlyWithManagedVsCode = settings.ShowOverlayOnlyWithManagedVsCode;
            managedWindowTracker.ManualVisibilityRequested = overlayRequestedVisible;
            managedWindowTracker.ExplicitVisibilityReasons = visibilityLeases.ActiveReasons;
            managedWindowTracker.Reconcile();
        }
        settingsWindow?.RefreshTheme();
        profileManagerWindow?.RefreshTheme();
        trayIcon?.UpdateStartWithWindows(settings.StartWithWindows);
        ApplyStartupSetting();
        _ = RegisterHotkeys();
    }

    private void SaveSettings(OverlaySettings updatedSettings)
    {
        try
        {
            settings = updatedSettings;
            if (!string.IsNullOrWhiteSpace(settings.LastOpenedWorkspace)
                && (Directory.Exists(settings.LastOpenedWorkspace)
                    || (File.Exists(settings.LastOpenedWorkspace)
                        && Path.GetExtension(settings.LastOpenedWorkspace).Equals(
                            ".code-workspace",
                            StringComparison.OrdinalIgnoreCase)))
                && !settings.LastOpenedWorkspace.Equals(
                    workspaceHistory.ReadLastWorkspace(),
                    StringComparison.OrdinalIgnoreCase))
            {
                workspaceHistory.SaveLastWorkspace(settings.LastOpenedWorkspace);
                trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
            }
            settingsService.Save(settings);
            statusService.SetStaleThreshold(TimeSpan.FromMinutes(settings.StaleDataThresholdMinutes));
            ApplySettings();
            RefreshStatusIndicators();
        }
        catch (Exception exception)
        {
            logger.Error("Could not save settings.", exception);
            overlayWindow?.ShowError(localizer["CouldNotSaveSettings"]);
        }
    }

    private void ApplyStartupSetting()
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            startupRegistrationService.SetEnabled(settings.StartWithWindows, executable);
        }
        catch (Exception exception)
        {
            logger.Error("Could not update Start with Windows.", exception);
            overlayWindow?.ShowError(localizer["StartupCouldNotUpdate"]);
        }
    }

    private IReadOnlyList<string> RegisterHotkeys()
    {
        if (hotkeyManager is null)
        {
            return Array.Empty<string>();
        }

        IReadOnlyList<string> conflicts = hotkeyManager.Register(settings.Hotkeys, profiles.Count);
        if (conflicts.Count > 0)
        {
            logger.Info($"Hotkey registration reported {conflicts.Count} conflict(s).");
        }

        return conflicts;
    }

    private void ResetPosition()
    {
        EnsureOverlay();
        settings.PositionPreset = PositionPreset.Custom;
        overlayWindow!.ResetFloatingPosition();
        SaveSettings(settings);
        if (settingsPreviewLease is not null)
        {
            ReconcileOverlayVisibility();
        }
        else
        {
            ShowOverlay();
        }
    }

    private void ResetSettings()
    {
        settings = new OverlaySettings();
        settingsService.Save(settings);
        settingsWindow?.Close();
        ApplySettings();
        ShowSettingsWindow();
    }

    private void OnManagedWindowStateChanged(ManagedVsCodeObservation? observation, bool shouldShow)
    {
        EnsureOverlay();
        managedObservation = observation;
        overlayWindow?.SetManagedVsCodeRunning(observation is not null);
        ManagedVsCodeWindow? managedWindow = observation?.Window;
        if (!shouldShow)
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow!.Hide();
            trayIcon?.UpdateOverlayState(false);
            return;
        }

        if (managedWindow is null)
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow!.Detach();
            overlayWindow.ShowFloating();
            trayIcon?.UpdateOverlayState(overlayWindow.IsVisible);
            return;
        }

        if (settings.AttachOverlayToVsCode)
        {
            if (attachedVsCodeWindow != managedWindow.Handle)
            {
                attachedVsCodeWindow = managedWindow.Handle;
                overlayWindow!.AttachTo(managedWindow.Handle);
                logger.Info("Attached switcher overlay to the verified managed Visual Studio Code window.");
            }

            overlayWindow!.UpdatePlacement(managedWindow.Handle);
        }
        else
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow!.ShowFloating();
        }

        trayIcon?.UpdateOverlayState(overlayWindow.IsVisible);
    }

    private void ReconcileOverlayVisibility()
    {
        if (managedWindowTracker is null)
        {
            if ((visibilityLeases.ActiveReasons
                & (OverlayVisibilityReason.SettingsPreview
                    | OverlayVisibilityReason.ProfileSwitchingStatus)) != 0)
            {
                overlayWindow?.ShowFloating();
            }

            return;
        }

        managedWindowTracker.ExplicitVisibilityReasons = visibilityLeases.ActiveReasons;
        managedWindowTracker.Reconcile();
    }

    private ManagedVsCodeObservation? ObserveManagedInstanceSafely()
    {
        try
        {
            ManagedVsCodeInstanceState? state = managedInstanceStore.Read();
            if (state is null)
            {
                return null;
            }

            ManagedVsCodeObservation? observation = managedRuntime.Observe(state);
            if (observation is null)
            {
                managedInstanceStore.Clear();
            }
            else if (observation.State.RootProcessId != state.RootProcessId
                || observation.State.RootProcessStartTimeUtc != state.RootProcessStartTimeUtc)
            {
                managedInstanceStore.Write(observation.State);
                logger.Info("Recovered the verified managed VS Code process after launcher handoff.");
            }

            return observation;
        }
        catch (Exception exception)
        {
            logger.Error("Managed Visual Studio Code window tracking failed.", exception);
            return null;
        }
    }

    private ProfileActivationService CreateActivationService()
        => new(
            settings.CodexProfileRoot,
            protectedPaths,
            executableLocator,
            managedRuntime,
            managedInstanceStore,
            activeProfileStore,
            workspaceHistory,
            extensionManager,
            launchPlanBuilder);

    private ActivationDiagnostic CreateActivationDiagnostic(
        ProfileActivationResult result,
        string profileId,
        string? workspace)
        => new(
            result.FailureCategory,
            DateTimeOffset.UtcNow,
            GetAppVersion(),
            executableLocator.Locate(settings.CustomVsCodeExecutablePath)
                ?? settings.CustomVsCodeExecutablePath,
            settings.DedicatedVsCodeUserDataDirectory,
            SelectedExtensionsDirectory,
            settings.DedicatedVsCodeSharedDataDirectory,
            profileId,
            workspace,
            result.Processes ?? [],
            result.TimeoutStage ?? result.FailureCategory switch
            {
                ActivationFailureCategory.ExecutableNotFound => "executable-validation",
                ActivationFailureCategory.ExecutableCouldNotStart => "process-start",
                ActivationFailureCategory.AccessDenied => "process-start",
                ActivationFailureCategory.ProfileInvalid => "profile-validation",
                ActivationFailureCategory.DedicatedDataDirectoryUnavailable => "dedicated-runtime-preparation",
                ActivationFailureCategory.ExtensionMissing => "extension-validation",
                ActivationFailureCategory.CertificateMissing => "certificate-validation",
                ActivationFailureCategory.WorkspaceMissing => "project-validation",
                ActivationFailureCategory.PreviousVsCodeDidNotClose => "managed-shutdown",
                ActivationFailureCategory.RollbackFailed => "rollback",
                _ => "activation",
            },
            result.ExceptionType,
            result.SanitizedExceptionMessage);

    private string BuildActivationFailureMessage(
        ProfileActivationResult result,
        string fallback)
        => result.FailureCategory switch
        {
            ActivationFailureCategory.ExecutableNotFound => localizer.Format(
                "VsCodeExecutableMissingWithPath",
                settings.CustomVsCodeExecutablePath),
            ActivationFailureCategory.ExecutableCouldNotStart => localizer["VsCodeProcessCouldNotStart"],
            ActivationFailureCategory.WindowDetectionTimeout => localizer["VsCodeWindowDetectionTimeout"],
            ActivationFailureCategory.MatchingProcessFoundWithoutWindow => localizer["ManagedWindowNotFoundProcessStillRunning"],
            ActivationFailureCategory.WorkspaceMissing => localizer.Format(
                "ProjectPathUnavailable",
                result.SanitizedExceptionMessage ?? settings.LastOpenedWorkspace),
            ActivationFailureCategory.Unexpected => localizer["ProjectLaunchInterrupted"],
            _ => fallback,
        };

    private void QueueLaunchRecovery(string profileId, string failureMessage)
        => _ = Application.Current.Dispatcher.BeginInvoke(() =>
            ShowLaunchRecovery(profileId, failureMessage));

    private void ShowLaunchRecovery(string profileId, string failureMessage)
    {
        Window? owner = settingsWindow?.IsVisible == true ? settingsWindow : null;
        IntPtr nativeOwner = owner is null && overlayWindow?.IsVisible == true
            ? overlayWindow.Handle
            : IntPtr.Zero;
        LaunchFailureAction action = LaunchFailureDialog.Show(
            owner,
            nativeOwner,
            failureMessage,
            localizer);
        switch (action)
        {
            case LaunchFailureAction.Retry:
                _ = SwitchProfileAsync(profileId, restartIfActive: false);
                break;
            case LaunchFailureAction.ChooseAnotherProject
                when TryChooseWorkspaceFolder(out string? selectedPath):
                PersistProjectSelection(selectedPath);
                _ = SwitchProfileAsync(profileId, restartIfActive: false);
                break;
            case LaunchFailureAction.OpenWithoutProject:
                PersistProjectSelection(null);
                _ = SwitchProfileAsync(profileId, restartIfActive: false);
                break;
            case LaunchFailureAction.OpenDiagnostics:
                ShowLastActivationDiagnostics();
                break;
            case LaunchFailureAction.ResetLaunchState:
                ResetManagedRuntimeState();
                break;
        }
    }

    private void ShowLastActivationDiagnostics()
    {
        if (lastActivationDiagnostic is null)
        {
            ShowIntegrationNotice("NoDiagnosticsAvailable");
            return;
        }

        var window = new DiagnosticReportWindow(
            localizer["Diagnostics"],
            ActivationDiagnosticsFormatter.Format(lastActivationDiagnostic),
            localizer);
        if (settingsWindow?.IsVisible == true)
        {
            window.Owner = settingsWindow;
        }

        window.ShowDialog();
    }

    private async Task ImportVsCodeSetupAsync()
    {
        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        if (executable is null)
        {
            ShowIntegrationError("VsCodeExecutableMissing");
            return;
        }

        try
        {
            string ordinaryUser = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code",
                "User");
            string ordinaryExtensions = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".vscode",
                "extensions");
            VsCodeSetupImportPlan plan = setupImportService.BuildPlan(
                ordinaryUser,
                ordinaryExtensions);
            if (plan.Files.Count == 0 && plan.ExtensionIds.Count == 0)
            {
                ShowIntegrationNotice("ImportVsCodeSetupNothingFound");
                return;
            }

            string files = plan.Files.Count == 0
                ? localizer["None"]
                : string.Join(
                    Environment.NewLine,
                    plan.Files.Select(file =>
                        $"• {file.DestinationRelativePath} ({file.Length / 1024d:0.#} KB)"));
            string preview = localizer.Format(
                "ImportVsCodeSetupPreview",
                files,
                plan.ExtensionIds.Count);
            bool approved = ConfirmDialog.Show(
                settingsWindow,
                IntPtr.Zero,
                localizer["ImportVsCodeSetup"],
                preview,
                localizer["Import"],
                localizer["Cancel"]);
            if (!approved)
            {
                return;
            }

            VsCodeSetupImportResult result = await setupImportService.ImportAsync(
                plan,
                executable,
                settings.DedicatedVsCodeUserDataDirectory,
                settings.DedicatedVsCodeExtensionsDirectory,
                settings.DedicatedVsCodeSharedDataDirectory,
                plan.ExtensionIds,
                disposalTokenSource.Token).ConfigureAwait(true);
            extensionManager.ConfigureDedicatedSettings(
                settings.DedicatedVsCodeUserDataDirectory,
                settings.LaunchCodexSidebarOnStartup);
            string summary = localizer.Format(
                "ImportVsCodeSetupComplete",
                result.CopiedFiles.Count,
                result.InstalledExtensionIds.Count);
            if (result.ExtensionFailures.Count == 0)
            {
                overlayWindow?.ShowNotification(summary);
            }
            else
            {
                string failures = string.Join(
                    Environment.NewLine,
                    result.ExtensionFailures.Select(failure =>
                        $"• {failure.ExtensionId}: {failure.Reason}"));
                overlayWindow?.ShowError(localizer.Format(
                    "ImportVsCodeSetupPartialFailure",
                    summary,
                    failures));
            }

            settingsWindow?.RefreshIntegrationPage();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            logger.Error("Safe VS Code setup import failed.", exception);
            ShowIntegrationError("ImportVsCodeSetupFailed");
        }
    }

    private void CreateCodexVsCodeShortcut()
    {
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The current executable path is unavailable.");
            _ = new WindowsShortcutService().CreateCodexVsCodeShortcut(executable);
            ShowIntegrationNotice("CodexVsCodeShortcutCreated");
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.Runtime.InteropServices.COMException)
        {
            logger.Error("Could not create the Codex VS Code shortcut.", exception);
            ShowIntegrationError("CodexVsCodeShortcutFailed");
        }
    }

    private void CopySafeProxySettings()
    {
        try
        {
            string ordinarySettings = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code",
                "User",
                "settings.json");
            VsCodeProxySettingsPlan plan = proxySettingsService.BuildPlan(ordinarySettings);
            if (plan.SettingNames.Count == 0)
            {
                ShowIntegrationNotice("NoSafeProxySettingsFound");
                return;
            }

            string names = string.Join(Environment.NewLine, plan.SettingNames.Select(name => $"• {name}"));
            if (!ConfirmDialog.Show(
                settingsWindow,
                IntPtr.Zero,
                localizer["CopySafeProxySettings"],
                localizer.Format("CopySafeProxySettingsPreview", names),
                localizer["Copy"],
                localizer["Cancel"]))
            {
                return;
            }

            proxySettingsService.Apply(settings.DedicatedVsCodeUserDataDirectory, plan);
            ShowIntegrationNotice("SafeProxySettingsCopied");
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            logger.Error("Safe proxy settings copy failed.", exception);
            ShowIntegrationError("SafeProxySettingsCopyFailed");
        }
    }

    private async Task RunNetworkDiagnosticsAsync()
    {
        string temporaryCodexHome = Path.Combine(
            Path.GetTempPath(),
            "CodexVsCodeSwitcher-Network-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temporaryCodexHome);
            CodexExtensionInstallationInfo? extension =
                extensionInstallationLocator.Locate(SelectedExtensionsDirectory);
            IReadOnlyDictionary<string, string> environmentOverrides;
            try
            {
                environmentOverrides = ManagedEnvironmentOverridesBuilder.Build(
                    temporaryCodexHome,
                    settings.CustomCaEnvironmentVariable,
                    settings.CustomCaCertificatePath);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                environmentOverrides = ManagedEnvironmentOverridesBuilder.Build(
                    temporaryCodexHome,
                    CustomCaEnvironmentVariable.None,
                    null);
            }

            var context = new CodexNetworkDiagnosticContext(
                CodexNetworkDiagnosticService.DefaultHttpsEndpoint,
                CodexNetworkDiagnosticService.DefaultWebSocketEndpoint,
                SelectedExtensionMode,
                SelectedExtensionsDirectory,
                extension?.ExtensionPath,
                extension?.BackendExecutablePath,
                settings.CustomCaEnvironmentVariable,
                settings.CustomCaCertificatePath,
                environmentOverrides,
                CollectManagedProcessPaths());
            CodexNetworkDiagnosticReport report = await networkDiagnosticService.RunAsync(
                context,
                disposalTokenSource.Token).ConfigureAwait(true);
            string formatted = CodexNetworkDiagnosticFormatter.Format(report, localizer.Language);
            WriteNetworkDiagnosticCache(formatted);
            var window = new DiagnosticReportWindow(
                localizer["NetworkDiagnosticsTitle"],
                formatted,
                localizer)
            {
                Owner = settingsWindow,
            };
            window.ShowDialog();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            logger.Error("Codex network diagnostics failed.", exception);
            ShowIntegrationError("NetworkDiagnosticsFailed");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryCodexHome)
                    && !Directory.EnumerateFileSystemEntries(temporaryCodexHome).Any())
                {
                    Directory.Delete(temporaryCodexHome, recursive: false);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private void OpenCodexLogs()
    {
        string root = Path.Combine(settings.DedicatedVsCodeUserDataDirectory, "logs");
        protectedPaths.AssertCanRead(root);
        string? log = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "Codex.log", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault()
            : null;
        if (log is null)
        {
            ShowIntegrationNotice("CodexLogsNotFound");
            return;
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { "/select,", Path.GetFullPath(log) },
            UseShellExecute = true,
        });
    }

    private void WriteNetworkDiagnosticCache(string report)
    {
        string cache = NetworkDiagnosticCacheFile;
        protectedPaths.AssertCanWrite(cache);
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        File.WriteAllText(cache, report, new System.Text.UTF8Encoding(false));
    }

    private void ResetNetworkingCache()
    {
        string cache = NetworkDiagnosticCacheFile;
        protectedPaths.AssertCanWrite(cache);
        if (File.Exists(cache))
        {
            File.Delete(cache);
        }

        ShowIntegrationNotice("NetworkingCacheReset");
    }

    private void CompareVsCodeEnvironments()
    {
        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        if (executable is null)
        {
            ShowIntegrationError("VsCodeExecutableMissing");
            return;
        }

        try
        {
            string ordinaryUserData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Code");
            VsCodeEnvironmentComparison comparison = environmentComparisonService.Build(
                executable,
                ordinaryUserData,
                settings.DedicatedVsCodeUserDataDirectory,
                paths.VsCodeSharedExtensionsDirectory,
                SelectedExtensionsDirectory,
                SelectedExtensionMode);
            var window = new DiagnosticReportWindow(
                localizer["CompareVsCodeEnvironments"],
                VsCodeEnvironmentComparisonFormatter.Format(comparison, localizer.Language),
                localizer)
            {
                Owner = settingsWindow,
            };
            window.ShowDialog();
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            logger.Error("Safe VS Code environment comparison failed.", exception);
            ShowIntegrationError("EnvironmentComparisonFailed");
        }
    }

    private void RestartCodexExtensionHost()
    {
        ManagedVsCodeInstanceState? state = managedInstanceStore.Read();
        if (state is null || managedRuntime.Observe(state) is null)
        {
            ShowIntegrationError("ManagedVsCodeNotRunning");
            return;
        }

        ProfileInfo? profile = profiles.FirstOrDefault(item =>
            item.Name.Equals(state.SelectedProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            ShowIntegrationError("ProfileInvalid");
            return;
        }

        try
        {
            VsCodeProcessStartSpec plan = launchPlanBuilder.BuildExtensionHostRestart(
                state.ExecutablePath,
                state.UserDataDirectory,
                state.ExtensionsDirectory,
                state.SharedDataDirectory,
                profile.DirectoryPath,
                state.ExtensionMode,
                settings.CustomCaEnvironmentVariable,
                settings.CustomCaCertificatePath);
            _ = managedRuntime.Launch(plan);
            ShowIntegrationNotice("CodexExtensionHostRestartRequested");
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or Win32Exception)
        {
            logger.Error("Codex extension host restart request failed.", exception);
            ShowIntegrationError("CodexExtensionHostRestartFailed");
        }
    }

    private string NetworkDiagnosticCacheFile
        => Path.Combine(paths.ApplicationDataDirectory, "network-diagnostics-cache.txt");

    private IReadOnlyList<BackendProcessPathDiagnostic> CollectManagedProcessPaths()
    {
        ManagedVsCodeInstanceState? state = managedInstanceStore.Read();
        if (state is null || managedRuntime.Observe(state) is null)
        {
            return [];
        }

        IReadOnlyDictionary<int, int> parents = SnapshotParentProcesses();
        var descendants = new HashSet<int> { state.RootProcessId };
        bool added;
        do
        {
            added = false;
            foreach ((int processId, int parentId) in parents)
            {
                if (descendants.Contains(parentId) && descendants.Add(processId))
                {
                    added = true;
                }
            }
        }
        while (added);

        var result = new List<BackendProcessPathDiagnostic>();
        foreach (int processId in descendants.Order())
        {
            try
            {
                using Process process = Process.GetProcessById(processId);
                string? executable = process.MainModule?.FileName;
                if (executable is null)
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(executable);
                string? extensionDirectory = fullPath.StartsWith(
                    Path.TrimEndingDirectorySeparator(SelectedExtensionsDirectory)
                        + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                    ? SelectedExtensionsDirectory
                    : null;
                result.Add(new BackendProcessPathDiagnostic(
                    process.ProcessName,
                    fullPath,
                    parents.GetValueOrDefault(processId),
                    new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero),
                    extensionDirectory));
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or Win32Exception)
            {
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<int, int> SnapshotParentProcesses()
    {
        var result = new Dictionary<int, int>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
        if (snapshot == NativeMethods.InvalidHandleValue)
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };
            if (!NativeMethods.Process32First(snapshot, ref entry))
            {
                return result;
            }

            do
            {
                result[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (NativeMethods.Process32Next(snapshot, ref entry));
        }
        finally
        {
            _ = NativeMethods.CloseHandle(snapshot);
        }

        return result;
    }

    private void CopyLastDiagnostics()
    {
        if (lastActivationDiagnostic is null)
        {
            ShowIntegrationNotice("NoDiagnosticsAvailable");
            return;
        }

        try
        {
            Clipboard.SetText(ActivationDiagnosticsFormatter.Format(lastActivationDiagnostic));
            ShowIntegrationNotice("DiagnosticsCopied");
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.ExternalException
                or ThreadStateException)
        {
            logger.Error("Could not copy sanitized diagnostics.", exception);
            ShowIntegrationError("DiagnosticsCopyFailed");
        }
    }

    private void ResetManagedRuntimeState()
    {
        ManagedVsCodeInstanceState? state = managedInstanceStore.Read();
        if (state is not null && managedRuntime.Observe(state) is not null)
        {
            ShowIntegrationError("RuntimeStateResetWhileRunning");
            return;
        }

        new ManagedRuntimeStateResetService(managedInstanceStore).Reset();
        managedObservation = null;
        attachedVsCodeWindow = IntPtr.Zero;
        overlayWindow?.SetManagedVsCodeRunning(false);
        managedWindowTracker?.Reconcile();
        settingsWindow?.RefreshIntegrationPage();
        ShowIntegrationNotice("RuntimeStateReset");
    }

    private static string GetAppVersion()
    {
        string? path = Environment.ProcessPath;
        return path is null
            ? "unknown"
            : FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "unknown";
    }

    private void SelectWorkspaceFolder()
    {
        if (TryChooseWorkspaceFolder(out string? selectedPath))
        {
            PersistProjectSelection(selectedPath);
        }
    }

    private bool TryChooseWorkspaceFolder(out string? selectedPath)
    {
        selectedPath = null;
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = localizer["SelectWorkspaceFolder"],
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(settings.LastOpenedWorkspace)
                ? settings.LastOpenedWorkspace
                : string.Empty,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            selectedPath = Path.GetFullPath(dialog.SelectedPath);
            return true;
        }

        return false;
    }

    private void SelectWorkspaceFile()
    {
        if (TryChooseWorkspaceFile(out string? selectedPath))
        {
            PersistProjectSelection(selectedPath);
        }
    }

    private bool TryChooseWorkspaceFile(out string? selectedPath)
    {
        selectedPath = null;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = localizer["SelectWorkspaceFile"],
            Filter = "Visual Studio Code workspace (*.code-workspace)|*.code-workspace",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(settingsWindow) == true)
        {
            selectedPath = Path.GetFullPath(dialog.FileName);
            return true;
        }

        return false;
    }

    private void PersistProjectSelection(string? projectPath)
    {
        string? normalized = string.IsNullOrWhiteSpace(projectPath)
            ? null
            : Path.GetFullPath(projectPath);
        workspaceHistory.SaveLastWorkspace(normalized);
        settings.LastOpenedWorkspace = normalized ?? string.Empty;
        SaveSettings(settings);
        trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
        settingsWindow?.RefreshIntegrationPage();
    }

    private void ClearWorkspaceForNextLaunch()
    {
        SetEmptyWorkspace();
        RestartManagedVsCode();
    }

    private void SetEmptyWorkspace()
    {
        PersistProjectSelection(null);
    }

    private void SelectRecentProject(string projectPath)
    {
        if (!Directory.Exists(projectPath)
            && !(File.Exists(projectPath)
                && Path.GetExtension(projectPath).Equals(".code-workspace", StringComparison.OrdinalIgnoreCase)))
        {
            if (ResolveMissingProject(projectPath))
            {
                RestartManagedVsCode();
            }
            return;
        }

        PersistProjectSelection(projectPath);
        RestartManagedVsCode();
    }

    private void RemoveRecentProject(string projectPath)
    {
        workspaceHistory.RemoveRecent(projectPath);
        trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
        settingsWindow?.RefreshIntegrationPage();
    }

    private void ClearRecentProjects()
    {
        workspaceHistory.ClearRecent();
        trayIcon?.UpdateProjects(workspaceHistory.ReadSnapshot());
        settingsWindow?.RefreshIntegrationPage();
    }

    private bool ResolveMissingProject(string projectPath)
    {
        MissingProjectAction action = MissingProjectDialog.Show(
            settingsWindow,
            overlayWindow?.Handle ?? IntPtr.Zero,
            projectPath,
            localizer);
        switch (action)
        {
            case MissingProjectAction.ChooseAnother:
                SelectWorkspaceFolder();
                return workspaceHistory.ReadLastWorkspace() is string selected
                    && !selected.Equals(projectPath, StringComparison.OrdinalIgnoreCase);
            case MissingProjectAction.OpenEmpty:
                SetEmptyWorkspace();
                return true;
            case MissingProjectAction.RemoveFromRecent:
                RemoveRecentProject(projectPath);
                return false;
            default:
                return false;
        }
    }

    private VsCodeIntegrationSnapshot GetIntegrationSnapshot()
    {
        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        ManagedVsCodeObservation? observation = ObserveManagedInstanceSafely();
        return new VsCodeIntegrationSnapshot(
            executable,
            observation is null ? ManagedInstanceStatus.NotRunning : ManagedInstanceStatus.Running,
            activeProfileStore.Read(),
            lastExtensionActionStatus is { State: not CodexExtensionState.Installed }
                ? lastExtensionActionStatus
                : extensionManager.Detect(SelectedExtensionsDirectory),
            FirstNonEmpty(workspaceHistory.ReadLastWorkspace(), settings.LastOpenedWorkspace),
            SelectedExtensionsDirectory,
            SelectedExtensionMode);
    }

    private void ShowIntegrationError(string messageKey)
    {
        string message = localizer[messageKey];
        overlayWindow?.ShowError(message);
        trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
    }

    private void ShowIntegrationNotice(string messageKey)
    {
        string message = localizer[messageKey];
        overlayWindow?.ShowNotification(message);
        trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
    }

    private static string? FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first)
            ? first
            : !string.IsNullOrWhiteSpace(second)
                ? second
                : null;

    private VsCodeExtensionMode SelectedExtensionMode
        => settings.UseExistingVsCodeExtensions
            ? VsCodeExtensionMode.Shared
            : VsCodeExtensionMode.Isolated;

    private string SelectedExtensionsDirectory
        => SelectedExtensionMode == VsCodeExtensionMode.Shared
            ? paths.VsCodeSharedExtensionsDirectory
            : settings.DedicatedVsCodeExtensionsDirectory;

    private void OpenFolder(string folder)
    {
        string fullPath = Path.GetFullPath(folder);
        protectedPaths.AssertCanWrite(fullPath);
        Directory.CreateDirectory(fullPath);
        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{fullPath}\"",
            UseShellExecute = true,
        });
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
        window.Focus();
        _ = NativeMethods.SetForegroundWindow(new WindowInteropHelper(window).Handle);
    }
}
