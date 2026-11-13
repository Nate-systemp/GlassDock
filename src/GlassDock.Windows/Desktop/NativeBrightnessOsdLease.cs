using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GlassDock.Windows.Desktop;

public sealed class NativeBrightnessOsdLease : IDisposable
{
    private nint mapping, view;
    private long observedRequests;
    private readonly int processId = Environment.ProcessId;
    private readonly int session = Process.GetCurrentProcess().SessionId;
    private readonly long created = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToFileTimeUtc();
    private readonly int bridgeId = int.TryParse(Environment.GetEnvironmentVariable("DOKY_BRIGHTNESS_OSD_BRIDGE_PID"), out var id) ? id : 0;
    private readonly long bridgeCreated = long.TryParse(Environment.GetEnvironmentVariable("DOKY_BRIGHTNESS_OSD_BRIDGE_CREATED"), out var time) ? time : 0;
    public bool Enabled => view != 0;
    public string Status { get; private set; } = "Experimental native brightness suppression is disabled.";

    public NativeBrightnessOsdLease()
    {
        if (Environment.GetEnvironmentVariable("DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD") != "1") return;
        nint descriptor = 0;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Current Windows SID unavailable.");
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW($"D:P(A;;GA;;;SY)(A;;GA;;;{sid})", 1, out descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            mapping = CreateFileMappingW(-1, ref security, 4, 0, 128, $"Local\\Doky.BrightnessOsd.v1.{session}");
            var error = Marshal.GetLastWin32Error();
            if (mapping == 0 || error == 183) throw new Win32Exception(error);
            view = MapViewOfFile(mapping, 6, 0, 0, 128);
            if (view == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Marshal.WriteInt64(view, 0, 1);
            Marshal.WriteInt32(view, 8, 1);
            Marshal.WriteInt32(view, 16, processId);
            Marshal.WriteInt32(view, 20, session);
            Marshal.WriteInt64(view, 24, created);
            Marshal.WriteInt32(view, 48, bridgeId);
            Marshal.WriteInt64(view, 56, bridgeCreated);
            Thread.MemoryBarrier();
            Marshal.WriteInt64(view, 0, 2);
            Pulse(false);
            Status = "Experimental brightness bridge lease is available.";
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Status = "Windows retains brightness OSD handling: " + error.Message;
            Dispose();
        }
        finally { if (descriptor != 0) LocalFree(descriptor); }
    }

    public bool Pulse(bool replacementReady)
    {
        if (view == 0) return false;
        if (replacementReady)
        {
            Marshal.WriteInt64(view, 32, Environment.TickCount64 + 3000);
            Thread.MemoryBarrier();
            Marshal.WriteInt32(view, 12, 1);
        }
        else
        {
            Marshal.WriteInt32(view, 12, 0);
            Thread.MemoryBarrier();
            Marshal.WriteInt64(view, 32, 0);
        }
        var requests = Marshal.ReadInt64(view, 40);
        var changed = requests != observedRequests;
        observedRequests = requests;
        return changed;
    }

    public void Dispose()
    {
        if (view != 0) { Pulse(false); UnmapViewOfFile(view); view = 0; }
        if (mapping != 0) { CloseHandle(mapping); mapping = 0; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Size; public nint Descriptor; public int Inherit; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out nint descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateFileMappingW(nint file, ref SecurityAttributes attributes, uint protection, uint high, uint low, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint MapViewOfFile(nint mapping, uint access, uint high, uint low, nuint size);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnmapViewOfFile(nint view);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
