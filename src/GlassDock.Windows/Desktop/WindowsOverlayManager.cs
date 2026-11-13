using System.ComponentModel;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

public sealed class WindowsOverlayManager : IDisposable
{
    private readonly nint hwnd;
    private readonly NativeMethods.SubclassProc callback;
    private NativeMethods.WinEventProc? foregroundCallback;
    private nint foregroundHook;
    private NativeMethods.Point[]? lastInteractionPolygon;
    private nint inputRegion;
    private (int Left, int Top, int Right, int Bottom)[] lastInteractionIcons = [];
    private const nuint InputTimer = 0x4744;
    private const nuint TopmostTimer = 0x4745;

    private const uint AppBarCallbackMessage = 0x8044;
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbnPosChanged = 0x00000001;
    private const uint AbeBottom = 3;

    private bool appBarRegistered;
    private bool applyingAppBarReservation;
    private double reservedBottomSpaceDips;
    private nint reservationMonitor;
    private int preexistingBottomWorkAreaPixels;
    private bool expandedInput;
    private bool transparentInput;
    private NativeMethods.Point? previousPointer;
    private bool? lastPointerInsideInput;
    private nint lastForegroundWindow;
    private bool lastForegroundEligible;
    private bool lastForegroundScreenshot;
    private nint screenshotOverlay;
    private nint currentMonitor;
    private nint fixedMonitor;
    private NativeMethods.Rect currentMonitorBounds;
    private double currentMonitorScale;
    private bool hasCurrentMonitorMetrics;
    private bool disposed;
    private bool relocating;
    public void InvalidateMonitorMetrics() => hasCurrentMonitorMetrics = false;
    public bool FullscreenSuppressed { get; private set; }
    public event EventHandler? ScreenshotRequested;

