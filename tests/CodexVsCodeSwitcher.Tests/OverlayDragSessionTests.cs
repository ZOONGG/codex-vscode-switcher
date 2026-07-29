using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class OverlayDragSessionTests
{
    [Fact]
    public void BackgroundPrimaryButton_StartsWithoutJumping()
    {
        var session = new OverlayDragSession();

        Assert.True(session.TryBegin(true, OverlayPointerRegion.Background, -1200, 100, -1500, 80));
        Assert.Null(session.Move(-1198, 102));

        OverlayDragMove move = Assert.IsType<OverlayDragMove>(session.Move(-1100, 160));
        Assert.Equal(-1400, move.Left);
        Assert.Equal(140, move.Top);
    }

    [Theory]
    [InlineData(OverlayPointerRegion.InteractiveControl)]
    [InlineData(OverlayPointerRegion.ProfileItem)]
    public void InteractiveRegions_DoNotStartWindowDragging(OverlayPointerRegion region)
    {
        var session = new OverlayDragSession();

        Assert.False(session.TryBegin(true, region, 10, 10, 100, 100));
        Assert.False(session.IsCaptured);
        Assert.Null(session.Move(50, 50));
    }

    [Fact]
    public void LostCapture_CancelsDragSafely()
    {
        var session = new OverlayDragSession();
        Assert.True(session.TryBegin(true, OverlayPointerRegion.Background, 10, 10, 100, 100));

        session.Cancel();

        Assert.False(session.IsCaptured);
        Assert.Null(session.Move(40, 40));
    }
}
