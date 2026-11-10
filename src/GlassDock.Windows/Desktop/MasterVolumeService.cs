using System.Collections.Concurrent;

namespace GlassDock.Windows.Desktop;

public sealed record MasterVolumeState(float Volume, bool Muted, bool Available, string? EndpointId, string Message);
public sealed class MasterVolumeChangedEventArgs(MasterVolumeState state, bool external) : EventArgs
{
    public MasterVolumeState State { get; } = state;
    public bool External { get; } = external;
}

public sealed class MasterVolumeService : IDisposable
{
    private readonly BlockingCollection<Action> work = new();
    private MasterVolumeState state = new(0, false, false, null, "Checking audio output…");
    private Thread? worker;
    private SystemControlAudio.VolumeMonitor? monitor;
    private sealed record Request(float Value, string? Endpoint);
    private Request? requested;
    private int writeQueued, disposed;
    public MasterVolumeState State => Volatile.Read(ref state);
    public event EventHandler<MasterVolumeChangedEventArgs>? Changed;
    public event EventHandler? VolumeKeyHandled;

    public void Start()
    {
        if (worker is not null || Volatile.Read(ref disposed) != 0) return;
        worker = new Thread(Run) { IsBackground = true, Name = "Doky master audio" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    private void Run()
    {
        try
        {
            monitor = new(Post, Publish);
            foreach (var action in work.GetConsumingEnumerable())
            {
                if (Volatile.Read(ref disposed) != 0) break;
                try { action(); }
                catch (Exception error) when (error is not OutOfMemoryException) { Unavailable(error); }
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { Unavailable(error); }
        finally { monitor?.Dispose(); monitor = null; }
    }
    private void Post(Action action) => TryPost(action);
    private bool TryPost(Action action)
    {
        if (Volatile.Read(ref disposed) != 0) return false;
        try { return work.TryAdd(action); } catch (InvalidOperationException) { return false; }
    }

    // Called by the input hook: no COM, UI work or waiting on the hook thread.
    public bool TryHandleVolumeKey(uint key)
    {
        var current = State;
        if (!VolumeKeyCapture.IsVolumeKey(key) || !current.Available || worker?.IsAlive != true) return false;
        return TryPost(() =>
        {
            if (monitor?.HandleVolumeKey(key, current.EndpointId) == true)
                VolumeKeyHandled?.Invoke(this, EventArgs.Empty);
        });
    }
    private void Publish(MasterVolumeState next, bool external)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        var before = State;
        Volatile.Write(ref state, next);
        var changed = before.EndpointId == next.EndpointId && before.Available && next.Available &&
            (Math.Abs(before.Volume - next.Volume) > .00001f || before.Muted != next.Muted);
        Changed?.Invoke(this, new(next, external && changed));
    }
    private void Unavailable(Exception error) => Publish(new(0, false, false, null, $"Audio output unavailable: {error.Message}"), false);

    public void SetVolume(float value)
    {
        if (!float.IsFinite(value) || !State.Available || Volatile.Read(ref disposed) != 0) return;
        Interlocked.Exchange(ref requested, new(Math.Clamp(value, 0, 1), State.EndpointId));
        QueueWrite();
    }
    private void QueueWrite()
    {
        if (Interlocked.Exchange(ref writeQueued, 1) == 0) Post(() =>
        {
            try
            {
                var next = Interlocked.Exchange(ref requested, null);
                if (next is not null) monitor?.SetVolume(next.Value, next.Endpoint);
            }
            finally
            {
                Interlocked.Exchange(ref writeQueued, 0);
                if (Volatile.Read(ref requested) is not null) QueueWrite();
            }
        });
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        work.CompleteAdding(); // The MTA worker releases/unregisters its own COM objects.
    }
}
