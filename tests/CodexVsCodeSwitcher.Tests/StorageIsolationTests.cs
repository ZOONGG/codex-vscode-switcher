using CodexVsCodeSwitcher.Core.Services;
using CodexVsCodeSwitcher.Core.Models;

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
    public void Settings_EnforceProductOwnedVsCodeAndProfileRoots()
    {
        using var temp = new TempDirectory();
        var layout = new CodexVsCodeStorageLayout(
            Path.Combine(temp.Path, "user"),
            Path.Combine(temp.Path, "local"));
        var service = new SettingsService(layout, ProtectedPathPolicy.FromLayout(layout));
        var settings = new OverlaySettings
        {
            DedicatedVsCodeUserDataDirectory = Path.Combine(temp.Path, "other-data"),
            DedicatedVsCodeExtensionsDirectory = Path.Combine(temp.Path, "other-extensions"),
            CodexProfileRoot = Path.Combine(temp.Path, "other-profiles"),
        };

        service.Save(settings);

        Assert.Equal(layout.VsCodeUserDataDirectory, settings.DedicatedVsCodeUserDataDirectory);
        Assert.Equal(layout.VsCodeExtensionsDirectory, settings.DedicatedVsCodeExtensionsDirectory);
        Assert.Equal(layout.ProfilesDirectory, settings.CodexProfileRoot);
        Assert.True(settings.ShowOverlayOnlyWithManagedVsCode);
    }

    [Fact]
    public void LaunchPlanning_DoesNotCreateOrModifyAuthenticationFiles()
    {
        using var temp = new TempDirectory();
        string profile = Path.Combine(temp.Path, "profile");
        Directory.CreateDirectory(profile);
        string auth = Path.Combine(profile, "auth.json");
        File.WriteAllText(auth, "dummy-auth");
        DateTime before = File.GetLastWriteTimeUtc(auth);

        _ = new VsCodeLaunchPlanBuilder().Build(
            Path.Combine(temp.Path, "Code.exe"),
            Path.Combine(temp.Path, "data"),
            Path.Combine(temp.Path, "extensions"),
            profile,
            workspacePath: null);

        Assert.Equal("dummy-auth", File.ReadAllText(auth));
        Assert.Equal(before, File.GetLastWriteTimeUtc(auth));
    }
}
