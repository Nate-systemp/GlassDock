using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace GlassDock.Windows.Applications;

/// <summary>
/// Low-memory, bounded cache of the last visible window frames.
///
/// Visible windows use normal DWM thumbnails. This cache exists only so a
/// minimized window can still be painted without restoring the real HWND.
///
/// RAM policy:
/// - only one Windows.Graphics.Capture session may exist at a time;
/// - a capture session is disposed immediately after one useful frame;
/// - retained frames are scaled to at most 960x540 BGRA;
/// - retained CPU pixels are capped at 24 MiB;
/// - minimized frames are preferred during eviction because they cannot be
///   recaptured until their real window is restored.
/// </summary>
public sealed class WindowFrameCache : IDisposable
{
    internal sealed record Frame(
        int Width,
        int Height,
        byte[] Pixels,
        DateTimeOffset CapturedAt);

    private sealed class Entry
    {
        public ApplicationWindow Window;
        public Frame? Latest;
        public CaptureJob? Capture;
        public bool Queued;
        public long LastAttemptTicks;

        public Entry(ApplicationWindow window)
        {
            Window = window;
        }

        public long RetainedBytes =>
            Volatile.Read(ref Latest)?.Pixels.LongLength ?? 0;
    }

    private readonly PreviewDiagnostics diagnostics = new();
    public string DiagnosticsPath => diagnostics.Path;

    private readonly object gate = new();

    private readonly Dictionary<
        (long Handle, int Process, long Started),
        Entry> entries = [];

    private IDirect3DDevice? device;
    private bool disposed;
    private int activeCaptures;

    // Keeping this at one is deliberate. A WGC frame pool is full source
    // resolution, so limiting concurrent pools saves much more RAM/GPU memory
    // than merely lowering the number of cached byte arrays.
    private const int MaxConcurrentCaptures = 1;

    // Retained preview frames are intentionally much smaller than a desktop
    // source. 960x540 BGRA is ~1.98 MiB per full-size cached frame.
    private const int MaxRetainedWidth = 960;
    private const int MaxRetainedHeight = 540;

    private const int MaxRetainedFrames = 16;
    private const long RetainedByteBudget = 24L * 1024 * 1024;

    // The active window is the one most likely to be minimized next.
    // Inactive windows are refreshed much less often to avoid keeping WGC busy.
    private static readonly TimeSpan ActiveRefreshAge =
        TimeSpan.FromSeconds(8);

    private static readonly TimeSpan InactiveRefreshAge =
        TimeSpan.FromSeconds(45);

    private static readonly TimeSpan FailedRetryAge =
        TimeSpan.FromSeconds(5);

    public WindowFrameCache() =>
        diagnostics.Write(
            null,
            "capture-policy",
            false,
            null,
            new
            {
                borderless = false,
                reason =
                    "Low-memory one-shot capture: at most one WGC pool is alive; retained frames are capped at 960x540.",
                maxConcurrentCaptures = MaxConcurrentCaptures,
                maxRetainedFrames = MaxRetainedFrames,
                retainedByteBudget = RetainedByteBudget,
                maxRetainedWidth = MaxRetainedWidth,
                maxRetainedHeight = MaxRetainedHeight
            });

    public void Report(
        ApplicationWindow window,
        string stage,
        object? detail = null)
    {
        Frame? latest;
        bool tracked;

        lock (gate)
        {
            tracked =
                entries.TryGetValue(
                    Key(window),
                    out var entry);

            latest =
                tracked
                    ? Volatile.Read(ref entry!.Latest)
                    : null;
        }

        diagnostics.Write(
            window,
            stage,
            tracked,
            latest,
            detail);
    }

