namespace GlassDock.Core.Applications;

/// <summary>One button-edge latch shared by pressed/moved/released routing.</summary>
public sealed class DockDragIntent
{
    public bool StackMode { get; private set; }
    private bool rightDown;
    public void Begin(bool rightAlreadyDown) { Reset(); rightDown = rightAlreadyDown; }
    public bool Observe(bool left, bool right)
    {
        var changed = left && right && !rightDown;
        if (changed) StackMode = !StackMode;
        rightDown = right;
        return changed;
    }
    public void Reset() { StackMode = false; rightDown = false; }
    public DockPointerTarget Resolve(double x, double y, IReadOnlyList<DockDragBounds> bounds,
        int source, Func<int, bool> eligible, int previous = -1)
    {
        if (!StackMode) return DockPointerTarget.ResolveReorder(x, y, bounds, source);
        var target = DockPointerTarget.Resolve(x, y, bounds, source, eligible, previous);
        return target.Mode == DockPointerMode.Stack ? target : new(DockPointerMode.Outside, -1);
    }
}
