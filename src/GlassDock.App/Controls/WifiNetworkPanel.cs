using System.Numerics;
using GlassDock.App.Rendering;
using GlassDock.App.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Controls;

internal sealed class WifiNetworkPanel : StackPanel, IUtilityPanel
{
    private bool CompactConnectionInput => selection.Selected is { } network && !NeedsPassword(network) &&
        (network.Connected || !string.IsNullOrEmpty(network.Profile));
    private double ConnectionInputHeight => CompactConnectionInput ? WifiScrollGeometry.Height : WifiNetworkListPanel.ConnectionRowHeight;
    private double SelectionHeight => 2 * WifiScrollGeometry.Height + ConnectionInputHeight + 2 * WifiScrollGeometry.Gap;
    private readonly WifiUtilityService wifi;
    private readonly UtilityTheme theme;
    private readonly WifiNetworkSelection selection = new();
    private readonly WifiNetworkListPanel body = new();
    private readonly ScrollViewer listScroll = new() { HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled, ZoomMode = ZoomMode.Disabled };
    private readonly ContentControl bodyHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 10.5, Opacity = .8 };
    private readonly Button refresh = new() { Content = new FontIcon { Glyph = "\uE72C", FontSize = 12 }, Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), BorderThickness = new(0), CornerRadius = new(12) };
    private readonly PasswordBox password = new() { PlaceholderText = "Password", FontSize = PopupStyle.CompactFontSize,
        PasswordRevealMode = PasswordRevealMode.Hidden, MinWidth = 0, MinHeight = 0, Height = 24, Margin = new(2, 1, 4, 1),
        Padding = new(7, 1, 7, 1), CornerRadius = new(12), BorderThickness = new(1),
        HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ToggleButton reveal = new() { MinWidth = 0, MinHeight = 0, Width = 24, Height = 24,
        Padding = new(3), CornerRadius = new(12), BorderThickness = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly CheckBox autoConnect = new() { Content = "Auto-connect", IsChecked = true,
        FontSize = PopupStyle.CompactFontSize, MinWidth = 0, MinHeight = 0, Height = 22, CornerRadius = new(8),
        Padding = new(6, 0, 0, 0), Margin = new(4, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private string? passwordRetryKey;
    private readonly TextBlock credentialHint = new() { FontSize = PopupStyle.CompactFontSize, VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Left };
    private readonly List<NetworkRow> rows = [];
    private readonly List<GlassPart> glassParts = [];
    private readonly List<UtilityGlassSurface> glassSurfaces = [];
    private readonly StackPanel recovery = new() { Orientation = Orientation.Horizontal, Spacing = 12 };
    private LiquidGlassMaterial optics = new();
    private GlassMaterial backdrop = new();
    private float layoutScale = 1;
    private bool highlights = true;
    public FrameworkElement View => this;
    public IReadOnlyList<UtilityGlassSurface> GlassSurfaces => glassSurfaces;
    public int GlassTargetsRevision { get; private set; }
    public event EventHandler? GlassChanged;
    private CancellationTokenSource operation = new();
    private bool disposed, refreshing, radioOn;
    private string? pendingNetworkKey;
    private bool networkBusy => pendingNetworkKey is not null;
    private bool disconnecting;
    private string? detailsKey;
    private int revision;
    private readonly bool animations = new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    private Storyboard? transition;
    private TaskCompletionSource<bool>? transitionCompletion;
    private int viewRevision;
    private bool transitioning;
    private NetworkRow? passwordRow;
    private NetworkRow? chevronRow;
    private Button? chevron;
    private bool expanded;
    private double savedListOffset;
    private double ListViewportHeight => Math.Min(visibleSlots, selection.Networks.Count) * WifiScrollGeometry.Pitch;
    private string? savedListAnchor;
    private int visibleSlots = WifiScrollGeometry.VisibleSlots, lastListLayout = -1;
    private sealed record NetworkRow(GlassPart Part, GlassPart ActionPart, Grid Header, Grid SourceHost,
        WifiNetworkContentEffect ContentEffect, TranslateTransform Offset,
        TextBlock Name, TextBlock ActionText, Button Action, UtilityIcon Signal, UtilityIcon Lock)
    { public WifiNetwork? Network { get; set; } }
    private sealed record GlassPart(FrameworkElement Host, UtilityGlassSurface Glass, Border Fallback)
    { public byte Highlight { get; set; } public bool Hovered { get; set; } }

    private void AlignPasswordContent(object sender, RoutedEventArgs e)
    {
        password.ApplyTemplate();
        if (VisualTreeHelper.GetChildrenCount(password) > 0 && VisualTreeHelper.GetChild(password, 0) is FrameworkElement templateRoot)
        {
            if (templateRoot.FindName("ContentElement") is ScrollViewer editor)
            {
                editor.HorizontalAlignment = HorizontalAlignment.Stretch;
                editor.HorizontalContentAlignment = HorizontalAlignment.Left;
                editor.VerticalContentAlignment = VerticalAlignment.Center;
            }
            if (templateRoot.FindName("PlaceholderTextContentPresenter") is TextBlock placeholder)
                placeholder.TextAlignment = TextAlignment.Left;
        }
    }

    public WifiNetworkPanel(WifiUtilityService wifi, UtilityTheme theme)
    {
        this.wifi = wifi; this.theme = theme; Width = PopupStyle.CompactMenuWidth + PopupStyle.Gutter * 2; Padding = new(0); Spacing = 6;
        radioOn = wifi.State.On;
        RequestedTheme = theme == UtilityTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        ConfigureAutoConnect();
        AutomationProperties.SetName(refresh, "Refresh available Wi-Fi networks");
        ToolTipService.SetToolTip(refresh, "Refresh networks");
        refresh.Click += async (_, _) => await RefreshAsync();
        listScroll.Content = body; bodyHost.Content = listScroll;
        listScroll.ViewChanged += (_, _) => body.SetScroll(listScroll.VerticalOffset, expanded && detailsKey is null, visibleSlots);
        listScroll.SizeChanged += (_, _) => body.SetViewport(listScroll.ActualHeight);
        body.LayoutUpdated += (_, _) => UpdateScrollContent();
        Children.Add(bodyHost); Children.Add(status); Children.Add(recovery);
        AutomationProperties.SetLiveSetting(status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        reveal.Checked += (_, _) => password.PasswordRevealMode = PasswordRevealMode.Visible;
        reveal.Unchecked += (_, _) => password.PasswordRevealMode = PasswordRevealMode.Hidden;
        reveal.Content = new UtilityIcon { Width = 14, Height = 14 };
        _ = ((UtilityIcon)reveal.Content).SetAsync("eye", false, UtilityColors.Icon(theme, false));
        AutomationProperties.SetName(password, "Wi-Fi password");
        AutomationProperties.SetName(reveal, "Show password"); ToolTipService.SetToolTip(reveal, "Show password");
        reveal.Checked += (_, _) => { AutomationProperties.SetName(reveal, "Hide password"); ToolTipService.SetToolTip(reveal, "Hide password"); };
        reveal.Unchecked += (_, _) => { AutomationProperties.SetName(reveal, "Show password"); ToolTipService.SetToolTip(reveal, "Show password"); };
        var inputShade = theme == UtilityTheme.Light ? (byte)0 : (byte)255;
        var inputFill = new SolidColorBrush(global::Windows.UI.Color.FromArgb(theme == UtilityTheme.Light ? (byte)16 : (byte)24,
            inputShade, inputShade, inputShade));
        password.Background = inputFill;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            password.Resources[key] = inputFill;
        password.Resources["TextControlThemeMinHeight"] = 0d;
        password.Resources["TextControlThemeMinWidth"] = 0d;
        ConfigurePasswordFocus();
        password.Loaded += AlignPasswordContent;
        password.KeyDown += async (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Enter) return;
            e.Handled = true; await ExecuteNetworkActionAsync();
        };
        var settings = new HyperlinkButton { Content = "Wi-Fi settings", FontSize = 10.5, Padding = new(0), HorizontalAlignment = HorizontalAlignment.Left };
        settings.Click += async (_, _) => await new GlassDock.Windows.Applications.WindowsApplicationLauncher().LaunchTargetAsync("ms-settings:network-wifi");
        recovery.Children.Add(refresh); recovery.Children.Add(settings);
        for (var i = 0; i < 3; i++) { var row = CreateRow(); rows.Add(row); body.Children.Add(row.Part.Host); }
        selection.Update(wifi.CachedNetworks); SynchronizeRows(); SetStatus(""); wifi.Changed += RadioChanged;
    }

    private void ConfigurePasswordFocus()
    {
        var accent = UtilityDefinition.All.First(d => d.Kind == UtilityKind.Wifi).Accent;
        var outline = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255,
            (byte)(accent >> 16), (byte)(accent >> 8), (byte)accent)) { Opacity = 0 };
        password.BorderBrush = outline;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            password.Resources[key] = outline;
        password.Resources["TextControlBorderBrushDisabled"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        password.Resources["TextControlBorderThemeThickness"] = new Thickness(1);
        password.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        Storyboard? focusTransition = null;
        void UpdateOutline()
        {
            var from = outline.Opacity;
            focusTransition?.Stop();
            var animation = new DoubleAnimation { From = from, To = password.FocusState == FocusState.Unfocused ? 0 : .75,
                Duration = new(TimeSpan.FromMilliseconds(animations ? 140 : 0)), EnableDependentAnimation = true };
            Storyboard.SetTarget(animation, outline); Storyboard.SetTargetProperty(animation, "Opacity");
            focusTransition = new(); focusTransition.Children.Add(animation); focusTransition.Begin();
        }
        password.GotFocus += (_, _) => UpdateOutline();
        password.LostFocus += (_, _) => UpdateOutline();
    }

    private void ConfigureAutoConnect()
    {
        var accent = UtilityDefinition.All.First(d => d.Kind == UtilityKind.Wifi).Accent;
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        autoConnect.Background = transparent;
        autoConnect.Loaded += (_, _) =>
        {
            autoConnect.ApplyTemplate();
            if (VisualTreeHelper.GetChildrenCount(autoConnect) == 0 ||
                VisualTreeHelper.GetChild(autoConnect, 0) is not FrameworkElement root) return;
            if (root.FindName("NormalRectangle") is FrameworkElement circle && circle.Parent is FrameworkElement slot)
                circle.Width = circle.Height = slot.Width = slot.Height = 16;
            if (root.FindName("CheckGlyph") is FrameworkElement glyph)
                glyph.Width = glyph.Height = 16;
        };
        foreach (var (suffix, alpha) in new[] { ("", (byte)255), ("PointerOver", (byte)235), ("Pressed", (byte)210) })
        {
            var blue = new SolidColorBrush(global::Windows.UI.Color.FromArgb(alpha, (byte)(accent >> 16), (byte)(accent >> 8), (byte)accent));
            autoConnect.Resources["CheckBoxCheckBackgroundFillChecked" + suffix] = blue;
            autoConnect.Resources["CheckBoxCheckBackgroundStrokeChecked" + suffix] = blue;
            autoConnect.Resources["CheckBoxCheckGlyphForegroundChecked" + suffix] = new SolidColorBrush(Microsoft.UI.Colors.White);
            autoConnect.Resources["CheckBoxCheckBackgroundFillUnchecked" + suffix] = new SolidColorBrush(
                global::Windows.UI.Color.FromArgb((byte)(suffix == "Pressed" ? 180 : suffix == "PointerOver" ? 240 : 220), 255, 255, 255));
            autoConnect.Resources["CheckBoxCheckBackgroundStrokeUnchecked" + suffix] = new SolidColorBrush(
                global::Windows.UI.Color.FromArgb(theme == UtilityTheme.Light ? (byte)80 : (byte)120, 120, 120, 128));
            autoConnect.Resources["CheckBoxBackgroundUnchecked" + suffix] = transparent;
            autoConnect.Resources["CheckBoxBackgroundChecked" + suffix] = transparent;
        }
    }

    private void RadioChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (disposed || wifi.State.Busy) return;
        if (wifi.State.On)
        {
            var enabled = !radioOn; radioOn = true;
            if (enabled && !refreshing && !networkBusy) _ = RefreshAsync();
            return;
        }
        radioOn = false; revision++; operation.Cancel(); pendingNetworkKey = null; refreshing = false;
        password.IsEnabled = reveal.IsEnabled = autoConnect.IsEnabled = bodyHost.IsEnabled = refresh.IsEnabled = true;
        CancelTransition(); RestoreList(); selection.Clear(); ClearCredentials(); SynchronizeRows(); SetStatus(wifi.State.Message);
    });

    public async Task RefreshAsync()
    {
        if (disposed || networkBusy) return;
        operation.Cancel(); operation.Dispose(); operation = new();
        var token = operation.Token; var current = ++revision;
        refreshing = true; refresh.IsEnabled = false;
        try
        {
            await wifi.RefreshAsync();
            if (disposed || current != revision) return;
            if (wifi.State.Busy) return;
            radioOn = wifi.State.On;
            if (!wifi.State.On) { CancelTransition(); RestoreList(); selection.Clear(); SynchronizeRows(); SetStatus(wifi.State.Message); return; }
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
        var lostSelection = selection.Update(available);
        var lostMorphTarget = transitioning && detailsKey is null && chevronRow?.Network is { } more &&
            selection.Networks.All(n => n.Key != more.Key);
        if (lostSelection || lostMorphTarget) { CancelTransition(); RestoreList(); }
        SynchronizeRows();
        SetStatus(completed && selection.Networks.Count == 0 ? "No networks" : "");
    }

    private NetworkRow CreateRow()
    {
        var header = new Grid { ColumnSpacing = 6, Height = 26 };
        foreach (var width in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            header.ColumnDefinitions.Add(new() { Width = width });
        var signal = new UtilityIcon { Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { FontSize = PopupStyle.CompactFontSize, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center };
        var locked = new UtilityIcon { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center };
        _ = locked.SetAsync("lock-simple", false, UtilityColors.Icon(theme, false));
        var name = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, BorderThickness = new(0), Padding = new(0),
            MinWidth = 0, MinHeight = 0, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        var actionFace = new Grid { Width = 64, Height = 22 };
        var actionFallback = new Border { CornerRadius = new(11), IsHitTestVisible = false };
        var actionGlass = AddGlass(actionFace, actionFallback, 11); actionGlass.Highlight = 16;
        actionFace.Children.Add(actionFallback);
        var actionText = new TextBlock { FontSize = 10.5, MaxWidth = 64, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        actionFace.Children.Add(actionText);
        var action = new Button { Content = actionFace, Padding = new(0), CornerRadius = new(11), MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0), VerticalAlignment = VerticalAlignment.Center };
        foreach (var button in new[] { name, action })
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
                button.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Grid.SetColumn(name, 1); Grid.SetColumn(locked, 2); Grid.SetColumn(action, 3);
        header.Children.Add(signal); header.Children.Add(name); header.Children.Add(locked); header.Children.Add(action);
        var face = new Grid { Height = WifiScrollGeometry.Height, Visibility = Visibility.Collapsed };
        var sourceHost = new Grid(); sourceHost.Children.Add(header);
        var card = new Border { Child = sourceHost, Padding = new(10, 5, 10, 5), CornerRadius = new(18) };
        var rowGlass = AddGlass(face, card, 18); face.Children.Add(card);
        var contentEffect = new WifiNetworkContentEffect(sourceHost, header); face.Children.Add(contentEffect.Element);
        var offset = new TranslateTransform(); face.RenderTransform = offset;
        if (animations) card.BackgroundTransition = new BrushTransition { Duration = TimeSpan.FromMilliseconds(150) };
        var row = new NetworkRow(rowGlass, actionGlass, header, sourceHost, contentEffect, offset, text, actionText, action, signal, locked);
        name.Click += async (_, _) => { if (detailsKey is null && row.Network is { } network) await SelectAsync(network.Key); };
        action.Click += async (_, _) =>
        {
            if (detailsKey is not null) await ExecuteNetworkActionAsync();
            else if (row.Network is { } network) await SelectAsync(network.Key);
        };
        face.Tapped += async (_, e) =>
        {
            e.Handled = true;
            if (detailsKey is null && row.Network is { } network) await SelectAsync(network.Key);
        };
        return row;
    }

    private void SynchronizeRows(bool duringTransition = false, double? viewportHeight = null)
    {
        if (transitioning && !duringTransition) return;
        if (detailsKey is not null)
        {
            foreach (var row in rows)
                if (row.Network is { } old) row.Network = selection.Networks.FirstOrDefault(n => n.Key == old.Key);
        }
        else
        {
            var first = (int)(listScroll.VerticalOffset / WifiScrollGeometry.Pitch);
            var anchor = first < rows.Count ? rows[first].Network?.Key : null;
            var fraction = listScroll.VerticalOffset % WifiScrollGeometry.Pitch;
            foreach (var row in rows)
                if (row.Network is { } old) row.Network = selection.Networks.FirstOrDefault(n => n.Key == old.Key);
            foreach (var network in selection.Networks)
            {
                if (rows.Any(r => r.Network?.Key == network.Key)) continue;
                var row = rows.FirstOrDefault(r => r.Network is null);
                if (row is null) { row = CreateRow(); rows.Add(row); body.Children.Add(row.Part.Host); }
                row.Network = network;
            }
            foreach (var row in rows.Where(r => r.Network is null).ToArray())
            {
                if (rows.Count <= 3) break;
                rows.Remove(row); body.Children.Remove(row.Part.Host); row.ContentEffect.Dispose();
                foreach (var part in new[] { row.Part, row.ActionPart })
                { glassParts.Remove(part); glassSurfaces.Remove(part.Glass); part.Glass.Dispose(); GlassTargetsRevision++; }
            }
            ConfigureChevron(); UpdateListViewport(viewportHeight);
            if (expanded)
            {
                var index = anchor is not null ? rows.FindIndex(r => r.Network?.Key == anchor) : first;
                if (index < 0) index = first;
                var offset = Math.Clamp(Math.Max(0, index) * WifiScrollGeometry.Pitch + fraction, 0,
                    Math.Max(0, selection.Networks.Count - visibleSlots) * WifiScrollGeometry.Pitch);
                body.SetScroll(offset, true, visibleSlots); listScroll.ChangeView(null, offset, null, true);
            }
        }
        foreach (var row in rows) UpdateRow(row);
        UpdateConnectionInput();
        GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigureChevron()
    {
        var next = Math.Min(3, visibleSlots - 1);
        var candidate = detailsKey is null && !expanded && rows.Count > next && rows[next].Network is not null ? rows[next] : null;
        if (candidate == chevronRow) return;
        if (chevronRow is { } old)
        {
            old.Part.Fallback.Child = old.SourceHost; old.Part.Fallback.Padding = new(10, 5, 10, 5);
            old.Part.Host.Width = double.NaN;
        }
        chevronRow = candidate; chevron = null;
        if (candidate is null) return;
        var glyph = new UtilityIcon { Width = 14, Height = 14 };
        _ = glyph.SetAsync("caret-down", false, UtilityColors.Icon(theme, false));
        chevron = new Button { Content = glyph, Width = WifiScrollGeometry.Height, Height = WifiScrollGeometry.Height,
            HorizontalAlignment = HorizontalAlignment.Center, Padding = new(0), MinWidth = 0, MinHeight = 0,
            BorderThickness = new(0), CornerRadius = new(18), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        foreach (var resourceKey in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            chevron.Resources[resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(chevron, "Show more Wi-Fi networks"); ToolTipService.SetToolTip(chevron, "More networks");
        chevron.Click += async (_, _) => await ExpandListAsync();
        candidate.Part.Host.Width = WifiScrollGeometry.Height; candidate.Part.Fallback.Padding = new(0);
        candidate.Part.Fallback.Child = chevron;
    }

    private async Task ExpandListAsync()
    {
        if (disposed || transitioning || expanded || chevronRow is not { } row || chevron is not { } button) return;
        var current = ++viewRevision; transitioning = true; bodyHost.IsHitTestVisible = false;
        row.Part.Fallback.Child = null;
        var contents = new Grid();
        row.SourceHost.Margin = new(10, 5, 10, 5); row.Header.Opacity = 0;
        contents.Children.Add(row.SourceHost); contents.Children.Add(button); row.Part.Fallback.Child = contents;
        if (!await AnimateAsync(new[]
            {
                ((DependencyObject)row.Part.Host, "Width", WifiScrollGeometry.Height, Width),
                ((DependencyObject)row.Header, "Opacity", 0d, 1d),
                ((DependencyObject)button, "Opacity", 1d, 0d)
            }, 220, morphContent: true) || disposed || current != viewRevision) return;
        contents.Children.Clear(); row.SourceHost.Margin = new(0);
        row.Part.Fallback.Child = row.SourceHost; row.Part.Fallback.Padding = new(10, 5, 10, 5);
        row.Part.Host.Width = double.NaN; row.Header.Opacity = 1;
        expanded = true; chevronRow = null; chevron = null;
        transitioning = false; bodyHost.IsHitTestVisible = true; SynchronizeRows();
    }

    public void SetAvailableHeight(double height)
    {
        if (status.Visibility == Visibility.Visible)
        {
            status.Measure(new(Width, double.PositiveInfinity)); recovery.Measure(new(Width, double.PositiveInfinity));
            height -= status.DesiredSize.Height + recovery.DesiredSize.Height + Spacing * 2;
        }
        var slots = Math.Clamp((int)Math.Floor(height / WifiScrollGeometry.Pitch), 1, WifiScrollGeometry.VisibleSlots);
        if (slots == visibleSlots) return;
        if (transitioning) { CancelTransition(); RestoreList(); }
        visibleSlots = slots; ConfigureChevron(); UpdateListViewport();
    }

    private void UpdateListViewport(double? viewportHeight = null)
    {
        var height = SelectionHeight;
        if (detailsKey is null)
        {
            for (var i = 0; i < rows.Count; i++)
                rows[i].Part.Host.Visibility = rows[i].Network is not null &&
                    (expanded || i < Math.Min(3, visibleSlots - (chevronRow is null ? 0 : 1)) || rows[i] == chevronRow)
                    ? Visibility.Visible : Visibility.Collapsed;
            height = ListViewportHeight;
        }
        bodyHost.Height = viewportHeight ?? height;
        listScroll.VerticalScrollMode = expanded && detailsKey is null ? ScrollMode.Enabled : ScrollMode.Disabled;
        body.SetScroll(detailsKey is null && expanded ? listScroll.VerticalOffset : 0, expanded && detailsKey is null, visibleSlots);
    }

    private void UpdateScrollContent()
    {
        if (disposed || lastListLayout == body.LayoutRevision) return;
        lastListLayout = body.LayoutRevision;
        foreach (var row in rows)
        {
            var height = row.Part.Host.Height;
            var progress = expanded && detailsKey is null ? height / WifiScrollGeometry.Height : 1;
            row.Part.Fallback.Height = height;
            row.Part.Fallback.CornerRadius = new(Math.Min(18, height / 2));
            row.Part.Fallback.Visibility = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
            row.Part.Host.IsHitTestVisible = progress > 0;
            row.ContentEffect.Update(progress, row.Part.Host.ActualWidth, height);
            if (progress <= 0) row.Part.Glass.Element.Visibility = Visibility.Collapsed;
        }
        GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SelectAsync(string key)
    {
        if (networkBusy || disposed || transitioning || detailsKey is not null) return;
        selection.Select(key);
        if (selection.Selected is null) return;
        var selected = rows.First(r => r.Network?.Key == key);
        var y = selected.Part.Host.TransformToVisual(listScroll).TransformPoint(new(0, 0)).Y;
        savedListOffset = listScroll.VerticalOffset;
        var fromHeight = bodyHost.Height;
        var anchorIndex = (int)(savedListOffset / WifiScrollGeometry.Pitch);
        savedListAnchor = anchorIndex < rows.Count ? rows[anchorIndex].Network?.Key : null;
        detailsKey = key; ClearCredentials(); SetStatus("");
        var current = ++viewRevision; transitioning = true; bodyHost.IsHitTestVisible = false;
        try
        {
            var available = await wifi.ReadNetworksAsync(false, operation.Token);
            if (disposed || current != viewRevision) return;
            if (selection.Update(available))
            {
                CancelTransition(); RestoreList(); SynchronizeRows(); SetStatus("Network no longer available."); return;
            }
            foreach (var row in rows)
                if (row.Network is { } old) row.Network = selection.Networks.FirstOrDefault(n => n.Key == old.Key);
            if (selection.Selected is { Connected: false } connectionTarget)
            {
                var automatic = await WifiNetworkControl.ReadAutoConnectAsync(connectionTarget, operation.Token);
                if (disposed || current != viewRevision) return;
                autoConnect.IsChecked = automatic;
            }
            if (disposed || current != viewRevision) return;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (!disposed && current == viewRevision)
            {
                CancelTransition(); RestoreList(); SynchronizeRows();
                if (error is not OperationCanceledException) SetStatus(WifiNetworkControl.ErrorMessage(error));
            }
            return;
        }
        Canvas.SetZIndex(selected.Part.Host, 1);
        var remaining = rows.Where(r => r != selected && r.Part.Host.Visibility == Visibility.Visible && r.Part.Host.Height > 0)
            .Concat(rows.Where(r => r != selected)).Distinct().Take(2).ToArray();
        if (chevronRow is { } more)
        {
            more.Part.Fallback.Child = more.SourceHost; more.Part.Fallback.Padding = new(10, 5, 10, 5);
            more.Part.Host.Width = double.NaN; chevronRow = null; chevron = null;
        }
        foreach (var row in rows)
        {
            row.ContentEffect.Update(1, row.Part.Host.ActualWidth, WifiScrollGeometry.Height);
            row.Part.Host.Visibility = row == selected || remaining.Contains(row) ? Visibility.Visible : Visibility.Collapsed;
        }
        body.Children.Remove(selected.Part.Host); body.Children.Insert(0, selected.Part.Host);
        body.SetConnectionRow(selection.Selected is { } network && NeedsPassword(network)
            ? selected.Part.Host : remaining[0].Part.Host, ConnectionInputHeight);
        UpdateListViewport(fromHeight); listScroll.ChangeView(null, 0, null, true);
        selected.Offset.Y = y;
        var tracks = remaining.Select(r => ((DependencyObject)r.Header, "Opacity", r.Header.Opacity, 0d)).ToList();
        tracks.Add((selected.Offset, "Y", y, 0));
        tracks.Add((bodyHost, "Height", fromHeight, SelectionHeight));
        if (!await AnimateAsync(tracks, 180) || disposed || current != viewRevision) return;
        selected.Offset.Y = 0;
        passwordRow = remaining[0];
        foreach (var row in remaining) row.Part.Host.Visibility = Visibility.Visible;
        var input = new Grid { ColumnSpacing = 4 };
        input.RowDefinitions.Add(new() { Height = new(26) });
        input.RowDefinitions.Add(new() { Height = GridLength.Auto });
        input.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        input.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        input.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        input.Children.Add(password); Grid.SetColumn(reveal, 1); input.Children.Add(reveal);
        input.Children.Add(credentialHint);
        Grid.SetRow(autoConnect, 1); Grid.SetColumnSpan(autoConnect, 3); input.Children.Add(autoConnect);
        passwordRow.Header.Children.Remove(passwordRow.Action); Grid.SetColumn(passwordRow.Action, 2); input.Children.Add(passwordRow.Action);
        passwordRow.Part.Fallback.Child = input;
        UpdateConnectionInput();
        var backContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var arrow = new UtilityIcon { Width = 14, Height = 14 };
        _ = arrow.SetAsync("arrow-left", false, UtilityColors.Icon(theme, false));
        backContent.Children.Add(arrow); backContent.Children.Add(new TextBlock { Text = "Back to networks", FontSize = PopupStyle.CompactFontSize });
        var back = new Button { Content = backContent, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(0), MinWidth = 0, MinHeight = 0,
            BorderThickness = new(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        foreach (var resourceKey in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            back.Resources[resourceKey] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(back, "Back to networks");
        back.Click += async (_, _) => await BackAsync(); remaining[1].Part.Fallback.Child = back;
        var formControls = new FrameworkElement[] { password, reveal, passwordRow.Action, autoConnect, back };
        foreach (var control in formControls) control.Opacity = 0;
        foreach (var row in rows) UpdateRow(row);
        body.UpdateLayout(); GlassChanged?.Invoke(this, EventArgs.Empty);
        if (!await AnimateAsync(formControls.Select(control => ((DependencyObject)control, "Opacity", 0d, 1d)), 120)
            || disposed || current != viewRevision) return;
        transitioning = false; bodyHost.IsHitTestVisible = true; SynchronizeRows();
        if (selection.Selected is { } currentNetwork && NeedsPassword(currentNetwork)) password.Focus(FocusState.Programmatic);
    }

    private async Task BackAsync()
    {
        if (disposed || transitioning || detailsKey is null) return;
        var selected = rows.First(r => r.Network?.Key == detailsKey);
        var fromHeight = bodyHost.Height;
        var current = ++viewRevision; transitioning = true; bodyHost.IsHitTestVisible = false;
        ClearCredentials(); SetStatus("");
        var formRows = rows.Where(r => r != selected && r.Part.Host.Visibility == Visibility.Visible).ToArray();
        if (!await AnimateAsync(formRows.Select(r => ((DependencyObject)r.Part.Fallback.Child!, "Opacity", 1d, 0d)), 100)
            || disposed || current != viewRevision) return;
        RestoreList(); SynchronizeRows(duringTransition: true, viewportHeight: fromHeight);
        body.SetTransitionRow(selected.Part.Host, fromHeight, ListViewportHeight);
        var anchorIndex = savedListAnchor is not null ? rows.FindIndex(r => r.Network?.Key == savedListAnchor) : -1;
        if (anchorIndex >= 0) savedListOffset = anchorIndex * WifiScrollGeometry.Pitch + savedListOffset % WifiScrollGeometry.Pitch;
        savedListOffset = Math.Clamp(savedListOffset, 0, Math.Max(0, selection.Networks.Count - visibleSlots) * WifiScrollGeometry.Pitch);
        body.SetScroll(savedListOffset, expanded, visibleSlots); listScroll.ChangeView(null, savedListOffset, null, true);
        body.UpdateLayout();
        var y = selected.Part.Host.TransformToVisual(listScroll).TransformPoint(new(0, 0)).Y;
        selected.Offset.Y = -y;
        body.InvalidateMeasure(); body.UpdateLayout();
        var tracks = rows.Where(r => r != selected).SelectMany(r => new[]
        {
            ((DependencyObject)r.Header, "Opacity", 0d, 1d),
            ((DependencyObject)r.ContentEffect.Element, "Opacity", 0d, 1d)
        }).ToList();
        tracks.Add((selected.Offset, "Y", -y, 0));
        tracks.Add((bodyHost, "Height", fromHeight, ListViewportHeight));
        if (!await AnimateAsync(tracks, 180) || disposed || current != viewRevision) return;
        body.SetTransitionRow(null);
        transitioning = false; bodyHost.IsHitTestVisible = true; SynchronizeRows();
        if (selected.Part.Host.Height >= WifiScrollGeometry.Height)
            ((Button)selected.Header.Children[1]).Focus(FocusState.Programmatic);
        else listScroll.Focus(FocusState.Programmatic);
    }

    private void RestoreList()
    {
        body.SetConnectionRow(null);
        if (passwordRow is not null)
        {
            if (passwordRow.Action.Parent is Grid actionHost) actionHost.Children.Remove(passwordRow.Action);
            if (password.Parent is Grid input) input.Children.Clear();
            if (passwordRow.Part.Fallback.Child == autoConnect) passwordRow.Part.Fallback.Child = null;
            password.Opacity = reveal.Opacity = autoConnect.Opacity = passwordRow.Action.Opacity = 1;
            Grid.SetRow(passwordRow.Action, 0);
            Grid.SetColumn(passwordRow.Action, 3); passwordRow.Header.Children.Add(passwordRow.Action); passwordRow = null;
        }
        foreach (var row in rows)
        {
            if (row.Part.Fallback.Child is Grid contents && contents.Children.Contains(row.SourceHost)) contents.Children.Remove(row.SourceHost);
            row.Part.Fallback.Child = row.SourceHost; row.SourceHost.Opacity = 1;
            row.Part.Fallback.Padding = new(10, 5, 10, 5);
            row.SourceHost.Margin = new(0); row.Part.Host.Width = double.NaN;
            Grid.SetColumnSpan(row.SourceHost, 1);
            body.Children.Remove(row.Part.Host); body.Children.Add(row.Part.Host);
            row.Part.Host.Visibility = row.Network is null ? Visibility.Collapsed : Visibility.Visible;
            Canvas.SetZIndex(row.Part.Host, 0);
        }
        chevronRow = null; chevron = null;
        detailsKey = null; selection.Select(null); ClearCredentials();
        foreach (var row in rows) UpdateRow(row);
    }

    private Task<bool> AnimateAsync(IEnumerable<(DependencyObject Target, string Property, double From, double To)> tracks,
        int milliseconds, bool morphContent = false)
    {
        var frames = tracks.ToArray();
        var duration = new Duration(TimeSpan.FromMilliseconds(animations ? milliseconds : 0));
        transition = new(); transitionCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = transitionCompletion;
        var story = transition;
        foreach (var frame in frames)
        {
            var animation = new DoubleAnimation { From = frame.From, To = frame.To, Duration = duration,
                EnableDependentAnimation = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            if (morphContent && frame.Property == "Opacity")
            {
                animation.BeginTime = TimeSpan.FromTicks(frame.From == 0 ? duration.TimeSpan.Ticks / 2 : 0);
                animation.Duration = new Duration(TimeSpan.FromTicks((long)(duration.TimeSpan.Ticks * (frame.From == 0 ? .5 : .65))));
            }
            Storyboard.SetTarget(animation, frame.Target); Storyboard.SetTargetProperty(animation, frame.Property); transition.Children.Add(animation);
        }
        transition.Completed += (_, _) =>
        {
            foreach (var frame in frames)
            {
                if (frame.Target is TranslateTransform offset) offset.Y = frame.To;
                else if (frame.Property == "Width" && frame.Target is FrameworkElement sized) sized.Width = frame.To;
                else if (frame.Property == "Height" && frame.Target is FrameworkElement sizedHeight) sizedHeight.Height = frame.To;
                else if (frame.Target is UIElement element) element.Opacity = frame.To;
            }
            story.Stop(); CompositionTarget.Rendering -= TransitionFrame; GlassChanged?.Invoke(this, EventArgs.Empty);
            completion.TrySetResult(true);
        };
        CompositionTarget.Rendering -= TransitionFrame;
        if (animations) CompositionTarget.Rendering += TransitionFrame;
        transition.Begin(); return completion.Task;
    }

    private void TransitionFrame(object? sender, object args)
    {
        body.InvalidateMeasure(); GlassChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CancelTransition()
    {
        viewRevision++; transition?.Stop(); transitionCompletion?.TrySetResult(false); transitioning = false;
        CompositionTarget.Rendering -= TransitionFrame;
        body.SetTransitionRow(null);
        bodyHost.IsHitTestVisible = true;
        foreach (var row in rows)
        { row.Offset.Y = 0; row.Part.Host.Opacity = row.Header.Opacity = row.ContentEffect.Element.Opacity = 1; }
    }

    private void SetNetworkBusy(bool value, bool disconnect = false)
    {
        pendingNetworkKey = value ? selection.SelectedKey : null;
        disconnecting = value && disconnect;
        password.IsEnabled = reveal.IsEnabled = autoConnect.IsEnabled = refresh.IsEnabled = !value;
        foreach (var row in rows)
        {
            UpdateRow(row);
        }
    }

    private async Task ExecuteNetworkActionAsync()
    {
        if (selection.Selected is not { } network || networkBusy || disposed || transitioning) return;
        var disconnect = network.Connected;
        if (NeedsPassword(network) && password.Password.Length == 0)
        { SetStatus("Password required."); password.Focus(FocusState.Programmatic); return; }
        SetNetworkBusy(true, disconnect); SetStatus(""); var current = ++revision; var token = operation.Token;
        try
        {
            var result = disconnect ? await WifiNetworkControl.DisconnectAsync(network, token)
                : await WifiNetworkControl.ConnectAsync(network, password.Password, autoConnect.IsChecked == true, token);
            if (disposed || current != revision) return;
            if (disconnect && result != "Disconnected.") { SetStatus(result); return; }
            ClearCredentials();
            if (!disconnect && result != "Connected." && network.Secured) passwordRetryKey = network.Key;
            var available = await wifi.ReadNetworksAsync(false, token);
            if (disposed || current != revision) return;
            ApplyNetworks(available, completed: true);
            var connected = selection.Networks.Any(n => n.Key == network.Key && n.Connected);
            SetStatus(disconnect ? connected ? "Windows still reports this network as connected." : ""
                : result == "Connected." ? connected ? "" : "Disconnected" : result);
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
                SetNetworkBusy(false);
            }
        }
    }

    private void ClearCredentials() { password.Password = ""; reveal.IsChecked = false; passwordRetryKey = null; }
    private void SetStatus(string text)
    {
        status.Text = text; status.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        recovery.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool NeedsPassword(WifiNetwork network) => !network.Connected &&
        (network.RequiresPassword || network.Secured && passwordRetryKey == network.Key);
    private void UpdateConnectionInput()
    {
        if (passwordRow is null || selection.Selected is not { } network) return;
        var required = NeedsPassword(network);
        var compact = CompactConnectionInput;
        var selected = rows.First(r => r.Network?.Key == detailsKey);
        var input = (Grid)password.Parent;
        var actionHost = required ? input : selected.Header;
        if (passwordRow.Action.Parent != actionHost)
        {
            if (passwordRow.Action.Parent is Grid previousHost) previousHost.Children.Remove(passwordRow.Action);
            actionHost.Children.Add(passwordRow.Action);
        }
        Grid.SetColumn(passwordRow.Action, required ? 2 : 3);
        Grid.SetRow(passwordRow.Action, required ? 1 : 0);
        if (required && selected.Part.Fallback.Child != input)
        {
            input.Children.Remove(autoConnect);
            selected.Part.Fallback.Child = null;
            passwordRow.Part.Fallback.Child = null;
            Grid.SetRow(selected.SourceHost, 0); Grid.SetColumnSpan(selected.SourceHost, 3);
            input.Children.Add(selected.SourceHost);
            selected.Part.Fallback.Child = input;
            passwordRow.Part.Fallback.Child = autoConnect;
        }
        else if (!required && selected.Part.Fallback.Child == input)
        {
            input.Children.Remove(selected.SourceHost);
            selected.Part.Fallback.Child = selected.SourceHost;
            passwordRow.Part.Fallback.Child = null;
            passwordRow.Part.Fallback.Child = input;
            input.Children.Add(autoConnect);
        }
        input.RowSpacing = required ? 2 : 0;
        Grid.SetRow(password, required ? 1 : 0);
        Grid.SetRow(reveal, required ? 1 : 0);
        password.Visibility = reveal.Visibility = required ? Visibility.Visible : Visibility.Collapsed;
        autoConnect.Visibility = Visibility.Visible;
        Grid.SetRow(autoConnect, required || compact ? 0 : 1);
        Grid.SetColumnSpan(autoConnect, 3);
        autoConnect.Margin = new(0);
        credentialHint.Visibility = required || compact ? Visibility.Collapsed : Visibility.Visible;
        credentialHint.Text = network.Connected ? "Connected" : string.IsNullOrEmpty(network.Profile) ? "No password required" : "Saved network";
        body.SetConnectionRow(required ? selected.Part.Host : passwordRow.Part.Host, ConnectionInputHeight);
        if (!transitioning) UpdateListViewport();
        if (!required) ClearCredentials();
    }
    private void UpdateRow(NetworkRow row)
    {
        if (row.Network is { } network)
        {
            row.Name.Text = network.Name;
            AutomationProperties.SetName((Button)row.Header.Children[1], network.Name);
            ToolTipService.SetToolTip(row.Header.Children[1], network.Name);
            row.Lock.Visibility = detailsKey is null && network.Secured ? Visibility.Visible : Visibility.Collapsed;
            row.Part.Highlight = network.Key == detailsKey ? (byte)16 : network.Connected ? (byte)8 : (byte)0;
            UpdateGlassFill(row.Part);
            _ = row.Signal.SetAsync(network.Signal >= 70 ? "wifi-high" : network.Signal >= 35 ? "wifi-medium" : "wifi-low",
                network.Connected, UtilityColors.Icon(theme, false));
        }
        var target = row == passwordRow ? selection.Selected : row.Network;
        row.Action.Visibility = target is not null && (detailsKey is null || row == passwordRow) ? Visibility.Visible : Visibility.Collapsed;
        row.ActionText.Text = networkBusy && pendingNetworkKey == target?.Key ? disconnecting ? "Disconnecting" : "Connecting"
            : target?.Connected == true ? row == passwordRow ? "Disconnect" : "Connected" : "Connect";
        row.Action.IsEnabled = !networkBusy && target is not null &&
            (target.Connected ? row == passwordRow : target.Connectable);
        AutomationProperties.SetName(row.Action, target?.Connected == true
            ? (row == passwordRow ? "Disconnect from " : "Connected to ") + target.Name : "Connect to " + target?.Name);
        AutomationProperties.SetItemStatus(row.Action, row.ActionText.Text);
    }

    private GlassPart AddGlass(Grid host, Border fallback, float radius)
    {
        var glass = new UtilityGlassSurface { Circular = false, CornerRadius = radius };
        var layer = new Canvas { IsHitTestVisible = false }; layer.Children.Add(glass.Element); host.Children.Add(layer);
        var part = new GlassPart(host, glass, fallback); glassParts.Add(part);
        glassSurfaces.Add(glass); GlassTargetsRevision++;
        glass.Configure(DockAppearanceMode.Clear, backdrop, optics, highlights, layoutScale);
        glass.RenderingChanged += (_, _) => UpdateGlassFill(part);
        host.SizeChanged += (_, _) => GlassChanged?.Invoke(this, EventArgs.Empty);
        host.Loaded += (_, _) => GlassChanged?.Invoke(this, EventArgs.Empty);
        host.PointerEntered += (_, _) => { part.Hovered = true; UpdateGlassFill(part); };
        host.PointerExited += (_, _) => { part.Hovered = false; UpdateGlassFill(part); };
        UpdateGlassFill(part);
        return part;
    }
    private void UpdateGlassFill(GlassPart part)
    {
        var shade = theme == UtilityTheme.Light ? (byte)245 : (byte)28;
        var fillShade = part.Glass.HasFrame ? part.Hovered ? (byte)255 : shade
            : (byte)Math.Min(255, shade + (part.Hovered ? 10 : 0));
        part.Fallback.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(
            part.Glass.HasFrame ? (byte)Math.Min(255, part.Highlight + (part.Hovered ? 20 : 0)) : (byte)(part.Hovered ? 245 : 235),
            fillShade, fillShade, fillShade));
    }
    public void ConfigureGlass(GlassMaterial material, LiquidGlassMaterial clearOptics, float scale, bool showHighlights)
    {
        backdrop = material; optics = clearOptics; layoutScale = scale; highlights = showHighlights;
        foreach (var part in glassParts) part.Glass.Configure(DockAppearanceMode.Clear, backdrop, optics, highlights, scale);
    }
    public bool PositionGlass(FrameworkElement relativeTo, Vector2 origin, bool active)
    {
        var changed = false;
        var rasterScale = relativeTo.XamlRoot?.RasterizationScale ?? 1;
        foreach (var part in glassParts)
        {
            if (!part.Host.IsLoaded || !relativeTo.IsLoaded || !IsVisible(part.Host))
            { changed |= part.Glass.Active; part.Glass.Active = false; continue; }
            var bounds = part.Host.TransformToVisual(relativeTo).TransformBounds(new(0, 0, part.Host.ActualWidth, part.Host.ActualHeight));
            var next = new Vector4((float)bounds.X + origin.X, (float)bounds.Y + origin.Y, (float)bounds.Width, (float)bounds.Height);
            var visible = active && bounds.Width * rasterScale >= 1 && bounds.Height * rasterScale >= 1;
            if (!visible) part.Glass.Element.Visibility = Visibility.Collapsed;
            changed |= part.Glass.Bounds != next || part.Glass.Active != visible;
            part.Glass.Bounds = next; part.Glass.Active = visible;
        }
        return changed;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; revision++; wifi.Changed -= RadioChanged;
        CancelTransition(); operation.Cancel(); operation.Dispose(); ClearCredentials();
        foreach (var row in rows) row.ContentEffect.Dispose();
        foreach (var part in glassParts) part.Glass.Dispose();
    }

    private static bool IsVisible(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: Visibility.Collapsed }) return false;
        return true;
    }
}
