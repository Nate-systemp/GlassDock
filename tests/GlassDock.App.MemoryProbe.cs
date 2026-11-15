using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDock.App.Controls;
using GlassDock.App.Desktop;
using GlassDock.App.ViewModels;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;

namespace GlassDock.App.Diagnostics;

internal static class MemoryLifecycleProbe
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Field(object owner, string name)
    {
        for (var type = owner.GetType(); type is not null; type = type.BaseType)
            if (type.GetField(name, Fields) is { } field) return field.GetValue(owner);
        throw new MissingFieldException(owner.GetType().Name, name);
    }
    private static object? Call(object owner, string name, params object?[] args) =>
        owner.GetType().GetMethod(name, Fields)!.Invoke(owner, args);

    internal static async Task RunAsync(MonitorDockCoordinator coordinator, GlassDockSettingsSession session)
    {
        var output = Environment.GetEnvironmentVariable("DOKY_MEMORY_PROBE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = Path.Combine(Path.GetTempPath(), "doky-memory-probe.jsonl");
        var saved = session.Current;
        var idleBenchmark = Environment.GetCommandLineArgs().Contains("--idle-memory-probe", StringComparer.OrdinalIgnoreCase);
        var dock = coordinator.PrimaryWindow;
        var references = new List<(string Kind, WeakReference Reference)>();
        var retainedPreviewResources = 0;
        long CaptureCount() => (long)typeof(DockPerformanceDiagnostics)
            .GetField("captureSessions", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var baselineCaptures = 0L;
        void Track(string kind, object? instance)
        {
            ArgumentNullException.ThrowIfNull(instance, kind);
            references.Add((kind, new WeakReference(instance)));
        }
        void Log(object value) => File.AppendAllText(output, JsonSerializer.Serialize(value) + Environment.NewLine);
        void SampleIdle(string stage, int cycle)
        {
            using var process = Process.GetCurrentProcess();
            var memory = new ProcessMemoryCounters { Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>() };
            if (!GetProcessMemoryInfo(process.Handle, ref memory, memory.Size))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Log(new { stage, cycle, pid = Environment.ProcessId,
                taskManagerBytes = (long)memory.PrivateWorkingSetSize,
                privateBytes = process.PrivateMemorySize64, workingSet = process.WorkingSet64,
                managedBytes = GC.GetTotalMemory(false), committedHeap = GC.GetGCMemoryInfo().TotalCommittedBytes });
        }
        async Task WaitClosed(Func<bool> isOpen, string kind)
        {
            var timeout = Stopwatch.StartNew();
            while (isOpen())
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(5))
                    throw new InvalidOperationException(kind + " did not close.");
                await Task.Delay(50);
            }
        }
        async Task Checkpoint(int cycle)
        {
            await Task.Delay(1200);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            Log(new { stage = "before-gc", cycle, privateBytes = process.PrivateMemorySize64,
                workingSet = process.WorkingSet64, managedBytes = GC.GetTotalMemory(false) });
            for (var pass = 0; pass < 3; pass++)
            {
                await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });
                await Task.Delay(700);
            }
            await Task.Delay(700);
            process.Refresh();
            var alive = references.GroupBy(r => r.Kind)
                .ToDictionary(g => g.Key, g => g.Count(r => r.Reference.IsAlive));
            Log(new { stage = "after-gc", cycle, privateBytes = process.PrivateMemorySize64,
                workingSet = process.WorkingSet64, managedBytes = GC.GetTotalMemory(false),
                committedHeap = GC.GetGCMemoryInfo().TotalCommittedBytes,
                captures = CaptureCount(),
                alive, retainedPreviewResources,
                handles = process.HandleCount });
            if (cycle == 20 && alive.Values.Any(count => count != 0))
                throw new InvalidOperationException("Closed popup instances remain alive after collection.");
            if (cycle == 20 && retainedPreviewResources != 0)
                throw new InvalidOperationException("Hidden Settings retained preview graphics resources.");
            if (cycle > 0 && CaptureCount() != baselineCaptures)
                throw new InvalidOperationException("Closed popups retained capture sessions.");
        }
        async Task Popup(string method, string field, UtilityKind kind)
        {
            Call(dock, "OpenQuickSettings");
            await Task.Delay(350);
            var buttons = (Dictionary<UtilityKind, UtilityButton>)Field(dock, "utilityButtons")!;
            Call(dock, method, buttons[kind]);
            await Task.Delay(450);
            var popup = Field(dock, field) as UtilityPopupWindow;
            if (popup is null)
            {
                Call(dock, "CloseQuickSettings", true);
                Call(dock, "OpenQuickSettings");
                Call(dock, method, buttons[kind]);
                popup = Field(dock, field) as UtilityPopupWindow;
            }
            if (popup is null) throw new InvalidOperationException(method + " did not open a popup.");
            Track(field, popup); Track(field + "-panel", Field(popup, "panel"));
            await ((IUtilityPanel)Field(popup, "panel")!).RefreshAsync();
            popup.Hide(); popup = null;
            await Task.Delay(450);
            await WaitClosed(() => Field(dock, field) is not null, field);
            Call(dock, "CloseQuickSettings", false);
            await Task.Delay(400);
        }
        try
        {
            if (idleBenchmark)
            {
                Log(new { stage = "benchmark-start", pid = Environment.ProcessId });
                await Task.Delay(60000);
                SampleIdle("startup-idle", 0);
            }
            else await Task.Delay(8000);
            session.Replace(saved with { PinDock = true, UtilityDisplayMode = UtilityDisplayMode.Grid,
                SelectedUtilities = [UtilityKind.Wifi, UtilityKind.Bluetooth, UtilityKind.Volume, UtilityKind.Brightness] });
            await Task.Delay(3000);
            Log(new { stage = "start", pid = Environment.ProcessId, mode = session.Current.DockAppearanceMode.ToString(), cycles = 20 });
            if (!idleBenchmark) await Checkpoint(0);
            baselineCaptures = CaptureCount();
            for (var cycle = 1; cycle <= 20; cycle++)
            {
                dock.ShowSettings();
                await Task.Delay(350);
                var slot = (RetainedWindowSlot<SettingsWindow>)Field(dock, "settingsWindow")!;
                var settings = slot.Current!;
                foreach (var mode in new[] { DockAppearanceMode.Clear, DockAppearanceMode.Acrylic, DockAppearanceMode.Dark })
                {
                    Call(settings, "SelectBuiltInGlass", mode);
                    await Task.Delay(80);
                }
                settings.AppWindow.Hide();
                await Task.Delay(250);
                var preview = Field(settings, "glassPreview")!;
                if (new[] { "scene", "shader", "blur", "native" }.Any(name => Field(preview, name) is not null))
                    retainedPreviewResources++;
                Call(dock, "OpenQuickSettings");
                await Task.Delay(400);
                Track("quickSettingsWindow", Field(dock, "quickSettingsWindow"));
                Track("quickSettingsPanel", Field(dock, "quickSettingsPanel"));
                Call(dock, "CloseQuickSettings", false);
                await Task.Delay(400);
                await Popup("ShowWifiNetworks", "wifiWindow", UtilityKind.Wifi);
                await Popup("ShowBluetoothDevices", "bluetoothWindow", UtilityKind.Bluetooth);
                foreach (var name in new[] { "volumeHud", "brightnessHud" })
                {
                    var shown = name == "volumeHud"
                        ? Call(coordinator, "ShowVolume", dock, true, false)
                        : Call(coordinator, "ShowBrightness", dock, true, null, false);
                    if (shown is not true) { Log(new { stage = "unavailable", cycle, kind = name }); continue; }
                    await Task.Delay(400);
                    var hud = (UtilityHudWindow)Field(coordinator, name)!;
                    hud.Hide();
                    await Task.Delay(350);
                    await WaitClosed(() => hud.IsOpen, name);
                }
                var applications = (DockApplicationsViewModel)Field(dock, "applications")!;
                var item = applications.VisibleDockApplications.FirstOrDefault(a => a.IsRunning);
                if (item is not null)
                {
                    var previews = (WindowPreviewCoordinator)Field(dock, "previews")!;
                    previews.ShowAppActions(item, 400);
                    await Task.Delay(300);
                    if (Field(previews, "appMenu") is not DockAppContextMenuWindow menu)
                        throw new InvalidOperationException("Application menu did not open.");
                    menu.Hide();
                    await Task.Delay(300);
                    Call(previews, "Enter", item, true);
                    await Task.Delay(400);
                    previews.Hide();
                    await Task.Delay(400);
                }
                Log(new { stage = "cycle", cycle });
                if (idleBenchmark && cycle is 1 or 5 or 10) SampleIdle("interaction", cycle);
                if (idleBenchmark && cycle == 20)
                {
                    session.Replace(saved);
                    await Task.Delay(60000);
                    SampleIdle("post-interaction-idle", cycle);
                    await Checkpoint(cycle);
                }
                else if (!idleBenchmark && cycle is 1 or 5 or 10 or 20) await Checkpoint(cycle);
            }
            Log(new { stage = "complete" });
        }
        catch (Exception error) { Log(new { stage = "failed", error = error.ToString() }); }
        finally
        {
            Call(dock, "CloseQuickSettings", true);
            session.Replace(saved);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessMemoryCounters
    {
        public uint Size, PageFaultCount;
        public nuint PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        public nuint PrivateUsage, PrivateWorkingSetSize, SharedCommitUsage;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(nint process, ref ProcessMemoryCounters counters, uint size);
}

