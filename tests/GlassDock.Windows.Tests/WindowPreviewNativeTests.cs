using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WindowPreviewNativeTests
{
    [Fact]
    public void MinimizedMaximizedMirrorKeepsLastDisplayedBoundsWithoutRestoringSource()
    {
        var source = CreateWindowExW(0x08000080, "STATIC", "Maximized preview source", 0x80CF0000,
            120, 100, 320, 240, 0, 0, 0, 0);
        var preview = CreateWindowExW(0x08000088, "STATIC", "Maximized preview host", 0x80000000,
            500, 500, 320, 240, 0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, source); Assert.NotEqual(0, preview);
            ShowWindow(source, 3); ShowWindow(preview, 4);
            var model = Model(source);
            using var cache = new WindowFrameCache();
            cache.Track([model]);
            Assert.True(DwmGetWindowAttribute(source, 9, out var expected, 16) >= 0);
            ShowWindow(source, 7);
            cache.Track([model with { IsMinimized = true }]);
            using var focus = new DesktopWindowFocus(preview, cache);
            Assert.True(focus.Show(model with { IsMinimized = true }));
            var mirror = (nint)typeof(DesktopWindowFocus).GetField("mirror", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(focus)!;
            Assert.True(GetWindowRect(mirror, out var actual));
            Assert.Equal(expected, actual);
            Assert.True(IsIconic(source));
        }
        finally
        {
            if (source != 0) DestroyWindow(source);
            if (preview != 0) DestroyWindow(preview);
        }
    }

    [Fact]
    public void DwmRelationshipReleasesAfterSourceClosesAndHandlesInvalidSources()
    {
        var source = CreateWindowExW(0x08000080, "STATIC", "Preview source", 0x80000000,
            100, 100, 320, 240, 0, 0, 0, 0);
        var target = CreateWindowExW(0x08000080, "STATIC", "Preview destination", 0x80000000,
            500, 100, 320, 240, 0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, source); Assert.NotEqual(0, target);
            ShowWindow(source, 4); ShowWindow(target, 4);
            using var thumbnail = new WindowThumbnail(target, Model(source));
            Assert.True(thumbnail.Update(new(8, 8, 240, 160), 1, 1, true));
            Assert.False(thumbnail.Update(new(8, 8, 240, 160), 0, 1, true));
            DestroyWindow(source); source = 0;
            Assert.False(thumbnail.Update(new(8, 8, 240, 160), 1, 1, true));
            Assert.Equal((nint)0, typeof(WindowThumbnail).GetField("thumbnail", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(thumbnail));
            thumbnail.Dispose(); thumbnail.Dispose();
            using var invalid = new WindowThumbnail(target, Model(0));
            Assert.False(invalid.Update(new(0, 0, 240, 160), 1, 1, true));
        }
        finally
        {
            if (source != 0) DestroyWindow(source);
            if (target != 0) DestroyWindow(target);
        }
    }

    [Fact]
    public void ZeroSizeWindowsAreExcludedButMinimizedWindowsAndCacheReadsAreSafe()
    {
        var source = CreateWindowExW(0x08000080, "STATIC", "Minimized preview source", 0x80000000,
            100, 100, 0, 0, 0, 0, 0, 0);
        try
        {
            Assert.NotEqual(0, source);
            ShowWindow(source, 4);
            var bounds = typeof(WindowsApplicationService).GetMethod("HasPreviewBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.False((bool)bounds.Invoke(null, [source])!);
            ShowWindow(source, 7);
            Assert.True((bool)bounds.Invoke(null, [source])!);
            var model = Model(source) with { IsMinimized = true };
            using var cache = new WindowFrameCache();
            cache.Track([model]);
            Assert.Null(cache.GetCachedFrame(model));
            Assert.Equal(0, cache.SessionsStarted);
            Assert.True(IsIconic(source));
            cache.Dispose();
            Assert.Null(cache.GetCachedFrame(model));
        }
        finally { if (source != 0) DestroyWindow(source); }
    }

    private static ApplicationWindow Model(nint window) => new(new(null, Environment.ProcessPath),
        "Test", window, Environment.ProcessId,
        System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks, false, null);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(
        uint ex, string cls, string name, uint style, int x, int y, int width, int height,
        nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint window, uint attribute, out Rect rect, int size);
}
