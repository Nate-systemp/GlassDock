namespace GlassDock.Windows.Desktop;

public sealed record DndState(bool? On, bool Available, bool Busy, string Message);

public sealed class DndUtilityService : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private DndNative? native;
    private Timer? fallbackTimer;
    private bool disposed, monitoring;
    private bool disabled = Environment.GetEnvironmentVariable("DOKY_ENABLE_EXPERIMENTAL_DND") != "1";
    private DndState state = new(null, false, false, "Experimental Do Not Disturb requires DOKY_ENABLE_EXPERIMENTAL_DND=1 before startup.");
    public DndState State { get { lock (sync) return state; } }
    public event EventHandler? Changed;

    public void SetMonitoring(bool enabled)
    {
        lock (sync)
        {
            if (disposed) return;
            monitoring = enabled;
            UpdateFallbackTimer();
        }
        if (enabled) _ = RefreshAsync();
    }
    private void UpdateFallbackTimer()
    {
        if (!monitoring || disposed || disabled || native?.HasNotifications != false)
        { fallbackTimer?.Dispose(); fallbackTimer = null; }
        else fallbackTimer ??= new Timer(ignored => _ = RefreshAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }
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
                if (toggle) state = state with { Busy = true, Message = "Changing Do Not Disturb…" };
            }
            if (toggle) Notify();
            var actual = await Task.Run(async () =>
            {
                var cancellation = changing.Token;
                cancellation.ThrowIfCancellationRequested();
                if (native is null)
                {
                    var created = new DndNative(StatusChanged);
                    bool canceled;
                    lock (sync)
                    {
                        canceled = disposed || disabled;
                        if (!canceled) { native = created; UpdateFallbackTimer(); }
                    }
                    if (canceled) { created.Dispose(); throw new OperationCanceledException(cancellation); }
                }
                var backend = native ?? throw new OperationCanceledException(cancellation);
                var before = backend.ReadMode();
                if (!toggle) return before;
                var desired = !DndNative.IsOn(before);
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var worker = new Thread(() =>
                {
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        lock (sync) { if (disposed || disabled) throw new OperationCanceledException(cancellation); }
                        DndNative.SetProfile(desired, cancellation);
                        completion.TrySetResult();
                    }
                    catch (Exception error) when (error is not OutOfMemoryException) { completion.TrySetException(error); }
                }) { IsBackground = true, Name = "Doky experimental Do Not Disturb" };
                worker.SetApartmentState(ApartmentState.STA); worker.Start();
                await completion.Task.WaitAsync(cancellation).ConfigureAwait(false);
                return await backend.ConfirmAsync(desired ? 1u : 0u, cancellation).ConfigureAwait(false);
            }, changing.Token).WaitAsync(changing.Token).ConfigureAwait(false);
            lock (sync) { if (!disposed) state = new(DndNative.IsOn(actual), true, toggle, Label(actual)); }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            uint? recovered = null;
            var backend = native;
            if (!changing.IsCancellationRequested && backend is not null)
            {
                try { recovered = await Task.Run(backend.ReadMode, changing.Token).WaitAsync(changing.Token).ConfigureAwait(false); }
                catch (Exception recovery) when (recovery is not OutOfMemoryException) { System.Diagnostics.Debug.WriteLine(recovery); }
            }
            lock (sync)
            {
                if (!disposed)
                {
                    if (changing.IsCancellationRequested || error is NotSupportedException or OperationCanceledException or TimeoutException) disabled = true;
                    state = new(recovered.HasValue ? DndNative.IsOn(recovered.Value) : null, !disabled && recovered.HasValue, toggle,
                        changing.IsCancellationRequested
                            ? "Do Not Disturb confirmation timed out; Windows rules may still apply. Disabled until restart."
                            : $"Do Not Disturb request failed: {error.Message}");
                    UpdateFallbackTimer();
                }
            }
        }
        finally
        {
            try { if (finishTransition is not null) await finishTransition().ConfigureAwait(false); }
            finally { lock (sync) state = state with { Busy = false }; gate.Release(); Notify(); }
        }
    }

    private static string Label(uint mode) => mode == 0 ? "Do Not Disturb off (experimental)"
        : mode == 1 ? "Do Not Disturb on (experimental)" : "Do Not Disturb on · alarms only (experimental)";
    private void StatusChanged(uint? actual)
    {
        lock (sync)
        {
            if (disposed) return;
            if (actual is null) { disabled = true; UpdateFallbackTimer(); }
            state = state with { On = actual.HasValue ? DndNative.IsOn(actual.Value) : null, Available = !disabled && actual.HasValue,
                Message = actual is null ? "Unknown notification mode; Do Not Disturb disabled until restart."
                    : disabled || state.Busy ? state.Message : Label(actual.Value) };
        }
        Notify();
    }
    private void Notify() { lock (sync) { if (disposed) return; } Changed?.Invoke(this, EventArgs.Empty); }
    public void Dispose()
    {
        DndNative? backend;
        lock (sync)
        {
            if (disposed) return;
            disposed = true; fallbackTimer?.Dispose(); fallbackTimer = null;
            backend = native; native = null;
        }
        lifetime.Cancel(); backend?.Dispose();
    }
}
