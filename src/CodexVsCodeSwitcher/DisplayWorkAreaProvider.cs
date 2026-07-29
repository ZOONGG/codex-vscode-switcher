using CodexVsCodeSwitcher.Core.Services;
using Screen = System.Windows.Forms.Screen;

namespace CodexVsCodeSwitcher;

internal static class DisplayWorkAreaProvider
{
    public static IReadOnlyList<DisplayWorkArea> GetAll()
        => Screen.AllScreens
            .Select(static screen => new DisplayWorkArea(
                screen.DeviceName,
                screen.WorkingArea.Left,
                screen.WorkingArea.Top,
                screen.WorkingArea.Width,
                screen.WorkingArea.Height,
                screen.Primary))
            .ToArray();
}
