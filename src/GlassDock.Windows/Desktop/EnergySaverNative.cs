using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace GlassDock.Windows.Desktop;

internal readonly record struct EnergySaverOverride(uint Value, uint Stamp);

internal sealed class EnergySaverNative : IDisposable
{
    private const ulong OverrideName = 0x41C6013DA3BC3075;
    private readonly EnergySaverStatusSubscription monitor;

    internal EnergySaverNative(Action<uint?> changed)
    {
        var path = Path.Combine(Environment.SystemDirectory, "SettingsHandlers_OneCore_BatterySaver.dll");
        var kernel = Path.Combine(Environment.SystemDirectory, "ntoskrnl.exe");
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100) ||
            FileVersionInfo.GetVersionInfo(path).FileBuildPart != 26100 ||
            FileVersionInfo.GetVersionInfo(path).FilePrivatePart != 9818 ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) !=
                "10D369BEC2623A23C92364060B0D1424B0BA0BA29BE65BD852B236D53A107FA8" ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(kernel))) !=
                "1DD53F287E2B1ED6B85044CEF0D3F55607240319E9C9842BC4B24AE353E55510")
            throw new NotSupportedException("Energy Saver private API is enabled only for the verified 26100.9818 x64 binary.");
        monitor = new(changed);
    }

    internal EnergySaverOverride ReadOverride()
    {
        var name = OverrideName;
        uint size = sizeof(uint);
        Check(NtQueryWnfStateData(in name, 0, 0, out var stamp, out var value, ref size));
        if (size != sizeof(uint) || value > 2) throw new NotSupportedException("Unknown Energy Saver override payload.");
        return new(value, stamp);
    }

    internal EnergySaverOverride Publish(EnergySaverOverride expected, uint value)
    {
        if (value > 2) throw new ArgumentOutOfRangeException(nameof(value));
        if (ReadOverride() != expected) throw new InvalidOperationException("Windows changed Energy Saver concurrently; request canceled.");
        Check(RtlPublishWnfStateData(OverrideName, 0, in value, sizeof(uint), 0));
        var actual = ReadOverride();
        if (actual.Value != value) throw new InvalidOperationException("Windows replaced the Energy Saver override.");
        return actual;
    }

    internal async Task<uint> ReadStatusAsync(CancellationToken cancellation)
    {
        using var probe = new EnergySaverStatusSubscription();
        return await probe.Initial.WaitAsync(cancellation).ConfigureAwait(false);
    }

    internal async Task<uint> ConfirmStatusAsync(bool on, CancellationToken cancellation)
    {
        while (true)
        {
            var version = monitor.Version;
            var actual = await ReadStatusAsync(cancellation).ConfigureAwait(false);
            if ((actual != 0) == on) return actual;
            await monitor.WaitForChangeAsync(version, cancellation).ConfigureAwait(false);
        }
    }

    internal bool AutomaticThresholdActive()
    {
        if (!GetSystemPowerStatus(out var power)) throw new Win32Exception();
        if (power.Ac == 1) return false;
        if (power.Flags == 255) throw new NotSupportedException("Battery presence is unknown.");
        if ((power.Flags & 128) != 0) return false;
        if (power.Ac != 0 || power.Percent > 100) throw new NotSupportedException("Battery conditions are unknown.");
        var subgroup = new Guid("DE830923-A562-41AF-A086-E3A2C6BAD2DA");
        var threshold = new Guid("E69653CA-CF7F-4F05-AA73-CB833FA90AD4");
        var error = PowerGetActiveScheme(0, out var scheme);
        if (error != 0) throw new Win32Exception((int)error);
        try
        {
            error = PowerReadDCValueIndex(0, scheme, in subgroup, in threshold, out var percent);
            if (error != 0) throw new Win32Exception((int)error);
            if (percent > 100) throw new NotSupportedException("Unknown Energy Saver battery threshold.");
            return percent > 0 && power.Percent <= percent;
        }
        finally { LocalFree(scheme); }
    }

    internal static uint SelectOverride(bool desiredOn, uint current, uint? previous, bool automaticActive)
    {
        if (current > 2 || previous > 2) throw new NotSupportedException("Unknown override value.");
        if (desiredOn) return 1;
        if (current == 1 && previous != 0 && !automaticActive) return 2;
        return 0;
    }

    private static void Check(int status)
    {
        if (status >= 0) return;
        if (status == unchecked((int)0xC0000022)) throw new UnauthorizedAccessException("Windows denied WNF access.");
        throw new COMException($"Energy Saver WNF failed (NTSTATUS 0x{status:X8}).", status);
    }

    public void Dispose() => monitor.Dispose();

    [StructLayout(LayoutKind.Sequential)] private struct PowerStatus
    { public byte Ac, Flags, Percent, Saver; public uint Life, FullLife; }
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern int NtQueryWnfStateData(in ulong name, nint type, nint scope, out uint stamp, out uint value, ref uint size);
    [DllImport("ntdll.dll", ExactSpelling = true)] private static extern int RtlPublishWnfStateData(ulong name, nint type, in uint value, uint size, nint scope);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemPowerStatus(out PowerStatus status);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(nint root, out nint scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(nint root, nint scheme, in Guid subgroup, in Guid setting, out uint value);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}

