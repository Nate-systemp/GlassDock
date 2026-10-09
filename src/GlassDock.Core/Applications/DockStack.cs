namespace GlassDock.Core.Applications;

/// <summary>Global organization referencing the existing persisted application identities.</summary>
public sealed record DockStack(string Id, string Name, IReadOnlyList<string> ApplicationIds)
{
    public const int MaximumApps = 9;
    public static int Columns(int count) => count <= 1 ? 1 : count <= 4 ? 2 : 3;
}

public enum DockDragMode { None, Reorder, StackCandidate, StackMerge, ExternalFiles }

/// <summary>One monotonic dwell decision; moving off the target cancels the candidate.</summary>
public sealed class DockStackDrag
{
    public const int DwellMilliseconds = 450;
    public DockDragMode Mode { get; private set; }
    public string? TargetId { get; private set; }
    private long candidateSince;
    public void PreviewTarget(string? target)
    {
        if (Mode is DockDragMode.None or DockDragMode.ExternalFiles) return;
        TargetId = target;
        Mode = target is null ? DockDragMode.Reorder : DockDragMode.StackMerge;
    }
    public void Begin(bool external = false) { Reset(); Mode = external ? DockDragMode.ExternalFiles : DockDragMode.Reorder; }
    public void Hover(string? eligibleCenteredTarget, long milliseconds)
    {
        if (Mode is DockDragMode.None or DockDragMode.ExternalFiles) return;
        if (eligibleCenteredTarget is null) { TargetId = null; Mode = DockDragMode.Reorder; return; }
        if (TargetId != eligibleCenteredTarget)
        {
            TargetId = eligibleCenteredTarget; candidateSince = milliseconds; Mode = DockDragMode.StackCandidate;
        }
        if (milliseconds - candidateSince >= DwellMilliseconds) Mode = DockDragMode.StackMerge;
    }
    public void Reset() { Mode = DockDragMode.None; TargetId = null; candidateSince = 0; }
}
