namespace GlassDock.Core.Desktop;

public sealed class UtilityHudBounce(double peak)
{
    public const double Duration = 350;
    private double started;
    public bool Active { get; private set; }
    public bool Held { get; private set; }

    public void Start(double now, bool held)
    {
        started = now;
        Held = held;
        Active = true;
    }

    public void Release(double now)
    {
        if (!Held) return;
        started = Math.Max(started, now - peak * Duration);
        Held = false;
    }

    public double Sample(double now)
    {
        if (!Active) return 1;
        var progress = Math.Clamp((now - started) / Duration, 0, Held ? peak : 1);
        if (progress == 1) Active = false;
        return progress;
    }

    public void Cancel() { Active = Held = false; }
}
