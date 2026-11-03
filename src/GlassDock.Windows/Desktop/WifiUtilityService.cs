using Windows.Devices.Radios;

namespace GlassDock.Windows.Desktop;

public sealed record WifiRadioState(bool Available, bool On, bool Busy, string Message);

public sealed class WifiUtilityService : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object networksLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<bool, CancellationToken, Task<IReadOnlyList<WifiNetwork>>> readNetworks;
    private IReadOnlyList<WifiNetwork> cachedNetworks = Array.Empty<WifiNetwork>();
    private Task<IReadOnlyList<WifiNetwork>>? pendingScan;
    private int networkEpoch;
    public bool HasNetworkSnapshot { get; private set; }
    public IReadOnlyList<WifiNetwork> CachedNetworks { get { lock (networksLock) return cachedNetworks; } }
    private Radio[] radios = [];
    private bool disposed;
    public WifiRadioState State { get; private set; } = new(false, false, false, "Checking Wi-Fi…");
    public event EventHandler? Changed;
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
        { State = new(false, false, false, "Wi-Fi unavailable. Check Windows radio permissions."); }
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

    private async Task ReadAsync()
    {
        var found = (await Radio.GetRadiosAsync()).Where(r => r.Kind == RadioKind.WiFi).ToArray();
        if (disposed) return;
        foreach (var radio in radios) radio.StateChanged -= RadioChanged;
        radios = found;
        foreach (var radio in radios) radio.StateChanged += RadioChanged;
        PublishRadioState();
    }

    private void PublishRadioState()
    {
        var available = radios.Length > 0 && radios.Any(r => r.State is RadioState.On or RadioState.Off);
        var on = radios.Any(r => r.State == RadioState.On);
        if (!on) InvalidateNetworks();
        State = new(available, on, false, available ? on ? "Wi-Fi on" : "Wi-Fi off" : "No controllable Wi-Fi adapter");
    }

    private void RadioChanged(Radio sender, object args)
    { if (disposed || !radios.Contains(sender)) return; PublishRadioState(); Changed?.Invoke(this, EventArgs.Empty); }

    public async Task ToggleAsync()
    {
        if (disposed || !await gate.WaitAsync(0)) return;
        try
        {
            State = State with { Busy = true, Message = "Changing Wi-Fi…" }; Changed?.Invoke(this, EventArgs.Empty);
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
                throw new InvalidOperationException("Windows denied Wi-Fi control. Check radio permissions.");
            await ReadAsync();
            if (disposed) return;
            if (!State.Available) throw new InvalidOperationException("No controllable Wi-Fi adapter");
            var desired = State.On ? RadioState.Off : RadioState.On;
            foreach (var radio in radios.ToArray())
                if (await radio.SetStateAsync(desired) != RadioAccessStatus.Allowed)
                    throw new InvalidOperationException("Windows or the adapter blocked this change.");
            await ReadAsync();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { State = State with { Busy = false, Message = error.Message }; }
        finally { gate.Release(); if (!disposed) Changed?.Invoke(this, EventArgs.Empty); }
    }

    public void Dispose()
    {
        disposed = true;
        lifetime.Cancel(); InvalidateNetworks();
        foreach (var radio in radios) radio.StateChanged -= RadioChanged;
        radios = [];
    }
}
