using GlassDock.Core.Desktop;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace GlassDock.Windows.Desktop;

public sealed record BluetoothRadioState(bool Available, bool On, bool Busy, string Message);

public sealed class BluetoothUtilityService : IDisposable
{
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string BatteryProperty = "System.Devices.BatteryLife";
    private const string ContainerProperty = "System.Devices.Aep.ContainerId";
    private const string ConnectionFailed = "Connection failed";
    private static readonly string[] Properties = new[]
    {
        ConnectedProperty, "System.Devices.Aep.IsPaired", BatteryProperty, ContainerProperty,
        "System.Devices.Aep.Bluetooth.Cod.Major", "System.Devices.Aep.Bluetooth.Cod.Minor",
        "System.Devices.Aep.Bluetooth.Le.Appearance.Category", "System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory"
    };
    private readonly object sync = new();
    private readonly SemaphoreSlim radioGate = new(1, 1);
    private readonly BluetoothDeviceStore store = new();
    private readonly Dictionary<string, (DeviceInformation Info, bool LowEnergy)> devices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CancellationTokenSource> operations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (BluetoothLEDevice Device, GattSession? Session)> connections = new(StringComparer.Ordinal);
    private readonly HashSet<string> audioDevices = new(StringComparer.Ordinal);
    private readonly List<DeviceWatcher> watchers = new();
    private readonly CancellationTokenSource lifetime = new();
    private Radio[] radios = [];
    private DeviceWatcher? adapterWatcher;
    private BluetoothRadioState state = new(false, false, false, "Checking Bluetooth…");
    private bool disposed;
    public BluetoothRadioState State { get { lock (sync) return state; } }
    public IReadOnlyList<BluetoothDeviceInfo> Devices { get { lock (sync) return store.Snapshot; } }
    public event EventHandler? Changed;

    public async Task RefreshAsync()
    {
        if (!await radioGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            lock (sync) { if (disposed) return; EnsureAdapterWatcher(); }
            await ReadRadiosAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (sync)
            {
                if (disposed) return;
                StopDevices();
                state = new(false, false, false, "Bluetooth unavailable. Check Windows radio permissions.");
            }
        }
        finally { radioGate.Release(); Notify(); }
    }

