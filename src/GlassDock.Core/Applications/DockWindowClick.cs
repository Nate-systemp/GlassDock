namespace GlassDock.Core.Applications;

public enum DockWindowClickAction { Activate, Restore, Minimize }

public static class DockWindowClick
{
    // Grouped applications retain activation/preview behavior rather than
    // unexpectedly minimizing whichever window happened to be enumerated first.
    public static DockWindowClickAction Resolve(int windowCount, bool foreground, bool minimized) =>
        minimized ? DockWindowClickAction.Restore :
        windowCount == 1 && foreground ? DockWindowClickAction.Minimize : DockWindowClickAction.Activate;

    // Window enumeration order is not activation order. A stopped/stale HWND must not
    // win selection over a live window, and the actual foreground window wins over
    // an older activation timestamp. Stable input order resolves remaining ties.
    public static ApplicationWindow? SelectWindow(
        IEnumerable<ApplicationWindow> windows,
        Func<ApplicationWindow, bool> canInteract,
        Func<ApplicationWindow, bool> isForeground) =>
        windows.Where(canInteract)
            .OrderByDescending(isForeground)
            .ThenByDescending(window => window.LastActivatedTicks)
            .FirstOrDefault();
}
