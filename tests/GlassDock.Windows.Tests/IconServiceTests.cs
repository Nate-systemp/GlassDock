using System.Runtime.InteropServices;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class IconServiceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void FromShell_extracts_high_resolution_icon_for_system_executable()
    {
        var service = new WindowsApplicationIconService();
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        Assert.True(File.Exists(cmdPath));

        var icon = service.FromShell("test:cmd", cmdPath);

        Assert.NotNull(icon);
        Assert.True(icon.Width >= 48, $"Expected icon width >= 48, but was {icon.Width}");
        Assert.True(icon.Height >= 48, $"Expected icon height >= 48, but was {icon.Height}");
        Assert.Equal(icon.Width * icon.Height * 4, icon.Pixels.Length);
        Assert.Contains(icon.Pixels, b => b != 0);
    }

    [Fact]
    public void FromShell_caches_extracted_icon()
    {
        var service = new WindowsApplicationIconService();
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var first = service.FromShell("test:cached_cmd", cmdPath);
        var second = service.FromShell("test:cached_cmd", cmdPath);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void FromShell_returns_null_for_nonexistent_target_safely()
    {
        var service = new WindowsApplicationIconService();
        var icon = service.FromShell("test:none", "C:\\nonexistent_app_xyz_12345.exe");
        Assert.Null(icon);
    }

    [Fact]
    public void Retain_removes_unreferenced_icons_from_cache()
    {
        var service = new WindowsApplicationIconService();
        var cmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var first = service.FromShell("test:evict_cmd", cmdPath);
        Assert.NotNull(first);

        service.Retain(["other:key"]);

        // Since test:evict_cmd was evicted, a new icon should be extracted on next request
        var second = service.FromShell("test:evict_cmd", cmdPath);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Test_Icon_Resolution_Details()
    {
        var service = new WindowsApplicationIconService();
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var icon = service.FromShell("test:explorer", explorerPath);
        Assert.NotNull(icon);
        Assert.Equal(96, icon.Width);
        Assert.Equal(96, icon.Height);
    }

    [Fact]
    public void Test_ReadPinned_Succeeds_With_HighRes_Icons()
    {
        var service = new WindowsApplicationIconService();
        var pins = ShellApplicationMetadata.ReadPinned(service);
        Assert.NotEmpty(pins);
        output.WriteLine($"ReadPinned returned {pins.Count} items:");
        foreach (var pin in pins)
        {
            output.WriteLine($"  PIN: {pin.Name} | Target: {pin.LaunchTarget} | Icon: {pin.Icon?.Width}x{pin.Icon?.Height}");
            Assert.NotNull(pin.Icon);
            Assert.Equal(96, pin.Icon.Width);
            Assert.Equal(96, pin.Icon.Height);
        }
    }
}
