using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using GlassDock.Core.Materials;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Storage.Streams;

namespace GlassDock.App.Controls;

internal sealed class UtilityIcon : Grid
{
    private readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly TextBlock fallback = new() { Text = "?", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
    private readonly Dictionary<string, SvgImageSource> sources = [];
    private readonly Dictionary<string, string> originals = [];
    private int revision;
    private string? displayedKey;
    private Image? previousImage;
    private Storyboard? colorTransition;
    private Microsoft.UI.Xaml.XamlRoot? observedRoot;
    public UtilityIcon()
    {
        Children.Add(fallback); Children.Add(image); IsHitTestVisible = false;
        UseLayoutRounding = true;
        SizeChanged += (_, _) => UpdateRasterization();
        Loaded += (_, _) =>
        {
            observedRoot = XamlRoot;
            if (observedRoot is not null) observedRoot.Changed += RootChanged;
            UpdateRasterization();
        };
        Unloaded += (_, _) =>
        {
            StopColorTransition();
            if (observedRoot is not null) observedRoot.Changed -= RootChanged;
            observedRoot = null;
        };
    }

    private void RootChanged(Microsoft.UI.Xaml.XamlRoot sender, Microsoft.UI.Xaml.XamlRootChangedEventArgs args) => UpdateRasterization();
    private void UpdateRasterization()
    {
        foreach (var source in sources.Values) SizeSource(source);
    }
    private void SizeSource(SvgImageSource source)
    {
        // SVG pixels follow the arranged DIPs and monitor DPI, including the icon's hover scale.
        var pixels = Math.Ceiling(Math.Max(1, ActualWidth > 0 ? ActualWidth : double.IsFinite(Width) ? Width : 28)
            * (XamlRoot?.RasterizationScale ?? 1) * 1.03);
        if (source.RasterizePixelWidth == pixels && source.RasterizePixelHeight == pixels) return;
        source.RasterizePixelWidth = source.RasterizePixelHeight = pixels;
    }
    public async Task SetAsync(string icon, bool active, uint color, TimeSpan? colorTransitionDuration = null)
    {
        var current = ++revision;
        fallback.Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255,
            (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Utilities", active ? "Fill" : "Regular", icon + (active ? "-fill" : "") + ".svg");
        var key = path + color;
        try
        {
            if (!sources.TryGetValue(key, out var source))
            {
                if (!originals.TryGetValue(path, out var text)) originals[path] = text = await File.ReadAllTextAsync(path);
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(Encoding.UTF8.GetBytes(UtilitySvg.Tint(text, color)).AsBuffer());
                stream.Seek(0);
                source = new SvgImageSource();
                SizeSource(source);
                if (await source.SetSourceAsync(stream) != SvgImageSourceLoadStatus.Success) throw new FormatException("SVG load failed.");
                sources[key] = source;
            }
            if (current != revision) return;
            if (displayedKey == key) return;
            var oldSource = image.Source;
            StopColorTransition();
            image.Source = source; fallback.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            displayedKey = key;
            if (oldSource is not null && colorTransitionDuration is { } duration && duration > TimeSpan.Zero && IsLoaded)
            {
                if (previousImage is null)
                {
                    previousImage = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false };
                    Children.Insert(0, previousImage);
                }
                // Keep the old color opaque underneath so recoloring never fades the glyph away.
                previousImage.Source = oldSource; previousImage.Visibility = Visibility.Visible;
                image.Opacity = 0;
                var animation = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(duration),
                    EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
                Storyboard.SetTarget(animation, image); Storyboard.SetTargetProperty(animation, "Opacity");
                var story = new Storyboard(); story.Children.Add(animation); colorTransition = story;
                story.Completed += (_, _) => { if (ReferenceEquals(colorTransition, story)) StopColorTransition(); };
                story.Begin();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException or System.Runtime.InteropServices.COMException)
        {
            if (current != revision) return;
            StopColorTransition(); displayedKey = null;
            image.Source = null; fallback.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private void StopColorTransition()
    {
        colorTransition?.Stop(); colorTransition = null; image.Opacity = 1;
        if (previousImage is not null) { previousImage.Visibility = Visibility.Collapsed; previousImage.Source = null; }
    }
}
