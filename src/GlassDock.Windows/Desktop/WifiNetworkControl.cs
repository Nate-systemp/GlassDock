using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassDock.Windows.Desktop;

public sealed record WifiNetwork(Guid Adapter, string Name, string Profile, byte[] Ssid,
    uint BssType, uint Signal, bool Connected, bool Secured, bool Connectable)
{
    public bool CanConnect => Connectable && (!Secured || !string.IsNullOrEmpty(Profile));
}

/// <summary>Native Wi-Fi for unpackaged desktop apps. Never reads or stores Wi-Fi passwords.</summary>
public static class WifiNetworkControl
{
    public static Task<IReadOnlyList<WifiNetwork>> ReadAsync(bool scan, CancellationToken cancellation = default) =>
        Task.Run<IReadOnlyList<WifiNetwork>>(() =>
        {
            using var session = new Session();
            Check(WlanEnumInterfaces(session.Handle, 0, out var interfaces));
            var result = new List<WifiNetwork>();
            try
            {
                for (var i = 0; i < Marshal.ReadInt32(interfaces); i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var adapter = Marshal.PtrToStructure<Interface>(interfaces + 8 + i * Marshal.SizeOf<Interface>());
                    if (scan) session.Scan(adapter.Id, cancellation);
                    Check(WlanGetAvailableNetworkList(session.Handle, ref adapter.Id, 0, 0, out var list));
                    try
                    {
                        for (var n = 0; n < Marshal.ReadInt32(list); n++)
                        {
                            var network = Marshal.PtrToStructure<Available>(list + 8 + n * Marshal.SizeOf<Available>());
                            var bytes = network.Ssid.Bytes.Take((int)Math.Min(32, network.Ssid.Length)).ToArray();
                            if (bytes.Length == 0) continue;
                            result.Add(new(adapter.Id, Encoding.UTF8.GetString(bytes), network.Profile, bytes,
                                network.BssType, network.Signal, (network.Flags & 1) != 0,
                                network.Security != 0, network.Connectable != 0));
                        }
                    }
                    finally { WlanFreeMemory(list); }
                }
            }
            finally { WlanFreeMemory(interfaces); }
            return result.GroupBy(n => (n.Adapter, Ssid: Convert.ToHexString(n.Ssid), n.Secured))
                .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Profile.Length).First())
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ToArray();
        }, cancellation);

    public static async Task<string> ConnectAsync(WifiNetwork network, CancellationToken cancellation)
    {
        if (!network.CanConnect) return "This network requires setup or credentials. Use Open Wi-Fi settings below.";
        await Task.Run(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            using var session = new Session();
            var ssid = new Ssid { Length = (uint)network.Ssid.Length, Bytes = new byte[32] };
            network.Ssid.CopyTo(ssid.Bytes, 0);
            var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Ssid>());
            try
            {
                Marshal.StructureToPtr(ssid, memory, false);
                var parameters = new Connection
                {
                    Mode = network.Profile.Length > 0 ? 0u : 3u, // saved profile / discovery unsecured
                    Profile = network.Profile.Length > 0 ? network.Profile : null,
                    Ssid = network.Profile.Length > 0 ? 0 : memory,
                    BssType = network.BssType
                };
                var adapter = network.Adapter;
                Check(WlanConnect(session.Handle, ref adapter, ref parameters, 0));
            }
            finally { Marshal.FreeHGlobal(memory); }
        }, cancellation);
        // WlanConnect only accepts a request. Confirm the connected flag before claiming success.
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500, cancellation);
            var networks = await ReadAsync(false, cancellation);
            if (networks.Any(n => n.Adapter == network.Adapter && n.Ssid.SequenceEqual(network.Ssid) && n.Connected))
                return "Connected.";
        }
        return "Connection was not confirmed. Refresh or open Wi-Fi settings for details.";
    }

    public static string ErrorMessage(Exception error) => error is Win32Exception { NativeErrorCode: 5 }
        ? "Windows denied Wi-Fi access. Check location permission in Windows Settings."
        : "Wi-Fi is unavailable or the operation failed. Check the radio and use Refresh or Settings.";

    private static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
    private sealed class Session : IDisposable
    {
        public nint Handle { get; }
        public Session() { Check(WlanOpenHandle(2, 0, out _, out var handle)); Handle = handle; }
        public void Scan(Guid adapter, CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NotificationCallback callback = (ref Notification notification, nint context) =>
            {
                if (notification.Adapter == adapter && notification.Source == 8 && notification.Code is 7 or 8)
                    completion.TrySetResult(notification.Code == 7);
            };
            Check(WlanRegisterNotification(Handle, 8, false, callback, 0, 0, out _));
            try
            {
                Check(WlanScan(Handle, ref adapter, 0, 0, 0));
                // The WLAN notification, not a fixed sleep, gates reading fresh scan results.
                if (!completion.Task.WaitAsync(TimeSpan.FromSeconds(8), cancellation).GetAwaiter().GetResult())
                    throw new Win32Exception("Wi-Fi scan failed.");
            }
            finally
            {
                WlanRegisterNotification(Handle, 0, false, null, 0, 0, out _);
                GC.KeepAlive(callback);
            }
        }
        public void Dispose() => WlanCloseHandle(Handle, 0);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Notification
    {
        public uint Source, Code;
        public Guid Adapter;
        public uint Size;
        public nint Data;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void NotificationCallback(ref Notification notification, nint context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Interface
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Available
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Ssid Ssid;
        public uint BssType, BssidCount, Connectable, Reason, PhyCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        public uint MorePhyTypes, Signal, Security, Authentication, Cipher, Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Connection
    {
        public uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Profile;
        public nint Ssid, BssidList;
        public uint BssType, Flags;
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out nint handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(nint handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(nint handle, ref Guid adapter, uint flags, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(nint handle, ref Guid adapter, nint ssid, nint data, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(nint handle, uint source,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotificationCallback? callback, nint context, nint reserved, out uint previous);
    [DllImport("wlanapi.dll")] private static extern uint WlanConnect(nint handle, ref Guid adapter, ref Connection parameters, nint reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
}
