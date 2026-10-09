using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Effects;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using W = global::Windows.UI.Composition;

namespace GlassDock.App.Rendering;

/// <summary>The OS compositor supplies desktop pixels; the laboratory's graph supplies the material.</summary>
internal sealed class DesktopGlassBackdrop : SystemBackdrop
{
    internal event EventHandler? PopupSurfaceChanged;
    internal DockAppearanceMode PopupMode { get; private set; }
    internal LiquidGlassMaterial PopupOptics { get; private set; } = new() { RefractionStrength = 12 };
    internal void SetPopupMode(DockAppearanceMode mode, LiquidGlassMaterial optics)
    {
        PopupMode = mode;
        PopupOptics = optics;
        PopupSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }
    internal (Vector4 Bounds, float Radius, float Scale) PopupGeometry =>
        (new((float)((lastWindowWidth - lastWidth) / 2),
            (float)(lastWindowHeight - lastBottom - lastHeight), (float)lastWidth, (float)lastHeight),
            (float)Math.Min(material.CornerRadius, Math.Min(lastWidth, lastHeight) / 2), (float)lastScale);
    private GlassDock.Core.Desktop.HomeCardRect[]? surfaceRegions;
    private double surfaceRegionScale = 1;

    /// <summary>Optional disjoint cards share one desktop effect, without a full-window glass/shadow.</summary>
    public void SetSurfaceRegions(IEnumerable<GlassDock.Core.Desktop.HomeCardRect> regions, double contentScale = 1)
    {
        var next = regions.ToArray();
        if (surfaceRegions is not null && surfaceRegions.SequenceEqual(next) && surfaceRegionScale == contentScale) return;
        surfaceRegions = next;
        surfaceRegionScale = contentScale;
        renderedMask = null;
        UpdateMaskPath();
    }
    public bool UseInnerEdge { get; set; }
    public bool UseDockSpecular { get; set; }
    public double SpecularHighlightAngle { get; set; } = 45;
    // Popup-only: soften Acrylic's lightly diffused base so the 12% base stream
    // cannot bring sharp wallpaper details back over the blurred material.
    // The main dock and Clear composition paths never enable this option.
    public bool UsePopupBlur { get; set; }
    // The dock's validated CreateBackdropBrush path is the default for
    // utility popups once their native HWNDs have the same layered DWM client.
    // Optional host sampling can be enabled for an A/B runtime comparison on
    // Windows versions where the regular source does not include desktop pixels.
    public bool UseHostBackdropForPopup { get; set; }
    private bool ConnectedSourceIsHost => UsePopupBlur && UseHostBackdropForPopup;
    private bool connectedWithHostBackdrop;
    private GlassMaterialMode? dockStyle;
    private ClearDockSpecular? clearSpecular;
    // An optional Clear-only composition path must not crash the entire dock
    // when a GPU/Windows compositor rejects one of its source brush types.
    // Keep the existing edge treatment as the supported fallback.
    private bool clearSpecularUnavailable;

    public void ApplyMainDock(GlassMaterialMode style, GlassMaterial value)
    {
        dockStyle = style;
        Apply(value);
    }
    /// <summary>
    /// Uses the existing vector mask and wave geometry with a solid color
    /// source. This is used only by the main dock; utility popups retain the
    /// desktop backdrop/material graph.
    /// </summary>
    public bool UseSolidSurface { get; set; }
    private double presentationScaleX = 1;
    private double presentationScaleY = 1;
    private double presentationOffsetX;
    private double presentationOffsetY;
    private double presentationX;
    private double presentationY;
    private double presentationOpacity = 1;
    private Matrix4x4? presentationTransform;
    private BackdropCompositionTrack? bodyTrack, rimTrack;

    public void AnimatePresentation(PopupCompositionFrame[] frames, TimeSpan duration)
    {
        if (visual is null) return;
        bodyTrack ??= new BackdropCompositionTrack(visual);
        bodyTrack.Bind();
        var units = (float)(lastScale * (UseInnerEdge || UseSolidSurface ? 2 : 1));
        bodyTrack.Start(frames, duration, units);
        if (edgeVisual is not null)
        {
            rimTrack ??= new BackdropCompositionTrack(edgeVisual, animateOpacity: false);
            rimTrack.Bind();
            rimTrack.Start(frames, duration, units);
        }
    }

    public void StopPresentationAnimation()
    {
        bodyTrack?.Stop();
        rimTrack?.Stop();
    }

    public void SetPresentationTransform(Matrix4x4 transform, double opacity)
    {
        presentationTransform = transform;
        presentationOpacity = Math.Clamp(opacity, 0, 1);
        ApplyPresentation();
    }

    // Backward-compatible uniform transform used by existing callers.
    public void SetPresentation(double scale, double offsetY, double originX, double originY, double opacity) =>
        SetPresentation(scale, scale, 0, offsetY, originX, originY, opacity);

