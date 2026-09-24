using System.Diagnostics;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Desktop;

// This helper does not restart any process. Its only mutation is reversible taskbar visibility.
if (args is ["--status"])
{
    var status = new WindowsTaskbarController().Inspect();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(status));
    return status.Available ? 0 : 1;
}
if (args is ["--restore"])
{
    var restored = TaskbarRecovery.RestoreNow();
    Console.WriteLine(restored ? "RESTORED" : "RESTORE FAILED: no visible taskbar was verified.");
    return restored ? 0 : 1;
}
if (args.Length != 3 || args[0] is not ("--watch" or "--watch-active") ||
    !int.TryParse(args[1], out var parentId) || !long.TryParse(args[2], out var startTicks))
{
    Console.WriteLine("GlassDock Recovery: --status, --restore, or App-owned --watch <pid> <start-ticks>");
    return 0;
}

var controller = new WindowsTaskbarController();
var ownsLease = false;
var exitCode = 0;
using var lease = new Mutex(false, TaskbarRecovery.LeaseName);
using var emergency = new EventWaitHandle(false, EventResetMode.ManualReset, TaskbarRecovery.EventName);
try
{
    using var parent = Process.GetProcessById(parentId);
    if (parent.StartTime.ToUniversalTime().Ticks != startTicks ||
        parent.SessionId != Process.GetCurrentProcess().SessionId ||
        !string.Equals(parent.ProcessName, "GlassDock.App", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Parent identity does not match GlassDock.App.");

    try { ownsLease = lease.WaitOne(0); }
    catch (AbandonedMutexException) { ownsLease = true; }
    if (!ownsLease) throw new InvalidOperationException("Another taskbar test owns the lease.");
    emergency.Reset();
    Console.WriteLine("READY");
    Console.Out.Flush();

    var clock = Stopwatch.StartNew();
    var deadline = new TaskbarLease(args[0] == "--watch-active");
    TimeSpan? hiddenAt = null;
    var consecutiveMaintenanceFailures = 0;
    var read = Task.Run(Console.ReadLine);
    while (!parent.HasExited && !emergency.WaitOne(0))
    {
        if (deadline.IsExpired(clock.Elapsed)) break;
        if (read.IsCompleted)
        {
            var command = read.GetAwaiter().GetResult();
            if (command is null or "RESTORE") break;
            if (command == "HIDE" && hiddenAt is null)
            {
                controller.HideForTest();
                hiddenAt = clock.Elapsed;
                deadline.Hidden(clock.Elapsed);
                Console.WriteLine("HIDDEN");
                Console.Out.Flush();
            }
            else if (command != "PING") break;
            deadline.Heartbeat(clock.Elapsed);
            read = Task.Run(Console.ReadLine);
        }
        if (hiddenAt is not null)
        {
            if (controller.MaintainHidden())
            {
                consecutiveMaintenanceFailures = 0;
            }
            else
            {
                // A shell/foreground transition is NOT a reason to terminate the
                // safety lease. Terminating here immediately runs Restore() and is
                // exactly what makes the Windows taskbar reappear when Task Manager
                // or another shell transition temporarily defeats one maintenance
                // pass. Keep the watchdog alive and retry on the normal 250 ms
                // cadence; parent death, heartbeat expiry, emergency restore, EOF,
                // or explicit RESTORE still end the lease normally.
                consecutiveMaintenanceFailures++;
            }
        }

        // Pump the WinEvent hooks while waiting. This remains the same 250 ms
        // maintenance cadence and adds no extra worker thread.
        controller.WaitAndPumpEvents(250);
    }
}
catch (Exception exception)
{
    exitCode = 1;
    Console.WriteLine($"ERROR: {exception.Message}");
}
finally
{
    if (ownsLease)
    {
        var restored = controller.Restore();
        if (!restored) restored = controller.EmergencyRestore();
        if (!restored) exitCode = 1;
        Console.WriteLine(restored ? "RESTORED: taskbar test ended." : "RESTORE FAILED: run GlassDock.Watchdog --restore.");
        Console.Out.Flush();
        lease.ReleaseMutex();
    }
}
return exitCode;
