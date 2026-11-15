using System.Runtime.InteropServices;
using GlassDock.Core.Settings;

namespace GlassDock.Windows.Desktop;

internal sealed class UtilityKeyInput : IDisposable
{
    private readonly UtilityKeyState keys;
    private readonly Dictionary<nint, nint> descriptors = [];
    private readonly bool registered;
    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<Header>();

    internal UtilityKeyInput(nint window, Action<UtilityKind, bool> changed)
    {
        keys = new(changed);
        Device[] devices = [new(1, 6, 0x2100, window), new(0xC, 1, 0x2100, window)];
        registered = RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>());
    }

    internal void Reset() => keys.Reset();

    internal void Remove(nint device)
    {
        keys.Remove(device);
        if (descriptors.Remove(device, out var descriptor)) Marshal.FreeHGlobal(descriptor);
    }

    internal void Read(nint input)
    {
        uint size = 0;
        if (GetRawInputData(input, 0x10000003, 0, ref size, HeaderSize) != 0 || size < HeaderSize || size > 1024 * 1024) return;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(input, 0x10000003, buffer, ref size, HeaderSize) != size) return;
            var header = Marshal.PtrToStructure<Header>(buffer);
            if (header.Size > size || header.Size < HeaderSize) return;
            var payload = buffer + (int)HeaderSize;
            var length = header.Size - HeaderSize;
            if (header.Type == 1 && length >= 16)
            {
                keys.Keyboard(header.Handle, (ushort)Marshal.ReadInt16(payload, 6), (Marshal.ReadInt16(payload, 2) & 1) == 0);
            }
            else if (header.Type == 2 && length >= 8)
            {
                var reportSize = (uint)Marshal.ReadInt32(payload);
                var count = (uint)Marshal.ReadInt32(payload, 4);
                if (reportSize == 0 || reportSize > length - 8 || count == 0 || count > (length - 8) / reportSize) return;
                var descriptor = Descriptor(header.Handle);
                if (descriptor == 0) return;
                var capacity = HidP_MaxUsageListLength(0, 0xC, descriptor);
                if (capacity == 0 || capacity > 65536) return;
                var usages = new ushort[capacity];
                var report = new byte[reportSize];
                for (uint i = 0; i < count; i++)
                {
                    Marshal.Copy(payload + 8 + (int)(i * reportSize), report, 0, report.Length);
                    var usageCount = capacity;
                    var status = HidP_GetUsages(0, 0xC, 0, usages, ref usageCount, descriptor, report, reportSize);
                    if (status == 0x110000 && usageCount <= capacity)
                        keys.Update(header.Handle, report[0], usages.Take((int)usageCount));
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private nint Descriptor(nint device)
    {
        if (descriptors.TryGetValue(device, out var cached)) return cached;
        uint size = 0;
        if (GetRawInputDeviceInfo(device, 0x20000005, 0, ref size) == uint.MaxValue || size == 0 || size > 1024 * 1024) return 0;
        var descriptor = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfo(device, 0x20000005, descriptor, ref size) == uint.MaxValue)
        {
            Marshal.FreeHGlobal(descriptor);
            return 0;
        }
        descriptors.Add(device, descriptor);
        return descriptor;
    }

    public void Dispose()
    {
        if (registered)
        {
            Device[] devices = [new(1, 6, 1, 0), new(0xC, 1, 1, 0)];
            RegisterRawInputDevices(devices, 2, (uint)Marshal.SizeOf<Device>());
        }
        keys.Reset();
        foreach (var descriptor in descriptors.Values) Marshal.FreeHGlobal(descriptor);
        descriptors.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Device(ushort page, ushort usage, uint flags, nint window)
    {
        public readonly ushort Page = page, Usage = usage;
        public readonly uint Flags = flags;
        public readonly nint Window = window;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public uint Type, Size; public nint Handle; public nuint Parameter; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] Device[] devices, uint count, uint size);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll")]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
    [DllImport("hid.dll")]
    private static extern uint HidP_MaxUsageListLength(int type, ushort page, nint descriptor);
    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int type, ushort page, ushort collection, [Out] ushort[] usages,
        ref uint count, nint descriptor, [In] byte[] report, uint length);
}
