using System.Diagnostics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Core.Materials;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Desktop;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

internal sealed class WindowPreviewWindow : Window
{
    private readonly Canvas root = new()
    {
        Background = Brush(1, 0, 0, 0),
        RequestedTheme = ElementTheme.Dark
    };

    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly WindowPreviewPlacement placement;
    private readonly WindowPreviewSession session;

    // Actual-window focus effect is preserved.
    // DesktopWindowHighlight was intentionally removed
    // so hovering a preview no longer creates a white border.
    private readonly DesktopWindowFocus desktopFocus;

    private readonly nint hwnd;
    private readonly nint dock;

    private readonly List<Card> cards = [];

    private readonly Button more = new()
    {
        Height = 26,
        Padding = new Thickness(8, 0, 8, 0),
        FontSize = 11
    };

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer animation;

    private WindowPreviewLayout compact =
        WindowPreviewLayout.Create(1, 600, 400, false);

    private WindowPreviewLayout expanded =
        WindowPreviewLayout.Create(1, 600, 400, true);

    private bool isShown;
    private bool rebuilding;
    private double progress;
    private double animationFrom;
    private double animationTo;
    private double animationStarted;

    private double anchorX;
    private double dockTop;

    private int page;

    private readonly Stopwatch elapsed = Stopwatch.StartNew();

    public event EventHandler? PointerArrived;
    public event EventHandler? PointerDeparted;
    public event EventHandler<ApplicationWindow>? WindowChosen;
    public event EventHandler<ApplicationWindow>? WindowCloseRequested;

    private sealed record Card(
        ApplicationWindow Window,
        Button Button,
        Button CloseButton,
        Border Border,
        TextBlock Title,
        TextBlock Fallback,
        WindowThumbnail Thumbnail)
    {
        public double Emphasis;
        public double EmphasisFrom;
        public double EmphasisTo;

        public double Dimming;
        public double DimmingFrom;
        public double DimmingTo;

        public double HoverStarted;
    }

    public WindowPreviewWindow(
        nint dock,
        WindowPreviewSession session,
        WindowFrameCache frameCache)
    {
        this.dock = dock;
        this.session = session;

        Title = "GlassDock — Window previews";
        Content = root;

        SystemBackdrop = backdrop;

        backdrop.Apply(
            GlassMaterialPresets.Create(
                GlassMaterialPreset.Frosted) with
            {
                BlurAmount = 8,
                Opacity = .08,
                CornerRadius = 12,
                EdgeHighlight = 0,
                BorderOpacity = 0.03
            }
        );

        var inspection =
            Environment.GetCommandLineArgs()
                .Contains(
                    "--controls",
                    StringComparer.OrdinalIgnoreCase);

        AppWindow.IsShownInSwitchers = inspection;

        var presenter =
            (OverlappedPresenter)AppWindow.Presenter;

        presenter.SetBorderAndTitleBar(false, false);

        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;

        hwnd =
            WinRT.Interop.WindowNative
                .GetWindowHandle(this);

        desktopFocus = new(hwnd, frameCache);

        //
        // IMPORTANT:
        // We only dismiss DesktopWindowFocus now.
        // No DesktopWindowHighlight exists anymore.
        //
        desktopFocus.Dismissed += (_, _) =>
        {
            // No white-border cleanup needed.
        };

        placement = new(hwnd, inspection);

        root.PointerEntered += (_, _) =>
        {
            Trace("ROOT ENTER");

            if (!placement.ContainsPointer())
                return;

            PointerArrived?.Invoke(
                this,
                EventArgs.Empty);

            Expand();
        };

        root.PointerExited += (_, e) =>
        {
            var point =
                e.GetCurrentPoint(root).Position;

            Trace(
                $"ROOT EXIT point={point} " +
                $"inside={placement.ContainsPointer()} " +
                $"bounds={root.ActualWidth}x{root.ActualHeight} " +
                $"source={e.OriginalSource.GetType().Name}");

            if (placement.ContainsPointer())
                return;

            Collapse();

            PointerDeparted?.Invoke(
                this,
                EventArgs.Empty);
        };

        root.KeyDown += (_, e) =>
        {
            if (e.Key ==
                global::Windows.System.VirtualKey.Escape)
            {
                Hide();

                PointerDeparted?.Invoke(
                    this,
                    EventArgs.Empty);

                e.Handled = true;
            }
        };

        root.SizeChanged += (_, _) =>
        {
            Draw();
        };

        more.Click += (_, _) =>
        {
            // Compact +N opens the current stack; only a settled expanded panel changes page.
            if (!CanFocus) { Expand(); return; }
            focusHideDelay?.Cancel();
            desktopFocus.Hide();
            session.Select(null);
            page = WindowPreviewLayout.NextPage(page, session.Windows.Count, expanded.Capacity);
            Rebuild();
            Draw();
        };
        animation = DispatcherQueue.CreateTimer();

        animation.Interval =
            TimeSpan.FromMilliseconds(10);

        animation.Tick += (_, _) =>
        {
            var moving = Advance();

            Trace(
                $"TICK p={progress:F3} " +
                $"target={animationTo} " +
                $"hover={string.Join(',', cards.Select(c => c.Emphasis.ToString("F2")))} " +
                $"moving={moving}");

            Draw();
            if (session.CompleteTransition(progress))
            {
                root.UpdateLayout();
                SyncHoveredCard();
                moving = true; // Selection may have just started its emphasis animation.
            }

            if (!moving)
                animation.Stop();
        };

        Closed += (_, _) =>
        {
            isShown = false;
            focusHideDelay?.Cancel();
            animation.Stop();

            desktopFocus.Dispose();

            ClearCards();

            placement.Dispose();
        };
    }

