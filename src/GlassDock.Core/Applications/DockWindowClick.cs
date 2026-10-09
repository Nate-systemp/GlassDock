namespace GlassDock.Core.Applications;

public enum DockWindowClickAction { Activate, Restore, Minimize }

public static class DockWindowClick
{
    // Grouped applications retain activation/preview behavior rather than
    // unexpectedly minimizing whichever window happened to be enumerated first.
    public static DockWindowClickAction Resolve(int windowCount, bool foreground, bool minimized) =>
        minimized ? DockWindowClickAction.Restore :
        windowCount == 1 && foreground ? DockWindowClickAction.Minimize : DockWindowClickAction.Activate;
}
