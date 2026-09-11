using System.Runtime.InteropServices;
using GlassDock.Core.Materials;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Rendering;

/// <summary>Live in-app backdrop. Owns its native resources for one connected XAML brush.</summary>
internal sealed class GlassCompositionBrush : XamlCompositionBrushBase
{
    private CompositionEffectBrush? effect;
    private CompositionEffectFactory? factory;
    private CompositionBackdropBrush? backdrop;
    private GlassMaterial material = new();
    private readonly UISettings uiSettings = new();


    public string RenderingMode { get; private set; } = "Connecting native Composition";
    public event EventHandler? RenderingModeChanged;

    public GlassCompositionBrush() => FallbackColor = Color.FromArgb(255, 47, 57, 77);

    // GlassSurface supplies its compositor on Loaded; no process-global Window is needed.

    public void Connect(Compositor compositor)
    {
        if (CompositionBrush is not null) return;
        try
        {
            var source = new CompositionEffectSourceParameter("Backdrop");
            factory = compositor.CreateEffectFactory(GlassEffectGraph.Create(source), GlassEffectGraph.Properties);
            effect = factory.CreateBrush();
            backdrop = compositor.CreateBackdropBrush();
            effect.SetSourceParameter("Backdrop", backdrop);
            CompositionBrush = effect;
            RenderingMode = "Native Composition · live in-app backdrop";
            Apply(material, false);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            Release();
            CompositionBrush = compositor.CreateColorBrush(FallbackColor);
            RenderingMode = $"Solid fallback · effects unavailable (0x{exception.HResult:X8})";
        }
        RenderingModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(GlassMaterial value, bool animate)
    {
        material = RefractionLayer.ForNativeBackend(value);
        if (effect is null) return;
        animate &= uiSettings.AnimationsEnabled;
        foreach (var (name, scalar) in GlassEffectGraph.Scalars(material)) Scalar(name, scalar, animate);
        effect.Properties.InsertColor("Tint.Color", GlassEffectGraph.Tint(material));
    }

    private void Scalar(string name, float value, bool animate)
    {
        if (effect is null) return;
        effect.Properties.StopAnimation(name);
        if (!animate) { effect.Properties.InsertScalar(name, value); return; }
        using var animation = effect.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1, value);
        animation.Duration = TimeSpan.FromMilliseconds(180);
        effect.Properties.StartAnimation(name, animation);
    }

    protected override void OnDisconnected() => Release();

    private void Release()
    {
        var brush = CompositionBrush;
        CompositionBrush = null;
        brush?.Dispose();
        effect = null;
        backdrop?.Dispose();
        backdrop = null;
        factory?.Dispose();
        factory = null;
    }
}
