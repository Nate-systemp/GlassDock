namespace GlassDock.Core.Desktop;

public enum DockState { Hidden, Idle, Hovering, Expanding, Expanded, Collapsing }

/// <summary>Deterministic transitions; timing belongs to the UI animation controller.</summary>
public sealed class DockStateMachine
{
    public DockState State { get; private set; } = DockState.Hidden;
    public long Revision { get; private set; }

    public void Show() { State = DockState.Idle; Revision++; }
    public void Hide() { State = DockState.Hidden; Revision++; }
    public long Enter()
    {
        if (State is DockState.Idle or DockState.Collapsing)
        {
            State = DockState.Hovering;
            Revision++;
        }
        return Revision;
    }
    public long Expand()
    {
        if (State == DockState.Hovering) { State = DockState.Expanding; Revision++; }
        return Revision;
    }
    public void LeavePeek()
    {
        if (State == DockState.Hovering) { State = DockState.Idle; Revision++; }
    }
    public long Collapse()
    {
        if (State is DockState.Expanding or DockState.Expanded or DockState.Hovering)
        {
            State = DockState.Collapsing;
            Revision++;
        }
        return Revision;
    }

    /// <summary>
    /// Invalidates an in-flight visual transition while preserving its current
    /// logical state. The desktop window can hold the rendered values under a
    /// context menu without allowing a stale completion to change state.
    /// </summary>
    public bool HoldTransition()
    {
        if (State is not (DockState.Expanding or DockState.Collapsing))
            return false;
        Revision++;
        return true;
    }

    /// <summary>Chooses the state from which normal interaction should resume.</summary>
    public void ResumeHeldTransition(bool pointerInside)
    {
        if (State is not (DockState.Expanding or DockState.Collapsing))
            return;
        State = pointerInside ? DockState.Hovering : DockState.Expanded;
        Revision++;
    }

    public void Complete(long revision)
    {
        if (revision != Revision) return;
        State = State switch { DockState.Expanding => DockState.Expanded, DockState.Collapsing => DockState.Idle, _ => State };
    }
}
