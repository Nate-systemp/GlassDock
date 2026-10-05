namespace GlassDock.Core.Applications;

/// <summary>Contextual data from the existing application snapshot, never guessed shell destinations.</summary>
public sealed record AppActionPanelModel(DockApplication Application, bool CanOpenFile, bool IsStackMember)
{
    public IReadOnlyList<ApplicationWindow> RunningWindows => Application.Windows;
    public string Status => Application.IsRunning
        ? $"Running · {RunningWindows.Count} {(RunningWindows.Count == 1 ? "window" : "windows")}"
        : Application.IsPinned ? "Pinned" : "Not running";
    public string PinAction => IsStackMember ? "Move out of stack"
        : Application.IsPinned ? "Unpin from Doky" : "Pin to Doky";
    // No trustworthy per-application recent source currently exists. Omit the section.
    public IReadOnlyList<string> RecentItems => Array.Empty<string>();
}
