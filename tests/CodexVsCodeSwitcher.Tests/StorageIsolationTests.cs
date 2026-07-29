using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class StorageIsolationTests
{
    [Fact]
    public void Layout_UsesDedicatedRoots()
    {
        using var temp = new TempDirectory();
        string user = Path.Combine(temp.Path, "user");
        string local = Path.Combine(temp.Path, "local");

        var layout = new CodexVsCodeStorageLayout(user, local);

        Assert.Equal(Path.Combine(user, ".codex-vscode-profiles"), layout.ProfilesDirectory);
        Assert.Equal(Path.Combine(local, "CodexVsCodeSwitcher"), layout.ApplicationDataDirectory);
        Assert.Equal(Path.Combine(layout.ApplicationDataDirectory, "logs"), layout.LogDirectory);
        Assert.Equal(Path.Combine(layout.ApplicationDataDirectory, "backups"), layout.BackupDirectory);
        Assert.Equal(Path.Combine(layout.ApplicationDataDirectory, "transactions"), layout.TransactionDirectory);
        Assert.Equal(Path.Combine(layout.ApplicationDataDirectory, "VSCodeData"), layout.VsCodeUserDataDirectory);
        Assert.Equal(Path.Combine(layout.ApplicationDataDirectory, "VSCodeExtensions"), layout.VsCodeExtensionsDirectory);
    }

    [Fact]
    public void ProtectedPolicy_RejectsRootsDescendantsTraversalAndCaseVariants()
    {
        using var temp = new TempDirectory();
        var layout = new CodexVsCodeStorageLayout(
            Path.Combine(temp.Path, "user"),
            Path.Combine(temp.Path, "local"));
        var policy = ProtectedPathPolicy.FromLayout(layout);

        Assert.Throws<UnauthorizedAccessException>(() => policy.AssertCanWrite(layout.ProtectedMainCodexDirectory));
        Assert.Throws<UnauthorizedAccessException>(() => policy.AssertCanWrite(Path.Combine(layout.ProtectedMainCodexDirectory, "auth.json")));
        Assert.Throws<UnauthorizedAccessException>(() => policy.AssertCanWrite(layout.ProtectedOriginalApplicationDataDirectory.ToUpperInvariant()));
        Assert.Throws<UnauthorizedAccessException>(() => policy.AssertCanWrite(
            Path.Combine(layout.UserProfile, "safe", "..", ".codex", "auth.json")));
        Assert.Throws<UnauthorizedAccessException>(() => policy.AssertCanCopy(
            layout.ProtectedMainCodexDirectory.Replace('\\', '/'),
            Path.Combine(layout.BackupDirectory, "copy")));

        policy.AssertCanWrite(layout.SettingsFile);
        policy.AssertCanCopy(
            Path.Combine(layout.ProfilesDirectory, "work", "auth.json"),
            Path.Combine(layout.BackupDirectory, "safe", "auth.json"));
    }

    [Fact]
    public async Task BootstrapActivation_DoesNotCreateOrModifyAuthenticationFiles()
    {
        using var temp = new TempDirectory();
        string profile = Path.Combine(temp.Path, "profile");
        Directory.CreateDirectory(profile);
        string auth = Path.Combine(profile, "auth.json");
        File.WriteAllText(auth, "dummy-auth");
        DateTime before = File.GetLastWriteTimeUtc(auth);

        BootstrapActivationResult result = await new BootstrapProfileActivationService().ActivateAsync("work");

        Assert.False(result.Succeeded);
        Assert.Equal(BootstrapProfileActivationService.MessageKey, result.MessageKey);
        Assert.Equal("dummy-auth", File.ReadAllText(auth));
        Assert.Equal(before, File.GetLastWriteTimeUtc(auth));
    }
}
