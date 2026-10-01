using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace GlassDock.Windows.Applications;

/// <summary>
/// Read-only Windows Notification Center provider. It records only AppUserModelId and toast
/// count metadata; it never reads notification text, removes notifications or polls.
/// </summary>
public class WindowsToastBadgeProvider : IBadgeProvider
{
    private UserNotificationListener? listener;
    private bool disposed, busy, requested, subscribed, enabled;

    public string Id => "windows-toast";
    public int Priority => BadgeProviderPriority.WindowsToast;
    public IReadOnlyDictionary<string, BadgeSignal> Snapshot { get; private set; } =
        new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase);
    public string Status { get; private set; } = "Notification badges are not enabled.";
    public event EventHandler? Changed;
    public event EventHandler? RefreshRequested;

    public async Task StartAsync(bool requestPermission = false)
    {
        if (disposed || busy) return;
        enabled = true;
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
                ReplaceSnapshot(new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase));
                Status = access == UserNotificationListenerAccessStatus.Denied
                    ? "Notification access denied. Allow Doky in Windows notification privacy settings."
                    : "Choose Enable notification badges to grant Windows notification access.";
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!subscribed)
            {
                listener.NotificationChanged += NotificationChanged;
                subscribed = true;
            }
            requested = true;
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or UnauthorizedAccessException)
        {
            Status = $"Notification badges unavailable (0x{error.HResult:X8}). Register Doky's notification identity package first.";
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            busy = false;
        }

        await RefreshAsync();
    }

    private void NotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    public async Task RefreshAsync()
    {
        requested = true;
        if (disposed || busy || listener is null || !enabled) return;
        busy = true;
        try
        {
            do
            {
                requested = false;
                var rawCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (listener.GetAccessStatus() == UserNotificationListenerAccessStatus.Allowed)
                {
                    var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
                    if (disposed || !enabled) return;

                    foreach (var notification in notifications)
                    {
                        // Intentionally read identity only. Do not access notification.Notification
                        // or any toast visual/text payload here.
                        var id = notification.AppInfo?.AppUserModelId;
                        if (!string.IsNullOrWhiteSpace(id))
                            rawCounts[id] = rawCounts.GetValueOrDefault(id) + 1;
                    }

                    var now = DateTimeOffset.UtcNow;
                    var next = new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase);
                    foreach (var pair in rawCounts)
                    {
                        var updatedAt = Snapshot.TryGetValue(pair.Key, out var previous) &&
                            previous.Kind == BadgeKind.Count && previous.Count == pair.Value
                                ? previous.UpdatedAt
                                : now;
                        next[pair.Key] = BadgeSignal.Counted(pair.Value, updatedAt);
                    }

                    ReplaceSnapshot(next);
                    Status = "Notification badges enabled (Windows Notification Center counts).";
                    TraceMetadata(next);
                }
                else
                {
                    ReplaceSnapshot(new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase));
                    Status = "Notification access is not allowed; badges cleared.";
                }

                Changed?.Invoke(this, EventArgs.Empty);
            } while (requested && !disposed);
        }
        catch (Exception error) when (error is COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            ReplaceSnapshot(new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase));
            Status = $"Windows notification counts unavailable (0x{error.HResult:X8}).";
            if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            busy = false;
        }
    }

    public void Stop()
    {
        if (disposed) return;
        enabled = false;
        requested = false;
        if (subscribed && listener is not null)
        {
            listener.NotificationChanged -= NotificationChanged;
            subscribed = false;
        }
        ReplaceSnapshot(new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase));
        Status = "Notification badges are off.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceSnapshot(IReadOnlyDictionary<string, BadgeSignal> snapshot) => Snapshot = snapshot;

    private void TraceMetadata(IReadOnlyDictionary<string, BadgeSignal> snapshot)
    {
        Debug.WriteLine($"Badge provider '{Id}': {snapshot.Count} application identities.");
        foreach (var pair in snapshot)
            Debug.WriteLine($"Badge provider '{Id}': AUMID={pair.Key}; kind={pair.Value.Kind}; count={pair.Value.Count}.");
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop();
        disposed = true;
        listener = null;
        Changed = null;
        RefreshRequested = null;
    }
}
