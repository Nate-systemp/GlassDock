namespace GlassDock.Core.Desktop;

public sealed class DockHoverMagnification<T> where T : class
{
    public const double HoverScale = 1.50;
    public const double NeighborScale = 1.25;

    public readonly record struct Item(T Id, double Left, double Top, double Width, double Height,
        bool Magnifiable = true, double ArtworkWidth = 0)
    {
        public double Footprint => !Magnifiable ? Width : Math.Max(
            ArtworkWidth > 0 && double.IsFinite(ArtworkWidth) ? ArtworkWidth : Width,
            Math.Min(Width, Height) * .70);
    }

    public readonly record struct Frame(T Id, double Scale, double OffsetX);
    private sealed class Motion { public double Scale = 1; public double Velocity; }
    private readonly Dictionary<T, Motion> motions = new();
    private readonly HashSet<T> liveItems = new(ReferenceEqualityComparer.Instance);
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
        for (var i = 0; i < items.Count; i++) liveItems.Add(items[i].Id);
        foreach (var id in motions.Keys)
            if (!liveItems.Contains(id)) motions.Remove(id);
        liveItems.Clear();
        for (var i = 0; i < items.Count; i++)
            if (!motions.ContainsKey(items[i].Id)) motions.Add(items[i].Id, new Motion());

        ContentWidth = items.Count == 0 ? 0 : Math.Max(0, items[^1].Left + items[^1].Width - items[0].Left);
        var seconds = lastTime is { } previous ? Math.Clamp((now - previous) / 1000, 0, .1) : 0;
        lastTime = now;
        if (paused) return Layout(items);

        var appCount = 0;
        double firstLeft = 0, lastRight = 0;
        var minimumTop = double.PositiveInfinity;
        var maximumBottom = double.NegativeInfinity;
        var nearestDistance = double.PositiveInfinity;
        int? focus = null;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!item.Magnifiable || !(item.Width > 0 && item.Height > 0)) continue;
            if (appCount == 0) firstLeft = item.Left;
            lastRight = item.Left + item.Width;
            minimumTop = Math.Min(minimumTop, item.Top - item.Height * .5);
            var bottom = item.Top + item.Height;
            if (appCount == 0 || bottom > maximumBottom || double.IsNaN(maximumBottom)) maximumBottom = bottom;
            if (pointerX is { } x)
            {
                var center = item.Left + item.Width / 2;
                var distance = Math.Abs(x - center);
                if (!(distance >= nearestDistance))
                {
                    nearestDistance = distance;
                    focus = appCount;
                }
            }
            appCount++;
        }
        if (pointerX is not { } pointer || appCount == 0 ||
            !(pointer >= firstLeft && pointer <= lastRight &&
              pointerY >= minimumTop && pointerY <= maximumBottom)) focus = null;

        IsAnimating = false;
        var appIndex = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
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

        const double frequency = 24;
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
        if (items.Count == 0) return Array.Empty<Frame>();
        var frames = new Frame[items.Count];
        for (var i = 0; i < items.Count; i++)
            frames[i] = new Frame(items[i].Id, motions[items[i].Id].Scale, 0);
        return frames;
    }
}
