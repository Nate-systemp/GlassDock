namespace GlassDock.Core.Applications;

public readonly record struct DockDragBounds(double Left, double Top, double Right, double Bottom)
{
    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

public enum DockPointerMode { Outside, Stack, Reorder }
public readonly record struct DockPointerTarget(DockPointerMode Mode, int Index)
{
    public static DockPointerTarget ResolveReorder(double x, double y, IReadOnlyList<DockDragBounds> bounds, int source)
    {
        var none = new DockPointerTarget(DockPointerMode.Outside, -1);
        if (!double.IsFinite(x) || !double.IsFinite(y) || bounds.Count == 0) return none;
        if (x < bounds[0].Left - 18 || x > bounds[^1].Right + 18 ||
            y < bounds.Min(b => b.Top) || y > bounds.Max(b => b.Bottom)) return none;
        var insertion = 0;
        for (var i = 0; i < bounds.Count; i++)
            if (i != source && x > (bounds[i].Left + bounds[i].Right) / 2) insertion++;
        return new(DockPointerMode.Reorder, insertion);
    }
    public static DockPointerTarget Resolve(double x, double y, IReadOnlyList<DockDragBounds> bounds,
        int source, Func<int, bool> canStack, int previousStack = -1)
    {
        var none = new DockPointerTarget(DockPointerMode.Outside, -1);
        if (!double.IsFinite(x) || !double.IsFinite(y) || bounds.Count == 0) return none;
        // Retain a target only within its real hitbox (including overlapping
        // magnification bounds). Never extend stack hysteresis into an empty gap.
        if (previousStack >= 0 && previousStack < bounds.Count && previousStack != source &&
            bounds[previousStack].Contains(x, y) && canStack(previousStack))
            return new(DockPointerMode.Stack, previousStack);
        for (var i = 0; i < bounds.Count; i++)
            if (bounds[i].Contains(x, y))
                return i != source && canStack(i) ? new(DockPointerMode.Stack, i) : none;

        for (var i = 0; i < bounds.Count - 1; i++)
        {
            var a = bounds[i]; var b = bounds[i + 1];
            if (x > a.Right && x < b.Left && y >= Math.Max(a.Top, b.Top) && y <= Math.Min(a.Bottom, b.Bottom))
                return new(DockPointerMode.Reorder, i + (source > i ? 1 : 0));
        }
        // Bounded end gaps, not infinite horizontal slots.
        var first = bounds[0]; var last = bounds[^1];
        const double endGap = 18;
        if (x >= first.Left - endGap && x < first.Left && y >= first.Top && y <= first.Bottom)
            return new(DockPointerMode.Reorder, 0);
        if (x > last.Right && x <= last.Right + endGap && y >= last.Top && y <= last.Bottom)
            return new(DockPointerMode.Reorder, bounds.Count - 1);
        return none;
    }
}