    public void Show(
        double anchor,
        double top)
    {
        anchorX = anchor;
        dockTop = top;

        isShown = true;
        focusHideDelay?.Cancel();
        desktopFocus.Hide();
        page = 0;

        progress = 0;
        animationFrom = 0;
        animationTo = 0;

        animation.Stop();

        Rebuild();
        Draw();
    }

    private void Expand()
    {
        if (session.State is not (WindowPreviewState.Compact or WindowPreviewState.Collapsing))
            return;

        session.Expand();

        StartTransition(1);
    }

    public void Collapse()
    {
        //
        // Keep original focus behavior.
        //
        focusHideDelay?.Cancel();
        desktopFocus.Hide();

        if (session.State is not
            (WindowPreviewState.Expanding or WindowPreviewState.Expanded or
             WindowPreviewState.WindowHovered))
            return;

        session.Collapse();

        StartTransition(0);

        AnimateSelection();
    }

    private void StartTransition(
        double target)
    {
        Trace(
            $"TRANSITION p={progress:F3} " +
            $"target={target} " +
            $"enabled={new UISettings().AnimationsEnabled}");

        Advance();

        animationFrom = progress;
        animationTo = target;

        animationStarted =
            elapsed.Elapsed.TotalMilliseconds;

        //
        // Preview expansion/collapse animation.
        //
        animation.Start();
    }

    private CancellationTokenSource? focusHideDelay;

    private async Task HideDesktopFocusDelayed()
    {
        focusHideDelay?.Cancel();
        var cancellation = new CancellationTokenSource();
        focusHideDelay = cancellation;
        try
        {
            await Task.Delay(180, cancellation.Token);
            if (!cancellation.IsCancellationRequested && session.SelectedWindow is null)
                desktopFocus.Hide();
        }
        catch (TaskCanceledException) { }
        finally
        {
            if (ReferenceEquals(focusHideDelay, cancellation)) focusHideDelay = null;
            cancellation.Dispose();
        }
    }

    private bool CanFocus => isShown && !rebuilding && progress == 1 && animationTo == 1 &&
        session.State is WindowPreviewState.Expanded or WindowPreviewState.WindowHovered;

    private void SyncHoveredCard()
    {
        if (!CanFocus) return;
        var hovered = cards.FirstOrDefault(card => card.Button.Visibility == Visibility.Visible && ContainsPointer(card.Button));
        var handle = hovered?.Window.Handle;
        if (session.SelectedWindow == handle) return;
        session.Select(handle);
        AnimateSelection();
    }

