namespace GlassDock.Core.Applications;

public static class VirtualDesktopPolicy
{
    public static bool Track(bool? onCurrentDesktop, int cloakFlags) =>
        cloakFlags == 0 || (onCurrentDesktop == false && cloakFlags == 2);

    public static bool IsCurrent(bool? onCurrentDesktop, int cloakFlags) =>
        cloakFlags == 0 || onCurrentDesktop == true;

    public static IReadOnlyList<ApplicationWindow> Filter(IEnumerable<ApplicationWindow> windows, bool showAll) =>
        windows.Where(window => showAll || window.IsOnCurrentDesktop).ToArray();
}
