using System.Runtime.InteropServices;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace GlassDock.Windows.Applications;

/// <summary>Read-only Notification Center counts. Never reads toast text, removes notifications or polls.</summary>
public sealed class WindowsNotificationService : IDisposable
{
    private UserNotificationListener? listener;
    private bool disposed, busy, requested, subscribed;
    public IReadOnlyDictionary<string, int> Counts { get; private set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public string Status { get; private set; } = "Notification badges are not enabled.";
    public event EventHandler? Changed;
    public event EventHandler? RefreshRequested;

    public async Task StartAsync(bool requestPermission = false)
    {
        if (disposed || busy) return;
        busy = true;
        try
        {
            // Package.Current throws for the ordinary unpackaged launch; don't issue a misleading consent prompt.
            _ = global::Windows.ApplicationModel.Package.Current.Id;
            listener ??= UserNotificationListener.Current;
            var access = requestPermission ? await listener.RequestAccessAsync() : listener.GetAccessStatus();
            if (disposed) return;
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                Status = access == UserNotificationListenerAccessStatus.Denied
                    ? "Notification access denied. Allow Doky in Windows notification privacy settings."
                    : "Choose Enable notification badges to grant Windows notification access.";
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!subscribed) { listener.NotificationChanged += NotificationChanged; subscribed = true; }
            requested = true;
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            Status = $"Notification badges unavailable (0x{error.HResult:X8}). Register Doky's notification identity package first.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { busy = false; }
        await RefreshAsync();
    }

    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    public async Task RefreshAsync()
    {
        requested = true;
        if (disposed || busy || listener is null) return;
        busy = true;
        try
        {
            do
            {
                requested = false;
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                    if (disposed) return;
                    foreach (var notification in notifications)
                    {
                        var id = notification.AppInfo?.AppUserModelId;
                        if (!string.IsNullOrWhiteSpace(id)) counts[id] = counts.GetValueOrDefault(id) + 1;
                    }
                    Status = "Notification badges enabled (Windows Notification Center counts).";
                }
                else Status = "Notification access is not allowed; badges cleared.";
                Counts = counts;
                Changed?.Invoke(this, EventArgs.Empty);
            } while (requested && !disposed);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            Counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Status = $"Windows notification counts unavailable (0x{error.HResult:X8}).";
            if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
        }
        finally { busy = false; }
    }

    public void Dispose()
    {
        disposed = true;
        if (subscribed && listener is not null) listener.NotificationChanged -= NotificationChanged;
        listener = null; Changed = null; RefreshRequested = null;
    }
}
