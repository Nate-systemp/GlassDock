using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using GlassDock.Core.Materials;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace GlassDock.App.Controls;

internal sealed class UtilityIcon : Grid
{
    private readonly Image image = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    private readonly TextBlock fallback = new() { Text = "?", HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
    private readonly Dictionary<string, SvgImageSource> sources = [];
    private readonly Dictionary<string, string> originals = [];
    private int revision;
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
    public async Task SetAsync(string icon, bool active, uint color)
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
            image.Source = source; fallback.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException or System.Runtime.InteropServices.COMException)
        {
            if (current != revision) return;
            image.Source = null; fallback.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }
}
