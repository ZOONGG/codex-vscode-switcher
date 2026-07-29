using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ManagedOverlayVisibilityPolicyTests
{
    private readonly ManagedOverlayVisibilityPolicy policy = new();

    [Fact]
    public void ShowsForManagedForegroundWindow()
        => Assert.True(policy.ShouldShow(Input(managedForeground: true)));

    [Fact]
    public void RemainsVisibleWhileOverlayIsForeground()
        => Assert.True(policy.ShouldShow(Input(overlayForeground: true)));

    [Fact]
    public void HidesForChatGptBrowserExplorerAndUnmanagedVsCodeForeground()
        => Assert.False(policy.ShouldShow(Input()));

    [Fact]
    public void HidesWhenManagedWindowIsMinimized()
        => Assert.False(policy.ShouldShow(Input(managedForeground: true, minimized: true)));

    [Fact]
    public void RestoresAfterManagedWindowIsRestoredAndFocused()
    {
        Assert.False(policy.ShouldShow(Input(managedForeground: true, minimized: true)));
        Assert.True(policy.ShouldShow(Input(managedForeground: true, minimized: false)));
    }

    [Fact]
    public void HidesWhenManagedWindowIsDestroyed()
        => Assert.False(policy.ShouldShow(Input(hasWindow: false)));

    [Fact]
    public void HidesOnLockOrSecureDesktop()
        => Assert.False(policy.ShouldShow(Input(managedForeground: true, inputDesktop: false)));

    [Fact]
    public void ManualFloatingModeCanBeShownWithoutManagedWindow()
        => Assert.True(policy.ShouldShow(Input(
            hasWindow: false,
            showOnlyManaged: false)));

    [Fact]
    public void ManualHideWinsOverForegroundState()
        => Assert.False(policy.ShouldShow(Input(
            managedForeground: true,
            manualRequested: false)));

    private static ManagedOverlayVisibilityInput Input(
        bool hasWindow = true,
        bool managedForeground = false,
        bool overlayForeground = false,
        bool minimized = false,
        bool inputDesktop = true,
        bool showOnlyManaged = true,
        bool manualRequested = true)
        => new(
            hasWindow,
            ManagedWindowVisible: hasWindow,
            ManagedWindowMinimized: minimized,
            ManagedWindowIsForeground: managedForeground,
            OverlayInteractionIsForeground: overlayForeground,
            InputDesktopAvailable: inputDesktop,
            ShowOnlyWithManagedVsCode: showOnlyManaged,
            ManualVisibilityRequested: manualRequested);
}
