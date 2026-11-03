namespace GlassDock.Core.Desktop;

public static class UtilityPlacement
{
    public static HomeCardRect Beside(HomeCardRect dock, double gap, int index = 0) =>
        new(dock.X + dock.Width + gap + index * (dock.Height + gap), dock.Y, dock.Height, dock.Height);

    public static HomeCardRect WithinRow(HomeCardRect circle, double left, double minimumLeft, double maximumRight) =>
        new(Math.Clamp(left, minimumLeft, Math.Max(minimumLeft, maximumRight - circle.Width)), circle.Y, circle.Width, circle.Height);
}
