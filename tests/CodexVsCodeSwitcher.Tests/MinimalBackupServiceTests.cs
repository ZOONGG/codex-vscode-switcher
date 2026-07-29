using System.Text.Json;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class MinimalBackupServiceTests
{
    [Fact]
    public void CreateCompletedBackup_CopiesOnlyExplicitAllowlistedSmallFiles()
    {
        using var temp = new TestLayout();
        temp.AddProfile("work", "dummy-auth");
        string profile = Path.Combine(temp.Paths.ProfilesDirectory, "work");
        string config = Path.Combine(profile, "config.toml");
        File.WriteAllText(config, "model = \"test\"");
        var service = new MinimalBackupService(temp.Paths, temp.ProtectedPaths);

        string backup = service.CreateCompletedBackup(
            "work",
            [Path.Combine(profile, "auth.json"), config]);

        Assert.Equal(
            new[] { "auth.json", "config.toml", "manifest.json" },
            Directory.EnumerateFiles(backup).Select(Path.GetFileName).OrderBy(static name => name).ToArray());
        MinimalBackupManifest? manifest = JsonSerializer.Deserialize<MinimalBackupManifest>(
            File.ReadAllText(Path.Combine(backup, "manifest.json")));
        Assert.NotNull(manifest);
        Assert.Equal(2, manifest.Files.Count);
        Assert.DoesNotContain("dummy-auth", File.ReadAllText(Path.Combine(backup, "manifest.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void CreateCompletedBackup_RejectsSessionsDatabasesAndOversizedFiles()
    {
        using var temp = new TestLayout();
        string profile = Path.Combine(temp.Paths.ProfilesDirectory, "work");
        Directory.CreateDirectory(Path.Combine(profile, "sessions"));
        string rollout = Path.Combine(profile, "sessions", "rollout.jsonl");
        File.WriteAllText(rollout, "not allowed");
        string database = Path.Combine(profile, "state.sqlite");
        File.WriteAllText(database, "not allowed");
        var service = new MinimalBackupService(temp.Paths, temp.ProtectedPaths);

        Assert.Throws<InvalidOperationException>(() => service.CreateCompletedBackup("work", [rollout]));
        Assert.Throws<InvalidOperationException>(() => service.CreateCompletedBackup("work", [database]));

        string auth = Path.Combine(profile, "auth.json");
        using (FileStream stream = File.Create(auth))
        {
            stream.SetLength(MinimalBackupService.MaximumFileBytes + 1);
        }

        Assert.Throws<InvalidOperationException>(() => service.CreateCompletedBackup("work", [auth]));
    }

    [Fact]
    public void CreateCompletedBackup_RejectsProtectedSource()
    {
        using var temp = new TestLayout();
        Directory.CreateDirectory(temp.Paths.ProtectedMainCodexDirectory);
        string protectedAuth = Path.Combine(temp.Paths.ProtectedMainCodexDirectory, "auth.json");
        File.WriteAllText(protectedAuth, "dummy");
        var service = new MinimalBackupService(temp.Paths, temp.ProtectedPaths);

        Assert.Throws<UnauthorizedAccessException>(() => service.CreateCompletedBackup("work", [protectedAuth]));
    }
}
