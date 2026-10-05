using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal sealed record DockAppMenuEntry(string Text, string Glyph, Action? Invoke = null,
    IReadOnlyList<DockAppMenuEntry>? Children = null, bool IsHeading = false);

/// <summary>One retained, interactive app menu using the dock's desktop glass and control palette.</summary>
internal sealed class DockAppContextMenuWindow : Window
{
    private const double Gutter = UtilityPopupStyle.Gutter;
    private readonly nint dock;
    private readonly Grid root = new();
    private readonly StackPanel rows = new();
    private readonly AdaptiveAppIcon icon = new(26, 1, showTile: false);
    private readonly TextBlock title = new() { FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new(Gutter), IsHitTestVisible = false };
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly ScrollViewer scroll;
    private readonly List<Button> actions = [];
    private IReadOnlyList<DockAppMenuEntry> entries = [];
    private readonly global::Windows.UI.ViewManagement.UISettings uiSettings = new();
    private PopupCompositionTrack? track;
    private Microsoft.UI.Composition.CompositionScopedBatch? batch;
    private int generation;
    private bool disposed, closing;
    private double anchor, top, scale = 1;
    public bool IsOpen { get; private set; }
    public event EventHandler? Hidden;

    public DockAppContextMenuWindow(nint dock)
    {
        this.dock = dock;
        Title = "Doky — App menu";
        WindowBranding.Apply(this);
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        root.Children.Add(glass);
        root.Children.Add(new Border { Margin = new(Gutter), CornerRadius = new(28),
            Background = theme.Overlay, IsHitTestVisible = false });
        var body = new Grid { Margin = new(Gutter + 8), RowSpacing = 4 };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var header = new Grid { Margin = new(10, 5, 10, 5), ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new());
        header.Children.Add(icon);
        title.Foreground = theme.Primary;
        status.Foreground = theme.Secondary;
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(title); heading.Children.Add(status);
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        body.Children.Add(header);
        scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        body.Children.Add(scroll);
        root.Children.Add(body);
        Content = root;
        host = new(WinRT.Interop.WindowNative.GetWindowHandle(this))
        { EnableHostBackdropBrush = true, UseDockLayeredTransparency = true };
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        // The native client and Content must exist before the backdrop connects.
        SystemBackdrop = backdrop;
        root.SizeChanged += (_, _) => UpdateBounds();
        root.Loaded += (_, _) => UpdateBounds();
        root.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape) { Hide(); e.Handled = true; }
            else if (e.Key is global::Windows.System.VirtualKey.Up or global::Windows.System.VirtualKey.Down
                or global::Windows.System.VirtualKey.Home or global::Windows.System.VirtualKey.End)
            {
                if (actions.Count == 0) return;
                var index = actions.FindIndex(button => button.FocusState != FocusState.Unfocused);
                index = e.Key switch
                {
                    global::Windows.System.VirtualKey.Home => 0,
                    global::Windows.System.VirtualKey.End => actions.Count - 1,
                    global::Windows.System.VirtualKey.Up => (index - 1 + actions.Count) % actions.Count,
                    _ => (index + 1) % actions.Count
                };
                if (actions.Count > 0) actions[index].Focus(FocusState.Keyboard);
                e.Handled = true;
            }
        };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) Hide(immediate: true); };
        Closed += (_, _) =>
        {
            disposed = true; IsOpen = false; generation++; batch?.Dispose(); track?.Dispose();
            host.Dispose(); SystemBackdrop = null;
        };
    }

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
    }

    public void Show(DockApplicationItem item, IReadOnlyList<DockAppMenuEntry> commands, double anchorX, double dockTop, string stateText = "")
    {
        entries = commands;
        title.Text = item.Name;
        status.Text = stateText;
        status.Visibility = string.IsNullOrEmpty(stateText) ? Visibility.Collapsed : Visibility.Visible;
        icon.SetIcon(item.Application.Icon);
        anchor = anchorX; top = dockTop;
        IsOpen = true; closing = false;
        batch?.Dispose(); batch = null; track?.Stop(); backdrop.StopPresentationAnimation();
        var visual = ElementCompositionPreview.GetElementVisual(root);
        visual.TransformMatrix = Matrix4x4.Identity;
        visual.Opacity = uiSettings.AnimationsEnabled ? 0 : 1;
        backdrop.SetPresentationTransform(Matrix4x4.Identity, visual.Opacity);
        BuildRows(entries);
        root.IsHitTestVisible = true;
        Activate();
        host.ActivateForUserInput();
        var revision = ++generation;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!IsOpen || revision != generation) return;
            UpdateBounds(); Animate(false);
            actions.FirstOrDefault()?.Focus(FocusState.Programmatic);
        });
    }

    private void BuildRows(IReadOnlyList<DockAppMenuEntry> commands, bool submenu = false)
    {
        rows.Children.Clear(); actions.Clear();
        Separator();
        if (submenu) AddRow(new("Back", "\uE72B", Children: entries));
        foreach (var entry in commands)
        {
            if (entry.Text.Length == 0) Separator();
            else if (entry.IsHeading) rows.Children.Add(new TextBlock { Text = entry.Text, FontSize = 11,
                Foreground = theme.Secondary, Margin = new(10, 5, 10, 3) });
            else AddRow(entry);
        }
        Reposition(anchor, top);
        scroll.ChangeView(null, 0, null);
    }

    private void Separator() => rows.Children.Add(new Border
    { Height = 1, Margin = new(10, 4, 10, 4), Background = theme.Divider });

    private void AddRow(DockAppMenuEntry entry)
    {
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new() { Width = new(20) });
        content.ColumnDefinitions.Add(new());
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        content.Children.Add(new FontIcon { Glyph = entry.Glyph, FontSize = 16, Foreground = theme.Primary });
        var label = new TextBlock { Text = entry.Text, FontSize = 13, Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        if (entry.Children is not null)
        {
            var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = theme.Secondary };
            Grid.SetColumn(chevron, 2); content.Children.Add(chevron);
        }
        var button = new Button { Content = content, Height = 34, Padding = new(10, 0, 10, 0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        theme.StyleButton(button);
        AutomationProperties.SetName(button, entry.Text);
        button.Click += (_, _) =>
        {
            if (entry.Children is { } children)
            {
                BuildRows(children, !ReferenceEquals(children, entries));
                actions.FirstOrDefault()?.Focus(FocusState.Keyboard);
            }
            else { Hide(immediate: true); entry.Invoke?.Invoke(); }
        };
        rows.Children.Add(button); actions.Add(button);
    }

    public void Reposition(double anchorX, double dockTop)
    {
        anchor = anchorX; top = dockTop;
        if (!IsOpen) return;
        var (area, dpi, dockBounds) = WindowPreviewPlacement.GetArea(dock);
        scale = dpi;
        var bounds = WindowPreviewLayout.Position(new(area.X, area.Y, area.Width, area.Height),
            new(dockBounds.X, dockBounds.Y, dockBounds.Width, dockBounds.Height), scale, anchor, top + 6,
            240 + Gutter * 2, Math.Min(490, 68 + Gutter * 2 + rows.Children.Sum(child => child is Button ? 34 : child is TextBlock ? 24 : 9)));
        AppWindow.MoveAndResize(new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
        UpdateBounds();
    }

    private void UpdateBounds() => backdrop.SetBounds(AppWindow.Size.Width / scale, AppWindow.Size.Height / scale,
        Math.Max(0, AppWindow.Size.Width / scale - Gutter * 2),
        Math.Max(0, AppWindow.Size.Height / scale - Gutter * 2), Gutter, scale, 1);

    private void Animate(bool hide)
    {
        batch?.Dispose(); batch = null;
        var visual = ElementCompositionPreview.GetElementVisual(root);
        track ??= new(visual);
        track.Stop(); backdrop.StopPresentationAnimation();
        if (!uiSettings.AnimationsEnabled)
        {
            visual.TransformMatrix = Matrix4x4.Identity; visual.Opacity = 1;
            backdrop.SetPresentationTransform(Matrix4x4.Identity, 1);
            if (hide) FinishHide();
            return;
        }
        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            var t = i / 8f; var eased = t * t * (3 - 2 * t);
            var visible = hide ? 1 - eased : eased;
            return new PopupCompositionFrame(Matrix4x4.CreateScale(0.97f + 0.03f * visible,
                0.97f + 0.03f * visible, 1, new((float)(AppWindow.Size.Width / scale / 2),
                    (float)(AppWindow.Size.Height / scale), 0)) * Matrix4x4.CreateTranslation(0, (1 - visible) * 4, 0), visible);
        }).ToArray();
        var revision = ++generation;
        batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        track.Bind(); track.Start(frames, TimeSpan.FromMilliseconds(hide ? 110 : 150));
        backdrop.AnimatePresentation(frames, TimeSpan.FromMilliseconds(hide ? 110 : 150));
        batch.Completed += (_, _) =>
        {
            if (disposed || revision != generation) return;
            track.Stop(); backdrop.StopPresentationAnimation();
            visual.TransformMatrix = Matrix4x4.Identity; visual.Opacity = 1;
            backdrop.SetPresentationTransform(Matrix4x4.Identity, 1);
            if (hide) FinishHide();
        };
        batch.End();
    }

    public void Hide(bool immediate = false)
    {
        if (!IsOpen || disposed) return;
        root.IsHitTestVisible = false;
        if (immediate) { FinishHide(); return; }
        if (closing) return;
        closing = true; Animate(true);
    }

    private void FinishHide()
    {
        generation++; IsOpen = false; closing = false;
        batch?.Dispose(); batch = null; track?.Stop(); backdrop.StopPresentationAnimation();
        AppWindow.Hide(); rows.Children.Clear(); actions.Clear(); entries = [];
        Hidden?.Invoke(this, EventArgs.Empty);
    }
}
