namespace GlassDock.Core.Desktop;

/// <summary>Shared, bounded critically-damped curve for content and glass tracks.</summary>
public sealed class UtilitiesPressScale
{
    public const double HeldScale = 1.2;
    public const int PressMilliseconds = 180, ReleaseMilliseconds = 240;
    private double from = 1, target = 1;
    private long started;
    public int Duration { get; private set; } = PressMilliseconds;
    public double At(long now)
    {
        var t = Math.Clamp((double)(now - started) / Duration, 0, 1);
        var eased = (1 - (1 + 7 * t) * Math.Exp(-7 * t)) / (1 - 8 * Math.Exp(-7));
        return from + (target - from) * eased;
    }
    public bool Set(bool held, long now, bool animate)
    {
        var next = held ? HeldScale : 1;
        if (next == target) return false;
        from = animate ? At(now) : next;
        target = next; started = now;
        Duration = held ? PressMilliseconds : ReleaseMilliseconds;
        return true;
    }
    public static double Padding(double width) => Math.Ceiling(width * (HeldScale - 1) / 2) + 4;
}
