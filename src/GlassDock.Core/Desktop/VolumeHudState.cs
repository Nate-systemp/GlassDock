namespace GlassDock.Core.Desktop;

public enum VolumeHudPhase { Hidden, Expanded, Compact }

public sealed class VolumeHudState
{
    public VolumeHudPhase Phase { get; private set; }
    public bool Interacting { get; private set; }
    public void Change(bool open = false)
    {
        Phase = open || Interacting || Phase == VolumeHudPhase.Hidden ? VolumeHudPhase.Expanded : VolumeHudPhase.Compact;
    }
    public void BeginInteraction() { Interacting = true; Phase = VolumeHudPhase.Expanded; }
    public void EndInteraction() => Interacting = false;
    public bool Hide()
    {
        if (Interacting) return false;
        Phase = VolumeHudPhase.Hidden; return true;
    }
    public static double VolumeAt(double x, double width) => width > 0 && double.IsFinite(x) && double.IsFinite(width)
        ? Math.Clamp(x / width, 0, 1) : 0;
}
