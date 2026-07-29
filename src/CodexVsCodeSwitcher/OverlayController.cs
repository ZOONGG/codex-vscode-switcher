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
    private readonly ProfileStatusService statusService;
    private readonly DispatcherTimer windowTrackingTimer;
    private readonly CancellationTokenSource disposalTokenSource = new();
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
        MinimalBackupService backupMaintenance)
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
        settings = settingsService.Load();
        localizer = new Localizer(settings.Language);
        App.ApplyTheme(settings.Theme);
        var statusStore = new ProfileStatusStore(paths.ProfileStatusFile, protectedPaths);
        statusService = new ProfileStatusService(statusStore, new CodexCliStatusUsageProvider(), logger);
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
        ProfileInfo? selected = profiles.FirstOrDefault(
            item => item.Name.Equals(profile, StringComparison.OrdinalIgnoreCase));
        string displayName = selected?.DisplayName ?? profile;
        overlayWindow?.SetSwitching(true, displayName);
        try
        {
            string? workspace = settings.ReopenLastWorkspaceAfterSwitch
                ? FirstNonEmpty(settings.LastOpenedWorkspace, workspaceHistory.ReadLastWorkspace())
                : null;
            ProfileActivationResult result = await CreateActivationService()
                .ActivateAsync(
                    profile,
                    settings.CustomVsCodeExecutablePath,
                    settings.DedicatedVsCodeUserDataDirectory,
                    settings.DedicatedVsCodeExtensionsDirectory,
                    workspace,
                    TimeSpan.FromSeconds(settings.GracefulCloseTimeoutSeconds),
                    restartIfActive,
                    requireExtension: true,
                    openCodexOnStartup: settings.LaunchCodexSidebarOnStartup,
                    progress: key => overlayWindow?.SetSwitchingStatus(localizer[key]),
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
                overlayWindow?.SetProfileActivationStatus(profile, result.MessageKey);
                overlayWindow?.ShowError(message);
                trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
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
            ShowIntegrationError("VsCodeLaunchFailed");
        }
        finally
        {
            overlayWindow?.SetSwitching(false, null);
        }
    }

    private void LaunchManagedVsCode()
    {
        string? active = activeProfileStore.Read();
        if (string.IsNullOrWhiteSpace(active))
        {
            ShowIntegrationError("SelectProfileFirst");
            return;
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
    {
        string? executable = executableLocator.Locate(settings.CustomVsCodeExecutablePath);
        if (executable is null)
        {
            ShowIntegrationError("VsCodeExecutableMissing");
            return;
        }

        CodexExtensionStatus result = await extensionManager.InstallAsync(
            executable,
            settings.DedicatedVsCodeUserDataDirectory,
            settings.DedicatedVsCodeExtensionsDirectory,
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
            ShowIntegrationError(key);
        }

        settingsWindow?.RefreshIntegrationPage();
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
            () => Application.Current.Shutdown());
        settingsWindow.Closed += (_, _) => settingsWindow = null;
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
        ShowOverlay();
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
        if (!shouldShow || managedWindow is null)
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow!.Hide();
            trayIcon?.UpdateOverlayState(false);
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
