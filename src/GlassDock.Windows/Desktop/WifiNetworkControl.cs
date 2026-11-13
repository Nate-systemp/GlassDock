using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace GlassDock.Windows.Desktop;

public sealed record WifiNetwork(Guid Adapter, string Name, string Profile, byte[] Ssid,
    uint BssType, uint Signal, bool Connected, bool Secured, bool Connectable)
{
    public bool CanConnect => Connectable && (!Secured || !string.IsNullOrEmpty(Profile));
    public bool RequiresPassword => Secured && !Connected && string.IsNullOrEmpty(Profile);
    public uint Authentication { get; init; }
    public uint Cipher { get; init; }
    public string Key => Convert.ToHexString(Ssid) + ":" + Authentication + ":" + Cipher;
    public string Security => !Secured ? "Open network" : Authentication switch
        { 4 => "WPA personal", 7 => "WPA2 personal", 9 => "WPA3 personal", _ => "Enterprise / other security" };
}

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
                                network.Security != 0, network.Connectable != 0) { Authentication = network.Authentication, Cipher = network.Cipher });
                        }
                    }
                    finally { WlanFreeMemory(list); }
                }
            }
            finally { WlanFreeMemory(interfaces); }
            return result.GroupBy(n => n.Key)
                .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Profile.Length).First())
                .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ToArray();
        }, cancellation);

    public static Task<bool> ReadAutoConnectAsync(WifiNetwork network, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(network.Profile)) return true;
        using var session = new Session();
        var adapter = network.Adapter;
        uint flags = 0;
        Check(WlanGetProfile(session.Handle, ref adapter, network.Profile, 0, out var profileXml, ref flags, out _));
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(Marshal.PtrToStringUni(profileXml)!);
            var root = document.Root!;
            return root.Element(root.Name.Namespace + "connectionMode")?.Value == "auto";
        }
        finally { WlanFreeMemory(profileXml); }
    }, cancellation);

    public static async Task<string> ConnectAsync(WifiNetwork network, string password, bool autoConnect, CancellationToken cancellation)
    {
        if (!network.Connectable) return "Windows reports that this network cannot be connected.";
        var current = (await ReadAsync(false, cancellation)).FirstOrDefault(n => n.Key == network.Key);
        if (current is null) return "This network is no longer available. Refresh the network list.";
        network = current;
        var existing = password.Length == 0 && !string.IsNullOrEmpty(network.Profile);
        var xml = existing ? null : GlassDock.Core.Desktop.WifiProfile.Create(network.Ssid, network.Authentication, network.Cipher, password, autoConnect);
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
                    Mode = 0,
                    Profile = existing ? network.Profile : "Doky-" + Convert.ToHexString(network.Ssid),
                    Ssid = 0,
                    BssType = network.BssType
                };
                var adapter = network.Adapter;
                if (existing)
                {
                    uint flags = 0;
                    Check(WlanGetProfile(session.Handle, ref adapter, network.Profile, 0, out var profileXml, ref flags, out _));
                    try
                    {
                        var document = System.Xml.Linq.XDocument.Parse(Marshal.PtrToStringUni(profileXml)!);
                        var root = document.Root!;
                        root.SetElementValue(root.Name.Namespace + "connectionMode", autoConnect ? "auto" : "manual");
                        Check(WlanSetProfile(session.Handle, ref adapter, flags & 2, document.ToString(), null, true, 0, out _));
                    }
                    finally { WlanFreeMemory(profileXml); }
                }
                if (!existing)
                {
                    Check(WlanSetProfile(session.Handle, ref adapter, 2, xml!, null, true, 0, out var reason));
                    if (reason != 0) throw new Win32Exception("Windows rejected this Wi-Fi profile.");
                }
                Check(WlanConnect(session.Handle, ref adapter, ref parameters, 0));
            }
            finally { Marshal.FreeHGlobal(memory); }
        }, cancellation);
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500, cancellation);
            var networks = await ReadAsync(false, cancellation);
            if (networks.Any(n => n.Adapter == network.Adapter && n.Ssid.SequenceEqual(network.Ssid) && n.Connected))
                return "Connected.";
        }
        return "Connection was not confirmed. Refresh or open Wi-Fi settings for details.";
    }

    public static Task<string> DisconnectAsync(WifiNetwork network, CancellationToken cancellation) => Task.Run(async () =>
    {
        using var session = new Session();
        var adapter = network.Adapter;
        cancellation.ThrowIfCancellationRequested();
        var current = ReadConnection(session.Handle, ref adapter);
        if (IsDisconnected(network, current)) return "Disconnected.";
        if (current.State is not (1 or 2) || current.Ssid.Length == 0) return "Wi-Fi connection is changing. Try again.";
        Check(WlanDisconnect(session.Handle, ref adapter, 0));
        for (var i = 0; i < 20; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            current = ReadConnection(session.Handle, ref adapter);
            if (IsDisconnected(network, current)) return "Disconnected.";
            await Task.Delay(500, cancellation).ConfigureAwait(false);
        }
        return "Disconnection was not confirmed. Try again or open Wi-Fi settings.";
    }, cancellation);

    internal static bool DisconnectConfirmed(WifiNetwork expected, uint state, ReadOnlySpan<byte> ssid) =>
        state == 4 || (state is 1 or 2) && !ssid.IsEmpty && !ssid.SequenceEqual(expected.Ssid);

    private static bool IsDisconnected(WifiNetwork expected, ConnectionAttributes current) =>
        DisconnectConfirmed(expected, current.State, current.Ssid.Bytes.AsSpan(0, (int)current.Ssid.Length));

    private static ConnectionAttributes ReadConnection(nint handle, ref Guid adapter)
    {
        var result = WlanQueryInterface(handle, ref adapter, 7, 0, out var size, out var data, out _);
        if (result == 5023)
        {
            Check(WlanQueryInterface(handle, ref adapter, 6, 0, out size, out data, out _));
            try
            {
                if (data == 0 || size < sizeof(uint)) throw new InvalidDataException("Windows returned an incomplete Wi-Fi state.");
                return new() { State = unchecked((uint)Marshal.ReadInt32(data)) };
            }
            finally { WlanFreeMemory(data); }
        }
        Check(result);
        try
        {
            if (data == 0 || size < Marshal.SizeOf<ConnectionAttributes>()) throw new InvalidDataException("Windows returned an incomplete Wi-Fi connection.");
            var current = Marshal.PtrToStructure<ConnectionAttributes>(data);
            if (current.Ssid.Length > 32) throw new InvalidDataException("Windows returned an invalid Wi-Fi SSID.");
            return current;
        }
        finally { WlanFreeMemory(data); }
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
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionAttributes
    {
        public uint State, Mode;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Ssid Ssid;
    }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, nint reserved, out uint negotiated, out nint handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(nint handle, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(nint handle, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(nint handle, ref Guid adapter, uint flags, nint reserved, out nint list);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(nint handle, ref Guid adapter, nint ssid, nint data, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(nint handle, uint source,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotificationCallback? callback, nint context, nint reserved, out uint previous);
    [DllImport("wlanapi.dll")] private static extern uint WlanConnect(nint handle, ref Guid adapter, ref Connection parameters, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(nint handle, ref Guid adapter, nint reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(nint handle, ref Guid adapter, uint opcode,
        nint reserved, out uint size, out nint data, out uint valueType);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanSetProfile(nint handle,
        ref Guid adapter, uint flags, string xml, string? security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, nint reserved, out uint reason);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanGetProfile(nint handle,
        ref Guid adapter, string name, nint reserved, out nint xml, ref uint flags, out uint access);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(nint memory);
}
