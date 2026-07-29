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
    private readonly BootstrapProfileActivationService activationService;
    private readonly IStartupRegistrationService startupRegistrationService;
    private readonly SafeLogger logger;
    private readonly MinimalBackupService backupMaintenance;
    private readonly ProfileStatusService statusService;
    private readonly VsCodeWindowFinder windowFinder = new();
    private readonly DispatcherTimer windowTrackingTimer;
    private readonly CancellationTokenSource disposalTokenSource = new();
    private OverlaySettings settings;
    private Localizer localizer;
    private TrayIconService? trayIcon;
    private OverlayWindow? overlayWindow;
    private SettingsWindow? settingsWindow;
    private ProfileManagerWindow? profileManagerWindow;
    private HotkeyManager? hotkeyManager;
    private IReadOnlyList<ProfileInfo> profiles = [];
    private IntPtr attachedVsCodeWindow;
    private bool overlayRequestedVisible;
    private bool persistedAttachmentPreference;

    public OverlayController(
        CodexVsCodeStorageLayout paths,
        IProtectedPathPolicy protectedPaths,
        ProfileManagerService profileManager,
        ActiveProfileStore activeProfileStore,
        SettingsService settingsService,
        BootstrapProfileActivationService activationService,
        IStartupRegistrationService startupRegistrationService,
        SafeLogger logger,
        MinimalBackupService backupMaintenance)
    {
        this.paths = paths;
        this.protectedPaths = protectedPaths;
        this.profileManager = profileManager;
        this.activeProfileStore = activeProfileStore;
        this.settingsService = settingsService;
        this.activationService = activationService;
        this.startupRegistrationService = startupRegistrationService;
        this.logger = logger;
        this.backupMaintenance = backupMaintenance;
        settings = settingsService.Load();
        persistedAttachmentPreference = settings.AttachOverlayToVsCode;
        localizer = new Localizer(settings.Language);
        App.ApplyTheme(settings.Theme);
        var statusStore = new ProfileStatusStore(paths.ProfileStatusFile, protectedPaths);
        statusService = new ProfileStatusService(statusStore, new CodexCliStatusUsageProvider(), logger);
        statusService.SetStaleThreshold(TimeSpan.FromMinutes(settings.StaleDataThresholdMinutes));
        windowTrackingTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(750),
        };
        windowTrackingTimer.Tick += (_, _) => TrackVsCodeWindowSafely();
    }

    public void Start()
    {
        EnsureOverlay();
        EnsureTray();
        RefreshProfiles();
        ApplySettings();
        if (settings.ShowOverlayOnStart)
        {
            ShowOverlay();
        }

        windowTrackingTimer.Start();
        if (settingsService.LastLoadWarningKey is string warningKey)
        {
            overlayWindow?.ShowError(localizer[warningKey]);
        }

        logger.Info("Switcher shell started; read-only VS Code window tracking is enabled and profile activation remains disabled.");
    }

    public void Dispose()
    {
        disposalTokenSource.Cancel();
        windowTrackingTimer.Stop();
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
            Topmost = true,
            OnSwitchProfile = profile => _ = ShowBootstrapUnavailableAsync(profile),
            OnRefreshProfiles = RefreshProfiles,
            OnOpenProfilesFolder = () => OpenFolder(paths.ProfilesDirectory),
            OnOpenApplicationDataFolder = () => OpenFolder(paths.ApplicationDataDirectory),
            OnOpenSettings = ShowSettingsWindow,
            OnManageProfiles = ShowProfileManager,
            OnAddProfile = ShowBootstrapUnavailable,
            OnHideOverlay = HideOverlay,
            OnExit = () => Application.Current.Shutdown(),
            OnSettingsChanged = SaveSettings,
            Localizer = localizer,
        };

        hotkeyManager = new HotkeyManager(overlayWindow.Handle);
        hotkeyManager.ToggleOverlayRequested += ToggleOverlay;
        hotkeyManager.ProfileHotkeyRequested += index =>
        {
            if (index >= 0 && index < profiles.Count)
            {
                _ = ShowBootstrapUnavailableAsync(profiles[index].Name);
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
        trayIcon.OpenCodexRequested += ShowSettingsWindow;
        trayIcon.SettingsRequested += ShowSettingsWindow;
        trayIcon.ProfileSelected += profile => _ = ShowBootstrapUnavailableAsync(profile);
        trayIcon.StartWithWindowsChanged += enabled =>
        {
            settings.StartWithWindows = enabled;
            SaveSettings(settings);
        };
        trayIcon.ExitRequested += () => Application.Current.Shutdown();
    }

    private async Task ShowBootstrapUnavailableAsync(string profile)
    {
        overlayWindow?.SetSwitching(true);
        try
        {
            BootstrapActivationResult result = await activationService
                .ActivateAsync(profile, disposalTokenSource.Token)
                .ConfigureAwait(true);
            string message = localizer[result.MessageKey];
            overlayWindow?.ShowError(message);
            trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
            logger.Info($"Profile activation requested for '{profile}'; bootstrap backend refused the operation.");
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            overlayWindow?.SetSwitching(false);
        }
    }

    private void ShowBootstrapUnavailable()
    {
        string message = localizer[BootstrapProfileActivationService.MessageKey];
        overlayWindow?.ShowError(message);
        trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
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
        if (profile is null)
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
            ShowBootstrapUnavailable,
            ShowProfileManager,
            () => OpenFolder(paths.ProfilesDirectory),
            () => OpenFolder(paths.RemovedProfilesDirectory),
            () => OpenFolder(paths.ApplicationDataDirectory),
            () => OpenFolder(paths.BackupDirectory),
            () => OpenFolder(paths.LogDirectory),
            ResetPosition,
            ResetSettings,
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
            ShowBootstrapUnavailable,
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
        UpdateOverlayHost();
    }

    private void HideOverlay()
    {
        overlayRequestedVisible = false;
        overlayWindow?.Hide();
        trayIcon?.UpdateOverlayState(false);
    }

    private void ApplySettings()
    {
        App.ApplyTheme(settings.Theme);
        localizer.SetLanguage(settings.Language);
        overlayWindow?.ApplySettings();
        if (!settings.AttachOverlayToVsCode)
        {
            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow?.Detach();
        }

        if (overlayRequestedVisible)
        {
            UpdateOverlayHost();
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
            bool explicitAttachmentRequest = updatedSettings.AttachOverlayToVsCode
                && !persistedAttachmentPreference;
            if (explicitAttachmentRequest && FindVsCodeWindow() == IntPtr.Zero)
            {
                updatedSettings.AttachOverlayToVsCode = false;
                string message = localizer["VsCodeWindowNotFound"];
                overlayWindow?.ShowError(message);
                trayIcon?.ShowBalloon("Codex VS Code Switcher", message);
            }

            settings = updatedSettings;
            settingsService.Save(settings);
            persistedAttachmentPreference = settings.AttachOverlayToVsCode;
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
        persistedAttachmentPreference = settings.AttachOverlayToVsCode;
        settingsWindow?.Close();
        ApplySettings();
        ShowSettingsWindow();
    }

    private void UpdateOverlayHost()
    {
        EnsureOverlay();
        if (!overlayRequestedVisible)
        {
            return;
        }

        IntPtr vsCodeWindow = settings.AttachOverlayToVsCode ? FindVsCodeWindow() : IntPtr.Zero;
        if (vsCodeWindow != IntPtr.Zero)
        {
            if (attachedVsCodeWindow != vsCodeWindow)
            {
                attachedVsCodeWindow = vsCodeWindow;
                overlayWindow!.AttachTo(vsCodeWindow);
                logger.Info("Attached switcher overlay to a supported Visual Studio Code window.");
            }

            overlayWindow!.UpdatePlacement(vsCodeWindow);
        }
        else
        {
            if (attachedVsCodeWindow != IntPtr.Zero)
            {
                logger.Info("The attached Visual Studio Code window is unavailable; returning to floating mode.");
            }

            attachedVsCodeWindow = IntPtr.Zero;
            overlayWindow!.ShowFloating();
        }

        trayIcon?.UpdateOverlayState(overlayWindow.IsVisible);
    }

    private IntPtr FindVsCodeWindow()
    {
        bool canSearch = settings.AutomaticallyDetectVsCode
            || !string.IsNullOrWhiteSpace(settings.CustomVsCodeExecutablePath);
        return canSearch
            ? windowFinder.FindMainWindow(
                settings.CustomVsCodeExecutablePath,
                settings.AutomaticallyDetectVsCode)
            : IntPtr.Zero;
    }

    private void TrackVsCodeWindowSafely()
    {
        try
        {
            UpdateOverlayHost();
        }
        catch (Exception exception)
        {
            logger.Error("Visual Studio Code window tracking failed.", exception);
            if (attachedVsCodeWindow != IntPtr.Zero)
            {
                attachedVsCodeWindow = IntPtr.Zero;
                overlayWindow?.ShowFloating();
            }
        }
    }

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
