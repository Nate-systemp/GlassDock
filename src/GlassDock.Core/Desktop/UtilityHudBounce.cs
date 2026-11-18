namespace GlassDock.Core.Desktop;

public sealed class UtilityHudBounce(double peak)
{
    public const double Duration = 350;
    private double started;
    private double duration = Duration;
    public bool Active { get; private set; }
    public bool Held { get; private set; }

    public void Start(double now, bool held, double duration = Duration)
    {
        started = now;
        this.duration = duration;
        Held = held;
        Active = true;
    }

    public void Release(double now)
    {
        if (!Held) return;
        started = Math.Max(started, now - peak * duration);
        Held = false;
    }

    public double Sample(double now)
    {
        if (!Active) return 1;
        var progress = Math.Clamp((now - started) / duration, 0, Held ? peak : 1);
        if (progress == 1) Active = false;
        return progress;
    }

    public void Cancel() { Active = Held = false; }
}
