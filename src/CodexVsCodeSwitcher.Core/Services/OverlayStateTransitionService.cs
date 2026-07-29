using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class OverlayStateTransitionService
{
    public bool CanSetDisplayMode(OverlayDisplayMode mode)
        => Enum.IsDefined(mode);

    public bool SetDisplayMode(OverlaySettings settings, OverlayDisplayMode mode)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!CanSetDisplayMode(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (settings.DisplayMode == mode)
        {
            return false;
        }

        settings.DisplayMode = mode;
        return true;
    }
}

public static class OverlayWindowStylePolicy
{
    public const long ExtendedTransparent = 0x00000020L;
    public const long ExtendedToolWindow = 0x00000080L;
    public const long ExtendedAppWindow = 0x00040000L;
    public const long ExtendedNoActivate = 0x08000000L;

    public static long ReturnToInteractive(long extendedStyle)
    {
        extendedStyle |= ExtendedToolWindow | ExtendedNoActivate;
        extendedStyle &= ~(ExtendedAppWindow | ExtendedTransparent);
        return extendedStyle;
    }

    public static bool IsClickThrough(long extendedStyle)
        => (extendedStyle & ExtendedTransparent) != 0;
}
