namespace GlassDock.Core.Applications;

/// <summary>Snapshot of the capabilities used by an app menu; no commands or native handles are owned here.</summary>
public sealed record DockAppMenuState(bool IsPinned, bool CanLaunch, bool CanElevate, bool CanLocate,
    IReadOnlyList<(long Handle, int ProcessId, long StartTicks)> Windows)
{
    public bool IsRunning => Windows.Count > 0;
    public bool ShowNewWindow => IsRunning && CanLaunch;
    public bool ShowPin => IsPinned || CanLaunch;
    public bool HasMultipleWindows => Windows.Count > 1;
    public string PinLabel => IsPinned ? "Unpin from Dock" : "Pin to Dock";
    public static DockAppMenuState Create(DockApplication app, bool canLaunch, bool canElevate, bool canLocate) =>
        new(app.IsPinned, canLaunch, canElevate, canLocate,
            app.Windows.Select(w => (w.Handle, w.ProcessId, w.ProcessStartTicks)).ToArray());

    public bool Matches(DockApplication app) => IsPinned == app.IsPinned &&
        Windows.ToHashSet().SetEquals(app.Windows.Select(w => (w.Handle, w.ProcessId, w.ProcessStartTicks)));
}
