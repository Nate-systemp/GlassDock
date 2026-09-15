using System.Diagnostics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

/// <summary>Retained, interactive Home HWND. Only the visible session polls the pointer.</summary>
internal sealed class GlassHomeWindow : Window
{
    private readonly GlassHomeSession session = new();
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly GlassSurface surface = new() { UseDesktopBackdrop = true };
    private readonly Grid root = new() { RequestedTheme = ElementTheme.Dark };
    private readonly TextBox search = new()
    {
        PlaceholderText = "Search apps and settings…", FontSize = 23,
        BorderThickness = new Thickness(0), Padding = new Thickness(0, 8, 0, 8),
        BorderBrush = Brush(0), Background = Brush(0), Foreground = Brush(245),
        VerticalAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = false
    };
    private readonly StackPanel extra = new() { Spacing = 18, Margin = new Thickness(24, 12, 24, 24) };
    private readonly WindowsApplicationIndex applicationIndex = new();
    private readonly WindowsApplicationLauncher launcher = new();
    private readonly GlassSearchSelection selection = new();
    private readonly IReadOnlyList<GlassSearchResult> settings = WindowsSettingsCatalog.Entries.Select(entry => entry.ToSearchResult()).ToArray();
    private readonly StackPanel resultsPanel = new() { Margin = new Thickness(16, 0, 16, 12), Spacing = 4, Visibility = Visibility.Collapsed };
    private readonly ListView resultsList = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
        IsTabStop = false, SingleSelectionFollowsFocus = false };
    private readonly TextBlock searchStatus = new() { Margin = new Thickness(12, 4, 12, 4), TextWrapping = TextWrapping.Wrap, Foreground = Brush(190) };
    private ScrollViewer browsePanel = null!;
    private bool launching;
    private string? launchError;
    private int refreshQueued;
    private bool HasQuery => !string.IsNullOrWhiteSpace(search.Text);
    private readonly TranslateTransform contentOffset = new();
    private readonly DispatcherQueueTimer polling;
    private readonly DispatcherQueueTimer animation;
    private readonly InteractiveGlassWindowHost host;
    private readonly nint hwnd;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double animationStart, progress, scale = 1, width = 660, expandedHeight = 440;
    private int x, y;
    private bool closed, changingVisibility;
    private bool active, searchFocusPending;

    public GlassHomeWindow()
    {
        Title = "GlassDock — Glass Home";
        var material = GlassMaterialPresets.Create(GlassMaterialPreset.Frosted) with
        {
            BlurAmount = 28, Opacity = .84, CornerRadius = 20, ShadowOpacity = .20,
            ShadowBlur = 24, ShadowOffset = 6, EdgeHighlight = 0, BorderOpacity = 0,
            BorderThickness = 0
        };
        surface.Apply(material);
        backdrop.Apply(material);
        root.Children.Add(surface);
        root.Children.Add(CreateContent());
        Content = root;
        SystemBackdrop = backdrop;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        host = new InteractiveGlassWindowHost(hwnd);
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        polling = DispatcherQueue.CreateTimer();
        polling.Interval = TimeSpan.FromMilliseconds(25);
        polling.Tick += Poll;
        animation = DispatcherQueue.CreateTimer();
        animation.Interval = TimeSpan.FromMilliseconds(16);
        animation.Tick += Animate;
        search.TextChanged += OnQueryChanged;
        search.PreviewKeyDown += OnSearchKeyDown;
        resultsList.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ListViewItem { Tag: GlassSearchResult result }) _ = LaunchResultAsync(result);
        };
        applicationIndex.Changed += OnIndexChanged;
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape) return;
            HideHome(); e.Handled = true;
        }), true);
        Activated += OnActivated;
        root.Loaded += (_, _) => { UpdateGlassBounds(); RequestSearchFocus(); };
        surface.SizeChanged += (_, _) => UpdateGlassBounds();
        Closed += (_, _) =>
        {
            closed = true; session.Hide();
            searchFocusPending = false;
            Activated -= OnActivated;
            polling.Stop(); animation.Stop();
            polling.Tick -= Poll; animation.Tick -= Animate;
            search.TextChanged -= OnQueryChanged;
            search.PreviewKeyDown -= OnSearchKeyDown;
            applicationIndex.Changed -= OnIndexChanged;
            applicationIndex.Dispose();
            host.Dispose();
        };
    }

    private Grid CreateContent()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new() { Height = new GridLength(86) });
        content.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var searchRow = new Grid { Margin = new Thickness(26, 0, 26, 0), ColumnSpacing = 16 };
        searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 22, Foreground = Brush(165) });
        // Keep the native editor/caret/IME, but remove WinUI's inset field and accent underline in every state.
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBackgroundDisabled", "TextControlBorderBrush", "TextControlBorderBrushPointerOver",
            "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled", "TextControlElevationBorderBrush" })
            search.Resources[key] = Brush(0);
        search.Resources["TextControlBorderThemeThickness"] = new Thickness(0);
        search.Resources["TextControlPlaceholderForeground"] = Brush(155);
        search.Resources["TextControlPlaceholderForegroundFocused"] = Brush(155);
        search.Resources["TextControlPlaceholderForegroundPointerOver"] = Brush(175);
        resultsList.Resources["ListViewItemBackgroundSelected"] = Brush(28);
        resultsList.Resources["ListViewItemBackgroundSelectedPointerOver"] = Brush(38);
        resultsList.Resources["ListViewItemBackgroundSelectedPressed"] = Brush(46);
        resultsList.Resources["ListViewItemBackgroundPointerOver"] = Brush(16);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, "Search apps and settings");
        Grid.SetColumn(search, 1); searchRow.Children.Add(search); content.Children.Add(searchRow);
        extra.RenderTransform = contentOffset;
        extra.Children.Add(Label("PINNED", 11));
        var categories = new Grid { ColumnSpacing = 10 };
        foreach (var name in new[] { "Apps", "Files", "Projects", "More" })
        {
            var index = categories.ColumnDefinitions.Count;
            categories.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var tile = new Border
            {
                Height = 72, CornerRadius = new CornerRadius(12), Background = Brush(14),
                Child = new TextBlock { Text = name, Foreground = Brush(190), HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center }
            };
            Grid.SetColumn(tile, index); categories.Children.Add(tile);
        }
        extra.Children.Add(categories);
        extra.Children.Add(Label("RECENT", 11));
        extra.Children.Add(Label("No recent items yet", 14));
        var footer = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(Label("GlassDock", 12));
        footer.Children.Add(new TextBlock { Text = "Power", Foreground = Brush(120), HorizontalAlignment = HorizontalAlignment.Right });
        extra.Children.Add(footer);
        browsePanel = new ScrollViewer
        {
            Content = extra, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(browsePanel, 1); content.Children.Add(browsePanel);
        resultsPanel.Children.Add(resultsList);
        resultsPanel.Children.Add(searchStatus);
        Grid.SetRow(resultsPanel, 1); content.Children.Add(resultsPanel);
        return content;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs args)
    {
        launchError = null;
        RefreshResults(false);
    }

    private void OnIndexChanged(object? sender, EventArgs args)
    {
        // Coalesce worker notifications; always filter the CURRENT query on the UI thread.
        if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref refreshQueued, 0);
            if (!closed && session.State != GlassHomeState.Hidden) RefreshResults(true);
        })) Interlocked.Exchange(ref refreshQueued, 0);
    }

    private void RefreshResults(bool preserveSelection)
    {
        if (closed) return;
        var snapshot = applicationIndex.Snapshot;
        selection.Replace(GlassSearch.Find(snapshot.Applications.Concat(settings), search.Text), preserveSelection);
        resultsList.Items.Clear();
        foreach (var result in selection.Results)
        {
            var row = new Grid { ColumnSpacing = 12, Height = 52 };
            row.ColumnDefinitions.Add(new() { Width = new GridLength(36) });
            row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            if (result.Icon is not null)
            {
                var icon = new AdaptiveAppIcon(32, 1); icon.SetIcon(result.Icon); row.Children.Add(icon);
            }
            else row.Children.Add(new FontIcon { Glyph = result.ResultType == GlassSearchResultType.Setting ? "\uE713" : "\uE71D",
                FontSize = 24, Foreground = Brush(220) });
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            labels.Children.Add(new TextBlock { Text = result.Title, FontSize = 16, Foreground = Brush(245), TextTrimming = TextTrimming.CharacterEllipsis });
            labels.Children.Add(new TextBlock { Text = result.Subtitle, FontSize = 12, Foreground = Brush(180), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var item = new ListViewItem { Content = row, Tag = result, IsTabStop = false, CornerRadius = new CornerRadius(10),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 2, 10, 2) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, result.Title + ", " + result.Subtitle);
            resultsList.Items.Add(item);
        }
        resultsList.SelectedIndex = selection.Index;
        searchStatus.Text = launchError ?? (snapshot.IsIndexing ? "Indexing applications…" : snapshot.Warning ??
            (selection.Results.Count == 0 ? "No results" : ""));
        searchStatus.Visibility = HasQuery && searchStatus.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (session.State != GlassHomeState.Hidden) ApplyFrame();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is global::Windows.System.VirtualKey.Down or global::Windows.System.VirtualKey.Up)
        {
            selection.Move(args.Key == global::Windows.System.VirtualKey.Down ? 1 : -1);
            resultsList.SelectedIndex = selection.Index;
            if (selection.Index >= 0) resultsList.ScrollIntoView(resultsList.Items[selection.Index]);
            args.Handled = true;
        }
        else if (args.Key == global::Windows.System.VirtualKey.Enter)
        {
            if (selection.Selected is { } result) _ = LaunchResultAsync(result);
            args.Handled = true;
        }
    }

    private async Task LaunchResultAsync(GlassSearchResult result)
    {
        if (launching || closed || session.State == GlassHomeState.Hidden) return;
        launching = true;
        var revision = session.Revision;
        try
        {
            var success = await launcher.LaunchTargetAsync(result.LaunchTarget);
            if (closed || session.Revision != revision) return;
            if (success) HideHome();
            else
            {
                launchError = $"Could not open {result.Title}. It may have been moved or removed.";
                searchStatus.Text = launchError;
                searchStatus.Visibility = Visibility.Visible;
                ApplyFrame(); TryFocusSearch();
            }
        }
        finally { launching = false; }
    }

    public void Toggle()
    {
        if (closed || changingVisibility) return;
        if (session.State != GlassHomeState.Hidden) { HideHome(); return; }
        changingVisibility = true;
        try
        {
            // Capture before activation/layout so a stationary pointer inside the new window stays Compact.
            session.Open(GlassHomeInput.Read(hwnd));
            searchFocusPending = true;
            animation.Stop(); progress = 0; search.Text = "";
            applicationIndex.Start();
            ConfigurePlacement(); RefreshResults(false); ApplyFrame();
            AppWindow.Show(); Activate();
            ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;
            UpdateGlassBounds(); RequestSearchFocus();
            polling.Start();
        }
        catch { HideHome(); throw; }
        finally { changingVisibility = false; }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (active) RequestSearchFocus();
    }

    private void RequestSearchFocus()
    {
        if (closed || !searchFocusPending || session.State == GlassHomeState.Hidden) return;
        // Give immediate typing a focus target, then confirm it after WinUI has
        // finished activation and restored focus to its XAML content island.
        TryFocusSearch();
        var revision = session.Revision;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || !searchFocusPending || session.Revision != revision) return;
            if (TryFocusSearch()) searchFocusPending = false;
            // If activation or loading is still pending, its event retries this
            // request. No timer, delay, or focus stealing after it is fulfilled.
        });
    }

    private bool TryFocusSearch() => active && search.IsLoaded && search.XamlRoot is not null &&
        session.State != GlassHomeState.Hidden && search.Focus(FocusState.Programmatic);

    public void HideHome()
    {
        if (closed) return;
        searchFocusPending = false;
        active = false;
        session.Hide(); polling.Stop(); animation.Stop();
        AppWindow.Hide(); // Keep the HWND and SystemBackdrop; reconnect restores the retained mask.
    }

    private void Poll(DispatcherQueueTimer sender, object args)
    {
        if (closed || changingVisibility || session.State == GlassHomeState.Hidden) return;
        var before = session.State;
        if (GlassHomeInput.Read(hwnd) is { } pointer) session.Observe(pointer);
        if (session.State == GlassHomeState.Hidden) { HideHome(); return; }
        if (Math.Abs(GlassHomeInput.Scale(hwnd) - scale) > .001) { ConfigurePlacement(); ApplyFrame(); }
        if (before == GlassHomeState.Compact && session.State == GlassHomeState.Expanded)
        {
            animationStart = clock.Elapsed.TotalMilliseconds;
            animation.Start();
        }
    }

    private void Animate(DispatcherQueueTimer sender, object args)
    {
        if (closed || session.State != GlassHomeState.Expanded) { animation.Stop(); return; }
        var t = Math.Clamp((clock.Elapsed.TotalMilliseconds - animationStart) / 200, 0, 1);
        progress = t * t * (3 - 2 * t);
        ApplyFrame();
        if (t == 1) animation.Stop();
    }

    private void ConfigurePlacement()
    {
        scale = GlassHomeInput.Scale(hwnd);
        var area = DisplayArea.Primary.WorkArea;
        width = Math.Min(660, Math.Max(1, area.Width / scale - 24));
        expandedHeight = Math.Min(440, Math.Max(86, area.Height / scale - 24));
        x = area.X + (int)Math.Round((area.Width - width * scale) / 2);
        // Center the search bar, reserving room below it so its top edge never jumps during expansion.
        y = area.Y + (int)Math.Max(0, Math.Min((area.Height - 86 * scale) / 2, area.Height - expandedHeight * scale - 12 * scale));
    }

    private void ApplyFrame()
    {
        var browseHeight = 86 + (expandedHeight - 86) * progress;
        var availableHeight = Math.Max(86, (DisplayArea.Primary.WorkArea.Y + DisplayArea.Primary.WorkArea.Height - y) / scale - 12);
        var statusHeight = searchStatus.Visibility == Visibility.Visible ? 56 : 0;
        var searchHeight = Math.Min(availableHeight, 86 + Math.Max(56, selection.Results.Count * 60 + statusHeight) + 12);
        var height = HasQuery ? searchHeight : browseHeight;
        resultsList.MaxHeight = Math.Max(0, height - 86 - 12 - statusHeight);
        resultsPanel.Visibility = HasQuery ? Visibility.Visible : Visibility.Collapsed;
        browsePanel.Visibility = HasQuery ? Visibility.Collapsed : Visibility.Visible;
        surface.Width = root.Width = width;
        surface.Height = root.Height = height;
        root.Clip = new RectangleGeometry { Rect = new(0, 0, width, height) };
        extra.Visibility = session.State == GlassHomeState.Compact ? Visibility.Collapsed : Visibility.Visible;
        extra.Opacity = progress;
        extra.IsHitTestVisible = progress == 1;
        contentOffset.Y = 8 * (1 - progress);
        AppWindow.MoveAndResize(new(x, y, (int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale)));
        UpdateGlassBounds();
    }

    private void UpdateGlassBounds() => backdrop.SetBounds(width, surface.Height, width, surface.Height, 0,
        surface.XamlRoot?.RasterizationScale ?? scale);

    private static SolidColorBrush Brush(byte alpha) => new(global::Windows.UI.Color.FromArgb(alpha, 245, 248, 255));
    private static TextBlock Label(string text, double size) => new() { Text = text, FontSize = size, Foreground = Brush(180) };
}
