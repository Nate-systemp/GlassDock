using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class LiquidGlassPerformanceWiringTests
{
    [Fact]
    public void Utility_effects_are_created_only_for_the_active_appearance_branch()
    {
        var source = Read("src/GlassDock.App/Rendering/UtilityGlassSurface.cs");
        var draw = source[source.IndexOf("public void Draw(", StringComparison.Ordinal)..source.IndexOf("public void Hide()", StringComparison.Ordinal)];
        var clearBranch = draw.IndexOf("if (mode == DockAppearanceMode.Clear)", StringComparison.Ordinal);
        var shaderCreation = draw.IndexOf("shader ??= new PixelShaderEffect", StringComparison.Ordinal);
        Assert.True(clearBranch >= 0 && shaderCreation > clearBranch);
        Assert.True(draw.IndexOf("blur ??= new()", StringComparison.Ordinal) > clearBranch);

        var configure = source[source.IndexOf("public void Configure(", StringComparison.Ordinal)..source.IndexOf("public void Draw(", StringComparison.Ordinal)];
        Assert.Contains("if (this.mode != mode) ReleaseEffects();", configure);
        var release = source[source.IndexOf("private void ReleaseEffects()", StringComparison.Ordinal)..source.IndexOf("public void Dispose()", StringComparison.Ordinal)];
        Assert.Contains("materialEffect?.Dispose()", release);
        Assert.Contains("shader?.Dispose()", release);
        Assert.Contains("blur?.Dispose()", release);
        Assert.Contains("previous = null", release);
        Assert.Contains("if (shader is not null) shader.Source1 = null;", draw);
    }

    private static string Read(string relative)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, relative));
    }
    [Fact]
    public void Drag_lens_traverses_the_shared_contrast_stage_before_exposure()
    {
        var source = Read("src/GlassDock.App/Rendering/DokyLiquidGlassSurface.cs");
        Assert.Contains("lensContrast = (ContrastEffect)lensComposite.Sources[0]", source);
        Assert.Contains("lensExposure = (ExposureEffect)lensContrast.Source", source);
        Assert.DoesNotContain("lensExposure = (ExposureEffect)lensComposite.Sources[0]", source);
        var drag = Read("src/GlassDock.App/Desktop/DesktopOverlayWindow.DragLens.cs");
        Assert.DoesNotContain("target.X -=", drag);
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
