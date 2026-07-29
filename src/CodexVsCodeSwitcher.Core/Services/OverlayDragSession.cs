namespace CodexVsCodeSwitcher.Core.Services;

public enum OverlayPointerRegion
{
    Background,
    InteractiveControl,
    ProfileItem,
}

public sealed class OverlayDragSession
{
    private const double DragThreshold = 3;
    private double pointerStartX;
    private double pointerStartY;
    private double windowStartLeft;
    private double windowStartTop;

    public bool IsCaptured { get; private set; }

    public bool HasMoved { get; private set; }

    public bool TryBegin(
        bool isPrimaryButton,
        OverlayPointerRegion region,
        double pointerX,
        double pointerY,
        double windowLeft,
        double windowTop)
    {
        Cancel();
        if (!isPrimaryButton || region != OverlayPointerRegion.Background)
        {
            return false;
        }

        pointerStartX = pointerX;
        pointerStartY = pointerY;
        windowStartLeft = windowLeft;
        windowStartTop = windowTop;
        IsCaptured = true;
        return true;
    }

    public OverlayDragMove? Move(double pointerX, double pointerY)
    {
        if (!IsCaptured)
        {
            return null;
        }

        double deltaX = pointerX - pointerStartX;
        double deltaY = pointerY - pointerStartY;
        if (!HasMoved && Math.Abs(deltaX) < DragThreshold && Math.Abs(deltaY) < DragThreshold)
        {
            return null;
        }

        HasMoved = true;
        return new OverlayDragMove(windowStartLeft + deltaX, windowStartTop + deltaY);
    }

    public bool End()
    {
        bool moved = HasMoved;
        Cancel();
        return moved;
    }

    public void Cancel()
    {
        IsCaptured = false;
        HasMoved = false;
    }
}

public sealed record OverlayDragMove(double Left, double Top);
