using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class OverlayVisibilityLeaseManagerTests
{
    [Fact]
    public void SettingsPreviewLeaseHasExplicitLifecycle()
    {
        var manager = new OverlayVisibilityLeaseManager();

        IDisposable preview = manager.Acquire(OverlayVisibilityReason.SettingsPreview);

        Assert.Equal(OverlayVisibilityReason.SettingsPreview, manager.ActiveReasons);

        preview.Dispose();

        Assert.Equal(OverlayVisibilityReason.None, manager.ActiveReasons);
    }

    [Fact]
    public void IndependentReasonsRemainUntilTheirOwnLeaseEnds()
    {
        var manager = new OverlayVisibilityLeaseManager();
        using IDisposable preview = manager.Acquire(OverlayVisibilityReason.SettingsPreview);
        IDisposable switching = manager.Acquire(OverlayVisibilityReason.ProfileSwitchingStatus);

        Assert.Equal(
            OverlayVisibilityReason.SettingsPreview | OverlayVisibilityReason.ProfileSwitchingStatus,
            manager.ActiveReasons);

        switching.Dispose();

        Assert.Equal(OverlayVisibilityReason.SettingsPreview, manager.ActiveReasons);
    }

    [Fact]
    public void DuplicateLeaseDisposalIsIdempotent()
    {
        var manager = new OverlayVisibilityLeaseManager();
        IDisposable first = manager.Acquire(OverlayVisibilityReason.SettingsPreview);
        using IDisposable second = manager.Acquire(OverlayVisibilityReason.SettingsPreview);

        first.Dispose();
        first.Dispose();

        Assert.Equal(OverlayVisibilityReason.SettingsPreview, manager.ActiveReasons);
    }

    [Fact]
    public void CombinedReasonCannotBeAcquiredAsOneLease()
    {
        var manager = new OverlayVisibilityLeaseManager();

        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Acquire(
            OverlayVisibilityReason.SettingsPreview | OverlayVisibilityReason.ProfileSwitchingStatus));
    }
}