    private void AnimateSelection()
    {
        if (!CanFocus)
        {
            session.Select(null);
            focusHideDelay?.Cancel();
            desktopFocus.Hide();
        }
        else
        {
            var selectedWindow = session.Windows.FirstOrDefault(window => window.Handle == session.SelectedWindow);
            if (selectedWindow is null) _ = HideDesktopFocusDelayed();
            else
            {
                focusHideDelay?.Cancel();
                desktopFocus.Show(selectedWindow);
            }
        }
        Advance();
        foreach (var card in cards)
        {
            var emphasis = CanFocus && session.SelectedWindow == card.Window.Handle ? 1d : 0d;
            var dimming = CanFocus && session.SelectedWindow is not null && emphasis == 0 ? 1d : 0d;
            if (card.EmphasisTo == emphasis && card.DimmingTo == dimming) continue;
            card.EmphasisFrom = card.Emphasis;
            card.DimmingFrom = card.Dimming;
            card.EmphasisTo = emphasis;
            card.DimmingTo = dimming;
            card.HoverStarted = elapsed.Elapsed.TotalMilliseconds;
        }
        animation.Start();
    }
    private bool Advance()
    {
        var now =
            elapsed.Elapsed.TotalMilliseconds;

        var t = Math.Clamp(
            (now - animationStarted) / 260,
            0,
            1
        );

        progress = Lerp(
            animationFrom,
            animationTo,
            Ease(t)
        );

        var moving =
            t < 1 &&
            animationFrom != animationTo;

        foreach (var card in cards)
        {
            var hover = Math.Clamp(
                (now - card.HoverStarted) / 160,
                0,
                1
            );

            card.Emphasis = Lerp(
                card.EmphasisFrom,
                card.EmphasisTo,
                Ease(hover)
            );

            card.Dimming = Lerp(
                card.DimmingFrom,
                card.DimmingTo,
                Ease(hover)
            );

            moving |=
                hover < 1 &&
                (
                    card.EmphasisFrom !=
                    card.EmphasisTo ||

                    card.DimmingFrom !=
                    card.DimmingTo
                );
        }

        return moving;
    }

    private static double Ease(
        double t)
        => t * t * (3 - 2 * t);

    private static double Lerp(
        double from,
        double to,
        double amount)
        => from +
           (to - from) *
           amount;

    public void Refresh(
        double anchor,
        double top)
    {
        anchorX = anchor;
        dockTop = top;

        if (page >= session.Windows.Count)
            page = 0;

        var (area, dpi, _) =
            WindowPreviewPlacement.GetArea(dock);

        var layout =
            WindowPreviewLayout.Create(
                session.Windows.Count - page,
                area.Width / dpi - 24,
                area.Height / dpi - 24,
                true
            );

        var visible =
            session.Windows
                .Skip(page)
                .Take(layout.Capacity)
                .ToArray();

        if (visible.Length == 0)
            page = 0;

        if (
            layout.Width != expanded.Width ||
            layout.Height != expanded.Height ||
            layout.Capacity != expanded.Capacity ||
            !cards
                .Select(card => card.Window)
                .SequenceEqual(visible))
        {
            Rebuild();
        }

        Draw();
    }

