using System.Text.Json;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class BundledCompanionExtensionProvisionerTests
{
    [Fact]
    public void SingleExecutableResources_ProvisionCompleteCompanionExtension()
    {
        using var layout = new TestLayout();
        var provisioner = new BundledCompanionExtensionProvisioner(layout.ProtectedPaths);

        string directory = provisioner.Provision(layout.Paths.CompanionExtensionDirectory);

        Assert.Equal(layout.Paths.CompanionExtensionDirectory, directory);
        Assert.True(File.Exists(Path.Combine(directory, "package.json")));
        Assert.True(File.Exists(Path.Combine(directory, "extension.js")));
        Assert.True(File.Exists(Path.Combine(directory, "companion-core.js")));
        using JsonDocument manifest = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(directory, "package.json")));
        Assert.Equal(
            "codex-vscode-switcher-companion",
            manifest.RootElement.GetProperty("name").GetString());

        var history = new WorkspaceHistoryService(
            layout.Paths.LastWorkspaceMetadataFile,
            layout.ProtectedPaths);
        var bridge = new CompanionBridgeService(
            layout.Paths.BridgeDirectory,
            directory,
            layout.ProtectedPaths,
            history);
        ManagedCompanionLaunchOptions launch = bridge.BeginSession(openCodexAutomatically: true);
        Assert.Equal(directory, launch.ExtensionDirectory);
    }

    [Fact]
    public void Provision_AtomicallyRepairsTamperedAllowlistedFileAndPreservesUnrelatedFiles()
    {
        using var layout = new TestLayout();
        var provisioner = new BundledCompanionExtensionProvisioner(layout.ProtectedPaths);
        string directory = provisioner.Provision(layout.Paths.CompanionExtensionDirectory);
        string extension = Path.Combine(directory, "extension.js");
        string expected = File.ReadAllText(extension);
        string unrelated = Path.Combine(directory, "user-note.txt");
        File.WriteAllText(extension, "tampered");
        File.WriteAllText(unrelated, "preserve");

        provisioner.Provision(directory);

        Assert.Equal(expected, File.ReadAllText(extension));
        Assert.Equal("preserve", File.ReadAllText(unrelated));
        Assert.Empty(Directory.GetFiles(directory, ".*.tmp"));
    }

    [Fact]
    public void Provision_RejectsProtectedCodexRoot()
    {
        using var layout = new TestLayout();
        var provisioner = new BundledCompanionExtensionProvisioner(layout.ProtectedPaths);

        Assert.Throws<UnauthorizedAccessException>(() =>
            provisioner.Provision(Path.Combine(layout.Paths.ProtectedMainCodexDirectory, "companion")));
        Assert.False(Directory.Exists(layout.Paths.ProtectedMainCodexDirectory));
    }
}
