namespace GlassDock.Core.Applications;

public enum DockWindowClickAction { Activate, Restore, Minimize }

public static class DockWindowClick
{
    public static DockWindowClickAction Resolve(int windowCount, bool foreground, bool minimized) =>
        minimized ? DockWindowClickAction.Restore :
        windowCount == 1 && foreground ? DockWindowClickAction.Minimize : DockWindowClickAction.Activate;

    public static ApplicationWindow? SelectWindow(
        IEnumerable<ApplicationWindow> windows,
        Func<ApplicationWindow, bool> canInteract,
        Func<ApplicationWindow, bool> isForeground) =>
        windows.Where(canInteract)
            .OrderByDescending(isForeground)
            .ThenByDescending(window => window.LastActivatedTicks)
            .FirstOrDefault();
}
