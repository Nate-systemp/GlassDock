namespace GlassDock.Core.Desktop;

public enum GlassHomeState { Hidden, Compact, Expanded }

public readonly record struct GlassHomePointer(double X, double Y, int Buttons, bool Inside);

/// <summary>One opening's pointer origin and button edges, in physical screen pixels.</summary>
public sealed class GlassHomeSession
{
    private GlassHomePointer? origin;
    private int previousButtons;
    public GlassHomeState State { get; private set; }
    public int Revision { get; private set; }

    public void Open(GlassHomePointer? pointer)
    {
        Revision++;
        origin = pointer;
        previousButtons = pointer?.Buttons ?? 0;
        State = GlassHomeState.Compact;
    }

    public void Observe(GlassHomePointer pointer)
    {
        if (State == GlassHomeState.Hidden) return;
        if (origin is null) { origin = pointer; previousButtons = pointer.Buttons; return; }
        var pressed = pointer.Buttons & ~previousButtons;
        previousButtons = pointer.Buttons;
        if (pressed != 0 && !pointer.Inside) { Hide(); return; }
        var initial = origin.Value;
        var dx = pointer.X - initial.X;
        var dy = pointer.Y - initial.Y;
        if (State == GlassHomeState.Compact && dx * dx + dy * dy >= 30 * 30)
            State = GlassHomeState.Expanded;
    }

    public void Hide()
    {
        Revision++;
        State = GlassHomeState.Hidden;
        origin = null;
        previousButtons = 0;
    }
}
