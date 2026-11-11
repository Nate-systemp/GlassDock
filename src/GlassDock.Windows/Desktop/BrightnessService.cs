namespace GlassDock.Windows.Desktop;

public sealed record BrightnessDisplay(string Id, string Name, nint Monitor, int? Percent, bool Available, string Backend, string Message);
public sealed class BrightnessChangedEventArgs(BrightnessDisplay? display, bool external) : EventArgs
{
    public BrightnessDisplay? Display { get; } = display;
    public bool External { get; } = external;
}

/// <summary>Serializes WMI/DDC hardware work; UI sees only immutable readback snapshots.</summary>
public sealed class BrightnessService : IDisposable
{
    private readonly AutoResetEvent wake = new(false);
    private readonly BrightnessWriteQueue writes = new();
    private readonly object writeLock = new();
    private readonly Dictionary<string, long> pendingRevisions = new();
    private long revision;
    private BrightnessDisplay[] displays = [];
    private Thread? worker;
    private int disposed, refreshRequested;
    public IReadOnlyList<BrightnessDisplay> Displays => Array.AsReadOnly(Volatile.Read(ref displays));
    public string Status { get; private set; } = "Checking display brightness…";
    public event EventHandler<BrightnessChangedEventArgs>? Changed;

    public void Start()
    {
        if (worker is not null || Volatile.Read(ref disposed) != 0) return;
        worker = new Thread(Run) { IsBackground = true, Name = "Doky display brightness" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    public BrightnessDisplay? ForMonitor(nint monitor) => Displays.Where(d => d.Monitor == monitor)
        .OrderByDescending(d => d.Available).FirstOrDefault();

    public void SetBrightness(string id, float percent)
    {
        if (!float.IsFinite(percent) || percent is < 0 or > 100 || Volatile.Read(ref disposed) != 0 ||
            !Displays.Any(d => d.Id == id && d.Available)) return;
        lock (writeLock)
        {
            var version = ++revision;
            pendingRevisions[id] = version;
            writes.Request(id, (int)Math.Round(percent), version);
        }
        Signal();
    }
    public bool IsPending(string id) { lock (writeLock) return pendingRevisions.ContainsKey(id); }
    public void Refresh()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Interlocked.Exchange(ref refreshRequested, 1); Signal();
    }
    private void Signal() { try { wake.Set(); } catch (ObjectDisposedException) { } }
    private void Run()
    {
        using var hardware = new BrightnessHardware();
        var selfUntil = new Dictionary<string, long>();
        var topology = "";
        var nextPoll = 0L;
        var nextDiscovery = 0L;
        var nextWrite = 0L;
        try
        {
            hardware.Watch(Refresh);
            while (Volatile.Read(ref disposed) == 0)
            {
                var now = Environment.TickCount64;
                var refresh = Interlocked.Exchange(ref refreshRequested, 0);
                if (now >= nextPoll || refresh != 0)
                {
                    try
                    {
                        var key = hardware.TopologyKey();
                        var rediscover = key != topology || now >= nextDiscovery;
                        if (rediscover)
                        {
                            hardware.Enumerate(); topology = key; nextDiscovery = now + 30000;
                            Status = hardware.Targets.Length == 0 ? "No physical display endpoints were found." : $"{hardware.Targets.Length} display endpoints detected.";
                            var ids = hardware.Targets.Select(t => t.Id).ToHashSet();
                            lock (writeLock)
                            {
                                writes.Keep(ids);
                                foreach (var id in pendingRevisions.Keys.Where(id => !ids.Contains(id)).ToArray()) pendingRevisions.Remove(id);
                            }
                            // Retain identity-matched old values only until each fresh hardware read below.
                            Volatile.Write(ref displays, Volatile.Read(ref displays).Where(d => ids.Contains(d.Id)).ToArray());
                        }
                        foreach (var target in hardware.Targets)
                        {
                            if (Volatile.Read(ref disposed) != 0) break;
                            Read(hardware, target, !rediscover && now >= selfUntil.GetValueOrDefault(target.Id));
                        }
                        if (rediscover) Notify(null, false);
                    }
                    catch (Exception error) when (error is not OutOfMemoryException)
                    {
                        topology = "";
                        Unavailable(error);
                    }
                    nextPoll = Environment.TickCount64 + 2000;
                }
                now = Environment.TickCount64;
                BrightnessWriteQueue.Requested? request = null;
                if (now >= nextWrite) lock (writeLock) request = writes.Take();
                if (request is not null)
                {
                    var target = hardware.Targets.FirstOrDefault(t => t.Id == request.Id);
                    if (target is not null && Displays.Any(d => d.Id == target.Id && d.Available))
                    {
                        selfUntil[target.Id] = now + 1500;
                        try { Publish(target, hardware.Write(target, request.Percent), null, false); }
                        catch (Exception error) when (error is not OutOfMemoryException) { Publish(target, null, error.Message, false); }
                    }
                    lock (writeLock)
                        if (pendingRevisions.GetValueOrDefault(request.Id) == request.Revision) pendingRevisions.Remove(request.Id);
                    Notify(Displays.FirstOrDefault(d => d.Id == request.Id), false);
                    nextWrite = Environment.TickCount64 + 120; // Coalesce drag writes: at most ~8 operations/sec.
                }
                bool pending;
                lock (writeLock) pending = writes.HasPending;
                var due = pending ? Math.Min(nextPoll, nextWrite) : nextPoll;
                wake.WaitOne((int)Math.Clamp(due - Environment.TickCount64, 1, 2000));
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Unavailable(error);
        }
        finally { hardware.Dispose(); wake.Dispose(); }
    }
    private void Unavailable(Exception error)
    {
        Status = "Brightness unavailable: " + error.Message;
        Volatile.Write(ref displays, Volatile.Read(ref displays).Select(d => d with { Available = false, Message = Status }).ToArray());
        Notify(null, false);
    }
    private void Read(BrightnessHardware hardware, BrightnessHardware.Target target, bool external)
    {
        try { Publish(target, hardware.Read(target), null, external); }
        catch (Exception error) when (error is not OutOfMemoryException) { Publish(target, null, error.Message, false); }
    }
    private void Publish(BrightnessHardware.Target target, int? percent, string? error, bool external)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        var before = Volatile.Read(ref displays);
        var previous = before.FirstOrDefault(d => d.Id == target.Id);
        var next = new BrightnessDisplay(target.Id, target.Name, target.Monitor, percent, percent.HasValue,
            target.Backend, error ?? $"{target.Name} · {percent}% · {target.Backend}");
        if (previous == next) return;
        Volatile.Write(ref displays, before.Where(d => d.Id != target.Id).Append(next).ToArray());
        Notify(next, external && previous?.Available == true && next.Available && previous.Percent != next.Percent);
    }
    private void Notify(BrightnessDisplay? display, bool external)
    {
        if (Volatile.Read(ref disposed) == 0) Changed?.Invoke(this, new(display, external));
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        if (worker is null) wake.Dispose();
        else
        {
            try { wake.Set(); } catch (ObjectDisposedException) { }
        }
        // Do not join a worker blocked in a monitor driver; it releases its own handles on return.
    }
}

internal sealed class BrightnessWriteQueue
{
    internal sealed record Requested(string Id, int Percent, long Revision = 0);
    private readonly Dictionary<string, Requested> pending = new();
    internal bool HasPending => pending.Count != 0;
    internal void Request(string id, int percent, long revision = 0)
    {
        if (string.IsNullOrWhiteSpace(id) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        pending[id] = new(id, percent, revision);
    }
    internal Requested? Take()
    {
        var next = pending.Values.FirstOrDefault();
        if (next is not null) pending.Remove(next.Id);
        return next;
    }
    internal void Keep(ISet<string> ids)
    {
        foreach (var id in pending.Keys.Where(k => !ids.Contains(k)).ToArray()) pending.Remove(id);
    }
}
