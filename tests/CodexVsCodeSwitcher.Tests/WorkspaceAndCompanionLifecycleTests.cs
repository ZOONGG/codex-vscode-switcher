using System.Text.Json;
using System.Text.Json.Serialization;
using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class WorkspaceAndCompanionLifecycleTests
{
    private static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void MissingBundledCompanion_IsARecoverableLaunchWarning()
    {
        using var layout = new TestLayout();
        string missingExtension = Path.Combine(layout.LocalAppData, "missing-companion");
        var bridge = new CompanionBridgeService(
            layout.Paths.BridgeDirectory,
            missingExtension,
            layout.ProtectedPaths,
            CreateHistory(layout));

        bool available = bridge.TryBeginSession(
            openCodexAutomatically: true,
            out ManagedCompanionLaunchOptions? launch,
            out Exception? failure);

        Assert.False(available);
        Assert.Null(launch);
        Assert.IsType<DirectoryNotFoundException>(failure);
        Assert.False(Directory.Exists(layout.Paths.BridgeDirectory));
    }

    [Fact]
    public void ManuallyOpenedFolder_IsReportedStoredAndRestoredAfterRestart()
    {
        using var layout = new TestLayout();
        string folder = Path.Combine(layout.UserProfile, "MoonRise");
        Directory.CreateDirectory(folder);
        var history = CreateHistory(layout);
        CompanionBridgeService bridge = CreateBridge(layout, history, out ManagedCompanionLaunchOptions launch);

        WriteBridgeState(layout, launch.SessionId, WorkspaceType.Folder, folder, [folder]);
        CompanionBridgeState? state = bridge.TryReadAndApplyLatest();

        Assert.NotNull(state);
        Assert.Equal(Path.GetFullPath(folder), history.ReadLastWorkspace());
        var reloaded = CreateHistory(layout);
        Assert.Equal(Path.GetFullPath(folder), reloaded.ReadLastWorkspace());
        Assert.True(reloaded.ReadSnapshot().CurrentProject?.PathExists);
    }

    [Fact]
    public void ManuallyOpenedWorkspaceFile_IsReportedAndStored()
    {
        using var layout = new TestLayout();
        string folder = Path.Combine(layout.UserProfile, "repo");
        string workspace = Path.Combine(layout.UserProfile, "MoonRise.code-workspace");
        Directory.CreateDirectory(folder);
        File.WriteAllText(workspace, "{}");
        var history = CreateHistory(layout);
        CompanionBridgeService bridge = CreateBridge(layout, history, out ManagedCompanionLaunchOptions launch);

        WriteBridgeState(layout, launch.SessionId, WorkspaceType.WorkspaceFile, workspace, [folder]);
        _ = bridge.TryReadAndApplyLatest();

        WorkspaceDescriptor current = Assert.IsType<WorkspaceDescriptor>(history.ReadSnapshot().CurrentProject);
        Assert.Equal(WorkspaceType.WorkspaceFile, current.Type);
        Assert.Equal(Path.GetFullPath(workspace), current.Path);
    }

    [Fact]
    public void WorkspaceChanges_UpdateSharedCurrentProjectWithoutProfilePartitioning()
    {
        using var layout = new TestLayout();
        string first = Path.Combine(layout.UserProfile, "first");
        string second = Path.Combine(layout.UserProfile, "second");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);
        var history = CreateHistory(layout);
        CompanionBridgeService bridge = CreateBridge(layout, history, out ManagedCompanionLaunchOptions launch);

        WriteBridgeState(layout, launch.SessionId, WorkspaceType.Folder, first, [first], DateTimeOffset.UtcNow);
        _ = bridge.TryReadAndApplyLatest();
        WriteBridgeState(layout, launch.SessionId, WorkspaceType.Folder, second, [second], DateTimeOffset.UtcNow.AddSeconds(1));
        _ = bridge.TryReadAndApplyLatest();

        Assert.Equal(Path.GetFullPath(second), history.ReadLastWorkspace());
        Assert.Equal(2, history.ReadSnapshot().RecentProjects.Count);
        Assert.DoesNotContain("profile", File.ReadAllText(layout.Paths.LastWorkspaceMetadataFile), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemporaryEmptyWorkspace_DoesNotOverwriteValidCurrentProject()
    {
        using var layout = new TestLayout();
        string folder = Path.Combine(layout.UserProfile, "current");
        Directory.CreateDirectory(folder);
        var history = CreateHistory(layout);
        history.SaveLastWorkspace(folder);
        CompanionBridgeService bridge = CreateBridge(layout, history, out ManagedCompanionLaunchOptions launch);

        WriteBridgeState(layout, launch.SessionId, WorkspaceType.Empty, null, []);
        _ = bridge.TryReadAndApplyLatest();

        Assert.Equal(Path.GetFullPath(folder), history.ReadLastWorkspace());
    }

    [Fact]
    public void RecentProjects_AreDeduplicatedAndCappedAtTen()
    {
        using var layout = new TestLayout();
        var history = CreateHistory(layout);
        for (int index = 0; index < 12; index++)
        {
            string folder = Path.Combine(layout.UserProfile, $"project-{index}");
            Directory.CreateDirectory(folder);
            history.SaveLastWorkspace(folder);
        }

        string latest = history.ReadLastWorkspace()!;
        history.SaveLastWorkspace(latest.ToUpperInvariant());
        WorkspaceHistorySnapshot snapshot = history.ReadSnapshot();

        Assert.Equal(WorkspaceHistoryService.MaximumRecentProjects, snapshot.RecentProjects.Count);
        Assert.Equal(snapshot.RecentProjects.Count, snapshot.RecentProjects
            .Select(static item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count());
    }

    [Fact]
    public void BridgeRejectsTraversalAndCorruptionWithoutChangingHistory()
    {
        using var layout = new TestLayout();
        var history = CreateHistory(layout);
        CompanionBridgeService bridge = CreateBridge(layout, history, out ManagedCompanionLaunchOptions launch);
        string traversing = Path.Combine(layout.UserProfile, "folder", "..", "elsewhere");
        Directory.CreateDirectory(Path.GetFullPath(traversing));

        WriteBridgeState(layout, launch.SessionId, WorkspaceType.Folder, traversing, [traversing]);
        Assert.Null(bridge.TryReadAndApplyLatest());
        Assert.Null(history.ReadLastWorkspace());

        File.WriteAllText(Path.Combine(layout.Paths.BridgeDirectory, CompanionBridgeService.StateFileName), "{broken");
        Assert.Null(bridge.TryReadAndApplyLatest());
        Assert.Null(history.ReadLastWorkspace());
    }

    [Fact]
    public void CompanionLaunch_IsManagedOnlyAndCarriesNoContentOrCredentialFields()
    {
        using var layout = new TestLayout();
        string executable = Path.Combine(layout.LocalAppData, "Code.exe");
        string profile = Path.Combine(layout.Paths.ProfilesDirectory, "fake");
        string companion = Path.Combine(layout.LocalAppData, "companion");
        Directory.CreateDirectory(profile);
        Directory.CreateDirectory(companion);
        var builder = new VsCodeLaunchPlanBuilder();
        var options = new ManagedCompanionLaunchOptions(
            companion,
            layout.Paths.BridgeDirectory,
            "safe-session",
            true);

        VsCodeProcessStartSpec managed = builder.Build(
            executable,
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            layout.Paths.VsCodeSharedDataDirectory,
            profile,
            null,
            companion: options);
        VsCodeProcessStartSpec ordinary = builder.Build(
            executable,
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            layout.Paths.VsCodeSharedDataDirectory,
            profile,
            null);

        Assert.Contains("--extensionDevelopmentPath", managed.Arguments);
        Assert.Contains(CompanionBridgeService.BridgePathEnvironmentVariable, managed.EnvironmentOverrides.Keys);
        Assert.DoesNotContain("--extensionDevelopmentPath", ordinary.Arguments);
        Assert.DoesNotContain(CompanionBridgeService.BridgePathEnvironmentVariable, ordinary.EnvironmentOverrides.Keys);
        Assert.DoesNotContain(typeof(CompanionBridgeState).GetProperties(), property =>
            property.Name.Contains("Content", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            || property.Name.Contains("Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingProject_RemainsCurrentUntilUserChoosesAnAction()
    {
        using var layout = new TestLayout();
        string missing = Path.Combine(layout.UserProfile, "missing.code-workspace");
        var history = CreateHistory(layout);
        history.SaveLastWorkspace(missing);

        WorkspaceDescriptor current = Assert.IsType<WorkspaceDescriptor>(history.ReadSnapshot().CurrentProject);
        Assert.False(current.PathExists);
        Assert.Equal(Path.GetFullPath(missing), current.Path);
        Assert.Contains(current, history.ReadSnapshot().RecentProjects);
    }

    private static WorkspaceHistoryService CreateHistory(TestLayout layout)
        => new(layout.Paths.LastWorkspaceMetadataFile, layout.ProtectedPaths);

    private static CompanionBridgeService CreateBridge(
        TestLayout layout,
        WorkspaceHistoryService history,
        out ManagedCompanionLaunchOptions launch)
    {
        string extension = Path.Combine(layout.LocalAppData, "companion-extension");
        Directory.CreateDirectory(extension);
        File.WriteAllText(Path.Combine(extension, "package.json"), "{}");
        File.WriteAllText(Path.Combine(extension, "extension.js"), "module.exports = {};");
        var bridge = new CompanionBridgeService(
            layout.Paths.BridgeDirectory,
            extension,
            layout.ProtectedPaths,
            history);
        launch = bridge.BeginSession(openCodexAutomatically: true);
        return bridge;
    }

    private static void WriteBridgeState(
        TestLayout layout,
        string sessionId,
        WorkspaceType type,
        string? path,
        IReadOnlyList<string> folders,
        DateTimeOffset? timestamp = null)
    {
        Directory.CreateDirectory(layout.Paths.BridgeDirectory);
        var state = new CompanionBridgeState(
            CompanionBridgeService.ProtocolVersion,
            sessionId,
            "test-window",
            timestamp ?? DateTimeOffset.UtcNow,
            type,
            path,
            folders,
            type == WorkspaceType.Empty,
            true,
            SidebarOpenStatus.Succeeded,
            false);
        File.WriteAllText(
            Path.Combine(layout.Paths.BridgeDirectory, CompanionBridgeService.StateFileName),
            JsonSerializer.Serialize(state, BridgeJsonOptions));
    }
}
