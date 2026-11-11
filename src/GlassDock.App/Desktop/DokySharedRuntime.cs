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
    public GlassDock.Windows.Desktop.WifiUtilityService Wifi { get; } = new();
    public GlassDock.Windows.Desktop.BluetoothUtilityService Bluetooth { get; } = new();
    public GlassDock.Windows.Desktop.AirplaneModeUtilityService AirplaneMode { get; } = new();
    public GlassDock.Windows.Desktop.NightLightUtilityService NightLight { get; } = new();
    public GlassDock.Windows.Desktop.EnergySaverUtilityService EnergySaver { get; } = new();
    public GlassDock.Windows.Desktop.DndUtilityService DoNotDisturb { get; } = new();
    public GlassDock.Windows.Desktop.MasterVolumeService Volume { get; } = new();
    public GlassDock.Windows.Desktop.BrightnessService Brightness { get; } = new();
    public WindowsToastBadgeProvider WindowsToastBadges { get; } = new();
    public BadgeCoordinator Badges { get; }

    public DokySharedRuntime()
    {
        Badges = new BadgeCoordinator(new[] { WindowsToastBadges });
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
        Wifi.Dispose();
        Bluetooth.Dispose();
        AirplaneMode.Dispose();
        NightLight.Dispose();
        EnergySaver.Dispose();
        DoNotDisturb.Dispose();
        Volume.Dispose();
        Brightness.Dispose();
        Badges.Dispose();
        Applications.Dispose();
    }
}
