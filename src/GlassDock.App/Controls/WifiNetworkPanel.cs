using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.App.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class WifiNetworkPanel : StackPanel, IDisposable
{
    private readonly WifiUtilityService wifi;
    private readonly UtilityTheme theme;
    private readonly WifiNetworkSelection selection = new();
    private readonly StackPanel body = new() { Spacing = 6 };
    private readonly ContentControl bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Opacity = .8 };
    private readonly Button refresh = new() { Content = new FontIcon { Glyph = "\uE72C", FontSize = 12 }, Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), BorderThickness = new(0), CornerRadius = new(12) };
    private readonly PasswordBox password = new() { Header = "Password", FontSize = PopupStyle.CompactFontSize, PasswordRevealMode = PasswordRevealMode.Hidden };
    private readonly CheckBox reveal = new() { Content = "Show password", FontSize = PopupStyle.CompactFontSize, MinHeight = 26 };
    private readonly CheckBox automatic = new() { Content = "Connect automatically", FontSize = PopupStyle.CompactFontSize, IsChecked = true, MinHeight = 26 };
    private readonly Dictionary<string, NetworkRow> rows = [];
    private readonly List<GlassPart> glassParts = [];
    private readonly StackPanel recovery = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private LiquidGlassMaterial optics = new();
    private GlassMaterial backdrop = new();
    private float layoutScale = 1;
    private bool highlights = true;
    public IEnumerable<UtilityGlassSurface> GlassSurfaces => glassParts.Select(p => p.Glass);
    public event EventHandler? GlassChanged;
    private CancellationTokenSource operation = new();
    private bool disposed, connecting, refreshing, radioOn;
    private string? detailsKey;
    private int revision;
    private sealed record NetworkRow(WifiNetwork Network, GlassPart Part, GlassPart ActionPart, StackPanel Details,
        TextBlock Name, TextBlock ActionText, Button Action, UtilityIcon Signal, UtilityIcon Lock);
    private sealed record GlassPart(FrameworkElement Host, UtilityGlassSurface Glass, Border Fallback)
    { public byte Highlight { get; set; } }

    public WifiNetworkPanel(WifiUtilityService wifi, UtilityTheme theme)
    {
        this.wifi = wifi; this.theme = theme; Width = PopupStyle.CompactMenuWidth + PopupStyle.Gutter * 2; Padding = new(0); Spacing = 6;
        radioOn = wifi.State.On;
        RequestedTheme = theme == UtilityTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        AutomationProperties.SetName(refresh, "Refresh available Wi-Fi networks");
        ToolTipService.SetToolTip(refresh, "Refresh networks");
        refresh.Click += async (_, _) => await RefreshAsync();
        bodyHost.Content = body;
        if (new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
            body.ChildrenTransitions = new TransitionCollection { new RepositionThemeTransition(), new EntranceThemeTransition { IsStaggeringEnabled = false } };
        Children.Add(bodyHost); Children.Add(status); Children.Add(recovery);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        reveal.Checked += (_, _) => password.PasswordRevealMode = PasswordRevealMode.Visible;
        reveal.Unchecked += (_, _) => password.PasswordRevealMode = PasswordRevealMode.Hidden;
        var settings = new HyperlinkButton { Content = "Wi-Fi settings", FontSize = 10.5, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        settings.Click += async (_, _) => await new GlassDock.Windows.Applications.WindowsApplicationLauncher().LaunchTargetAsync("ms-settings:network-wifi");
        recovery.Children.Add(refresh); recovery.Children.Add(settings);
        selection.Update(wifi.CachedNetworks); SynchronizeRows(); SetStatus(""); wifi.Changed += RadioChanged;
    }

    private void RadioChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (disposed || wifi.State.Busy) return;
        if (wifi.State.On)
        {
            var enabled = !radioOn; radioOn = true;
            if (enabled && !refreshing && !connecting) _ = RefreshAsync();
            return;
        }
        radioOn = false; revision++; operation.Cancel(); connecting = refreshing = false; bodyHost.IsEnabled = refresh.IsEnabled = true;
        selection.Clear(); ClearCredentials(); SynchronizeRows(); SetStatus(wifi.State.Message);
    });

    public async Task RefreshAsync()
    {
        if (disposed || connecting) return;
        operation.Cancel(); operation.Dispose(); operation = new();
        var token = operation.Token; var current = ++revision;
        refreshing = true; refresh.IsEnabled = false;
        try
        {
            await wifi.RefreshAsync();
            if (disposed || current != revision) return;
            if (wifi.State.Busy) return;
            radioOn = wifi.State.On;
            if (!wifi.State.On) { selection.Clear(); SynchronizeRows(); SetStatus(wifi.State.Message); return; }
            if (wifi.HasNetworkSnapshot)
                ApplyNetworks(wifi.CachedNetworks, completed: false);
            else
            {
                var known = await wifi.ReadNetworksAsync(false, token);
                if (disposed || current != revision) return;
                ApplyNetworks(known, completed: false);
            }
            var available = await wifi.ReadNetworksAsync(true, token);
            if (disposed || current != revision) return;
            ApplyNetworks(available, completed: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && current == revision) SetStatus(WifiNetworkControl.ErrorMessage(error)); }
        finally { if (!disposed && current == revision) { refreshing = false; refresh.IsEnabled = true; } }
    }

    private void ApplyNetworks(IReadOnlyList<WifiNetwork> available, bool completed)
    {
        if (selection.Update(available)) ClearCredentials();
        SynchronizeRows();
        SetStatus(completed && selection.Networks.Count == 0 ? "No networks" : "");
    }

    private void SynchronizeRows()
    {
        foreach (var key in rows.Keys.Where(k => !selection.Networks.Any(n => n.Key == k)).ToArray())
        {
            var row = rows[key]; row.Details.Children.Clear(); body.Children.Remove(row.Part.Host); rows.Remove(key);
            foreach (var part in new[] { row.Part, row.ActionPart }) { glassParts.Remove(part); part.Glass.Dispose(); }
        }
        foreach (var network in selection.Networks)
        {
            if (rows.TryGetValue(network.Key, out var existing))
            {
                rows[network.Key] = existing with { Network = network };
                UpdateRow(rows[network.Key]); continue;
            }
            var header = new Grid { ColumnSpacing = 6, Height = 26 };
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var signal = new UtilityIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
            _ = signal.SetAsync(network.Signal >= 70 ? "wifi-high" : network.Signal >= 35 ? "wifi-medium" : "wifi-low", network.Connected, UtilityColors.Icon(theme, false));
            header.Children.Add(signal);
            var text = new TextBlock { Text = network.Name, FontSize = PopupStyle.CompactFontSize,
                TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
            var locked = new UtilityIcon { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
            _ = locked.SetAsync("lock-simple", false, UtilityColors.Icon(theme, false));
            Grid.SetColumn(locked, 2); header.Children.Add(locked);
            var name = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                BorderThickness = new(0), Padding = new(0), MinWidth = 0, MinHeight = 0, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            ToolTipService.SetToolTip(name, network.Name);
            AutomationProperties.SetName(name, network.Name);
            name.Click += (_, _) => Select(selection.SelectedKey != network.Key && NeedsPassword(rows[network.Key].Network) ? network.Key : null);
            var actionFace = new Grid { Width = 64, Height = 22 };
            var actionFallback = new Border { CornerRadius = new(11), IsHitTestVisible = false };
            var actionGlass = AddGlass(actionFace, actionFallback, 11); actionGlass.Highlight = 16;
            actionFace.Children.Add(actionFallback);
            var actionText = new TextBlock { FontSize = 10.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            actionFace.Children.Add(actionText);
            var action = new Button { Content = actionFace, Padding = new(0), CornerRadius = new(11), MinWidth = 0, MinHeight = 0,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new(0), IsEnabled = !network.Connected && network.Connectable, VerticalAlignment = VerticalAlignment.Center };
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
                action.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            AutomationProperties.SetName(action, "Connect to " + network.Name);
            action.Click += async (_, _) => { Select(network.Key); if (selection.Selected?.CanConnect == true) await ConnectAsync(); };
            Grid.SetColumn(name, 1); Grid.SetColumn(action, 3); header.Children.Add(name); header.Children.Add(action);
            var details = new StackPanel { Spacing = 4, Margin = new(20, 4, 0, 4), Visibility = Visibility.Collapsed };
            if (new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
                details.Transitions = new TransitionCollection { new EntranceThemeTransition { IsStaggeringEnabled = false } };
            var content = new StackPanel(); content.Children.Add(header); content.Children.Add(details);
            var face = new Grid();
            var card = new Border { Child = content, Padding = new(10, 5, 10, 5), CornerRadius = new(18) };
            var rowGlass = AddGlass(face, card, 18);
            face.Children.Add(card);
            if (new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
                card.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(150) };
            var row = new NetworkRow(network, rowGlass, actionGlass, details, text, actionText, action, signal, locked);
            rows[network.Key] = row; UpdateRow(row); body.Children.Add(face);
        }
        // Keep retained rows in place while signal rankings change, preserving password focus.
        UpdateSelection();
        GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Select(string? key)
    {
        if (connecting || disposed) return;
        if (selection.SelectedKey != key) ClearCredentials();
        selection.Select(key); UpdateSelection();
        SetStatus("");
    }

    private void UpdateSelection()
    {
        var target = selection.Selected is { } selectedNetwork && NeedsPassword(selectedNetwork) ? selectedNetwork.Key : null;
        var rebuild = detailsKey != target;
        if (rebuild) { foreach (var row in rows.Values) row.Details.Children.Clear(); ClearCredentials(); detailsKey = target; }
        foreach (var row in rows.Values)
        {
            var selected = row.Network.Key == selection.SelectedKey;
            row.Part.Highlight = selected ? (byte)16 : row.Network.Connected ? (byte)8 : (byte)0;
            UpdateGlassFill(row.Part);
            row.Details.Visibility = row.Network.Key == target ? Visibility.Visible : Visibility.Collapsed;
            if (!rebuild || row.Network.Key != target) continue;
            row.Details.Children.Add(password); row.Details.Children.Add(reveal);
            row.Details.Children.Add(automatic);
            var connect = new Button { Content = "Connect", FontSize = PopupStyle.CompactFontSize, CornerRadius = new(10), Padding = new(10, 5, 10, 5),
                HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = row.Network.Connectable };
            connect.Click += async (_, _) => await ConnectAsync(); row.Details.Children.Add(connect);
        }
    }

    private void SetConnecting(bool value)
    {
        connecting = value; bodyHost.IsEnabled = refresh.IsEnabled = !value;
        foreach (var row in rows.Values)
        {
            UpdateRow(row);
        }
    }

    private async Task ConnectAsync()
    {
        if (selection.Selected is not { } network || connecting || disposed) return;
        if (network.Secured && network.Profile.Length == 0 && password.Password.Length == 0)
        { SetStatus("Password required."); password.Focus(FocusState.Programmatic); return; }
        SetConnecting(true); SetStatus(""); var current = ++revision; var token = operation.Token;
        try
        {
            var result = await WifiNetworkControl.ConnectAsync(network, password.Password, automatic.IsChecked == true, token);
            if (disposed || current != revision) return;
            ClearCredentials(); var available = await wifi.ReadNetworksAsync(false, token);
            if (disposed || current != revision) return;
            selection.Update(available); SynchronizeRows();
            SetStatus(result == "Connected." && selection.Networks.Any(n => n.Key == network.Key && n.Connected)
                ? "" : result == "Connected." ? "Disconnected" : result);
            await wifi.RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException error) { if (!disposed && current == revision) SetStatus(error.Message); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { if (!disposed && current == revision) SetStatus(WifiNetworkControl.ErrorMessage(error)); }
        finally
        {
            if (!disposed && current == revision)
            {
                SetConnecting(false);
            }
        }
    }

    private void ClearCredentials() { password.Password = ""; reveal.IsChecked = false; }
    private void SetStatus(string text)
    {
        status.Text = text; status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        recovery.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool NeedsPassword(WifiNetwork network) => network.Secured && network.Profile.Length == 0 && !network.Connected;
    private void UpdateRow(NetworkRow row)
    {
        row.Name.Text = row.Network.Name;
        row.ActionText.Text = connecting && selection.SelectedKey == row.Network.Key ? "Connecting" : row.Network.Connected ? "Connected" : "Connect";
        row.Action.IsEnabled = !connecting && !row.Network.Connected && row.Network.Connectable;
        AutomationProperties.SetItemStatus(row.Action, row.ActionText.Text);
        row.Lock.Visibility = row.Network.Secured ? Visibility.Visible : Visibility.Collapsed;
        _ = row.Signal.SetAsync(row.Network.Signal >= 70 ? "wifi-high" : row.Network.Signal >= 35 ? "wifi-medium" : "wifi-low",
            row.Network.Connected, UtilityColors.Icon(theme, false));
    }

    private GlassPart AddGlass(Grid host, Border fallback, float radius)
    {
        var glass = new UtilityGlassSurface { Circular = false, CornerRadius = radius };
        // Canvas keeps the drawing target out of measurement, so expanded rows can shrink again.
        var layer = new Canvas { IsHitTestVisible = false }; layer.Children.Add(glass.Element); host.Children.Add(layer);
        var part = new GlassPart(host, glass, fallback); glassParts.Add(part);
        glass.Configure(DockAppearanceMode.Clear, backdrop, optics, highlights, layoutScale);
        glass.RenderingChanged += (_, _) => UpdateGlassFill(part);
        host.SizeChanged += (_, _) => GlassChanged?.Invoke(this, EventArgs.Empty);
        host.Loaded += (_, _) => GlassChanged?.Invoke(this, EventArgs.Empty);
        UpdateGlassFill(part);
        return part;
    }
    private void UpdateGlassFill(GlassPart part)
    {
        var shade = theme == UtilityTheme.Light ? (byte)245 : (byte)28;
        part.Fallback.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(
            part.Glass.HasFrame ? part.Highlight : (byte)235, shade, shade, shade));
    }
    public void ConfigureGlass(GlassMaterial material, LiquidGlassMaterial clearOptics, float scale, bool showHighlights)
    {
        backdrop = material; optics = clearOptics; layoutScale = scale; highlights = showHighlights;
        foreach (var part in glassParts) part.Glass.Configure(DockAppearanceMode.Clear, backdrop, optics, highlights, scale);
    }
    public bool PositionGlass(FrameworkElement relativeTo, Vector2 origin, bool active)
    {
        var changed = false;
        foreach (var part in glassParts)
        {
            if (!part.Host.IsLoaded || !relativeTo.IsLoaded) { part.Glass.Active = false; continue; }
            var bounds = part.Host.TransformToVisual(relativeTo).TransformBounds(new(0, 0, part.Host.ActualWidth, part.Host.ActualHeight));
            var next = new Vector4((float)bounds.X + origin.X, (float)bounds.Y + origin.Y, (float)bounds.Width, (float)bounds.Height);
            var visible = active && bounds.Width > 0 && bounds.Height > 0;
            changed |= part.Glass.Bounds != next || part.Glass.Active != visible;
            part.Glass.Bounds = next; part.Glass.Active = visible;
        }
        return changed;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; revision++; wifi.Changed -= RadioChanged;
        operation.Cancel(); operation.Dispose(); ClearCredentials();
        foreach (var part in glassParts) part.Glass.Dispose();
    }
}
