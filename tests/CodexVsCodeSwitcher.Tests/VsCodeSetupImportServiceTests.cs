using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class VsCodeSetupImportServiceTests
{
    [Fact]
    public void Plan_ContainsOnlyAllowlistedSettingsSnippetsProfilesAndExtensionIds()
    {
        using var layout = new TestLayout();
        string ordinaryUser = Path.Combine(layout.LocalAppData, "ordinary", "User");
        string ordinaryExtensions = Path.Combine(layout.UserProfile, ".vscode", "extensions");
        Directory.CreateDirectory(Path.Combine(ordinaryUser, "snippets"));
        Directory.CreateDirectory(Path.Combine(ordinaryUser, "globalStorage"));
        Directory.CreateDirectory(Path.Combine(ordinaryUser, "workspaceStorage"));
        Directory.CreateDirectory(Path.Combine(ordinaryUser, "profiles", "named", "snippets"));
        File.WriteAllText(Path.Combine(ordinaryUser, "settings.json"), "{\"workbench.colorTheme\":\"Dark+\"}");
        File.WriteAllText(Path.Combine(ordinaryUser, "keybindings.json"), "[]");
        File.WriteAllText(Path.Combine(ordinaryUser, "snippets", "csharp.code-snippets"), "{}");
        File.WriteAllText(Path.Combine(ordinaryUser, "snippets", "ignored.txt"), "ignore");
        File.WriteAllText(Path.Combine(ordinaryUser, "globalStorage", "state.vscdb"), "secret");
        File.WriteAllText(Path.Combine(ordinaryUser, "workspaceStorage", "state.vscdb"), "secret");
        File.WriteAllText(Path.Combine(ordinaryUser, "profiles", "named", "settings.json"), "{}");
        File.WriteAllText(
            Path.Combine(ordinaryUser, "profiles", "named", "snippets", "profile.json"),
            "{}");
        AddExtension(ordinaryExtensions, "publisher", "theme");
        AddExtension(ordinaryExtensions, "openai", "chatgpt");

        var service = new VsCodeSetupImportService(
            layout.ProtectedPaths,
            new RecordingRunner(),
            new VsCodeLaunchPlanBuilder());
        VsCodeSetupImportPlan plan = service.BuildPlan(ordinaryUser, ordinaryExtensions);

        Assert.Equal(
            new[]
            {
                "keybindings.json",
                Path.Combine("profiles", "named", "settings.json"),
                Path.Combine("profiles", "named", "snippets", "profile.json"),
                "settings.json",
                Path.Combine("snippets", "csharp.code-snippets"),
            },
            plan.Files.Select(static file => file.DestinationRelativePath));
        Assert.Equal(new[] { "publisher.theme" }, plan.ExtensionIds);
        Assert.DoesNotContain(
            plan.Files,
            file => file.SourcePath.Contains("globalStorage", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            plan.Files,
            file => file.SourcePath.Contains("workspaceStorage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Import_CopiesOnlyConfirmedPlanAndTargetsDedicatedExtensions()
    {
        using var layout = new TestLayout();
        string ordinaryUser = Path.Combine(layout.LocalAppData, "ordinary", "User");
        string ordinaryExtensions = Path.Combine(layout.UserProfile, ".vscode", "extensions");
        Directory.CreateDirectory(ordinaryUser);
        File.WriteAllText(Path.Combine(ordinaryUser, "settings.json"), "{\"editor.fontSize\":16}");
        AddExtension(ordinaryExtensions, "publisher", "theme");
        var runner = new RecordingRunner();
        var service = new VsCodeSetupImportService(
            layout.ProtectedPaths,
            runner,
            new VsCodeLaunchPlanBuilder());
        VsCodeSetupImportPlan plan = service.BuildPlan(ordinaryUser, ordinaryExtensions);
        string executable = Path.Combine(layout.LocalAppData, "Code.exe");
        File.WriteAllText(executable, "fake");

        VsCodeSetupImportResult result = await service.ImportAsync(
            plan,
            executable,
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            layout.Paths.VsCodeSharedDataDirectory,
            plan.ExtensionIds,
            CancellationToken.None);

        Assert.Equal(new[] { "settings.json" }, result.CopiedFiles);
        Assert.Equal(new[] { "publisher.theme" }, result.InstalledExtensionIds);
        Assert.Empty(result.ExtensionFailures);
        Assert.Equal(
            "{\"editor.fontSize\":16}",
            File.ReadAllText(Path.Combine(
                layout.Paths.VsCodeUserDataDirectory,
                "User",
                "settings.json")));
        VsCodeProcessStartSpec install = Assert.Single(runner.Plans);
        AssertArgumentValue(
            install.Arguments,
            "--extensions-dir",
            layout.Paths.VsCodeExtensionsDirectory);
        Assert.Contains("publisher.theme", install.Arguments);
    }

    [Fact]
    public async Task Import_RejectsExtensionOutsideConfirmedPlan()
    {
        using var layout = new TestLayout();
        string ordinaryUser = Path.Combine(layout.LocalAppData, "ordinary", "User");
        string ordinaryExtensions = Path.Combine(layout.UserProfile, ".vscode", "extensions");
        Directory.CreateDirectory(ordinaryUser);
        Directory.CreateDirectory(ordinaryExtensions);
        var runner = new RecordingRunner();
        var service = new VsCodeSetupImportService(
            layout.ProtectedPaths,
            runner,
            new VsCodeLaunchPlanBuilder());
        VsCodeSetupImportPlan plan = service.BuildPlan(ordinaryUser, ordinaryExtensions);

        VsCodeSetupImportResult result = await service.ImportAsync(
            plan,
            Path.Combine(layout.LocalAppData, "Code.exe"),
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            layout.Paths.VsCodeSharedDataDirectory,
            ["unconfirmed.extension"],
            CancellationToken.None);

        VsCodeExtensionImportFailure failure = Assert.Single(result.ExtensionFailures);
        Assert.Equal("unconfirmed.extension", failure.ExtensionId);
        Assert.Empty(runner.Plans);
    }

    private static void AddExtension(string root, string publisher, string name)
    {
        string directory = Path.Combine(root, $"{publisher}.{name}-1.0.0");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "package.json"),
            $$"""{"publisher":"{{publisher}}","name":"{{name}}"}""");
    }

    private static void AssertArgumentValue(
        IReadOnlyList<string> arguments,
        string option,
        string expected)
    {
        int index = arguments.IndexOf(option);
        Assert.True(index >= 0 && index + 1 < arguments.Count);
        Assert.Equal(Path.GetFullPath(expected), arguments[index + 1]);
    }

    private sealed class RecordingRunner : IProcessCommandRunner
    {
        public List<VsCodeProcessStartSpec> Plans { get; } = [];

        public Task<int> RunAsync(
            VsCodeProcessStartSpec startSpec,
            CancellationToken cancellationToken)
        {
            Plans.Add(startSpec);
            return Task.FromResult(0);
        }
    }
}
