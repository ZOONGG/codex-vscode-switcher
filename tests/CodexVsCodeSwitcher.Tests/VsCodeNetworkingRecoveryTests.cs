using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class VsCodeNetworkingRecoveryTests
{
    [Theory]
    [InlineData(VsCodeExtensionMode.Shared, false)]
    [InlineData(VsCodeExtensionMode.Isolated, true)]
    public void ExtensionHostRestart_TargetsOnlyTheManagedVsCodeEnvironment(
        VsCodeExtensionMode mode,
        bool expectsExtensionsArgument)
    {
        using var temp = new TempDirectory();
        VsCodeProcessStartSpec plan = new VsCodeLaunchPlanBuilder().BuildExtensionHostRestart(
            Path.Combine(temp.Path, "Code.exe"),
            Path.Combine(temp.Path, "user-data"),
            Path.Combine(temp.Path, "extensions"),
            Path.Combine(temp.Path, "shared-data"),
            Path.Combine(temp.Path, "profile"),
            mode,
            CustomCaEnvironmentVariable.None,
            null);

        Assert.Equal(expectsExtensionsArgument, plan.Arguments.Contains("--extensions-dir"));
        Assert.Contains("--user-data-dir", plan.Arguments);
        Assert.Contains("--shared-data-dir", plan.Arguments);
        Assert.Contains("--reuse-window", plan.Arguments);
        Assert.Contains(
            "vscode://command/workbench.action.restartExtensionHost",
            plan.Arguments);
        Assert.DoesNotContain("--ignore-certificate-errors", plan.Arguments);
    }

    [Fact]
    public void SafeComparison_ReportsExactDifferentBackendPathsAndMatchingHashes()
    {
        using var layout = new TestLayout();
        string executable = Path.Combine(layout.LocalAppData, "Code.exe");
        Directory.CreateDirectory(layout.LocalAppData);
        File.WriteAllText(executable, "code-test");
        string ordinaryExtensions = layout.Paths.VsCodeSharedExtensionsDirectory;
        string isolatedExtensions = layout.Paths.VsCodeExtensionsDirectory;
        AddCodexExtension(ordinaryExtensions, "1.2.3", "same-backend");
        AddCodexExtension(isolatedExtensions, "1.2.3", "same-backend");
        string ordinaryUserData = Path.Combine(layout.LocalAppData, "ordinary-data");
        Directory.CreateDirectory(ordinaryUserData);
        Directory.CreateDirectory(layout.Paths.VsCodeUserDataDirectory);
        var service = new VsCodeEnvironmentComparisonService(layout.ProtectedPaths);

        VsCodeEnvironmentComparison comparison = service.Build(
            executable,
            ordinaryUserData,
            layout.Paths.VsCodeUserDataDirectory,
            ordinaryExtensions,
            isolatedExtensions,
            VsCodeExtensionMode.Isolated);

        Assert.True(comparison.BackendPathsDiffer);
        Assert.NotEqual(
            comparison.Ordinary.BackendExecutablePath,
            comparison.Managed.BackendExecutablePath);
        Assert.Equal(comparison.Ordinary.BackendSha256, comparison.Managed.BackendSha256);
        Assert.Equal("1.2.3", comparison.Ordinary.CodexExtensionVersion);
    }

    [Fact]
    public void SharedComparison_UsesTheSameExtensionAndBackendInstallationPaths()
    {
        using var layout = new TestLayout();
        string executable = Path.Combine(layout.LocalAppData, "Code.exe");
        Directory.CreateDirectory(layout.LocalAppData);
        File.WriteAllText(executable, "code-test");
        AddCodexExtension(layout.Paths.VsCodeSharedExtensionsDirectory, "1.2.3", "backend");
        string ordinaryData = Path.Combine(layout.LocalAppData, "ordinary-data");
        Directory.CreateDirectory(ordinaryData);
        Directory.CreateDirectory(layout.Paths.VsCodeUserDataDirectory);
        var service = new VsCodeEnvironmentComparisonService(layout.ProtectedPaths);

        VsCodeEnvironmentComparison comparison = service.Build(
            executable,
            ordinaryData,
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeSharedExtensionsDirectory,
            layout.Paths.VsCodeSharedExtensionsDirectory,
            VsCodeExtensionMode.Shared);

        Assert.False(comparison.BackendPathsDiffer);
        Assert.Equal(
            comparison.Ordinary.CodexExtensionPath,
            comparison.Managed.CodexExtensionPath);
    }

    private static void AddCodexExtension(string root, string version, string backendContents)
    {
        string extension = Path.Combine(root, $"openai.chatgpt-{version}-win32-x64");
        string bin = Path.Combine(extension, "bin", "windows-x86_64");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(extension, "package.json"), $"{{\"version\":\"{version}\"}}");
        File.WriteAllText(Path.Combine(bin, "codex.exe"), backendContents);
    }
}
