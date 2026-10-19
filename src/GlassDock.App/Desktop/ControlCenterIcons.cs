using GlassDock.Core.Settings;
using GlassDock.App.Rendering;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace GlassDock.App.Desktop;

/// <summary>Per-window vector source cache. Clear preserves the supplied SVG colors verbatim.</summary>
internal sealed class ControlCenterIcons
{
    private readonly Dictionary<(string File, bool Light, int Pixels), Task<SvgImageSource>> sources = [];
    private readonly Dictionary<Image, (string Name, bool Active, bool Disabled, bool DarkInk)> images = [];
    private DockAppearanceMode mode;
    public Image Create(string name, double size)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        XamlRoot? root = null;
        void Refresh()
        {
            if (images.TryGetValue(image, out var state)) Set(image, state.Name, state.Active, state.Disabled, state.DarkInk);
        }
        void DpiChanged(XamlRoot sender, XamlRootChangedEventArgs args) => Refresh();
        image.Loaded += (_, _) => { root = image.XamlRoot; if (root is not null) root.Changed += DpiChanged; Refresh(); };
        image.Unloaded += (_, _) => { if (root is not null) root.Changed -= DpiChanged; root = null; };
        image.SizeChanged += (_, _) => Refresh();
        Set(image, name);
        return image;
    }
    public void Apply(DockAppearanceMode value)
    {
        mode = value;
        foreach (var (image, state) in images.ToArray()) Set(image, state.Name, state.Active, state.Disabled, state.DarkInk);
    }
    public async void Set(Image image, string name, bool active = false, bool disabled = false, bool darkInk = false)
    {
        var state = (Name: name, Active: active, Disabled: disabled, DarkInk: darkInk);
        var suffix = disabled ? "-disabled" : active ? "-active" : "";
        var file = name + suffix + ".svg";
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "ControlCenter", file);
        if (!File.Exists(path)) file = name + ".svg";
        var pixels = Math.Max(1, (int)Math.Ceiling(image.Width * (image.XamlRoot?.RasterizationScale ?? 1)));
        var cacheKey = (file, mode == DockAppearanceMode.Light && !active || mode == DockAppearanceMode.Clear && darkInk, pixels);
        if (images.TryGetValue(image, out var old) && old == state && Equals(image.Tag, cacheKey)) return;
        images[image] = state;
        image.Tag = cacheKey;
        try
        {
            if (!sources.TryGetValue(cacheKey, out var task)) sources[cacheKey] = task = Load(file, cacheKey.Item2, pixels);
            var source = await task;
            if (Equals(image.Tag, cacheKey)) image.Source = source;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        { StartupDiagnostics.Write("Control Center SVG unavailable: " + file, e); }
    }
    private static async Task<SvgImageSource> Load(string file, bool light, int pixels)
    {
        var source = new SvgImageSource { RasterizePixelWidth = pixels, RasterizePixelHeight = pixels };
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "ControlCenter", file));
        // Dark ink is required on Light surfaces and white active-card chevrons.
        // State-specific artwork colors and original asset files stay untouched.
        if (light) text = text.Replace("#ffffff", "#242424", StringComparison.OrdinalIgnoreCase);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        { writer.WriteBytes(System.Text.Encoding.UTF8.GetBytes(text)); await writer.StoreAsync(); }
        stream.Seek(0);
        var result = await source.SetSourceAsync(stream);
        if (result != SvgImageSourceLoadStatus.Success) throw new IOException($"Invalid SVG: {file} ({result})");
        return source;
    }
}
