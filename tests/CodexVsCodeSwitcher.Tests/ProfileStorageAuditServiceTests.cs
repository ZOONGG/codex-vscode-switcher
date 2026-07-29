using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProfileStorageAuditServiceTests
{
    [Fact]
    public void AuditProfiles_ValidatesAllowlistedFilesWithoutReturningCredentialValues()
    {
        using var temp = new TempDirectory();
        string profile = Path.Combine(temp.Path, "work");
        Directory.CreateDirectory(profile);
        const string privateValue = "private-placeholder-value";
        File.WriteAllText(Path.Combine(profile, "auth.json"), $$"""{"credential":"{{privateValue}}"}""");
        File.WriteAllText(Path.Combine(profile, "config.toml"), "credential_store = \"file\"");
        var service = CreateService(temp.Path);

        ProfileStorageAudit audit = Assert.Single(service.AuditProfiles());

        Assert.Equal(ProfileValidationStatus.Valid, audit.Status);
        Assert.True(audit.HasAuthFile);
        Assert.True(audit.HasConfigFile);
        Assert.True(audit.IsEligibleForSwitching);
        Assert.DoesNotContain(privateValue, audit.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void AuditProfiles_ReportsInvalidAndIncompleteProfiles()
    {
        using var temp = new TempDirectory();
        string invalid = Directory.CreateDirectory(Path.Combine(temp.Path, "invalid")).FullName;
        _ = Directory.CreateDirectory(Path.Combine(temp.Path, "incomplete"));
        File.WriteAllText(Path.Combine(invalid, "auth.json"), "{broken");

        IReadOnlyList<ProfileStorageAudit> audits = CreateService(temp.Path).AuditProfiles();

        Assert.Equal(ProfileValidationStatus.Incomplete, audits.Single(item => item.ProfileName == "incomplete").Status);
        Assert.Equal(ProfileValidationStatus.Invalid, audits.Single(item => item.ProfileName == "invalid").Status);
        Assert.All(audits, static audit => Assert.False(audit.IsEligibleForSwitching));
    }

    [Fact]
    public void AuditProfiles_CountsRuntimeDataWithoutReadingOrBackingItUp()
    {
        using var temp = new TempDirectory();
        string profile = Directory.CreateDirectory(Path.Combine(temp.Path, "copied")).FullName;
        File.WriteAllText(Path.Combine(profile, "auth.json"), """{"credential":"placeholder"}""");
        string sessions = Directory.CreateDirectory(Path.Combine(profile, "sessions")).FullName;
        string archived = Directory.CreateDirectory(Path.Combine(profile, "archived_sessions")).FullName;
        File.WriteAllText(Path.Combine(sessions, "session.jsonl"), "runtime-one");
        File.WriteAllText(Path.Combine(archived, "archived.jsonl"), "runtime-two");
        File.WriteAllText(Path.Combine(profile, "rollout-example.jsonl"), "runtime-three");
        File.WriteAllText(Path.Combine(profile, "state.sqlite"), "runtime-four");
        File.WriteAllText(Path.Combine(profile, "notes.txt"), "small-note");

        ProfileStorageAudit audit = Assert.Single(CreateService(temp.Path).AuditProfiles());

        Assert.Equal(4, audit.IgnoredRuntimeFileCount);
        Assert.True(audit.DirectorySizeBytes > 0);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "backups")));
    }

    [Fact]
    public void FutureCleanupPlan_CannotExecuteAndRequiresConfirmation()
    {
        using var temp = new TempDirectory();
        string profile = Directory.CreateDirectory(Path.Combine(temp.Path, "copied")).FullName;
        File.WriteAllText(Path.Combine(profile, "auth.json"), """{"credential":"placeholder"}""");
        string sessions = Directory.CreateDirectory(Path.Combine(profile, "sessions")).FullName;
        File.WriteAllText(Path.Combine(sessions, "session.jsonl"), "runtime");

        ProfileCleanupPlan plan = CreateService(temp.Path).CreateFutureCleanupPlan("copied");

        Assert.True(plan.RequiresExplicitConfirmation);
        Assert.False(plan.CanExecute);
        Assert.Equal(1, plan.RuntimeFileCount);
        Assert.True(File.Exists(Path.Combine(sessions, "session.jsonl")));
    }

    private static ProfileStorageAuditService CreateService(string profilesRoot)
        => new(
            profilesRoot,
            new ProtectedPathPolicy([Path.Combine(profilesRoot, "protected")]));
}