    /// <summary>
    /// Updates the set of windows that may need a minimized fallback.
    ///
    /// This does NOT keep a capture session alive for every window. Visible
    /// windows are queued for one-shot refreshes; minimized windows only retain
    /// their last already-captured frame.
    /// </summary>
    public void Track(
        IEnumerable<ApplicationWindow> windows)
    {
        var current =
            windows
                .DistinctBy(Key)
                .ToDictionary(Key);

        List<CaptureJob>? cancelled = null;

        lock (gate)
        {
            if (disposed)
                return;

            foreach (
                var key in
                entries.Keys
                    .Where(
                        key =>
                            !current.ContainsKey(key))
                    .ToArray())
            {
                var entry =
                    entries[key];

                if (entry.Capture is not null)
                {
                    cancelled ??= [];
                    cancelled.Add(
                        entry.Capture);

                    entry.Capture = null;

                    if (activeCaptures > 0)
                        activeCaptures--;
                }

                Volatile.Write(
                    ref entry.Latest,
                    null);

                entries.Remove(
                    key);
            }

            foreach (
                var window in
                current.Values)
            {
                var key =
                    Key(window);

                if (!entries.TryGetValue(
                        key,
                        out var entry))
                {
                    entry =
                        new Entry(window);

                    entries.Add(
                        key,
                        entry);
                }
                else
                {
                    entry.Window =
                        window;
                }

                QueueIfNeededNoLock(
                    entry);
            }

            TrimRetainedFramesNoLock();
        }

        if (cancelled is not null)
        {
            foreach (
                var job in
                cancelled)
            {
                job.Dispose();
            }
        }

        PumpCaptures();
    }

    internal Frame? Get(
        ApplicationWindow window)
    {
        lock (gate)
        {
            if (disposed)
                return null;

            return entries.TryGetValue(
                    Key(window),
                    out var entry)
                ? Volatile.Read(
                    ref entry.Latest)
                : null;
        }
    }

    private void QueueIfNeededNoLock(
        Entry entry)
    {
        if (entry.Capture is not null ||
            entry.Queued)
        {
            return;
        }

        var window =
            entry.Window;

        var handle =
            (nint)window.Handle;

        // A minimized source cannot be refreshed safely. Its last cached frame
        // is exactly what we are preserving.
        if (NativeMethods.IsIconic(handle) ||
            !WindowsApplicationService.IsEligible(
                window))
        {
            return;
        }

        var now =
            DateTimeOffset.UtcNow;

        var latest =
            Volatile.Read(
                ref entry.Latest);

        var refreshAge =
            window.IsActive
                ? ActiveRefreshAge
                : InactiveRefreshAge;

        if (latest is not null &&
            now - latest.CapturedAt <
            refreshAge)
        {
            return;
        }

        var ticks =
            Environment.TickCount64;

        if (entry.LastAttemptTicks != 0 &&
            ticks -
            entry.LastAttemptTicks <
            FailedRetryAge.TotalMilliseconds)
        {
            return;
        }

        entry.Queued = true;
    }

    private void PumpCaptures()
    {
        CaptureJob? job = null;

        lock (gate)
        {
            if (disposed ||
                activeCaptures >=
                MaxConcurrentCaptures)
            {
                return;
            }

            if (!GraphicsCaptureSession.IsSupported())
            {
                foreach (
                    var entry in
                    entries.Values)
                {
                    entry.Queued = false;
                }

                ReleaseDeviceNoLock();
                return;
            }

            var candidate =
                entries.Values
                    .Where(
                        entry =>
                            entry.Queued &&
                            entry.Capture is null &&
                            !NativeMethods.IsIconic(
                                (nint)entry.Window.Handle) &&
                            WindowsApplicationService
                                .IsEligible(entry.Window))
                    .OrderByDescending(
                        entry =>
                            entry.Window.IsActive)
                    .ThenBy(
                        entry =>
                            Volatile.Read(
                                ref entry.Latest) is null
                                ? 0
                                : 1)
                    .ThenByDescending(
                        entry =>
                            entry.Window
                                .LastActivatedTicks)
                    .FirstOrDefault();

            if (candidate is null)
            {
                ReleaseDeviceNoLock();
                return;
            }

            candidate.Queued = false;

            candidate.LastAttemptTicks =
                Environment.TickCount64;

            try
            {
                device ??=
                    CreateDevice();

                job =
                    new CaptureJob(
                        device,
                        candidate.Window,
                        candidate,
                        CaptureCompleted);

                candidate.Capture =
                    job;

                activeCaptures++;
            }
            catch (
                Exception error)
                when (
                    error is COMException or
                    ArgumentException or
                    InvalidOperationException)
            {
                diagnostics.Write(
                    candidate.Window,
                    "capture-start-failed",
                    Volatile.Read(
                        ref candidate.Latest) is not null,
                    Volatile.Read(
                        ref candidate.Latest),
                    new
                    {
                        error =
                            error.Message,
                        hresult =
                            error.HResult
                    });

                ReleaseDeviceNoLock();
            }
        }

        if (job is null)
        {
            // A failed source must not permanently block the rest of the queue.
            PumpCaptures();
            return;
        }

        try
        {
            job.Start();
        }
        catch (
            Exception error)
            when (
                error is COMException or
                ArgumentException or
                InvalidOperationException)
        {
            CaptureCompleted(
                job.Entry,
                job,
                null,
                error);
        }
    }

