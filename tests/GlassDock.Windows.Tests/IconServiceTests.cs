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
        Assert.True(icon.Width >= 128, $"Expected icon width >= 128, but was {icon.Width}");
        Assert.True(icon.Height >= 128, $"Expected icon height >= 128, but was {icon.Height}");
        Assert.Equal(256, icon.Width);
        Assert.Equal(256, icon.Height);
    }

    [Fact]
    public void Test_Pinned_And_Running_Apps_Have_HighRes_And_Alpha_Preserved()
    {
        var service = new WindowsApplicationIconService();
        var pins = ShellApplicationMetadata.ReadPinned(service);
        Assert.NotEmpty(pins);
        output.WriteLine($"=== PINNED APPLICATIONS ({pins.Count}) ===");
        foreach (var pin in pins)
        {
            var icon = pin.Icon;
            Assert.NotNull(icon);
            Assert.True(icon.Width >= 128, $"Expected pin {pin.Name} width >= 128, but was {icon.Width}");
            Assert.True(icon.Height >= 128, $"Expected pin {pin.Name} height >= 128, but was {icon.Height}");

            var zeroAlpha = 0;
            var partialAlpha = 0;
            var fullAlpha = 0;
            for (var i = 3; i < icon.Pixels.Length; i += 4)
            {
                var a = icon.Pixels[i];
                if (a == 0) zeroAlpha++;
                else if (a == 255) fullAlpha++;
                else partialAlpha++;
            }
            output.WriteLine($"  [PIN] {pin.Name} | Size: {icon.Width}x{icon.Height} | 0-a={zeroAlpha}, part-a={partialAlpha}, 255-a={fullAlpha}");
            Assert.True(zeroAlpha > 0, $"Expected {pin.Name} to have transparent pixels, but zeroAlpha was 0");
            Assert.True(fullAlpha > 0, $"Expected {pin.Name} to have opaque pixels, but fullAlpha was 0");
        }

        // Test Visual Studio high-res extraction
        var vsPath = @"C:\Program Files\Microsoft Visual Studio\18\Insiders\Common7\IDE\devenv.exe";
        if (File.Exists(vsPath))
        {
            var vsIcon = service.FromShell("test:vs", vsPath);
            Assert.NotNull(vsIcon);
            Assert.True(vsIcon.Width >= 128);
            Assert.True(vsIcon.Height >= 128);
            output.WriteLine($"  [VS] {vsIcon.Width}x{vsIcon.Height} extracted successfully");
        }

        // Test Edge high-res extraction
        var edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (File.Exists(edgePath))
        {
            var edgeIcon = service.FromShell("test:edge", edgePath);
            Assert.NotNull(edgeIcon);
            Assert.True(edgeIcon.Width >= 128);
            Assert.True(edgeIcon.Height >= 128);
            output.WriteLine($"  [Edge] {edgeIcon.Width}x{edgeIcon.Height} extracted successfully");
        }
    }
}
