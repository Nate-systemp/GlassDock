using GlassDock.Core.Settings;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Devices.Radios;
using static GlassDock.App.Desktop.SystemControlStyle;

namespace GlassDock.App.Desktop;

internal sealed class SystemQuickSettingsWindow : Window
{
    private const double PanelWidth = 474, PanelHeight = 336, Gutter = 16;
    private readonly WindowsSystemControlService controls;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new() { Background = Brush(0) };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly TextBlock networkDetail = Label("Network", 11, 155);
    private readonly TextBlock batteryDetail = Label("", 12, 190);
    private readonly TextBlock volumeValue = Label("", 13);
    private readonly Slider volumeSlider = new();
    private readonly Button networkTile;
    private readonly Button bluetoothTile;
    private Border networkFrame = null!, bluetoothFrame = null!;
    private readonly Dictionary<Button, (IconElement Glyph, TextBlock Title, TextBlock Detail, IconElement Chevron)> tileVisuals = new();
    private bool lastWifiOn, lastBluetoothOn;
    private readonly TextBlock bluetoothDetail = Label("Checking…", 11, 155);
    private readonly TextBlock focusDetail = Label("Checking…", 11, 155);
    private readonly Slider brightness = new();
    private readonly TextBlock brightnessValue = Label("—", 13, 150);
    private readonly SystemRadioControls radios = new();
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? detailLifetime;
    private readonly Grid panel;
    private readonly Grid details = new() { Margin = new Thickness(Gutter + 22), Visibility = Visibility.Collapsed, RowSpacing = 12 };
    private readonly StackPanel detailItems = new() { Spacing = 8 };
    private readonly TextBlock detailMessage = new() { FontSize = 12, Foreground = Brush(190), TextWrapping = TextWrapping.Wrap };
    private int detailRevision;
    private bool hardwareBusy, radioBusy, brightnessBusy, wifiConnecting;
    private int? pendingBrightness;
    private DateTime nextHardwareRefresh;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer refreshTimer;
    private bool syncing;
    private readonly UtilityPopupPresentation presentation;
    private bool closed;