    /// <summary>
    /// Applies the same affine presentation transform used by a utility popup's
    /// content to the desktop-glass mask and inner edge. Values are in DIPs.
    /// </summary>
    public void SetPresentation(
        double scaleX,
        double scaleY,
        double offsetX,
        double offsetY,
        double originX,
        double originY,
        double opacity)
    {
        presentationTransform = null;
        presentationScaleX = double.IsFinite(scaleX) ? Math.Max(0, scaleX) : 1;
        presentationScaleY = double.IsFinite(scaleY) ? Math.Max(0, scaleY) : 1;
        presentationOffsetX = double.IsFinite(offsetX) ? offsetX : 0;
        presentationOffsetY = double.IsFinite(offsetY) ? offsetY : 0;
        presentationX = double.IsFinite(originX) ? originX : 0;
        presentationY = double.IsFinite(originY) ? originY : 0;
        presentationOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        if (visual is null)
            return;

        var units = (float)(lastScale * (UseInnerEdge || UseSolidSurface ? 2 : 1));
        var origin = new Vector3(
            (float)presentationX * units,
            (float)presentationY * units,
            0);
        var translation = new Vector3(
            (float)presentationOffsetX * units,
            (float)presentationOffsetY * units,
            0);

        var matrix = Matrix4x4.CreateTranslation(-origin) *
            Matrix4x4.CreateScale(
                (float)presentationScaleX,
                (float)presentationScaleY,
                1) *
            Matrix4x4.CreateTranslation(origin + translation);

        if (presentationTransform is { } transform)
        {
            // Conjugate the complete projective matrix, including perspective,
            // from DIPs into the mask's supersampled physical coordinates.
            matrix = Matrix4x4.CreateScale(1 / units, 1 / units, 1) * transform *
                Matrix4x4.CreateScale(units, units, 1);
        }

        clearSpecular?.SetTransform(matrix);
        visual.TransformMatrix = matrix;
        visual.Opacity = liquidActive ? 0 : (float)(presentationOpacity * lastOpacity);

        if (edgeVisual is not null)
            edgeVisual.TransformMatrix = matrix;
    }
    private W.CompositionEffectFactory? edgeFactory;
    private W.CompositionEffectBrush? edgeEffect;
    private W.CompositionSpriteShape? edgeShape;
    private W.ShapeVisual? edgeVisual;
    private W.ContainerVisual? edgeCaptureRoot;
    private W.CompositionVisualSurface? edgeSurface;
    private W.CompositionSurfaceBrush? edgeMask;
    private W.CompositionColorBrush? edgeFill;

    // Partial specular rim highlights. These are deliberately separate from the
    // continuous refractive inner edge: the full rim stays subtle while only a
    // few selected edge sections catch brighter "glass glints".
    private W.CompositionPathGeometry? specularLeftTopGeometry;
    private W.CompositionPathGeometry? specularRightTopGeometry;
    private W.CompositionPathGeometry? specularLeftBottomGeometry;
    private W.CompositionSpriteShape? specularLeftTopShape;
    private W.CompositionSpriteShape? specularRightTopShape;
    private W.CompositionSpriteShape? specularLeftBottomShape;
    private W.CompositionColorBrush? specularFill;
    private CanvasGeometry? specularLeftTopCanvas;
    private CanvasGeometry? specularRightTopCanvas;
    private CanvasGeometry? specularLeftBottomCanvas;

    private W.Compositor? compositor;
    private W.CompositionEffectFactory? factory;
    private W.CompositionEffectBrush? effect;
    private W.CompositionBackdropBrush? source;
    private W.CompositionColorBrush? solidSurface;
    private W.CompositionPathGeometry? geometry;
    private W.CompositionSpriteShape? shape;
    private CanvasDevice? canvasDevice;
    private CanvasGeometry? canvasGeometry;
    internal CanvasGeometry? DockGeometry => surfaceRegions is null ? canvasGeometry : null;
    private bool liquidActive;
    internal void SetLiquidActive(bool active)
    {
        if (liquidActive == active) return;
        liquidActive = active;
        ApplyPresentation();
    }
    private readonly record struct MaskState(double WindowWidth, double WindowHeight, double Width,
        double Height, double Bottom, double Scale, double Radius, bool Wave, double Center,
        double HalfWidth, double Rise, double Strength);
    private MaskState? renderedMask;
    private W.CompositionColorBrush? fill;
    private W.ShapeVisual? visual;
    private W.ContainerVisual? maskCaptureRoot;
    private W.CompositionVisualSurface? maskSurface;
    private W.CompositionSurfaceBrush? mask;
    private W.CompositionMaskBrush? output;
    private W.CompositionBrush? fallback;
    private GlassMaterial material = new();
    private DockAppearanceMode solidAppearance = DockAppearanceMode.Dark;
    private double solidOpacity = 1;
    private double solidCornerRadius = 28;

    // Retain the latest mask geometry because AppWindow.Hide()/Show()
    // can disconnect and reconnect the SystemBackdrop.
    private bool hasBounds;
    private double lastWindowWidth;
    private double lastWindowHeight;
    private double lastWidth;
    private double lastHeight;
    private double lastBottom;
    private double lastScale = 1;
    private double lastOpacity = 1;

    // Continuous dock-wave state. This deforms the TOP EDGE of the same
    // dock silhouette; it is not a second pill/shape layered above it.
    private bool waveEnabled;
    private double waveCenterX;
    private double waveHalfWidth = 68;
    private double waveRise = 18;
    private double waveStrength;

    public string RenderingMode { get; private set; } = "Desktop backdrop connecting";
    public event EventHandler? RenderingModeChanged;
    private int connectionVersion;
    private FrameworkElement? loadingRoot;
    private RoutedEventHandler? loadedHandler;
    // Keep the WinUI SystemBackdrop attached to the Window across appearance changes.
    // Replacing Window.SystemBackdrop during a live transition can leave its queued
    // default-configuration callback holding an invalid native target.
    private ICompositionSupportsSystemBackdrop? connectedTarget;
    private FrameworkElement? connectedRoot;

