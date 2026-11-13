namespace GlassDock.Core.Applications;

public static class DockApplicationMonitorFilter
{
    public static ApplicationSnapshot ForMonitor(
        ApplicationSnapshot snapshot,
        Func<ApplicationWindow, bool> belongsToMonitor)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(belongsToMonitor);

        var applications = new List<DockApplication>(snapshot.Applications.Count);
        foreach (var application in snapshot.Applications)
        {
            var localWindows = application.Windows.Where(belongsToMonitor).ToArray();
            if (!application.IsPinned && localWindows.Length == 0)
                continue;

            applications.Add(application with { Windows = localWindows,
                StackApps = application.Stack is null ? application.StackApps :
                    ForMonitor(new(application.StackApps), belongsToMonitor).Applications });
        }

        return new ApplicationSnapshot(applications, snapshot.Warning);
    }
}
