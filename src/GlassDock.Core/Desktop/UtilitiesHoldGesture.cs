namespace GlassDock.Core.Desktop;

/// <summary>One bounded background-only hold; coordinates are logical pixels.</summary>
public sealed class UtilitiesHoldGesture
{
    public const int DurationMilliseconds = 600;
    private double x, y;
    private long started;
    public bool Pending { get; private set; }
    public void Begin(bool emptyBackground, double pointerX, double pointerY, long now)
    { Pending = emptyBackground; x = pointerX; y = pointerY; started = now; }
    public void Move(double pointerX, double pointerY, bool emptyBackground)
    {
        if (!emptyBackground || Math.Abs(pointerX - x) > 4 || Math.Abs(pointerY - y) > 4) Cancel();
    }
    public bool Take(long now)
    {
        if (!Pending || now - started < DurationMilliseconds) return false;
        Pending = false;
        return true;
    }
    public void Cancel() => Pending = false;
}
