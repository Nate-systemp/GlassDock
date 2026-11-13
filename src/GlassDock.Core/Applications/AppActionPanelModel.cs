namespace GlassDock.Core.Applications;

public sealed record AppActionPanelModel(DockApplication Application, bool CanOpenFile, bool IsStackMember)
{
    public IReadOnlyList<ApplicationWindow> RunningWindows => Application.Windows;
    public string Status => Application.IsRunning
        ? $"Running · {RunningWindows.Count} {(RunningWindows.Count == 1 ? "window" : "windows")}"
        : Application.IsPinned ? "Pinned" : "Not running";
    public string PinAction => IsStackMember ? "Move out of stack"
        : Application.IsPinned ? "Unpin from Doky" : "Pin to Doky";
    public IReadOnlyList<string> RecentItems => Array.Empty<string>();
}
