using CodexProfileOverlay.Core.Models;
using CodexProfileOverlay.Core.Services;

namespace CodexProfileOverlay.Tests;

public sealed class ProfileStatusServiceTests
{
    private static ProfileStatusService CreateService(TempDirectory temp)
    {
        var store = new ProfileStatusStore(Path.Combine(temp.Path, "status.json"));
        var logger = new SafeLogger(temp.Path);
        return new ProfileStatusService(store, new FakeUsageProvider(), logger);
    }

    [Fact]
    public async Task RefreshUsageAsync_SavesSnapshotWhenSupported()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var document = new ProfileStatusDocument();

        await service.RefreshUsageAsync("test-profile", @"C:\.codex-profiles\test-profile", document, CancellationToken.None);

        var snapshot = service.GetCachedSnapshot(document, "test-profile");
        Assert.NotNull(snapshot);
        Assert.Equal(65, snapshot!.ShortWindowRemainingPercent);
    }

    [Fact]
    public void GetLimitIndicator_ReturnsCorrectEmoji()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);

        var highSnapshot = new UsageSnapshot { ShortWindowRemainingPercent = 75, IsStale = false };
        Assert.Equal("🟢", service.GetLimitIndicator(highSnapshot, 60, 25));

        var mediumSnapshot = new UsageSnapshot { ShortWindowRemainingPercent = 50, IsStale = false };
        Assert.Equal("🟡", service.GetLimitIndicator(mediumSnapshot, 60, 25));

        var lowSnapshot = new UsageSnapshot { ShortWindowRemainingPercent = 15, IsStale = false };
        Assert.Equal("🔴", service.GetLimitIndicator(lowSnapshot, 60, 25));

        var nullPercent = new UsageSnapshot { IsStale = false };
        Assert.Equal(string.Empty, service.GetLimitIndicator(nullPercent, 60, 25));
    }

    [Fact]
    public void GetLimitIndicator_ReturnsEmptyForStale()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var staleSnapshot = new UsageSnapshot { ShortWindowRemainingPercent = 75, IsStale = true };

        Assert.Equal(string.Empty, service.GetLimitIndicator(staleSnapshot, 60, 25));
    }

    [Fact]
    public void FindRecommendedProfile_FindsBestProfile()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var document = new ProfileStatusDocument
        {
            Snapshots =
            {
                ["profile-a"] = new UsageSnapshot { ShortWindowRemainingPercent = 80, IsStale = false },
                ["profile-b"] = new UsageSnapshot { ShortWindowRemainingPercent = 65, IsStale = false },
                ["profile-c"] = new UsageSnapshot { ShortWindowRemainingPercent = 40, IsStale = false },
            },
        };

        var recommended = service.FindRecommendedProfile(
            new[] { "profile-a", "profile-b", "profile-c" },
            document,
            60,
            25);

        Assert.Equal("profile-c", recommended); // Lowest percent wins
    }

    [Fact]
    public void FindRecommendedProfile_ReturnsNullWhenLessThanTwoProfiles()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var document = new ProfileStatusDocument
        {
            Snapshots =
            {
                ["profile-a"] = new UsageSnapshot { ShortWindowRemainingPercent = 80, IsStale = false },
            },
        };

        var recommended = service.FindRecommendedProfile(
            new[] { "profile-a" },
            document,
            60,
            25);

        Assert.Null(recommended);
    }

    [Fact]
    public void FindRecommendedProfile_RanksExhaustedLast()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var document = new ProfileStatusDocument
        {
            Snapshots =
            {
                ["profile-a"] = new UsageSnapshot { ShortWindowRemainingPercent = 15, IsExhausted = true, IsStale = false },
                ["profile-b"] = new UsageSnapshot { ShortWindowRemainingPercent = 50, IsStale = false },
            },
        };

        var recommended = service.FindRecommendedProfile(
            new[] { "profile-a", "profile-b" },
            document,
            60,
            25);

        Assert.Equal("profile-b", recommended);
    }

    [Fact]
    public void FindRecommendedProfile_ReturnsNullIfOnlyProfileIsExhausted()
    {
        using var temp = new TempDirectory();
        var service = CreateService(temp);
        var document = new ProfileStatusDocument
        {
            Snapshots =
            {
                ["profile-a"] = new UsageSnapshot { ShortWindowRemainingPercent = 10, IsExhausted = true, IsStale = false },
                ["profile-b"] = new UsageSnapshot { ShortWindowRemainingPercent = 50, IsStale = false },
                ["profile-c"] = new UsageSnapshot { ShortWindowRemainingPercent = 40, IsExhausted = true, IsStale = false },
            },
        };

        var recommended = service.FindRecommendedProfile(
            new[] { "profile-a", "profile-b", "profile-c" },
            document,
            60,
            25);

        Assert.Equal("profile-b", recommended);
    }

    [Fact]
    public void UnavailableUsageProvider_ReturnsNull()
    {
        using var temp = new TempDirectory();
        var service = new ProfileStatusService(
            new ProfileStatusStore(Path.Combine(temp.Path, "status.json")),
            new UnavailableUsageProvider(),
            new SafeLogger(temp.Path));
        var document = new ProfileStatusDocument();

        var recommended = service.FindRecommendedProfile(
            new[] { "profile-a" },
            document,
            60,
            25);

        Assert.Null(recommended);
    }

    private sealed class FakeUsageProvider : IUsageProvider
    {
        public UsageProviderCapability Capability => UsageProviderCapability.Supported;

        public Task<UsageSnapshot?> GetUsageAsync(string profileDirectory, CancellationToken cancellationToken)
        {
            return Task.FromResult<UsageSnapshot?>(new UsageSnapshot
            {
                ShortWindowRemainingPercent = 65,
                LongWindowRemainingPercent = 55,
                CapturedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}