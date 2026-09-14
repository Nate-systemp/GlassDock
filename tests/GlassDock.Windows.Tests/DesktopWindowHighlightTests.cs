using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Tests;

public sealed class DesktopWindowHighlightTests
{
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
                ShowWindow(second, 7); focus.Show(Model(second)); Assert.False(IsWindowVisible(dim));
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
    public async Task Overlay_is_hollow_nonactivating_and_leaves_targets_unchanged()
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
                using var highlight = new DesktopWindowHighlight();
                highlight.Show(first);
                var overlay = (nint)typeof(DesktopWindowHighlight).GetField("overlay", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(highlight)!;
                Assert.NotEqual(0, overlay);
                Assert.True(IsWindowVisible(overlay));
                Assert.Equal(0x080800A0L, GetWindowLongPtrW(overlay, -20).ToInt64() & 0x080800A0L);
                Assert.NotEqual(0, GetWindowLongPtrW(overlay, -20).ToInt64() & 8); // Only overlay is topmost.
                var region = CreateRectRgn(0, 0, 0, 0);
                try
                {
                    Assert.NotEqual(0, GetWindowRgn(overlay, region));
                    GetWindowRect(overlay, out var rectangle);
                    Assert.False(PtInRegion(region, (rectangle.Right - rectangle.Left) / 2, (rectangle.Bottom - rectangle.Top) / 2));
                    Assert.True(PtInRegion(region, 1, (rectangle.Bottom - rectangle.Top) / 2));
                }
                finally { DeleteObject(region); }
                highlight.Show(second);
                Assert.Equal(foreground, GetForegroundWindow());
                Assert.Equal(order, TargetOrder(first, second));
                GetWindowRect(first, out var afterFirst); GetWindowRect(second, out var afterSecond);
                Assert.Equal(originalFirst, afterFirst); Assert.Equal(originalSecond, afterSecond);
                highlight.Hide(); Assert.False(IsWindowVisible(overlay));
                ShowWindow(second, 7); // Minimize test window without activating it.
                highlight.Show(second); Assert.False(IsWindowVisible(overlay));
                highlight.Show(first); Assert.True(IsWindowVisible(overlay));
                DestroyWindow(first); first = 0;
                SendMessageW(overlay, 0x0113, 1, 0); // Deliver the overlay's tracking timer.
                Assert.False(IsWindowVisible(overlay));
                highlight.Dispose(); Assert.False(IsWindow(overlay));
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

    private static nint[] TargetOrder(nint first, nint second)
    {
        var order = new List<nint>();
        EnumWindows((window, _) => { if (window == first || window == second) order.Add(window); return true; }, 0);
        return order.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
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
