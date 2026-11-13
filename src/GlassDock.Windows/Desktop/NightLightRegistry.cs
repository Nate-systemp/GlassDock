using Microsoft.Win32;

namespace GlassDock.Windows.Desktop;

internal sealed class NightLightRegistry
{
    internal const string StateKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";
    private readonly Func<byte[]> read;
    private readonly Action<byte[], byte[], CancellationToken> write;
    private ulong highestWritten;

    internal NightLightRegistry() : this(ReadState, WriteState) { }
    internal NightLightRegistry(Func<byte[]> read, Action<byte[], byte[], CancellationToken> write)
    { this.read = read; this.write = write; }

    internal NightLightCloudStore ReadOrToggle(bool toggle, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        var before = read(); var state = new NightLightCloudStore(before);
        if (!toggle) return state;
        var desired = !state.On;
        var now = DateTimeOffset.UtcNow;
        var updated = state.SetActive(desired, now, highestWritten);
        cancel.ThrowIfCancellationRequested();
        write(before, updated, cancel);
        highestWritten = new NightLightCloudStore(updated).Modified;
        var confirmations = 0;
        while (true)
        {
            cancel.ThrowIfCancellationRequested();
            state = new NightLightCloudStore(read());
            if (!state.Usable) throw new NotSupportedException("Windows reports Night Light unavailable on this display.");
            confirmations = state.On == desired && state.Modified >= highestWritten &&
                state.LastTransition >= (ulong)now.UtcDateTime.ToFileTimeUtc() ? confirmations + 1 : 0;
            if (confirmations >= 2) return state;
            if (cancel.WaitHandle.WaitOne(100)) cancel.ThrowIfCancellationRequested();
        }
    }

    private static byte[] ReadState()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StateKey) ?? throw new NotSupportedException("Night Light state is not initialized on this Windows profile.");
        return ReadValue(key);
    }

    private static byte[] ReadValue(RegistryKey key)
    {
        if (key.GetValueKind("Data") != RegistryValueKind.Binary ||
            key.GetValue("Data", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not byte[] bytes || bytes.Length > 65536)
            throw new InvalidDataException("Night Light state is not a supported binary record.");
        return bytes;
    }

    private static void WriteState(byte[] expected, byte[] updated, CancellationToken cancel)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StateKey, writable: true) ?? throw new NotSupportedException("Night Light state is unavailable.");
        if (!ReadValue(key).AsSpan().SequenceEqual(expected))
            throw new IOException("Night Light changed externally before the write. Try again.");
        cancel.ThrowIfCancellationRequested();
        key.SetValue("Data", updated, RegistryValueKind.Binary);
    }
}
