using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProfileDiscoveryServiceTests
{
    [Fact]
    public void DiscoverProfiles_ReturnsValidInvalidAndIncompleteStatuses()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "alpha"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "beta"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "gamma"));
        File.WriteAllText(Path.Combine(temp.Path, "alpha", "auth.json"), """{"credential":"placeholder"}""");
        File.WriteAllText(Path.Combine(temp.Path, "gamma", "auth.json"), "not-json");

        var policy = new ProtectedPathPolicy([Path.Combine(temp.Path, "protected")]);
        var service = new ProfileDiscoveryService(temp.Path, policy);

        var profiles = service.DiscoverProfiles();

        Assert.Equal<string[]>(["alpha", "beta", "gamma"], profiles.Select(profile => profile.Name).ToArray());
        Assert.Equal(ProfileValidationStatus.Valid, profiles[0].ValidationStatus);
        Assert.Equal(ProfileValidationStatus.Incomplete, profiles[1].ValidationStatus);
        Assert.Equal(ProfileValidationStatus.Invalid, profiles[2].ValidationStatus);
    }
}
