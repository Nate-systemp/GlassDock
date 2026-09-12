using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas.Effects;
using Windows.Graphics.Effects;
using Windows.UI;

namespace GlassDock.App.Rendering;

/// <summary>One material graph shared by WinUI in-app and Windows desktop compositor adapters.</summary>
internal static class GlassEffectGraph
{
    public static readonly string[] Properties =
        ["Blur.BlurAmount", "Color.Saturation", "Light.Exposure", "Tint.Color",
         "Material.Source1Amount", "Material.Source2Amount"];

    public static IGraphicsEffect Create(IGraphicsEffectSource source, IGraphicsEffectSource? blurredBaseSource = null)
    {
        var blurred = new GaussianBlurEffect
        {
            Name = "Blur", BlurAmount = 28, BorderMode = EffectBorderMode.Hard, Source = source
        };
        return new ArithmeticCompositeEffect
        {
            // Desktop translucency must not blend sharp desktop pixels back over the blur.
            Name = "Material", Source1 = blurredBaseSource is not null
                ? new GaussianBlurEffect { Name = "BaseBlur", BlurAmount = 28, BorderMode = EffectBorderMode.Hard, Source = blurredBaseSource }
                : source,
            Source2 = new CompositeEffect
            {
                Mode = Microsoft.Graphics.Canvas.CanvasComposite.SourceOver,
                Sources =
                {
                    new ExposureEffect
                    {
                        Name = "Light",
                        Source = new SaturationEffect
                        {
                            Name = "Color",
                            Source = blurred
                        }
                    },
                    new ColorSourceEffect { Name = "Tint", Color = Color.FromArgb(26, 220, 234, 255) }
                }
            },
            MultiplyAmount = 0, Offset = 0, Source1Amount = .12f, Source2Amount = .88f
        };
    }

    public static IEnumerable<(string Name, float Value)> Scalars(GlassMaterial material)
    {
        var m = RefractionLayer.ForNativeBackend(material);
        yield return ("Blur.BlurAmount", (float)m.BlurAmount);
        yield return ("Color.Saturation", (float)m.Saturation);
        yield return ("Light.Exposure", (float)Math.Log2(m.Brightness));
        yield return ("Material.Source1Amount", (float)(1 - m.Opacity));
        yield return ("Material.Source2Amount", (float)m.Opacity);
    }

    public static Color Tint(GlassMaterial material) => Color.FromArgb(26,
        (byte)(material.Tint >> 16), (byte)(material.Tint >> 8), (byte)material.Tint);
}
