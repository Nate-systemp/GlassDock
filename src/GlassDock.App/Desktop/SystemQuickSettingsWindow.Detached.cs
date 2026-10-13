using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GlassDock.App.Desktop;

internal sealed partial class SystemQuickSettingsWindow
{
    private UIElement[] dockModules = [];
    private readonly Dictionary<Slider, (Panel Parent, int Index)> sliderHomes = new();
    private readonly Dictionary<Slider, Panel> detachedSliderHosts = new();
    private readonly Dictionary<Slider, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler> detachedFillHandlers = new();
    private readonly WindowsMediaControls media = new();
    private TextBlock? mediaTitle, mediaArtist, mediaTime, detachedBattery;
    private Image? mediaArtwork;
    private ProgressBar? mediaProgress;
    private Button? previousMedia, toggleMedia, nextMedia;
    private bool mediaBusy;
    private string? artworkKey;

    private void BuildDetachedModules()
    {
        dockModules = panel.Children.ToArray();
        panel.Children.Clear();
        panel.RowDefinitions.Clear();
        foreach (var height in new[] { 108d, 104, 32 }) panel.RowDefinitions.Add(new() { Height = new(height) });
        panel.Margin = new(Gutter + 12);
        panel.RowSpacing = 6;
        Grid.SetRow(tiles, 0);
        tiles.ColumnSpacing = tiles.RowSpacing = 6;
        panel.Children.Add(tiles);
        foreach (var frame in tiles.Children.OfType<Border>())
        {
            var v = tileLayouts[frame];
            var more = v.Caption.Children.OfType<Button>().Last();
            v.Caption.Children.Clear(); v.Stack.Children.Clear();
            var row = new Grid { ColumnSpacing = 4 };
            row.ColumnDefinitions.Add(new() { Width = new(26) });
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = new(12) });
            more.Width = 12;
            v.Button.Width = v.Button.Height = 26; v.Button.CornerRadius = new(13);
            v.Button.Background = theme.Overlay;
            if (v.Button.Content is FontIcon glyph) glyph.FontSize = 15;
            row.Children.Add(v.Button);
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            v.Label.FontSize = 10; v.Label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            v.Label.MaxWidth = double.PositiveInfinity; v.Label.TextWrapping = TextWrapping.NoWrap;
            v.Label.TextAlignment = v.Detail.TextAlignment = TextAlignment.Left;
            v.Detail.FontSize = 9;
            text.Children.Add(v.Label); text.Children.Add(v.Detail);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            Grid.SetColumn(more, 2); row.Children.Add(more);
            frame.Child = row; frame.Padding = new(6); frame.CornerRadius = new(12);
        }
        var middle = new Grid { ColumnSpacing = 6 };
        middle.ColumnDefinitions.Add(new());
        middle.ColumnDefinitions.Add(new() { Width = new(36) });
        middle.ColumnDefinitions.Add(new() { Width = new(36) });
        middle.Children.Add(BuildMediaCard());
        AddVerticalSlider(middle, brightness, "\uE706", "Brightness", 1);
        AddVerticalSlider(middle, volumeSlider, "\uE767", "Volume", 2);
        Grid.SetRow(middle, 1); panel.Children.Add(middle);
        detachedBattery = new TextBlock { FontSize = 11, Foreground = theme.Primary, VerticalAlignment = VerticalAlignment.Center };
        var batteryRow = new Grid { ColumnSpacing = 8 };
        batteryRow.ColumnDefinitions.Add(new() { Width = new(24) }); batteryRow.ColumnDefinitions.Add(new());
        batteryRow.Children.Add(new FontIcon { Glyph = "\uE83F", FontSize = 18, Foreground = theme.Primary });
        Grid.SetColumn(detachedBattery, 1); batteryRow.Children.Add(detachedBattery);
        var batteryCard = Surface(batteryRow); Grid.SetRow(batteryCard, 2); panel.Children.Add(batteryCard);
        artworkKey = null;
    }

    private Border Surface(UIElement child) => new() { Child = child, Background = theme.Tile,
        BorderBrush = theme.TileBorder, BorderThickness = new(.5), CornerRadius = new(12), Padding = new(6) };

    private void AddVerticalSlider(Grid parent, Slider slider, string glyph, string label, int column)
    {
        // Parent is not reliable until WinUI loads the window. Use the owner
        // recorded when SliderRow constructed the existing control.
        var home = sliderHomes[slider];
        home.Parent.Children.Remove(slider);
        slider.Orientation = Orientation.Vertical; slider.Height = 92; slider.Width = 26;
        slider.Resources["SliderTrackThemeHeight"] = 26d;
        slider.Resources["SliderVerticalThumbWidth"] = 26d;
        slider.Resources["SliderVerticalThumbHeight"] = 8d;
        var stack = new Grid { Height = 92, Width = 26 };
        var fill = new Border { Background = ReferenceEquals(slider, volumeSlider) ? theme.Accent : theme.Primary,
            VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new(10) };
        stack.Children.Add(fill);
        // Retain native Slider input and UIA; the broad fill provides the visual.
        slider.Opacity = .01;
        stack.Children.Add(slider);
        stack.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14, Foreground = theme.Secondary,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 8), IsHitTestVisible = false });
        void UpdateFill() => fill.Height = Math.Max(0, 92 * slider.Value / 100);
        UpdateFill();
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler handler = (_, _) => UpdateFill();
        slider.ValueChanged += handler;
        detachedFillHandlers[slider] = handler;
        detachedSliderHosts[slider] = stack;
        var card = Surface(stack); card.Padding = new(4); card.CornerRadius = new(15);
        ToolTipService.SetToolTip(card, label); Grid.SetColumn(card, column); parent.Children.Add(card);
    }

    private Border BuildMediaCard()
    {
        var content = new Grid { RowSpacing = 4 };
        content.RowDefinitions.Add(new() { Height = new(34) });
        content.RowDefinitions.Add(new() { Height = new(26) });
        content.RowDefinitions.Add(new() { Height = new(18) });
        var info = new Grid { ColumnSpacing = 6 };
        info.ColumnDefinitions.Add(new() { Width = new(32) }); info.ColumnDefinitions.Add(new());
        mediaArtwork = new Image { Width = 32, Height = 32, Stretch = Stretch.UniformToFill };
        info.Children.Add(new Border { CornerRadius = new(10), Background = theme.Overlay, Child = mediaArtwork });
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        mediaTitle = new() { Text = "Nothing playing", FontSize = 11, Foreground = theme.Primary, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        mediaArtist = new() { Text = "No media session", FontSize = 9, Foreground = theme.Secondary, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        text.Children.Add(mediaTitle); text.Children.Add(mediaArtist); Grid.SetColumn(text, 1); info.Children.Add(text); content.Children.Add(info);
        var playback = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        Button Action(string glyph, string label, int action)
        {
            var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 13, Foreground = theme.Primary }, Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), CornerRadius = new(12), IsEnabled = false, Background = theme.Overlay };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, label);
            b.Click += async (_, _) => { await media.ExecuteAsync(action); await RefreshMediaAsync(); };
            playback.Children.Add(b); return b;
        }
        previousMedia = Action("\uE892", "Previous track", -1); toggleMedia = Action("\uE768", "Play or pause", 0); nextMedia = Action("\uE893", "Next track", 1);
        Grid.SetRow(playback, 1); content.Children.Add(playback);
        var timeline = new StackPanel { Spacing = 3 };
        mediaProgress = new() { Minimum = 0, Maximum = 1, Height = 3, Foreground = theme.Accent };
        mediaTime = new() { FontSize = 8, Foreground = theme.Secondary };
        timeline.Children.Add(mediaProgress); timeline.Children.Add(mediaTime); Grid.SetRow(timeline, 2); content.Children.Add(timeline);
        return Surface(content);
    }

    private async Task RefreshMediaAsync()
    {
        if (mediaBusy || !detached || closed || presentation.IsVisible != true || mediaTitle is null) return;
        mediaBusy = true;
        try
        {
            var state = await media.ReadAsync();
            if (closed || !detached || !presentation.IsVisible) return;
            mediaTitle.Text = state?.Title ?? "Nothing playing"; mediaArtist!.Text = state?.Artist ?? "No available media session";
            previousMedia!.IsEnabled = state?.Previous == true; toggleMedia!.IsEnabled = state?.Toggle == true; nextMedia!.IsEnabled = state?.Next == true;
            ((FontIcon)toggleMedia.Content).Glyph = state?.Playing == true ? "\uE769" : "\uE768";
            mediaProgress!.Value = state is { Duration.TotalSeconds: > 0 } ? Math.Clamp(state.Position.TotalSeconds / state.Duration.TotalSeconds, 0, 1) : 0;
            mediaTime!.Text = state is null ? "" : $"{state.Position:mm\\:ss} / {state.Duration:mm\\:ss}";
            var key = state is null ? "" : state.Title + "|" + state.Artist;
            if (key != artworkKey)
            {
                artworkKey = key; mediaArtwork!.Source = null;
                if (state?.Artwork is { } art)
                {
                    using var stream = await art.OpenReadAsync();
                    var image = new BitmapImage(); await image.SetSourceAsync(stream);
                    if (!closed && detached && artworkKey == key) mediaArtwork.Source = image;
                }
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException) { }
        finally { mediaBusy = false; }
    }

    private void RestoreDockModules()
    {
        panel.Children.Clear();
        foreach (var pair in sliderHomes)
        {
            detachedSliderHosts[pair.Key].Children.Remove(pair.Key);
            pair.Key.ValueChanged -= detachedFillHandlers[pair.Key];
            pair.Key.Opacity = 1;
            pair.Key.Orientation = Orientation.Horizontal; pair.Key.Width = pair.Key.Height = double.NaN;
            pair.Key.Resources.Remove("SliderTrackThemeHeight");
            pair.Key.Resources.Remove("SliderVerticalThumbWidth");
            pair.Key.Resources.Remove("SliderVerticalThumbHeight");
            pair.Key.CornerRadius = new(0);
            pair.Value.Parent.Children.Insert(pair.Value.Index, pair.Key);
        }
        detachedSliderHosts.Clear();
        detachedFillHandlers.Clear();
        foreach (var frame in tiles.Children.OfType<Border>())
        {
            var v = tileLayouts[frame]; var row = (Grid)frame.Child;
            var text = (StackPanel)row.Children[1]; var more = row.Children[2];
            text.Children.Clear(); row.Children.Clear();
            v.Caption.Children.Add(v.Button); v.Caption.Children.Add(v.Label); v.Caption.Children.Add(more);
            v.Stack.Children.Add(v.Caption); v.Stack.Children.Add(v.Detail); frame.Child = v.Stack;
            v.Label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        }
        foreach (var child in dockModules) panel.Children.Add(child);
        dockModules = [];
    }
}
