namespace WinQuickSwitch.Features.Widget;

internal readonly record struct ScreenPoint(int X, int Y);

internal readonly record struct ScreenRectangle(
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

internal static class WidgetPlacementCalculator
{
    public static ScreenPoint PlaceNearPointer(
        ScreenPoint pointer,
        ScreenRectangle workArea,
        int widgetWidth,
        int widgetHeight,
        int gap = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widgetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widgetHeight);

        int left = pointer.X + gap;
        int top = pointer.Y + gap;

        if (left + widgetWidth > workArea.Right)
        {
            left = pointer.X - widgetWidth - gap;
        }

        if (top + widgetHeight > workArea.Bottom)
        {
            top = pointer.Y - widgetHeight - gap;
        }

        int maximumLeft = Math.Max(workArea.Left, workArea.Right - widgetWidth);
        int maximumTop = Math.Max(workArea.Top, workArea.Bottom - widgetHeight);

        return new ScreenPoint(
            Math.Clamp(left, workArea.Left, maximumLeft),
            Math.Clamp(top, workArea.Top, maximumTop));
    }

    public static ScreenPoint ClampToWorkArea(
        ScreenPoint position,
        ScreenRectangle workArea,
        int widgetWidth,
        int widgetHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widgetWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widgetHeight);

        int maximumLeft = Math.Max(workArea.Left, workArea.Right - widgetWidth);
        int maximumTop = Math.Max(workArea.Top, workArea.Bottom - widgetHeight);

        return new ScreenPoint(
            Math.Clamp(position.X, workArea.Left, maximumLeft),
            Math.Clamp(position.Y, workArea.Top, maximumTop));
    }
}
