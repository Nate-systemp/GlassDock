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

    public AdaptiveAppIcon(double size, double maximumHoverScale)
    {
        Configure(size, maximumHoverScale);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
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
        tile.Opacity = alphaTotal / (255d * pixelWidth * pixelHeight) > 0.85 ? 0.45 : 1;
        Fit();
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Fit();

    private void Fit()
    {
        if (pixelWidth == 0 || pixelHeight == 0) return;
        var dpi = XamlRoot?.RasterizationScale ?? 1;
        const double padding = 2;
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
