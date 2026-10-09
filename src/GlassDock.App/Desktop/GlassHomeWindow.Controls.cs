using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Devices.Radios;

namespace GlassDock.App.Desktop;

internal sealed partial class GlassHomeWindow
{
    private readonly WindowsSystemControlService systemControls;
    private readonly SystemRadioControls radios = new();
    private readonly Slider volume = new() { Minimum = 0, Maximum = 100, Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Slider brightness = new() { Minimum = 0, Maximum = 100, Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private TextBlock volumeLabel = null!, brightnessLabel = null!, wifiStatus = null!, bluetoothStatus = null!, focusStatus = null!;
    private Button wifiButton = null!, bluetoothButton = null!;
    private CancellationTokenSource? controlCancellation;
    private bool refreshingControls, changingControlValues, radioBusy, brightnessWriting, muted, dialogOpen;
    private int? pendingBrightness;

    private Grid CreateQuickControls()
    {
        var grid = new Grid { RowSpacing = 8, ColumnSpacing = 8 };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
        grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
        (FrameworkElement Host, Button Main, TextBlock Status) Tile(string title, string glyph, Action action, string uri)
        {
            var tile = new Grid { Background = theme.Tile, CornerRadius = new(16), BorderBrush = theme.TileBorder, BorderThickness = new(.5) };
            tile.ColumnDefinitions.Add(new()); tile.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
            var content = new Grid { ColumnSpacing = 8 };
            content.ColumnDefinitions.Add(new() { Width = new GridLength(34) }); content.ColumnDefinitions.Add(new());
            content.Children.Add(new Border { Height = 38, CornerRadius = new(11), Background = theme.Hover,
                Child = new FontIcon { Glyph = glyph, FontSize = 23, Foreground = theme.Primary } });
            var labels = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Label(title, 12)); var status = Label("Reading…", 10, true); labels.Children.Add(status);
            Grid.SetColumn(labels, 1); content.Children.Add(labels);
            var main = new Button { Content = content, Padding = new(8), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), MinWidth = 0, MinHeight = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            theme.StyleButton(main); main.Click += (_, _) => action(); AutomationProperties.SetName(main, title);
            tile.Children.Add(main);
            var secondary = ActionButton("", "\uE76C", () => OpenSettingsUri(uri)); secondary.Padding = new(2); secondary.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent); secondary.BorderThickness = new(0);
            secondary.VerticalAlignment = VerticalAlignment.Stretch; AutomationProperties.SetName(secondary, "Open " + title + " settings");
            ToolTipService.SetToolTip(secondary, "Open " + title + " settings");
            Grid.SetColumn(secondary, 1); tile.Children.Add(secondary);
            return (tile, main, status);
        }
        var wifi = Tile("Wi-Fi", "\uE701", () => _ = ToggleRadioAsync(RadioKind.WiFi), "ms-settings:network-wifi");
        wifiButton = wifi.Main; wifiStatus = wifi.Status; grid.Children.Add(wifi.Host);
        var bluetooth = Tile("Bluetooth", "\uE702", () => _ = ToggleRadioAsync(RadioKind.Bluetooth), "ms-settings:bluetooth");
        bluetoothButton = bluetooth.Main; bluetoothStatus = bluetooth.Status; Grid.SetColumn(bluetooth.Host, 1); grid.Children.Add(bluetooth.Host);
        var focus = Tile("Focus", "\uE708", () => actionStatus.Text = "Focus control is managed by Windows. Use its Settings arrow.", "ms-settings:quietmomentshome");
        focusStatus = focus.Status; Grid.SetRow(focus.Host, 1); grid.Children.Add(focus.Host);
        var night = Tile("Night light", "\uE706", () => actionStatus.Text = "Night light has no supported direct control. Use its Settings arrow.", "ms-settings:nightlight");
        night.Status.Text = "Windows settings"; Grid.SetRow(night.Host, 1); Grid.SetColumn(night.Host, 1); grid.Children.Add(night.Host);
        return grid;
    }

