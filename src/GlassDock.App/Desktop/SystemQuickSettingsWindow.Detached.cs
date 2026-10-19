using static GlassDock.App.Desktop.SystemControlStyle;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace GlassDock.App.Desktop;

internal sealed partial class SystemQuickSettingsWindow
{
    private readonly ControlCenterDepth depth = new();

    // Local retained palette: other utility windows keep their existing styling.
    private void ApplyControlCenterPalette(GlassDock.Core.Settings.DockAppearanceMode mode)
    {
        depth.Apply(mode);
        icons.Apply(mode);
        UpdateShortcutStates();
        var light = mode == GlassDock.Core.Settings.DockAppearanceMode.Light;
        var clear = mode == GlassDock.Core.Settings.DockAppearanceMode.Clear;
        if (!clear) return; // theme.Apply already restored Dark/Light/Frosted/Acrylic
        theme.Tile.Color = light ? global::Windows.UI.Color.FromArgb(45, 255, 252, 246)
            : global::Windows.UI.Color.FromArgb(clear ? (byte)10 : (byte)24, 255, 255, 255);
        theme.TileBorder.Color = light ? global::Windows.UI.Color.FromArgb(38, 70, 64, 60)
            : global::Windows.UI.Color.FromArgb(65, 244, 244, 250);
        theme.Track.Color = light ? global::Windows.UI.Color.FromArgb(38, 56, 52, 48)
            : global::Windows.UI.Color.FromArgb(clear ? (byte)22 : (byte)46, 224, 224, 232);
        theme.Secondary.Color = theme.Primary.Color with { A = 190 };
        if (tileLayouts.TryGetValue(nightFrame, out var night))
        {
            // Settings shortcut: Windows does not report a supported enabled state.
            night.Button.Background = detached
                ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                : depth.Icon;
            if (night.Button.Content is FontIcon icon)
                icon.Foreground = theme.Primary;
        }
    }

    private UIElement[] dockModules = [];
    private readonly Dictionary<Slider, (Panel Parent, int Index)> sliderHomes = new();
    private readonly Dictionary<Slider, Panel> detachedSliderHosts = new();
    private readonly Dictionary<Slider, ResourceDictionary> sliderResourceHomes = new();
    private readonly Dictionary<Slider, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler> detachedFillHandlers = new();
    private readonly WindowsMediaControls media = new();
    private TextBlock? mediaTitle, mediaArtist, detachedBattery;
    private Image? mediaArtwork;
    private Button? previousMedia, toggleMedia, nextMedia;
    private bool mediaBusy;
    private string? artworkKey;

