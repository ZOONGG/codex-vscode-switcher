using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class FloatingOverlayPlacementServiceTests
{
    private static readonly DisplayWorkArea[] WorkAreas =
    [
        new(@"\\.\DISPLAY2", -1920, 0, 1920, 1080),
        new(@"\\.\DISPLAY1", 0, 0, 2560, 1400, IsPrimary: true),
    ];

    [Fact]
    public void Resolve_RestoresNegativeCoordinatesOnPersistedMonitor()
    {
        var settings = new OverlaySettings
        {
            PositionPreset = PositionPreset.Custom,
            FloatingLeft = -1700,
            FloatingTop = 120,
            FloatingMonitorId = @"\\.\DISPLAY2",
        };

        FloatingOverlayPlacement placement = new FloatingOverlayPlacementService()
            .Resolve(settings, 560, 50, WorkAreas);

        Assert.Equal(-1700, placement.Left);
        Assert.Equal(120, placement.Top);
        Assert.Equal(@"\\.\DISPLAY2", placement.MonitorId);
    }

    [Fact]
    public void Resolve_ClampsOffScreenCoordinatesToNearestVisibleWorkArea()
    {
        var settings = new OverlaySettings
        {
            PositionPreset = PositionPreset.Custom,
            FloatingLeft = -5000,
            FloatingTop = 9000,
            FloatingMonitorId = "removed-monitor",
        };

        FloatingOverlayPlacement placement = new FloatingOverlayPlacementService()
            .Resolve(settings, 560, 50, WorkAreas);

        Assert.Equal(-2416, placement.Left);
        Assert.Equal(1048, placement.Top);
        Assert.Equal(@"\\.\DISPLAY2", placement.MonitorId);
    }

    [Fact]
    public void Reset_ReturnsVisiblePositionOnPrimaryMonitor()
    {
        FloatingOverlayPlacement placement = new FloatingOverlayPlacementService()
            .Reset(560, 50, WorkAreas);

        Assert.Equal(1976, placement.Left);
        Assert.Equal(24, placement.Top);
        Assert.Equal(@"\\.\DISPLAY1", placement.MonitorId);
    }

    [Theory]
    [InlineData(PositionPreset.TopLeft, 24)]
    [InlineData(PositionPreset.TopCenter, 1000)]
    [InlineData(PositionPreset.TopRight, 1976)]
    [InlineData(PositionPreset.AfterMenu, 1976)]
    public void Resolve_AppliesPositionPresetImmediately(PositionPreset preset, double expectedLeft)
    {
        var settings = new OverlaySettings { PositionPreset = preset };

        FloatingOverlayPlacement placement = new FloatingOverlayPlacementService()
            .Resolve(settings, 560, 50, WorkAreas);

        Assert.Equal(expectedLeft, placement.Left);
        Assert.Equal(24, placement.Top);
    }
}
