using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace GlassDock.Windows.Desktop;

public sealed class NativeBrightnessOsdLease : IDisposable
{
    private nint mapping, view;
    private long observedRequests;
    private long observedVolumeRequests;
    private readonly TaskCompletionSource replacementReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private NativeOsdBridgeHost? host;
    private bool disposed, stopping;
    private readonly int processId = Environment.ProcessId;
    private readonly int session;
    private readonly long created;
    public bool Enabled => view != 0 && !disposed && !stopping && (host?.Available ?? true);
    private string status = "Native brightness suppression is disabled.";
    public string Status => host?.Status ?? status;
    public bool BridgeActive => Enabled && host is not null && Marshal.ReadInt32(view, 88) == 1;
    public int EngineProcessId => Enabled && host is not null ? Marshal.ReadInt32(view, 92) : 0;

    public NativeBrightnessOsdLease(bool enable = true)
    {
        using var process = Process.GetCurrentProcess();
        session = process.SessionId;
        created = process.StartTime.ToUniversalTime().ToFileTimeUtc();
        if (!enable || Environment.GetEnvironmentVariable("DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD") == "0") return;
        var bridgeId = processId;
        var bridgeCreated = created;
        var external = Environment.GetCommandLineArgs().Contains("--legacy-osd-supervisor", StringComparer.OrdinalIgnoreCase) &&
            Environment.GetEnvironmentVariable("DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD") == "1" &&
            int.TryParse(Environment.GetEnvironmentVariable("DOKY_BRIGHTNESS_OSD_BRIDGE_PID"), out bridgeId) && bridgeId > 0 &&
            long.TryParse(Environment.GetEnvironmentVariable("DOKY_BRIGHTNESS_OSD_BRIDGE_CREATED"), out bridgeCreated) && bridgeCreated > 0;
        if (!external) { bridgeId = processId; bridgeCreated = created; }
        var directory = Path.Combine(AppContext.BaseDirectory, "NativeOsdRuntime");
        if (!external && !File.Exists(Path.Combine(directory, "windhawk.exe")))
        {
            status = "Native OSD runtime is not installed.";
            return;
        }
        var mappingName = external ? $"Local\\Doky.BrightnessOsd.v1.{session}" : $"Local\\Doky.NativeOsd.v2.{session}.{processId}.{created}";
        nint descriptor = 0;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var sid = identity.User?.Value ?? throw new InvalidOperationException("Current Windows SID unavailable.");
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW($"D:P(A;;GA;;;SY)(A;;GA;;;{sid})", 1, out descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var security = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            mapping = CreateFileMappingW(-1, ref security, 4, 0, 128, mappingName);
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
            status = "Native brightness bridge lease is available.";
            if (!external) host = new(directory, mappingName, processId, created, session, replacementReady.Task);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            status = "Windows retains brightness OSD handling: " + error.Message;
            Dispose();
        }
        finally { if (descriptor != 0) LocalFree(descriptor); }
    }

    public bool Pulse(bool replacementReady) => Pulse(replacementReady, 12, 32, 40, ref observedRequests);

    public bool PulseVolume(bool replacementReady) => Pulse(replacementReady, 64, 72, 80, ref observedVolumeRequests);

    private bool Pulse(bool replacementReady, int readyOffset, int deadlineOffset, int requestsOffset, ref long observed)
    {
        if (view == 0) return false;
        if (stopping) return false;
        if (host is not null && Marshal.ReadInt32(view, 104) != 0)
        {
            _ = StopAsync();
            return false;
        }
        if (replacementReady)
        {
            Marshal.WriteInt64(view, deadlineOffset, Environment.TickCount64 + 3000);
            Thread.MemoryBarrier();
            Marshal.WriteInt32(view, readyOffset, 1);
            this.replacementReady.TrySetResult();
        }
        else
        {
            Marshal.WriteInt32(view, readyOffset, 0);
            Thread.MemoryBarrier();
            Marshal.WriteInt64(view, deadlineOffset, 0);
        }
        var requests = Marshal.ReadInt64(view, requestsOffset);
        var changed = requests != observed;
        observed = requests;
        return changed;
    }

    public void Dispose()
    {
        if (disposed) return;
        Revoke();
        host?.Dispose();
        if (view != 0) { UnmapViewOfFile(view); view = 0; }
        if (mapping != 0) { CloseHandle(mapping); mapping = 0; }
        disposed = true;
    }

    private void Revoke()
    {
        if (view == 0) return;
        Marshal.WriteInt32(view, 12, 0);
        Marshal.WriteInt32(view, 64, 0);
        Thread.MemoryBarrier();
        Marshal.WriteInt64(view, 32, 0);
        Marshal.WriteInt64(view, 72, 0);
    }

    public Task StopAsync()
    {
        stopping = true;
        Revoke();
        return host?.StopAsync() ?? Task.CompletedTask;
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