    private Grid CreateLowerControls()
    {
        var grid = new Grid { ColumnSpacing = 9 };
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(2.25, GridUnitType.Star) });
        FrameworkElement Fader(string name, string glyph, Slider slider, out TextBlock label, Action? iconAction = null)
        {
            var panel = new Grid { RowSpacing = 3 };
            panel.RowDefinitions.Add(new() { Height = new GridLength(17) }); panel.RowDefinitions.Add(new()); panel.RowDefinitions.Add(new() { Height = new GridLength(25) });
            panel.Children.Add(Label(name, 10, true));
            slider.Resources["SliderTrackFill"] = theme.Track;
            slider.Resources["SliderTrackValueFill"] = theme.Primary;
            slider.Resources["SliderThumbBackground"] = theme.Primary;
            slider.Resources["SliderThumbBackgroundPointerOver"] = theme.Primary;
            slider.Resources["SliderThumbBackgroundPressed"] = theme.Secondary;
            slider.Resources["SliderThumbBorderBrush"] = theme.TileBorder;
            // Keep WinUI's native Slider input/automation and range behavior;
            // size its track as a Control Center pill rather than a thin line.
            slider.Resources["SliderTrackThemeHeight"] = 40d;
            slider.Resources["SliderHorizontalThumbWidth"] = 8d;
            slider.Resources["SliderHorizontalThumbHeight"] = 8d;
            slider.Resources["SliderPreContentMargin"] = 0d;
            slider.Resources["SliderPostContentMargin"] = 0d;
            slider.CornerRadius = new(20);
            slider.MinHeight = 0;
            slider.Width = 44;
            slider.HorizontalAlignment = HorizontalAlignment.Center;
            AutomationProperties.SetName(slider, name);
            Grid.SetRow(slider, 1); panel.Children.Add(slider);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
            if (iconAction is not null)
            {
                var icon = ActionButton("", glyph, iconAction); icon.Padding = new(0); icon.MinWidth = 20; icon.Background = theme.Tile;
                AutomationProperties.SetName(icon, "Mute or unmute"); footer.Children.Add(icon);
            }
            else footer.Children.Add(new FontIcon { Glyph = glyph, FontSize = 13, Foreground = theme.Secondary });
            label = Label("—", 10, true); label.VerticalAlignment = VerticalAlignment.Center; footer.Children.Add(label);
            Grid.SetRow(footer, 2); panel.Children.Add(footer); return panel;
        }
        grid.Children.Add(Fader("Volume", "\uE767", volume, out volumeLabel, () =>
        {
            if (systemControls.SetMuted(!muted)) { muted = !muted; volumeLabel.Text = muted ? "Muted" : $"{volume.Value:0}%"; }
            else actionStatus.Text = "Volume control unavailable.";
        }));
        var bright = Fader("Brightness", "\uE706", brightness, out brightnessLabel);
        Grid.SetColumn(bright, 1); grid.Children.Add(bright);
        volume.ValueChanged += (_, _) =>
        {
            if (changingControlValues) return;
            if (systemControls.SetMasterVolume(volume.Value / 100)) volumeLabel.Text = $"{volume.Value:0}%";
            else { actionStatus.Text = "Volume control unavailable."; volume.IsEnabled = false; }
        };
        brightness.ValueChanged += (_, _) =>
        {
            if (changingControlValues) return;
            pendingBrightness = (int)Math.Round(brightness.Value);
            _ = WriteBrightnessAsync();
        };
        var actions = new Grid { RowSpacing = 5 };
        actions.RowDefinitions.Add(new()); actions.RowDefinitions.Add(new()); actions.RowDefinitions.Add(new());
        actions.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var profile = ActionButton(Environment.UserName, "\uE77B", () => OpenSettingsUri("ms-settings:yourinfo"));
        actions.Children.Add(profile);
        var settingsButton = ActionButton("Settings", "\uE713", showSettings);
        Grid.SetRow(settingsButton, 1); actions.Children.Add(settingsButton);
        var powerRow = new Grid { ColumnSpacing = 5 };
        for (var i = 0; i < 3; i++) powerRow.ColumnDefinitions.Add(new());
        var sleep = ActionButton("", "\uE708", () => _ = ConfirmPowerAsync("Sleep"));
        var locked = ActionButton("", "\uE72E", () => { if (HomeDesktopEnvironment.Lock()) HideHome(); else actionStatus.Text = "Could not lock Windows."; });
        var power = ActionButton("", "\uE7E8", () => { });
        var flyout = new MenuFlyout();
        foreach (var name in new[] { "Shut down", "Restart" })
        {
            var item = new MenuFlyoutItem { Text = name }; item.Click += (_, _) => _ = ConfirmPowerAsync(name); flyout.Items.Add(item);
        }
        power.Flyout = flyout;
        foreach (var (button, name, col) in new[] { (sleep, "Sleep", 0), (locked, "Lock", 1), (power, "Power", 2) })
        {
            button.Padding = new(3); AutomationProperties.SetName(button, name); ToolTipService.SetToolTip(button, name);
            Grid.SetColumn(button, col); powerRow.Children.Add(button);
        }
        Grid.SetRow(powerRow, 2); actions.Children.Add(powerRow);
        actionStatus = Label("", 10, true); actionStatus.TextWrapping = TextWrapping.Wrap; actionStatus.MaxLines = 2;
        AutomationProperties.SetLiveSetting(actionStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        Grid.SetRow(actionStatus, 3); actions.Children.Add(actionStatus);
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        return grid;
    }

    private async Task RefreshControlsAsync()
    {
        if (closed || !IsVisible || refreshingControls) return;
        refreshingControls = true;
        controlCancellation?.Dispose(); controlCancellation = new();
        var cancellation = controlCancellation.Token;
        var revision = visibilityRevision;
        try
        {
            var wifi = radios.ReadAsync(RadioKind.WiFi);
            var bluetooth = radios.ReadAsync(RadioKind.Bluetooth);
            var bright = DisplayBrightnessControl.ReadAsync();
            await Task.WhenAll(wifi, bluetooth, bright);
            if (closed || !IsVisible || cancellation.IsCancellationRequested || revision != visibilityRevision) return;
            wifiStatus.Text = wifi.Result.Text; bluetoothStatus.Text = bluetooth.Result.Text;
            wifiButton.IsEnabled = wifi.Result.Available && !radioBusy;
            bluetoothButton.IsEnabled = bluetooth.Result.Available && !radioBusy;
            wifiButton.Background = wifi.Result.IsOn ? theme.Pressed : theme.Tile;
            bluetoothButton.Background = bluetooth.Result.IsOn ? theme.Pressed : theme.Tile;
            focusStatus.Text = SystemRadioControls.FocusStatus();
            changingControlValues = true;
            if (systemControls.ReadMasterVolume() is { } audio)
            {
                volume.IsEnabled = true; volume.Value = audio.Percent; muted = audio.Muted;
                volumeLabel.Text = muted ? "Muted" : $"{audio.Percent}%";
            }
            else { volume.IsEnabled = false; volumeLabel.Text = "N/A"; }
            if (!brightnessWriting)
            {
                brightness.IsEnabled = bright.Result.HasValue;
                if (bright.Result is { } level) brightness.Value = level;
                brightnessLabel.Text = bright.Result is { } value ? $"{value}%" : "N/A";
                ToolTipService.SetToolTip(brightness, bright.Result.HasValue ? "Display brightness" : "This display does not expose brightness control");
            }
        }
        catch (Exception error)
        {
            if (!closed && IsVisible) actionStatus.Text = "Controls unavailable: " + error.Message;
        }
        finally { changingControlValues = false; refreshingControls = false; }
    }
    private async Task ToggleRadioAsync(RadioKind kind)
    {
        if (radioBusy || closed) return;
        radioBusy = true; wifiButton.IsEnabled = bluetoothButton.IsEnabled = false;
        try
        {
            var message = await radios.ToggleAsync(kind, controlCancellation?.Token ?? default);
            if (!closed && IsVisible) actionStatus.Text = message;
        }
        finally { radioBusy = false; if (!closed) await RefreshControlsAsync(); }
    }
    private async Task WriteBrightnessAsync()
    {
        if (brightnessWriting || closed) return;
        brightnessWriting = true;
        try
        {
            while (!closed && IsVisible && pendingBrightness is { } value)
            {
                pendingBrightness = null;
                var actual = await DisplayBrightnessControl.SetAsync(value);
                if (closed || !IsVisible) break;
                brightnessLabel.Text = actual is { } level ? $"{level}%" : "N/A";
                brightness.IsEnabled = actual.HasValue;
            }
        }
        finally { pendingBrightness = null; brightnessWriting = false; }
    }
    private void OpenSettingsUri(string uri)
    {
        if (!HomeDesktopEnvironment.OpenSettings(uri)) actionStatus.Text = "Could not open Windows settings.";
    }
    private async Task ConfirmPowerAsync(string action)
    {
        if (dialogOpen || closed) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog { XamlRoot = root.XamlRoot, RequestedTheme = root.RequestedTheme,
                Title = action + " this PC?", Content = "Save your work before continuing.", PrimaryButtonText = action,
                CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
            var success = action == "Sleep" ? await HomeDesktopEnvironment.SleepAsync() : HomeDesktopEnvironment.Shutdown(action == "Restart");
            if (success) HideHome(); else actionStatus.Text = "Windows could not complete that action.";
        }
        finally { dialogOpen = false; }
    }
}