    private void CaptureCompleted(
        Entry entry,
        CaptureJob job,
        Frame? frame,
        Exception? error)
    {
        bool accepted = false;
        ApplicationWindow window;

        // Dispose the full-resolution WGC resources before we pump the next
        // source. That keeps peak native/GPU memory bounded to one pool.
        job.Dispose();

        lock (gate)
        {
            window =
                entry.Window;

            if (!ReferenceEquals(
                    entry.Capture,
                    job))
            {
                return;
            }

            entry.Capture = null;

            if (activeCaptures > 0)
                activeCaptures--;

            var key =
                Key(window);

            if (!disposed &&
                entries.TryGetValue(
                    key,
                    out var liveEntry) &&
                ReferenceEquals(
                    liveEntry,
                    entry) &&
                frame is not null &&
                !NativeMethods.IsIconic(
                    (nint)window.Handle) &&
                WindowsApplicationService
                    .IsEligible(window))
            {
                Volatile.Write(
                    ref entry.Latest,
                    frame);

                accepted = true;

                TrimRetainedFramesNoLock();
            }

            if (activeCaptures == 0 &&
                !entries.Values.Any(
                    value =>
                        value.Queued))
            {
                ReleaseDeviceNoLock();
            }
        }

        if (error is not null)
        {
            diagnostics.Write(
                window,
                "frame-failed",
                Volatile.Read(
                    ref entry.Latest) is not null,
                Volatile.Read(
                    ref entry.Latest),
                new
                {
                    error =
                        error.Message,
                    hresult =
                        error.HResult
                });
        }
        else if (accepted)
        {
            diagnostics.Write(
                window,
                "frame-retained",
                true,
                frame,
                new
                {
                    retainedBytes =
                        frame!.Pixels.LongLength,
                    scaled =
                        new
                        {
                            frame.Width,
                            frame.Height
                        }
                });
        }

        PumpCaptures();
    }

    private void TrimRetainedFramesNoLock()
    {
        long bytes =
            entries.Values.Sum(
                entry =>
                    entry.RetainedBytes);

        var frames =
            entries.Values.Count(
                entry =>
                    Volatile.Read(
                        ref entry.Latest) is not null);

        if (bytes <=
                RetainedByteBudget &&
            frames <=
                MaxRetainedFrames)
        {
            return;
        }

        // First drop visible/inactive frames: those can be cheaply recaptured
        // later. Minimized frames are kept as long as the budget allows.
        var victims =
            entries.Values
                .Where(
                    entry =>
                        Volatile.Read(
                            ref entry.Latest) is not null)
                .OrderBy(
                    entry =>
                        NativeMethods.IsIconic(
                            (nint)entry.Window.Handle)
                            ? 1
                            : 0)
                .ThenBy(
                    entry =>
                        entry.Window.IsActive
                            ? 1
                            : 0)
                .ThenBy(
                    entry =>
                        Volatile.Read(
                            ref entry.Latest)!
                            .CapturedAt)
                .ToArray();

        foreach (
            var victim in
            victims)
        {
            if (bytes <=
                    RetainedByteBudget &&
                frames <=
                    MaxRetainedFrames)
            {
                break;
            }

            var previous =
                Volatile.Read(
                    ref victim.Latest);

            if (previous is null)
                continue;

            Volatile.Write(
                ref victim.Latest,
                null);

            bytes -=
                previous.Pixels
                    .LongLength;

            frames--;

            // If the source is still visible, allow it to be filled again on
            // a later Track call. Do not immediately queue here or trimming
            // would simply refill the budget in the same pass.
            victim.Queued = false;
        }
    }

