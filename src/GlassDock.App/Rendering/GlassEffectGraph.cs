using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;
using Windows.UI;

namespace GlassDock.App.Rendering;

internal static class GlassEffectGraph
{
    public static readonly string[] Properties =
        ["Blur.BlurAmount", "Color.Saturation", "Light.Exposure", "Contrast.Contrast", "Tint.Color",
         "Material.Source1Amount", "Material.Source2Amount"];

    public static IGraphicsEffect Create(IGraphicsEffectSource source, IGraphicsEffectSource? blurredBaseSource = null,
        int quality = 1, GlassMaterial? value = null, double? baseBlur = null)
    {
        var material = value ?? new GlassMaterial();
        var blurred = new GaussianBlurEffect
        {
            Name = "Blur", BlurAmount = (float)material.BlurAmount, BorderMode = EffectBorderMode.Hard, Source = source, Optimization = Quality(quality)
        };
        return new ArithmeticCompositeEffect
        {
            Name = "Material", Source1 = blurredBaseSource is not null
                ? new GaussianBlurEffect { Name = "BaseBlur", BlurAmount = (float)(baseBlur ?? material.BlurAmount), BorderMode = EffectBorderMode.Hard, Source = blurredBaseSource, Optimization = Quality(quality) }
                : source,
            Source2 = new CompositeEffect
            {
                Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver,
                Sources =
                {
                    new ContrastEffect
                    {
                        Name = "Contrast", Contrast = (float)(material.Contrast - 1),
                        Source = new ExposureEffect
                        {
                            Name = "Light", Exposure = (float)Math.Log2(material.Brightness),
                            Source = new SaturationEffect { Name = "Color", Saturation = (float)material.Saturation, Source = blurred }
                        }
                    },
                    new ColorSourceEffect { Name = "Tint", Color = Tint(material) }
                }
            },
            MultiplyAmount = 0, Offset = 0, Source1Amount = (float)(1 - material.Opacity), Source2Amount = (float)material.Opacity
        };
    }

    public static IEnumerable<(string Name, float Value)> Scalars(GlassMaterial material)
    {
        var m = RefractionLayer.ForNativeBackend(material);
        yield return ("Blur.BlurAmount", (float)m.BlurAmount);
        yield return ("Color.Saturation", (float)m.Saturation);
        yield return ("Light.Exposure", (float)Math.Log2(m.Brightness));
        yield return ("Contrast.Contrast", (float)(m.Contrast - 1));
        yield return ("Material.Source1Amount", (float)(1 - m.Opacity));
        yield return ("Material.Source2Amount", (float)m.Opacity);
    }

    internal static EffectOptimization Quality(int value) => value switch
    {
        0 => EffectOptimization.Speed,
        2 => EffectOptimization.Quality,
        _ => EffectOptimization.Balanced
    };

    public static Color Tint(GlassMaterial material) => Color.FromArgb(26,
        (byte)(material.Tint >> 16), (byte)(material.Tint >> 8), (byte)material.Tint);
}
