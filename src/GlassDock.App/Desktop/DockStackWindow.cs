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
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace GlassDock.App.Desktop;

internal sealed record StackAppDrag(string StackId, string AppId);

/// <summary>Retained grid surface; uses the dock's glass pipeline and shared application/badge sources.</summary>
internal sealed class DockStackWindow : Window
{
    internal const string DragFormat = "Doky.StackApplication";
    private const double Gutter = UtilityPopupStyle.Gutter;
    private readonly Grid root = new();
    private readonly Grid grid = new() { RowSpacing = 8, ColumnSpacing = 8 };
    private readonly TextBox name = new() { MaxLength = 40, IsReadOnly = true, BorderThickness = new(0), FontSize = 14 };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new(Gutter), IsHitTestVisible = false };
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly nint dock;
    private readonly Func<DockApplication, bool> launch;
    private readonly Func<ApplicationIdentity, BadgeDisplayState> badge;
    private readonly Action<string, string> rename;
    private readonly Action<string, IReadOnlyList<string>> reorder;
    private readonly Func<bool> pointerOnSource;
    private DockApplication? stack;
    private readonly Dictionary<string, AdaptiveAppIcon> appIcons = [];
    private PopupCompositionTrack? track;
    private Microsoft.UI.Composition.CompositionScopedBatch? batch;
    private int generation;
    private bool disposed, dragging, closing;
    private double scale = 1;
    public bool IsOpen { get; private set; }
    public bool IsClosing => closing;
    public string? StackId => stack?.Id;
    public event EventHandler? Hidden;
    public event Action<DockApplication, string, double>? AppActionsRequested;

    public DockStackWindow(nint dock, Func<DockApplication, bool> launch,
        Func<ApplicationIdentity, BadgeDisplayState> badge, Action<string, string> rename,
        Action<string, IReadOnlyList<string>> reorder, Func<bool> pointerOnSource)
    {
        this.dock = dock; this.launch = launch; this.badge = badge; this.rename = rename; this.reorder = reorder;
        this.pointerOnSource = pointerOnSource;
        Title = "Doky — Stack"; WindowBranding.Apply(this); AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        root.Children.Add(glass);
        root.Children.Add(new Border { Margin = new(Gutter), CornerRadius = new(28), Background = theme.Overlay, IsHitTestVisible = false });
        var body = new StackPanel { Margin = new(Gutter + 12), Spacing = 10 };
        name.Foreground = theme.Primary; name.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        AutomationProperties.SetName(name, "Stack name");
        body.Children.Add(name); body.Children.Add(grid); root.Children.Add(body); Content = root;
        host = new(WinRT.Interop.WindowNative.GetWindowHandle(this)) { EnableHostBackdropBrush = true, UseDockLayeredTransparency = true };
        host.Configure(); SystemBackdrop = backdrop;
        root.SizeChanged += (_, _) => UpdateBounds(); root.Loaded += (_, _) => UpdateBounds();
        name.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Enter) { SaveName(); e.Handled = true; } };
        name.LostFocus += (_, _) => SaveName();
        root.KeyDown += (_, e) => { if (e.Key == global::Windows.System.VirtualKey.Escape) { Hide(); e.Handled = true; } };
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated && !dragging && !pointerOnSource()) Hide(); };
        Closed += (_, _) => { disposed = true; generation++; batch?.Dispose(); track?.Dispose(); host.Dispose(); SystemBackdrop = null; };
    }
    private void SaveName()
    {
        if (name.IsReadOnly || stack is null) return;
        name.IsReadOnly = true; rename(stack.Id, name.Text);
    }
    public void Rename() { name.IsReadOnly = false; name.Focus(FocusState.Programmatic); name.SelectAll(); }
    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode); root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
    }
    public void Show(DockApplication value, double anchorX, double dockTop)
    {
        generation++;
        batch?.Dispose(); batch = null; track?.Stop(); backdrop.StopPresentationAnimation();
        Update(value); Position(anchorX, dockTop);
        IsOpen = true; closing = false; root.IsHitTestVisible = true;
        var visual = ElementCompositionPreview.GetElementVisual(root);
        visual.Opacity = 0; backdrop.SetPresentationTransform(Matrix4x4.Identity, 0);
        Activate();
        var revision = ++generation;
        DispatcherQueue.TryEnqueue(() => { if (IsOpen && revision == generation) { UpdateBounds(); Animate(false); } });
    }
    public void Update(DockApplication value)
    {
        if (dragging) return;
        var rebuild = stack is null || stack.Id != value.Id || !stack.StackApps.Select(app => (app.Id, app.Name, app.Icon, app.IsRunning))
            .SequenceEqual(value.StackApps.Select(app => (app.Id, app.Name, app.Icon, app.IsRunning)));
        stack = value;
        if (name.IsReadOnly) name.Text = value.Name;
        if (!rebuild) { RefreshBadges(); return; }
        grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear(); appIcons.Clear();
        var columns = DockStack.Columns(value.StackApps.Count);
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(76) });
        for (var i = 0; i < (value.StackApps.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = new(86) });
        for (var i = 0; i < value.StackApps.Count; i++)
        {
            var app = value.StackApps[i];
            var icon = new AdaptiveAppIcon(32, 1, false); icon.SetIcon(app.Icon); appIcons[app.Id] = icon;
            var body = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            body.Children.Add(icon);
            body.Children.Add(new TextBlock { Text = app.Name, Foreground = theme.Primary, FontSize = 11, MaxWidth = 68,
                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center });
            body.Children.Add(new Border { Width = 4, Height = 3, CornerRadius = new(2), Background = theme.Primary,
                Opacity = app.IsRunning ? 0.7 : 0, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = body, Padding = new(4), HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new(0), AllowDrop = true };
            theme.StyleButton(button); AutomationProperties.SetName(button, app.Name);
            button.ContextRequested += (_, e) =>
            {
                e.Handled = true;
                if (dragging) return;
                var (_, dpi, owner) = WindowPreviewPlacement.GetArea(dock);
                var point = button.TransformToVisual(root).TransformPoint(new(button.ActualWidth / 2, 0));
                var anchor = (AppWindow.Position.X - owner.X) / dpi + point.X;
                Hide(immediate: true);
                AppActionsRequested?.Invoke(app, value.Id, anchor);
            };
            global::Windows.Foundation.Point? press = null;
            long suppressClickUntil = 0;
            button.Click += (_, _) => { if (!dragging && Environment.TickCount64 >= suppressClickUntil && launch(app)) Hide(); };
            // ButtonBase consumes pointer input before CanDrag's gesture recognition.
            // Observe handled events, then hand the real gesture to WinUI's drag broker once.
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
            {
                var point = e.GetCurrentPoint(button);
                if (point.Properties.IsLeftButtonPressed) { press = point.Position; TraceDrag("Grid pointer pressed"); }
            }), true);
            button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
            {
                var point = e.GetCurrentPoint(button);
                if (dragging || press is not { } origin || !point.Properties.IsLeftButtonPressed) return;
                var dx = point.Position.X - origin.X; var dy = point.Position.Y - origin.Y;
                if (dx * dx + dy * dy < 36) return;
                press = null; dragging = true; e.Handled = true;
                button.ReleasePointerCaptures();
                TraceDrag("Grid gesture: starting drag broker");
                try { await button.StartDragAsync(point); }
                catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
                { TraceDrag("Start failed: " + error); }
                finally { dragging = false; suppressClickUntil = Environment.TickCount64 + 500; }
            }), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => press = null), true);
            button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => press = null), true);
            button.DragStarting += (_, e) =>
            {
                dragging = true;
                // Properties describe content; a real format is required to transport it between HWNDs.
                e.Data.SetData(DragFormat, value.Id + "\n" + app.Id);
                e.Data.RequestedOperation = DataPackageOperation.Move;
                TraceDrag("Starting: custom format registered");
            };
            button.DropCompleted += (_, e) => { TraceDrag("Completed: " + e.DropResult); dragging = false; Hide(); };
            button.DragOver += (_, e) => { e.Handled = true; e.AcceptedOperation = HasPayload(e) ? DataPackageOperation.Move : DataPackageOperation.None; };
            button.Drop += async (_, e) =>
            {
                e.Handled = true;
                var deferral = e.GetDeferral();
                try
                {
                    if (await PayloadAsync(e) is not { } data || data.StackId != value.Id) return;
                    var order = value.StackApps.Select(item => item.Id).ToList();
                    if (data.AppId == app.Id || !order.Remove(data.AppId)) return;
                    order.Insert(Math.Max(0, order.IndexOf(app.Id)), data.AppId);
                    reorder(value.Id, order); e.AcceptedOperation = DataPackageOperation.Move;
                    TraceDrag("Grid drop: reordered");
                }
                finally { deferral.Complete(); }
            };
            Grid.SetRow(button, i / columns); Grid.SetColumn(button, i % columns); grid.Children.Add(button);
        }
        RefreshBadges();
    }
    internal static bool HasPayload(DragEventArgs e) => e.DataView.Contains(DragFormat);
    internal static async Task<StackAppDrag?> PayloadAsync(DragEventArgs e)
    {
        if (!HasPayload(e)) return null;
        try
        {
            if (await e.DataView.GetDataAsync(DragFormat) is string text && text.Split('\n', 2) is { Length: 2 } parts)
                return new(parts[0], parts[1]);
            TraceDrag("Drop: invalid payload");
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        { TraceDrag("Drop: " + error.GetType().Name); }
        return null;
    }
    [System.Diagnostics.Conditional("DEBUG")]
    internal static void TraceDrag(string message)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "stack-drag.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void RefreshBadges()
    {
        if (stack is null) return;
        foreach (var app in stack.StackApps) if (appIcons.TryGetValue(app.Id, out var icon)) icon.SetNotificationBadge(badge(app.Identity));
    }
    public void Position(double anchorX, double dockTop)
    {
        if (stack is null) return;
        var (area, dpi, owner) = WindowPreviewPlacement.GetArea(dock); scale = dpi;
        var columns = DockStack.Columns(stack.StackApps.Count);
        var width = columns * 76 + (columns - 1) * 8 + 24 + Gutter * 2;
        var height = ((stack.StackApps.Count + columns - 1) / columns) * 94 + 60 + Gutter * 2;
        var bounds = WindowPreviewLayout.Position(new(area.X, area.Y, area.Width, area.Height),
            new(owner.X, owner.Y, owner.Width, owner.Height), scale, anchorX, dockTop + 6, width, height);
        if (AppWindow.Position.X != (int)bounds.X || AppWindow.Position.Y != (int)bounds.Y ||
            AppWindow.Size.Width != (int)bounds.Width || AppWindow.Size.Height != (int)bounds.Height)
            AppWindow.MoveAndResize(new((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height)); UpdateBounds();
    }
    private void UpdateBounds() => backdrop.SetBounds(AppWindow.Size.Width / scale, AppWindow.Size.Height / scale,
        Math.Max(0, AppWindow.Size.Width / scale - Gutter * 2), Math.Max(0, AppWindow.Size.Height / scale - Gutter * 2), Gutter, scale, 1);
    private void Animate(bool hide)
    {
        batch?.Dispose(); track?.Stop(); backdrop.StopPresentationAnimation();
        var visual = ElementCompositionPreview.GetElementVisual(root); track ??= new(visual);
        var frames = Enumerable.Range(0, 9).Select(i =>
        {
            var t = i / 8f; var v = t * t * (3 - 2 * t); if (hide) v = 1 - v;
            return new PopupCompositionFrame(Matrix4x4.CreateScale(0.96f + 0.04f * v, 0.96f + 0.04f * v, 1,
                new((float)(AppWindow.Size.Width / scale / 2), (float)(AppWindow.Size.Height / scale), 0)) * Matrix4x4.CreateTranslation(0, (1-v)*4, 0), v);
        }).ToArray();
        var revision = ++generation;
        batch = visual.Compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        track.Bind(); track.Start(frames, TimeSpan.FromMilliseconds(hide ? 110 : 160));
        backdrop.AnimatePresentation(frames, TimeSpan.FromMilliseconds(hide ? 110 : 160));
        batch.Completed += (_, _) => { if (disposed || revision != generation) return; track.Stop(); backdrop.StopPresentationAnimation();
            visual.TransformMatrix = Matrix4x4.Identity; visual.Opacity = 1; backdrop.SetPresentationTransform(Matrix4x4.Identity, 1);
            if (hide) { IsOpen = false; closing = false; AppWindow.Hide(); Hidden?.Invoke(this, EventArgs.Empty); } };
        batch.End();
    }
    public void Hide(bool immediate = false)
    {
        if (!IsOpen || disposed) return;
        root.IsHitTestVisible = false;
        if (immediate)
        {
            generation++; batch?.Dispose(); batch = null; track?.Stop(); backdrop.StopPresentationAnimation();
            IsOpen = false; closing = false; AppWindow.Hide(); Hidden?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (closing) return;
        closing = true; Animate(true);
    }
}
