using CodexVsCodeSwitcher.Core.Models;

namespace CodexVsCodeSwitcher.Core.Services;

public sealed class FloatingOverlayPlacementService
{
    public const double MinimumVisibleWidth = 64;
    public const double MinimumVisibleHeight = 32;
    private const double DefaultMargin = 24;

    public FloatingOverlayPlacement Resolve(
        OverlaySettings settings,
        double overlayWidth,
        double overlayHeight,
        IReadOnlyList<DisplayWorkArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(settings);
        DisplayWorkArea monitor = SelectMonitor(
            settings.FloatingMonitorId,
            settings.FloatingLeft,
            settings.FloatingTop,
            workAreas);

        (double left, double top) = settings.PositionPreset switch
        {
            PositionPreset.TopLeft => (monitor.Left + DefaultMargin, monitor.Top + DefaultMargin),
            PositionPreset.TopCenter => (monitor.Left + ((monitor.Width - overlayWidth) / 2), monitor.Top + DefaultMargin),
            PositionPreset.TopRight or PositionPreset.AfterMenu => (
                monitor.Right - overlayWidth - DefaultMargin,
                monitor.Top + DefaultMargin),
            PositionPreset.Custom when IsFinite(settings.FloatingLeft) && IsFinite(settings.FloatingTop) => (
                settings.FloatingLeft!.Value,
                settings.FloatingTop!.Value),
            _ => (monitor.Right - overlayWidth - DefaultMargin, monitor.Top + DefaultMargin),
        };

        return Clamp(left, top, overlayWidth, overlayHeight, monitor);
    }

    public FloatingOverlayPlacement Clamp(
        double left,
        double top,
        double overlayWidth,
        double overlayHeight,
        DisplayWorkArea workArea)
    {
        double width = NormalizeSize(overlayWidth, MinimumVisibleWidth);
        double height = NormalizeSize(overlayHeight, MinimumVisibleHeight);
        double minimumLeft = workArea.Left - Math.Max(0, width - MinimumVisibleWidth);
        double maximumLeft = workArea.Right - Math.Min(MinimumVisibleWidth, width);
        double minimumTop = workArea.Top;
        double maximumTop = workArea.Bottom - Math.Min(MinimumVisibleHeight, height);
        return new FloatingOverlayPlacement(
            OverlayLayoutService.Clamp(left, minimumLeft, Math.Max(minimumLeft, maximumLeft)),
            OverlayLayoutService.Clamp(top, minimumTop, Math.Max(minimumTop, maximumTop)),
            workArea.Id);
    }

    public FloatingOverlayPlacement Reset(
        double overlayWidth,
        double overlayHeight,
        IReadOnlyList<DisplayWorkArea> workAreas)
    {
        DisplayWorkArea primary = RequireWorkAreas(workAreas)
            .FirstOrDefault(static area => area.IsPrimary)
            ?? workAreas[0];
        return Clamp(
            primary.Right - NormalizeSize(overlayWidth, MinimumVisibleWidth) - DefaultMargin,
            primary.Top + DefaultMargin,
            overlayWidth,
            overlayHeight,
            primary);
    }

    public DisplayWorkArea SelectNearest(
        double left,
        double top,
        IReadOnlyList<DisplayWorkArea> workAreas)
    {
        IReadOnlyList<DisplayWorkArea> areas = RequireWorkAreas(workAreas);
        return areas
            .OrderBy(area => SquaredDistanceToArea(left, top, area))
            .First();
    }

    private DisplayWorkArea SelectMonitor(
        string monitorId,
        double? left,
        double? top,
        IReadOnlyList<DisplayWorkArea> workAreas)
    {
        IReadOnlyList<DisplayWorkArea> areas = RequireWorkAreas(workAreas);
        DisplayWorkArea? persisted = areas.FirstOrDefault(
            area => area.Id.Equals(monitorId, StringComparison.OrdinalIgnoreCase));
        if (persisted is not null)
        {
            return persisted;
        }

        if (IsFinite(left) && IsFinite(top))
        {
            return SelectNearest(left!.Value, top!.Value, areas);
        }

        return areas.FirstOrDefault(static area => area.IsPrimary) ?? areas[0];
    }

    private static IReadOnlyList<DisplayWorkArea> RequireWorkAreas(IReadOnlyList<DisplayWorkArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0)
        {
            throw new ArgumentException("At least one display work area is required.", nameof(workAreas));
        }

        return workAreas;
    }

    private static double SquaredDistanceToArea(double x, double y, DisplayWorkArea area)
    {
        double nearestX = Math.Clamp(x, area.Left, area.Right);
        double nearestY = Math.Clamp(y, area.Top, area.Bottom);
        double deltaX = x - nearestX;
        double deltaY = y - nearestY;
        return (deltaX * deltaX) + (deltaY * deltaY);
    }

    private static double NormalizeSize(double value, double fallback)
        => double.IsFinite(value) && value > 0 ? value : fallback;

    private static bool IsFinite(double? value)
        => value is not null && double.IsFinite(value.Value);
}

public sealed record DisplayWorkArea(
    string Id,
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsPrimary = false)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;
}

public sealed record FloatingOverlayPlacement(double Left, double Top, string MonitorId);
