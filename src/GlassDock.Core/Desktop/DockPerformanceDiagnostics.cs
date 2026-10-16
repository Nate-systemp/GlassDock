using System.Diagnostics;

namespace GlassDock.Core.Desktop;

/// <summary>
/// Low-overhead, opt-in diagnostic counters for investigating UI-thread and rendering activity.
/// Enabled via DOKY_PERF_DIAG=1 environment variable or --perf-diag command-line flag.
/// Never performs per-frame file I/O.
/// </summary>
public static class DockPerformanceDiagnostics
{
    public static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("DOKY_PERF_DIAG"), "1", StringComparison.OrdinalIgnoreCase) ||
        Environment.GetCommandLineArgs().Contains("--perf-diag", StringComparer.OrdinalIgnoreCase);

    private static long displayTimerTicks;
    private static long displayTimerTotalStopwatchTicks;
    private static long displayTimerMaxStopwatchTicks;

    private static long geometryRebuilds;
    private static long geometrySkipped;
    private static long geometryTotalStopwatchTicks;

    private static long inputRegionRebuilds;
    private static long inputRegionSkipped;

    private static long backdropUpdates;
    private static long backdropSkipped;
    private static long backdropTotalStopwatchTicks;

    private static long captureDraws;
    private static long shaderWrites, shaderWritesSkipped;
    private static long slowUiOperations;
    private static long captureSessions, captureSessionPeak, captureCallbacks, captureCallbackTicks;
    private static long emptyCaptureDraws, drawTicks, drawMaxTicks, drawQueueTicks, drawQueueMaxTicks, timedDraws;
    private static long nextReport;
    private sealed class RendererTiming
    {
        public long Count, Prepare, Render, MaxPrepare, MaxRender;
        public void Add(long prepare, long render)
        {
            Interlocked.Increment(ref Count);
            Interlocked.Add(ref Prepare, prepare); Interlocked.Add(ref Render, render);
            UpdateMax(ref MaxPrepare, prepare); UpdateMax(ref MaxRender, render);
        }
        public string Summary(string name) =>
            $"{name}:count={Interlocked.Read(ref Count)},prepareMs={Ms(Interlocked.Read(ref Prepare)):0.00},renderMs={Ms(Interlocked.Read(ref Render)):0.00},intervalMaxPrepareMs={Ms(Interlocked.Exchange(ref MaxPrepare, 0)):0.00},intervalMaxRenderMs={Ms(Interlocked.Exchange(ref MaxRender, 0)):0.00}";
    }
    private static readonly RendererTiming dockTiming = new(), popupTiming = new();
    public static void RecordRendererTiming(bool popup, long prepare, long render)
    {
        if (Enabled) (popup ? popupTiming : dockTiming).Add(prepare, render);
    }

    public static void CaptureSessionChanged(int delta)
    {
        if (!Enabled) return;
        var current = Interlocked.Add(ref captureSessions, delta);
        UpdateMax(ref captureSessionPeak, current);
    }
    public static void RecordCaptureCallback(long ticks)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref captureCallbacks);
        Interlocked.Add(ref captureCallbackTicks, ticks);
    }
    public static void RecordEmptyCaptureDraw() { if (Enabled) Interlocked.Increment(ref emptyCaptureDraws); }
    public static void RecordDrawQueueDelay(long ticks)
    {
        if (!Enabled) return;
        Interlocked.Add(ref drawQueueTicks, ticks); UpdateMax(ref drawQueueMaxTicks, ticks);
    }
    public static void RecordDrawDuration(long ticks)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref timedDraws);
        Interlocked.Add(ref drawTicks, ticks); UpdateMax(ref drawMaxTicks, ticks);
    }
    public static bool ReportDue()
    {
        if (!Enabled) return false;
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Read(ref nextReport);
        return now >= previous && Interlocked.CompareExchange(ref nextReport, now + Stopwatch.Frequency * 10, previous) == previous;
    }

    public static void RecordDisplayTimer(long elapsedStopwatchTicks)
    {
        if (!Enabled) return;
        Interlocked.Increment(ref displayTimerTicks);
        Interlocked.Add(ref displayTimerTotalStopwatchTicks, elapsedStopwatchTicks);
        UpdateMax(ref displayTimerMaxStopwatchTicks, elapsedStopwatchTicks);
        if (Stopwatch.GetElapsedTime(0, elapsedStopwatchTicks).TotalMilliseconds >= 8.0)
            Interlocked.Increment(ref slowUiOperations);
    }

    public static void RecordGeometry(bool rebuilt, long elapsedStopwatchTicks = 0)
    {
        if (!Enabled) return;
        if (rebuilt)
        {
            Interlocked.Increment(ref geometryRebuilds);
            Interlocked.Add(ref geometryTotalStopwatchTicks, elapsedStopwatchTicks);
        }
        else
        {
            Interlocked.Increment(ref geometrySkipped);
        }
    }

    public static void RecordInputRegion(bool rebuilt)
    {
        if (!Enabled) return;
        if (rebuilt) Interlocked.Increment(ref inputRegionRebuilds);
        else Interlocked.Increment(ref inputRegionSkipped);
    }

    public static void RecordBackdropUpdate(bool updated, long elapsedStopwatchTicks = 0)
    {
        if (!Enabled) return;
        if (updated)
        {
            Interlocked.Increment(ref backdropUpdates);
            Interlocked.Add(ref backdropTotalStopwatchTicks, elapsedStopwatchTicks);
        }
        else
        {
            Interlocked.Increment(ref backdropSkipped);
        }
    }

    public static void RecordCaptureDraw()
    {
        if (!Enabled) return;
        Interlocked.Increment(ref captureDraws);
    }

    public static void RecordShaderChanges(int changes)
    {
        if (!Enabled) return;
        var writes = System.Numerics.BitOperations.PopCount((uint)changes);
        Interlocked.Add(ref shaderWrites, writes);
        Interlocked.Add(ref shaderWritesSkipped, 6 - writes);
    }

    public static void RecordSlowUiOperation(long elapsedStopwatchTicks)
    {
        if (!Enabled) return;
        if (Stopwatch.GetElapsedTime(0, elapsedStopwatchTicks).TotalMilliseconds >= 8.0)
            Interlocked.Increment(ref slowUiOperations);
    }

    public static string FormatSummary()
    {
        var dispTicks = Interlocked.Read(ref displayTimerTicks);
        var dispMs = Stopwatch.GetElapsedTime(0, Interlocked.Read(ref displayTimerTotalStopwatchTicks)).TotalMilliseconds;
        var dispMaxMs = Stopwatch.GetElapsedTime(0, Interlocked.Read(ref displayTimerMaxStopwatchTicks)).TotalMilliseconds;
        var geoRebuilt = Interlocked.Read(ref geometryRebuilds);
        var geoSkip = Interlocked.Read(ref geometrySkipped);
        var geoMs = Stopwatch.GetElapsedTime(0, Interlocked.Read(ref geometryTotalStopwatchTicks)).TotalMilliseconds;
        var rgnRebuilt = Interlocked.Read(ref inputRegionRebuilds);
        var rgnSkip = Interlocked.Read(ref inputRegionSkipped);
        var bdUpdated = Interlocked.Read(ref backdropUpdates);
        var bdSkip = Interlocked.Read(ref backdropSkipped);
        var bdMs = Stopwatch.GetElapsedTime(0, Interlocked.Read(ref backdropTotalStopwatchTicks)).TotalMilliseconds;
        var draws = Interlocked.Read(ref captureDraws);
        var slow = Interlocked.Read(ref slowUiOperations);

        return $"PerfDiag: displayTimer={dispTicks} ({dispMs:0.0}ms total, {dispMaxMs:0.00}ms max) | " +
               $"geometry={geoRebuilt} rebuilt/{geoSkip} skipped ({geoMs:0.0}ms) | " +
               $"inputRgn={rgnRebuilt} rebuilt/{rgnSkip} skipped | " +
               $"backdrop={bdUpdated} updated/{bdSkip} skipped ({bdMs:0.0}ms) | " +
               $"captureDraws={draws} | shaderWrites={Interlocked.Read(ref shaderWrites)}/{Interlocked.Read(ref shaderWritesSkipped)} skipped | slowUiOps={slow} | " +
               $"captureSessions={Interlocked.Read(ref captureSessions)} peak={Interlocked.Read(ref captureSessionPeak)} callbacks={Interlocked.Read(ref captureCallbacks)} callbackMs={Ms(captureCallbackTicks):0.00} | " +
               $"emptyDrawsSkipped={Interlocked.Read(ref emptyCaptureDraws)} timedDraws={Interlocked.Read(ref timedDraws)} drawMs={Ms(drawTicks):0.00} maxDrawMs={Ms(drawMaxTicks):0.00} queueMs={Ms(drawQueueTicks):0.00} maxQueueMs={Ms(drawQueueMaxTicks):0.00} | " +
               dockTiming.Summary("dock") + " | " + popupTiming.Summary("popup");
    }
    private static double Ms(long ticks) => Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds;

    private static void UpdateMax(ref long location, long value)
    {
        long current = Interlocked.Read(ref location);
        while (value > current)
        {
            var prev = Interlocked.CompareExchange(ref location, value, current);
            if (prev == current) break;
            current = prev;
        }
    }
}
