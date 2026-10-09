using System.Runtime.InteropServices.WindowsRuntime;
using GlassDock.Core.Applications;
using GlassDock.App.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GlassDock.App.Controls;

/// <summary>Padded glass tile with an unmasked, native-resolution icon above it.</summary>
internal sealed class AdaptiveAppIcon : Grid
{
    private readonly Image image = new()
    {
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        UseLayoutRounding = false
    };
    private readonly Border tile = new()
    {
        Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(18, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(26, 255, 255, 255)),
        BorderThickness = new Thickness(0.5), IsHitTestVisible = false
    };
    private readonly Canvas artwork = new() { IsHitTestVisible = false };
    private double maximumHoverScale;
    private int pixelWidth;
    private int pixelHeight;
    private int sourceWidth, sourceHeight, left, top;
    private XamlRoot? observedRoot;
    private ApplicationIcon? original;
    private int rasterWidth, rasterHeight;
    private NotificationBadge? badge;
    private Canvas? stackPreview;
    private ApplicationIcon?[] stackIcons = [];
    private readonly double artworkPadding;
    private bool isStackLayer;
    private GlassDock.Core.Settings.DockAppearanceMode stackAppearance = GlassDock.Core.Settings.DockAppearanceMode.Dark;

    internal void SetDockAppearance(GlassDock.Core.Settings.DockAppearanceMode mode)
    {
        stackAppearance = mode;
        UpdateStackCard();
        if (stackPreview is not null)
            foreach (var child in stackPreview.Children)
                ((AdaptiveAppIcon)child).SetDockAppearance(mode);
    }

    // Supplies the same native artwork and fitted Image to the GPU drag-lens scene.
    // Called once per drag, not once per frame; no UI screenshot/readback is needed.
    internal void VisitLensArtwork(Action<ApplicationIcon?, FrameworkElement, Border> visit)
    {
        if (stackPreview is not null)
        {
            foreach (var child in stackPreview.Children)
                if (child is AdaptiveAppIcon mini) mini.VisitLensArtwork(visit);
        }
        else visit(original, image, tile);
    }

    internal NotificationBadge? LensBadge => badge;

    public void SetStack(IReadOnlyList<DockApplication> apps)
    {
        artwork.Visibility = Visibility.Collapsed;
        tile.Visibility = Visibility.Collapsed;
        var count = Math.Min(3, apps.Count);
        if (stackPreview is null)
        {
            stackPreview = new Canvas { IsHitTestVisible = false };
            Children.Insert(1, stackPreview);
        }
        if (stackIcons.Length != count)
        {
            stackPreview.Children.Clear();
            stackIcons = new ApplicationIcon?[count];
            // Paint back to front. The same order is visited by the drag-lens
            // sampler, so overlap agrees between XAML and the refracted artwork.
            for (var depth = count - 1; depth >= 0; depth--)
            {
                var layer = new AdaptiveAppIcon(Width * StackLayerScale(depth),
                    maximumHoverScale, false, artworkPadding * StackLayerScale(depth)) { isStackLayer = true };
                layer.SetDockAppearance(stackAppearance);
                stackPreview.Children.Add(layer);
            }
            LayoutStackLayers();
        }
        for (var depth = 0; depth < count; depth++)
        {
            var next = apps[depth].Icon;
            if (ReferenceEquals(stackIcons[depth], next)) continue;
            stackIcons[depth] = next;
            ((AdaptiveAppIcon)stackPreview.Children[count - 1 - depth]).SetIcon(next);
        }
    }

    private static double StackLayerScale(int depth) => depth == 0 ? 1 : depth == 1 ? .85 : .72;

    private void UpdateStackCard()
    {
        if (!isStackLayer) return;
        // ActualTheme only distinguishes Light/Dark; it cannot represent glass.
        // Use the selected dock appearance explicitly, including after reparenting.
        var color = stackAppearance switch
        {
            GlassDock.Core.Settings.DockAppearanceMode.Light or GlassDock.Core.Settings.DockAppearanceMode.Dark
                => Desktop.DockControlPalette.SolidSurface(stackAppearance),
            GlassDock.Core.Settings.DockAppearanceMode.Frosted => global::Windows.UI.Color.FromArgb(190, 42, 49, 59),
            GlassDock.Core.Settings.DockAppearanceMode.Acrylic => global::Windows.UI.Color.FromArgb(150, 42, 49, 59),
            _ => global::Windows.UI.Color.FromArgb(100, 42, 49, 59)
        };
        tile.Background = new SolidColorBrush(color);
        tile.BorderBrush = new SolidColorBrush(Desktop.DockControlPalette.Surface(stackAppearance, 36));
        tile.Opacity = 1;
        tile.Visibility = Visibility.Visible;
    }

    private void LayoutStackLayers()
    {
        if (stackPreview is null) return;
        // Rear centers move up/right by 3.75 DIP per layer at the default 28 DIP
        // icon size. Only the artwork overhangs slightly; the dock slot is fixed.
        var step = Math.Clamp(Width * (3.75 / 28), 2.5, 6);
        for (var index = 0; index < stackPreview.Children.Count; index++)
        {
            var depth = stackPreview.Children.Count - 1 - index;
            var layer = (AdaptiveAppIcon)stackPreview.Children[index];
            var size = Width * StackLayerScale(depth);
            layer.Configure(size, maximumHoverScale);
            Canvas.SetLeft(layer, (Width - size) / 2 + depth * step);
            Canvas.SetTop(layer, (Height - size) / 2 - depth * step);
        }
    }

