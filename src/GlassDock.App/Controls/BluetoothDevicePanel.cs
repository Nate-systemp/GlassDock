using System.Numerics;
using GlassDock.App.Desktop;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class BluetoothDevicePanel : StackPanel, IUtilityPanel
{
    private readonly BluetoothUtilityService bluetooth;
    private readonly UtilityTheme theme;
    private readonly UtilityGlassSurface glass = new() { Circular = false, CornerRadius = 20 };
    private readonly Grid surface = new() { Height = 0, CornerRadius = new(20) };
    private readonly Border fallback = new() { CornerRadius = new(20), IsHitTestVisible = false };
    private readonly ScrollViewer scroll = new() { HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollMode = ScrollMode.Enabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, ZoomMode = ZoomMode.Disabled };
    private readonly ScrollBar scrollbar = new() { Orientation = Orientation.Vertical, Width = 8, MinWidth = 0,
        IndicatorMode = ScrollingIndicatorMode.MouseIndicator,
        HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 10, 3, 10), Opacity = 0, SmallChange = 24 };
    private readonly StackPanel list = new();
    private readonly TextBlock status = new() { FontSize = 11, Opacity = .8, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<string, DeviceRow> rows = new(StringComparer.Ordinal);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer idle;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private Storyboard? viewportTransition, scrollbarTransition;
    private double availableHeight = BluetoothListGeometry.VisibleRows * BluetoothListGeometry.RowHeight;
    private double targetHeight = -1;
    private string? expandedId;
    private bool disposed, scrollbarHover, scrollbarDragging, synchronizingScroll;
    public FrameworkElement View => this;
    public IReadOnlyList<UtilityGlassSurface> GlassSurfaces { get; }
    public int GlassTargetsRevision => 1;
    public event EventHandler? GlassChanged;

    private sealed class DeviceRow
    {
        public required BluetoothDeviceInfo Device;
        public required Grid Host;
        public required Grid HeaderHost;
        public required Grid DetailHost;
        public required StackPanel Details;
        public required Button Header;
        public required TextBlock Name;
        public required TextBlock Status;
        public required UtilityIcon Icon;
        public required UtilityIcon Battery;
        public required Button Disconnect;
        public required Button Remove;
        public required StackPanel Confirmation;
        public required WifiNetworkContentEffect Effect;
        public Storyboard? Transition;
        public bool Expanded;
        public bool Pending;
    }

    public BluetoothDevicePanel(BluetoothUtilityService bluetooth, UtilityTheme theme)
    {
        this.bluetooth = bluetooth; this.theme = theme;
        Width = PopupStyle.CompactMenuWidth + PopupStyle.Gutter * 2;
        RequestedTheme = theme == UtilityTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        GlassSurfaces = new[] { glass };
        scrollbar.Resources["ScrollBarSize"] = 8d;
        scrollbar.Resources["ScrollBarVerticalThumbMinWidth"] = 4d;
        scroll.Content = list;
        surface.Children.Add(glass.Element); surface.Children.Add(fallback); surface.Children.Add(scroll); surface.Children.Add(scrollbar);
        Children.Add(surface); Children.Add(status);
        AutomationProperties.SetName(scrollbar, "Paired Bluetooth devices");
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        idle = DispatcherQueue.CreateTimer(); idle.Interval = TimeSpan.FromMilliseconds(800); idle.IsRepeating = false;
        idle.Tick += (_, _) => { if (!scrollbarHover && !scrollbarDragging) FadeScrollbar(0); };
        scroll.ViewChanged += (_, _) => { UpdateScroll(); RevealScrollbar(); };
        list.SizeChanged += (_, _) => UpdateScroll();
        surface.SizeChanged += (_, _) => { UpdateScroll(); GlassChanged?.Invoke(this, EventArgs.Empty); };
        scrollbar.ValueChanged += (_, e) => { if (!synchronizingScroll) scroll.ChangeView(null, e.NewValue, null, true); };
        scrollbar.PointerEntered += (_, _) => { scrollbarHover = true; RevealScrollbar(); };
        scrollbar.PointerExited += (_, _) => { scrollbarHover = false; idle.Start(); };
        scrollbar.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        { scrollbarDragging = true; RevealScrollbar(); }), true);
        scrollbar.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) =>
        { scrollbarDragging = false; idle.Start(); }), true);
        scrollbar.PointerCaptureLost += (_, _) => { scrollbarDragging = false; idle.Start(); };
        glass.RenderingChanged += GlassRenderingChanged;
        Loaded += (_, _) => { UpdateScroll(); GlassChanged?.Invoke(this, EventArgs.Empty); };
        bluetooth.Changed += BluetoothChanged;
        UpdateFallback(); Synchronize();
    }

    public Task RefreshAsync() => bluetooth.RefreshAsync();
    private void BluetoothChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (!disposed) Synchronize(); });
    private void GlassRenderingChanged(object? sender, EventArgs e) => UpdateFallback();
    private void UpdateFallback()
    {
        var shade = theme == UtilityTheme.Light ? (byte)245 : (byte)28;
        fallback.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(glass.HasFrame ? (byte)0 : (byte)235, shade, shade, shade));
    }

    private void Synchronize()
    {
        var radio = bluetooth.State;
        if (!radio.On)
        {
            SetExpanded(null);
            status.Text = string.IsNullOrWhiteSpace(radio.Message) ? "Bluetooth is off" : radio.Message;
            status.Visibility = Visibility.Visible; surface.IsHitTestVisible = false;
            UpdateViewport(); return;
        }
        var devices = bluetooth.Devices;
        var identities = devices.Select(device => device.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in rows.Keys.Where(id => !identities.Contains(id)).ToArray())
        {
            var removed = rows[id]; removed.Transition?.Stop(); removed.Effect.Dispose(); list.Children.Remove(removed.Host); rows.Remove(id);
            if (expandedId == id) expandedId = null;
        }
        foreach (var device in devices)
        {
            if (!rows.TryGetValue(device.Id, out var row))
            {
                row = CreateRow(device); rows.Add(device.Id, row); list.Children.Add(row.Host);
            }
            UpdateRow(row, device);
        }
        var informativeMessage = !string.IsNullOrWhiteSpace(radio.Message) && radio.Message != "Bluetooth on";
        status.Text = informativeMessage ? radio.Message : devices.Count == 0 ? "No paired devices" : "";
        status.Visibility = string.IsNullOrEmpty(status.Text) ? Visibility.Collapsed : Visibility.Visible;
        surface.IsHitTestVisible = radio.On && radio.Available;
        UpdateViewport(); UpdateScroll();
    }

    private DeviceRow CreateRow(BluetoothDeviceInfo device)
    {
        var icon = new UtilityIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var battery = new UtilityIcon { Width = 14, Height = 14, Opacity = .7, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = PopupStyle.CompactFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        var connection = new TextBlock { FontSize = 10.5, Opacity = .7, MaxWidth = 112,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var face = new Grid { Padding = new(10, 5, 16, 5), ColumnSpacing = 6, Height = BluetoothListGeometry.RowHeight };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            face.ColumnDefinitions.Add(new() { Width = width });
        Grid.SetColumn(name, 1); Grid.SetColumn(battery, 2); Grid.SetColumn(connection, 3);
        face.Children.Add(icon); face.Children.Add(name); face.Children.Add(battery); face.Children.Add(connection);
        var sourceHost = new Grid(); sourceHost.Children.Add(face);
        var headerContent = new Grid(); headerContent.Children.Add(sourceHost);
        var effect = new WifiNetworkContentEffect(sourceHost, face, Vector2.Zero, 0); headerContent.Children.Add(effect.Element);
        var header = new Button { Content = headerContent, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0), MinWidth = 0,
            MinHeight = 0, BorderThickness = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            header.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var details = new StackPanel { Spacing = 6, Padding = new(10, 0, 16, 10), Visibility = Visibility.Collapsed };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left };
        var disconnect = ActionButton("Disconnect"); var remove = ActionButton("Remove"); actions.Children.Add(disconnect); actions.Children.Add(remove);
        var confirmation = new StackPanel { Spacing = 4, Visibility = Visibility.Collapsed };
        confirmation.Children.Add(new TextBlock { Text = "Remove this paired device?", FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var confirmActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var confirm = ActionButton("Remove device"); var cancel = ActionButton("Cancel"); confirmActions.Children.Add(confirm); confirmActions.Children.Add(cancel); confirmation.Children.Add(confirmActions);
        details.Children.Add(actions); details.Children.Add(confirmation);
        var detailHost = new Grid { Height = 0 }; detailHost.Children.Add(details);
        detailHost.SizeChanged += (_, _) => detailHost.Clip = new RectangleGeometry { Rect = new(0, 0, detailHost.ActualWidth, detailHost.ActualHeight) };
        var headerHost = new Grid(); headerHost.Children.Add(header);
        var content = new StackPanel(); content.Children.Add(headerHost); content.Children.Add(detailHost);
        var highlight = new Border { CornerRadius = new(18), IsHitTestVisible = false,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        if (animations) highlight.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(150) };
        var host = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        host.Children.Add(highlight); host.Children.Add(content);
        host.PointerEntered += (_, _) => highlight.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(20, 255, 255, 255));
        host.PointerExited += (_, _) => highlight.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var row = new DeviceRow { Device = device, Host = host, HeaderHost = headerHost, DetailHost = detailHost, Details = details, Header = header, Name = name,
            Status = connection, Icon = icon, Battery = battery, Disconnect = disconnect, Remove = remove, Confirmation = confirmation, Effect = effect };
        header.Click += async (_, _) => await ActivateRow(row);
        disconnect.Click += async (_, _) => await Operate(row, () => bluetooth.DisconnectAsync(row.Device.Id), "Disconnecting...");
        remove.Click += (_, _) => { confirmation.Visibility = Visibility.Visible; AnimateDetails(row); };
        cancel.Click += (_, _) => { confirmation.Visibility = Visibility.Collapsed; AnimateDetails(row); };
        confirm.Click += async (_, _) => await Operate(row, () => bluetooth.RemoveAsync(row.Device.Id), "Removing...");
        return row;
    }

    private async Task ActivateRow(DeviceRow row)
    {
        if (disposed || row.Pending || row.Device.Busy) return;
        if (row.Device.IsConnected) SetExpanded(expandedId == row.Device.Id ? null : row.Device.Id);
        else
        {
            await Operate(row, () => bluetooth.ConnectAsync(row.Device.Id), "Connecting...");
        }
    }

    private static Button ActionButton(string text) => new() { Content = text, FontSize = 11, Padding = new(8, 3, 8, 3), MinWidth = 0, MinHeight = 26, CornerRadius = new(8) };

    private async Task Operate(DeviceRow row, Func<Task> action, string message)
    {
        if (disposed || row.Pending || row.Device.Busy) return;
        row.Pending = true; row.Status.Text = message; row.Header.IsEnabled = row.Disconnect.IsEnabled = row.Remove.IsEnabled = false;
        try { await action(); }
        finally { row.Pending = false; if (!disposed) Synchronize(); }
    }

    private void UpdateRow(DeviceRow row, BluetoothDeviceInfo device)
    {
        var old = row.Device; row.Device = device;
        row.Name.Text = device.Name;
        if (!row.Pending) row.Status.Text = device.Status ?? (device.IsConnected ? "Connected" : "Not connected");
        row.Header.IsEnabled = !device.Busy && !row.Pending;
        row.Disconnect.IsEnabled = device.CanDisconnect && device.IsConnected && !device.Busy && !row.Pending;
        row.Remove.IsEnabled = !device.Busy && !row.Pending;
        AutomationProperties.SetName(row.Header, $"{device.Name}, {row.Status.Text}");
        if (old.Kind != device.Kind || row.Icon.Tag is null)
        {
            row.Icon.Tag = device.Kind;
            _ = row.Icon.SetAsync(device.Kind switch { BluetoothDeviceKind.Headphones => "headphones", BluetoothDeviceKind.Headset => "headset",
                BluetoothDeviceKind.Mouse => "mouse", BluetoothDeviceKind.Keyboard => "keyboard", BluetoothDeviceKind.Controller => "game-controller",
                BluetoothDeviceKind.Speaker => "speaker-hifi", _ => "bluetooth" }, false, UtilityColors.Icon(theme, false));
        }
        row.Battery.Visibility = device.BatteryPercent is >= 0 and <= 100 ? Visibility.Visible : Visibility.Collapsed;
        if (device.BatteryPercent is >= 0 and <= 100 && (old.BatteryPercent != device.BatteryPercent || row.Battery.Tag is null))
        {
            row.Battery.Tag = device.BatteryPercent;
            var icon = device.BatteryPercent switch { <= 10 => "battery-empty", <= 30 => "battery-low", <= 60 => "battery-medium", <= 85 => "battery-high", _ => "battery-full" };
            _ = row.Battery.SetAsync(icon, false, UtilityColors.Icon(theme, false));
        }
    }

    private void SetExpanded(string? id)
    {
        expandedId = id;
        foreach (var row in rows.Values)
        {
            var expand = row.Device.Id == id;
            if (row.Expanded == expand) continue;
            row.Expanded = expand; row.Confirmation.Visibility = Visibility.Collapsed; AnimateDetails(row);
        }
    }

    private void AnimateDetails(DeviceRow row)
    {
        row.Details.Visibility = Visibility.Visible;
        row.Details.IsHitTestVisible = row.Expanded;
        row.Details.Measure(new(Math.Max(1, Width), double.PositiveInfinity));
        var target = row.Expanded ? row.Details.DesiredSize.Height : 0;
        AnimateHeight(row.DetailHost, target, ref row.Transition, () =>
        {
            if (!row.Expanded) row.Details.Visibility = Visibility.Collapsed;
            UpdateScroll();
            if (row.Expanded && !disposed) row.DetailHost.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = animations });
        });
    }

    public void SetAvailableHeight(double height)
    {
        var next = Math.Max(0, height);
        if (Math.Abs(next - availableHeight) < .1) return;
        availableHeight = next; UpdateViewport();
    }

    private void UpdateViewport()
    {
        var statusHeight = 0d;
        if (status.Visibility == Visibility.Visible)
        {
            status.Measure(new(Width, double.PositiveInfinity));
            statusHeight = status.DesiredSize.Height;
        }
        var next = bluetooth.State.On && bluetooth.State.Available
            ? BluetoothListGeometry.ViewportHeight(rows.Count, Math.Max(0, availableHeight - statusHeight - (statusHeight > 0 ? 6 : 0))) : 0;
        Spacing = next > 0 && statusHeight > 0 ? 6 : 0;
        if (Math.Abs(next - targetHeight) < .1) return;
        targetHeight = next; AnimateHeight(surface, next, ref viewportTransition, UpdateScroll);
    }

    private void AnimateHeight(FrameworkElement element, double target, ref Storyboard? transition, Action complete)
    {
        var from = double.IsNaN(element.Height) ? element.ActualHeight : element.Height;
        transition?.Stop(); element.Height = from;
        if (!animations || !IsLoaded || Math.Abs(from - target) < .1) { element.Height = target; complete(); return; }
        var animation = new DoubleAnimation { From = from, To = target, Duration = new(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        Storyboard.SetTarget(animation, element); Storyboard.SetTargetProperty(animation, "Height");
        var storyboard = new Storyboard(); storyboard.Children.Add(animation); transition = storyboard;
        storyboard.Completed += (_, _) => { if (disposed) return; element.Height = target; complete(); };
        storyboard.Begin();
    }

    private void UpdateScroll()
    {
        if (disposed) return;
        synchronizingScroll = true;
        scrollbar.Maximum = Math.Max(0, scroll.ScrollableHeight); scrollbar.ViewportSize = scroll.ViewportHeight;
        scrollbar.LargeChange = scroll.ViewportHeight; scrollbar.Value = scroll.VerticalOffset;
        scrollbar.Visibility = scrollbar.Maximum > .5 ? Visibility.Visible : Visibility.Collapsed;
        synchronizingScroll = false;
        if (!list.IsLoaded) return;
        foreach (var row in rows.Values)
        {
            var top = row.HeaderHost.TransformToVisual(list).TransformPoint(new(0, 0)).Y;
            var progress = BluetoothListGeometry.EdgeProgress(top, row.HeaderHost.ActualHeight, scroll.VerticalOffset, scroll.ViewportHeight);
            row.Effect.Update(progress, row.HeaderHost.ActualWidth, row.HeaderHost.ActualHeight);
        }
    }

    private void RevealScrollbar() { if (scrollbar.Maximum <= 0) return; FadeScrollbar(1); idle.Stop(); idle.Start(); }
    private void FadeScrollbar(double opacity)
    {
        var from = scrollbar.Opacity; scrollbarTransition?.Stop();
        var animation = new DoubleAnimation { From = from, To = opacity, Duration = new(TimeSpan.FromMilliseconds(animations ? 180 : 0)) };
        Storyboard.SetTarget(animation, scrollbar); Storyboard.SetTargetProperty(animation, "Opacity");
        scrollbarTransition = new(); scrollbarTransition.Children.Add(animation); scrollbarTransition.Begin();
    }

    public void ConfigureGlass(GlassMaterial material, LiquidGlassMaterial optics, float scale, bool highlights) =>
        glass.Configure(DockAppearanceMode.Clear, material, optics, highlights, scale);

    public bool PositionGlass(FrameworkElement relativeTo, Vector2 origin, bool active)
    {
        if (!surface.IsLoaded || !relativeTo.IsLoaded) { var wasActive = glass.Active; glass.Active = false; return wasActive; }
        var bounds = surface.TransformToVisual(relativeTo).TransformBounds(new(0, 0, surface.ActualWidth, surface.ActualHeight));
        var next = new Vector4((float)bounds.X + origin.X, (float)bounds.Y + origin.Y, (float)bounds.Width, (float)bounds.Height);
        var visible = active && bounds.Width >= 1 && bounds.Height >= 1;
        var changed = next != glass.Bounds || visible != glass.Active; glass.Bounds = next; glass.Active = visible;
        if (!visible) glass.Element.Visibility = Visibility.Collapsed;
        return changed;
    }

    public void Dispose()
    {
        if (disposed) return; disposed = true;
        bluetooth.Changed -= BluetoothChanged; glass.RenderingChanged -= GlassRenderingChanged;
        idle.Stop(); viewportTransition?.Stop(); scrollbarTransition?.Stop();
        foreach (var row in rows.Values) { row.Transition?.Stop(); row.Effect.Dispose(); }
        glass.Dispose();
    }
}
