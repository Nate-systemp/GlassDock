namespace GlassDock.Windows.Desktop;

public sealed record NightLightState(bool? On, bool Available, bool Busy, string Message);

public sealed class NightLightUtilityService : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly NightLightRegistry registry;
    private Timer? refreshTimer;
    private bool disposed, disabled;
    private NightLightState state = new(null, false, false, "Checking experimental Night Light…");
    public NightLightState State { get { lock (sync) return state; } }
    public event EventHandler? Changed;

    public NightLightUtilityService() : this(new NightLightRegistry())
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            Environment.GetEnvironmentVariable("DOKY_DISABLE_EXPERIMENTAL_NIGHT_LIGHT") == "1")
        {
            disabled = true;
            state = new(null, false, false, "Experimental Night Light is disabled or this Windows version is unsupported.");
        }
    }
    internal NightLightUtilityService(NightLightRegistry registry) => this.registry = registry;

    public void SetMonitoring(bool enabled)
    {
        lock (sync)
        {
            if (disposed) return;
            if (!enabled || disabled) { refreshTimer?.Dispose(); refreshTimer = null; }
            else refreshTimer ??= new Timer(ignored => _ = RefreshAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(3));
        }
    }

    public Task RefreshAsync() => ExecuteAsync(false, null);
    public Task ToggleAsync(Func<Task>? finishTransition = null) => ExecuteAsync(true, finishTransition);

    private async Task ExecuteAsync(bool toggle, Func<Task>? finishTransition)
    {
        lock (sync) { if (disposed || disabled || toggle && state.Busy) return; }
        if (!await gate.WaitAsync(toggle ? Timeout.Infinite : 0, lifetime.Token).ConfigureAwait(false)) return;
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        changing.CancelAfter(TimeSpan.FromSeconds(10));
        var cancellation = changing.Token;
        try
        {
            lock (sync)
            {
                if (disposed || disabled) return;
                if (toggle) state = state with { Busy = true, Message = "Changing Night Light…" };
            }
            if (toggle) Notify();
            var result = await Task.Run(() => registry.ReadOrToggle(toggle, cancellation), cancellation)
                .WaitAsync(cancellation).ConfigureAwait(false);
            lock (sync)
            {
                if (disposed) return;
                state = new(result.On, result.Usable, toggle, !result.Usable ? "Windows reports Night Light unavailable on this display."
                    : result.On ? "Night Light on (experimental)" : "Night Light off (experimental)");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            NightLightCloudStore? recovered = null;
            if (!changing.IsCancellationRequested && error is not (InvalidDataException or NotSupportedException or OverflowException))
            {
                try
                {
                    recovered = await Task.Run(() => registry.ReadOrToggle(false, cancellation), cancellation)
                        .WaitAsync(cancellation).ConfigureAwait(false);
                }
                catch (Exception recovery) when (recovery is not OutOfMemoryException)
                { System.Diagnostics.Debug.WriteLine($"Night Light recovery: {recovery.Message}"); }
            }
            lock (sync)
            {
                if (disposed) return;
                state = new(recovered?.On, false, toggle, error is OperationCanceledException or TimeoutException || changing.IsCancellationRequested
                    ? "Night Light confirmation timed out; state is unknown. Disabled until Doky restarts."
                    : $"Experimental Night Light unavailable: {error.Message}");
                if (changing.IsCancellationRequested || error is InvalidDataException or NotSupportedException or OverflowException or OperationCanceledException or TimeoutException)
                { disabled = true; refreshTimer?.Dispose(); refreshTimer = null; }
            }
        }
        finally
        {
            try { if (finishTransition is not null) await finishTransition().ConfigureAwait(false); }
            finally
            {
                lock (sync) state = state with { Busy = false };
                gate.Release(); Notify();
            }
        }
    }

    private void Notify() { lock (sync) { if (disposed) return; } Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true; refreshTimer?.Dispose(); refreshTimer = null;
        }
        lifetime.Cancel();
    }
}
