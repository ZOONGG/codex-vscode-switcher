using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProfileMigrationPlannerTests
{
    [Fact]
    public void CreatePlan_IsReadOnlyAndIncludesOnlySmallAllowlistedFiles()
    {
        using var temp = new TestLayout();
        string legacy = Path.Combine(temp.Paths.LegacyProfilesDirectory, "work");
        Directory.CreateDirectory(Path.Combine(legacy, "sessions"));
        File.WriteAllText(Path.Combine(legacy, "auth.json"), "dummy");
        File.WriteAllText(Path.Combine(legacy, "config.toml"), "model = \"test\"");
        File.WriteAllText(Path.Combine(legacy, "state.sqlite"), "database");
        File.WriteAllText(Path.Combine(legacy, "sessions", "rollout.jsonl"), "session");

        IReadOnlyList<ProfileMigrationCandidate> plan = new ProfileMigrationPlanner(temp.Paths).CreatePlan();

        ProfileMigrationCandidate candidate = Assert.Single(plan);
        Assert.Equal("work", candidate.ProfileId);
        Assert.Equal(new[] { "auth.json", "config.toml" }, candidate.AllowedFiles.Select(static file => file.FileName).Order().ToArray());
        Assert.False(Directory.Exists(candidate.DestinationDirectory));
        Assert.True(File.Exists(Path.Combine(legacy, "state.sqlite")));
        Assert.True(File.Exists(Path.Combine(legacy, "sessions", "rollout.jsonl")));
    }
}
