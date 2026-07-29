using CodexVsCodeSwitcher.Core.Models;
using CodexVsCodeSwitcher.Core.Services;

namespace CodexVsCodeSwitcher.Tests;

public sealed class OverlayStateTransitionServiceTests
{
    [Theory]
    [InlineData(OverlayDisplayMode.Auto)]
    [InlineData(OverlayDisplayMode.Compact)]
    [InlineData(OverlayDisplayMode.Expanded)]
    public void EveryDisplayMode_CanExecuteAndUpdatesState(OverlayDisplayMode mode)
    {
        var service = new OverlayStateTransitionService();
        var settings = new OverlaySettings
        {
            DisplayMode = mode == OverlayDisplayMode.Expanded
                ? OverlayDisplayMode.Compact
                : OverlayDisplayMode.Expanded,
        };

        Assert.True(service.CanSetDisplayMode(mode));
        Assert.True(service.SetDisplayMode(settings, mode));
        Assert.Equal(mode, settings.DisplayMode);
    }

    [Fact]
    public void DisplayModeChange_PersistsWithoutTouchingAuthOrProcessMarkers()
    {
        using var temp = new TempDirectory();
        string settingsFile = Path.Combine(temp.Path, "owned", "settings.json");
        string protectedAuthMarker = Path.Combine(temp.Path, "main-auth-marker.json");
        string vsCodeProcessMarker = Path.Combine(temp.Path, "vscode-process-marker.txt");
        string chatGptProcessMarker = Path.Combine(temp.Path, "chatgpt-process-marker.txt");
        File.WriteAllText(protectedAuthMarker, "unchanged-auth");
        File.WriteAllText(vsCodeProcessMarker, "running");
        File.WriteAllText(chatGptProcessMarker, "running");
        var settings = new OverlaySettings();

        _ = new OverlayStateTransitionService().SetDisplayMode(settings, OverlayDisplayMode.Compact);
        new SettingsService(settingsFile).Save(settings);

        Assert.Equal("unchanged-auth", File.ReadAllText(protectedAuthMarker));
        Assert.Equal("running", File.ReadAllText(vsCodeProcessMarker));
        Assert.Equal("running", File.ReadAllText(chatGptProcessMarker));
        Assert.Equal(OverlayDisplayMode.Compact, new SettingsService(settingsFile).Load().DisplayMode);
    }

    [Fact]
    public void InteractiveStyle_RemovesClickThroughAndCanBeRecovered()
    {
        long clickThrough = OverlayWindowStylePolicy.ExtendedTransparent
            | OverlayWindowStylePolicy.ExtendedAppWindow;

        long interactive = OverlayWindowStylePolicy.ReturnToInteractive(clickThrough);

        Assert.False(OverlayWindowStylePolicy.IsClickThrough(interactive));
        Assert.NotEqual(0, interactive & OverlayWindowStylePolicy.ExtendedToolWindow);
        Assert.NotEqual(0, interactive & OverlayWindowStylePolicy.ExtendedNoActivate);
        Assert.Equal(0, interactive & OverlayWindowStylePolicy.ExtendedAppWindow);
    }
}