    public void SetNotificationCount(int count) =>
        SetNotificationBadge(BadgeDisplayState.Counted(count, "legacy-count"));

    public void SetNotificationBadge(BadgeDisplayState display)
    {
        if (badge is null && !display.IsVisible) return;
        if (badge is null)
        {
            badge = new NotificationBadge
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -5, 0)
            };
            Children.Add(badge);
        }
        badge.SetBadge(display);
    }

    public AdaptiveAppIcon(double size, double maximumHoverScale, bool showTile = true)
        : this(size, maximumHoverScale, showTile, 2)
    {
    }

    private AdaptiveAppIcon(double size, double maximumHoverScale, bool showTile, double artworkPadding)
    {
        this.artworkPadding = double.IsFinite(artworkPadding) ? Math.Max(0, artworkPadding) : 2;
        image.UseLayoutRounding = this.artworkPadding < 1;
        Configure(size, maximumHoverScale);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        tile.Visibility = showTile ? Visibility.Visible : Visibility.Collapsed;
        Children.Add(tile);
        // Siblings: the rounded tile never clips or masks the image's alpha edges.
        Children.Add(artwork);
        artwork.Children.Add(image);
        Loaded += (_, _) =>
        {
            observedRoot = XamlRoot;
            if (observedRoot is not null) observedRoot.Changed += OnRootChanged;
            Fit();
        };
        Unloaded += (_, _) =>
        {
            if (observedRoot is not null) observedRoot.Changed -= OnRootChanged;
            observedRoot = null;
        };
    }

    public void Configure(double size, double hoverScale)
    {
        size = double.IsFinite(size) && size > 0 ? size : 28;
        maximumHoverScale = double.IsFinite(hoverScale) && hoverScale >= 1 ? hoverScale : 1.24;
        Width = Height = size;
        LayoutStackLayers();
        tile.CornerRadius = new CornerRadius(size * 0.25);
        Fit();
    }

    public void SetIcon(ApplicationIcon? icon)
    {
        image.Source = null;
        original = null;
        rasterWidth = rasterHeight = 0;
        pixelWidth = pixelHeight = 0;
        if (icon is null || icon.Width <= 0 || icon.Height <= 0 ||
            (long)icon.Width * icon.Height * 4 != icon.Pixels.Length) return;

        left = icon.Width;
        top = icon.Height;
        var right = -1;
        var bottom = -1;
        long alphaTotal = 0;
        for (var y = 0; y < icon.Height; y++)
        for (var x = 0; x < icon.Width; x++)
        {
            // Include every nonzero alpha pixel, including antialiasing and native shadows.
            if (icon.Pixels[(y * icon.Width + x) * 4 + 3] == 0) continue;
            alphaTotal += icon.Pixels[(y * icon.Width + x) * 4 + 3];
            left = Math.Min(left, x); top = Math.Min(top, y);
            right = Math.Max(right, x); bottom = Math.Max(bottom, y);
        }
        if (right < left) return;
        pixelWidth = right - left + 1;
        pixelHeight = bottom - top + 1;
        sourceWidth = icon.Width;
        sourceHeight = icon.Height;
        original = icon;
        // Already-filled artwork needs less of a backing plate than an open silhouette.
        tile.Opacity = !isStackLayer && alphaTotal / (255d * pixelWidth * pixelHeight) > 0.85 ? 0.45 : 1;
        Fit();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();

    private void Fit()
    {
        if (pixelWidth == 0 || pixelHeight == 0) return;
        var dpi = XamlRoot?.RasterizationScale ?? 1;
        var padding = Math.Min(artworkPadding, Math.Max(0, (Math.Min(Width, Height) - 1) / 2));
        var scale = Math.Min(Math.Min((Width - padding * 2) / pixelWidth, (Height - padding * 2) / pixelHeight),
            1 / (dpi * maximumHoverScale));
        image.Width = sourceWidth * scale;
        image.Height = sourceHeight * scale;
        Canvas.SetLeft(image, (Width - pixelWidth * scale) / 2 - left * scale);
        Canvas.SetTop(image, (Height - pixelHeight * scale) / 2 - top * scale);
        var targetWidth = Math.Min(sourceWidth, (int)Math.Ceiling(image.Width * dpi * maximumHoverScale));
        var targetHeight = Math.Min(sourceHeight, (int)Math.Ceiling(image.Height * dpi * maximumHoverScale));
        if (original is not null && (targetWidth != rasterWidth || targetHeight != rasterHeight))
        {
            // Always filter from the native source, never a previously reduced bitmap.
            // Reserve enough physical pixels for the maximum hover magnification.
            var raster = IconRasterizer.Downsample(original, targetWidth, targetHeight);
            var bitmap = new WriteableBitmap(raster.Width, raster.Height);
            using (var pixels = bitmap.PixelBuffer.AsStream()) pixels.Write(raster.Pixels);
            bitmap.Invalidate();
            image.Source = bitmap;
            rasterWidth = raster.Width;
            rasterHeight = raster.Height;
        }
        System.Diagnostics.Debug.WriteLine($"Dock icon: source={sourceWidth}x{sourceHeight}; display raster={rasterWidth}x{rasterHeight}; alpha bounds={pixelWidth}x{pixelHeight}; " +
            $"DPI={dpi:0.##}; artwork={pixelWidth * scale:0.##}x{pixelHeight * scale:0.##} DIP; " +
            $"hover pixels={pixelWidth * scale * dpi * maximumHoverScale:0.##}x{pixelHeight * scale * dpi * maximumHoverScale:0.##}");
    }
}
