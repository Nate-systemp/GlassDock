using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        using var device = new CanvasDevice();
        using var scene = new CanvasRenderTarget(device, 440, 180, 96);
        using (var draw = scene.CreateDrawingSession())
        {
            draw.Clear(Color.FromArgb(255, 35, 50, 70));
            for (var y = 0; y < 180; y += 12)
                for (var x = 0; x < 440; x += 12)
                    draw.FillRectangle(x, y, 12, 12, Color.FromArgb(255,
                        (byte)((x * 5 + y) % 256), (byte)((x + y * 3) % 256), (byte)((x * 2 + y * 5) % 256)));
        }
        using var shader = new PixelShaderEffect(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "LiquidGlass.bin")));
        using var blur = new GaussianBlurEffect { Source = scene, BorderMode = EffectBorderMode.Hard };
        using var rim = new LiquidGlassRim();
        LiquidGlassShaderState? state = null;
        var bounds = new Vector4(40, 40, 360, 100);
        byte[] Render(LiquidGlassMaterial m)
        {
            using var target = new CanvasRenderTarget(device, 440, 180, 96);
            using var geometry = CanvasGeometry.CreateRoundedRectangle(device, 40, 40, 360, 100, m.CornerRadius, m.CornerRadius);
            blur.BlurAmount = m.BlurAmount;
            blur.Optimization = GlassEffectGraph.Quality((int)m.BlurQuality);
            shader.Source1 = m.BlurAmount > 0 ? blur : scene;
            LiquidGlassShader.Configure(shader, m, 1, bounds, new(220, 180, 0, m.CornerRadius),
                new(440, 180), Vector4.Zero, ref state);
            using (var draw = target.CreateDrawingSession())
            {
                draw.Clear(Color.FromArgb(0, 0, 0, 0));
                using (draw.CreateLayer(1, geometry))
                {
                    draw.DrawImage(shader);
                    rim.Draw(draw, geometry, bounds, 1, m, dockSurface: true);
                }
            }
            return target.GetPixelBytes();
        }
        var checks = 0;
        byte[] RenderLens(float refraction, float opacity)
        {
            using var target = new CanvasRenderTarget(device, 440, 180, 96);
            shader.Source1 = scene;
            LiquidGlassShader.Configure(shader, new LiquidGlassMaterial { RefractionStrength = refraction },
                1, new(150, 40, 100, 100), new(200, 50, 0, 50), new(440, 180), new(.07f, 0, 1, opacity), ref state);
            using (var draw = target.CreateDrawingSession())
            {
                draw.Clear(Color.FromArgb(0, 0, 0, 0));
                draw.DrawImage(shader);
            }
            return target.GetPixelBytes();
        }
        var lens = RenderLens(11, 1);
        if (lens[3] != 0 || lens[(90 * 440 + 200) * 4 + 3] == 0)
            throw new InvalidOperationException("Drag lens capsule clipping failed.");
        if (lens.SequenceEqual(RenderLens(0, 1)))
            throw new InvalidOperationException("Drag lens refraction did not change pixels.");
        if (RenderLens(11, 0).Any(b => b != 0))
            throw new InvalidOperationException("Dismissed drag lens is not transparent.");
        checks += 3;
        foreach (var control in GlassVisualControl.All)
        {
            var baseline = GlassVisualProfile.BuiltIn(DockAppearanceMode.Clear).Material;
            if (control.Id == "ReflectionSoftness") baseline = baseline with { ReflectionIntensity = .2f };
            if (control.Id == "EdgeWidth") baseline = baseline with { EdgeOpacity = .6f };
            if (control.Id == "BlurQuality") baseline = baseline with { BlurAmount = 40, BlurQuality = 0 };
            var changed = control.Write(baseline, control.Read(baseline) == control.Maximum ? (float)control.Minimum : (float)control.Maximum);
            var before = Render(baseline);
            var after = Render(changed);
            var delta = before.Zip(after, (a, b) => Math.Abs(a - b)).Sum();
            if (delta == 0) throw new InvalidOperationException(control.Id + " did not change rendered pixels.");
            Console.WriteLine($"{control.Id}: pixel delta {delta}");
            checks++;
        }
        foreach (var mode in new[] { GlassMaterialMode.Frosted, GlassMaterialMode.Acrylic, GlassMaterialMode.Clear })
        {
            var m = DockMaterialStylePresets.Create(mode) with { Contrast = 1.4 };
            using var effect = (ArithmeticCompositeEffect)GlassEffectGraph.Create(scene, scene, m.BlurQuality, m, DockMaterialRendering.BaseBlur(mode, m.BlurAmount));
            // Match the retained drag-lens traversal, which must include contrast.
            var composite = (CompositeEffect)effect.Source2;
            var contrast = (ContrastEffect)composite.Sources[0];
            var exposure = (ExposureEffect)contrast.Source;
            var saturation = (SaturationEffect)exposure.Source;
            var nativeBlur = (GaussianBlurEffect)saturation.Source;
            nativeBlur.Optimization = GlassEffectGraph.Quality(m.BlurQuality);
            using var target = new CanvasRenderTarget(device, 440, 180, 96);
            using (var draw = target.CreateDrawingSession()) draw.DrawImage(effect);
            if (target.GetPixelBytes().All(b => b == 0)) throw new InvalidOperationException(mode + " native graph is empty.");
            checks++;
        }
        Console.WriteLine($"PASS: {checks} GPU checks. No windows, capture sessions, or user settings touched.");
        return 0;
    }
}
