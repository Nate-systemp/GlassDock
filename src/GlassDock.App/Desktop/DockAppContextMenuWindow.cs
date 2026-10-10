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
    // The visual geometry is identical across Dark, Light, Frosted, Acrylic and Clear.
    // Only the surface material and palette change with the appearance setting.
    // Scale the complete existing menu uniformly rather than redesigning its
    // layout, colors, hover treatment, or native glass/screenshot pipeline.
    private const double MenuSizeScale = 0.85;
    private static double MenuSize(double value) => value * MenuSizeScale;
    private const double MenuCornerRadius = 9 * MenuSizeScale;
    private const double MenuWidth = 258 * MenuSizeScale;
    private const double MenuRowHeight = 36 * MenuSizeScale;
    private const double MenuRowRadius = 5 * MenuSizeScale;
    private readonly nint dock;
    private readonly Grid root = new();
    private readonly StackPanel rows = new();
    private readonly AdaptiveAppIcon icon = new((int)Math.Round(MenuSize(20)), 1, showTile: false);
    private readonly TextBlock title = new() { FontFamily = new("Segoe UI Variable Text"), FontSize = MenuSize(13), FontWeight = Microsoft.UI.Text.FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock status = new() { FontFamily = new("Segoe UI Variable Text"), FontSize = MenuSize(10.5), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new(Gutter), IsHitTestVisible = false };
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly PopupLiquidGlassSurface liquid;
    private readonly UtilityPopupTheme theme = new();
    // Silver / pearl-blue material sampled from the generated Jump List mockup.
    // The native Clear/Frosted/Acrylic optics remain beneath this translucent
    // overlay; no changes to desktop capture or screenshot exclusion are needed.
    private readonly LinearGradientBrush referenceGlassFill = new()
    {
        StartPoint = new(0.08, 0),
        EndPoint = new(0.92, 1),
        GradientStops =
        {
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(236, 195, 203, 216), Offset = 0 },
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(236, 224, 232, 244), Offset = 0.53 },
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(236, 184, 194, 209), Offset = 1 }
        }
    };
    private readonly LinearGradientBrush glassEdge = new()
    {
        StartPoint = new(0, 0),
        EndPoint = new(1, 1),
        GradientStops =
        {
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(205, 246, 249, 253), Offset = 0 },
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(105, 207, 217, 229), Offset = 0.52 },
            new GradientStop { Color = global::Windows.UI.Color.FromArgb(165, 130, 143, 160), Offset = 1 }
        }
    };
    private readonly Border chrome;
    private readonly SolidColorBrush activeInk = new(Microsoft.UI.Colors.White);
    private readonly SolidColorBrush menuAccent = new(global::Windows.UI.Color.FromArgb(255, 24, 126, 247));
    private readonly InteractiveGlassWindowHost host;
    private readonly ScrollViewer scroll;
    private readonly List<Button> actions = [];
    private IReadOnlyList<DockAppMenuEntry> entries = [];
    private readonly global::Windows.UI.ViewManagement.UISettings uiSettings = new();
    private PopupCompositionTrack? track;
    private Microsoft.UI.Composition.CompositionScopedBatch? batch;
    private int generation;
    private bool disposed, closing, snippingCaptureActive;
    private int showRevision;
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
        // Dark/Light use their original solid surface. Glass modes receive a
        // pearlescent silver/gray wash matching the supplied Jump List reference.
        chrome = new Border { Margin = new(Gutter), CornerRadius = new(MenuCornerRadius),
            Background = theme.Overlay, BorderBrush = theme.TileBorder,
            BorderThickness = new(1), IsHitTestVisible = false };
        root.Children.Add(chrome);
        var body = new Grid { Margin = new(Gutter + MenuSize(3)), RowSpacing = MenuSize(1) };
        body.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(scroll);
        // Match the native Jump List hierarchy: actions first, compact app
        // identity at the bottom, separated by a single hairline divider.
        var footer = new StackPanel { Spacing = 0 };
        footer.Children.Add(new Border { Height = 1, Margin = new(MenuSize(11), MenuSize(7), MenuSize(11), MenuSize(3)), Background = theme.Divider });
        var header = new Grid { Margin = new(MenuSize(14), MenuSize(7), MenuSize(10), MenuSize(4)), ColumnSpacing = MenuSize(11) };
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new());
        header.Children.Add(icon);
        title.Foreground = theme.Primary;
        status.Foreground = theme.Secondary;
        var heading = new StackPanel { Spacing = MenuSize(1), VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(title);
        heading.Children.Add(status);
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        footer.Children.Add(header);
        Grid.SetRow(footer, 1);
        body.Children.Add(footer);
        root.Children.Add(body);
        Content = root;
        host = new(WinRT.Interop.WindowNative.GetWindowHandle(this))
        { EnableHostBackdropBrush = true, UseDockLayeredTransparency = true };
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        // The native client and Content must exist before the backdrop connects.
        SystemBackdrop = backdrop;
        liquid = new(this, root, backdrop);
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
        Activated += async (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated || snippingCaptureActive) return;
            // The Win+Shift+S event comes through an asynchronous keyboard message.
            // Let it arrive before treating Snipping Tool's focus as click-outside.
            var revision = showRevision;
            await Task.Delay(180);
            if (!disposed && IsOpen && !snippingCaptureActive && showRevision == revision)
                Hide(immediate: true);
        };
        Closed += (_, _) =>
        {
            disposed = true; IsOpen = false; generation++; batch?.Dispose(); track?.Dispose();
            host.Dispose(); SystemBackdrop = null;
        };
    }

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        // Same exact menu geometry in all five modes. Only the color/material
        // changes: silver glass in Clear/Frosted/Acrylic; original solid palette
        // in Dark/Light. The light system theme removes dark WinUI button chrome.
        if (mode.GlassStyle() is not null)
        {
            theme.Primary.Color = global::Windows.UI.Color.FromArgb(255, 22, 29, 40);
            theme.Secondary.Color = global::Windows.UI.Color.FromArgb(255, 39, 48, 62);
            theme.Muted.Color = global::Windows.UI.Color.FromArgb(255, 83, 94, 111);
            theme.Divider.Color = global::Windows.UI.Color.FromArgb(128, 103, 117, 137);
            // Keep Clear more translucent than Frosted while still reading as
            // pale silver on the user's black wallpaper (not navy/black).
            var alpha = mode switch
            {
                DockAppearanceMode.Frosted => (byte)248,
                DockAppearanceMode.Acrylic => (byte)242,
                _ => (byte)236
            };
            referenceGlassFill.GradientStops[0].Color = global::Windows.UI.Color.FromArgb(alpha, 195, 203, 216);
            referenceGlassFill.GradientStops[1].Color = global::Windows.UI.Color.FromArgb(alpha, 224, 232, 244);
            referenceGlassFill.GradientStops[2].Color = global::Windows.UI.Color.FromArgb(alpha, 184, 194, 209);
            chrome.Background = referenceGlassFill;
            chrome.BorderBrush = glassEdge;
            menuAccent.Color = global::Windows.UI.Color.FromArgb(255, 24, 126, 247);
        }
        else
        {
            chrome.Background = theme.Overlay;
            chrome.BorderBrush = theme.TileBorder;
            menuAccent.Color = global::Windows.UI.Color.FromArgb(229, 31, 112, 243);
        }
        root.RequestedTheme = mode == DockAppearanceMode.Dark ? ElementTheme.Dark : ElementTheme.Light;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode, cornerRadius: MenuCornerRadius);
        glass.Apply(GlassDock.Core.Materials.UtilityMaterial.CreateForPopup(appearance, mode) with
        { CornerRadius = MenuCornerRadius, ShadowOpacity = 0, BorderOpacity = 0, EdgeHighlight = 0 });
    }

    public void Show(DockApplicationItem item, IReadOnlyList<DockAppMenuEntry> commands, double anchorX, double dockTop, string stateText = "")
    {
        entries = commands;
        title.Text = item.Name;
        status.Text = stateText;
        status.Visibility = string.IsNullOrEmpty(stateText) ? Visibility.Collapsed : Visibility.Visible;
        icon.SetIcon(item.Application.Icon);
        anchor = anchorX; top = dockTop;
        ++showRevision;
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

    // Keep both the popup HWND and its Clear-mode GPU glass image screenshot-visible.
    // Frosted/Acrylic/Dark/Light do not use this live Clear capture path.
    public void BeginSnippingCapture()
    {
        if (disposed || !IsOpen || snippingCaptureActive) return;
        snippingCaptureActive = true;
        liquid.BeginScreenshotMode();
    }

    public void EndSnippingCapture()
    {
        if (!snippingCaptureActive) return;
        snippingCaptureActive = false;
        liquid.EndScreenshotMode();
        // Snipping Tool may have taken foreground ownership without delivering
        // another Deactivated event. Restore the usual click-away dismissal.
        if (IsOpen && GetForegroundWindow() != WinRT.Interop.WindowNative.GetWindowHandle(this))
            Hide(immediate: true);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private void BuildRows(IReadOnlyList<DockAppMenuEntry> commands, bool submenu = false)
    {
        rows.Children.Clear(); actions.Clear();
        var needsSeparator = false;
        var hasHeading = false;
        var tasksHeadingShown = false;
        if (submenu)
        {
            AddRow(new("Back", "\uE72B", Children: entries));
            needsSeparator = true;
        }
        foreach (var entry in commands)
        {
            if (entry.Text.Length == 0)
            {
                // The command model uses blank entries as section separators.
                // Don't render duplicate or leading dividers.
                needsSeparator = rows.Children.Count > 0;
                continue;
            }
            if (needsSeparator)
            {
                Separator();
                needsSeparator = false;
                hasHeading = false;
            }
            if (entry.IsHeading)
            {
                AddHeading(entry.Text);
                hasHeading = true;
            }
            else
            {
                // Sections without supplied headings still get a quiet Tasks
                // label, rather than fabricating unsupported Recent items.
                if (!hasHeading && !tasksHeadingShown && !submenu)
                {
                    AddHeading("Tasks");
                    hasHeading = true;
                    tasksHeadingShown = true;
                }
                AddRow(entry);
            }
        }
        Reposition(anchor, top);
        scroll.ChangeView(null, 0, null);
    }

    private void AddHeading(string text) => rows.Children.Add(new TextBlock
    {
        Text = text, FontFamily = new("Segoe UI Variable Text"), FontSize = MenuSize(12.5), FontWeight = Microsoft.UI.Text.FontWeights.Normal,
        Foreground = theme.Secondary, Margin = new(MenuSize(14), MenuSize(7), MenuSize(10), MenuSize(5))
    });

    private void Separator() => rows.Children.Add(new Border
    { Height = 1, Margin = new(MenuSize(11), MenuSize(7), MenuSize(11), MenuSize(5)), Background = theme.Divider });

    private void AddRow(DockAppMenuEntry entry)
    {
        var content = new Grid { ColumnSpacing = MenuSize(11), Margin = new(MenuSize(17), 0, MenuSize(9), 0) };
        content.ColumnDefinitions.Add(new() { Width = new(MenuSize(19)) });
        content.ColumnDefinitions.Add(new());
        content.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var glyph = new FontIcon { Glyph = entry.Glyph, FontSize = MenuSize(16), Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(glyph);
        var label = new TextBlock { Text = entry.Text, FontFamily = new("Segoe UI Variable Text"), FontSize = MenuSize(13), Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(label, 1); content.Children.Add(label);
        FontIcon? chevron = null;
        if (entry.Children is not null)
        {
            chevron = new FontIcon { Glyph = "\uE76C", FontSize = MenuSize(12), Foreground = theme.Secondary };
            Grid.SetColumn(chevron, 2); content.Children.Add(chevron);
        }
        var highlight = new Border { Background = menuAccent, CornerRadius = new(MenuRowRadius),
            IsHitTestVisible = false };
        // A fixed-height content grid makes the blue hover capsule fill the row.
        // Previously WinUI auto-sized this to the 16px label, leaving a cramped strip.
        var row = new Grid { Height = MenuRowHeight - MenuSize(4), VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(highlight);
        row.Children.Add(content);
        var button = new Button { Content = row, Height = MenuRowHeight, Padding = new(0),
            Margin = new(MenuSize(1), 0, MenuSize(1), 0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center };
        theme.StyleButton(button);
        button.CornerRadius = new(MenuRowRadius);
        // The default WinUI Button hover fill would square off the blue selection
        // and hide its border radius. Our shared highlight owns this interaction.
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Resources["ButtonBackgroundPointerOver"] = clear;
        button.Resources["ButtonBackgroundPressed"] = clear;
        AutomationProperties.SetName(button, entry.Text);
        var hoverVisual = ElementCompositionPreview.GetElementVisual(highlight);
        hoverVisual.Opacity = 0;
        var pointerInside = false;
        var highlighted = false;
        void SetHighlight(bool active)
        {
            if (highlighted == active) return;
            highlighted = active;
            glyph.Foreground = active ? activeInk : theme.Primary;
            label.Foreground = active ? activeInk : theme.Primary;
            if (chevron is not null) chevron.Foreground = active ? activeInk : theme.Secondary;
            if (!uiSettings.AnimationsEnabled)
            {
                hoverVisual.StopAnimation("Opacity");
                hoverVisual.Opacity = active ? 1f : 0f;
                return;
            }
            var transition = hoverVisual.Compositor.CreateScalarKeyFrameAnimation();
            transition.InsertKeyFrame(1, active ? 1f : 0f);
            transition.Duration = TimeSpan.FromMilliseconds(active ? 115 : 95);
            hoverVisual.StartAnimation("Opacity", transition);
        }
        button.PointerEntered += (_, _) => { pointerInside = true; SetHighlight(true); };
        button.PointerExited += (_, _) =>
        {
            pointerInside = false;
            SetHighlight(button.FocusState == FocusState.Keyboard);
        };
        button.GotFocus += (_, _) => SetHighlight(pointerInside || button.FocusState == FocusState.Keyboard);
        button.LostFocus += (_, _) => SetHighlight(pointerInside);
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
            new(dockBounds.X, dockBounds.Y, dockBounds.Width, dockBounds.Height), scale, anchor, top + 2,
            MenuWidth + Gutter * 2, Math.Min(MenuSize(480), (status.Visibility == Visibility.Visible ? MenuSize(93) : MenuSize(87)) + rows.Children.Sum(child =>
                child is Button ? MenuRowHeight : child is TextBlock ? MenuSize(28) : MenuSize(13))));
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
        ++showRevision;
        generation++; IsOpen = false; closing = false;
        batch?.Dispose(); batch = null; track?.Stop(); backdrop.StopPresentationAnimation();
        AppWindow.Hide(); rows.Children.Clear(); actions.Clear(); entries = [];
        Hidden?.Invoke(this, EventArgs.Empty);
    }
}
