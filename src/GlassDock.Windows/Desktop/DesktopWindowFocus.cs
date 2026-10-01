using System.Runtime.InteropServices;

using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class DesktopWindowFocus : IDisposable
{
    private readonly nint preview;
    private readonly NativeMethods.SubclassProc callback;
    private readonly WindowFrameCache? frameCache;
    private WindowFrameCache.Frame? cachedFrame;

    private nint dim, mirror;

    private bool mirrorShown;
    private ApplicationWindow? target;
    private WindowThumbnail? thumbnail;
    private long? dismissedWindow;
    private bool disposed;

    private NativeMethods.Rect lastFrame;
    private NativeMethods.Rect lastBounds;
    private bool hasLastPosition;

    public event EventHandler? Dismissed;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;

        public Point MinPosition;
        public Point MaxPosition;

        public NativeMethods.Rect NormalPosition;

        public NativeMethods.Rect Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;

        public NativeMethods.Rect Monitor;
        public NativeMethods.Rect Work;

        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(
        nint hWnd,
        ref WindowPlacement placement);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint hwnd,
        uint flags);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(
        nint monitor,
        ref MonitorInfo info);

    public DesktopWindowFocus(nint previewWindow, WindowFrameCache? frameCache = null)
    {
        preview = previewWindow;
        this.frameCache = frameCache;

        callback = (
            window,
            message,
            w,
            l,
            id,
            data) =>
        {
            if (message == 0x84)
                return -1;

            if (message == 0x21)
                return 3;

            if (window == mirror &&
                message == 0x14)
            {
                return 1;
            }

            if (message == 0x0F)
            {
                if (window == mirror)
                {
                    PaintCachedFrame();
                    NativeMethods.ValidateRect(
                        window,
                        0);

                    return 0;
                }

                var dc =
                    NativeMethods.GetDC(
                        window);

                try
                {
                    if (dc != 0 &&
                        NativeMethods.GetClientRect(
                            window,
                            out var rect))
                    {
                        NativeMethods.FillRect(
                            dc,
                            ref rect,
                            NativeMethods.GetStockObject(4));
                    }
                }
                finally
                {
                    if (dc != 0)
                    {
                        NativeMethods.ReleaseDC(
                            window,
                            dc);
                    }
                }

                NativeMethods.ValidateRect(
                    window,
                    0);

                return 0;
            }

            if (message == 0x113)
            {
                if ((NativeMethods.GetAsyncKeyState(0x1B) &
                     0x8000) != 0)
                {
                    var handle =
                        target?.Handle;

                    Hide();

                    dismissedWindow =
                        handle;

                    Dismissed?.Invoke(
                        this,
                        EventArgs.Empty);
                }
                else
                {
                    Update();
                }

                return 0;
            }

            return NativeMethods.DefSubclassProc(
                window,
                message,
                w,
                l);
        };
    }

    public bool Show(
        ApplicationWindow window)
    {
        if (disposed)
            return false;

        if (dismissedWindow ==
            window.Handle)
        {
            return false;
        }

        dismissedWindow = null;

        using var coordinates =
            new PhysicalCoordinates();

        if (!Eligible(window))
        {
            Hide();
            return false;
        }

        if (!EnsureWindows())
        {
            Hide();
            return false;
        }

        if (target is null ||
            target.Handle != window.Handle ||
            target.ProcessId != window.ProcessId ||
            target.ProcessStartTicks !=
            window.ProcessStartTicks)
        {
            var previous =
                thumbnail;

            target = window;

            thumbnail =
                new WindowThumbnail(
                    mirror,
                    window);

            //
            // Force the newly-created DWM thumbnail
            // to receive its source and destination.
            //
            hasLastPosition = false;
            lastFrame = default;
            lastBounds = default;

            Update();

            NativeMethods.DwmFlush();

            previous?.Dispose();
        }
        else
        {
            Update();
        }

        if (target is not null &&
            NativeMethods.SetTimer(
                dim,
                1,
                250,
                0) == 0)
        {
            Hide();
        }

        return target is not null;
    }

    private static bool Eligible(
        ApplicationWindow window)
    {
        //
        // IMPORTANT:
        //
        // Do NOT reject minimized windows.
        // The real HWND stays minimized.
        // We only display a DWM mirror.
        //
        return WindowsApplicationService
            .IsEligible(window);
    }

    private bool EnsureWindows()
    {
        if (dim == 0)
        {
            dim = Create(
                "Doky desktop dim",
                153);
        }

        if (mirror == 0)
        {
            mirror = Create(
                "Doky desktop mirror",
                0);
        }

        return dim != 0 &&
               mirror != 0;
    }

    private nint Create(
        string title,
        byte opacity)
    {
        var window =
            NativeMethods.CreateWindowExW(
                0x080800A8,
                "STATIC",
                title,
                0x80000000,
                0,
                0,
                1,
                1,
                0,
                0,
                0,
                0);

        if (window == 0)
            return 0;

        if (!NativeMethods.SetWindowSubclass(
                window,
                callback,
                1,
                0) ||
            !NativeMethods.SetLayeredWindowAttributes(
                window,
                0,
                opacity,
                2))
        {
            NativeMethods.DestroyWindow(
                window);

            return 0;
        }

        return window;
    }

    private static bool TryGetVisualBounds(
        nint window,
        out NativeMethods.Rect bounds,
        out object? placementDetails)
    {
        bounds = default;
        placementDetails = null;

        //
        // Normal / visible window:
        // use its current real position.
        //
        if (!NativeMethods.IsIconic(window))
        {
            return NativeMethods.GetWindowRect(
                window,
                out bounds);
        }

        //
        // Minimized window:
        //
        // GetWindowRect() can report its iconic/minimized
        // coordinates instead of the position where it
        // would normally appear.
        //
        // GetWindowPlacement gives us NormalPosition,
        // which represents the restored window position.
        //
        var placement =
            new WindowPlacement
            {
                Length =
                    (uint)Marshal.SizeOf<
                        WindowPlacement>()
            };

        if (!GetWindowPlacement(
                window,
                ref placement))
        {
            return false;
        }

        bounds =
            placement.NormalPosition;

        //
        // WINDOWPLACEMENT uses workspace coordinates
        // for normal top-level windows.
        //
        // Convert that into desktop/screen coordinates
        // so our mirror appears in the correct location.
        //
        var monitor =
            MonitorFromWindow(
                window,
                2); // MONITOR_DEFAULTTONEAREST

        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info)) return false;

        var toolWindow = (NativeMethods.GetWindowLongPtr(window, -20).ToInt64() & 0x80) != 0;
        var converted = WorkspaceToScreen(ToPreviewRect(bounds), ToPreviewRect(info.Monitor),
            ToPreviewRect(info.Work), toolWindow);
        bounds = new NativeMethods.Rect
        {
            Left = (int)converted.X, Top = (int)converted.Y,
            Right = (int)(converted.X + converted.Width), Bottom = (int)(converted.Y + converted.Height)
        };
        placementDetails = new
        {
            hwnd = $"0x{window:X}", monitorHandle = $"0x{monitor:X}",
            rcNormalPosition = ToPreviewRect(placement.NormalPosition),
            monitorRect = ToPreviewRect(info.Monitor), workRect = ToPreviewRect(info.Work),
            finalMirrorRect = converted, toolWindow,
            sourceDpi = NativeMethods.GetDpiForWindow(window), coordinateSpace = "physical-screen-pixels"
        };
        return
            bounds.Right >
            bounds.Left &&
            bounds.Bottom >
            bounds.Top;
    }

    private static PreviewRect ToPreviewRect(NativeMethods.Rect rect) =>
        new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

    private static PreviewRect WorkspaceToScreen(PreviewRect normal, PreviewRect monitor, PreviewRect work, bool toolWindow)
    {
        // rcNormalPosition is not monitor-local. Add only the work-area inset, never the monitor origin.
        // Placement, monitor bounds and SetWindowPos share the caller's PMv2 physical coordinate context.
        if (toolWindow) return normal;
        return normal with { X = normal.X + work.X - monitor.X, Y = normal.Y + work.Y - monitor.Y };
    }

    private void Update()
    {
        if (target is null)
            return;

        using var coordinates =
            new PhysicalCoordinates();

        if (!NativeMethods.IsWindow(preview) ||
            !NativeMethods.IsWindowVisible(preview) ||
            !Eligible(target))
        {
            Hide();
            return;
        }

        var window =
            (nint)target.Handle;

        if (!TryGetVisualBounds(
                window,
                out var bounds, out var placementDetails))
        {
            Hide();
            return;
        }

        var frame =
            bounds;

        if (NativeMethods.IsIconic(window) && frameCache?.GetLastVisibleGeometry(target) is { } geometry)
        {
            // Match the last displayed snapped/maximized surface, not the normal
            // restored rectangle that WINDOWPLACEMENT reports while minimized.
            bounds = geometry.Bounds;
            frame = geometry.Frame;
            placementDetails = new { coordinateSpace = "last-visible-physical-screen-pixels", bounds = ToPreviewRect(bounds) };
        }

        //
        // Extended frame bounds are safe/useful
        // while the real source window is visible.
        //
        // Do NOT use DWM extended bounds while
        // minimized because they may represent
        // the minimized/iconic state.
        //
        if (!NativeMethods.IsIconic(window) &&
            NativeMethods.DwmGetFrameBounds(
                window,
                9,
                out var extended,
                Marshal.SizeOf<
                    NativeMethods.Rect>()) >= 0 &&
            extended.Right >
            extended.Left &&
            extended.Bottom >
            extended.Top)
        {
            frame =
                extended;
        }

        var width =
            frame.Right -
            frame.Left;

        var height =
            frame.Bottom -
            frame.Top;

        var nextFrame = NativeMethods.IsIconic(window) ? frameCache?.Get(target) : null;
        var contentChanged = !ReferenceEquals(nextFrame, cachedFrame);
        cachedFrame = nextFrame;
        var boundsChanged = contentChanged ||
            !hasLastPosition ||
            !frame.Equals(lastFrame) ||
            !bounds.Equals(lastBounds);

        if (width <= 0 ||
            height <= 0 ||
            thumbnail is null)
        {
            Hide();
            return;
        }

        //
        // Don't constantly reconfigure the DWM
        // thumbnail if the window has not moved.
        //
       if (boundsChanged && cachedFrame is null)
{
    var minimized =
        NativeMethods.IsIconic(window);

    if (!thumbnail.UpdateDesktop(
            new PreviewRect(
                frame.Left,
                frame.Top,
                width,
                height),
            new PreviewRect(
                bounds.Left,
                bounds.Top,
                bounds.Right - bounds.Left,
                bounds.Bottom - bounds.Top),
            cropSource: !minimized))
    {
        Hide();
        return;
    }
}

        var x =
            NativeMethods.GetSystemMetrics(
                76);

        var y =
            NativeMethods.GetSystemMetrics(
                77);

        var desktopWidth =
            NativeMethods.GetSystemMetrics(
                78);

        var desktopHeight =
            NativeMethods.GetSystemMetrics(
                79);

        if (desktopWidth <= 0 ||
            desktopHeight <= 0)
        {
            Hide();
            return;
        }

        if (boundsChanged)
        {
            frameCache?.Report(target, "mirror-position-requested", new
            {
                placement = placementDetails, finalMirrorRect = ToPreviewRect(frame),
                virtualScreenOrigin = new { x, y },
                setWindowPos = new { x = frame.Left, y = frame.Top, width, height }
            });
            if (!NativeMethods.SetWindowPos(
                    mirror,
                    preview,
                    frame.Left,
                    frame.Top,
                    width,
                    height,
                    0x10 | 0x40) ||
                !NativeMethods.SetWindowPos(
                    dim,
                    mirror,
                    x,
                    y,
                    desktopWidth,
                    desktopHeight,
                    0x10 | 0x40))
            {
                Hide();
                return;
            }

            lastFrame =
                frame;

            lastBounds =
                bounds;

            hasLastPosition =
                true;
        }

        if (cachedFrame is not null)
        {
            // Paint the replacement before removing DWM content. Only our mirror is touched.
            if (boundsChanged && !PaintCachedFrame())
            {
                frameCache?.Report(target, "cached-draw-failed");
                Hide();
                return;
            }
            thumbnail.SetVisible(false);
        }
        if (boundsChanged)
            frameCache?.Report(target, "focus-presented", new
            {
                path = cachedFrame is null ? "dwm-content-unverified" : "cached-frame",
                drawSucceeded = cachedFrame is not null,
                bounds = new { left = frame.Left, top = frame.Top, width, height },
                foreground = $"0x{ApplicationNative.GetForegroundWindow():X}"
            });

        if (!mirrorShown)
        {
            NativeMethods.DwmFlush();

            if (!NativeMethods
                .SetLayeredWindowAttributes(
                    mirror,
                    0,
                    255,
                    2))
            {
                Hide();
                return;
            }

            mirrorShown =
                true;
        }
    }

    public void Hide()
    {
        if (target is not null) frameCache?.Report(target, "focus-hidden");
        dismissedWindow = null;

        target = null;

        hasLastPosition = false;
        lastFrame = default;
        lastBounds = default;

        if (dim != 0)
        {
            NativeMethods.KillTimer(
                dim,
                1);

            NativeMethods.ShowWindow(
                dim,
                0);
        }

        if (mirror != 0)
        {
            NativeMethods.SetLayeredWindowAttributes(
                mirror,
                0,
                0,
                2);

            NativeMethods.ShowWindow(
                mirror,
                0);
        }

        mirrorShown =
            false;
        // Release compositor content only after the host is invisible.
        thumbnail?.Dispose();
        thumbnail = null;
        cachedFrame = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixels, YPixels;
        public uint Colors, Important;
    }
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(nint dc, int x, int y, int width, int height,
        int sourceX, int sourceY, int sourceWidth, int sourceHeight, byte[] pixels, ref BitmapInfo info, uint usage, uint operation);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);

    private bool PaintCachedFrame()
    {
        if (cachedFrame is not { } frame || mirror == 0) return false;
        var dc = NativeMethods.GetDC(mirror);
        nint buffer = 0, bitmap = 0, previous = 0;
        try
        {
            if (dc == 0 || !NativeMethods.GetClientRect(mirror, out var rect)) return false;
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return false;
            buffer = CreateCompatibleDC(dc);
            bitmap = CreateCompatibleBitmap(dc, width, height);
            if (buffer == 0 || bitmap == 0) return false;
            previous = SelectObject(buffer, bitmap);
            var ratio = Math.Min((double)width / frame.Width, (double)height / frame.Height);
            var drawWidth = Math.Max(1, (int)Math.Round(frame.Width * ratio));
            var drawHeight = Math.Max(1, (int)Math.Round(frame.Height * ratio));
            // Top-down BGRA DIB; aspect fit avoids distorting cached frames after a DPI/size change.
            var info = new BitmapInfo { Size = 40, Width = frame.Width, Height = -frame.Height, Planes = 1, Bits = 32 };
            NativeMethods.FillRect(buffer, ref rect, NativeMethods.GetStockObject(4));
            SetStretchBltMode(buffer, 4); // HALFTONE
            var copied = StretchDIBits(buffer, (width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight,
                0, 0, frame.Width, frame.Height, frame.Pixels, ref info, 0, 0x00CC0020);
            if (copied == 0 || copied == -1) return false;
            // Present the completed bitmap in one copy, never an intermediate black clear.
            return BitBlt(dc, 0, 0, width, height, buffer, 0, 0, 0x00CC0020);
        }
        finally
        {
            if (previous != 0) SelectObject(buffer, previous);
            if (bitmap != 0) NativeMethods.DeleteObject(bitmap);
            if (buffer != 0) DeleteDC(buffer);
            if (dc != 0) NativeMethods.ReleaseDC(mirror, dc);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Hide();

        disposed = true;

        foreach (var window in new[]
        {
            mirror,
            dim
        })
        {
            if (window == 0)
                continue;

            NativeMethods.RemoveWindowSubclass(
                window,
                callback,
                1);

            NativeMethods.DestroyWindow(
                window);
        }

        mirror = 0;
        dim = 0;

        GC.KeepAlive(callback);
    }

    private readonly struct PhysicalCoordinates :
        IDisposable
    {
        private readonly nint previous;

        public PhysicalCoordinates()
        {
            previous =
                NativeMethods
                    .SetThreadDpiAwarenessContext(
                        -4);
        }

        public void Dispose()
        {
            if (previous != 0)
            {
                NativeMethods
                    .SetThreadDpiAwarenessContext(
                        previous);
            }
        }
    }
}