internal sealed class EnergySaverStatusSubscription : IDisposable
{
    internal static readonly Guid StatusGuid = new("550E8400-E29B-41D4-A716-446655440000");
    private readonly object sync = new();
    private readonly PowerCallback callback;
    private readonly TaskCompletionSource<uint> initial = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long version;
    private bool disposed;
    private nint registration;
    internal Task<uint> Initial => initial.Task;
    internal long Version { get { lock (sync) return version; } }

    internal EnergySaverStatusSubscription(Action<uint?>? changed = null)
    {
        callback = (_, kind, data) =>
        {
            try
            {
                if (kind != 0x8013 || data == 0) return 0;
                var header = Marshal.PtrToStructure<PowerSettingHeader>(data);
                if (header.Id != StatusGuid) return 0;
                var next = ParseStatus(header.Id, header.Length,
                    header.Length == sizeof(uint) ? unchecked((uint)Marshal.ReadInt32(data, 20)) : uint.MaxValue);
                lock (sync)
                {
                    if (disposed) return 0;
                    version++; initial.TrySetResult(next);
                    signal.TrySetResult(); signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Deliver(next);
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                lock (sync)
                {
                    version++; initial.TrySetException(error);
                    signal.TrySetResult(); signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                Deliver(null);
            }
            return 0;
        };
        void Deliver(uint? value)
        {
            try { changed?.Invoke(value); }
            catch (Exception error) when (error is not OutOfMemoryException) { Debug.WriteLine(error); }
        }
        var subscription = new Subscription { Callback = callback };
        var guid = StatusGuid;
        var error = PowerSettingRegisterNotification(in guid, 2, ref subscription, out registration);
        if (error != 0) throw new Win32Exception((int)error);
    }

    internal static uint ParseStatus(Guid id, uint length, uint value) =>
        id == StatusGuid && length == sizeof(uint) && value <= 2 ? value :
            throw new NotSupportedException("Unknown GUID_ENERGY_SAVER_STATUS notification.");

    internal async Task WaitForChangeAsync(long observedVersion, CancellationToken cancellation)
    {
        while (true)
        {
            Task wait;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(EnergySaverStatusSubscription));
                if (version != observedVersion) return;
                wait = signal.Task;
            }
            await wait.WaitAsync(cancellation).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; signal.TrySetResult(); }
        if (registration != 0) { PowerSettingUnregisterNotification(registration); registration = 0; }
        GC.KeepAlive(callback);
    }

    [StructLayout(LayoutKind.Sequential)] private struct PowerSettingHeader { public Guid Id; public uint Length; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate uint PowerCallback(nint context, uint kind, nint setting);
    [StructLayout(LayoutKind.Sequential)] private struct Subscription { public PowerCallback Callback; public nint Context; }
    [DllImport("powrprof.dll")] private static extern uint PowerSettingRegisterNotification(in Guid setting, uint flags, ref Subscription recipient, out nint registration);
    [DllImport("powrprof.dll")] private static extern uint PowerSettingUnregisterNotification(nint registration);
}
