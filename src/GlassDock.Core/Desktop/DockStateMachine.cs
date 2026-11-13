namespace GlassDock.Core.Desktop;

public enum DockState { Hidden, Idle, Hovering, Expanding, Expanded, Collapsing }

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
        if (State is DockState.Idle or DockState.Hovering or DockState.Collapsing)
        { State = DockState.Expanding; Revision++; }
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

    public bool HoldTransition()
    {
        if (State is not (DockState.Expanding or DockState.Collapsing))
            return false;
        Revision++;
        return true;
    }

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
