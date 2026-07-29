using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ProfileDiscoveryServiceTests
{
    [Fact]
    public void DiscoverProfiles_ReturnsOnlyDirectoriesWithAuthJson()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "alpha"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "beta"));
        Directory.CreateDirectory(Path.Combine(temp.Path, "gamma"));
        File.WriteAllText(Path.Combine(temp.Path, "alpha", "auth.json"), "dummy");
        File.WriteAllText(Path.Combine(temp.Path, "gamma", "auth.json"), "dummy");

        var policy = new ProtectedPathPolicy([Path.Combine(temp.Path, "protected")]);
        var service = new ProfileDiscoveryService(temp.Path, policy);

        var profiles = service.DiscoverProfiles();

        Assert.Equal<string[]>(["alpha", "gamma"], profiles.Select(profile => profile.Name).ToArray());
    }
}
