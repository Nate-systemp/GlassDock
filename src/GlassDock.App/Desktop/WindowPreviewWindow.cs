using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Core.Settings;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using global::Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

/// <summary>Retained preview host. DWM owns live pixels; XAML owns card chrome.</summary>
internal sealed class WindowPreviewWindow : Window
{
    private readonly Canvas root = new();
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, IsHitTestVisible = false };
    private readonly Border solid = new() { IsHitTestVisible = false, CornerRadius = new(28) };
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly UtilityPopupTheme theme = new();
    private readonly WindowPreviewPlacement placement;
    private readonly WindowPreviewSession session;
    private readonly WindowFrameCache frameCache;
    private readonly DesktopWindowFocus desktopFocus;
    private readonly nint hwnd;
    private readonly nint dock;
    private readonly List<Card> cards = [];
    private readonly Button more = new() { Height = 26, Padding = new(8, 0, 8, 0), FontSize = 11 };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer animation;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly UISettings uiSettings = new();
    private WindowPreviewLayout compact = WindowPreviewLayout.Create(1, 600, 400, false);
    private WindowPreviewLayout expanded = WindowPreviewLayout.Create(1, 600, 400, true);
    private bool isShown;
    private bool rebuilding;
    private bool closed;
    private double progress, animationFrom, animationTo, animationStarted;
    private double visibility, visibilityFrom, visibilityTo, visibilityStarted;
    private double anchorX, dockTop;
    private int page, windowCount;
    private CancellationTokenSource? focusHideDelay;
    private bool AnimationsEnabled => uiSettings.AnimationsEnabled;
    public bool IsClosing => isShown && visibilityTo == 0;
    public event EventHandler? Hidden;
    public event EventHandler? DismissRequested;
    public event EventHandler? PointerArrived;
    public event EventHandler? PointerDeparted;
    public event EventHandler<ApplicationWindow>? WindowChosen;
    public event EventHandler<ApplicationWindow>? WindowCloseRequested;

    private sealed class Card
    {
        public required ApplicationWindow Window;
        public required Button Button;
        public required Button CloseButton;
        public required Border Border;
        public required TextBlock Title;
        public required TextBlock Fallback;
        public required Image Cached;
        public required WindowThumbnail Thumbnail;
        public double Emphasis, EmphasisFrom, EmphasisTo, HoverStarted;
        public ReadOnlyMemory<byte> CachedPixels;
    }

    public WindowPreviewWindow(nint dock, WindowPreviewSession session, WindowFrameCache frameCache,
        DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        this.dock = dock;
        this.session = session;
        this.frameCache = frameCache;
        Title = "Doky — Window previews";
        WindowBranding.Apply(this);
        Content = root;
        var inspection = Environment.GetCommandLineArgs().Contains("--controls", StringComparer.OrdinalIgnoreCase);
        AppWindow.IsShownInSwitchers = inspection;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        placement = new(hwnd, inspection);
        desktopFocus = new(hwnd, frameCache);
        root.Children.Add(glass);
        root.Children.Add(solid);
        solid.Background = theme.Overlay;
        ApplyAppearance(appearance, mode);
        // As with the dock/utilities, attach after Content and native setup.
        SystemBackdrop = backdrop;

        root.PointerEntered += (_, _) =>
        {
            if (!placement.ContainsPointer() || IsClosing) return;
            PointerArrived?.Invoke(this, EventArgs.Empty);
            Expand();
        };
        root.PointerExited += (_, _) =>
        {
            if (placement.ContainsPointer() || IsClosing) return;
            Collapse();
            PointerDeparted?.Invoke(this, EventArgs.Empty);
        };
        root.KeyDown += (_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape) return;
            DismissRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        };
        root.SizeChanged += (_, _) => Draw();
        more.Click += (_, _) =>
        {
            if (!CanFocus) return;
            ClearSelection();
            page = WindowPreviewLayout.NextPage(page, windowCount, expanded.Capacity);
            Rebuild();
            Draw();
            root.UpdateLayout();
            SyncHoveredCard();
        };
        theme.StyleButton(more);
        more.Background = theme.Tile;
        animation = DispatcherQueue.CreateTimer();
        animation.Interval = TimeSpan.FromMilliseconds(16);
        animation.Tick += (_, _) =>
        {
            var moving = Advance();
            Draw();
            if (session.CompleteTransition(progress))
            {
                root.UpdateLayout();
                SyncHoveredCard();
                moving = true;
            }
            if (visibility == 0 && visibilityTo == 0) FinishHide();
            else if (!moving) animation.Stop();
        };
        Closed += (_, _) =>
        {
            closed = true;
            isShown = false;
            focusHideDelay?.Cancel();
            animation.Stop();
            desktopFocus.Dispose();
            ClearCards();
            placement.Dispose();
        };
    }

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        // Alpha 1 is an input surface, not a separate material overlay.
        root.Background = new SolidColorBrush(DockControlPalette.Surface(mode, 1));
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
        if (isShown) Draw();
    }

    public void Show(double anchor, double top)
    {
        anchorX = anchor;
        dockTop = top;
        animation.Stop();
        ClearSelection();
        page = 0;
        isShown = true;
        root.IsHitTestVisible = true;
        visibility = visibilityFrom = 0;
        visibilityTo = 1;
        visibilityStarted = elapsed.Elapsed.TotalMilliseconds;
        progress = animationFrom = animationTo = 0;
        Rebuild();
        Draw();
        // Show the full group automatically, without requiring a second hover
        // to discover other windows. Focus remains gated until expansion settles.
        Expand();
    }

    private void Expand()
    {
        if (session.State is not (WindowPreviewState.Compact or WindowPreviewState.Collapsing)) return;
        session.Expand();
        StartTransition(1);
    }

    private void Collapse()
    {
        ClearSelection();
        if (session.State is not (WindowPreviewState.Expanding or WindowPreviewState.Expanded or WindowPreviewState.WindowHovered)) return;
        session.Collapse();
        StartTransition(0);
    }

    private void StartTransition(double target)
    {
        Advance();
        animationFrom = progress;
        animationTo = target;
        animationStarted = elapsed.Elapsed.TotalMilliseconds;
        animation.Start();
    }

    private bool CanFocus => isShown && !IsClosing && !rebuilding && progress == 1 && animationTo == 1 &&
        session.State is WindowPreviewState.Expanded or WindowPreviewState.WindowHovered;

    private void ClearSelection()
    {
        session.Select(null);
        focusHideDelay?.Cancel();
        desktopFocus.Hide();
    }

    private void SyncHoveredCard()
    {
        if (!CanFocus) return;
        var hovered = cards.FirstOrDefault(card => ContainsPointer(card.Button) || ContainsPointer(card.CloseButton));
        if (session.SelectedWindow == hovered?.Window.Handle) return;
        session.Select(hovered?.Window.Handle);
        AnimateSelection();
    }

    private async Task HideDesktopFocusDelayed()
    {
        focusHideDelay?.Cancel();
        var cancellation = new CancellationTokenSource();
        focusHideDelay = cancellation;
        try
        {
            await Task.Delay(180, cancellation.Token);
            if (!cancellation.IsCancellationRequested && session.SelectedWindow is null) desktopFocus.Hide();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(focusHideDelay, cancellation)) focusHideDelay = null;
            cancellation.Dispose();
        }
    }

    private void AnimateSelection()
    {
        if (!CanFocus) ClearSelection();
        else
        {
            var selected = cards.FirstOrDefault(card => card.Window.Handle == session.SelectedWindow);
            if (selected is null) _ = HideDesktopFocusDelayed();
            else
            {
                focusHideDelay?.Cancel();
                desktopFocus.Show(selected.Window);
            }
        }
        Advance();
        foreach (var card in cards)
        {
            var target = CanFocus && session.SelectedWindow == card.Window.Handle ? 1d : 0d;
            if (card.EmphasisTo == target) continue;
            card.EmphasisFrom = card.Emphasis;
            card.EmphasisTo = target;
            card.HoverStarted = elapsed.Elapsed.TotalMilliseconds;
        }
        animation.Start();
    }

    private bool Advance()
    {
        var now = elapsed.Elapsed.TotalMilliseconds;
        var enabled = AnimationsEnabled;
        var t = enabled ? Math.Clamp((now - animationStarted) / 220, 0, 1) : 1;
        progress = Lerp(animationFrom, animationTo, Ease(t));
        var moving = t < 1 && animationFrom != animationTo;
        var fade = enabled ? Math.Clamp((now - visibilityStarted) / 140, 0, 1) : 1;
        visibility = Lerp(visibilityFrom, visibilityTo, Ease(fade));
        moving |= fade < 1 && visibilityFrom != visibilityTo;
        foreach (var card in cards)
        {
            var hover = enabled ? Math.Clamp((now - card.HoverStarted) / 120, 0, 1) : 1;
            card.Emphasis = Lerp(card.EmphasisFrom, card.EmphasisTo, Ease(hover));
            moving |= hover < 1 && card.EmphasisFrom != card.EmphasisTo;
        }
        return moving;
    }

    public void Refresh(double anchor, double top)
    {
        anchorX = anchor;
        dockTop = top;
        if (!isShown || IsClosing) return;
        if (session.Windows.Count == 0) { Hide(); return; }
        if (page >= session.Windows.Count) page = 0;
        var (area, dpi, _) = WindowPreviewPlacement.GetArea(dock);
        var layout = WindowPreviewLayout.Create(session.Windows.Count - page,
            area.Width / dpi - 24, area.Height / dpi - 24, true);
        var windows = session.Windows.Skip(page).Take(layout.Capacity).ToArray();
        if (layout.Width != expanded.Width || layout.Height != expanded.Height ||
            layout.Capacity != expanded.Capacity ||
            !cards.Select(card => Key(card.Window)).SequenceEqual(windows.Select(Key)))
            Rebuild();
        else
        {
            windowCount = session.Windows.Count;
            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].Window = windows[i];
                UpdateTitle(cards[i]);
            }
        }
        Draw();
    }

    private static (long, int, long) Key(ApplicationWindow window) =>
        (window.Handle, window.ProcessId, window.ProcessStartTicks);

    private void Rebuild()
    {
        rebuilding = true;
        try
        {
            var previous = cards.ToDictionary(card => Key(card.Window));
            cards.Clear();
            root.Children.Clear();
            root.Children.Add(glass);
            root.Children.Add(solid);
            var (area, dpi, _) = WindowPreviewPlacement.GetArea(dock);
            var count = session.Windows.Count - page;
            windowCount = session.Windows.Count;
            compact = WindowPreviewLayout.Create(count, area.Width / dpi - 24, area.Height / dpi - 24, false);
            expanded = WindowPreviewLayout.Create(count, area.Width / dpi - 24, area.Height / dpi - 24, true);
            var windows = session.Windows.Skip(page).Take(expanded.Capacity).ToArray();
            // DWM registrations follow compact stack z-order, front last.
            foreach (var window in windows.Reverse())
            {
                if (!previous.Remove(Key(window), out var card)) card = CreateCard(window);
                card.Window = window;
                UpdateTitle(card);
                cards.Insert(0, card);
                root.Children.Add(card.Button);
                root.Children.Add(card.CloseButton);
            }
            foreach (var unused in previous.Values) unused.Thumbnail.Dispose();
            root.Children.Add(more);
        }
        finally { rebuilding = false; }
        if (session.SelectedWindow is long selected && !cards.Any(card => card.Window.Handle == selected)) ClearSelection();
    }

    private Card CreateCard(ApplicationWindow window)
    {
        var title = new TextBlock { FontSize = 12, Foreground = theme.Primary,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var fallback = new TextBlock { FontSize = 12, Foreground = theme.Secondary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var cached = new Image { Stretch = Stretch.Uniform, Margin = new(8, 36, 8, 34), IsHitTestVisible = false };
        var icon = new AdaptiveAppIcon(22, 1, showTile: false) { VerticalAlignment = VerticalAlignment.Center };
        icon.SetIcon(window.Icon);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(10, 0, 10, 5),
            Height = 28, VerticalAlignment = VerticalAlignment.Bottom };
        footer.Children.Add(icon);
        footer.Children.Add(title);
        var content = new Grid();
        content.Children.Add(fallback);
        content.Children.Add(cached);
        content.Children.Add(footer);
        var border = new Border { CornerRadius = new(DockControlPalette.ButtonRadius),
            BorderThickness = new(1), BorderBrush = theme.TileBorder, Background = theme.Tile, Child = content };
        var button = new Button { Content = border, Padding = new(0), BorderThickness = new(0),
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        theme.StyleButton(button);
        // A sibling rather than a child of the activation button. Its click
        // cannot bubble through the card activation handler.
        var close = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10, Foreground = theme.Primary },
            Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0),
            BorderThickness = new(0), Background = theme.Tile, Opacity = 0, IsHitTestVisible = false };
        theme.StyleButton(close);
        var card = new Card { Window = window, Button = button, CloseButton = close, Border = border,
            Title = title, Fallback = fallback, Cached = cached, Thumbnail = new(hwnd, window) };
        button.PointerEntered += (_, _) =>
        {
            if (!ContainsPointer(button) || IsClosing) return;
            Expand();
            if (CanFocus) { session.Select(card.Window.Handle); AnimateSelection(); }
            Draw();
        };
        button.PointerExited += (_, _) => { SyncHoveredCard(); Draw(); };
        button.Click += (_, _) =>
        {
            if (!IsClosing) WindowChosen?.Invoke(this, card.Window);
        };
        close.Click += (_, _) =>
        {
            if (IsClosing) return;
            if (session.SelectedWindow == card.Window.Handle) { ClearSelection(); AnimateSelection(); }
            WindowCloseRequested?.Invoke(this, card.Window);
        };
        close.PointerEntered += (_, _) => Draw();
        close.PointerExited += (_, _) => { SyncHoveredCard(); Draw(); };
        close.GotFocus += (_, _) => Draw();
        close.LostFocus += (_, _) => Draw();
        return card;
    }

    private static void UpdateTitle(Card card)
    {
        card.Title.Text = string.IsNullOrWhiteSpace(card.Window.Title) ? card.Window.Name : card.Window.Title;
        card.Fallback.Text = card.Window.IsMinimized ? "Minimized · no cached preview yet" : "Preview unavailable";
        AutomationProperties.SetName(card.Button, card.Title.Text);
        AutomationProperties.SetName(card.CloseButton, "Close " + card.Title.Text);
        AutomationProperties.SetItemStatus(card.Button, card.Window.IsMinimized ? "Minimized" : card.Window.IsActive ? "Active" : "Running");
    }

    private void Draw()
    {
        if (!isShown || rebuilding || cards.Count == 0) return;
        var width = Lerp(compact.Width, expanded.Width, progress);
        var height = Lerp(compact.Height, expanded.Height, progress);
        placement.Position(dock, anchorX, dockTop, width, height);
        var dpi = WindowPreviewPlacement.GetArea(dock).Dpi;
        // All layers, including native thumbnails, use the same fade and travel.
        var travel = 6 * (1 - visibility);
        root.Opacity = visibility;
        glass.Width = solid.Width = width;
        glass.Height = solid.Height = Math.Max(0, height - 12);
        Canvas.SetTop(glass, travel);
        Canvas.SetTop(solid, travel);
        backdrop.SetBounds(width, height, width, Math.Max(0, height - 12 - travel), 12, dpi, visibility);
        for (var index = cards.Count - 1; index >= 0; index--)
        {
            var card = cards[index];
            var from = compact.Cards[index];
            var to = expanded.Cards[index];
            var rect = new PreviewRect(Lerp(from.X, to.X, progress), Lerp(from.Y, to.Y, progress) + travel,
                Lerp(from.Width, to.Width, progress), Lerp(from.Height, to.Height, progress));
            var visible = progress > 0 || index < 3;
            var opacity = visibility * (index == 0 ? 1 : Lerp(.66, 1, progress));
            if (index >= 3) opacity *= progress;
            card.Button.Visibility = card.CloseButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            card.Button.Opacity = visibility > 0 ? opacity / visibility : 0;
            Canvas.SetLeft(card.Button, rect.X);
            Canvas.SetTop(card.Button, rect.Y);
            Canvas.SetZIndex(card.Button, cards.Count - index);
            card.Button.Width = rect.Width;
            card.Button.Height = rect.Height;
            card.Title.MaxWidth = Math.Max(0, rect.Width - 60);
            card.Border.Background = card.Emphasis > .5 ? theme.Hover : theme.Tile;
            var closeVisible = visible && CanFocus &&
                (ContainsPointer(card.Button) || ContainsPointer(card.CloseButton) || card.CloseButton.FocusState != FocusState.Unfocused);
            card.CloseButton.Opacity = closeVisible ? 1 : 0;
            card.CloseButton.IsHitTestVisible = closeVisible;
            card.CloseButton.IsTabStop = CanFocus;
            var closeRect = WindowPreviewLayout.CloseButtonBounds(rect, card.CloseButton.Width);
            Canvas.SetLeft(card.CloseButton, closeRect.X);
            Canvas.SetTop(card.CloseButton, closeRect.Y);
            Canvas.SetZIndex(card.CloseButton, 25);

            var frame = card.Window.IsMinimized ? frameCache.GetCachedFrame(card.Window) : null;
            if (frame is { } cachedFrame && !card.CachedPixels.Equals(cachedFrame.Pixels))
            {
                var bitmap = new WriteableBitmap(cachedFrame.Width, cachedFrame.Height);
                using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(cachedFrame.Pixels.Span);
                bitmap.Invalidate();
                card.Cached.Source = bitmap;
                card.CachedPixels = cachedFrame.Pixels;
            }
            var useCache = frame is not null;
            card.Cached.Visibility = useCache ? Visibility.Visible : Visibility.Collapsed;
            if (frame is null && card.Cached.Source is not null)
            {
                card.Cached.Source = null;
                card.CachedPixels = default;
            }
            var shown = card.Thumbnail.Update(new(rect.X + 8, rect.Y + 36, Math.Max(1, rect.Width - 16), Math.Max(1, rect.Height - 70)),
                dpi, opacity, visible && !useCache);
            card.Fallback.Visibility = useCache || shown ? Visibility.Collapsed : Visibility.Visible;
        }
        var remaining = windowCount - page - cards.Count;
        more.Content = remaining > 0 ? $"+{remaining} more · {page + 1}–{page + cards.Count} of {windowCount}"
            : $"Back to first · {page + 1}–{page + cards.Count} of {windowCount}";
        more.Visibility = windowCount > expanded.Capacity ? Visibility.Visible : Visibility.Collapsed;
        more.IsEnabled = CanFocus;
        more.MaxWidth = Math.Max(0, width - 24);
        Canvas.SetLeft(more, 12);
        Canvas.SetTop(more, height - 34 + travel);
        Canvas.SetZIndex(more, 30);
    }

    private bool ContainsPointer(Button button) => placement.ContainsPointer(
        new(Canvas.GetLeft(button), Canvas.GetTop(button), button.Width, button.Height));

    private void ClearCards()
    {
        foreach (var card in cards) card.Thumbnail.Dispose();
        cards.Clear();
        root.Children.Clear();
        root.Children.Add(glass);
        root.Children.Add(solid);
    }

    public void Hide(bool immediate = false)
    {
        if (!isShown || closed) return;
        ClearSelection();
        root.IsHitTestVisible = false;
        if (immediate || !AnimationsEnabled) { FinishHide(); return; }
        Advance();
        visibilityFrom = visibility;
        visibilityTo = 0;
        visibilityStarted = elapsed.Elapsed.TotalMilliseconds;
        animation.Start();
    }

    private void FinishHide()
    {
        isShown = false;
        animation.Stop();
        placement.Hide();
        ClearCards();
        Hidden?.Invoke(this, EventArgs.Empty);
    }

    private static double Ease(double t) => t * t * (3 - 2 * t);
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static readonly bool TraceEnabled = Environment.GetEnvironmentVariable("GLASSDOCK_PREVIEW_TRACE") == "1";
    internal static void Trace(string message)
    {
        if (!TraceEnabled) return;
        Debug.WriteLine("[WindowPreview] " + message);
    }
}