    // The same Control Center content is used by Inside Dock, Floating, and
    // Split Utilities. Only the popup anchor differs between placements.
    private void BuildDetachedModules()
    {
        dockModules = panel.Children.ToArray();
        panel.Children.Clear();
        panel.RowDefinitions.Clear();
        foreach (var height in new[] { 22d, 108d, 40d, 80d, 48d, 48d, 24d })
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });
        panel.Margin = new Thickness(Gutter + 14);
        panel.RowSpacing = 12;
        var indexInRoot = root.Children.IndexOf(panel);
        root.Children.Remove(panel);
        panel.Margin = new Thickness(0);
        panel.Width = 266;
        panel.Height = 442;
        controlLayout = new Viewbox { Child = panel, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            Margin = new Thickness(Gutter + 14), VerticalAlignment = VerticalAlignment.Center };
        root.Children.Insert(indexInRoot, controlLayout);
        tiles.RowDefinitions.Clear();
        tiles.ColumnDefinitions.Clear();
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.ColumnDefinitions.Add(new ColumnDefinition());
        tiles.RowDefinitions.Add(new RowDefinition());
        tiles.RowDefinitions.Add(new RowDefinition());
        tiles.ColumnSpacing = 8;
        tiles.RowSpacing = 8;
        var slot = 0;
        foreach (var frame in tiles.Children.OfType<Border>())
        {
            var v = tileLayouts[frame];
            var more = v.Caption.Children.OfType<Button>().Last();
            v.Caption.Children.Clear(); v.Stack.Children.Clear();
            var name = v.Label.Text switch { "Wi-Fi" => "wifi", "Bluetooth" => "bluetooth", "Focus" => "focus", "Night Light" => "night-light", _ => "airplane" };
            var row = new Grid { ColumnSpacing = 3 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            v.Button.Width = v.Button.Height = 28;
            v.Button.MinWidth = v.Button.MinHeight = 0;
            v.Button.Padding = new Thickness(2);
            v.Button.CornerRadius = new CornerRadius(18);
            v.Button.Content = icons.Create(name, 23);
            MakeQuietButton(v.Button);
            v.Button.IsEnabledChanged += (_, _) => UpdateControlIcon(v.Button, name);
            row.Children.Add(v.Button);
            var text = new StackPanel { Spacing = name is "wifi" or "bluetooth" or "focus" ? 0 : 2, VerticalAlignment = VerticalAlignment.Center };
            v.Label.FontSize = 11.5; v.Label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            v.Detail.FontSize = 10; v.Detail.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            v.Label.MaxWidth = double.PositiveInfinity;
            v.Label.TextWrapping = TextWrapping.NoWrap;
            v.Label.TextAlignment = v.Detail.TextAlignment = TextAlignment.Left;
            v.Label.Visibility = v.Detail.Visibility = Visibility.Visible;
            if (name == "night-light") v.Detail.Visibility = Visibility.Collapsed;
            v.Label.TextTrimming = v.Detail.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(v.Label); text.Children.Add(v.Detail);
            Grid.SetColumn(text, 1); row.Children.Add(text);
            more.Content = icons.Create("expand", 10);
            more.Width = 12; more.Height = 36; more.MinWidth = more.MinHeight = 0;
            more.Padding = new Thickness(0); more.Visibility = Visibility.Visible;
            MakeQuietButton(more);
            Grid.SetColumn(more, 2); row.Children.Add(more);
            frame.Child = row; frame.Width = frame.Height = double.NaN;
            frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            frame.VerticalAlignment = VerticalAlignment.Stretch;
            frame.Padding = new Thickness(8, 5, 6, 5);
            frame.CornerRadius = new CornerRadius(25);
            depth.Raise(frame, interactive: true);
            if (frame != airplaneFrame)
            {
                Grid.SetRow(frame, slot / 2); Grid.SetColumn(frame, slot % 2); slot++;
                Grid.SetColumnSpan(frame, 1);
            }
            // The labels and empty card area perform the same action as the icon.
            frame.Tapped += (_, args) =>
            {
                for (DependencyObject? p = args.OriginalSource as DependencyObject; p is not null && p != frame; p = VisualTreeHelper.GetParent(p))
                    if (p is Button) return;
                if (v.Button == networkTile && networkTile.IsEnabled) _ = ToggleRadioAsync(global::Windows.Devices.Radios.RadioKind.WiFi);
                else if (v.Button == bluetoothTile && bluetoothTile.IsEnabled) _ = ToggleRadioAsync(global::Windows.Devices.Radios.RadioKind.Bluetooth);
                else if (name == "focus") _ = ToggleWindowsControlAsync(WindowsQuickControl.Focus);
                else if (name == "night-light") _ = ToggleWindowsControlAsync(WindowsQuickControl.NightLight);
            };
        }
        Grid.SetRow(tiles, 1); panel.Children.Add(tiles);
        var header = new Grid();
        header.Children.Add(new TextBlock { Text = "Control Center", FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = theme.Primary, VerticalAlignment = VerticalAlignment.Center });
        var settings = Button(icons.Create("settings", 18), "Utilities settings",
            () => UtilitiesSettingsRequested?.Invoke(), 30, 28);
        MakeQuietButton(settings); settings.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(settings); panel.Children.Add(header);

        var shortcuts = new Grid { ColumnSpacing = 6 };
        for (var i = 0; i < 3; i++) shortcuts.ColumnDefinitions.Add(new ColumnDefinition());
        void Shortcut(string asset, string title, Action action, int column)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var symbol = icons.Create(asset, 16);
            content.Children.Add(symbol);
            var caption = new StackPanel();
            var label = new TextBlock { Text = title, FontSize = 10, Foreground = theme.Primary };
            caption.Children.Add(label);
            content.Children.Add(caption);
            var b = Button(content, asset == "hotspot" ? "Toggle Hotspot" : asset == "airplane" ? "Toggle Airplane mode" : title + " details", action, double.NaN, double.NaN);
            MakeQuietButton(b); b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Center;
            var surface = Surface(b); surface.Padding = new Thickness(2);
            surface.CornerRadius = new CornerRadius(20);
            shortcutVisuals[asset] = (surface, b, symbol, label, title);
            Grid.SetColumn(surface, column); shortcuts.Children.Add(surface);
        }
        Shortcut("airplane", "Airplane", () => _ = ToggleWindowsControlAsync(WindowsQuickControl.Airplane), 0);
        Shortcut("hotspot", "Hotspot", () => _ = ToggleHotspotAsync(), 1);
        Shortcut("battery-saver", "Saver", () => ShowInformation("Energy saver",
            "Doky can read Energy saver status, but direct control is unavailable. Changing power plans would not toggle Energy saver.", controls.OpenBatterySettings), 2);
        Grid.SetRow(shortcuts, 2); panel.Children.Add(shortcuts);
        var mediaCard = BuildMediaCard(); Grid.SetRow(mediaCard, 3); panel.Children.Add(mediaCard);
        AddHorizontalSlider(brightness, "brightness", 4);
        AddHorizontalSlider(volumeSlider, "volume-high", 5);

        detachedBattery = new TextBlock { FontSize = 11, Foreground = theme.Primary,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis };
        batteryStatus = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center };
        batteryStatus.Children.Add(icons.Create("battery", 18)); batteryStatus.Children.Add(detachedBattery);
        var footerRow = new Grid(); footerRow.Children.Add(batteryStatus);
        var oldMenuButton = (Button)headingRow.Children.Last();
        var menuButton = Button(icons.Create("more", 18), "More system settings", () => { },
            30, 30);
        MakeQuietButton(menuButton); menuButton.HorizontalAlignment = HorizontalAlignment.Right;
        menuButton.Flyout = oldMenuButton.Flyout;
        footerRow.Children.Add(menuButton);
        Grid.SetRow(footerRow, 6); panel.Children.Add(footerRow);
        SetControlTypography(panel);
        UpdateTileContrast(networkTile, lastWifiOn); UpdateTileContrast(bluetoothTile, lastBluetoothOn);
        artworkKey = null;
    }

    private readonly ControlCenterIcons icons = new();
    private readonly Dictionary<string, (Border Surface, Button Button, Image Icon, TextBlock Label, string Title)> shortcutVisuals = new();
    private QuickControlStatus shortcutStatus = new(null, null);
    private readonly MobileHotspotControl hotspot = new();
    private bool hotspotBusy;
    private void ShowFocusUnavailable() => ShowInformation("Focus",
        "Focus session: " + SystemRadioControls.FocusStatus() + ". The primary button starts or stops a timed Focus session using Windows' controls and current duration. Do Not Disturb is a separate Windows setting.", controls.OpenFocusSettings);
    private void ShowNightLightUnavailable() => ShowInformation("Night Light",
        "The primary button operates Windows' Night Light switch. Settings may appear briefly. Status is unavailable when Windows does not expose its control.", controls.OpenNightLightSettings);
    private async Task ToggleHotspotAsync()
    {
        if (closed || hotspotBusy) return;
        if (windowsControlBusy) return;
        hotspotBusy = true;
        if (shortcutVisuals.TryGetValue("hotspot", out var view)) view.Button.IsEnabled = false;
        try
        {
            var result = await hotspot.ToggleAsync(lifetime.Token);
            if (closed) return;
            nextHardwareRefresh = DateTime.MinValue;
            await RefreshHardwareAsync();
            if (!closed && presentation.IsVisible && !result.Succeeded)
                ShowInformation("Hotspot", result.Message, controls.OpenHotspotSettings);
        }
        finally
        {
            hotspotBusy = false;
            if (!closed && shortcutVisuals.TryGetValue("hotspot", out view)) view.Button.IsEnabled = true;
        }
    }
    private void UpdateShortcutStates()
    {
        foreach (var (name, view) in shortcutVisuals)
        {
            bool? state = name switch { "battery-saver" => shortcutStatus.EnergySaver, "hotspot" => shortcutStatus.Hotspot, "airplane" => windowsControlState.Airplane, _ => null };
            var active = state == true;
            depth.SetActive(view.Surface, active);
            icons.Set(view.Icon, name, active, disabled: name == "airplane" && state is null);
            view.Label.Foreground = active && currentMode == DockAppearanceMode.Clear ? activeTitle : theme.Primary;
            var status = state is { } on ? on ? "On" : "Off" : "Status unavailable";
            var action = name == "hotspot" ? "Toggle Hotspot" : name == "airplane" ? "Toggle Airplane mode using Windows controls" : "Show control availability";
            ToolTipService.SetToolTip(view.Button, $"{view.Title}: {status}. {action}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(view.Button, status + ". " + action);
        }
    }
    private Viewbox? controlLayout;
    private StackPanel? batteryStatus;
    private Image? volumeSvg, brightnessSvg;
    private readonly SolidColorBrush activeTitle = new(Microsoft.UI.Colors.Black);
    private readonly SolidColorBrush activeSubtitle = new(global::Windows.UI.Color.FromArgb(255, 142, 142, 147));
    private void UpdateControlIcon(Button button, string name)
    {
        if (button.Content is not Image image) return;
        var state = name == "focus" ? windowsControlState.Focus : name == "night-light" ? windowsControlState.NightLight : (bool?)false;
        var active = button == networkTile ? lastWifiOn : button == bluetoothTile ? lastBluetoothOn : state == true;
        icons.Set(image, name, active, !button.IsEnabled || state is null);
    }
    private void MakeQuietButton(Button button)
    {
        var empty = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Background = empty; button.BorderThickness = new Thickness(0);
        button.Resources["ButtonBackground"] = empty;
        button.Resources["ButtonBackgroundPointerOver"] = depth.Hover;
        button.Resources["ButtonBackgroundPressed"] = depth.Pressed;
        button.Resources["ButtonBackgroundDisabled"] = empty;
        foreach (var key in new[] { "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed", "ButtonBorderBrushDisabled" }) button.Resources[key] = empty;
    }
    private static void SetControlTypography(DependencyObject parent)
    {
        if (parent is TextBlock text) text.FontFamily = new FontFamily("Segoe UI Variable");
        if (parent is Control control) control.FontFamily = new FontFamily("Segoe UI Variable");
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) SetControlTypography(VisualTreeHelper.GetChild(parent, i));
    }

    private Border Surface(UIElement child) => RaisedSurface(child);

    private Border RaisedSurface(UIElement child)
    {
        var card = new Border()
        {
            Child = child,
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.75),
            CornerRadius = new CornerRadius(34),
            Padding = new Thickness(14)
        };
        depth.Raise(card);
        return card;
    }

    private void AddHorizontalSlider(Slider slider, string glyph, int row)
    {
        var home = sliderHomes[slider];
        home.Parent.Children.Remove(slider);
        slider.Orientation = Orientation.Horizontal;
        slider.Opacity = 1;
        slider.Width = double.NaN; slider.Height = 26;
        slider.CornerRadius = new CornerRadius(1.5);
        // The default template keeps a taller minimum and pre/post track rows;
        // forcing only Height clips the bottom of its enlarged thumb.
        // Build a fresh dictionary off-tree. Replacing aliased entries in the live
        // WinUI dictionary throws 0x800F0902 (SliderThumbBackgroundPressed).
        sliderResourceHomes.TryAdd(slider, slider.Resources);
        var resources = new ResourceDictionary();
        resources["SliderHorizontalHeight"] = 26d;
        resources["SliderPreContentMargin"] = 11.5d;
        resources["SliderPostContentMargin"] = 11.5d;
        resources["SliderThumbCornerRadius"] = new CornerRadius(9);
        resources["SliderTrackThemeHeight"] = 3d;
        resources["SliderThumbBackground"] = theme.Primary;
        resources["SliderThumbBackgroundPointerOver"] = theme.Primary;
        resources["SliderThumbBackgroundPressed"] = theme.Primary;
        resources["SliderThumbBackgroundDisabled"] = theme.Primary;
        resources["SliderOuterThumbBackground"] = theme.Primary;
        resources["SliderThumbBorderBrush"] = depth.Edge;
        resources["SliderHorizontalThumbWidth"] = 14d;
        resources["SliderHorizontalThumbHeight"] = 14d;
        resources["SliderTrackFill"] = depth.Track;
        resources["SliderTrackFillPointerOver"] = depth.Track;
        resources["SliderTrackFillPressed"] = depth.Track;
        resources["SliderTrackFillDisabled"] = depth.Track;
        resources["SliderTrackValueFill"] = depth.Fill;
        resources["SliderTrackValueFillPointerOver"] = depth.Fill;
        resources["SliderTrackValueFillPressed"] = depth.Fill;
        resources["SliderTrackValueFillDisabled"] = depth.Fill;
        slider.Resources = resources;
        var track = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        track.ColumnDefinitions.Add(new ColumnDefinition());
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        var symbol = icons.Create(glyph, 16);
        if (slider == volumeSlider)
        {
            volumeSvg = symbol;
            var mute = Button(symbol, "Mute or unmute", () => { controls.SetMuted(!controls.GetSnapshot().Muted); Refresh(); },
                24, 26);
            mute.MinWidth = mute.MinHeight = 0; mute.Padding = new Thickness(0); MakeQuietButton(mute);
            track.Children.Add(mute);
        }
        else { brightnessSvg = symbol; track.Children.Add(symbol); }
        Grid.SetColumn(slider, 1); track.Children.Add(slider);
        var end = new TextBlock { Text = slider.IsEnabled ? $"{Math.Round(slider.Value)}%" : "—",
            FontSize = 12,
            Foreground = theme.Secondary, FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventHandler handler = (_, args) => end.Text = slider.IsEnabled ? $"{Math.Round(args.NewValue)}%" : "—";
        slider.ValueChanged += handler; detachedFillHandlers[slider] = handler;
        slider.IsEnabledChanged += (_, _) => end.Text = slider.IsEnabled ? $"{Math.Round(slider.Value)}%" : "—";
        Grid.SetColumn(end, 2); track.Children.Add(end);
        detachedSliderHosts[slider] = track;
        var card = Surface(track);
        card.CornerRadius = new CornerRadius(24);
        card.Padding = new Thickness(15, 3, 15, 3);
        Grid.SetRow(card, row); panel.Children.Add(card);
    }

    private Border BuildMediaCard()
    {
        var content = new Grid { RowSpacing = 6 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        var info = new Grid { ColumnSpacing = 10 };
        info.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        info.ColumnDefinitions.Add(new ColumnDefinition());
        var artwork = new Grid { Width = 26, Height = 26 };
        artwork.Children.Add(icons.Create("music", 22));
        mediaArtwork = new Image { Width = 26, Height = 26, Stretch = Stretch.UniformToFill };
        artwork.Children.Add(mediaArtwork); info.Children.Add(artwork);
        var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        mediaTitle = new TextBlock { Text = "Nothing playing", FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = theme.Primary, MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        mediaArtist = new TextBlock { Text = "No active media session", FontSize = 10, Foreground = theme.Secondary,
            MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };
        labels.Children.Add(mediaTitle); labels.Children.Add(mediaArtist);
        Grid.SetColumn(labels, 1); info.Children.Add(labels); content.Children.Add(info);
        var playback = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Button Action(string asset, string name, int action, double size)
        {
            var b = Button(icons.Create(asset, size), name, () => { },
                32, 30);
            b.Padding = new Thickness(0); b.CornerRadius = new CornerRadius(22); b.MinWidth = b.MinHeight = 0;
            MakeQuietButton(b); b.IsEnabled = false;
            b.Click += async (_, _) => { await media.ExecuteAsync(action); await RefreshMediaAsync(); };
            playback.Children.Add(b);
            return b;
        }
        previousMedia = Action("previous", "Previous track", -1, 14);
        toggleMedia = Action("play", "Play or pause", 0, 20);
        nextMedia = Action("next", "Next track", 1, 14);
        Grid.SetRow(playback, 1); content.Children.Add(playback);
        var surface = Surface(content);
        surface.Padding = new Thickness(12, 8, 12, 8);
        surface.CornerRadius = new CornerRadius(24);
        return surface;
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
            icons.Set((Image)previousMedia.Content, "previous", disabled: !previousMedia.IsEnabled);
            icons.Set((Image)toggleMedia.Content, state?.Playing == true ? "pause" : "play", disabled: !toggleMedia.IsEnabled);
            icons.Set((Image)nextMedia.Content, "next", disabled: !nextMedia.IsEnabled);
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
        if (controlLayout is not null)
        {
            var index = root.Children.IndexOf(controlLayout);
            controlLayout.Child = null; root.Children.Remove(controlLayout);
            root.Children.Insert(index, panel); controlLayout = null;
            panel.Width = panel.Height = double.NaN;
        }
        panel.Children.Clear();
        shortcutVisuals.Clear();
        foreach (var pair in sliderHomes)
        {
            detachedSliderHosts[pair.Key].Children.Remove(pair.Key);
            if (detachedFillHandlers.TryGetValue(pair.Key, out var handler)) pair.Key.ValueChanged -= handler;
            pair.Key.Opacity = 1;
            pair.Key.Orientation = Orientation.Horizontal; pair.Key.Width = pair.Key.Height = double.NaN;
            pair.Key.Resources = sliderResourceHomes[pair.Key];
            pair.Key.CornerRadius = new(0);
            pair.Value.Parent.Children.Insert(pair.Value.Index, pair.Key);
        }
        detachedSliderHosts.Clear(); sliderResourceHomes.Clear();
        detachedFillHandlers.Clear();
        foreach (var frame in tiles.Children.OfType<Border>())
        {
            var v = tileLayouts[frame]; var row = (Grid)frame.Child;
            var text = (StackPanel)row.Children[1]; var more = row.Children[2];
            text.Children.Clear(); row.Children.Clear();
            more.Visibility = Visibility.Visible;
            frame.Width = frame.Height = double.NaN;
            frame.HorizontalAlignment = HorizontalAlignment.Stretch;
            frame.VerticalAlignment = VerticalAlignment.Stretch;
            v.Caption.Children.Add(v.Button); v.Caption.Children.Add(v.Label); v.Caption.Children.Add(more);
            v.Button.Content = tileVisuals[v.Button].Glyph;
            v.Stack.Children.Add(v.Caption); v.Stack.Children.Add(v.Detail); frame.Child = v.Stack;
            v.Label.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            v.Label.Visibility = v.Detail.Visibility = Visibility.Visible;
        }
        foreach (var child in dockModules) panel.Children.Add(child);
        dockModules = [];
    }
}