    private void ReleaseDeviceNoLock()
    {
        if (activeCaptures != 0 ||
            entries.Values.Any(
                entry =>
                    entry.Capture is not null))
        {
            return;
        }

        device?.Dispose();
        device = null;
    }

    private static (
        long Handle,
        int Process,
        long Started)
        Key(
            ApplicationWindow window) =>
        (
            window.Handle,
            window.ProcessId,
            window.ProcessStartTicks
        );

    private sealed class CaptureJob :
        IDisposable
    {
        private readonly ApplicationWindow window;
        private readonly Direct3D11CaptureFramePool pool;
        private readonly GraphicsCaptureSession session;
        private readonly Action<
            Entry,
            CaptureJob,
            Frame?,
            Exception?> completion;

        private readonly System.Threading.Timer timeout;

        private int finished;
        private int copying;
        private int resourcesDisposed;

        public Entry Entry { get; }

        public CaptureJob(
            IDirect3DDevice device,
            ApplicationWindow window,
            Entry entry,
            Action<
                Entry,
                CaptureJob,
                Frame?,
                Exception?> completion)
        {
            this.window =
                window;

            Entry =
                entry;

            this.completion =
                completion;

            _ =
                entry ??
                throw new ArgumentNullException(
                    nameof(entry));

            var item =
                CreateItem(
                    (nint)window.Handle);

            if (item.Size.Width <= 0 ||
                item.Size.Height <= 0)
            {
                throw new InvalidOperationException(
                    "Capture source has invalid dimensions.");
            }

            pool =
                Direct3D11CaptureFramePool
                    .CreateFreeThreaded(
                        device,
                        DirectXPixelFormat
                            .B8G8R8A8UIntNormalized,
                        2,
                        item.Size);

            GraphicsCaptureSession? created =
                null;

            try
            {
                session =
                    created =
                        pool.CreateCaptureSession(
                            item);

                session.IsCursorCaptureEnabled =
                    false;

                pool.FrameArrived +=
                    OnFrame;
            }
            catch
            {
                created?.Dispose();
                pool.Dispose();
                throw;
            }

            timeout =
                new System.Threading.Timer(
                    _ =>
                        OnTimeout(),
                    null,
                    System.Threading.Timeout.Infinite,
                    System.Threading.Timeout.Infinite);
        }

        public void Start()
        {
            if (Volatile.Read(
                    ref finished) != 0)
            {
                return;
            }

            timeout.Change(
                5000,
                System.Threading.Timeout.Infinite);

            session.StartCapture();
        }

        private async void OnFrame(
            Direct3D11CaptureFramePool sender,
            object args)
        {
            if (Volatile.Read(
                    ref finished) != 0 ||
                Interlocked.CompareExchange(
                    ref copying,
                    1,
                    0) != 0)
            {
                return;
            }

            Direct3D11CaptureFrame? frame =
                null;

            Frame? captured =
                null;

            Exception? failure =
                null;

            var complete =
                false;

            try
            {
                frame =
                    sender.TryGetNextFrame();

                if (frame is null)
                    return;

                if (NativeMethods.IsIconic(
                        (nint)window.Handle) ||
                    !WindowsApplicationService
                        .IsEligible(window))
                {
                    complete = true;
                    return;
                }

                captured =
                    await CreateRetainedFrameAsync(
                        frame);

                complete = true;
            }
            catch (
                Exception error)
                when (
                    error is COMException or
                    ArgumentException or
                    InvalidOperationException or
                    TimeoutException)
            {
                failure =
                    error;

                complete =
                    true;
            }
            finally
            {
                frame?.Dispose();

                Volatile.Write(
                    ref copying,
                    0);

                if (complete)
                {
                    Finish(
                        captured,
                        failure);
                }
            }
        }

        private static async Task<Frame?>
            CreateRetainedFrameAsync(
                Direct3D11CaptureFrame frame)
        {
            var size =
                frame.ContentSize;

            if (size.Width <= 0 ||
                size.Height <= 0)
            {
                return null;
            }

            using var bitmap =
                await SoftwareBitmap
                    .CreateCopyFromSurfaceAsync(
                        frame.Surface,
                        BitmapAlphaMode.Ignore);

            if (bitmap.PixelWidth <= 0 ||
                bitmap.PixelHeight <= 0)
            {
                return null;
            }

            var scale =
                Math.Min(
                    1d,
                    Math.Min(
                        (double)MaxRetainedWidth /
                        bitmap.PixelWidth,
                        (double)MaxRetainedHeight /
                        bitmap.PixelHeight));

            var targetWidth =
                Math.Max(
                    1,
                    (int)Math.Round(
                        bitmap.PixelWidth *
                        scale));

            var targetHeight =
                Math.Max(
                    1,
                    (int)Math.Round(
                        bitmap.PixelHeight *
                        scale));

            SoftwareBitmap retainedBitmap;

            if (targetWidth ==
                    bitmap.PixelWidth &&
                targetHeight ==
                    bitmap.PixelHeight)
            {
                retainedBitmap =
                    bitmap;
            }
            else
            {
                using var stream =
                    new InMemoryRandomAccessStream();

                var encoder =
                    await BitmapEncoder
                        .CreateAsync(
                            BitmapEncoder.BmpEncoderId,
                            stream);

                encoder.SetSoftwareBitmap(
                    bitmap);

                encoder.BitmapTransform
                    .ScaledWidth =
                    (uint)targetWidth;

                encoder.BitmapTransform
                    .ScaledHeight =
                    (uint)targetHeight;

                encoder.BitmapTransform
                    .InterpolationMode =
                    BitmapInterpolationMode.Fant;

                await encoder.FlushAsync();

                stream.Seek(0);

                var decoder =
                    await BitmapDecoder
                        .CreateAsync(
                            stream);

                retainedBitmap =
                    await decoder
                        .GetSoftwareBitmapAsync(
                            BitmapPixelFormat.Bgra8,
                            BitmapAlphaMode.Ignore);
            }

            try
            {
                var pixels =
                    new byte[
                        checked(
                            retainedBitmap.PixelWidth *
                            retainedBitmap.PixelHeight *
                            4)];

                retainedBitmap.CopyToBuffer(
                    pixels.AsBuffer());

                var hasColor =
                    false;

                // Reject empty WGC surfaces. A genuinely all-black window
                // conservatively keeps its previous cached frame, matching
                // the existing GlassDock behavior.
                for (
                    var i = 0;
                    i < pixels.Length;
                    i += 4)
                {
                    if (pixels[i] != 0 ||
                        pixels[i + 1] != 0 ||
                        pixels[i + 2] != 0)
                    {
                        hasColor = true;
                        break;
                    }
                }

                if (!hasColor)
                    return null;

                return new Frame(
                    retainedBitmap.PixelWidth,
                    retainedBitmap.PixelHeight,
                    pixels,
                    DateTimeOffset.UtcNow);
            }
            finally
            {
                if (!ReferenceEquals(
                        retainedBitmap,
                        bitmap))
                {
                    retainedBitmap.Dispose();
                }
            }
        }

        private void OnTimeout()
        {
            if (Volatile.Read(
                    ref finished) != 0)
            {
                return;
            }

            if (Volatile.Read(
                    ref copying) != 0)
            {
                timeout.Change(
                    1000,
                    System.Threading.Timeout.Infinite);

                return;
            }

            Finish(
                null,
                new TimeoutException(
                    "Window capture timed out."));
        }

        private void Finish(
            Frame? frame,
            Exception? error)
        {
            if (Interlocked.Exchange(
                    ref finished,
                    1) != 0)
            {
                return;
            }

            timeout.Change(
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);

            completion(
                Entry,
                this,
                frame,
                error);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(
                    ref resourcesDisposed,
                    1) != 0)
            {
                return;
            }

            Interlocked.Exchange(
                ref finished,
                1);

            timeout.Change(
                System.Threading.Timeout.Infinite,
                System.Threading.Timeout.Infinite);

            pool.FrameArrived -=
                OnFrame;

            session.Dispose();
            pool.Dispose();
            timeout.Dispose();
        }

    }

    private static IDirect3DDevice
        CreateDevice()
    {
        nint native = 0;
        nint context = 0;
        nint dxgi = 0;
        nint inspectable = 0;

        try
        {
            Marshal.ThrowExceptionForHR(
                D3D11CreateDevice(
                    0,
                    1,
                    0,
                    0x20,
                    0,
                    0,
                    7,
                    out native,
                    out _,
                    out context));

            var iid =
                new Guid(
                    "54ec77fa-1377-44e6-8c32-88fd5f44c84c");

            Marshal.ThrowExceptionForHR(
                Marshal.QueryInterface(
                    native,
                    in iid,
                    out dxgi));

            Marshal.ThrowExceptionForHR(
                CreateDirect3D11DeviceFromDXGIDevice(
                    dxgi,
                    out inspectable));

            return WinRT
                .MarshalInterface<
                    IDirect3DDevice>
                .FromAbi(
                    inspectable);
        }
        finally
        {
            foreach (
                var pointer in
                new[]
                {
                    inspectable,
                    dxgi,
                    context,
                    native
                })
            {
                if (pointer != 0)
                    Marshal.Release(
                        pointer);
            }
        }
    }

    [DllImport("d3d11.dll")]
    private static extern int
        D3D11CreateDevice(
            nint adapter,
            uint driverType,
            nint software,
            uint flags,
            nint levels,
            uint levelCount,
            uint sdk,
            out nint device,
            out uint level,
            out nint context);

    [DllImport("d3d11.dll")]
    private static extern int
        CreateDirect3D11DeviceFromDXGIDevice(
            nint device,
            out nint result);

    [ComImport]
    [Guid(
        "3628e81b-3cac-4c60-b7f4-23ce0e0c3356")]
    [InterfaceType(
        ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICaptureInterop
    {
        nint CreateForWindow(
            nint window,
            in Guid iid);

        nint CreateForMonitor(
            nint monitor,
            in Guid iid);
    }

    [DllImport("combase.dll")]
    private static extern int
        WindowsCreateString(
            [MarshalAs(
                UnmanagedType.LPWStr)]
            string text,
            int length,
            out nint value);

    [DllImport("combase.dll")]
    private static extern int
        WindowsDeleteString(
            nint value);

    [DllImport("combase.dll")]
    private static extern int
        RoGetActivationFactory(
            nint name,
            in Guid iid,
            out ICaptureInterop factory);

    private static GraphicsCaptureItem
        CreateItem(
            nint window)
    {
        const string name =
            "Windows.Graphics.Capture.GraphicsCaptureItem";

        Marshal.ThrowExceptionForHR(
            WindowsCreateString(
                name,
                name.Length,
                out var text));

        try
        {
            Marshal.ThrowExceptionForHR(
                RoGetActivationFactory(
                    text,
                    typeof(ICaptureInterop)
                        .GUID,
                    out var factory));

            try
            {
                var pointer =
                    factory.CreateForWindow(
                        window,
                        new Guid(
                            "79c3f95b-31f7-4ec2-a464-632ef5d30760"));

                try
                {
                    return WinRT
                        .MarshalInterface<
                            GraphicsCaptureItem>
                        .FromAbi(
                            pointer);
                }
                finally
                {
                    Marshal.Release(
                        pointer);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(
                    factory);
            }
        }
        finally
        {
            WindowsDeleteString(
                text);
        }
    }

    public void Dispose()
    {
        List<CaptureJob> jobs;

        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;

            jobs =
                entries.Values
                    .Select(
                        entry =>
                            entry.Capture)
                    .Where(
                        job =>
                            job is not null)
                    .Cast<CaptureJob>()
                    .ToList();

            foreach (
                var entry in
                entries.Values)
            {
                entry.Capture = null;
                entry.Queued = false;

                Volatile.Write(
                    ref entry.Latest,
                    null);
            }

            entries.Clear();
            activeCaptures = 0;
        }

        foreach (
            var job in
            jobs)
        {
            job.Dispose();
        }

        lock (gate)
        {
            device?.Dispose();
            device = null;
        }
    }
}
