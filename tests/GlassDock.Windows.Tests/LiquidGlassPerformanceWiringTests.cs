using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class LiquidGlassPerformanceWiringTests
{
    private static string Read(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, relative));
    }
    [Fact]
    public void Pooled_surfaces_are_released_before_capture_pool_shutdown()
    {
        var source = Read("src/GlassDock.App/Rendering/DokyLiquidGlassSurface.cs");
        var stop = source[source.IndexOf("private void Stop()", StringComparison.Ordinal)..];
        Assert.True(stop.IndexOf("bitmap?.Dispose()", StringComparison.Ordinal) < stop.IndexOf("capture?.Dispose()", StringComparison.Ordinal));
        Assert.True(stop.IndexOf("currentFrame?.Dispose()", StringComparison.Ordinal) < stop.IndexOf("capture?.Dispose()", StringComparison.Ordinal));
        Assert.Contains("shaderState = null", stop);
        Assert.Contains("lensShaderState = null", source);
    }
    [Fact]
    public void Frame_drain_is_bounded_and_disposes_replaced_frames()
    {
        var source = Read("src/GlassDock.Windows/Desktop/DesktopCaptureSource.cs");
        var take = source[source.IndexOf("public Direct3D11CaptureFrame? TakeFrame()", StringComparison.Ordinal)..source.IndexOf("public void Dispose()", StringComparison.Ordinal)];
        Assert.DoesNotContain("while", take);
        Assert.Contains("latest.Dispose(); latest = newer", take);
        Assert.Contains("catch { latest.Dispose(); throw; }", take);
    }
}
