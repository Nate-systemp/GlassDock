using System.Diagnostics;

namespace GlassDock.Windows.Desktop;

public static class TaskbarRecovery
{
    public static string EventName => $"Local\\GlassDock.Recovery.{Process.GetCurrentProcess().SessionId}";
    public static string LeaseName => $"Local\\GlassDock.TaskbarLease.{Process.GetCurrentProcess().SessionId}";

    public static bool RestoreNow()
    {
        if (EventWaitHandle.TryOpenExisting(EventName, out var signal))
        {
            using (signal) signal.Set();
        }
        return new WindowsTaskbarController().EmergencyRestore();
    }
}