    public void RefreshFullscreenPolicy()
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        var foreground = NativeMethods.GetForegroundWindow();
        var suppressed = false;
        if (foreground != 0 && foreground != hwnd)
        {
            bool eligible;
            if (foreground == lastForegroundWindow)
            {
                eligible = lastForegroundEligible;
            }
            else
            {
                NativeMethods.GetWindowThreadProcessId(foreground, out var owner);
                var name = new System.Text.StringBuilder(256);
                NativeMethods.GetClassName(foreground, name, name.Capacity);
                eligible = owner != Environment.ProcessId &&
                    name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
                lastForegroundScreenshot = InteractiveGlassWindowHost.IsScreenshotWindow(foreground);
                lastForegroundWindow = foreground;
                lastForegroundEligible = eligible;
            }

            if (eligible && !NativeMethods.IsIconic(foreground) && NativeMethods.GetWindowRect(foreground, out var bounds))
            {
                nint monitor;
                NativeMethods.Rect monitorBounds;
                if (hasCurrentMonitorMetrics && currentMonitor != 0)
                {
                    monitor = currentMonitor;
                    monitorBounds = currentMonitorBounds;
                }
                else
                {
                    monitor = NativeMethods.MonitorFromWindow(hwnd, 2);
                    var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
                    monitorBounds = NativeMethods.GetMonitorInfo(monitor, ref info) ? info.Monitor : default;
                }

                if (monitorBounds.Right > monitorBounds.Left && monitorBounds.Bottom > monitorBounds.Top)
                {
                    suppressed = FullscreenPolicy.CoversMonitor(eligible,
                        NativeMethods.MonitorFromWindow(foreground, 2) == monitor, false,
                        bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                        monitorBounds.Left, monitorBounds.Top, monitorBounds.Right, monitorBounds.Bottom);
                }
            }
        }
        if (suppressed && lastForegroundScreenshot)
        {
            suppressed = false;
            if (screenshotOverlay != foreground)
            {
                screenshotOverlay = foreground;
                ScreenshotRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        else screenshotOverlay = 0;
        if (FullscreenSuppressed == suppressed) return;
        FullscreenSuppressed = suppressed;
        NativeMethods.SetWindowPos(hwnd, suppressed ? -2 : -1, 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0010 | (suppressed ? 0x0080u : 0x0040u));
    }
    public event EventHandler? PointerMovedOutsideInput;
    public event EventHandler? PointerMovedInsideInput;
    public WindowsOverlayManager(nint hwnd, nint fixedMonitor = 0)
    {
        this.hwnd = hwnd;
        this.fixedMonitor = fixedMonitor;
        callback = WindowMessage;
    }

    public nint CurrentMonitor => currentMonitor;
    public nint FixedMonitor => fixedMonitor;

    public void SetFixedMonitor(nint monitor)
    {
        if (fixedMonitor == monitor) return;
        fixedMonitor = monitor;
        hasCurrentMonitorMetrics = false;
        lastInteractionPolygon = null;
        previousPointer = null;
        lastPointerInsideInput = null;
    }
    public const double Width = 1600;
    public const double Height = 320;
    public double CurrentHostWidthDips { get; private set; } = 960;
    private double dropAcquisitionWidthDips = 560;
    public double Scale => NativeMethods.GetDpiForWindow(hwnd) is var dpi && dpi > 0 ? dpi / 96d : 1;

    public void Configure(bool inspection = false)
    {
        var ex = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        var extendedStyle = inspection ? ((ex | 0x08040000L) & ~0x80L) : ((ex | 0x08000080L) & ~0x00040000L);
        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)(extendedStyle | 0x00080000L));
        if (!NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, 2)) throw new Win32Exception();
        var style = NativeMethods.GetWindowLongPtr(hwnd, -16).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -16, (nint)(style & ~0x00CF0000L));
        var noCorner = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, 33, ref noCorner, sizeof(int));
        ConfigureTransparency();
        if (!NativeMethods.SetWindowSubclass(hwnd, callback, 2, 0))
            throw new InvalidOperationException("Cannot attach overlay transparency handling.");
        var dc = NativeMethods.GetDC(hwnd);
        try { ClearBackground(dc); } finally { NativeMethods.ReleaseDC(hwnd, dc); }
        foregroundCallback = (_, _, _, _, _, _, _) => EnsureTopmost();
        foregroundHook = NativeMethods.SetWinEventHook(3, 3, 0, foregroundCallback, 0, 0, 2);
        if (foregroundHook == 0) throw new InvalidOperationException("Cannot monitor foreground changes for the desktop overlay.");
        EnsureTopmost();
        NativeMethods.SetTimer(hwnd, TopmostTimer, 350, 0);
    }

    private void ConfigureTransparency()
    {
        var margins = new NativeMethods.Margins();
        Marshal.ThrowExceptionForHR(NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins));
        var emptyRegion = NativeMethods.CreateRectRgn(-2, -2, -1, -1);
        try
        {
            var blur = new NativeMethods.BlurBehind { Flags = 3, Enable = 1, Region = emptyRegion };
            Marshal.ThrowExceptionForHR(NativeMethods.DwmEnableBlurBehindWindow(hwnd, ref blur));
        }
        finally { NativeMethods.DeleteObject(emptyRegion); }
    }

    private bool ClearBackground(nint dc)
    {
        if (dc == 0 || !NativeMethods.GetClientRect(hwnd, out var rect)) return false;
        return NativeMethods.FillRect(dc, ref rect, NativeMethods.GetStockObject(4)) != 0;
    }

    private nint WindowMessage(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == AppBarCallbackMessage)
        {
            if ((uint)wParam == AbnPosChanged && reservedBottomSpaceDips > 0)
                ApplyBottomWorkAreaReservation();
            return 0;
        }

        if (message == 0x0113 && wParam == TopmostTimer)
        {
            NativeMethods.KillTimer(hwnd, TopmostTimer);
            EnsureTopmost();
            return 0;
        }
        if (message == 0x0084 && inputRegion != 0)
        {
            var point = new NativeMethods.Point { X = (short)(long)lParam, Y = (short)((long)lParam >> 16) };
            var inside = ContainsScreenPoint(point);
            if (inside) lastPointerInsideInput = true;
            var acquiringExternalDrag = !inside && IsLeftButtonDown() && ContainsDropAcquisitionPoint(point);
            return inside || acquiringExternalDrag ? 1 : -1;
        }
        if (message == 0x0113 && wParam == InputTimer) { UpdateInputTransparency(); return 0; }
        if (message == 0x0014 && ClearBackground((nint)wParam)) return 1;

        if (message is 0x007E or 0x001A or 0x02E0)
        {
            hasCurrentMonitorMetrics = false;
            lastInteractionPolygon = null;
            previousPointer = null;
            lastPointerInsideInput = null;
        }

        if (message == 0x031E) ConfigureTransparency();
        return NativeMethods.DefSubclassProc(window, message, wParam, lParam);
    }

   private void EnsureTopmost()
    {
        RefreshFullscreenPolicy();
        if (FullscreenSuppressed || (NativeMethods.GetForegroundWindow() == lastForegroundWindow && lastForegroundScreenshot)) return;
        if (!NativeMethods.IsWindow(hwnd))
            return;

        NativeMethods.SetWindowPos(
            hwnd,
            -1,
            0,
            0,
            0,
            0,
            0x0001 |
            0x0002 |
            0x0010
        );
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ClearBottomWorkAreaReservation();
        NativeMethods.KillTimer(hwnd, InputTimer);
        NativeMethods.KillTimer(hwnd, TopmostTimer);
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        if (foregroundHook != 0) NativeMethods.UnhookWinEvent(foregroundHook);
        foregroundHook = 0;
        NativeMethods.RemoveWindowSubclass(hwnd, callback, 2);
        GC.KeepAlive(foregroundCallback);
    }

    public PixelRect Position(double margin, DockDisplayMode displayMode = DockDisplayMode.Primary)
    {
        var monitor = fixedMonitor != 0 ? fixedMonitor : ResolveMonitor(displayMode);
        if (monitor == 0)
            monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);

        return PositionOnMonitor(margin, monitor);
    }

    private PixelRect PositionOnMonitor(double margin, nint monitor)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!NativeMethods.IsWindow(hwnd)) throw new Win32Exception(1400);

        var monitorChanged = currentMonitor != 0 && currentMonitor != monitor;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            throw new Win32Exception();

        var screen = new PixelRect(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top);

        var scale = GetMonitorScale(monitor);
        if (screen.Width <= 0 || screen.Height <= 0 || !double.IsFinite(scale) || scale <= 0)
            throw new Win32Exception(87, "Destination monitor has invalid geometry or DPI.");

        var availableWidthDips = Math.Max(320d, screen.Width / scale - 32d);
        var hostWidthDips = availableWidthDips;

        var rect = DesktopPlacement.BottomCenter(
            screen,
            hostWidthDips,
            Height,
            margin,
            scale);

        if (!NativeMethods.SetWindowPos(
            hwnd,
            -1,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            0x0010 |
            0x0020 |
            0x0040
        ))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        currentMonitor = monitor;
        CurrentHostWidthDips = hostWidthDips;
        currentMonitorBounds = info.Monitor;
        currentMonitorScale = scale;
        hasCurrentMonitorMetrics = true;

        if (reservedBottomSpaceDips > 0)
            ApplyBottomWorkAreaReservation();

        if (monitorChanged)
        {
            lastInteractionPolygon = null;
            previousPointer = null;
        }

        EnsureTopmost();
        return rect;
    }

    public bool SetReservedBottomSpace(double heightDips)
    {
        if (!double.IsFinite(heightDips) || heightDips <= 0)
        {
            ClearBottomWorkAreaReservation();
            return true;
        }

        reservedBottomSpaceDips = heightDips;

        if (!appBarRegistered)
        {
            CapturePreexistingBottomWorkArea();
            var registration = CreateAppBarData();
            if (NativeMethods.SHAppBarMessage(AbmNew, ref registration) == 0)
            {
                reservedBottomSpaceDips = 0;
                return false;
            }

            appBarRegistered = true;
        }

        return ApplyBottomWorkAreaReservation();
    }

    public void ClearBottomWorkAreaReservation()
    {
        reservedBottomSpaceDips = 0;

        if (appBarRegistered)
        {
            var data = CreateAppBarData();
            NativeMethods.SHAppBarMessage(AbmRemove, ref data);
            appBarRegistered = false;
        }

        reservationMonitor = 0;
        preexistingBottomWorkAreaPixels = 0;
    }

    private void CapturePreexistingBottomWorkArea()
    {
        var monitor = currentMonitor != 0
            ? currentMonitor
            : NativeMethods.MonitorFromWindow(hwnd, 2);

        if (monitor == 0)
            return;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return;

        reservationMonitor = monitor;
        preexistingBottomWorkAreaPixels = Math.Max(0, info.Monitor.Bottom - info.Work.Bottom);
    }

    private NativeMethods.AppBarData CreateAppBarData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.AppBarData>(),
        Window = hwnd,
        CallbackMessage = AppBarCallbackMessage,
        Edge = AbeBottom
    };

    private bool ApplyBottomWorkAreaReservation()
    {
        if (!appBarRegistered || reservedBottomSpaceDips <= 0 || applyingAppBarReservation)
            return appBarRegistered;

        var monitor = currentMonitor != 0
            ? currentMonitor
            : NativeMethods.MonitorFromWindow(hwnd, 2);

        if (monitor == 0)
            return false;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        if (reservationMonitor != monitor)
            CapturePreexistingBottomWorkArea();

        var scale = hasCurrentMonitorMetrics && currentMonitor == monitor
            ? currentMonitorScale
            : GetMonitorScale(monitor);

        var heightPixels = DockSizing.AdditionalReservationPixels(reservedBottomSpaceDips,
            Math.Max(scale, 0.001), preexistingBottomWorkAreaPixels);

        var data = CreateAppBarData();
        data.Rectangle = info.Monitor;
        data.Rectangle.Top = data.Rectangle.Bottom - heightPixels;

        applyingAppBarReservation = true;
        try
        {
            NativeMethods.SHAppBarMessage(AbmQueryPos, ref data);

            data.Rectangle.Top = data.Rectangle.Bottom - heightPixels;

            var result = NativeMethods.SHAppBarMessage(AbmSetPos, ref data);
            return result != 0;
        }
        finally
        {
            applyingAppBarReservation = false;
        }
    }

    public bool RepositionIfMonitorChanged(double margin, DockDisplayMode displayMode,
        Action? beforeMove = null, Action? beforeCrossMonitorMove = null)
    {
        if (disposed || relocating || !NativeMethods.IsWindow(hwnd)) return false;
        var target = fixedMonitor != 0 ? fixedMonitor : ResolveMonitor(displayMode);
        if (target == 0)
            return false;

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (!NativeMethods.GetMonitorInfo(target, ref info))
            return false;

        var bounds = info.Monitor;
        var boundsChanged =
            !hasCurrentMonitorMetrics ||
            target != currentMonitor ||
            bounds.Left != currentMonitorBounds.Left ||
            bounds.Top != currentMonitorBounds.Top ||
            bounds.Right != currentMonitorBounds.Right ||
            bounds.Bottom != currentMonitorBounds.Bottom;

        if (!boundsChanged)
            return false;

        var targetScale = GetMonitorScale(target);
        var metricsChanged =
            boundsChanged ||
            Math.Abs(targetScale - currentMonitorScale) > 0.001;

        if (!metricsChanged)
            return false;

        relocating = true;
        try
        {
            beforeMove?.Invoke();
            if (currentMonitor != 0 && currentMonitor != target)
                beforeCrossMonitorMove?.Invoke();
            PositionOnMonitor(margin, target);
            return true;
        }
        finally { relocating = false; }
    }

    private nint ResolveMonitor(DockDisplayMode displayMode)
    {
        const uint DefaultToNearest = 2;

        if (displayMode == DockDisplayMode.Pointer &&
            NativeMethods.GetCursorPos(out var pointer))
        {
            return NativeMethods.MonitorFromPoint(pointer, DefaultToNearest);
        }

        if (displayMode == DockDisplayMode.Foreground)
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != 0 && foreground != hwnd)
            {
                var monitor = NativeMethods.MonitorFromWindow(foreground, DefaultToNearest);
                if (monitor != 0)
                    return monitor;
            }

            if (NativeMethods.GetCursorPos(out var fallbackPointer))
                return NativeMethods.MonitorFromPoint(fallbackPointer, DefaultToNearest);
        }

        return NativeMethods.MonitorFromPoint(new NativeMethods.Point(), 1);
    }

    private double GetMonitorScale(nint monitor)
    {
        try
        {
            if (monitor != 0 &&
                NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) >= 0 &&
                dpiX > 0)
            {
                return dpiX / 96d;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96d : 1;
    }

    public void SetInteractionRegion(bool expanded, double bottomMargin = 24)
    {
        NativeMethods.KillTimer(hwnd, InputTimer);
        SetInputTransparent(false);
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = 0;
        lastInteractionPolygon = null;
        lastPointerInsideInput = null;
        var scale = Scale;
        NativeMethods.SetWindowRgn(hwnd, 0, true);
        var hostWidth = NativeMethods.GetClientRect(hwnd, out var client) ? (client.Right - client.Left) / scale : Width;
        var hostHeight = client.Bottom > client.Top ? (client.Bottom - client.Top) / scale : Height;
        var x = expanded ? 0 : (hostWidth - 200) / 2;
        var y = expanded ? 0 : Math.Max(0, hostHeight - bottomMargin - 28);
        var width = expanded ? hostWidth : 200;
        var height = hostHeight - y;
        var region = NativeMethods.CreateRoundRectRgn((int)(x * scale), (int)(y * scale),
            (int)((x + width) * scale) + 1, (int)((y + height) * scale) + 1, (int)(16 * scale), (int)(16 * scale));
        if (region == 0) throw new Win32Exception();

        inputRegion = region;
        expandedInput = true;
        if (NativeMethods.GetCursorPos(out var current)) previousPointer = current;
        NativeMethods.SetTimer(hwnd, InputTimer, 50, 0);
        UpdateInputTransparency();
    }

    public bool IsPointerInsideInput()
    {
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var bounds))
            return false;

        if (inputRegion != 0)
        {
            var inside = ContainsScreenPoint(point, bounds);
            if (inside) lastPointerInsideInput = true;
            return inside;
        }

        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == 0)
            return false;
        try
        {
            var hasRegion = NativeMethods.GetWindowRgn(hwnd, region) > 0;
            var inside = hasRegion && PtInRegion(region, point.X - bounds.Left, point.Y - bounds.Top);
            if (inside) lastPointerInsideInput = true;
            return inside;
        }
        finally
        {
            NativeMethods.DeleteObject(region);
        }
    }

    public void SetPeekInteraction(double bottom)
    {
        var hostWidth = NativeMethods.GetClientRect(hwnd, out var client) ? (client.Right - client.Left) / Scale : Width;
        var hostHeight = client.Bottom > client.Top ? (client.Bottom - client.Top) / Scale : Height;
        var left = (hostWidth - 120) / 2;
        var top = hostHeight - bottom - 5;
        SetInteractionPolygon([(left, top), (left + 120, top),
            (left + 120, top + 13), (left, top + 13)]);
    }

    public bool TryGetPointerPosition(out double x, out double y)
    {
        x = 0; y = 0;
        if (!NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var bounds))
            return false;
        x = (point.X - bounds.Left) / Scale;
        y = (point.Y - bounds.Top) / Scale;
        return true;
    }
    public void SetInteractionPolygon(IReadOnlyList<(double X, double Y)> outline,
        IReadOnlyList<(double Left, double Top, double Right, double Bottom)>? iconBounds = null)
    {
        if (outline.Count < 3) return;
        var scale = Scale;
        var points = new NativeMethods.Point[outline.Count];
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        for (var i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var px = (int)Math.Round(p.X * scale);
            var py = (int)Math.Round(p.Y * scale);
            points[i] = new NativeMethods.Point { X = px, Y = py };
            if (px < minX) minX = px;
            if (px > maxX) maxX = px;
        }

        var polygonWidthPixels = maxX - minX;
        var polygonWidthDips = polygonWidthPixels / Math.Max(scale, 0.001);
        if (polygonWidthDips > 240)
            dropAcquisitionWidthDips = Math.Clamp(polygonWidthDips, 320, CurrentHostWidthDips);

        (int Left, int Top, int Right, int Bottom)[] pixelIcons;
        if (iconBounds is null || iconBounds.Count == 0)
        {
            pixelIcons = [];
        }
        else
        {
            var list = new List<(int Left, int Top, int Right, int Bottom)>(iconBounds.Count);
            for (var i = 0; i < iconBounds.Count; i++)
            {
                var bounds = iconBounds[i];
                if (!double.IsFinite(bounds.Left) || !double.IsFinite(bounds.Top) ||
                    !double.IsFinite(bounds.Right) || !double.IsFinite(bounds.Bottom) ||
                    bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) continue;
                list.Add((
                    (int)Math.Floor(bounds.Left * scale),
                    (int)Math.Floor(bounds.Top * scale),
                    (int)Math.Ceiling(bounds.Right * scale),
                    (int)Math.Ceiling(bounds.Bottom * scale)));
            }
            pixelIcons = list.ToArray();
        }

        if (lastInteractionPolygon is { } previous && points.AsSpan().SequenceEqual(previous) &&
            lastInteractionIcons.AsSpan().SequenceEqual(pixelIcons))
        {
            DockPerformanceDiagnostics.RecordInputRegion(rebuilt: false);
            return;
        }
        DockPerformanceDiagnostics.RecordInputRegion(rebuilt: true);
        var region = NativeMethods.CreatePolygonRgn(points, points.Length, 2              );
        if (region == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            foreach (var rect in pixelIcons)
            {
                var extra = NativeMethods.CreateRectRgn(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (extra == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    if (NativeMethods.CombineRgn(region, region, extra, 2             ) == 0)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                finally { NativeMethods.DeleteObject(extra); }
            }
        }
        catch { NativeMethods.DeleteObject(region); throw; }
        lastInteractionIcons = pixelIcons;
        lastPointerInsideInput = null;
        if (!expandedInput)
        {
            if (NativeMethods.GetCursorPos(out var current)) previousPointer = current;
            NativeMethods.SetWindowRgn(hwnd, 0, true);
            expandedInput = true;
            NativeMethods.SetTimer(hwnd, InputTimer, 50, 0);
        }
        if (inputRegion != 0) NativeMethods.DeleteObject(inputRegion);
        inputRegion = region;
        lastInteractionPolygon = points;
        UpdateInputTransparency();
    }

    private bool ContainsScreenPoint(NativeMethods.Point point) =>
        inputRegion != 0 && NativeMethods.GetWindowRect(hwnd, out var bounds) &&
        ContainsScreenPoint(point, bounds);

    private bool ContainsScreenPoint(NativeMethods.Point point, NativeMethods.Rect bounds) =>
        inputRegion != 0 &&
        point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom &&
        PtInRegion(inputRegion, point.X - bounds.Left, point.Y - bounds.Top);

    private void UpdateInputTransparency()
    {
        if (inputRegion == 0 || !NativeMethods.GetCursorPos(out var point) || !NativeMethods.GetWindowRect(hwnd, out var bounds)) return;
        var inside = ContainsScreenPoint(point, bounds);
        var acquiringExternalDrag = !inside && IsLeftButtonDown() && ContainsDropAcquisitionPoint(point, bounds);
        SetInputTransparent(!(inside || acquiringExternalDrag));
        var moved = previousPointer is { } previous && (previous.X != point.X || previous.Y != point.Y);
        previousPointer = point;
        if (moved)
        {
            if (inside)
            {
                lastPointerInsideInput = true;
                PointerMovedInsideInput?.Invoke(this, EventArgs.Empty);
            }
            else if (lastPointerInsideInput != false)
            {
                lastPointerInsideInput = false;
                PointerMovedOutsideInput?.Invoke(this, EventArgs.Empty);
            }
        }
        else if (inside)
        {
            lastPointerInsideInput = true;
        }
    }

    private static bool IsLeftButtonDown() =>
        (NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0;

    private bool ContainsDropAcquisitionPoint(NativeMethods.Point point) =>
        NativeMethods.GetWindowRect(hwnd, out var bounds) && ContainsDropAcquisitionPoint(point, bounds);

    private bool ContainsDropAcquisitionPoint(NativeMethods.Point point, NativeMethods.Rect bounds)
    {
        var scale = Math.Max(Scale, 0.001);
        var hostWidthPixels = bounds.Right - bounds.Left;
        var hostHeightPixels = bounds.Bottom - bounds.Top;
        if (hostWidthPixels <= 0 || hostHeightPixels <= 0)
            return false;

        var localX = point.X - bounds.Left;
        var localY = point.Y - bounds.Top;

        var requestedWidthPixels = (int)Math.Round(
            Math.Clamp(dropAcquisitionWidthDips + 112, 360, CurrentHostWidthDips) * scale);
        var acquisitionWidthPixels = Math.Min(hostWidthPixels, requestedWidthPixels);
        var minX = (hostWidthPixels - acquisitionWidthPixels) / 2;
        var maxX = minX + acquisitionWidthPixels;

        return localX >= minX && localX <= maxX &&
               localY >= 0 && localY < hostHeightPixels;
    }

    private void SetInputTransparent(bool transparent)
    {
        if (transparentInput == transparent) return;
        var style = NativeMethods.GetWindowLongPtr(hwnd, -20).ToInt64();
        NativeMethods.SetWindowLongPtr(hwnd, -20, (nint)(transparent ? style | 0x20 : style & ~0x20L));
        transparentInput = transparent;
    }

    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
}
