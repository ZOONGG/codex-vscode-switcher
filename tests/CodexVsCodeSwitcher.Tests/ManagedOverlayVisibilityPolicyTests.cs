using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class ManagedOverlayVisibilityPolicyTests
{
    private readonly ManagedOverlayVisibilityPolicy policy = new();

    [Fact]
    public void ShowsForManagedForegroundWindow()
        => Assert.True(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.ManagedVsCodeForeground)));

    [Fact]
    public void RemainsVisibleWhileOverlayIsForeground()
        => Assert.True(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.OverlayInteraction)));

    [Fact]
    public void HidesForChatGptBrowserExplorerAndUnmanagedVsCodeForeground()
        => Assert.False(policy.ShouldShow(Input()));

    [Fact]
    public void HidesWhenManagedWindowIsMinimized()
        => Assert.False(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.ManagedVsCodeForeground,
            minimized: true)));

    [Fact]
    public void RestoresAfterManagedWindowIsRestoredAndFocused()
    {
        Assert.False(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.ManagedVsCodeForeground,
            minimized: true)));
        Assert.True(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.ManagedVsCodeForeground,
            minimized: false)));
    }

    [Fact]
    public void HidesWhenManagedWindowIsDestroyed()
        => Assert.False(policy.ShouldShow(Input(hasWindow: false)));

    [Fact]
    public void HidesOnLockOrSecureDesktop()
        => Assert.False(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.SettingsPreview,
            inputDesktop: false)));

    [Fact]
    public void ManualFloatingModeCanBeShownWithoutManagedWindow()
        => Assert.True(policy.ShouldShow(Input(
            hasWindow: false,
            showOnlyManaged: false)));

    [Fact]
    public void ManualHideWinsOverForegroundState()
        => Assert.False(policy.ShouldShow(Input(
            reasons: OverlayVisibilityReason.ManagedVsCodeForeground,
            manualRequested: false)));

    [Fact]
    public void SettingsPreviewShowsWithoutManagedWindow()
        => Assert.True(policy.ShouldShow(Input(
            hasWindow: false,
            reasons: OverlayVisibilityReason.SettingsPreview,
            manualRequested: false)));

    [Fact]
    public void ClosingSettingsPreviewRestoresManagedOnlyVisibility()
    {
        Assert.True(policy.ShouldShow(Input(
            hasWindow: false,
            reasons: OverlayVisibilityReason.SettingsPreview)));
        Assert.False(policy.ShouldShow(Input(
            hasWindow: false,
            reasons: OverlayVisibilityReason.None)));
    }

    [Fact]
    public void ProfileSwitchingStatusShowsDuringManagedWindowRestart()
        => Assert.True(policy.ShouldShow(Input(
            hasWindow: false,
            reasons: OverlayVisibilityReason.ProfileSwitchingStatus)));

    private static ManagedOverlayVisibilityInput Input(
        bool hasWindow = true,
        OverlayVisibilityReason reasons = OverlayVisibilityReason.None,
        bool minimized = false,
        bool inputDesktop = true,
        bool showOnlyManaged = true,
        bool manualRequested = true)
        => new(
            hasWindow,
            ManagedWindowVisible: hasWindow,
            ManagedWindowMinimized: minimized,
            VisibilityReasons: reasons,
            InputDesktopAvailable: inputDesktop,
            ShowOnlyWithManagedVsCode: showOnlyManaged,
            ManualVisibilityRequested: manualRequested);
}
