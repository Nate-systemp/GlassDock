using Windows.Devices.Radios;

namespace GlassDock.Windows.Desktop;

public sealed record WifiRadioState(bool Available, bool On, bool Busy, string Message);

public sealed class WifiUtilityService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object networksLock = new();
    private readonly object radioLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<WifiNetwork>>> readNetworks;
    private IReadOnlyList<WifiNetwork> cachedNetworks = Array.Empty<WifiNetwork>();
    private Task<IReadOnlyList<WifiNetwork>>? pendingScan;
    private int networkEpoch;
    public bool HasNetworkSnapshot { get; private set; }
    public IReadOnlyList<WifiNetwork> CachedNetworks { get { lock (networksLock) return cachedNetworks; } }
    private Radio[] radios = [];
    private bool disposed;
    private WifiRadioState state = new(false, false, false, "Checking Wi-Fi…");
    public WifiRadioState State { get { lock (radioLock) return state; } }
    public event EventHandler? Changed;
    public event EventHandler? NetworksChanged;
    public WifiUtilityService() : this(WifiNetworkControl.ReadAsync) { }
    internal WifiUtilityService(Func<bool, CancellationToken, Task<IReadOnlyList<WifiNetwork>>> readNetworks) => this.readNetworks = readNetworks;

    public Task<IReadOnlyList<WifiNetwork>> ReadNetworksAsync(bool scan, CancellationToken cancellation = default)
    {
        Task<IReadOnlyList<WifiNetwork>> work;
        lock (networksLock)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            // Closing one panel cancels its wait, not another monitor's shared scan.
            work = scan && pendingScan is { IsCompleted: false } running ? running : ReadAndCacheAsync(scan, networkEpoch);
            if (scan) pendingScan = work;
        }
        return work.WaitAsync(cancellation);
    }
    private async Task<IReadOnlyList<WifiNetwork>> ReadAndCacheAsync(bool scan, int epoch)
    {
        var available = await readNetworks(scan, lifetime.Token).ConfigureAwait(false);
        lock (networksLock)
        {
            if (disposed || epoch != networkEpoch) throw new OperationCanceledException("Wi-Fi state changed.", lifetime.Token);
            cachedNetworks = available; HasNetworkSnapshot = true;
        }
        if (!disposed) NetworksChanged?.Invoke(this, EventArgs.Empty);
        return available;
    }
    internal void InvalidateNetworks()
    {
        lock (networksLock)
        { networkEpoch++; cachedNetworks = Array.Empty<WifiNetwork>(); HasNetworkSnapshot = false; pendingScan = null; }
    }

    public async Task RefreshAsync()
    {
        if (disposed || !await gate.WaitAsync(0)) return;
        try { await ReadAsync(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { lock (radioLock) state = new(false, false, false, "Wi-Fi unavailable. Check Windows radio permissions."); }
        finally { gate.Release(); if (!disposed) Changed?.Invoke(this, EventArgs.Empty); }
        if (!disposed && State.On && !HasNetworkSnapshot)
        {
            try { await ReadNetworksAsync(false); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Prefetch is optional; opening the panel retries and reports actual access errors.
                System.Diagnostics.Debug.WriteLine(WifiNetworkControl.ErrorMessage(error));
            }
        }
    }

    private async Task ReadAsync(CancellationToken cancel = default)
    {
        var found = (await Radio.GetRadiosAsync().AsTask(cancel)).Where(r => r.Kind == RadioKind.WiFi).ToArray();
        lock (radioLock)
        {
            if (disposed) return;
            foreach (var radio in radios) radio.StateChanged -= RadioChanged;
            radios = found;
            foreach (var radio in radios) radio.StateChanged += RadioChanged;
            PublishRadioState();
        }
    }

    private void PublishRadioState()
    {
        var available = radios.Length > 0 && radios.Any(r => r.State is RadioState.On or RadioState.Off);
        var on = radios.Any(r => r.State == RadioState.On);
        if (!on) InvalidateNetworks();
        state = new(available, on, state.Busy, available ? on ? "Wi-Fi on" : "Wi-Fi off" : "No controllable Wi-Fi adapter");
    }

    private void RadioChanged(Radio sender, object args)
    {
        lock (radioLock)
        {
            if (disposed || !radios.Contains(sender)) return;
            try { PublishRadioState(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            { state = new(false, false, state.Busy, "Wi-Fi unavailable. Check Windows radio permissions."); }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ToggleAsync(Func<Task>? finishTransition = null)
    {
        if (disposed || !await gate.WaitAsync(0)) return;
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        changing.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            lock (radioLock) state = state with { Busy = true, Message = "Changing Wi-Fi…" };
            Changed?.Invoke(this, EventArgs.Empty);
            if (await Radio.RequestAccessAsync().AsTask(changing.Token) != RadioAccessStatus.Allowed)
                throw new InvalidOperationException("Windows denied Wi-Fi control. Check radio permissions.");
            await ReadAsync(changing.Token);
            Radio[] selected;
            RadioState desired;
            lock (radioLock)
            {
                if (disposed) return;
                if (!state.Available) throw new InvalidOperationException("No controllable Wi-Fi adapter");
                desired = state.On ? RadioState.Off : RadioState.On;
                selected = radios.Where(radio => radio.State is RadioState.On or RadioState.Off).ToArray();
            }
            await RadioStateChange.SetAsync(selected, desired, changing.Token);
            await ReadAsync(changing.Token);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (radioLock)
            {
                if (!disposed)
                {
                    try { PublishRadioState(); }
                    catch (Exception refreshError) when (refreshError is not OutOfMemoryException)
                    { state = new(false, false, true, "Wi-Fi unavailable."); }
                    state = state with { Busy = true, Message = error is OperationCanceledException && !lifetime.IsCancellationRequested
                        ? "Wi-Fi did not finish changing. Try again." : error.Message };
                }
            }
        }
        finally
        {
            try { if (finishTransition is not null) await finishTransition(); }
            finally
            {
                lock (radioLock) { if (!disposed) state = state with { Busy = false }; }
                gate.Release(); if (!disposed) Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        lock (radioLock)
        {
            disposed = true;
            foreach (var radio in radios) radio.StateChanged -= RadioChanged;
            radios = [];
        }
        lifetime.Cancel(); InvalidateNetworks();
    }
}
