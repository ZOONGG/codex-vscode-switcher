namespace CodexVsCodeSwitcher.Core.Services;

public sealed record ManagedOverlayVisibilityInput(
    bool HasValidManagedWindow,
    bool ManagedWindowVisible,
    bool ManagedWindowMinimized,
    OverlayVisibilityReason VisibilityReasons,
    bool InputDesktopAvailable,
    bool ShowOnlyWithManagedVsCode,
    bool ManualVisibilityRequested);

public sealed class ManagedOverlayVisibilityPolicy
{
    public bool ShouldShow(ManagedOverlayVisibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.InputDesktopAvailable)
        {
            return false;
        }

        if ((input.VisibilityReasons
            & (OverlayVisibilityReason.SettingsPreview
                | OverlayVisibilityReason.ProfileSwitchingStatus)) != 0)
        {
            return true;
        }

        if (!input.ManualVisibilityRequested)
        {
            return false;
        }

        if (!input.ShowOnlyWithManagedVsCode)
        {
            return true;
        }

        return input.HasValidManagedWindow
            && input.ManagedWindowVisible
            && !input.ManagedWindowMinimized
            && (input.VisibilityReasons
                & (OverlayVisibilityReason.ManagedVsCodeForeground
                    | OverlayVisibilityReason.OverlayInteraction)) != 0;
    }
}