    public SystemQuickSettingsWindow(WindowsSystemControlService controls, DockAppearanceSettings appearance,
        DockAppearanceMode dockMode, Func<bool>? utilityOwnsPointer = null)
    {
        this.controls = controls;
        Title = "Doky Quick Settings";
        WindowBranding.Apply(this);
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, dockMode);
        root.Children.Add(glass);
        root.Children.Add(new Border
        {
            Margin = new Thickness(Gutter),
            CornerRadius = new CornerRadius(28),
            Background = theme.Overlay,
            IsHitTestVisible = false
        });
        panel = new Grid
        {
            Margin = new Thickness(Gutter + 18, Gutter + 18, Gutter + 18, Gutter + 17),
            RowSpacing = 10
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(108) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(50) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tiles = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        networkTile = AddTile(tiles, 0, "\uE701", "Wi-Fi", networkDetail,
            () => _ = ToggleRadioAsync(RadioKind.WiFi), () => _ = ShowWifiAsync(), out networkFrame);
        bluetoothTile = AddTile(tiles, 1, "\uE702", "Bluetooth", bluetoothDetail,
            () => _ = ToggleRadioAsync(RadioKind.Bluetooth), () => _ = ShowBluetoothAsync(), out bluetoothFrame);
        AddTile(tiles, 2, "\uE708", "Focus", focusDetail, null,
            () => ShowInformation("Focus", "Focus is " + SystemRadioControls.FocusStatus() + ". Direct control requires restricted Windows API access.", controls.OpenFocusSettings), out _);
        AddTile(tiles, 3, "\uE709", "Airplane", Label("Unsupported", 10, 155), null,
            () => ShowInformation("Airplane mode", "Windows does not expose a supported airplane-mode toggle here.", controls.OpenAirplaneSettings), out _);
        panel.Children.Add(tiles);
        StyleSlider(volumeSlider, "System volume");
        volumeSlider.ValueChanged += (_, e) =>
        {
            if (syncing || closed || presentation?.IsVisible != true) return;
            if (controls.SetMasterVolume(e.NewValue / 100)) volumeValue.Text = $"{Math.Round(e.NewValue)}%";
            else Refresh();
        };
        AddRow(panel, 2, SliderRow("\uE767", volumeSlider, volumeValue, () =>
        {
            controls.SetMuted(!controls.GetSnapshot().Muted);
            Refresh();
        }));
        StyleSlider(brightness, "Built-in display brightness");
        brightness.IsEnabled = false;
        brightness.ValueChanged += (_, e) =>
        {
            if (syncing || closed || !brightness.IsEnabled) return;
            pendingBrightness = (int)Math.Round(e.NewValue);
            _ = ApplyBrightnessAsync();
        };
        AddRow(panel, 1, SliderRow("\uE706", brightness, brightnessValue, null));
        AddRow(panel, 3, new Border { Height = 1, Background = theme.Divider });
        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var battery = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center
        };
        var batteryIcon = Icon("\uE83F", 16);
        batteryIcon.Foreground = theme.Primary;
        batteryDetail.Foreground = theme.Secondary;
        battery.Children.Add(batteryIcon);
        battery.Children.Add(batteryDetail);
        footer.Children.Add(battery);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6, VerticalAlignment = VerticalAlignment.Center
        };
        actions.Children.Add(ActionChip("\uE713", "Display", () => ShowInformation("Display", brightness.IsEnabled
            ? "The slider controls the built-in display. External displays without this Windows brightness provider are unsupported."
            : "Direct brightness is unavailable for this display.", controls.OpenDisplaySettings)));
        actions.Children.Add(ActionChip("\uE7F4", "Alerts", () => ShowInformation("Notifications",
            "Do not disturb status/control is not available through a public API here.", controls.OpenNotificationSettings)));
        actions.Children.Add(ActionChip("\uE712", "Settings", controls.OpenTraySettings));
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        AddRow(panel, 4, footer);
        root.Children.Add(panel);
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition());
        var backGlyph = Icon("\uE72B", 18);
        backGlyph.Foreground = theme.Primary;
        details.Children.Add(Button(backGlyph, "Back to controls", () =>
        {
            detailRevision++;
            detailLifetime?.Cancel();
            details.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
        }, 36, 30));
        AddRow(details, 1, detailMessage);
        AddRow(details, 2, new ScrollViewer { Content = detailItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        root.Children.Add(details);
        networkDetail.Foreground = theme.Secondary;
        bluetoothDetail.Foreground = theme.Secondary;
        focusDetail.Foreground = theme.Secondary;
        volumeValue.Foreground = theme.Secondary;
        brightnessValue.Foreground = theme.Secondary;
        detailMessage.Foreground = theme.Secondary;
        Content = root;
        presentation = new UtilityPopupPresentation(this, root, backdrop, utilityOwnsPointer);
        host = new InteractiveGlassWindowHost(WinRT.Interop.WindowNative.GetWindowHandle(this))
        {
            EnableHostBackdropBrush = true,
            UseDockLayeredTransparency = true
        };
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        SystemBackdrop = backdrop;
        liquid = new(this, root, backdrop);
        ApplyAppearance(appearance, dockMode);
        root.SizeChanged += (_, _) => UpdateBackdrop();
        root.Loaded += (_, _) =>
        {
            UpdateBackdrop();
            _ = RefreshHardwareAsync();

        };
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += (_, _) => { Refresh(); if (DateTime.UtcNow >= nextHardwareRefresh) _ = RefreshHardwareAsync(); };
        presentation.Hidden += (_, _) => { refreshTimer.Stop(); detailRevision++; detailLifetime?.Cancel(); };
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; Dismiss(); } };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); detailLifetime?.Cancel(); refreshTimer.Stop(); detailLifetime?.Dispose(); lifetime.Dispose(); host.Dispose(); };
    }

    private void UpdateBackdrop() => presentation.UpdateBackdropBounds();

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
        UpdateTileContrast(networkTile, lastWifiOn);
        UpdateTileContrast(bluetoothTile, lastBluetoothOn);
        // Resource brushes track the live palette; changing the appearance also
        // refreshes the two tiles whose on/off colors depend on system state.
        if (presentation?.IsVisible == true) _ = RefreshHardwareAsync();
    }

    public event EventHandler? Dismissed
    {
        add => presentation.Dismissed += value;
        remove => presentation.Dismissed -= value;
    }
    public event EventHandler? Hidden { add => presentation.Hidden += value; remove => presentation.Hidden -= value; }
    public void HideImmediately() => presentation.HideImmediately();
    public void RetargetClosed() => presentation.RetargetClosed();
    public void Present() { presentation.Present(); refreshTimer.Start(); Refresh(); if (DateTime.UtcNow >= nextHardwareRefresh) _ = RefreshHardwareAsync(); }
    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(
        AppWindow owner,
        double scale,
        double anchorX,
        double anchorY,
        double dockTop)
    {
        UtilityPopupStyle.Position(AppWindow, owner, scale, anchorX, dockTop, PanelWidth, PanelHeight);
        presentation.SetTargetWindowGeometry(
            owner.Position.X + anchorX * scale,
            owner.Position.Y + anchorY * scale,
            scale);
        host.InputHeightPixels = presentation.InputHeightPixels;
    }


    public void Refresh()
    {
        if (syncing || closed || presentation?.IsVisible != true) return;
        syncing = true;
        try
        {
            var snapshot = controls.GetSnapshot();
            batteryDetail.Text = snapshot.HasBattery ? $"{snapshot.BatteryPercent}%{(snapshot.PluggedIn ? " · Charging" : "")}" : "AC power";
            volumeValue.Text = snapshot.Muted ? "Muted" : $"{snapshot.VolumePercent}%";
            volumeSlider.Value = snapshot.VolumePercent;
        }
        finally { syncing = false; }
    }

    private async Task RefreshHardwareAsync()
    {
        if (closed || presentation?.IsVisible != true || hardwareBusy) return;
        hardwareBusy = true;
        try
        {
            var wifi = await radios.ReadAsync(RadioKind.WiFi);
            var bluetooth = await radios.ReadAsync(RadioKind.Bluetooth);
            var level = brightnessBusy ? null : await DisplayBrightnessControl.ReadAsync();
            if (closed || presentation?.IsVisible != true) return;
            networkDetail.Text = wifi.Text;
            bluetoothDetail.Text = bluetooth.Text;
            lastWifiOn = wifi.IsOn;
            lastBluetoothOn = bluetooth.IsOn;
            networkFrame.Background = wifi.IsOn ? theme.AccentFill : theme.Tile;
            bluetoothFrame.Background = bluetooth.IsOn ? theme.AccentFill : theme.Tile;
            UpdateTileContrast(networkTile, wifi.IsOn);
            UpdateTileContrast(bluetoothTile, bluetooth.IsOn);
            networkTile.IsEnabled = wifi.Available && !radioBusy;
            bluetoothTile.IsEnabled = bluetooth.Available && !radioBusy;
            focusDetail.Text = SystemRadioControls.FocusStatus();
            if (!brightnessBusy)
            {
                syncing = true;
                brightness.IsEnabled = level.HasValue;
                brightnessValue.Text = level is { } value ? $"{value}%" : "—";
                if (level.HasValue) brightness.Value = level.Value;
                ToolTipService.SetToolTip(brightness, level.HasValue ? "Built-in display brightness" : "Direct brightness unsupported");
                syncing = false;
            }
        }
        finally { hardwareBusy = false; nextHardwareRefresh = DateTime.UtcNow.AddSeconds(5); }
    }

    private async Task ToggleRadioAsync(RadioKind kind)
    {
        if (closed || radioBusy) return;
        radioBusy = true;
        networkTile.IsEnabled = bluetoothTile.IsEnabled = false;
        var message = await radios.ToggleAsync(kind, lifetime.Token);
        radioBusy = false;
        if (closed || presentation?.IsVisible != true) return;
        await RefreshHardwareAsync();
        if (closed || presentation?.IsVisible != true) return;
        ToolTipService.SetToolTip(kind == RadioKind.WiFi ? networkTile : bluetoothTile, message);
        if (!message.StartsWith("Change requested", StringComparison.Ordinal))
            ShowInformation(kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth", message,
                kind == RadioKind.WiFi ? controls.OpenNetworkSettings : controls.OpenBluetoothSettings);
    }

    private async Task ApplyBrightnessAsync()
    {
        if (brightnessBusy || closed) return;
        brightnessBusy = true;
        try
        {
            while (!closed && pendingBrightness is { } value)
            {
                pendingBrightness = null;
                var result = await DisplayBrightnessControl.SetAsync(value);
                if (closed || presentation?.IsVisible != true) return;
                if (result is null)
                {
                    brightness.IsEnabled = false;
                    brightnessValue.Text = "—";
                    pendingBrightness = null;
                    break;
                }
                brightnessValue.Text = $"{result}%";
            }
        }
        finally { brightnessBusy = false; }
    }

    private int BeginDetails(string title, Action settings, Action? refresh = null)
    {
        detailLifetime?.Cancel();
        detailLifetime?.Dispose();
        detailLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        detailRevision++;
        panel.Visibility = Visibility.Collapsed;
        details.Visibility = Visibility.Visible;
        detailItems.Children.Clear();
        detailMessage.Text = title;
        if (refresh is not null) detailItems.Children.Add(DetailButton("Refresh", refresh));
        detailItems.Children.Add(DetailButton("Open " + title + " settings", settings));
        return detailRevision;
    }

    private void ShowInformation(string title, string message, Action settings)
    {
        BeginDetails(title, settings);
        detailMessage.Text = title + "\n" + message;
    }

    private async Task ShowWifiAsync(bool scan = true)
    {
        var revision = BeginDetails("Wi-Fi", controls.OpenNetworkSettings, () => _ = ShowWifiAsync());
        var cancellation = detailLifetime!.Token;
        detailMessage.Text = "Wi-Fi · Reading available networks…";
        try
        {
            var networks = await WifiNetworkControl.ReadAsync(scan, cancellation);
            if (closed || revision != detailRevision) return;
            detailMessage.Text = networks.Count == 0 ? "Wi-Fi · No networks available. Check Wi-Fi is on, then Refresh." :
                "Wi-Fi · Select a saved or open network to connect. Refresh updates scan results.";
            foreach (var network in networks)
            {
                var text = $"{network.Name}  ·  {network.Signal}%\n" + (network.Connected ? "Connected" :
                    network.CanConnect ? network.Secured ? "Saved · Connect" : "Open · Connect" : "Needs setup in Settings");
                var button = DetailButton(text, () => _ = ConnectWifiAsync(network, revision));
                button.IsEnabled = !network.Connected;
                detailItems.Children.Add(button);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!closed && revision == detailRevision) detailMessage.Text = WifiNetworkControl.ErrorMessage(error);
        }
    }

    private async Task ConnectWifiAsync(WifiNetwork network, int revision)
    {
        if (closed || wifiConnecting || revision != detailRevision) return;
        if (!network.CanConnect)
        {
            detailMessage.Text = "This network needs credentials or setup. Choose Open Wi-Fi settings explicitly.";
            return;
        }
        wifiConnecting = true;
        foreach (var button in detailItems.Children.OfType<Button>()) button.IsEnabled = false;
        detailMessage.Text = "Connecting to " + network.Name + "…";
        try
        {
            var message = await WifiNetworkControl.ConnectAsync(network, detailLifetime!.Token);
            if (closed || revision != detailRevision) return;
            await ShowWifiAsync(false);
            if (!closed && detailRevision == revision + 1) detailMessage.Text = message;
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!closed && revision == detailRevision) detailMessage.Text = WifiNetworkControl.ErrorMessage(error);
        }
        finally
        {
            wifiConnecting = false;
            if (!closed && revision == detailRevision)
                foreach (var button in detailItems.Children.OfType<Button>()) button.IsEnabled = true;
        }
    }

    private async Task ShowBluetoothAsync()
    {
        var revision = BeginDetails("Bluetooth", controls.OpenBluetoothSettings, () => _ = ShowBluetoothAsync());
        detailMessage.Text = "Bluetooth · Reading paired devices…";
        try
        {
            var devices = await radios.ReadPairedAsync();
            if (closed || revision != detailRevision) return;
            detailMessage.Text = devices.Count == 0 ? "Bluetooth · No paired devices available." : "Bluetooth · Paired devices. Pairing and device-specific controls are available in Settings.";
            foreach (var device in devices)
                detailItems.Children.Add(new TextBlock { Text = device.Name + " · " + (device.Connected ? "Connected" : "Paired"), FontSize = 13, Foreground = theme.Primary, TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception)
        {
            if (!closed && revision == detailRevision) detailMessage.Text = "Windows could not enumerate paired Bluetooth devices. Use the explicit Settings action.";
        }
    }

    private Button DetailButton(string text, Action action)
    {
        var button = Button(new TextBlock
        {
            Text = text, FontSize = 13,
            Foreground = theme.Primary, TextWrapping = TextWrapping.Wrap
        }, text, action, double.NaN, double.NaN);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Background = theme.Tile;
        button.BorderBrush = theme.TileBorder;
        button.BorderThickness = new Thickness(.7);
        button.CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius);
        button.Padding = new Thickness(12, 9, 12, 9);
        theme.StyleButton(button);
        return button;
    }

    private static TextBlock Label(string text, double size, byte alpha = 240) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(alpha),
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
    };

    private Button AddTile(Grid parent, int column, string glyph, string title,
        TextBlock detail, Action? action, Action secondary, out Border frame)
    {
        var stack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var glyphControl = Icon(glyph, 22);
        glyphControl.Foreground = theme.Primary;
        // Unsupported radio controls still have a useful details/settings
        // action. Leave their glyphs readable instead of WinUI dimming them
        // into invisible disabled icons against Dark or Clear glass.
        var button = Button(glyphControl,
            action is null ? title + " details" : "Toggle " + title,
            action ?? secondary, 43, 39);
        button.Background = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        button.CornerRadius = new CornerRadius(12);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        theme.StyleButton(button);
        stack.Children.Add(button);
        var label = Label(title, 12.5);
        label.Foreground = theme.Primary;
        label.TextAlignment = detail.TextAlignment = TextAlignment.Center;
        detail.Foreground = theme.Secondary;
        detail.FontSize = 10.5;
        var caption = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 1
        };
        caption.Children.Add(label);
        var moreGlyph = Icon("\uE76C", 9);
        moreGlyph.Foreground = theme.Muted;
        var more = Button(moreGlyph, title + " details", secondary, 17, 18);
        more.Background = new SolidColorBrush(Colors.Transparent);
        more.BorderThickness = new Thickness(0);
        more.Padding = new Thickness(0);
        theme.StyleButton(more);
        caption.Children.Add(more);
        stack.Children.Add(caption);
        stack.Children.Add(detail);
        frame = new Border
        {
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(3, 6, 3, 6),
            Child = stack
        };
        tileVisuals[button] = (glyphControl, label, detail, moreGlyph);
        Grid.SetColumn(frame, column);
        parent.Children.Add(frame);
        return button;
    }

    private void UpdateTileContrast(Button tile, bool active)
    {
        if (!tileVisuals.TryGetValue(tile, out var visuals)) return;
        var foreground = active ? theme.AccentText : theme.Primary;
        visuals.Glyph.Foreground = foreground;
        visuals.Title.Foreground = foreground;
        visuals.Detail.Foreground = active ? theme.AccentText : theme.Secondary;
        visuals.Chevron.Foreground = active ? theme.AccentText : theme.Muted;
    }

    private Button ActionChip(string glyph, string text, Action action)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center
        };
        var icon = Icon(glyph, 14);
        icon.Foreground = theme.Primary;
        row.Children.Add(icon);
        row.Children.Add(new TextBlock
        {
            Text = text, FontSize = 11.5,
            Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center
        });
        var button = Button(row, text, action, double.NaN, 36);
        button.Padding = new Thickness(9, 4, 9, 4);
        button.Background = theme.Tile;
        button.BorderBrush = theme.TileBorder;
        button.BorderThickness = new Thickness(.7);
        button.CornerRadius = new CornerRadius(12);
        theme.StyleButton(button);
        return button;
    }

    private void StyleSlider(Slider slider, string name)
    {
        slider.Minimum = 0;
        slider.Maximum = 100;
        slider.StepFrequency = 1;
        slider.VerticalAlignment = VerticalAlignment.Center;
        slider.MinWidth = 0;
        slider.Resources["SliderTrackValueFill"] = theme.Accent;
        slider.Resources["SliderTrackValueFillPointerOver"] = theme.Accent;
        slider.Resources["SliderTrackFill"] = theme.Track;
        slider.Resources["SliderThumbBackground"] = new SolidColorBrush(Colors.White);
        slider.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(Colors.White);
        slider.Resources["SliderThumbBackgroundPressed"] = theme.Accent;
        AutomationProperties.SetName(slider, name);
    }

    private Border SliderRow(string glyph, Slider slider, TextBlock value, Action? action)
    {
        var grid = new Grid
        {
            ColumnSpacing = 10, Padding = new Thickness(10, 0, 12, 0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(43) });
        var icon = Icon(glyph, 19);
        icon.Foreground = theme.Primary;
        if (action is not null)
        {
            var mute = Button(icon, "Mute or unmute", action, 28, 36);
            mute.Background = new SolidColorBrush(Colors.Transparent);
            mute.BorderThickness = new Thickness(0);
            theme.StyleButton(mute);
            grid.Children.Add(mute);
        }
        else grid.Children.Add(icon);
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        value.Foreground = theme.Primary;
        value.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);
        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            Child = grid
        };
    }

    private static void AddRow(Grid parent, int row, FrameworkElement child)
    {
        Grid.SetRow(child, row); parent.Children.Add(child);
    }
}
