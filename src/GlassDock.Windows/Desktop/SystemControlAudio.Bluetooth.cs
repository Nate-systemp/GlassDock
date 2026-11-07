using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDock.Windows.Applications;

namespace GlassDock.Windows.Desktop;

internal sealed partial class SystemControlAudio
{
    private static readonly Guid BluetoothAudioProperties = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private static readonly ApplicationNative.PropertyKey ContainerIdProperty = new()
        { Format = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };

    // A successful KS request only starts the attempt; endpoint state must confirm the result.
    internal static Task<bool?> SetBluetoothConnectionAsync(Guid containerId, bool connect, CancellationToken cancel) =>
        Task.Run<bool?>(() => SetBluetoothConnection(containerId, connect, cancel), cancel);

    private static bool? SetBluetoothConnection(Guid containerId, bool connect, CancellationToken cancel)
    {
        if (containerId == Guid.Empty || MmDeviceEnumeratorType is null) return null;
        IMMDeviceEnumerator instance;
        try { instance = (IMMDeviceEnumerator)Activator.CreateInstance(MmDeviceEnumeratorType)!; }
        catch (COMException) { return null; }
        using var enumerator = new ComLease<IMMDeviceEnumerator>(instance);
        if (enumerator.Value!.EnumAudioEndpoints(EDataFlow.All, 0xF, out var collection) < 0) return null;
        using var collectionLease = new ComLease<IMMDeviceCollection>(collection);
        Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
        var endpoints = new List<ComLease<IMMDevice>>();
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var accepted = false;
        var matched = false;
        try
        {
            for (uint index = 0; index < count; index++)
            {
                cancel.ThrowIfCancellationRequested();
                if (collection.Item(index, out var endpoint) < 0) continue;
                ComLease<IMMDevice>? lease = new(endpoint);
                try
                {
                    if (ReadContainerId(endpoint) != containerId) continue;
                    matched = true;
                    // Do not enable an endpoint the user has explicitly disabled.
                    if (endpoint.GetState(out var state) < 0 || (state & 2) != 0) continue;
                    endpoints.Add(lease); lease = null;
                    var topologyId = typeof(IDeviceTopology).GUID;
                    if (endpoint.Activate(ref topologyId, 23, 0, out var topologyObject) < 0) continue;
                    using var topology = new ComLease<IDeviceTopology>((IDeviceTopology)topologyObject);
                    if (topology.Value!.GetConnectorCount(out var connectors) < 0) continue;
                    for (uint connectorIndex = 0; connectorIndex < connectors; connectorIndex++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (topology.Value.GetConnector(connectorIndex, out var connectorObject) < 0) continue;
                        using var connector = new ComLease<IConnector>(connectorObject);
                        if (connector.Value!.GetDeviceIdConnectedTo(out var filterId) < 0 ||
                            enumerator.Value.GetDevice(filterId, out var filterObject) < 0) continue;
                        using var filter = new ComLease<IMMDevice>(filterObject);
                        var ksId = typeof(IKsControl).GUID;
                        if (filter.Value!.Activate(ref ksId, 23, 0, out var ksObject) < 0) continue;
                        using var control = new ComLease<IKsControl>((IKsControl)ksObject);
                        var property = new KsProperty { Set = BluetoothAudioProperties, Id = connect ? 0u : 1u, Flags = 0x200 };
                        var support = Marshal.AllocHGlobal(sizeof(uint));
                        try
                        {
                            Marshal.WriteInt32(support, 0);
                            if (control.Value!.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), support, sizeof(uint), out _) < 0 ||
                                (Marshal.ReadInt32(support) & 1) == 0) continue;
                        }
                        finally { Marshal.FreeHGlobal(support); }
                        if (requested.Add(filterId))
                        {
                            cancel.ThrowIfCancellationRequested();
                            property.Flags = 1; // KSPROPERTY_TYPE_GET; ONESHOT requests have no value buffer.
                            accepted |= control.Value.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), 0, 0, out _) >= 0;
                        }
                        break;
                    }
                }
                finally { lease?.Dispose(); }
            }
            if (requested.Count == 0) return matched ? false : null;
            if (!accepted) return false;
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var states = endpoints.Select(endpoint => endpoint.Value!.GetState(out var state) >= 0 ? (uint?)state : null).ToArray();
                var active = states.Any(state => state is not null && (state.Value & 1) != 0);
                if (connect ? active : states.All(state => state is not null) && !active) return true;
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(15)) return false;
                // Bounded operation-only confirmation, never an idle polling loop.
                if (cancel.WaitHandle.WaitOne(200)) cancel.ThrowIfCancellationRequested();
            }
        }
        finally { foreach (var endpoint in endpoints) endpoint.Dispose(); }
    }

    private static Guid ReadContainerId(IMMDevice endpoint)
    {
        if (endpoint.OpenPropertyStore(0, out var properties) < 0) return Guid.Empty;
        using var lease = new ComLease<ApplicationNative.IPropertyStore>(properties);
        var result = properties.GetValue(in ContainerIdProperty, out var value);
        try
        {
            return result >= 0 && value.Type == 72 && value.Value != 0
                ? Marshal.PtrToStructure<Guid>(value.Value) : Guid.Empty;
        }
        finally { ApplicationNative.PropVariantClear(ref value); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KsProperty { public Guid Set; public uint Id, Flags; }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceTopology
    {
        [PreserveSig] int GetConnectorCount(out uint count);
        [PreserveSig] int GetConnector(uint index, out IConnector connector);
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IConnector
    {
        [PreserveSig] int GetConnectorType(out int type);
        [PreserveSig] int GetDataFlow(out int flow);
        [PreserveSig] int ConnectTo(IConnector connector);
        [PreserveSig] int Disconnect();
        [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool connected);
        [PreserveSig] int GetConnectedTo(out IConnector connector);
        [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty property, uint propertyLength, nint data, uint dataLength, out uint returned);
    }
}
