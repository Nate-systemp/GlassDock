using System.ComponentModel;
using System.Management;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

internal sealed class BrightnessHardware : IDisposable
{
    internal sealed record Target(string Id, string Name, nint Monitor, string Backend,
        string? ReadPath = null, string? WritePath = null, byte[]? Levels = null, nint Physical = 0);
    private readonly List<nint> physicalHandles = [];
    private readonly ManagementScope scope = new(@"\\.\root\wmi", new ConnectionOptions { Timeout = TimeSpan.FromSeconds(2) });
    private ManagementEventWatcher? watcher;
    internal Target[] Targets { get; private set; } = [];

    internal void Watch(Action changed)
    {
        try
        {
            watcher = new(scope, new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            watcher.EventArrived += (_, e) => { using (e.NewEvent) changed(); };
            watcher.Start();
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException)
        { watcher?.Dispose(); watcher = null; System.Diagnostics.Debug.WriteLine(error.Message); }
    }

    internal string TopologyKey() => string.Join('|', Screens().Select(s => $"{s.Handle}:{s.Device}:{string.Join(',', s.Interfaces)}"));

    internal void Enumerate()
    {
        ReleaseMonitors();
        var panels = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        var methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var methodSearch = Search("SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
            using var methodResults = methodSearch.Get();
            foreach (ManagementObject value in methodResults)
                using (value) methods[InstanceKey((string)value["InstanceName"])] = value.Path.RelativePath;
            using var brightnessSearch = Search("SELECT * FROM WmiMonitorBrightness WHERE Active=True");
            using var brightnessResults = brightnessSearch.Get();
            foreach (ManagementObject value in brightnessResults)
                using (value)
                {
                    var instance = (string)value["InstanceName"];
                    var key = InstanceKey(instance);
                    if (methods.TryGetValue(key, out var method))
                        panels[key] = new("wmi:" + instance, key, 0, "WMI", value.Path.RelativePath, method, (byte[])value["Level"]);
                }
        }
        catch (Exception error) when (error is ManagementException or COMException or UnauthorizedAccessException or InvalidOperationException)
        { System.Diagnostics.Debug.WriteLine($"Brightness WMI discovery: {error.Message}"); }

        var targets = new List<Target>();
        foreach (var screen in Screens())
        {
            var matches = panels.Where(p => screen.Interfaces.Contains(p.Key, StringComparer.OrdinalIgnoreCase)).Select(p => p.Value).ToArray();
            if (matches.Length > 0)
            {
                targets.AddRange(matches.Select(p => p with { Monitor = screen.Handle, Name = screen.Device + " · Built-in display" }));
                continue;
            }
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(screen.Handle, out var count) || count is 0 or > 32)
            {
                targets.Add(new("unsupported:" + screen.Device, screen.Device, screen.Handle, "Unsupported"));
                continue;
            }
            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(screen.Handle, count, monitors))
            {
                targets.Add(new("unsupported:" + screen.Device, screen.Device, screen.Handle, "Unsupported"));
                continue;
            }
            for (var i = 0; i < monitors.Length; i++)
            {
                physicalHandles.Add(monitors[i].Handle);
                var identity = string.Join('+', screen.Interfaces);
                targets.Add(new($"ddc:{screen.Device}:{identity}:{i}",
                    screen.Device + " · " + monitors[i].Description, screen.Handle, "DDC/CI", Physical: monitors[i].Handle));
            }
        }
        Targets = targets.ToArray();
    }

    private ManagementObjectSearcher Search(string query) => new(scope, new ObjectQuery(query),
        new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = true, Rewindable = false });

    internal int Read(Target target)
    {
        if (target.ReadPath is not null)
        {
            using var value = new ManagementObject(scope, new ManagementPath(target.ReadPath),
                new ObjectGetOptions(null, TimeSpan.FromSeconds(2), false));
            value.Get();
            var percent = Convert.ToInt32(value["CurrentBrightness"]);
            if (percent is < 0 or > 100) throw new InvalidOperationException("Invalid hardware brightness value.");
            return percent;
        }
        if (target.Physical == 0) throw new NotSupportedException("Display exposes neither WMI brightness nor a physical DDC/CI endpoint.");
        if (!GetMonitorBrightness(target.Physical, out var min, out var current, out var max)) throw new Win32Exception(Marshal.GetLastWin32Error(), "DDC/CI brightness read failed; the monitor may be busy or unsupported.");
        return BrightnessRange.Percent(min, current, max);
    }

    internal int Write(Target target, int percent)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (target.WritePath is not null)
        {
            using var method = new ManagementObject(scope, new ManagementPath(target.WritePath), null);
            using var input = method.GetMethodParameters("WmiSetBrightness");
            input["Timeout"] = (uint)0;
            input["Brightness"] = BrightnessRange.NearestLevel(percent, target.Levels);
            using var result = method.InvokeMethod("WmiSetBrightness", input, new InvokeMethodOptions(null, TimeSpan.FromSeconds(2)));
            var returned = result?.Properties.Cast<PropertyData>().FirstOrDefault(p => p.Name == "ReturnValue")?.Value;
            if (!WmiWriteAccepted(returned)) throw new InvalidOperationException("Windows rejected hardware brightness change.");
        }
        else
        {
            if (target.Physical == 0) throw new NotSupportedException("Brightness is unavailable on this display.");
            if (!GetMonitorBrightness(target.Physical, out var min, out _, out var max)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!SetMonitorBrightness(target.Physical, BrightnessRange.NativeValue(percent, min, max))) throw new Win32Exception(Marshal.GetLastWin32Error(), "Display rejected DDC/CI brightness change.");
        }
        for (var attempt = 0; ; attempt++)
        {
            try { return Read(target); }
            catch (Win32Exception) when (attempt < 3) { Thread.Sleep(80); }
        }
    }

    internal static string InstanceKey(string instance)
    {
        var key = instance;
        if (key.StartsWith(@"\\?\", StringComparison.Ordinal)) key = key[4..];
        if (key.Contains('#')) key = string.Join('\\', key.Split('#').Take(3));
        else
        {
            var suffix = key.LastIndexOf('_');
            if (suffix >= 0 && int.TryParse(key[(suffix + 1)..], out _)) key = key[..suffix];
        }
        return key;
    }

    internal static bool WmiWriteAccepted(object? result) => result switch
    {
        null => true,
        bool accepted => accepted,
        uint code => code == 0,
        int code => code == 0,
        _ => false
    };

    private sealed record Screen(nint Handle, string Device, string[] Interfaces);
    private static IEnumerable<Screen> Screens()
    {
        foreach (var monitor in WindowsMonitorService.GetMonitors())
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor.Handle, ref info)) continue;
            var interfaces = new List<string>();
            for (uint i = 0; i < 32; i++)
            {
                var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevicesW(info.Device, i, ref device, 1)) break;
                if ((device.State & 1) != 0 && !string.IsNullOrWhiteSpace(device.Id)) interfaces.Add(InstanceKey(device.Id));
            }
            yield return new(monitor.Handle, info.Device, interfaces.ToArray());
        }
    }
    private void ReleaseMonitors()
    {
        foreach (var handle in physicalHandles) if (handle != 0) DestroyPhysicalMonitor(handle);
        physicalHandles.Clear(); Targets = [];
    }
    public void Dispose()
    {
        if (watcher is not null)
        {
            try { watcher.Stop(); } catch (Exception error) when (error is ManagementException or COMException) { System.Diagnostics.Debug.WriteLine(error); }
            watcher.Dispose(); watcher = null;
        }
        ReleaseMonitors();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor { public nint Handle; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo { public int Size; public int Left, Top, Right, Bottom, WorkLeft, WorkTop, WorkRight, WorkBottom; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice { public int Size; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description; public uint State; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayDevicesW(string device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, out uint count);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, [Out] PhysicalMonitor[] monitors);
    [DllImport("dxva2.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyPhysicalMonitor(nint monitor);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorBrightness(nint monitor, out uint min, out uint current, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMonitorBrightness(nint monitor, uint value);
}

internal static class BrightnessRange
{
    internal static int Percent(uint min, uint current, uint max)
    {
        if (max <= min || current < min || current > max) throw new InvalidOperationException("Invalid DDC/CI brightness range.");
        return (int)Math.Round((current - (double)min) * 100 / (max - (double)min));
    }
    internal static uint NativeValue(int percent, uint min, uint max)
    {
        if (percent is < 0 or > 100 || max <= min) throw new ArgumentOutOfRangeException(nameof(percent));
        return (uint)Math.Round(min + (max - (double)min) * percent / 100);
    }
    internal static byte NearestLevel(int percent, byte[]? levels)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        return levels?.Where(l => l <= 100).OrderBy(l => Math.Abs(l - percent)).FirstOrDefault((byte)percent) ?? (byte)percent;
    }
}
