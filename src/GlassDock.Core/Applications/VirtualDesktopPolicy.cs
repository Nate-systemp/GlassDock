namespace GlassDock.Core.Applications;

public static class VirtualDesktopPolicy
{
    // DWM_CLOAKED_SHELL alone can represent an otherwise valid window on another
    // desktop. App/inherited cloaking must not expose suspended/auxiliary windows.
    public static bool Track(bool? onCurrentDesktop, int cloakFlags) =>
        cloakFlags == 0 || (onCurrentDesktop == false && cloakFlags == 2);

    // Windows pinned to all desktops can be visible even when their home desktop
    // differs. Unknown membership retains the former uncloaked-window behavior.
    public static bool IsCurrent(bool? onCurrentDesktop, int cloakFlags) =>
        cloakFlags == 0 || onCurrentDesktop == true;

    public static IReadOnlyList<ApplicationWindow> Filter(IEnumerable<ApplicationWindow> windows, bool showAll) =>
        windows.Where(window => showAll || window.IsOnCurrentDesktop).ToArray();
}
