using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using CodexVsCodeSwitcher.Core;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;
using Application = System.Windows.Application;

namespace CodexVsCodeSwitcher;

public partial class App : Application
{
    private static readonly IReadOnlyDictionary<string, string> DarkTheme = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WindowBackgroundBrush"] = "#0B0B0D",
        ["Surface1Brush"] = "#141417",
        ["Surface2Brush"] = "#1B1B20",
        ["HoverSurfaceBrush"] = "#24242B",
        ["PressedSurfaceBrush"] = "#2B2B34",
        ["BorderBrush"] = "#303038",
        ["StrongTextBrush"] = "#F3F3F5",
        ["MutedTextBrush"] = "#A7A7B0",
        ["DisabledTextBrush"] = "#686872",
        ["AccentBrush"] = "#8B5CF6",
        ["AccentHoverBrush"] = "#9D72FF",
        ["SuccessBrush"] = "#2DD4A3",
        ["ErrorBrush"] = "#F05D6C",
        ["InputBackgroundBrush"] = "#101014",
        ["OverlayBackgroundBrush"] = "#000000",
        ["OverlayBorderBrush"] = "#17171B",
        ["TabBackgroundBrush"] = "#111114",
        ["TabHoverBrush"] = "#1A1A1F",
        ["TabActiveBrush"] = "#241B38",
    };

    private static readonly IReadOnlyDictionary<string, string> LightTheme = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WindowBackgroundBrush"] = "#F5F6FA",
        ["Surface1Brush"] = "#FFFFFF",
        ["Surface2Brush"] = "#F0F2F7",
        ["HoverSurfaceBrush"] = "#E7EAF2",
        ["PressedSurfaceBrush"] = "#DDE2EC",
        ["BorderBrush"] = "#C5CCD8",
        ["StrongTextBrush"] = "#161922",
        ["MutedTextBrush"] = "#5D6575",
        ["DisabledTextBrush"] = "#9AA1AE",
        ["AccentBrush"] = "#6D4AFF",
        ["AccentHoverBrush"] = "#7E61FF",
        ["SuccessBrush"] = "#0F9F75",
        ["ErrorBrush"] = "#D8435D",
        ["InputBackgroundBrush"] = "#FFFFFF",
        ["OverlayBackgroundBrush"] = "#FFFFFF",
        ["OverlayBorderBrush"] = "#AEB7C6",
        ["TabBackgroundBrush"] = "#EEF1F7",
        ["TabHoverBrush"] = "#E2E6EF",
        ["TabActiveBrush"] = "#E8E1FF",
    };

    private Mutex? mutex;
    private EventWaitHandle? activationEvent;
    private Thread? activationThread;
    private OverlayController? controller;
    private SafeLogger? logger;
    private volatile bool exiting;

    public static void ApplyTheme(AppTheme theme)
    {
        IReadOnlyDictionary<string, string> palette = theme == AppTheme.Light ? LightTheme : DarkTheme;
        foreach ((string key, string value) in palette)
        {
            if (ColorConverter.ConvertFromString(value) is Color color)
            {
                Current.Resources[key] = new SolidColorBrush(color);
            }
        }

        WindowCaptionThemeService.ApplyToOpenWindows(theme);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(ProductIdentity.AppUserModelId);

        mutex = new Mutex(initiallyOwned: true, ProductIdentity.MutexName, out bool created);
        if (!created)
        {
            try
            {
                using EventWaitHandle existingEvent = EventWaitHandle.OpenExisting(ProductIdentity.ActivationEventName);
                _ = existingEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
            }

            Shutdown();
            return;
        }

        activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ProductIdentity.ActivationEventName);

        var paths = CodexVsCodeStorageLayout.FromEnvironment();
        var protectedPaths = ProtectedPathPolicy.FromLayout(paths);
        protectedPaths.AssertCanWrite(paths.ApplicationDataDirectory);
        Directory.CreateDirectory(paths.ApplicationDataDirectory);
        logger = new SafeLogger(paths.LogDirectory, protectedPaths);

        try
        {
            var profileDiscovery = new ProfileDiscoveryService(paths.ProfilesDirectory, protectedPaths);
            var profileMetadataStore = new ProfileMetadataStore(paths.ProfilesMetadataFile, protectedPaths);
            var profileManager = new ProfileManagerService(paths, profileDiscovery, profileMetadataStore, protectedPaths);
            var activeProfileStore = new ActiveProfileStore(paths.ActiveProfileFile, protectedPaths);
            var settingsService = new SettingsService(paths, protectedPaths);
            var backupMaintenance = new MinimalBackupService(paths, protectedPaths, logger);
            var launchPlanBuilder = new VsCodeLaunchPlanBuilder();
            var processRunner = new ProcessCommandRunner();
            var extensionManager = new CodexExtensionManager(
                launchPlanBuilder,
                processRunner,
                protectedPaths);
            var setupImportService = new VsCodeSetupImportService(
                protectedPaths,
                processRunner,
                launchPlanBuilder);
            var workspaceHistory = new WorkspaceHistoryService(
                paths.LastWorkspaceMetadataFile,
                protectedPaths);
            var companionBridge = new CompanionBridgeService(
                paths.BridgeDirectory,
                Path.Combine(AppContext.BaseDirectory, "companion-extension"),
                protectedPaths,
                workspaceHistory);
            backupMaintenance.CleanupRetention();
            controller = new OverlayController(
                paths,
                protectedPaths,
                profileManager,
                activeProfileStore,
                settingsService,
                VsCodeExecutableLocator.FromEnvironment(),
                new ManagedVsCodeRuntime(),
                new ManagedInstanceStore(paths.ManagedInstanceMetadataFile, protectedPaths),
                workspaceHistory,
                companionBridge,
                extensionManager,
                launchPlanBuilder,
                new StartupRegistrationService(),
                logger,
                backupMaintenance,
                setupImportService);
            controller.Start();
            if (e.Args.Any(argument =>
                argument.Equals("--launch", StringComparison.OrdinalIgnoreCase)))
            {
                controller.LaunchLastProfileOrChoose();
            }

            StartActivationListener();
            logger.Info("Overlay started.");
        }
        catch (Exception exception)
        {
            logger?.Error("Startup failed.", exception);
            MessageBox.Show(exception.Message, ProductIdentity.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        activationEvent?.Set();
        controller?.Dispose();
        activationEvent?.Dispose();
        mutex?.Dispose();
        base.OnExit(e);
    }

    private void StartActivationListener()
    {
        if (activationEvent is null)
        {
            return;
        }

        activationThread = new Thread(() =>
        {
            while (!exiting)
            {
                activationEvent.WaitOne();
                if (!exiting)
                {
                    controller?.RevealFromSecondInstance();
                }
            }
        })
        {
            IsBackground = true,
            Name = "Single instance activation listener",
        };
        activationThread.Start();
    }
}
