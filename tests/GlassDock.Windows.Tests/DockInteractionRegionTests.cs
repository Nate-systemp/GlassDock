using System.Runtime.InteropServices;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

[Collection(NativeDesktopTestCollection.Name)]
public sealed class DockInteractionRegionTests
{
    [Fact]
    public void Topmost_recheck_repairs_demoted_dock_without_activation()
    {
        var foreground = GetForegroundWindow();
        Assert.NotEqual(0, foreground);
        var window = CreateWindowExW(0x08000080, "STATIC", "Dock stacking test", 0x80000000,
            -960, -144, 960, 144, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            using var manager = new WindowsOverlayManager(window);
            manager.Configure();
            Assert.True(SetWindowPos(window, -2, 0, 0, 0, 0, 0x0013));
            Assert.Equal(0, GetWindowLongPtrW(window, -20).ToInt64() & 8);
            SendMessageW(window, 0x113, 0x4745, 0);
            Assert.NotEqual(0, GetWindowLongPtrW(window, -20).ToInt64() & 8);
            Assert.Equal(foreground, GetForegroundWindow());
        }
        finally { DestroyWindow(window); }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [Fact]
    public void Region_excludes_transparent_host_and_updates_without_recreating_window()
    {
        var window = CreateWindowExW(0x08000080, "STATIC", "Dock region test", 0x80000000,
            -640, -144, 640, 144, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            using var manager = new WindowsOverlayManager(window);
            manager.Configure();
            var scale = manager.Scale;
            void Hit(double x, double y, bool inside)
            {
                var sx = -640 + (int)Math.Round(x * scale);
                var sy = -144 + (int)Math.Round(y * scale);
                var packed = (nint)((ushort)sx | ((long)(ushort)sy << 16));
                var transparent = GetWindowLongPtrW(window, -20).ToInt64() & 0x20;
                var result = SendMessageW(window, 0x84, 0, packed);
                Assert.Equal(!inside, result == -1);
                Assert.Equal(transparent, GetWindowLongPtrW(window, -20).ToInt64() & 0x20);
            }
            for (var i = 0; i < 100; i++)
            {
                // A wave rising above the base, with transparent space beside it.
                manager.SetInteractionPolygon([(100, 70), (270, 70), (320, 58),
                    (370, 70), (540, 70), (540, 120), (100, 120)]);
                Assert.Equal(0, GetWindowRgn(window, region)); // No visual clipping region.
                Hit(320, 65, true);
                Hit(320, 100, true);
                Hit(150, 65, false);
                Hit(320, 20, false);
                Hit(50, 100, false);
                Hit(320, 135, false);

                manager.SetInteractionRegion(false);
                Assert.Equal(0, GetWindowRgn(window, region)); // Input-only; preserve rendered shadow.
                Hit(320, 120, true);
                Hit(150, 100, false);

                // The hit region follows each animated position, without a
                // permanent corridor above or beside the pill.
                manager.SetPeekInteraction(24);
                Assert.Equal(0, GetWindowRgn(window, region));
                Hit(320, 125, true);
                Hit(320, 120, true);
                Hit(320, 105, false);
                Hit(240, 120, false);
                Hit(259, 120, false);
                Hit(381, 120, false);
                Hit(320, 130, false);

                // Once the pill is settled, only the pill-width region and a
                // small downward click extension remain interactive.
                manager.SetPeekInteraction(-2);
                Assert.Equal(0, GetWindowRgn(window, region)); // no visual clipping
                Hit(320, 143, true);
                Hit(320, 139, false);
                Hit(240, 143, false);
            }
        }
        finally { DeleteObject(region); DestroyWindow(window); }
    }

    [Theory]
    [InlineData(640)]
    [InlineData(960)]
    public void Peek_keeps_reacquisition_timer_and_input_region_without_visual_clipping(int hostWidth)
    {
        Assert.True(GetCursorPos(out var cursor));
        var window = CreateWindowExW(0x08000080, "STATIC", "Peek reacquisition test", 0x80000000,
            cursor.X - hostWidth / 2, cursor.Y - 120, hostWidth, 144, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        try
        {
            using var manager = new WindowsOverlayManager(window);
            manager.Configure();
            // Position using the target HWND's DPI; no mouse movement or activation.
            var scale = manager.Scale;
            SetWindowPos(window, 0, cursor.X - (int)(hostWidth / 2 * scale), cursor.Y - (int)(120 * scale),
                (int)(hostWidth * scale), (int)(144 * scale), 0x0014);
            manager.SetPeekInteraction(-2);
            Assert.False(manager.IsPointerInsideInput());
            Assert.NotEqual(0, GetWindowLongPtrW(window, -20).ToInt64() & 0x20);
            manager.SetPeekInteraction(24);
            SendMessageW(window, 0x113, 0x4744, 0);
            Assert.True(manager.IsPointerInsideInput());
            Assert.Equal(0, GetWindowLongPtrW(window, -20).ToInt64() & 0x20);
            // An OLE/window-discovery probe outside the dock must not make the
            // HWND transparent while the real cursor is inside the drop target.
            var outside = (nint)((ushort)(cursor.X - hostWidth) | ((long)(ushort)cursor.Y << 16));
            Assert.Equal(-1, SendMessageW(window, 0x84, 0, outside));
            Assert.Equal(0, GetWindowLongPtrW(window, -20).ToInt64() & 0x20);
            manager.SetPeekInteraction(-2);
            SendMessageW(window, 0x113, 0x4744, 0);
            Assert.False(manager.IsPointerInsideInput());
        }
        finally { DestroyWindow(window); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint window, int index);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(nint window, nint region);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(nint region, int x, int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
}