    public async Task ToggleAsync(Func<Task>? finishTransition = null)
    {
        if (!await radioGate.WaitAsync(0).ConfigureAwait(false)) return;
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        changing.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            lock (sync)
            {
                if (disposed) return;
                state = state with { Busy = true, Message = "Changing Bluetooth…" };
            }
            Notify();
            if (await Radio.RequestAccessAsync().AsTask(changing.Token) != RadioAccessStatus.Allowed)
                throw new InvalidOperationException("Windows denied Bluetooth control.");
            await ReadRadiosAsync(changing.Token).ConfigureAwait(false);
            Radio[] selected;
            RadioState desired;
            lock (sync)
            {
                if (disposed) return;
                if (!state.Available) throw new InvalidOperationException("No controllable Bluetooth adapter");
                selected = radios.Where(r => r.State is RadioState.On or RadioState.Off).ToArray();
                if (selected.Length == 0) throw new InvalidOperationException("No controllable Bluetooth adapter");
                desired = state.On ? RadioState.Off : RadioState.On;
                if (desired == RadioState.Off) StopDevices();
                state = state with { Busy = true };
            }
            await RadioStateChange.SetAsync(selected, desired, changing.Token).ConfigureAwait(false);
            await ReadRadiosAsync(changing.Token).ConfigureAwait(false);
            lock (sync)
                if (!disposed && state.On != (desired == RadioState.On))
                    state = state with { Message = "Bluetooth change requested; waiting for Windows." };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (sync)
            {
                if (!disposed)
                {
                    try { PublishRadios(); }
                    catch (Exception refreshError) when (refreshError is not OutOfMemoryException)
                    {
                        StopDevices();
                        state = new(false, false, false, "Bluetooth unavailable.");
                        System.Diagnostics.Debug.WriteLine($"Bluetooth recovery: {refreshError.Message}");
                    }
                    state = state with { Busy = true, Message = error is TimeoutException ||
                        (error is OperationCanceledException && !lifetime.IsCancellationRequested)
                        ? "Bluetooth did not finish changing. Try again." : error.Message };
                }
            }
        }
        finally
        {
            try { if (finishTransition is not null) await finishTransition().ConfigureAwait(false); }
            finally
            {
                lock (sync) { if (!disposed) state = state with { Busy = false }; }
                radioGate.Release(); Notify();
            }
        }
    }

    private async Task ReadRadiosAsync(CancellationToken cancel = default)
    {
        var found = (await Radio.GetRadiosAsync().AsTask(cancel)).Where(r => r.Kind == RadioKind.Bluetooth).ToArray();
        lock (sync)
        {
            if (disposed) return;
            foreach (var radio in radios) radio.StateChanged -= RadioChanged;
            radios = found;
            foreach (var radio in radios) radio.StateChanged += RadioChanged;
            PublishRadios();
        }
    }

    private void PublishRadios()
    {
        var available = radios.Any(r => r.State is RadioState.On or RadioState.Off);
        var on = radios.Any(r => r.State == RadioState.On);
        state = new(available, on, state.Busy, available ? on ? "Bluetooth on" : "Bluetooth off" : "No controllable Bluetooth adapter");
        if (on) StartDevices(); else StopDevices();
    }

    private void RadioChanged(Radio sender, object args)
    {
        lock (sync)
        {
            if (disposed || !radios.Contains(sender)) return;
            try { PublishRadios(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                StopDevices();
                state = state with { Message = "Bluetooth device updates unavailable." };
            }
        }
        Notify();
    }

    private void EnsureAdapterWatcher()
    {
        if (adapterWatcher is not null) return;
        adapterWatcher = DeviceInformation.CreateWatcher(Radio.GetDeviceSelector());
        adapterWatcher.Added += AdapterAdded;
        adapterWatcher.Removed += AdapterRemoved;
        adapterWatcher.Start();
    }
    private void AdapterAdded(DeviceWatcher sender, DeviceInformation args) => _ = RefreshAsync();
    private void AdapterRemoved(DeviceWatcher sender, DeviceInformationUpdate args) => _ = RefreshAsync();

    private void StartDevices()
    {
        if (watchers.Count > 0) return;
        AddWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        AddWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
    }

    private void AddWatcher(string selector)
    {
        var watcher = DeviceInformation.CreateWatcher(selector, Properties, DeviceInformationKind.AssociationEndpoint);
        watcher.Added += DeviceAdded;
        watcher.Updated += DeviceUpdated;
        watcher.Removed += DeviceRemoved;
        watcher.Stopped += WatcherStopped;
        watchers.Add(watcher);
        watcher.Start();
    }

    private void DeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        lock (sync)
        {
            if (disposed || !state.On || !watchers.Contains(sender)) return;
            devices[info.Id] = (info, watchers.IndexOf(sender) == 1);
            UpdateDevice(info.Id);
        }
        Notify();
    }

    private void DeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (sync)
        {
            if (disposed || !watchers.Contains(sender) || !devices.TryGetValue(update.Id, out var entry)) return;
            entry.Info.Update(update);
            UpdateDevice(update.Id);
        }
        Notify();
    }

    private void DeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (sync)
        {
            if (disposed || !watchers.Contains(sender)) return;
            RemoveDevice(update.Id);
        }
        Notify();
    }

    private void WatcherStopped(DeviceWatcher sender, object args)
    {
        lock (sync)
        {
            if (disposed || !watchers.Contains(sender) || sender.Status != DeviceWatcherStatus.Aborted) return;
            StopDevices();
            state = state with { Message = "Bluetooth device updates unavailable." };
        }
        Notify();
    }

    private void UpdateDevice(string id)
    {
        var info = devices[id].Info;
        if (!info.Pairing.IsPaired) { RemoveDevice(id); return; }
        var connected = info.Properties.TryGetValue(ConnectedProperty, out var value) && value is true;
        var battery = info.Properties.TryGetValue(BatteryProperty, out value) ? ReadBattery(value) : null;
        var kind = DeviceKind(info.Properties);
        store.Update(new(id, string.IsNullOrWhiteSpace(info.Name) ? "Bluetooth device" : info.Name,
            kind, connected, battery, true, connections.ContainsKey(id) || audioDevices.Contains(id) ||
                (kind is BluetoothDeviceKind.Headphones or BluetoothDeviceKind.Headset or BluetoothDeviceKind.Speaker &&
                    ContainerId(info) != Guid.Empty)), true);
    }

    private Guid ContainerId(DeviceInformation info)
    {
        lock (sync)
            return info.Properties.TryGetValue(ContainerProperty, out var value) && value is Guid id ? id : Guid.Empty;
    }

    internal static int? ReadBattery(object? value) => value switch
    {
        byte number => BluetoothDeviceStore.ValidBattery(number),
        ushort number => BluetoothDeviceStore.ValidBattery(number),
        uint number when number <= 100 => (int)number,
        int number => BluetoothDeviceStore.ValidBattery(number),
        _ => null
    };

    internal static BluetoothDeviceKind DeviceKind(IReadOnlyDictionary<string, object> properties)
    {
        int Number(string key) => properties.TryGetValue(key, out var value)
            ? value switch { byte n => n, ushort n => n, uint n when n <= int.MaxValue => (int)n, int n => n, _ => -1 } : -1;
        var major = Number("System.Devices.Aep.Bluetooth.Cod.Major");
        var minor = Number("System.Devices.Aep.Bluetooth.Cod.Minor");
        if (major == 4) return minor switch { 1 or 2 => BluetoothDeviceKind.Headset, 5 => BluetoothDeviceKind.Speaker, 6 => BluetoothDeviceKind.Headphones, _ => BluetoothDeviceKind.Generic };
        if (major == 5 && minor >= 0)
        {
            if ((minor & 0x20) != 0) return BluetoothDeviceKind.Mouse;
            if ((minor & 0x10) != 0) return BluetoothDeviceKind.Keyboard;
            if ((minor & 0x0f) is 1 or 2) return BluetoothDeviceKind.Controller;
        }
        if (Number("System.Devices.Aep.Bluetooth.Le.Appearance.Category") == 15)
            return Number("System.Devices.Aep.Bluetooth.Le.Appearance.Subcategory") switch
            { 1 => BluetoothDeviceKind.Keyboard, 2 => BluetoothDeviceKind.Mouse, 3 or 4 => BluetoothDeviceKind.Controller, _ => BluetoothDeviceKind.Generic };
        return BluetoothDeviceKind.Generic;
    }

    public Task ConnectAsync(string id) => RunDeviceOperationAsync(id, "Connecting...", async (entry, cancel) =>
    {
        var audio = await SystemControlAudio.SetBluetoothConnectionAsync(ContainerId(entry.Info), true, cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (audio is not null)
        {
            lock (sync)
            {
                cancel.ThrowIfCancellationRequested();
                audioDevices.Add(id);
                if (audio.Value && store.Find(id) is { } current)
                    store.Update(current with { IsConnected = true, CanDisconnect = true }, true);
            }
            return audio.Value ? null : ConnectionFailed;
        }
        lock (sync)
            if (!entry.LowEnergy || store.Find(id)?.Kind is BluetoothDeviceKind.Headphones or BluetoothDeviceKind.Headset or BluetoothDeviceKind.Speaker)
                return ConnectionFailed;
        BluetoothLEDevice? device = null;
        GattSession? session = null;
        var retained = false;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(id).AsTask(cancel).ConfigureAwait(false);
            if (device is null) return ConnectionFailed;
            session = await GattSession.FromDeviceIdAsync(device.BluetoothDeviceId).AsTask(cancel).ConfigureAwait(false);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ConnectionChanged(BluetoothLEDevice sender, object args)
            { if (sender.ConnectionStatus == BluetoothConnectionStatus.Connected) connected.TrySetResult(); }
            device.ConnectionStatusChanged += ConnectionChanged;
            try
            {
                if (session?.CanMaintainConnection == true) session.MaintainConnection = true;
                else
                {
                    var services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached).AsTask(cancel).ConfigureAwait(false);
                    foreach (var service in services.Services) service.Dispose();
                    if (services.Status != GattCommunicationStatus.Success && device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                        return ConnectionFailed;
                }
                ConnectionChanged(device, EventArgs.Empty);
                await connected.Task.WaitAsync(TimeSpan.FromSeconds(15), cancel).ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                lock (sync)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (device.ConnectionStatus != BluetoothConnectionStatus.Connected)
                        return ConnectionFailed;
                    ReleaseConnection(id);
                    connections[id] = (device, session);
                    retained = true;
                    if (store.Find(id) is { } current)
                        store.Update(current with { IsConnected = true, CanDisconnect = true }, true);
                    device.ConnectionStatusChanged += MaintainedConnectionChanged;
                }
                return null;
            }
            finally { device.ConnectionStatusChanged -= ConnectionChanged; }
        }
        finally
        {
            if (!retained)
            {
                DisposeConnection(device, session);
            }
        }
    });

    private void MaintainedConnectionChanged(BluetoothLEDevice sender, object args)
    {
        lock (sync)
        {
            if (disposed || !connections.TryGetValue(sender.DeviceId, out var connection) || connection.Device != sender) return;
            if (store.Find(sender.DeviceId) is { } current)
                store.Update(current with { IsConnected = sender.ConnectionStatus == BluetoothConnectionStatus.Connected }, true);
        }
        Notify();
    }

    public Task DisconnectAsync(string id) => RunDeviceOperationAsync(id, "Disconnecting...", async (entry, cancel) =>
    {
        var audio = await SystemControlAudio.SetBluetoothConnectionAsync(ContainerId(entry.Info), false, cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (audio is not null)
        {
            if (!audio.Value) return "Disconnection failed";
            lock (sync)
            {
                cancel.ThrowIfCancellationRequested();
                ReleaseConnection(id);
                if (store.Find(id) is { } current) store.Update(current with { IsConnected = false }, true);
            }
            return null;
        }
        lock (sync)
        {
            if (!connections.ContainsKey(id)) return "Disconnection failed";
            ReleaseConnection(id);
        }
        await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
        var latest = await DeviceInformation.CreateFromIdAsync(id, Properties, DeviceInformationKind.AssociationEndpoint).AsTask(cancel).ConfigureAwait(false);
        lock (sync)
        {
            cancel.ThrowIfCancellationRequested();
            devices[id] = (latest, entry.LowEnergy);
            UpdateDevice(id);
            return store.Find(id)?.IsConnected == true
                ? "Disconnection failed" : null;
        }
    });

    public Task RemoveAsync(string id) => RunDeviceOperationAsync(id, "Removing...", async (entry, cancel) =>
    {
        var result = await entry.Info.Pairing.UnpairAsync().AsTask(cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
        if (result.Status is not (DeviceUnpairingResultStatus.Unpaired or DeviceUnpairingResultStatus.AlreadyUnpaired))
            return "Removal failed";
        lock (sync) { cancel.ThrowIfCancellationRequested(); RemoveDevice(id); }
        return null;
    });

    private async Task RunDeviceOperationAsync(string id, string message,
        Func<(DeviceInformation Info, bool LowEnergy), CancellationToken, Task<string?>> action)
    {
        long token;
        CancellationTokenSource cancellation;
        (DeviceInformation Info, bool LowEnergy) entry;
        lock (sync)
        {
            if (disposed || !state.On || !devices.TryGetValue(id, out entry) || store.Begin(id, message) is not { } started) return;
            token = started;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            if (message == "Connecting...") cancellation.CancelAfter(TimeSpan.FromSeconds(20));
            operations[id] = cancellation;
        }
        Notify();
        try
        {
            var result = await action(entry, cancellation.Token).ConfigureAwait(false);
            lock (sync) { if (!disposed) store.Complete(id, token, result); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            lock (sync) { if (!disposed) store.Complete(id, token, ConnectionFailed); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var failure = message == "Connecting..." ? ConnectionFailed : message == "Disconnecting..." ? "Disconnection failed" : "Removal failed";
            lock (sync) { if (!disposed) store.Complete(id, token, failure); }
        }
        finally
        {
            lock (sync) { if (operations.GetValueOrDefault(id) == cancellation) operations.Remove(id); }
            cancellation.Dispose();
            Notify();
        }
    }

    private void ReleaseConnection(string id)
    {
        if (!connections.Remove(id, out var connection)) return;
        connection.Device.ConnectionStatusChanged -= MaintainedConnectionChanged;
        DisposeConnection(connection.Device, connection.Session);
    }

    private static void DisposeConnection(BluetoothLEDevice? device, GattSession? session)
    {
        try
        {
            try { if (session is not null) session.MaintainConnection = false; }
            finally { try { session?.Dispose(); } finally { device?.Dispose(); } }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Debug.WriteLine($"Bluetooth resource cleanup: {error.Message}"); }
    }

    private void RemoveDevice(string id)
    {
        if (operations.Remove(id, out var operation)) operation.Cancel();
        ReleaseConnection(id);
        audioDevices.Remove(id);
        devices.Remove(id);
        store.Remove(id);
    }

    private void StopDevices()
    {
        foreach (var watcher in watchers)
        {
            watcher.Added -= DeviceAdded; watcher.Updated -= DeviceUpdated; watcher.Removed -= DeviceRemoved; watcher.Stopped -= WatcherStopped;
            StopWatcher(watcher);
        }
        watchers.Clear();
        foreach (var id in devices.Keys.ToArray()) RemoveDevice(id);
        store.Clear();
    }

    private void Notify()
    {
        lock (sync) { if (disposed) return; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void StopWatcher(DeviceWatcher watcher)
    {
        try { if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { System.Diagnostics.Debug.WriteLine($"Bluetooth watcher cleanup: {error.Message}"); }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            StopDevices();
            foreach (var radio in radios) radio.StateChanged -= RadioChanged;
            radios = [];
            if (adapterWatcher is { } watcher)
            {
                watcher.Added -= AdapterAdded; watcher.Removed -= AdapterRemoved;
                StopWatcher(watcher);
                adapterWatcher = null;
            }
            Changed = null;
        }
    }
}
