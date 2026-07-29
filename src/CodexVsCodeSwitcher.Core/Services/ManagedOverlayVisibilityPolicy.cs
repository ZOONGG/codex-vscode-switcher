namespace CodexVsCodeSwitcher.Core.Services;

public sealed record ManagedOverlayVisibilityInput(
    bool HasValidManagedWindow,
    bool ManagedWindowVisible,
    bool ManagedWindowMinimized,
    bool ManagedWindowIsForeground,
    bool OverlayInteractionIsForeground,
    bool InputDesktopAvailable,
    bool ShowOnlyWithManagedVsCode,
    bool ManualVisibilityRequested);

public sealed class ManagedOverlayVisibilityPolicy
{
    public bool ShouldShow(ManagedOverlayVisibilityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.InputDesktopAvailable || !input.ManualVisibilityRequested)
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
            && (input.ManagedWindowIsForeground || input.OverlayInteractionIsForeground);
    }
}