    /// <summary>
    /// Rebuilds the solid/glass native pipeline under the SAME XAML backdrop
    /// attachment. Only the underlying composition brush is swapped; WinUI's
    /// Window.SystemBackdrop property is never detached during theme switching.
    /// Must be called on the window's UI dispatcher after selecting the new mode.
    /// </summary>
    public void RebuildConnectedSurface()
    {
        var target = connectedTarget;
        if (target is null || compositor is null)
        {
            // On initial construction the loaded callback will connect using
            // the latest mode. Basic-rendering fallback also stays intact.
            return;
        }

        try
        {
            ReleaseDesktop(target);
            ConnectDesktop(target);
            if (connectedRoot is not null)
                SetSurfaceFallback(connectedRoot, false);
        }
        catch (Exception error) when (OptionalComposition.IsRenderingFailure(error))
        {
            // Recover only from native composition failures, as at first load.
            // Do not swallow unrelated UI or application exceptions.
            ReleaseDesktop(target);
            if (Application.Current is App app)
                app.DisableDesktopComposition(error);
            RenderingMode = "Basic XAML surface · desktop composition unavailable";
            if (connectedRoot is not null)
                SetSurfaceFallback(connectedRoot, true);
            RenderingModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Switch the sampling source directly on the existing effect brush. Never
    /// detach or rebuild a cached utility SystemBackdrop on a mode change:
    /// WinUI may have a queued configuration callback against its native target.
    /// Keeping the brush, mask, specular edge, and HWND intact also preserves
    /// live popup presentation animations.
    /// </summary>
    public void RefreshPopupBackdropSource()
    {
        // Preserve Window.SystemBackdrop and both existing composition
        // effect objects. Only the *source brush* can change live.
        var wantsHost = ConnectedSourceIsHost;
        if (compositor is null || effect is null || UseSolidSurface ||
            connectedWithHostBackdrop == wantsHost)
            return;

        W.CompositionBackdropBrush? replacement = null;
        try
        {
            replacement = wantsHost
                ? compositor.CreateHostBackdropBrush()
                : compositor.CreateBackdropBrush();
            effect.SetSourceParameter("Backdrop", replacement);
            effect.SetSourceParameter("BaseBackdrop", replacement);
            var previous = source;
            source = replacement;
            replacement = null;
            connectedWithHostBackdrop = wantsHost;
            previous?.Dispose();
            RenderingMode = connectedWithHostBackdrop
                ? "Native utility backdrop · opt-in host source"
                : UsePopupBlur
                    ? "Native utility backdrop · main-dock layered source"
                    : "Native system backdrop · shared glass graph";
            System.Diagnostics.Debug.WriteLine($"[UtilityBackdrop] Switched native source: {RenderingMode}");
            StartupDiagnostics.Write($"Utility backdrop source: {RenderingMode}");
            RenderingModeChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (OptionalComposition.IsRenderingFailure(error))
        {
            // Do not tear down a functioning popup when optional host sampling
            // is unsupported (e.g. DWM disabled or remote desktop session).
            StartupDiagnostics.Write("Utility host backdrop source unavailable", error);
            replacement?.Dispose();
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        connectedTarget = target;
        connectedRoot = xamlRoot.Content as FrameworkElement;
        var version = ++connectionVersion;
        StartupDiagnostics.Write($"Backdrop target connected: root={xamlRoot.Content?.GetType().Name ?? "missing"}");
        if (xamlRoot.Content is not FrameworkElement root) return;
        void QueueConnection()
        {
            SetSurfaceFallback(root, true);
            root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (version != connectionVersion) return;
                RenderingMode = "Basic XAML surface";
                if ((Application.Current as App)?.TryEnableDesktopComposition() == true)
                {
                    try
                    {
                        StartupDiagnostics.Write("Loaded window: Win2D/backdrop initializing");
                        ConnectDesktop(target);
                        SetSurfaceFallback(root, false);
                        StartupDiagnostics.Write("Desktop backdrop ready");
                    }
                    catch (Exception error) when (OptionalComposition.IsRenderingFailure(error))
                    {
                        // Release partially created native resources. Keep the XAML body
                        // visible; never mark unrelated XAML exceptions handled globally.
                        ReleaseDesktop(target);
                        ((App)Application.Current).DisableDesktopComposition(error);
                        RenderingMode = "Basic XAML surface · desktop composition unavailable";
                        SetSurfaceFallback(root, true);
                    }
                }
                RenderingModeChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        if (root.IsLoaded) QueueConnection();
        else
        {
            loadingRoot = root;
            loadedHandler = (_, _) =>
            {
                root.Loaded -= loadedHandler;
                loadedHandler = null;
                loadingRoot = null;
                if (version == connectionVersion) QueueConnection();
            };
            root.Loaded += loadedHandler;
        }
    }

    private static void SetSurfaceFallback(DependencyObject element, bool enabled)
    {
        if (element is Controls.GlassSurface surface && surface.UseDesktopBackdrop)
            surface.SetDesktopFallback(enabled);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
            SetSurfaceFallback(VisualTreeHelper.GetChild(element, i), enabled);
    }

    private void ConnectDesktop(ICompositionSupportsSystemBackdrop target)
    {
        compositor = new W.Compositor();
        canvasDevice = CanvasDevice.GetSharedDevice();
        geometry = compositor.CreatePathGeometry();
        shape = compositor.CreateSpriteShape(geometry);
        fill = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255));
        shape.FillBrush = fill;
        visual = compositor.CreateShapeVisual();
        // These off-tree mask visuals have no XAML parent to supply soft edge
        // composition. Explicitly request antialiased bitmap/clip boundaries.
        visual.BorderMode = W.CompositionBorderMode.Soft;
        visual.Shapes.Add(shape);
        // Capture in a stationary parent's coordinate space. The animated visual
        // must be a child: transforming the capture root changes its outer-space
        // placement, rather than the pixels sampled in its own local space.
        maskCaptureRoot = compositor.CreateContainerVisual();
        maskCaptureRoot.Children.InsertAtTop(visual);
        maskSurface = compositor.CreateVisualSurface();
        maskSurface.SourceVisual = maskCaptureRoot;
        mask = compositor.CreateSurfaceBrush(maskSurface);
        mask.Stretch = W.CompositionStretch.Fill;
        mask.BitmapInterpolationMode = W.CompositionBitmapInterpolationMode.Linear;
        output = compositor.CreateMaskBrush();
        output.Mask = mask;

        if (UseSolidSurface)
        {
            solidSurface = compositor.CreateColorBrush(SolidColor(solidAppearance, solidOpacity));
            output.Source = solidSurface;
            material = material with
            {
                CornerRadius = solidCornerRadius,
                BorderOpacity = 0,
                BorderThickness = 0,
                EdgeHighlight = 0
            };
            RenderingMode = "Solid dock surface";
            Apply(material);
            if (hasBounds)
                ApplyBounds(
                    lastWindowWidth,
                    lastWindowHeight,
                    lastWidth,
                    lastHeight,
                    lastBottom,
                    lastScale,
                    lastOpacity);

            target.SystemBackdrop = output;
            RenderingModeChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var stage = "backdrop source";
        try
        {
            // Default to the same source as the WORKING main dock. Previously
            // utility HWNDs omitted WS_EX_LAYERED, so unlike the dock they did
            // not have a compatible desktop sampling client. The optional host
            // brush remains available as a controlled A/B diagnostic path.
            connectedWithHostBackdrop = ConnectedSourceIsHost;
            source = connectedWithHostBackdrop
                ? compositor.CreateHostBackdropBrush()
                : compositor.CreateBackdropBrush();
            stage = "effect factory";
            // Separate named leaves keep the graph tree-shaped. The selected main-dock
            // style controls base diffusion; popups retain their fully blurred base.
            factory = compositor.CreateEffectFactory(
                GlassEffectGraph.Create(new W.CompositionEffectSourceParameter("Backdrop"), new W.CompositionEffectSourceParameter("BaseBackdrop")),
                GlassEffectGraph.Properties.Concat(["BaseBlur.BlurAmount"]));
            stage = "effect brush";
            effect = factory.CreateBrush();
            effect.SetSourceParameter("Backdrop", source);
            effect.SetSourceParameter("BaseBackdrop", source);
            output.Source = effect;
            if (UseInnerEdge)
            {
                // One vector path owns both the body mask and the inner perimeter.
                // The final outer mask clips the centered stroke to its inner half.
                edgeShape = compositor.CreateSpriteShape(geometry);
                edgeFill = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(30, 255, 255, 255));
                edgeShape.StrokeBrush = edgeFill;

                // Three short highlight paths create the discontinuous white rim
                // catches seen on real curved glass. They share the same captured
                // edge surface so there is no extra HWND, timer, or render loop.
                specularLeftTopGeometry = compositor.CreatePathGeometry();
                specularRightTopGeometry = compositor.CreatePathGeometry();
                specularLeftBottomGeometry = compositor.CreatePathGeometry();
                specularFill = compositor.CreateColorBrush(
                    global::Windows.UI.Color.FromArgb(90, 255, 255, 255));

                specularLeftTopShape = compositor.CreateSpriteShape(specularLeftTopGeometry);
                specularRightTopShape = compositor.CreateSpriteShape(specularRightTopGeometry);
                specularLeftBottomShape = compositor.CreateSpriteShape(specularLeftBottomGeometry);
                specularLeftTopShape.StrokeBrush = specularFill;
                specularRightTopShape.StrokeBrush = specularFill;
                specularLeftBottomShape.StrokeBrush = specularFill;

                edgeVisual = compositor.CreateShapeVisual();
                edgeVisual.BorderMode = W.CompositionBorderMode.Soft;
                edgeVisual.Shapes.Add(edgeShape);
                edgeVisual.Shapes.Add(specularLeftTopShape);
                edgeVisual.Shapes.Add(specularRightTopShape);
                edgeVisual.Shapes.Add(specularLeftBottomShape);
                edgeCaptureRoot = compositor.CreateContainerVisual();
                edgeCaptureRoot.Children.InsertAtTop(edgeVisual);
                edgeSurface = compositor.CreateVisualSurface();
                edgeSurface.SourceVisual = edgeCaptureRoot;
                edgeMask = compositor.CreateSurfaceBrush(edgeSurface);
                edgeMask.Stretch = W.CompositionStretch.Fill;
                edgeMask.BitmapInterpolationMode = W.CompositionBitmapInterpolationMode.Linear;
                edgeFactory = compositor.CreateEffectFactory(new CompositeEffect
                {
                    Mode = CanvasComposite.SourceOver,
                    Sources =
                    {
                        new W.CompositionEffectSourceParameter("Body"),
                        new AlphaMaskEffect
                        {
                            Source = new ExposureEffect { Exposure = .35f,
                                Source = new SaturationEffect { Saturation = 1.12f,
                                    Source = new W.CompositionEffectSourceParameter("BodyEdge") } },
                            AlphaMask = new W.CompositionEffectSourceParameter("EdgeMask")
                        }
                    }
                });
                edgeEffect = edgeFactory.CreateBrush();
                edgeEffect.SetSourceParameter("Body", effect);
                edgeEffect.SetSourceParameter("BodyEdge", effect);
                edgeEffect.SetSourceParameter("EdgeMask", edgeMask);
                output.Source = edgeEffect;
            }
            RenderingMode = connectedWithHostBackdrop
                ? "Native utility backdrop · opt-in host source"
                : UsePopupBlur
                    ? "Native utility backdrop · main-dock layered source"
                    : "Native system backdrop · shared glass graph";
            System.Diagnostics.Debug.WriteLine($"[UtilityBackdrop] {RenderingMode}; " +
                $"blur={material.BlurAmount:0.##} DIP; host={connectedWithHostBackdrop}");
            if (UsePopupBlur)
                StartupDiagnostics.Write($"Utility backdrop connected: {RenderingMode}, " +
                    $"blur={material.BlurAmount:0.##} DIP");
            stage = "material parameters";
            Apply(material);

            if (hasBounds)
                ApplyBounds(
                    lastWindowWidth,
                    lastWindowHeight,
                    lastWidth,
                    lastHeight,
                    lastBottom,
                    lastScale,
                    lastOpacity);
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            fallback = compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(230, 35, 45, 62));
            output.Source = fallback;
            RenderingMode = $"Desktop solid fallback: {stage} (0x{exception.HResult:X8})";
            StartupDiagnostics.Write($"{RenderingMode}: {exception}");
        }
        target.SystemBackdrop = output;
        RenderingModeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(GlassMaterial value)
    {
        material = RefractionLayer.ForNativeBackend(value);
        if (edgeShape is not null)
        {
            edgeShape.StrokeThickness = (float)(material.BorderThickness * 6 * lastScale);

            // Keep the continuous edge quiet. The eye should read the brighter
            // discontinuous specular catches rather than a white outline.
            edgeFill!.Color = global::Windows.UI.Color.FromArgb(
                (byte)Math.Clamp(material.BorderOpacity * 72, 0, 92),
                255, 255, 255);

            if (specularFill is not null)
            {
                specularFill.Color = global::Windows.UI.Color.FromArgb(
                    (byte)Math.Clamp(38 + material.BorderOpacity * 170, 38, 142),
                    255, 255, 255);
            }

            var glintThickness = (float)(material.BorderThickness * 4.2 * lastScale);
            if (specularLeftTopShape is not null) specularLeftTopShape.StrokeThickness = glintThickness;
            if (specularRightTopShape is not null) specularRightTopShape.StrokeThickness = glintThickness;
            if (specularLeftBottomShape is not null) specularLeftBottomShape.StrokeThickness = glintThickness;
        }

        if (effect is not null)
        {
            foreach (var (name, scalar) in GlassEffectGraph.Scalars(material))
                effect.Properties.InsertScalar(name, scalar);

            var baseBlur = DockMaterialRendering.BaseBlur(dockStyle, material.BlurAmount);
            if (UsePopupBlur && dockStyle == GlassMaterialMode.Acrylic)
            {
                // Maintain Acrylic's two-layer texture but diffuse the
                // backdrop sufficiently in utility popups to be noticeable.
                baseBlur = Math.Max(baseBlur, Math.Min(material.BlurAmount, 18));
            }
            effect.Properties.InsertScalar("BaseBlur.BlurAmount", (float)baseBlur);

            effect.Properties.InsertColor(
                "Tint.Color",
                GlassEffectGraph.Tint(material));
        }

        W.CompositionBrush? specularBody = UseSolidSurface ? solidSurface : effect;
        if ((UseInnerEdge || UseDockSpecular) && specularBody is not null && fallback is null && output is not null)
        {
            if ((UseDockSpecular || dockStyle == GlassMaterialMode.Clear) && surfaceRegions is null && !clearSpecularUnavailable)
            {
                try
                {
                    clearSpecular ??= new ClearDockSpecular(compositor!, geometry!, specularBody);
                    // Solid surfaces keep their opaque body; only the shared rim
                    // receives lighting. A white catch needs more contrast on Light.
                    clearSpecular.Apply(UseDockSpecular
                        ? material with { BorderThickness = .35, BorderOpacity = UseSolidSurface
                            ? (solidAppearance == DockAppearanceMode.Light ? .45 : .24) : .28 }
                        : material, diagonalCatches: UseDockSpecular);
                    clearSpecular.SetVisible(true);
                    UpdateClearBounds();
                    ApplyPresentation();
                    // Publish the optional result only after its initialization
                    // and bounds update have both succeeded.
                    output.Source = clearSpecular.Brush;
                }
                catch (Exception error) when (OptionalComposition.IsRenderingFailure(error))
                {
                    // The base effect and the existing vector edge remain valid.
                    // Do not retry an unsupported native graph on every live switch.
                    clearSpecularUnavailable = true;
                    StartupDiagnostics.Write("Clear specular unavailable; using existing edge", error);
                    try { clearSpecular?.Dispose(); }
                    catch (Exception disposeError) when (OptionalComposition.IsRenderingFailure(disposeError))
                    {
                        StartupDiagnostics.Write("Clear specular cleanup failed", disposeError);
                    }
                    clearSpecular = null;
                    output.Source = edgeEffect ?? specularBody;
                }
            }
            else
            {
                clearSpecular?.SetVisible(false);
                output.Source = edgeEffect ?? specularBody;
            }
        }
        UpdateMaskPath();
    }

    private void UpdateClearBounds()
    {
        if (!hasBounds || clearSpecular is null) return;
        clearSpecular.SetBounds(lastWindowWidth, lastWindowHeight,
            lastWindowHeight - lastBottom - lastHeight, lastHeight, lastScale, lastWidth, SpecularHighlightAngle);
    }

    public void SetSolidAppearance(
        DockAppearanceMode appearance,
        double opacity,
        double cornerRadius)
    {
        dockStyle = null;
        solidAppearance = Enum.IsDefined(appearance)
            ? appearance
            : DockAppearanceMode.Dark;
        solidOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0, 1) : 1;
        solidCornerRadius = double.IsFinite(cornerRadius) ? Math.Clamp(cornerRadius, 0, 100) : 28;
        material = material with
        {
            CornerRadius = solidCornerRadius,
            BorderOpacity = 0,
            BorderThickness = 0,
            EdgeHighlight = 0
        };

        if (solidSurface is not null)
            solidSurface.Color = SolidColor(solidAppearance, solidOpacity);

        Apply(material);
    }

    private static global::Windows.UI.Color SolidColor(DockAppearanceMode appearance, double opacity)
    {
        var alpha = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        return Desktop.DockControlPalette.SolidSurface(appearance, alpha);
    }

    public void SetBounds(
        double windowWidth,
        double windowHeight,
        double width,
        double height,
        double bottom,
        double scale,
        double opacity = 1)
    {
        hasBounds = true;
        lastWindowWidth = windowWidth;
        lastWindowHeight = windowHeight;
        lastWidth = width;
        lastHeight = height;
        lastBottom = bottom;
        lastScale = scale;
        lastOpacity = opacity;

        ApplyBounds(
            windowWidth,
            windowHeight,
            width,
            height,
            bottom,
            scale,
            opacity);
        PopupSurfaceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyBounds(
        double windowWidth,
        double windowHeight,
        double width,
        double height,
        double bottom,
        double scale,
        double opacity)
    {
        if (geometry is null ||
            visual is null ||
            maskSurface is null)
        {
            return;
        }

        ApplyPresentation();

        var size =
            new Vector2(
                (float)(windowWidth * scale),
                (float)(windowHeight * scale));

        // Supersample the vector coverage mask (not the desktop pixels). The
        // compositor then linearly downsamples the mask, preserving subpixel
        // coverage on rounded corners and the animated wave crest. The legacy
        // XAML rim is now geometry-only, so this is the sole visible edge path.
        var sampling = UseInnerEdge || UseSolidSurface ? 2f : 1f;
        shape!.Scale = new Vector2(sampling);
        visual.Size = size * sampling;
        maskCaptureRoot!.Size = size * sampling;
        maskSurface.SourceSize = size * sampling;
        if (edgeVisual is not null)
        {
            edgeShape!.Scale = new Vector2(sampling);
            edgeShape.StrokeThickness = (float)(material.BorderThickness * 6 * scale);

            var glintScale = new Vector2(sampling);
            var glintThickness = (float)(material.BorderThickness * 4.2 * scale);
            if (specularLeftTopShape is not null)
            {
                specularLeftTopShape.Scale = glintScale;
                specularLeftTopShape.StrokeThickness = glintThickness;
            }
            if (specularRightTopShape is not null)
            {
                specularRightTopShape.Scale = glintScale;
                specularRightTopShape.StrokeThickness = glintThickness;
            }
            if (specularLeftBottomShape is not null)
            {
                specularLeftBottomShape.Scale = glintScale;
                specularLeftBottomShape.StrokeThickness = glintThickness;
            }

            edgeVisual.Size = size * sampling;
            edgeCaptureRoot!.Size = size * sampling;
            edgeSurface!.SourceSize = size * sampling;
        }

        UpdateClearBounds();
        UpdateMaskPath();
    }

    /// <summary>
    /// Deforms the dock's existing top edge into one smooth wave.
    /// All values are in root/window DIPs.
    /// </summary>
    public void SetDockWave(
        double centerX,
        double halfWidth,
        double rise,
        double strength)
    {
        if (!double.IsFinite(centerX) ||
            !double.IsFinite(halfWidth) ||
            !double.IsFinite(rise) ||
            !double.IsFinite(strength))
        {
            return;
        }

        waveEnabled = true;
        waveCenterX = centerX;
        waveHalfWidth = Math.Max(24, halfWidth);
        waveRise = Math.Max(0, rise);
        waveStrength = Math.Clamp(strength, 0, 1);

        UpdateMaskPath();
    }

    public void ClearDockWave()
    {
        waveEnabled = false;
        waveStrength = 0;
        UpdateMaskPath();
    }

    private CanvasGeometry CreateOpenPath(params (Vector2 Point, Vector2? Control1, Vector2? Control2)[] nodes)
    {
        using var builder = new CanvasPathBuilder(canvasDevice!);

        if (nodes.Length == 0)
            return CanvasGeometry.CreatePath(builder);

        builder.BeginFigure(nodes[0].Point);
        for (var i = 1; i < nodes.Length; i++)
        {
            var node = nodes[i];
            if (node.Control1 is { } c1 && node.Control2 is { } c2)
                builder.AddCubicBezier(c1, c2, node.Point);
            else
                builder.AddLine(node.Point);
        }
        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private CanvasGeometry CreateUpperRim(GlassDock.Core.Desktop.DockWaveGeometry.Outline outline, double left, double right)
    {
        var part = GlassDock.Core.Desktop.DockEdgeSlice.Upper(outline, left, right);
        Vector2 Px(GlassDock.Core.Desktop.DockWaveGeometry.Point p) => new((float)(p.X * lastScale), (float)(p.Y * lastScale));
        using var builder = new CanvasPathBuilder(canvasDevice!);
        builder.BeginFigure(Px(part.Start));
        foreach (var segment in part.Segments)
            builder.AddCubicBezier(Px(segment.Control1), Px(segment.Control2), Px(segment.End));
        builder.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(builder);
    }

    private void UpdateSpecularPaths(GlassDock.Core.Desktop.DockWaveGeometry.Outline outline)
    {
        if (canvasDevice is null ||
            specularLeftTopGeometry is null ||
            specularRightTopGeometry is null ||
            specularLeftBottomGeometry is null ||
            !hasBounds)
        {
            return;
        }

        var left = (lastWindowWidth - lastWidth) / 2;
        var top = lastWindowHeight - lastBottom - lastHeight;
        var right = left + lastWidth;
        var bottom = top + lastHeight;
        var radius = Math.Max(2,
            Math.Min(material.CornerRadius, Math.Min(lastWidth / 2, lastHeight / 2)));

        // Keep the existing partial spans and intensity. Their curves are sliced
        // from the same final outline, including waves near either end of the dock.
        var topLeftStart = left + radius * .78;
        var topLeftEnd = Math.Min(
            right - radius - 18,
            left + Math.Max(radius + 28, lastWidth * .27));

        var topRightStart = Math.Max(
            left + radius + 18,
            right - Math.Max(radius + 56, lastWidth * .23));

        var bottomLeftEnd = Math.Min(
            right - radius - 20,
            left + Math.Max(radius + 22, lastWidth * .16));

        Vector2 Px(double x, double y) =>
            new((float)(x * lastScale), (float)(y * lastScale));

        // Preserve the established spans, but slice the final wave outline:
        // no resting-baseline segment can remain inside the raised glass.
        var leftTop = CreateUpperRim(outline, topLeftStart, topLeftEnd);
        var r = radius;
        var rightTop = CreateUpperRim(outline, topRightStart, right);

        // Bottom-left: deliberately shorter and dimmer-looking by placement;
        // it balances the composition without enclosing the entire dock.
        var leftBottom = CreateOpenPath(
            (Px(left + r, bottom), null, null),
            (Px(bottomLeftEnd, bottom), null, null));

        specularLeftTopGeometry.Path = new W.CompositionPath(leftTop);
        specularRightTopGeometry.Path = new W.CompositionPath(rightTop);
        specularLeftBottomGeometry.Path = new W.CompositionPath(leftBottom);

        var previousLeftTop = specularLeftTopCanvas;
        var previousRightTop = specularRightTopCanvas;
        var previousLeftBottom = specularLeftBottomCanvas;

        specularLeftTopCanvas = leftTop;
        specularRightTopCanvas = rightTop;
        specularLeftBottomCanvas = leftBottom;

        previousLeftTop?.Dispose();
        previousRightTop?.Dispose();
        previousLeftBottom?.Dispose();
    }

    private void UpdateMaskPath()
    {
        if (geometry is null ||
            canvasDevice is null ||
            visual is null ||
            maskSurface is null ||
            !hasBounds)
        {
            return;
        }

        var state = new MaskState(lastWindowWidth, lastWindowHeight, lastWidth, lastHeight,
            lastBottom, lastScale, material.CornerRadius, waveEnabled, waveCenterX,
            waveHalfWidth, waveRise, waveStrength);
        if (renderedMask == state) return;

        if (surfaceRegions is not null)
        {
            UpdateRegionPaths();
            renderedMask = state;
            return;
        }

        var outline = GlassDock.Core.Desktop.DockWaveGeometry.Create(
            (lastWindowWidth - lastWidth) / 2,
            lastWindowHeight - lastBottom - lastHeight, lastWidth, lastHeight,
            material.CornerRadius, waveCenterX, waveHalfWidth, waveRise,
            waveEnabled ? waveStrength : 0);
        Vector2 Pixel(GlassDock.Core.Desktop.DockWaveGeometry.Point point) =>
            new((float)(point.X * lastScale), (float)(point.Y * lastScale));
        using var builder = new CanvasPathBuilder(canvasDevice);
        builder.BeginFigure(Pixel(outline.Start));
        foreach (var segment in outline.Segments)
        {
            if (segment.IsLine) builder.AddLine(Pixel(segment.End));
            else builder.AddCubicBezier(Pixel(segment.Control1), Pixel(segment.Control2), Pixel(segment.End));
        }
        builder.EndFigure(CanvasFigureLoop.Closed);
        var nextGeometry =
            CanvasGeometry.CreatePath(
                builder);

        geometry.Path = new W.CompositionPath(nextGeometry);
        UpdateSpecularPaths(outline);
        renderedMask = state;

        var previousGeometry =
            canvasGeometry;

        canvasGeometry =
            nextGeometry;

        previousGeometry?.Dispose();
    }

    private void UpdateRegionPaths()
    {
        // The same continuous vector contours define the blur mask and optical rim.
        using var body = new CanvasPathBuilder(canvasDevice!);
        using var upper = new CanvasPathBuilder(canvasDevice!);
        Vector2 Pixel(GlassDock.Core.Desktop.DockWaveGeometry.Point p) =>
            new((float)(p.X * lastScale), (float)(p.Y * lastScale));
        foreach (var rect in surfaceRegions!)
        {
            var outline = GlassDock.Core.Desktop.DockWaveGeometry.Create(rect.X, rect.Y,
                rect.Width, rect.Height, material.CornerRadius * surfaceRegionScale, 0, 24, 0, 0);
            body.BeginFigure(Pixel(outline.Start));
            foreach (var s in outline.Segments)
                if (s.IsLine) body.AddLine(Pixel(s.End));
                else body.AddCubicBezier(Pixel(s.Control1), Pixel(s.Control2), Pixel(s.End));
            body.EndFigure(CanvasFigureLoop.Closed);
            var slice = GlassDock.Core.Desktop.DockEdgeSlice.Upper(outline,
                rect.X + Math.Min(28, rect.Width / 4), rect.X + rect.Width * .72);
            upper.BeginFigure(Pixel(slice.Start));
            foreach (var s in slice.Segments)
                upper.AddCubicBezier(Pixel(s.Control1), Pixel(s.Control2), Pixel(s.End));
            upper.EndFigure(CanvasFigureLoop.Open);
        }
        var next = CanvasGeometry.CreatePath(body);
        geometry!.Path = new W.CompositionPath(next);
        var previous = canvasGeometry; canvasGeometry = next; previous?.Dispose();
        if (specularLeftTopGeometry is not null)
        {
            var top = CanvasGeometry.CreatePath(upper);
            specularLeftTopGeometry.Path = new W.CompositionPath(top);
            var old = specularLeftTopCanvas; specularLeftTopCanvas = top; old?.Dispose();
            // Other glint geometries remain empty: there is no full-window rim between cards.
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        connectedTarget = null;
        connectedRoot = null;
        connectionVersion++;
        if (loadingRoot is not null && loadedHandler is not null)
            loadingRoot.Loaded -= loadedHandler;
        loadingRoot = null;
        loadedHandler = null;
        ReleaseDesktop(target);
        base.OnTargetDisconnected(target);
    }

    private void ReleaseDesktop(ICompositionSupportsSystemBackdrop target)
    {
        StopPresentationAnimation();
        bodyTrack?.Dispose(); bodyTrack = null;
        rimTrack?.Dispose(); rimTrack = null;
        target.SystemBackdrop = null;
        clearSpecular?.Dispose(); clearSpecular = null;
        edgeEffect?.Dispose(); edgeFactory?.Dispose(); edgeMask?.Dispose();
        edgeSurface?.Dispose(); edgeVisual?.Dispose(); edgeShape?.Dispose(); edgeFill?.Dispose();

        specularLeftTopShape?.Dispose();
        specularRightTopShape?.Dispose();
        specularLeftBottomShape?.Dispose();
        specularLeftTopGeometry?.Dispose();
        specularRightTopGeometry?.Dispose();
        specularLeftBottomGeometry?.Dispose();
        specularFill?.Dispose();
        specularLeftTopCanvas?.Dispose();
        specularRightTopCanvas?.Dispose();
        specularLeftBottomCanvas?.Dispose();

        edgeCaptureRoot?.Dispose();
        edgeCaptureRoot = null;
        edgeEffect = null; edgeFactory = null; edgeMask = null; edgeSurface = null;
        edgeVisual = null; edgeShape = null; edgeFill = null;

        specularLeftTopShape = null;
        specularRightTopShape = null;
        specularLeftBottomShape = null;
        specularLeftTopGeometry = null;
        specularRightTopGeometry = null;
        specularLeftBottomGeometry = null;
        specularFill = null;
        specularLeftTopCanvas = null;
        specularRightTopCanvas = null;
        specularLeftBottomCanvas = null;
        output?.Dispose();
        mask?.Dispose();
        maskSurface?.Dispose();
        visual?.Dispose();
        maskCaptureRoot?.Dispose();
        maskCaptureRoot = null;
        shape?.Dispose();
        fill?.Dispose();
        geometry?.Dispose();
        canvasGeometry?.Dispose();
        fallback?.Dispose();
        effect?.Dispose();
        factory?.Dispose();
        source?.Dispose();
        solidSurface?.Dispose();
        compositor?.Dispose();
        output = null;
        mask = null;
        maskSurface = null;
        visual = null;
        shape = null;
        fill = null;
        geometry = null;
        renderedMask = null;
        canvasGeometry = null;
        canvasDevice = null;
        fallback = null;
        effect = null;
        factory = null;
        source = null;
        solidSurface = null;
        compositor = null;
        connectedWithHostBackdrop = false;
    }
}
