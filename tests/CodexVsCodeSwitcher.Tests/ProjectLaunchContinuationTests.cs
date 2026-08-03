using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProjectLaunchContinuationTests
{
    [Fact]
    public async Task SelectingFolder_PersistsBeforeResumingOriginalProfile()
    {
        using var layout = new TestLayout();
        string project = Path.Combine(layout.UserProfile, "site project");
        Directory.CreateDirectory(project);
        var history = new WorkspaceHistoryService(
            layout.Paths.LastWorkspaceMetadataFile,
            layout.ProtectedPaths);
        var flow = new ProjectLaunchContinuation(layout.ProtectedPaths);
        PendingProjectActivation pending = flow.Begin("grille");
        ResolvedProjectActivation selection = flow.SelectFolder(pending, project);
        var order = new List<string>();
        string? activatedProfile = null;

        await flow.ResumeAsync(
            selection,
            path =>
            {
                history.SaveLastWorkspace(path);
                order.Add("saved");
            },
            profile =>
            {
                Assert.Equal(Path.GetFullPath(project), history.ReadLastWorkspace());
                activatedProfile = profile;
                order.Add("activated");
                return Task.CompletedTask;
            });

        Assert.Equal("grille", activatedProfile);
        Assert.Equal(["saved", "activated"], order);
        Assert.Equal(Path.GetFullPath(project), history.ReadSnapshot().CurrentProject?.Path);
        Assert.Contains(history.ReadSnapshot().RecentProjects, item =>
            item.Path?.Equals(Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task FailedActivation_PreservesChosenProjectForRetry()
    {
        using var layout = new TestLayout();
        string project = Path.Combine(layout.UserProfile, "сайт с пробелами");
        Directory.CreateDirectory(project);
        var history = new WorkspaceHistoryService(
            layout.Paths.LastWorkspaceMetadataFile,
            layout.ProtectedPaths);
        var flow = new ProjectLaunchContinuation(layout.ProtectedPaths);
        ResolvedProjectActivation selection = flow.SelectFolder(flow.Begin("grille"), project);

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ResumeAsync(
            selection,
            history.SaveLastWorkspace,
            _ => throw new InvalidOperationException("disposable launch failure")));

        Assert.Equal(Path.GetFullPath(project), history.ReadLastWorkspace());
    }

    [Fact]
    public void SpacesAndCyrillicProjectPath_IsOneStructuredLaunchArgument()
    {
        using var layout = new TestLayout();
        string project = Path.Combine(layout.UserProfile, "проект сайта с пробелами");
        Directory.CreateDirectory(project);
        ResolvedProjectActivation selection = new ProjectLaunchContinuation(layout.ProtectedPaths)
            .SelectFolder(new PendingProjectActivation("grille"), project);

        VsCodeProcessStartSpec plan = new VsCodeLaunchPlanBuilder().Build(
            Path.Combine(layout.LocalAppData, "Code.exe"),
            layout.Paths.VsCodeUserDataDirectory,
            layout.Paths.VsCodeExtensionsDirectory,
            layout.Paths.VsCodeSharedDataDirectory,
            Path.Combine(layout.Paths.ProfilesDirectory, "grille"),
            selection.ProjectPath,
            VsCodeExtensionMode.Shared);

        Assert.Equal(Path.GetFullPath(project), plan.Arguments[^1]);
        Assert.Equal(1, plan.Arguments.Count(argument =>
            argument.Equals(Path.GetFullPath(project), StringComparison.Ordinal)));
        Assert.DoesNotContain($"\"{project}\"", plan.Arguments);
    }

    [Fact]
    public async Task OpenWithoutProject_StillResumesPendingProfile()
    {
        using var layout = new TestLayout();
        var flow = new ProjectLaunchContinuation(layout.ProtectedPaths);
        ResolvedProjectActivation selection = flow.OpenWithoutProject(flow.Begin("grille"));
        string? activated = null;

        await flow.ResumeAsync(
            selection,
            path => Assert.Null(path),
            profile =>
            {
                activated = profile;
                return Task.CompletedTask;
            });

        Assert.Equal("grille", activated);
    }
}
