using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

public sealed record AirplaneModeState(bool? On, bool Available, bool Busy, string Message);

public sealed class AirplaneModeUtilityService : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Func<bool, CancellationToken, AirplaneModeResult> native;
    private Timer? refreshTimer;
    private bool disabled, disposed;
    private AirplaneModeState state = new(null, false, false, "Checking experimental Airplane mode…");
    public AirplaneModeState State { get { lock (sync) return state; } }
    public event EventHandler? Changed;

    public AirplaneModeUtilityService() : this(AirplaneModeNative.Run)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ||
            Environment.GetEnvironmentVariable("DOKY_DISABLE_EXPERIMENTAL_AIRPLANE_MODE") == "1")
        {
            disabled = true;
            state = new(null, false, false, "Experimental Airplane mode is disabled or this Windows version is unsupported.");
        }
    }

    internal AirplaneModeUtilityService(Func<bool, CancellationToken, AirplaneModeResult> native) => this.native = native;

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
        // A hover refresh may own the gate when the click arrives. Queue the click.
        if (!await gate.WaitAsync(toggle ? Timeout.Infinite : 0, lifetime.Token).ConfigureAwait(false)) return;
        using var changing = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        changing.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            lock (sync)
            {
                if (disposed || disabled) return;
                if (toggle) state = state with { Busy = true, Message = "Changing Airplane mode…" };
            }
            if (toggle) Notify();
            var completion = new TaskCompletionSource<AirplaneModeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellation = changing.Token;
            var worker = new Thread(() =>
            {
                try { completion.TrySetResult(native(toggle, cancellation)); }
                catch (Exception error) when (error is not OutOfMemoryException) { completion.TrySetResult(new(null, error)); }
            }) { IsBackground = true, Name = "Doky experimental Airplane mode" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            var result = await completion.Task.WaitAsync(changing.Token).ConfigureAwait(false);
            if (result.On is null && result.Error is null)
                throw new NotSupportedException("Windows did not provide a confirmed global Airplane mode state.");
            lock (sync)
            {
                if (disposed) return;
                state = new(result.On, result.Error is null && result.On.HasValue, toggle,
                    result.Error is null ? result.On == true ? "Airplane mode on (experimental)" : "Airplane mode off (experimental)"
                        : FailureMessage(result.Error));
                if (result.Error is { } error && PermanentFailure(error)) Disable();
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            lock (sync)
            {
                if (disposed) return;
                state = new(null, false, toggle, FailureMessage(error));
                if (PermanentFailure(error)) Disable();
            }
        }
        finally
        {
            try { if (finishTransition is not null) await finishTransition().ConfigureAwait(false); }
            finally
            {
                lock (sync) { state = state with { Busy = false }; }
                gate.Release(); Notify();
            }
        }
    }

    private static bool PermanentFailure(Exception error) => error is NotSupportedException or TimeoutException or OperationCanceledException ||
        error is COMException { HResult: unchecked((int)0x80040154) or unchecked((int)0x80004002) };

    private static string FailureMessage(Exception error) => error switch
    {
        OperationCanceledException or TimeoutException => "Airplane mode timed out; Windows state is unknown. Disabled until Doky restarts.",
        UnauthorizedAccessException => "Windows denied Airplane mode access. Check device policy and permissions.",
        COMException { HResult: unchecked((int)0x80070005) } => "Windows denied Airplane mode access. Check device policy and permissions.",
        _ => $"Experimental Airplane mode unavailable: {error.Message} (0x{error.HResult:X8})"
    };

    // A private COM call cannot be forcibly stopped safely. Quarantine instead of starting more workers.
    private void Disable() { disabled = true; refreshTimer?.Dispose(); refreshTimer = null; }
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