    private void Rebuild()
    {
        rebuilding = true;
        try
        {
        Trace("REBUILD");

        var previous =
            cards.ToDictionary(
                card =>
                    (
                        card.Window.Handle,
                        card.Window.ProcessId,
                        card.Window.ProcessStartTicks
                    ));

        ClearCards();

        var (area, dpi, _) =
            WindowPreviewPlacement.GetArea(dock);

        compact =
            WindowPreviewLayout.Create(
                session.Windows.Count - page,
                area.Width / dpi - 24,
                area.Height / dpi - 24,
                false
            );

        expanded =
            WindowPreviewLayout.Create(
                session.Windows.Count - page,
                area.Width / dpi - 24,
                area.Height / dpi - 24,
                true
            );

        foreach (
            var window in
            session.Windows
                .Skip(page)
                .Take(expanded.Capacity)
                .Reverse())
        {
            var title =
                new TextBlock
                {
                    Text =
                        string.IsNullOrWhiteSpace(
                            window.Title)
                            ? window.Name
                            : window.Title,

                    FontSize = 12,

                    TextTrimming =
                        TextTrimming.CharacterEllipsis,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    Foreground =
                        Brush(
                            240,
                            245,
                            247,
                            255)
                };

            var fallback =
                new TextBlock
                {
                    Text =
                        window.IsMinimized
                            ? "Minimized window"
                            : "Preview unavailable",

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center,

                    FontSize = 12,

                    Opacity = .75
                };

            var appIcon =
                new AdaptiveAppIcon(
                    size: 22,
                    maximumHoverScale: 1.0);

            appIcon.SetIcon(window.Icon);

            appIcon.VerticalAlignment =
                VerticalAlignment.Center;

            var footer =
                new StackPanel
                {
                    Orientation =
                        Orientation.Horizontal,

                    Spacing = 8,

                    Margin =
                        new Thickness(
                            10,
                            0,
                            10,
                            5),

                    Height = 28,

                    VerticalAlignment =
                        VerticalAlignment.Bottom
                };

            footer.Children.Add(appIcon);
            footer.Children.Add(title);

            var grid = new Grid();

            grid.Children.Add(fallback);
            grid.Children.Add(footer);

            var border =
                new Border
                {
                    CornerRadius =
                        new CornerRadius(9),

                    BorderThickness =
                        new Thickness(1),

                    BorderBrush =
                        Brush(
                            55,
                            190,
                            200,
                            215),

                    Background =
                        Brush(
                            55,
                            18,
                            21,
                            28),

                    Child = grid
                };

            var button =
                new Button
                {
                    Content = border,

                    Padding =
                        new Thickness(0),

                    BorderThickness =
                        new Thickness(0),

                    Background =
                        Brush(
                            0,
                            0,
                            0,
                            0)
                };

            foreach (
                var resource in
                new[]
                {
                    "ButtonBackgroundPointerOver",
                    "ButtonBackgroundPressed",
                    "ButtonBorderBrushPointerOver",
                    "ButtonBorderBrushPressed"
                })
            {
                button.Resources[resource] =
                    Brush(
                        0,
                        0,
                        0,
                        0);
            }

            button.HorizontalContentAlignment =
                HorizontalAlignment.Stretch;

            button.VerticalContentAlignment =
                VerticalAlignment.Stretch;

            AutomationProperties.SetName(
                button,
                title.Text);

            // Sibling of the card button: its click cannot bubble through the activation button.
           var closeButton = new Button
{
    Content = null,

    Width = 14,
    Height = 14,

    MinWidth = 0,
    MinHeight = 0,

    Padding = new Thickness(0),

    CornerRadius = new CornerRadius(7),

    BorderThickness = new Thickness(0),

    // macOS close red
    Background = Brush(255, 255, 95, 87),

    Foreground = Brush(255, 75, 20, 18),

    FontSize = 11,
    FontWeight = Microsoft.UI.Text.FontWeights.Bold,

    HorizontalContentAlignment = HorizontalAlignment.Center,
    VerticalContentAlignment = VerticalAlignment.Center,

    Opacity = 0,
    IsHitTestVisible = false
};

// Remove WinUI's default button hover/pressed visuals.
foreach (var resource in new[]
{
    "ButtonBackgroundPointerOver",
    "ButtonBackgroundPressed",
    "ButtonBorderBrushPointerOver",
    "ButtonBorderBrushPressed"
})
{
    closeButton.Resources[resource] =
        resource.Contains("Background")
            ? Brush(255, 255, 95, 87)
            : Brush(0, 0, 0, 0);
}
            AutomationProperties.SetName(closeButton, $"Close {title.Text}");
            closeButton.Click += (_, _) =>
            {
                if (session.SelectedWindow == window.Handle)
                {
                    session.Select(null);
                    focusHideDelay?.Cancel();
                    desktopFocus.Hide();
                    AnimateSelection();
                }
                WindowCloseRequested?.Invoke(this, window);
            };
            closeButton.PointerEntered += (_, _) =>
{
    closeButton.Content = new TextBlock
{
    Text = "\u00D7",
    FontSize = 11,
    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
    Foreground = Brush(255, 75, 20, 18),

    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,

    Margin = new Thickness(0, -1, 0, 0)
};
    Draw();
};

closeButton.PointerExited += (_, _) =>
{
    closeButton.Content = null;

    SyncHoveredCard();
    Draw();
};

closeButton.GotFocus += (_, _) =>
{
    closeButton.Content = new TextBlock
{
    Text = "\u00D7",
    FontSize = 11,
    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
    Foreground = Brush(255, 75, 20, 18),

    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,

    Margin = new Thickness(0, -1, 0, 0)
};
    Draw();
};

closeButton.LostFocus += (_, _) =>
{
    closeButton.Content = null;
    Draw();
};

            AutomationProperties.SetItemStatus(
                button,
                window.IsMinimized
                    ? "Minimized"
                    : window.IsActive
                        ? "Active"
                        : "Running");

            button.PointerEntered += (_, _) =>
            {
                if (!ContainsPointer(button))
                    return;

                Expand();
                Draw();
                if (!CanFocus) return;

                session.Select(
                    window.Handle);

                AnimateSelection();
            };

            button.PointerExited += (_, _) =>
            {
                Draw();
                if (!CanFocus) return;
                if (
                    ContainsPointer(button) ||
                    session.SelectedWindow !=
                    window.Handle)
                    return;

                session.Select(null);

                AnimateSelection();
            };

            button.Click += (_, _) =>
            {
                WindowChosen?.Invoke(
                    this,
                    window);
            };

            //
            // Register rear thumbnails first so
            // DWM composites the dominant one last.
            //
            var card =
                new Card(
                    window,
                    button,
                    closeButton,
                    border,
                    title,
                    fallback,
                    new WindowThumbnail(
                        hwnd,
                        window)
                );

            if (
                previous.TryGetValue(
                    (
                        window.Handle,
                        window.ProcessId,
                        window.ProcessStartTicks
                    ),
                    out var old))
            {
                card.Emphasis =
                    old.Emphasis;

                card.EmphasisFrom =
                    old.EmphasisFrom;

                card.EmphasisTo =
                    old.EmphasisTo;

                card.Dimming =
                    old.Dimming;

                card.DimmingFrom =
                    old.DimmingFrom;

                card.DimmingTo =
                    old.DimmingTo;

                card.HoverStarted =
                    old.HoverStarted;
            }

            cards.Insert(
                0,
                card);

            root.Children.Add(
                button);
            root.Children.Add(closeButton);
        }

        root.Children.Add(more);
        }
        finally { rebuilding = false; }
        if (session.SelectedWindow is long selected && !cards.Any(card => card.Window.Handle == selected))
        {
            session.Select(null);
            focusHideDelay?.Cancel();
            desktopFocus.Hide();
        }
        AnimateSelection();
    }

