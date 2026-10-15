namespace GlassDock.Core.Desktop;

/// <summary>
/// A local, spring-smoothed icon hover: the focused icon and its two immediate
/// neighbors scale in place. Unlike a macOS reflow, no button, running indicator,
/// or utility changes its allocated position, and the glass wave is independent.
/// </summary>
public sealed class DockHoverMagnification<T> where T : class
{
    public const double HoverScale = 1.50;
    public const double NeighborScale = 1.25;

    public readonly record struct Item(T Id, double Left, double Top, double Width, double Height,
        bool Magnifiable = true, double ArtworkWidth = 0)
    {
        // Retained for callers that measure icon artwork. It does NOT drive
        // hover layout: button positions and spacing must remain unchanged.
        public double Footprint => !Magnifiable ? Width : Math.Max(
            ArtworkWidth > 0 && double.IsFinite(ArtworkWidth) ? ArtworkWidth : Width,
            Math.Min(Width, Height) * .70);
    }

    public readonly record struct Frame(T Id, double Scale, double OffsetX);
    private sealed class Motion { public double Scale = 1; public double Velocity; }
    private readonly Dictionary<T, Motion> motions = new();
    private double? pointerX, lastTime;
    private double pointerY;
    private bool paused;

    public bool IsAnimating { get; private set; }
    public bool HasPointer => pointerX is not null;
    public double ContentWidth { get; private set; }

    public bool SetPointer(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return Leave();
        if (pointerX == x && pointerY == y) return false;
        if (!IsAnimating) lastTime = null;
        pointerX = x;
        pointerY = y;
        return true;
    }

    public bool Leave()
    {
        if (pointerX is null) return false;
        if (!IsAnimating) lastTime = null;
        pointerX = null;
        return true;
    }

    public void Reset()
    {
        pointerX = null;
        motions.Clear();
        lastTime = null;
        ContentWidth = 0;
        IsAnimating = false;
    }

    public void Pause(bool value, double now)
    {
        paused = value;
        lastTime = now;
    }

    // Only the focused icon and directly adjacent icons are magnified.
    // Smooth interpolation is kept for tests/future callers; the active focus
    // always uses the exact integer distances 0, 1, 2, ... .
    public static double ScaleAtDistance(double distance)
    {
        distance = Math.Abs(distance);
        if (!double.IsFinite(distance) || distance >= 2) return 1;
        var from = distance < 1 ? HoverScale : NeighborScale;
        var to = distance < 1 ? NeighborScale : 1;
        var t = distance - Math.Floor(distance);
        t = t * t * (3 - 2 * t);
        return from + (to - from) * t;
    }

    public IReadOnlyList<Frame> Step(IReadOnlyList<Item> items, double now, bool animationsEnabled)
    {
        foreach (var id in motions.Keys.Where(id => !items.Any(item => ReferenceEquals(item.Id, id))).ToArray())
            motions.Remove(id);
        foreach (var item in items)
            if (!motions.ContainsKey(item.Id)) motions.Add(item.Id, new Motion());

        ContentWidth = items.Count == 0 ? 0 : Math.Max(0, items[^1].Left + items[^1].Width - items[0].Left);
        var seconds = lastTime is { } previous ? Math.Clamp((now - previous) / 1000, 0, .1) : 0;
        lastTime = now;
        if (paused) return Layout(items);

        // Acquire hover from ORIGINAL button slots, never a magnified/reflowed
        // position. This prevents drift and sudden row movement at the edges.
        var apps = items.Where(item => item.Magnifiable && item.Width > 0 && item.Height > 0).ToArray();
        int? focus = null;
        if (pointerX is { } x && apps.Length > 0 &&
            x >= apps[0].Left && x <= apps[^1].Left + apps[^1].Width &&
            pointerY >= apps.Min(item => item.Top - item.Height * .5) &&
            pointerY <= apps.Max(item => item.Top + item.Height))
        {
            var nearestDistance = double.PositiveInfinity;
            for (var i = 0; i < apps.Length; i++)
            {
                var center = apps[i].Left + apps[i].Width / 2;
                var distance = Math.Abs(x - center);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                focus = i;
            }
        }

        IsAnimating = false;
        var appIndex = 0;
        foreach (var item in items)
        {
            var isApp = item.Magnifiable && item.Width > 0 && item.Height > 0;
            var target = isApp && focus is { } focused
                ? ScaleAtDistance(appIndex - focused)
                : 1;
            if (isApp) appIndex++;
            IsAnimating |= Advance(motions[item.Id], target, seconds, animationsEnabled);
        }
        return Layout(items);
    }

    private static bool Advance(Motion motion, double target, double seconds, bool animationsEnabled)
    {
        if (!animationsEnabled)
        {
            motion.Scale = target;
            motion.Velocity = 0;
            return false;
        }

        // Critically damped spring, roughly 200ms to settle, with no bounce or
        // animation restart when pointer movement retargets a different icon.
        const double frequency = 36;
        var displacement = motion.Scale - target;
        var coefficient = motion.Velocity + frequency * displacement;
        var decay = Math.Exp(-frequency * seconds);
        motion.Scale = target + (displacement + coefficient * seconds) * decay;
        motion.Velocity = (motion.Velocity - frequency * coefficient * seconds) * decay;
        if (motion.Scale < 1 || motion.Scale > HoverScale)
        {
            motion.Scale = Math.Clamp(motion.Scale, 1, HoverScale);
            motion.Velocity = 0;
        }
        if (Math.Abs(motion.Scale - target) < .0005 && Math.Abs(motion.Velocity) < .01)
        {
            motion.Scale = target;
            motion.Velocity = 0;
            return false;
        }
        return true;
    }

    private IReadOnlyList<Frame> Layout(IReadOnlyList<Item> items)
    {
        // OffsetX is always zero. Only the artwork is scaled by the UI layer;
        // buttons, badges, indicators, and dock glass stay exactly in place.
        return items.Select(item => new Frame(item.Id, motions[item.Id].Scale, 0)).ToArray();
    }
}
