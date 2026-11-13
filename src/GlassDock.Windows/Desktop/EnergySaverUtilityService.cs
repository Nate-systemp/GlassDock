namespace GlassDock.Windows.Desktop;

public sealed record EnergySaverState(bool? On, bool Available, bool Busy, string Message);

public sealed class EnergySaverUtilityService : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private EnergySaverNative? native;
    private EnergySaverOverride? ownedOverride;
    private uint? previousOverride;
    private bool disposed, disabled = Environment.GetEnvironmentVariable("DOKY_ENABLE_EXPERIMENTAL_ENERGY_SAVER") != "1";
    private EnergySaverState state = new(null, false, false, "Experimental Energy Saver requires DOKY_ENABLE_EXPERIMENTAL_ENERGY_SAVER=1 before startup.");
    public EnergySaverState State { get { lock (sync) return state; } }
    public event EventHandler? Changed;

    public void SetMonitoring(bool enabled) { if (enabled) _ = RefreshAsync(); }
    public Task RefreshAsync() => ExecuteAsync(false, null);
    public Task ToggleAsync(Func<Task>? finishTransition = null) => ExecuteAsync(true, finishTransition);

    private async Task ExecuteAsync(bool toggle, Func<Task>? finishTransition)
    {
        lock (sync) { if (disposed || disabled || toggle && state.Busy) return; }
        if (!await gate.WaitAsync(toggle ? Timeout.Infinite : 0, lifetime.Token).ConfigureAwait(false)) return;
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        changing.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            lock (sync)
            {
                if (disposed || disabled) return;
                if (toggle) state = state with { Busy = true, Message = "Changing Energy Saver…" };
            }
            if (toggle) Notify();
            var actual = await Task.Run(async () =>
            {
                var cancellation = changing.Token;
                cancellation.ThrowIfCancellationRequested();
                if (native is null)
                {
                    var created = new EnergySaverNative(StatusChanged);
                    bool canceled;
                    lock (sync)
                    {
                        canceled = disposed || disabled;
                        if (!canceled) native = created;
                    }
                    if (canceled) { created.Dispose(); throw new OperationCanceledException(cancellation); }
                }
                var backend = native ?? throw new OperationCanceledException(cancellation);
                var before = await backend.ReadStatusAsync(cancellation).ConfigureAwait(false);
                if (!toggle) return before;
                var expected = backend.ReadOverride();
                if (ownedOverride != expected) { ownedOverride = null; previousOverride = null; }
                var desiredOn = before == 0;
                var value = EnergySaverNative.SelectOverride(desiredOn, expected.Value, previousOverride,
                    !desiredOn && expected.Value == 1 && previousOverride != 0 && backend.AutomaticThresholdActive());
                cancellation.ThrowIfCancellationRequested();
                lock (sync) { if (disposed || disabled) throw new OperationCanceledException(cancellation); }
                var published = backend.Publish(expected, value);
                var confirmed = await backend.ConfirmStatusAsync(desiredOn, cancellation).ConfigureAwait(false);
                if ((confirmed != 0) != desiredOn || backend.ReadOverride() != published)
                    throw new InvalidOperationException("Windows changed Energy Saver during confirmation.");
                if (desiredOn) { previousOverride = expected.Value; ownedOverride = published; }
                else { previousOverride = null; ownedOverride = null; }
                return confirmed;
            }, changing.Token).WaitAsync(changing.Token).ConfigureAwait(false);
            lock (sync) { if (!disposed) state = new(actual != 0, true, toggle, Label(actual)); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (sync)
            {
                if (!disposed)
                {
                    if (error is NotSupportedException or OperationCanceledException or TimeoutException) disabled = true;
                    state = state with { On = changing.IsCancellationRequested ? null : state.On,
                        Available = !disabled && state.On.HasValue,
                        Message = changing.IsCancellationRequested
                            ? "Energy Saver confirmation timed out; no success confirmed. Disabled until restart."
                            : $"Experimental Energy Saver request failed: {error.Message}" };
                }
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

    private static string Label(uint status) => status == 0 ? "Energy Saver off (experimental)"
        : status == 1 ? "Energy Saver on · standard (experimental)" : "Energy Saver on · high savings (experimental)";
    private void StatusChanged(uint? actual)
    {
        lock (sync)
        {
            if (disposed) return;
            if (actual is null) disabled = true;
            state = state with { On = actual.HasValue ? actual != 0 : null, Available = !disabled && actual.HasValue,
                Message = actual is null ? "Unknown Energy Saver status; disabled until restart."
                    : disabled || state.Busy ? state.Message : Label(actual.Value) };
        }
        Notify();
    }
    private void Notify() { lock (sync) { if (disposed) return; } Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        EnergySaverNative? backend;
        lock (sync) { if (disposed) return; disposed = true; backend = native; native = null; }
        lifetime.Cancel(); backend?.Dispose();
    }
}
