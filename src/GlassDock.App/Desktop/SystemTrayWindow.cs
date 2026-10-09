using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Settings;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Applications;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

/// <summary>
/// GlassDock's hidden-tray surface. Windows does not expose a supported public API
/// for enumerating every third-party notification icon, so this window uses the
/// existing accessibility providers as a best-effort bridge and distinguishes
/// registry-derived application shortcuts from live actions.
/// </summary>
internal sealed class SystemTrayWindow : Window
{
    private const double PanelWidth = 352;
    private const double PanelHeight = 406;
    private const double Gutter = UtilityPopupStyle.Gutter;

    private readonly WindowsSystemControlService controls;
    private readonly WindowsApplicationService applicationService;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new() { Background = Brush(0) };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly Grid trayGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly TextBlock status = new()
    {
        FontSize = 11.5,
        Foreground = Brush(165),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly UtilityPopupPresentation presentation;
    private bool closed;
    private string[] itemKeys = [];
    private IReadOnlyList<WindowsTrayAccessibility.TrayItem> displayedItems = [];
    private int refreshVersion;
    private bool refreshRunning;
    private CancellationTokenSource? iconLoadCts;
    private readonly TrayOrderStore orderStore = new();
    private readonly Task<TrayOrder> loadOrder;
    private TrayOrder? order;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer refreshTimer;
    private string? draggedKey;
    private bool activationPending;
    private bool menuOpen;
    private readonly TrayActivationGate activationGate = new();
    private const string DragFormat = "Doky.Tray.Reorder";
    private static string Key(WindowsTrayAccessibility.TrayItem item) =>
        TrayIdentity.Key(item.Source == WindowsTrayAccessibility.TrayItemSource.Registry, item.Name, item.ExecutablePath);

    public SystemTrayWindow(
        WindowsSystemControlService controls,
        WindowsApplicationService applicationService,
        DockAppearanceSettings appearance,
        DockAppearanceMode dockMode,
        Func<bool>? utilityOwnsPointer = null)
    {
        loadOrder = orderStore.LoadAsync();
        refreshTimer = DispatcherQueue.CreateTimer();
        refreshTimer.Interval = TimeSpan.FromSeconds(3);
        refreshTimer.Tick += (_, _) =>
        {
            if (!closed && presentation?.IsVisible == true && draggedKey is null && !menuOpen && !activationPending && !refreshRunning)
                _ = RefreshAsync();
        };
        trayGrid.ChildrenTransitions = new TransitionCollection { new RepositionThemeTransition() };
        this.controls = controls;
        this.applicationService = applicationService;
        Title = "Doky Hidden Tray";
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
        var content = new Grid
        {
            Margin = new Thickness(Gutter + 17, Gutter + 17, Gutter + 17, Gutter + 17),
            RowSpacing = 10
        };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(41) });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "TRAY",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 10.5,
            CharacterSpacing = 150,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = theme.Muted,
            VerticalAlignment = VerticalAlignment.Center
        });
        var headerActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5, VerticalAlignment = VerticalAlignment.Center
        };
        headerActions.Children.Add(IconButton("\uE72C", "Refresh tray", () => _ = RefreshAsync()));
        headerActions.Children.Add(IconButton("\uE712", "Windows tray settings", controls.OpenTraySettings));
        Grid.SetColumn(headerActions, 1);
        header.Children.Add(headerActions);
        content.Children.Add(header);

        for (var i = 0; i < 3; i++)
            trayGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var scroll = new ScrollViewer
        {
            Content = trayGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);

        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        status.Foreground = theme.Muted;
        status.VerticalAlignment = VerticalAlignment.Center;
        status.FontSize = 11;
        footer.Children.Add(status);
        var settings = TextButton("Tray settings", controls.OpenTraySettings);
        Grid.SetColumn(settings, 1);
        footer.Children.Add(settings);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);
        root.Children.Add(content);
        Content = root;
        presentation = new UtilityPopupPresentation(
            this,
            root,
            backdrop,
            utilityOwnsPointer);

        presentation.Hidden += (_, _) => refreshTimer.Stop();

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
            _ = RefreshAsync();
        };
        root.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Dismiss();
            }
        };
        Closed += (_, _) =>
        {
            closed = true;
            refreshTimer.Stop();
            iconLoadCts?.Cancel();
            iconLoadCts?.Dispose();
            iconLoadCts = null;
            host.Dispose();
        };
    }

    public event EventHandler? Hidden
    {
        add => presentation.Hidden += value;
        remove => presentation.Hidden -= value;
    }

    public event EventHandler? Dismissed
    {
        add => presentation.Dismissed += value;
        remove => presentation.Dismissed -= value;
    }

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
        if (displayedItems.Count > 0)
        {
            // Rebuild only on mode change: real app icons remain loaded from
            // their source and must not be replaced by decorative stand-ins.
            itemKeys = [];
            RenderItems(displayedItems);
        }
    }

    public void Present()
    {
        activationPending = false;
        refreshTimer.Start();
        // Cached tray windows must refresh when they are shown again.
        _ = RefreshAsync();
        presentation.Present();
    }

    public void RetargetClosed() => presentation.RetargetClosed();
    public void HideImmediately() => presentation.HideImmediately();
    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(
        AppWindow owner,
        double scale,
        double anchorX,
        double anchorY,
        double dockTop)
    {
        UtilityPopupStyle.Position(
            AppWindow,
            owner,
            scale,
            anchorX,
            dockTop,
            PanelWidth,
            PanelHeight);

        scale = double.IsFinite(scale) && scale > 0
            ? scale
            : 1;

        presentation.SetTargetWindowGeometry(
            owner.Position.X + anchorX * scale,
            owner.Position.Y + anchorY * scale,
            scale);
        host.InputHeightPixels = presentation.InputHeightPixels;
    }


    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (closed || draggedKey is not null || menuOpen)
            return;

        // Do not queue refreshes. A later request supersedes the in-flight scan.
        var version = ++refreshVersion;
        if (refreshRunning)
            return;

        refreshRunning = true;
        try
        {
            status.Text = "Reading Windows hidden tray…";

            order ??= await loadOrder;
            // Read-only scan on an STA worker: never realize/show Explorer UI.
            var items = await WindowsTrayAccessibility.ReadHiddenItemsAsync();

            if (closed || draggedKey is not null || menuOpen || activationPending || version != refreshVersion)
                return;

            RenderItems(items);
        }
        catch (Exception)
        {
            if (!closed && version == refreshVersion)
                status.Text = "Could not refresh tray apps. Try Refresh.";
        }
        finally
        {
            refreshRunning = false;

            // If another refresh request arrived while scanning, run exactly one
            // more pass rather than building an unbounded Task queue.
            if (!closed && version != refreshVersion)
                _ = RefreshAsync();
        }
    }

    private void RenderItems(IReadOnlyList<WindowsTrayAccessibility.TrayItem> items)
    {
        var byKey = items.GroupBy(Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        items = (order ?? new TrayOrder()).Apply(byKey.Keys).Select(k => byKey[k]).ToArray();
        displayedItems = items;
        var nextKeys = items
            .Select(item => item.Name + "\n" + item.DefaultAction + "\n" + item.ExecutablePath + "\n" + item.Source)
            .ToArray();

        status.Text = GetTrayStatus(items);
        if (itemKeys.SequenceEqual(nextKeys) && trayGrid.Children.Count > 0)
        {
            // Preserve focus, pointer capture, and scroll position during refresh.
            foreach (var button in trayGrid.Children.OfType<Button>())
                button.Tag = byKey[Key((WindowsTrayAccessibility.TrayItem)button.Tag)];
            return;
        }

        displayedItems = items.ToArray();
        itemKeys = nextKeys;
        iconLoadCts?.Cancel();
        iconLoadCts?.Dispose();
        var iconLoad = new CancellationTokenSource();
        iconLoadCts = iconLoad;
        trayGrid.Children.Clear();
        trayGrid.RowDefinitions.Clear();

        if (items.Count == 0)
        {
            status.Text = "No active tray apps";

            trayGrid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            var empty = new TextBlock
            {
                Text = "No active hidden tray apps are available right now.",
                FontSize = 13,
                Foreground = theme.Secondary,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 34, 12, 12)
            };

            Grid.SetColumnSpan(empty, 3);
            trayGrid.Children.Add(empty);
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var row = index / 3;
            var column = index % 3;

            while (trayGrid.RowDefinitions.Count <= row)
                trayGrid.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });

            var tile = TrayButton(item, iconLoad.Token);
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            trayGrid.Children.Add(tile);
        }
    }

    private static string GetTrayStatus(IReadOnlyList<WindowsTrayAccessibility.TrayItem> items)
    {
        if (items.Count == 0)
            return "No active tray apps";

        var fallbackCount = items.Count(item =>
            item.Source == WindowsTrayAccessibility.TrayItemSource.Registry);

        return fallbackCount == 0
            ? $"{items.Count} tray app{(items.Count == 1 ? "" : "s")}"
            : $"{items.Count - fallbackCount} live actions · {fallbackCount} app shortcuts";
    }

    private Button TrayButton(
        WindowsTrayAccessibility.TrayItem item,
        CancellationToken iconToken)
    {
        var initial = item.Name.Trim().FirstOrDefault();
        var glyph = char.IsLetterOrDigit(initial)
            ? char.ToUpperInvariant(initial).ToString()
            : "•";

        var fallback = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = theme.Primary,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var realIcon = new AdaptiveAppIcon(30, 1, showTile: false)
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        var icon = new Grid
        {
            Width = 30,
            Height = 30
        };
        icon.Children.Add(fallback);
        icon.Children.Add(realIcon);

        if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
            _ = LoadTrayIconAsync(item, realIcon, fallback, iconToken);

        var label = new TextBlock
        {
            Text = item.Name,
            FontSize = 10.5,
            Foreground = theme.Primary,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Width = 84
        };

        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(icon);
        stack.Children.Add(label);
        stack.Children.Add(new TextBlock
        {
            Text = item.Source == WindowsTrayAccessibility.TrayItemSource.Registry ? "Open Application" : "Live action",
            FontSize = 9, Foreground = theme.Muted, HorizontalAlignment = HorizontalAlignment.Center
        });

        var button = new Button
        {
            Content = stack,
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(3, 8, 3, 8),
            Height = 92,
            CanDrag = true,
            AllowDrop = true,
            Tag = item,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        theme.StyleButton(button);
        ToolTipService.SetToolTip(
            button,
            item.Source == WindowsTrayAccessibility.TrayItemSource.Registry
                ? $"{item.Name} · Open Application · app shortcut, not a live tray icon"
                : string.IsNullOrWhiteSpace(item.DefaultAction)
                    ? item.Name
                    : $"{item.Name} · {item.DefaultAction}");

        // For an accessibility-backed entry, click requests the actual native
        // default action. Registry-only entries explicitly open the application;
        // they cannot stand in for arbitrary tray-icon messages.
        button.Click += async (_, _) =>
        {
            if (activationPending || draggedKey is not null) return;
            if (!activationGate.TryAccept(Environment.TickCount64, WindowsTrayAccessibility.DoubleClickMilliseconds)) return;
            activationPending = true;
            var current = (WindowsTrayAccessibility.TrayItem)button.Tag;
            WindowsTrayAccessibility.Trace($"Click: {current.Name}; source={current.Source}");
            button.IsEnabled = false;
            try
            {
                var invoked = await WindowsTrayAccessibility.InvokeAsync(current);
                WindowsTrayAccessibility.Trace($"Activation result: {invoked}");
                if (closed)
                    return;

                if (invoked)
                    Dismiss();
                else
                    status.Text = current.Source == WindowsTrayAccessibility.TrayItemSource.Registry
                        ? $"Could not show {current.Name}. Its background instance may not expose a window."
                        : $"Windows did not expose {current.Name}'s tray action.";
            }
            catch (Exception error)
            {
                WindowsTrayAccessibility.Trace($"Tray click failed: {error.GetType().Name}");
                if (!closed)
                    status.Text = $"Could not activate {current.Name}.";
            }
            finally
            {
                if (!closed)
                    button.IsEnabled = true;
                // Keep a successful activation latched until next Present, so a
                // double-click cannot dispatch another launch during dismissal.
                if (presentation.IsVisible) activationPending = false;
            }
        };

        button.ContextRequested += (sender, args) =>
        {
            args.Handled = true;
            if (closed || draggedKey is not null) return;
            var menu = new MenuFlyout();
            menuOpen = true;
            menu.Closed += (_, _) => { menuOpen = false; _ = RefreshAsync(); };
            menu.Items.Add(new MenuFlyoutItem { Text = "Doky tray controls", IsEnabled = false });
            var refresh = new MenuFlyoutItem { Text = "Refresh" };
            refresh.Click += (_, _) => _ = RefreshAsync();
            menu.Items.Add(refresh);
            AddMove("Move earlier", -1);
            AddMove("Move later", 1);
            menu.ShowAt(button);

            void AddMove(string title, int delta)
            {
                var index = displayedItems.ToList().FindIndex(i => Key(i) == Key((WindowsTrayAccessibility.TrayItem)button.Tag));
                var targetIndex = index + delta;
                var move = new MenuFlyoutItem { Text = title, IsEnabled = targetIndex >= 0 && targetIndex < displayedItems.Count };
                move.Click += async (_, _) =>
                {
                    if (targetIndex >= 0 && targetIndex < displayedItems.Count)
                        await MoveAsync(Key((WindowsTrayAccessibility.TrayItem)button.Tag), Key(displayedItems[targetIndex]), delta > 0);
                };
                menu.Items.Add(move);
            }
        };

        button.DragStarting += (_, args) =>
        {
            if (activationPending) { args.Cancel = true; return; }
            draggedKey = Key((WindowsTrayAccessibility.TrayItem)button.Tag);
            args.Data.SetData(DragFormat, draggedKey);
            args.Data.RequestedOperation = DataPackageOperation.Move;
            button.Opacity = .55;
        };
        button.DragOver += (_, args) =>
        {
            args.Handled = true;
            if (draggedKey is null || !args.DataView.Contains(DragFormat)) { args.AcceptedOperation = DataPackageOperation.None; return; }
            args.AcceptedOperation = DataPackageOperation.Move;
            var after = args.GetPosition(button).X > button.ActualWidth / 2;
            button.BorderBrush = theme.Primary;
            button.BorderThickness = after ? new Thickness(.7, .7, 3, .7) : new Thickness(3, .7, .7, .7);
            args.DragUIOverride.Caption = after ? "Place after" : "Place before";
        };
        button.DragLeave += (_, _) => ResetDropCue();
        button.Drop += async (_, args) =>
        {
            args.Handled = true;
            if (draggedKey is not { } source || !args.DataView.Contains(DragFormat)) return;
            var after = args.GetPosition(button).X > button.ActualWidth / 2;
            args.AcceptedOperation = DataPackageOperation.Move;
            ResetDropCue();
            draggedKey = null;
            await MoveAsync(source, Key((WindowsTrayAccessibility.TrayItem)button.Tag), after);
        };
        button.DropCompleted += (_, _) =>
        {
            draggedKey = null; button.Opacity = 1;
            foreach (var tile in trayGrid.Children.OfType<Button>()) { tile.BorderBrush = theme.TileBorder; tile.BorderThickness = new Thickness(.7); }
            _ = RefreshAsync();
        };
        void ResetDropCue() { button.BorderBrush = theme.TileBorder; button.BorderThickness = new Thickness(.7); }

        return button;
    }

    private async Task MoveAsync(string source, string target, bool after)
    {
        order ??= await loadOrder;
        if (closed) return;
        if (!order.Move(source, target, after, displayedItems.Select(Key))) return;
        var byKey = displayedItems.ToDictionary(Key);
        displayedItems = order.Apply(byKey.Keys).Select(key => byKey[key]).ToArray();
        var tiles = trayGrid.Children.OfType<Button>().ToDictionary(tile => Key((WindowsTrayAccessibility.TrayItem)tile.Tag));
        for (var i = 0; i < displayedItems.Count; i++)
        {
            var tile = tiles[Key(displayedItems[i])];
            Grid.SetRow(tile, i / 3); Grid.SetColumn(tile, i % 3);
        }
        itemKeys = displayedItems.Select(item => item.Name + "\n" + item.DefaultAction + "\n" + item.ExecutablePath + "\n" + item.Source).ToArray();
        try { await orderStore.SaveAsync(order.Snapshot()); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { if (!closed) status.Text = "Order changed, but could not be saved."; }
    }

    private async Task LoadTrayIconAsync(
        WindowsTrayAccessibility.TrayItem item,
        AdaptiveAppIcon target,
        TextBlock fallback,
        CancellationToken token)
    {
        var executablePath = item.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath) || token.IsCancellationRequested)
            return;

        try
        {
            var icon = await Task.Run(
                () => applicationService.GetIconForExternalTarget(executablePath),
                token);

            if (icon is null || token.IsCancellationRequested || closed)
                return;

            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || closed)
                        return;

                    target.SetIcon(icon);
                    target.Visibility = Visibility.Visible;
                    fallback.Visibility = Visibility.Collapsed;
                }))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateBackdrop() =>
        presentation.UpdateBackdropBounds();

    private Button IconButton(string glyph, string tooltip, Action action)
    {
        var icon = SystemControlStyle.Icon(glyph, 15);
        icon.Foreground = theme.Primary;
        var button = SystemControlStyle.Button(icon, tooltip, action, 30, 30);
        button.Background = theme.Tile;
        button.BorderBrush = theme.TileBorder;
        button.BorderThickness = new Thickness(.7);
        button.CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius);
        theme.StyleButton(button);
        return button;
    }

    private Button TextButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11.5,
            Foreground = theme.Primary,
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius),
            Padding = new Thickness(10, 5, 10, 5)
        };
        button.Click += (_, _) => action();
        theme.StyleButton(button);
        return button;
    }

    private static SolidColorBrush Brush(byte alpha, byte r = 255, byte g = 255, byte b = 255) =>
        new(global::Windows.UI.Color.FromArgb(alpha, r, g, b));
}
