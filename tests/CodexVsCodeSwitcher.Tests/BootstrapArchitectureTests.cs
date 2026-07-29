using CodexVsCodeSwitcher.Core;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class BootstrapArchitectureTests
{
    [Fact]
    public void ProductIdentity_IsUnique()
    {
        Assert.Equal("CodexVsCodeSwitcher.exe", ProductIdentity.ExecutableName);
        Assert.Equal("ZOONGG.CodexVsCodeSwitcher", ProductIdentity.AppUserModelId);
        Assert.DoesNotContain("ProfileOverlay", ProductIdentity.MutexName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("{00000000-0000-0000-0000-000000000000}", ProductIdentity.InstallerAppId);
        Assert.False(UpdateChannelConfiguration.Unconfigured.IsConfigured);
        Assert.Null(UpdateChannelConfiguration.Unconfigured.Endpoint);
    }

    [Fact]
    public void IntegrationContracts_SupportConcreteManagedBackend()
    {
        Type[] contracts =
        [
            typeof(IVsCodeLocator),
            typeof(IVsCodeLauncher),
            typeof(IVsCodeProcessService),
            typeof(IVsCodeWindowLocator),
            typeof(ICodexProfileHomeService),
            typeof(IWorkspaceHistoryService),
            typeof(IProtectedPathPolicy),
        ];

        Assert.All(contracts, static contract => Assert.True(contract.IsInterface));
        Assert.True(VsCodeIntegrationStatus.Ready.IsImplemented);
        Assert.True(VsCodeIntegrationStatus.Ready.IsPrepared);
    }

    [Fact]
    public void ManagedActivation_NeverMutatesGlobalEnvironmentOrCopiesProfiles()
    {
        string source = ReadRepositoryFile(
            "src",
            "CodexVsCodeSwitcher.Core",
            "Services",
            "ProfileActivationService.cs");

        Assert.DoesNotContain("SetEnvironmentVariable", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Copy", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionController_DoesNotLaunchASecondCodexHomeProcessForUsage()
    {
        string source = ReadRepositoryFile(
            "src",
            "CodexVsCodeSwitcher",
            "OverlayController.cs");

        Assert.Contains("new UnavailableUsageProvider()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new CodexCliStatusUsageProvider()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Overlay_IsNotConfiguredAsGloballyTopmost()
    {
        string controller = ReadRepositoryFile(
            "src",
            "CodexVsCodeSwitcher",
            "OverlayController.cs");
        string window = ReadRepositoryFile(
            "src",
            "CodexVsCodeSwitcher",
            "OverlayWindow.cs");

        Assert.DoesNotContain("Topmost = true", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("Topmost = true", window, StringComparison.Ordinal);
        Assert.Contains("ManagedVsCodeWindowTracker", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_DoesNotQueryOrControlChatGptDesktop()
    {
        string root = FindRepositoryRoot();
        IEnumerable<string> sourceFiles = Directory.EnumerateFiles(
            Path.Combine(root, "src"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.All(sourceFiles, path =>
        {
            string source = File.ReadAllText(path);
            Assert.DoesNotContain("ChatGPT.exe", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("GetProcessesByName(\"ChatGPT", source, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void MinimalBackup_HasNoRecursiveProfileBackupPath()
    {
        string source = ReadRepositoryFile(
            "src",
            "CodexVsCodeSwitcher.Core",
            "Services",
            "MinimalBackupService.cs");

        Assert.DoesNotContain("SearchOption.AllDirectories", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.GetFiles", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(params string[] parts)
        => File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodexVsCodeSwitcher.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
