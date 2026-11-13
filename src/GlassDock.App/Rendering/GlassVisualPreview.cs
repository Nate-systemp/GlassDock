using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Desktop;
using GlassDock.Core.Applications;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDock.App.Rendering;

internal sealed class GlassVisualPreview : Grid, IDisposable
{
    private readonly CanvasControl canvas = new() { IsHitTestVisible = false };
    private readonly TextBlock status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel icons = new() { Orientation = Orientation.Horizontal, Spacing = 14,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly List<(string Id, Grid Host, AdaptiveAppIcon Icon, Border Indicator)> buttons = [];
    private readonly LiquidGlassRim rim = new();
    private GlassVisualProfile profile = new();
    private bool highlights = true, disposed;
    private CanvasRenderTarget? scene;
    private PixelShaderEffect? shader;
    private GaussianBlurEffect? blur;
    private ArithmeticCompositeEffect? native;
    private LiquidGlassShaderState? shaderState;
    private GlassVisualProfile? renderedProfile;
    private Vector2 sceneSize;
    private readonly IReadOnlyList<DockApplication> applications;
    private string focusedComponent = "";

    public GlassVisualPreview(IReadOnlyList<DockApplication> applications)
    {
        this.applications = applications;
        Children.Add(canvas); Children.Add(icons); Children.Add(status);
        FocusComponent("main");
        canvas.Draw += Draw;
        SizeChanged += (_, _) => canvas.Invalidate();
        AutomationProperties.SetName(this, "Live glass preview with representative dock buttons");
    }

    public void FocusComponent(string id)
    {
        if (disposed || focusedComponent == id) return;
        focusedComponent = id;
        icons.Children.Clear(); buttons.Clear();
        foreach (var app in applications.OrderByDescending(app => app.Id == id).Take(5))
        {
            var host = new Grid { Width = 44, Height = 48 };
            var icon = new AdaptiveAppIcon(28, 1, false);
            if (app.Stack is null) icon.SetIcon(app.Icon); else icon.SetStack(app.StackApps);
            host.Children.Add(icon);
            var indicator = new Border { Width = 10, Height = 3, CornerRadius = new(1.5),
                VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center };
            host.Children.Add(indicator);
            icons.Children.Add(host); buttons.Add((app.Id, host, icon, indicator));
            AutomationProperties.SetName(host, app.Name);
        }
        if (buttons.Count == 0)
            foreach (var glyph in new[] { "\uE80F", "\uE8B7", "\uE713" })
                icons.Children.Add(new FontIcon { Glyph = glyph, FontSize = 28 });
    }

    public void Update(GlassVisualProfile value, bool showHighlights)
    {
        if (disposed) return;
        profile = value.Normalize(); highlights = showHighlights;
        foreach (var (id, host, icon, indicator) in buttons)
        {
            icon.SetDockAppearance(profile.Mode);
            var material = profile.Component(id);
            var layer = host.Children.OfType<GlassSurface>().FirstOrDefault();
            if (material is null) { if (layer is not null) host.Children.Remove(layer); }
            else
            {
                if (layer is null) { layer = new() { IsHitTestVisible = false }; Canvas.SetZIndex(layer, -1); host.Children.Add(layer); }
                layer.Opacity = material.SurfaceOpacity;
                layer.Apply(DesktopOverlayWindow.ComponentMaterial(material, profile.Mode));
            }
            var indicatorMaterial = profile.Component("indicators");
            indicator.Background = new SolidColorBrush(DockControlPalette.Foreground(profile.Mode));
            indicator.Opacity = indicatorMaterial?.SurfaceOpacity ?? 1;
            indicator.CornerRadius = new(Math.Min(indicatorMaterial?.CornerRadius ?? 1.5, 1.5));
            indicator.BorderThickness = new(indicatorMaterial?.EdgeWidth ?? 0);
            indicator.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(255 * (indicatorMaterial?.EdgeOpacity ?? 0)), 255, 255, 255));
        }
        canvas.Invalidate();
    }

    private void Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (disposed || ActualWidth < 1 || ActualHeight < 1) return;
        try
        {
            var size = new Vector2((float)ActualWidth, (float)ActualHeight);
            if (scene is null || size != sceneSize)
            {
                native?.Dispose(); native = null; renderedProfile = null;
                scene?.Dispose();
                scene = new(sender.Device, size.X, size.Y, 96);
                sceneSize = size;
                using var background = scene.CreateDrawingSession();
                background.Clear(Color.FromArgb(255, 35, 53, 80));
                background.FillCircle(size.X * .25f, size.Y * .5f, 96, Color.FromArgb(255, 65, 143, 190));
                background.FillCircle(size.X * .8f, size.Y * .6f, 112, Color.FromArgb(255, 173, 108, 91));
                for (var x = 0; x < size.X; x += 24)
                    background.DrawLine(x, 0, x + 50, size.Y, Color.FromArgb(120, 230, 238, 245), 2);
            }
            var drawing = args.DrawingSession;
            drawing.DrawImage(scene);
            var width = (float)Math.Min(size.X - 24, Math.Max(260, icons.ActualWidth + 48));
            var bounds = new Vector4((size.X - width) / 2, (size.Y - 68) / 2, width, 68);
            var m = profile.DockMaterial;
            var radius = Math.Min(34, m.CornerRadius);
            using var geometry = CanvasGeometry.CreateRoundedRectangle(sender.Device, bounds.X, bounds.Y, bounds.Z, bounds.W, radius, radius);
            using (drawing.CreateLayer(1, geometry))
            {
                if (profile.Mode == DockAppearanceMode.Clear)
                {
                    shader ??= new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Rendering", "Shaders", "LiquidGlass.bin")));
                    blur ??= new() { BorderMode = EffectBorderMode.Hard };
                    blur.Source = scene; blur.BlurAmount = m.BlurAmount; blur.Optimization = GlassEffectGraph.Quality((int)m.BlurQuality);
                    shader.Source1 = m.BlurAmount > 0 ? blur : scene;
                    LiquidGlassShader.Configure(shader, m, 1, bounds, new(bounds.X + width / 2, width / 2, 0, radius), size, Vector4.Zero, ref shaderState);
                    drawing.DrawImage(shader);
                    rim.Draw(drawing, geometry, bounds, 1, highlights ? m : m with { SpecularIntensity = 0 }, dockSurface: true);
                }
                else
                {
                    using (drawing.CreateLayer(m.SurfaceOpacity))
                    {
                        if (profile.Mode.GlassStyle() is { } style)
                        {
                            if (renderedProfile != profile || native is null)
                            {
                                native?.Dispose();
                                var material = DesktopOverlayWindow.ComponentMaterial(m, profile.Mode);
                                native = (ArithmeticCompositeEffect)GlassEffectGraph.Create(scene, scene, material.BlurQuality,
                                    material, DockMaterialRendering.BaseBlur(style, material.BlurAmount));
                                renderedProfile = profile;
                            }
                            drawing.DrawImage(native);
                        }
                        else drawing.FillGeometry(geometry, DockControlPalette.SolidSurface(profile.Mode));
                        var nativeRim = m with { InnerShine = 0, SurfaceOpacity = 1,
                            SpecularIntensity = highlights ? m.SpecularIntensity * (profile.Mode == DockAppearanceMode.Light ? .45f : profile.Mode == DockAppearanceMode.Dark ? .24f : .28f) / .384f : 0,
                            HighlightWidth = m.HighlightWidth * .5f };
                        rim.Draw(drawing, geometry, bounds, 1, nativeRim);
                    }
                }
            }
            status.Text = "";
        }
        catch (Exception error) when (OptionalComposition.IsRenderingFailure(error) || error is IOException)
        {
            status.Text = "Glass preview unavailable: " + error.Message;
            StartupDiagnostics.Write("Glass settings preview failed", error);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        canvas.Draw -= Draw;
        shader?.Dispose(); blur?.Dispose(); native?.Dispose(); scene?.Dispose(); rim.Dispose();
        canvas.RemoveFromVisualTree();
    }
}