    private void Draw()
    {
        if (!isShown || cards.Count == 0)
            return;

        var width =
            compact.Width +
            (
                expanded.Width -
                compact.Width
            ) *
            progress;

        var height =
            expanded.Height;

        placement.Position(
            dock,
            anchorX,
            dockTop,
            width,
            height);

        var dpi =
            WindowPreviewPlacement
                .GetArea(dock)
                .Dpi;

        backdrop.SetBounds(
            width,
            height,
            width,
            height - 12,
            12,
            dpi);

        for (
            var index =
                cards.Count - 1;

            index >= 0;

            index--)
        {
            var card =
                cards[index];

            var from =
                compact.Cards[index];

            var to =
                expanded.Cards[index];

            var rect =
                new PreviewRect(
                    from.X +
                    (to.X - from.X) *
                    progress,

                    from.Y +
                    (to.Y - from.Y) *
                    progress,

                    from.Width +
                    (to.Width - from.Width) *
                    progress,

                    from.Height +
                    (to.Height - from.Height) *
                    progress
                );

            var selected =
                session.SelectedWindow ==
                card.Window.Handle;

            rect =
                new PreviewRect(
                    rect.X -
                    rect.Width *
                    .02 *
                    card.Emphasis,

                    rect.Y -
                    4 *
                    card.Emphasis,

                    rect.Width *
                    (
                        1 +
                        .04 *
                        card.Emphasis
                    ),

                    rect.Height *
                    (
                        1 +
                        .04 *
                        card.Emphasis
                    )
                );

            var opacity =
                (
                    index == 0
                        ? 1
                        : .66 +
                          .34 *
                          progress
                ) *
                (
                    1 -
                    .78 *
                    card.Dimming *
                    progress
                );

            if (index >= 3)
            {
                opacity *= progress;
            }

            var visible =
                progress > 0 ||
                index < 3;

            card.Button.Visibility =
                visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            card.Button.Opacity =
                opacity;

            card.Title.Opacity =
                index == 0
                    ? 1
                    : progress;

            Canvas.SetZIndex(
                card.Button,
                selected
                    ? 20
                    : cards.Count - index);

            Canvas.SetLeft(
                card.Button,
                rect.X);

            Canvas.SetTop(
                card.Button,
                rect.Y);

            card.Button.Width =
                rect.Width;

            card.Button.Height =
                rect.Height;

            var closeVisible = visible && CanFocus &&
                (ContainsPointer(card.Button) || card.CloseButton.FocusState != FocusState.Unfocused);
            card.CloseButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            card.CloseButton.Opacity = closeVisible ? 1 : 0;
            card.CloseButton.IsHitTestVisible = closeVisible;
            card.CloseButton.IsTabStop = CanFocus;
            Canvas.SetLeft(
    card.CloseButton,
    rect.X + 9);

Canvas.SetTop(
    card.CloseButton,
    rect.Y + 8);
            Canvas.SetZIndex(card.CloseButton, 25);

            ((SolidColorBrush)
                card.Border.BorderBrush)
                .Color =
                    global::Windows.UI.Color
                        .FromArgb(
                            (byte)Lerp(
                                45,
                                165,
                                card.Emphasis),

                            (byte)Lerp(
                                235,
                                215,
                                card.Emphasis),

                            (byte)Lerp(
                                243,
                                234,
                                card.Emphasis),

                            255
                        );

            ((SolidColorBrush)
                card.Border.Background)
                .Color =
                    global::Windows.UI.Color
                        .FromArgb(
                            (byte)Lerp(
                                30,
                                60,
                                card.Emphasis),

                            (byte)Lerp(
                                25,
                                90,
                                card.Emphasis),

                            (byte)Lerp(
                                30,
                                105,
                                card.Emphasis),

                            (byte)Lerp(
                                45,
                                135,
                                card.Emphasis)
                        );

            var shown =
                card.Thumbnail.Update(
                    new PreviewRect(
                        rect.X + 8,
                        rect.Y + 26,
                        rect.Width - 16,
                        rect.Height - 58),

                    dpi,
                    opacity,
                    visible);

            card.Fallback.Visibility =
                shown
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        var shownCount = progress < 1 ? Math.Min(3, cards.Count) : cards.Count;
        var remaining = session.Windows.Count - page - cards.Count;
        var hasAnotherPage = session.Windows.Count > expanded.Capacity;
        more.Content = progress < 1
            ? $"+{session.Windows.Count - shownCount} more"
            : remaining > 0 ? $"+{remaining} more · {page + 1}–{page + cards.Count} of {session.Windows.Count}"
            : $"Back to first · {page + 1}–{page + cards.Count} of {session.Windows.Count}";
        more.MaxWidth = Math.Max(0, width - 24);
        more.Visibility = (progress < 1 ? session.Windows.Count > shownCount : hasAnotherPage)
            ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(
            more,
            12);

        Canvas.SetTop(
            more,
            height - 34);

        Canvas.SetZIndex(
            more,
            30);
    }

    private bool ContainsPointer(
        Button button)
        =>
            placement.ContainsPointer(
                new PreviewRect(
                    Canvas.GetLeft(button),
                    Canvas.GetTop(button),
                    button.Width,
                    button.Height));

    private void ClearCards()
    {
        foreach (var card in cards)
        {
            card.Thumbnail.Dispose();
        }

        cards.Clear();

        root.Children.Clear();
    }

    public void Hide()
    {
        isShown = false;
        session.Select(null);
        focusHideDelay?.Cancel();
        desktopFocus.Hide();

        Trace("HIDE");

        animation.Stop();

        placement.Hide();

        ClearCards();
    }

    private static SolidColorBrush Brush(
        byte a,
        byte r,
        byte g,
        byte b)
        =>
            new(
                global::Windows.UI.Color
                    .FromArgb(
                        a,
                        r,
                        g,
                        b));

    internal static void Trace(
        string message)
    {
        System.IO.File.AppendAllText(
            @"C:\Dev\GlassDock\artifacts\preview-runtime.trace",
            $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
    }
}
