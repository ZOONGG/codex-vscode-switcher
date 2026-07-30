using System.Diagnostics;
using System.IO;
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
    private readonly ICodexExtensionManager extensionManager;
    private readonly VsCodeLaunchPlanBuilder launchPlanBuilder;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly SafeLogger logger;
    private readonly MinimalBackupService backupMaintenance;
    private readonly VsCodeSetupImportService setupImportService;
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
        this.extensionManager = extensionManager;
        this.launchPlanBuilder = launchPlanBuilder;
        this.startupRegistrationService = startupRegistrationService;
        this.logger = logger;
        this.backupMaintenance = backupMaintenance;
        this.setupImportService = setupImportService;
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
        windowTrackingTimer.Tick += (_, _) => managedWindowTracker?.Reconcile();
    }

    public void Start()
    {
        EnsureOverlay();
        EnsureTray();
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
        trayIcon.OpenCodexRequested += LaunchManagedVsCode;
        trayIcon.SettingsRequested += ShowSettingsWindow;
        trayIcon.ProfileSelected += profile => _ = SwitchProfileAsync(profile, restartIfActive: false);
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
        try
        {
            if (!await EnsureCodexExtensionForLaunchAsync(displayName).ConfigureAwait(true))
            {
                return;
            }

            string? workspace = settings.ReopenLastWorkspaceAfterSwitch
                ? FirstNonEmpty(settings.LastOpenedWorkspace, workspaceHistory.ReadLastWorkspace())
                : null;
            ProfileActivationResult result = await CreateActivationService()
                .ActivateAsync(
                    profile,
                    settings.CustomVsCodeExecutablePath,
                    settings.DedicatedVsCodeUserDataDirectory,
                    settings.DedicatedVsCodeExtensionsDirectory,
                    settings.DedicatedVsCodeSharedDataDirectory,
                    workspace,
                    TimeSpan.FromSeconds(settings.GracefulCloseTimeoutSeconds),
                    restartIfActive,
                    requireExtension: true,
                    openCodexOnStartup: settings.LaunchCodexSidebarOnStartup,
                    progress: new Progress<string>(
                        key => overlayWindow?.SetSwitchingStatus(localizer[key])),
                    cancellationToken: disposalTokenSource.Token)
                .ConfigureAwait(true);
            string message = localizer[result.MessageKey];
            if (result.Status is ProfileActivationStatus.Succeeded or ProfileActivationStatus.AlreadyActive)
            {
                overlayWindow?.ShowNotification(message);
                logger.Info($"Managed VS Code activated for profile '{profile}'.");
                RefreshProfiles();
                managedWindowTracker?.Reconcile();
                settingsWindow?.RefreshIntegrationPage();
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
                settings.DedicatedVsCodeExtensionsDirectory,
                settings.DedicatedVsCodeSharedDataDirectory,
                profile,
                settings.LastOpenedWorkspace,
                [],
                "activation",
                exception.GetType().Name,
                DiagnosticTextSanitizer.Sanitize(exception.Message));
            ShowIntegrationError("VsCodeLaunchFailed");
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

        _ = SwitchProfileAsync(active, restartIfActive: false);
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

    private async Task InstallCodexExtensionAsync()
        => _ = await InstallCodexExtensionCoreAsync().ConfigureAwait(true);

    private async Task<CodexExtensionStatus> InstallCodexExtensionCoreAsync()
    {
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

        CodexExtensionStatus current = extensionManager.Detect(settings.DedicatedVsCodeExtensionsDirectory);
        if (current.State == CodexExtensionState.Installed)
        {
            return true;
        }

        Window? owner = settingsWindow?.IsVisible == true ? settingsWindow : null;
        IntPtr nativeOwner = owner is null && overlayWindow?.IsVisible == true
            ? overlayWindow.Handle
            : IntPtr.Zero;
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
            SelectWorkspaceFolder,
            SelectWorkspaceFile,
            ClearWorkspaceForNextLaunch,
            GetIntegrationSnapshot,
            () => OpenFolder(settings.DedicatedVsCodeUserDataDirectory),
            () => _ = ImportVsCodeSetupAsync(),
            CreateCodexVsCodeShortcut,
            CopyLastDiagnostics,
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
            settings.DedicatedVsCodeExtensionsDirectory,
            settings.DedicatedVsCodeSharedDataDirectory,
            profileId,
            workspace,
            result.Processes ?? [],
            result.TimeoutStage,
            result.ExceptionType,
            result.SanitizedExceptionMessage);

    private string BuildActivationFailureMessage(
        ProfileActivationResult result,
        string fallback)
        => result.FailureCategory == ActivationFailureCategory.ExecutableNotFound
            ? localizer.Format(
                "VsCodeExecutableMissingWithPath",
                settings.CustomVsCodeExecutablePath)
            : fallback;

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
            settings.LastOpenedWorkspace = Path.GetFullPath(dialog.SelectedPath);
            workspaceHistory.SaveLastWorkspace(settings.LastOpenedWorkspace);
            SaveSettings(settings);
            settingsWindow?.RefreshIntegrationPage();
        }
    }

    private void SelectWorkspaceFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = localizer["SelectWorkspaceFile"],
            Filter = "Visual Studio Code workspace (*.code-workspace)|*.code-workspace",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog(settingsWindow) == true)
        {
            settings.LastOpenedWorkspace = Path.GetFullPath(dialog.FileName);
            workspaceHistory.SaveLastWorkspace(settings.LastOpenedWorkspace);
            SaveSettings(settings);
            settingsWindow?.RefreshIntegrationPage();
        }
    }

    private void ClearWorkspaceForNextLaunch()
    {
        settings.LastOpenedWorkspace = string.Empty;
        workspaceHistory.SaveLastWorkspace(null);
        SaveSettings(settings);
        settingsWindow?.RefreshIntegrationPage();
        RestartManagedVsCode();
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
                : extensionManager.Detect(settings.DedicatedVsCodeExtensionsDirectory),
            FirstNonEmpty(settings.LastOpenedWorkspace, workspaceHistory.ReadLastWorkspace()));
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
