using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;

namespace GlassDock.App.Desktop;

/// <summary>
/// Process-wide services shared by every monitor dock. Individual overlay windows
/// own only their HWND/UI state; application discovery and badge providers run once.
/// </summary>
internal sealed class DokySharedRuntime : IDisposable
{
    private bool disposed;

    public WindowsApplicationService Applications { get; } = new();
    public WindowsToastBadgeProvider WindowsToastBadges { get; } = new();
    public BadgeCoordinator Badges { get; }

    public DokySharedRuntime()
    {
        Badges = new BadgeCoordinator([WindowsToastBadges]);
    }

    public void StartApplications()
    {
        if (disposed) return;
        Applications.Start();
    }

    public Task SetNotificationBadgesAsync(bool enabled, bool requestPermission = false)
    {
        if (disposed) return Task.CompletedTask;
        if (!enabled)
        {
            Badges.Stop();
            return Task.CompletedTask;
        }

        return Badges.StartAsync(requestPermission);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Badges.Dispose();
        Applications.Dispose();
    }
}
