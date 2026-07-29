namespace CodexVsCodeSwitcher.Core.Services;

public static class ManualOverlayRevealGuidance
{
    public static string? ResolveMessageKey(
        bool showOnlyWithManagedVsCode,
        bool overlayVisible,
        bool hasManagedWindow,
        bool hasActiveProfile)
    {
        if (!showOnlyWithManagedVsCode || overlayVisible)
        {
            return null;
        }

        if (hasManagedWindow)
        {
            return "FocusManagedVsCodeToShowOverlay";
        }

        return hasActiveProfile
            ? "LaunchManagedVsCodeToShowOverlay"
            : "SelectProfileToShowOverlay";
    }
}
