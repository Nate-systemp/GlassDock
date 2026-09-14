namespace GlassDock.Core.Applications;

public enum WindowPreviewState { Hidden, Waiting, Compact, Expanding, Expanded, WindowHovered, Collapsing, Activating }

/// <summary>Ordering is chosen on entry, then preserved until this preview session ends.</summary>
public sealed class WindowPreviewSession
{
    public WindowPreviewState State { get; private set; }
    public string? ApplicationId { get; private set; }
    public IReadOnlyList<ApplicationWindow> Windows { get; private set; } = [];
    public long? SelectedWindow { get; private set; }
    public int Revision { get; private set; }

    public int Begin(DockApplication application)
    {
        Revision++;
        ApplicationId = application.Id;
        Windows = application.Windows.OrderByDescending(window => window.IsActive)
            .ThenByDescending(window => window.LastActivatedTicks).ToArray();
        SelectedWindow = null;
        State = Windows.Count == 0 ? WindowPreviewState.Hidden : WindowPreviewState.Waiting;
        return Revision;
    }

    public bool Show(int revision)
    {
        if (revision != Revision || State != WindowPreviewState.Waiting) return false;
        State = WindowPreviewState.Compact;
        return true;
    }

    public void Expand()
    {
        if (State is WindowPreviewState.Compact or WindowPreviewState.Collapsing) State = WindowPreviewState.Expanding;
    }

    public void Collapse()
    {
        if (State is not (WindowPreviewState.Expanding or WindowPreviewState.Expanded or WindowPreviewState.WindowHovered)) return;
        SelectedWindow = null;
        State = WindowPreviewState.Collapsing;
    }

    /// <summary>Called only after the corresponding animation geometry has been drawn.</summary>
    public bool CompleteTransition(double progress)
    {
        if (State == WindowPreviewState.Expanding && progress == 1)
        { State = WindowPreviewState.Expanded; return true; }
        if (State == WindowPreviewState.Collapsing && progress == 0) State = WindowPreviewState.Compact;
        return false;
    }

    public void Select(long? handle)
    {
        if (State is not (WindowPreviewState.Expanded or WindowPreviewState.WindowHovered)) return;
        SelectedWindow = Windows.Any(window => window.Handle == handle) ? handle : null;
        State = SelectedWindow is null ? WindowPreviewState.Expanded : WindowPreviewState.WindowHovered;
    }

    public ApplicationWindow? Activate(long handle)
    {
        var window = Windows.FirstOrDefault(window => window.Handle == handle);
        if (window is null || State is WindowPreviewState.Hidden or WindowPreviewState.Waiting) return null;
        State = WindowPreviewState.Activating;
        return window;
    }

    public void Refresh(IReadOnlyList<ApplicationWindow> windows)
    {
        var remaining = windows.ToDictionary(window => (window.Handle, window.ProcessId, window.ProcessStartTicks));
        var ordered = new List<ApplicationWindow>();
        foreach (var old in Windows)
            if (remaining.Remove((old.Handle, old.ProcessId, old.ProcessStartTicks), out var current)) ordered.Add(current);
        ordered.AddRange(remaining.Values.OrderBy(window => window.Handle));
        Windows = ordered;
        if (Windows.Count == 0) { Hide(); return; }
        if (!Windows.Any(window => window.Handle == SelectedWindow)) Select(null);
    }

    public void Hide()
    {
        Revision++;
        State = WindowPreviewState.Hidden;
        ApplicationId = null;
        SelectedWindow = null;
        Windows = [];
    }
}
