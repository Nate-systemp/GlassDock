using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;

namespace GlassDock.Windows.Tests;

public sealed class DesktopWindowHighlightTests
{
    [Fact]
    public async Task Capture_frequency_measurement()
    {
        var completion = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint source = 0;
            try
            {
                source = CreateWindowExW(0x08000080, "STATIC", "Capture frequency test", 0x80000006,
                    120, 120, 320, 240, 0, 0, 0, 0);
                ShowWindow(source, 4); UpdateWindow(source);
                var model = new ApplicationWindow(new(null, Environment.ProcessPath), "Test", source,
                    Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, true, null);
                using var cache = new WindowFrameCache();
                var until = Environment.TickCount64 + 18000;
                while (Environment.TickCount64 < until)
                {
                    cache.Track([model]);
                    while (PeekMessageW(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); }
                    Thread.Sleep(50);
                }
                completion.SetResult(cache.SessionsStarted);
            }
            catch (Exception error) { completion.SetException(error); }
            finally { if (source != 0) DestroyWindow(source); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        var count = await completion.Task.WaitAsync(TimeSpan.FromSeconds(25));
        Console.WriteLine($"WGC sessions in 18 seconds for one continuously visible active source: {count}");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Minimized_mirror_matches_restored_bounds_on_each_connected_monitor()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var previousDpi = SetThreadDpiAwarenessContext(-4);
            nint preview = 0, source = 0;
            try
            {
                var monitors = new List<MonitorInfo>();
                MonitorProc enumerate = (nint monitor, nint dc, ref Rect rect, nint data) =>
                {
                    var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfoW(monitor, ref info)) monitors.Add(info);
                    return true;
                };
                Assert.True(EnumDisplayMonitors(0, 0, enumerate, 0));
                Assert.NotEmpty(monitors);
                preview = CreateWindowExW(0x08000088, "STATIC", "Monitor preview test", 0x80000006,
                    monitors[0].Work.Left + 20, monitors[0].Work.Top + 20, 200, 80, 0, 0, 0, 0);
                ShowWindow(preview, 4);
                var ticks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
                using var cache = new WindowFrameCache();
                using var focus = new DesktopWindowFocus(preview, cache);
                foreach (var monitor in monitors)
                {
                    // Normal top-level window: exercise workspace rather than TOOLWINDOW screen coordinates.
                    source = CreateWindowExW(0x08000000, "STATIC", "Monitor placement test", 0x80000006,
                        monitor.Work.Left + 120, monitor.Work.Top + 120, 320, 240, 0, 0, 0, 0);
                    Assert.NotEqual(0, source);
                    ShowWindow(source, 4);
                    Assert.True(GetWindowRect(source, out var restored));
                    ShowWindow(source, 7);
                    var foreground = GetForegroundWindow();
                    var model = new ApplicationWindow(new(null, Environment.ProcessPath), "Monitor test", source,
                        Environment.ProcessId, ticks, false, null);
                    Assert.True(focus.Show(model));
                    var mirror = (nint)typeof(DesktopWindowFocus).GetField("mirror", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(focus)!;
                    Assert.True(GetWindowRect(mirror, out var actual));
                    Assert.Equal(restored, actual);
                    Assert.True(IsIconic(source));
                    Assert.Equal(foreground, GetForegroundWindow());
                    focus.Hide(); DestroyWindow(source); source = 0;
                }
                GC.KeepAlive(enumerate);
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                if (source != 0) DestroyWindow(source);
                if (preview != 0) DestroyWindow(preview);
                if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 100, 120, 800, 600, false, 100, 120)]
    [InlineData(1920, 0, 1920, 0, 2100, 120, 800, 600, false, 2100, 120)]
    [InlineData(-1920, 0, -1920, 0, -1800, 120, 800, 600, false, -1800, 120)]
    [InlineData(0, -1440, 0, -1440, 100, -1300, 1200, 900, false, 100, -1300)]
    [InlineData(-2560, -1440, -2512, -1400, -2400, -1300, 1200, 900, false, -2352, -1260)]
    [InlineData(1920, 0, 1980, 48, 2200, 100, 1600, 1200, false, 2260, 148)]
    [InlineData(-1920, 0, -1872, 40, -1800, 120, 800, 600, true, -1800, 120)]
    public void Restored_coordinates_add_only_work_area_insets_and_preserve_physical_size(
        int monitorX, int monitorY, int workX, int workY, int x, int y, int width, int height,
        bool toolWindow, int expectedX, int expectedY)
    {
        var convert = typeof(DesktopWindowFocus).GetMethod("WorkspaceToScreen", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (PreviewRect)convert.Invoke(null,
            [new PreviewRect(x, y, width, height), new PreviewRect(monitorX, monitorY, 2560, 1440),
             new PreviewRect(workX, workY, 2500, 1400), toolWindow])!;
        Assert.Equal(new PreviewRect(expectedX, expectedY, width, height), result);
    }

    [Fact]
    public async Task Last_visible_capture_survives_minimize_and_is_used_without_restoring_source()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint source = 0, preview = 0;
            var earlierWindows = new List<nint>();
            try
            {
                source = CreateWindowExW(0x08000080, "STATIC", "Capture cache test", 0x80000006, 120, 120, 320, 240, 0, 0, 0, 0);
                preview = CreateWindowExW(0x08000088, "STATIC", "Capture cache preview", 0x80000006, 500, 700, 200, 80, 0, 0, 0, 0);
                ShowWindow(source, 4); ShowWindow(preview, 4); UpdateWindow(source);
                var ticks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
                var model = new ApplicationWindow(new(null, Environment.ProcessPath), "Test", source, Environment.ProcessId, ticks, false, null);
                using var cache = new WindowFrameCache();
                // Regress the former eight-session cutoff: the requested source is ninth.
                for (var i = 0; i < 8; i++)
                {
                    var other = CreateWindowExW(0x08000080, "STATIC", "Earlier capture test", 0x80000006,
                        500 + i * 10, 120, 320, 240, 0, 0, 0, 0);
                    Assert.NotEqual(0, other);
                    earlierWindows.Add(other); ShowWindow(other, 4); UpdateWindow(other);
                }
                cache.Track(earlierWindows.Select(handle => model with { Handle = handle }).Append(model));
                var get = typeof(WindowFrameCache).GetMethod("Get", BindingFlags.NonPublic | BindingFlags.Instance)!;
                object? Frame() => get.Invoke(cache, [model]);
                var deadline = Environment.TickCount64 + 8000;
                while (Frame() is null && Environment.TickCount64 < deadline)
                {
                    while (PeekMessageW(out var message, 0, 0, 0, 1)) { TranslateMessage(ref message); DispatchMessageW(ref message); }
                    Thread.Sleep(10);
                }
                var retained = Frame();
                Assert.NotNull(retained);
                var pixels = (byte[])retained.GetType().GetProperty("Pixels")!.GetValue(retained)!;
                Assert.Contains(pixels.Where((_, index) => index % 4 != 3), value => value > 32);
                ShowWindow(source, 7);
                var foreground = GetForegroundWindow();
                using var focus = new DesktopWindowFocus(preview, cache);
                Assert.True(focus.Show(model));
                Assert.True(IsIconic(source));
                Assert.Equal(foreground, GetForegroundWindow());
                Assert.Same(retained, typeof(DesktopWindowFocus).GetField("cachedFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(focus));
                var mirror = (nint)typeof(DesktopWindowFocus).GetField("mirror", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(focus)!;
                var dc = GetDC(mirror);
                try
                {
                    var pixel = GetPixel(dc, 160, 120);
                    Assert.NotEqual(0xFFFFFFFFu, pixel);
                    Assert.NotEqual(0u, pixel & 0xFFFFFF);
                }
                finally { ReleaseDC(mirror, dc); }
                focus.Hide();
                Assert.Same(retained, Frame());
                cache.Track([]);
                Assert.Null(Frame());
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                foreach (var other in earlierWindows) DestroyWindow(other);
                if (source != 0) DestroyWindow(source); if (preview != 0) DestroyWindow(preview);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task Desktop_focus_retargets_and_cleans_up_without_changing_real_windows()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint first = 0, second = 0, preview = 0;
            try
            {
                first = CreateWindowExW(0x08000080, "STATIC", "Peek test A", 0x80000006, 100, 100, 320, 240, 0, 0, 0, 0);
                second = CreateWindowExW(0x08000080, "STATIC", "Peek test B", 0x80000006, 480, 100, 320, 240, 0, 0, 0, 0);
                preview = CreateWindowExW(0x08000088, "STATIC", "Peek test preview", 0x80000006, 400, 700, 200, 80, 0, 0, 0, 0);
                Assert.NotEqual(0, first); Assert.NotEqual(0, second); Assert.NotEqual(0, preview);
                ShowWindow(first, 4); ShowWindow(second, 4); ShowWindow(preview, 4);
                var foreground = GetForegroundWindow();
                var order = TargetOrder(first, second);
                GetWindowRect(first, out var beforeA); GetWindowRect(second, out var beforeB);
                var ticks = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
                ApplicationWindow Model(nint handle) => new(new(null, Environment.ProcessPath), "Test", handle, Environment.ProcessId, ticks, false, null);
                using var focus = new DesktopWindowFocus(preview);
                nint Field(string name) => (nint)typeof(DesktopWindowFocus).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(focus)!;
                focus.Show(Model(first));
                var dim = Field("dim"); var mirror = Field("mirror");
                Assert.True(IsWindowVisible(dim)); Assert.True(IsWindowVisible(mirror));
                Assert.Equal(0x080800A0L, GetWindowLongPtrW(dim, -20).ToInt64() & 0x080800A0L);
                Assert.Equal(0x080800A0L, GetWindowLongPtrW(mirror, -20).ToInt64() & 0x080800A0L);
                focus.Show(Model(second));
                Assert.Equal(dim, Field("dim")); Assert.Equal(mirror, Field("mirror"));
                Assert.Equal(foreground, GetForegroundWindow()); Assert.Equal(order, TargetOrder(first, second));
                GetWindowRect(first, out var afterA); GetWindowRect(second, out var afterB);
                Assert.Equal(beforeA, afterA); Assert.Equal(beforeB, afterB);
                focus.Hide(); Assert.False(IsWindowVisible(dim)); Assert.False(IsWindowVisible(mirror));
                Assert.Null(typeof(DesktopWindowFocus).GetField("thumbnail", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(focus));
                ShowWindow(second, 7); focus.Show(Model(second)); Assert.True(IsIconic(second));
                focus.Show(Model(first)); DestroyWindow(first); first = 0;
                SendMessageW(dim, 0x113, 1, 0); Assert.False(IsWindowVisible(dim)); Assert.False(IsWindowVisible(mirror));
                focus.Dispose(); Assert.False(IsWindow(dim)); Assert.False(IsWindow(mirror));
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                if (first != 0) DestroyWindow(first);
                if (second != 0) DestroyWindow(second);
                if (preview != 0) DestroyWindow(preview);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Disabled_highlight_creates_no_window_and_leaves_targets_unchanged()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            nint first = 0, second = 0;
            try
            {
                // Disposable test windows only, shown without activation.
                first = CreateWindowExW(0x08000080, "STATIC", "GlassDock highlight test A", 0x80000006,
                    200, 200, 320, 240, 0, 0, 0, 0);
                second = CreateWindowExW(0x08000080, "STATIC", "GlassDock highlight test B", 0x80000006,
                    560, 200, 320, 240, 0, 0, 0, 0);
                Assert.NotEqual(0, first); Assert.NotEqual(0, second);
                ShowWindow(first, 4); ShowWindow(second, 4);
                var foreground = GetForegroundWindow();
                GetWindowRect(first, out var originalFirst);
                GetWindowRect(second, out var originalSecond);
                var order = TargetOrder(first, second);
                var threadWindows = CurrentThreadWindows();
                using var highlight = new DesktopWindowHighlight();
                highlight.Show(first);
                Assert.Equal(threadWindows, CurrentThreadWindows());
                highlight.Show(second);
                Assert.Equal(foreground, GetForegroundWindow());
                Assert.Equal(order, TargetOrder(first, second));
                GetWindowRect(first, out var afterFirst); GetWindowRect(second, out var afterSecond);
                Assert.Equal(originalFirst, afterFirst); Assert.Equal(originalSecond, afterSecond);
                highlight.Hide();
                ShowWindow(second, 7); // Minimize only the disposable test window.
                highlight.Show(second);
                Assert.True(IsIconic(second));
                highlight.Show(first);
                highlight.Show(0);
                highlight.Dispose();
                highlight.Dispose();
                highlight.Show(first);
                highlight.Hide();
                Assert.Equal(threadWindows, CurrentThreadWindows());
                GetWindowRect(first, out afterFirst);
                Assert.Equal(originalFirst, afterFirst);
                Assert.True(IsIconic(second));
                Assert.Equal(foreground, GetForegroundWindow());
                completion.SetResult();
            }
            catch (Exception error) { completion.SetException(error); }
            finally
            {
                if (first != 0) DestroyWindow(first);
                if (second != 0) DestroyWindow(second);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static nint[] CurrentThreadWindows()
    {
        var windows = new List<nint>();
        EnumThreadWindows(GetCurrentThreadId(), (window, _) => { windows.Add(window); return true; }, 0);
        return windows.Order().ToArray();
    }

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumProc callback, nint data);

    private static nint[] TargetOrder(nint first, nint second)
    {
        var order = new List<nint>();
        EnumWindows((window, _) => { if (window == first || window == second) order.Add(window); return true; }, 0);
        return order.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    private delegate bool MonitorProc(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint data);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [StructLayout(LayoutKind.Sequential)] private struct Message { public nint Window; public uint Id; public nuint W; public nint L; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, nint window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] private static extern nint DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);
    private delegate bool EnumProc(nint window, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint w, nint l);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
}
