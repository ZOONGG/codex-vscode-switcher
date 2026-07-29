using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ManualOverlayRevealGuidanceTests
{
    [Fact]
    public void DirectsFirstLaunchToProfileSelection()
    {
        string? key = ManualOverlayRevealGuidance.ResolveMessageKey(
            showOnlyWithManagedVsCode: true,
            overlayVisible: false,
            hasManagedWindow: false,
            hasActiveProfile: false);

        Assert.Equal("SelectProfileToShowOverlay", key);
    }

    [Fact]
    public void DirectsConfiguredUserToManagedLaunch()
    {
        string? key = ManualOverlayRevealGuidance.ResolveMessageKey(
            showOnlyWithManagedVsCode: true,
            overlayVisible: false,
            hasManagedWindow: false,
            hasActiveProfile: true);

        Assert.Equal("LaunchManagedVsCodeToShowOverlay", key);
    }

    [Fact]
    public void DirectsRunningManagedWindowToForeground()
    {
        string? key = ManualOverlayRevealGuidance.ResolveMessageKey(
            showOnlyWithManagedVsCode: true,
            overlayVisible: false,
            hasManagedWindow: true,
            hasActiveProfile: true);

        Assert.Equal("FocusManagedVsCodeToShowOverlay", key);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void DoesNotShowGuidanceWhenRevealIsAllowed(
        bool showOnlyWithManagedVsCode,
        bool overlayVisible)
    {
        string? key = ManualOverlayRevealGuidance.ResolveMessageKey(
            showOnlyWithManagedVsCode,
            overlayVisible,
            hasManagedWindow: false,
            hasActiveProfile: false);

        Assert.Null(key);
    }
}
