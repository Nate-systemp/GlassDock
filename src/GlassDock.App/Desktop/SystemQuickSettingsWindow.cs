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
    private const double PanelWidth = 400, PanelHeight = 324, Gutter = 16;
    private readonly WindowsSystemControlService controls;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new() { Background = Brush(0) };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly TextBlock networkDetail = Label("Network", 11, 155);
    private readonly TextBlock batteryDetail = Label("", 12, 190);
    private readonly TextBlock volumeValue = Label("", 13);
    private readonly Slider volumeSlider = new();
    private readonly Button networkTile;
    private readonly Button bluetoothTile;
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

    public SystemQuickSettingsWindow(WindowsSystemControlService controls, DockAppearanceSettings appearance)
    {
        this.controls = controls;
        Title = "GlassDock Quick Settings";
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        // Keep the desktop visible through the panel and let the GlassDock graph
        // provide the material depth. The stronger edge/highlight and lower fill
        // opacity make this read as glass rather than a dark blurred card.
        UtilityPopupStyle.Apply(glass, backdrop, appearance);
        SystemBackdrop = backdrop;
        root.Children.Add(glass);
        panel = new Grid { Margin = new Thickness(Gutter + 22, Gutter + 22, Gutter + 22, Gutter + 18), RowSpacing = 14 };
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(90) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tiles = new Grid { ColumnSpacing = 14 };
        for (var i = 0; i < 4; i++) tiles.ColumnDefinitions.Add(new ColumnDefinition());
        networkTile = AddTile(tiles, 0, "\uE701", "Wi-Fi", networkDetail,
            () => _ = ToggleRadioAsync(RadioKind.WiFi), () => _ = ShowWifiAsync());
        bluetoothTile = AddTile(tiles, 1, "\uE702", "Bluetooth", bluetoothDetail,
            () => _ = ToggleRadioAsync(RadioKind.Bluetooth), () => _ = ShowBluetoothAsync());
        AddTile(tiles, 2, "\uE708", "Focus", focusDetail, null,
            () => ShowInformation("Focus", "Focus is " + SystemRadioControls.FocusStatus() + ". Direct control requires restricted Windows API access.", controls.OpenFocusSettings));
        AddTile(tiles, 3, "\uE709", "Airplane", Label("Unsupported", 10, 155), null,
            () => ShowInformation("Airplane mode", "Windows does not expose a supported airplane-mode toggle here.", controls.OpenAirplaneSettings));
        panel.Children.Add(tiles);
        StyleSlider(volumeSlider, "System volume");
        volumeSlider.ValueChanged += (_, e) =>
        {
            if (syncing || closed) return;
            if (controls.SetMasterVolume(e.NewValue / 100)) volumeValue.Text = $"{Math.Round(e.NewValue)}%";
            else Refresh();
        };
        AddRow(panel, 1, SliderRow("\uE767", volumeSlider, volumeValue, () =>
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
        AddRow(panel, 2, SliderRow("\uE706", brightness, brightnessValue, null));
        AddRow(panel, 3, new Border { Height = 1, Background = Brush(22) });
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var battery = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        battery.Children.Add(Icon("\uE83F", 18));
        battery.Children.Add(batteryDetail);
        footer.Children.Add(battery);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Button(Icon("\uE713", 18), "Display details", () => ShowInformation("Display", brightness.IsEnabled ? "The slider controls the built-in display. External displays without this Windows brightness provider are unsupported." : "Direct brightness is unavailable for this display.", controls.OpenDisplaySettings), 36, 36));
        actions.Children.Add(Button(Icon("\uE7F4", 18), "Notifications", () => ShowInformation("Notifications", "Do not disturb status/control is not available through a public API here.", controls.OpenNotificationSettings), 36, 36));
        actions.Children.Add(Button(Icon("\uE712", 18), "Windows system tray settings", controls.OpenTraySettings, 36, 36));
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        AddRow(panel, 4, footer);
        root.Children.Add(panel);
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition());
        details.Children.Add(Button(Icon("\uE72B", 18), "Back to controls", () =>
        {
            detailRevision++;
            detailLifetime?.Cancel();
            details.Visibility = Visibility.Collapsed;
            panel.Visibility = Visibility.Visible;
        }, 36, 30));
        AddRow(details, 1, detailMessage);
        AddRow(details, 2, new ScrollViewer { Content = detailItems, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        root.Children.Add(details);
        Content = root;
        presentation = new UtilityPopupPresentation(this, root, backdrop);
        host = new InteractiveGlassWindowHost(WinRT.Interop.WindowNative.GetWindowHandle(this));
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        root.SizeChanged += (_, _) => UpdateBackdrop();
        root.Loaded += (_, _) =>
        {
            UpdateBackdrop();
            _ = RefreshHardwareAsync();

        };
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(1);
        refreshTimer.Tick += (_, _) => { Refresh(); if (DateTime.UtcNow >= nextHardwareRefresh) _ = RefreshHardwareAsync(); };
        Activated += (_, _) => { if (!closed) refreshTimer.Start(); };
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { e.Handled = true; Dismiss(); } };
        Closed += (_, _) => { closed = true; lifetime.Cancel(); detailLifetime?.Cancel(); refreshTimer.Stop(); host.Dispose(); };
    }

    private void UpdateBackdrop() => backdrop.SetBounds(root.ActualWidth, root.ActualHeight,
        Math.Max(0, root.ActualWidth - Gutter * 2), Math.Max(0, root.ActualHeight - Gutter * 2),
        Gutter, root.XamlRoot?.RasterizationScale ?? 1);

    public void ApplyAppearance(DockAppearanceSettings appearance) => UtilityPopupStyle.Apply(glass, backdrop, appearance);

    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(AppWindow owner, double scale, double anchorX, double dockTop)
    {
        UtilityPopupStyle.Position(AppWindow, owner, scale, anchorX, dockTop, PanelWidth, PanelHeight);
        presentation.AnchorX = (owner.Position.X + anchorX * scale - AppWindow.Position.X) / scale;
    }


    public void Refresh()
    {
        if (syncing || closed) return;
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
        if (closed || hardwareBusy) return;
        hardwareBusy = true;
        try
        {
            var wifi = await radios.ReadAsync(RadioKind.WiFi);
            var bluetooth = await radios.ReadAsync(RadioKind.Bluetooth);
            var level = brightnessBusy ? null : await DisplayBrightnessControl.ReadAsync();
            if (closed) return;
            networkDetail.Text = wifi.Text;
            bluetoothDetail.Text = bluetooth.Text;
            networkTile.Background = wifi.IsOn ? Brush(135, 20, 125, 215) : Brush(22);
            bluetoothTile.Background = bluetooth.IsOn ? Brush(135, 20, 125, 215) : Brush(22);
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
        if (closed) return;
        await RefreshHardwareAsync();
        if (closed) return;
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
                if (closed) return;
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
                detailItems.Children.Add(new TextBlock { Text = device.Name + " · " + (device.Connected ? "Connected" : "Paired"), FontSize = 13, Foreground = Brush(240), TextWrapping = TextWrapping.Wrap });
        }
        catch (Exception)
        {
            if (!closed && revision == detailRevision) detailMessage.Text = "Windows could not enumerate paired Bluetooth devices. Use the explicit Settings action.";
        }
    }

    private static Button DetailButton(string text, Action action)
    {
        var button = Button(new TextBlock { Text = text, FontSize = 13, Foreground = Brush(240), TextWrapping = TextWrapping.Wrap }, text, action, double.NaN, double.NaN);
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Padding = new Thickness(10, 8, 10, 8);
        return button;
    }

    private static TextBlock Label(string text, double size, byte alpha = 240) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(alpha), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
    };

    private static Button AddTile(Grid parent, int column, string glyph, string title, TextBlock detail, Action? action, Action secondary)
    {
        var stack = new StackPanel { Spacing = 5, HorizontalAlignment = HorizontalAlignment.Stretch };
        var button = Button(Icon(glyph, 21), "Toggle " + title, action ?? (() => { }), 48, 44);
        button.IsEnabled = action is not null;
        button.Background = Brush(22);
        button.CornerRadius = new CornerRadius(15);
        button.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(button);
        var label = Label(title, 13);
        label.TextAlignment = detail.TextAlignment = TextAlignment.Center;
        var caption = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        caption.Children.Add(label);
        caption.Children.Add(Button(Icon("\uE76C", 9), title + " details", secondary, 16, 20));
        stack.Children.Add(caption);
        stack.Children.Add(detail);
        Grid.SetColumn(stack, column);
        parent.Children.Add(stack);
        return button;
    }

    private static void StyleSlider(Slider slider, string name)
    {
        slider.Minimum = 0; slider.Maximum = 100; slider.StepFrequency = 1;
        slider.VerticalAlignment = VerticalAlignment.Center;
        slider.MinWidth = 0;
        slider.Resources["SliderTrackValueFill"] = Brush(255, 63, 174, 255);
        slider.Resources["SliderTrackValueFillPointerOver"] = Brush(255, 93, 192, 255);
        slider.Resources["SliderTrackFill"] = Brush(60);
        slider.Resources["SliderThumbBackground"] = new SolidColorBrush(Colors.White);
        slider.Resources["SliderThumbBackgroundPointerOver"] = new SolidColorBrush(Colors.White);
        slider.Resources["SliderThumbBackgroundPressed"] = Brush(255, 150, 220, 255);
        AutomationProperties.SetName(slider, name);
    }

    private static Border SliderRow(string glyph, Slider slider, TextBlock value, Action? action)
    {
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(9, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        if (action is not null) grid.Children.Add(Button(Icon(glyph, 19), "Mute or unmute", action, 28, 36));
        else grid.Children.Add(Icon(glyph, 19));
        Grid.SetColumn(slider, 1); grid.Children.Add(slider);
        value.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(value, 2); grid.Children.Add(value);
        return new Border { CornerRadius = new CornerRadius(14), Background = Brush(17), Child = grid };
    }

    private static void AddRow(Grid parent, int row, FrameworkElement child)
    {
        Grid.SetRow(child, row); parent.Children.Add(child);
    }
}
