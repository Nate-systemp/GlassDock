using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDock.App.Controls;

/// <summary>A reusable UI surface; no window or shell behavior is owned here.</summary>
public sealed partial class GlassSurface : Microsoft.UI.Xaml.Controls.UserControl
{
    private readonly GlassCompositionBrush brush = new();
    private SpriteVisual? shadowVisual;
    private DropShadow? shadow;
    private CompositionBrush? shadowMask;
    private CompositionRoundedRectangleGeometry? shadowGeometry;
    private ShapeVisual? maskVisual;
    private CompositionVisualSurface? maskSurface;
    private CompositionSpriteShape? maskShape;
    private CompositionColorBrush? maskFill;
    private GlassMaterial material = new();
    private DockAppearanceMode plainAppearance = DockAppearanceMode.Dark;
    private double plainOpacity = 1;
    private double plainCornerRadius = 28;
    private bool desktopFallback;

    internal void SetDesktopFallback(bool enabled)
    {
        desktopFallback = enabled;
        MaterialShape.Fill = new SolidColorBrush(enabled
            ? Desktop.DockControlPalette.SolidSurface(plainAppearance, 245)
            : Colors.Transparent);
        if (!enabled && IsLoaded && !UsePlainSurface && shadow is null)
            OnLoaded(this, new RoutedEventArgs());
    }

    public string RenderingMode => UseDesktopBackdrop ? "Desktop system backdrop · shared material graph" : brush.RenderingMode;
    public bool UseDesktopBackdrop { get; set; }
    public bool UsePlainSurface { get; set; }
    public event EventHandler? RenderingModeChanged;
    public UIElement? PreviewContent { get => SurfaceContent.Content as UIElement; set => SurfaceContent.Content = value; }

    public GlassSurface()
    {
        InitializeComponent();
        MaterialShape.Fill = brush;
        brush.RenderingModeChanged += (_, _) => RenderingModeChanged?.Invoke(this, EventArgs.Empty);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => ResizeShadow();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (UseDesktopBackdrop && (desktopFallback || (Application.Current as App)?.BasicRendering == true))
        {
            SetDesktopFallback(true);
            return;
        }
        if (UsePlainSurface)
        {
            // The solid SystemBackdrop is below XAML. A filled XAML shadow
            // mask would darken its interior; the plain dock needs no overlay.
            MaterialShape.Fill = new SolidColorBrush(Colors.Transparent);
            ApplyPlain(plainAppearance, plainOpacity, plainCornerRadius);
            return;
        }
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        if (UseDesktopBackdrop) MaterialShape.Fill = new SolidColorBrush(Colors.Transparent);
        else brush.Connect(compositor);
        shadow = compositor.CreateDropShadow();
        shadow.Color = Colors.Black;
        // Independent opaque geometry avoids redirecting a backdrop-sampling XAML shape.
        shadowGeometry = compositor.CreateRoundedRectangleGeometry();
        maskShape = compositor.CreateSpriteShape(shadowGeometry);
        maskFill = compositor.CreateColorBrush(Colors.White);
        maskShape.FillBrush = maskFill;
        maskVisual = compositor.CreateShapeVisual();
        maskVisual.Shapes.Add(maskShape);
        maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = maskVisual;
        shadowMask = compositor.CreateSurfaceBrush(maskSurface);
        shadow.Mask = shadowMask;
        shadowVisual = compositor.CreateSpriteVisual();
        shadowVisual.Shadow = shadow;
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, shadowVisual);
        Apply(material);
        ResizeShadow();
    }

    public void Apply(GlassMaterial value, bool animate = false)
    {
        material = value.Normalize();
        brush.Apply(material, animate);
        MaterialShape.RadiusX = MaterialShape.RadiusY = material.CornerRadius;
        LightingShape.RadiusX = LightingShape.RadiusY = material.CornerRadius;
        Rim.CornerRadius = new CornerRadius(material.CornerRadius);
        Rim.BorderThickness = new Thickness(material.BorderThickness);
        Rim.Opacity = material.BorderOpacity;
        // A restrained top reflection and cool lower edge: lighting, not pixel displacement.
        LightingShape.Fill = new LinearGradientBrush
        {
            StartPoint = new global::Windows.Foundation.Point(0, 0),
            EndPoint = new global::Windows.Foundation.Point(0.8, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb((byte)(material.EdgeHighlight * 255), 255, 255, 255), Offset = 0 },
                new GradientStop { Color = Colors.Transparent, Offset = 0.4 },
                new GradientStop { Color = Color.FromArgb((byte)(material.EdgeHighlight * 100), 145, 224, 255), Offset = 1 }
            }
        };
        if (shadow is not null)
        {
            shadow.Opacity = (float)material.ShadowOpacity;
            shadow.BlurRadius = (float)material.ShadowBlur;
            shadow.Offset = new Vector3(0, (float)material.ShadowOffset, 0);
        }
        ResizeShadow();
    }

    /// <summary>
    /// Configures the main dock's plain surface and layout. Popup surfaces continue to use
    /// <see cref="Apply(GlassMaterial, bool)"/> and the glass graph.
    /// </summary>
    public void ApplyPlain(
        DockAppearanceMode appearance,
        double opacity = 1,
        double cornerRadius = 28)
    {
        plainAppearance = Enum.IsDefined(appearance)
            ? appearance
            : DockAppearanceMode.Dark;
        plainOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        plainCornerRadius = double.IsFinite(cornerRadius) ? Math.Clamp(cornerRadius, 0, 100) : 28;

        material = material with
        {
            Opacity = plainOpacity,
            CornerRadius = plainCornerRadius,
            BorderOpacity = 0,
            BorderThickness = 0,
            EdgeHighlight = 0,
            ShadowOpacity = 0
        };

        MaterialShape.RadiusX = MaterialShape.RadiusY = plainCornerRadius;
        LightingShape.RadiusX = LightingShape.RadiusY = plainCornerRadius;
        LightingShape.Fill = new SolidColorBrush(Colors.Transparent);
        Rim.CornerRadius = new CornerRadius(plainCornerRadius);
        Rim.BorderThickness = new Thickness(0);
        Rim.Opacity = 0;

        if (!UseDesktopBackdrop || desktopFallback)
        {
            var color = Desktop.DockControlPalette.SolidSurface(plainAppearance, (byte)(plainOpacity * 255));
            MaterialShape.Fill = new SolidColorBrush(color);
        }

        if (shadow is not null)
        {
            shadow.Opacity = (float)material.ShadowOpacity;
            shadow.BlurRadius = (float)material.ShadowBlur;
            shadow.Offset = new Vector3(0, (float)material.ShadowOffset, 0);
        }

        ResizeShadow();
    }

    private void ResizeShadow()
    {
        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        if (shadowVisual is not null) shadowVisual.Size = size;
        if (shadowGeometry is not null)
        {
            shadowGeometry.Size = size;
            shadowGeometry.CornerRadius = new Vector2((float)material.CornerRadius);
        }
        if (maskVisual is not null) maskVisual.Size = size;
        if (maskSurface is not null) maskSurface.SourceSize = size;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ElementCompositionPreview.SetElementChildVisual(ShadowHost, null);
        shadowVisual?.Dispose();
        shadowVisual = null;
        shadow?.Dispose();
        shadow = null;
        shadowMask?.Dispose();
        shadowMask = null;
        maskSurface?.Dispose();
        maskSurface = null;
        maskVisual?.Dispose();
        maskVisual = null;
        maskShape?.Dispose();
        maskShape = null;
        maskFill?.Dispose();
        maskFill = null;
        shadowGeometry?.Dispose();
        shadowGeometry = null;
    }
}
