using GlassDock.App.ViewModels;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal sealed class WindowPreviewCoordinator : IDisposable
{
    private readonly DockApplicationsViewModel applications;
    private readonly FrameworkElement dockRoot;
    private readonly Func<double> dockTop;
    private readonly nint dock;
    private readonly WindowPreviewSession session = new();
    private readonly WindowFrameCache frameCache = new();
    private WindowPreviewWindow? preview;
    private readonly Dictionary<string, Button> buttons = [];
    private CancellationTokenSource? pending;
    private object? pointerOwner;
    private DockApplicationItem? pendingShowAll;
    private readonly HashSet<object> contextMenus = [];
    private readonly Dictionary<string, (MenuFlyout Menu, long[] Handles, bool Pinned)> menuSnapshots = [];
    private bool disposed;
    public bool ContextMenuOpen => contextMenus.Count > 0;
    public bool HoldsDock => ContextMenuOpen || session.State != WindowPreviewState.Hidden;
    public event EventHandler? HoldChanged;
    public event EventHandler<string>? ActionFailed;

    public WindowPreviewCoordinator(DockApplicationsViewModel applications, FrameworkElement dockRoot, nint dock, Func<double> dockTop)
    {
        this.applications = applications; this.dockRoot = dockRoot; this.dock = dock; this.dockTop = dockTop;
        applications.SnapshotApplied += OnSnapshot;
        TrackFrames();
    }

    public void Attach(Button button, DockApplicationItem item)
    {
        buttons[item.Id] = button;
        button.PointerEntered += (_, _) => { pointerOwner = button; Enter(item); };
        button.PointerExited += (_, _) =>
        {
            if (!ReferenceEquals(pointerOwner, button)) return;
            pointerOwner = null; Leave();
        };
        button.Click += (_, _) => Hide();
        var menu = new MenuFlyout();
        menu.Opening += (_, _) =>
        {
            pendingShowAll = null;
            BeginContextMenu(menu);
            Hide();
            BuildMenu(menu, item);
            menuSnapshots[item.Id] = (menu, item.Application.Windows.Select(w => w.Handle).ToArray(), item.IsPinned);
        };
        menu.Closed += (_, _) =>
        {
            menu.Items.Clear();
            menuSnapshots.Remove(item.Id);
            EndContextMenu(menu);
        };
        button.ContextFlyout = menu;
    }

    public void Detach(string id)
    {
        if (buttons.Remove(id, out var button))
        {
            if (ReferenceEquals(pointerOwner, button)) pointerOwner = null;
            if (button.ContextFlyout is MenuFlyout menu) { menu.Hide(); EndContextMenu(menu); }
            button.ContextFlyout = null;
        }
        menuSnapshots.Remove(id);
        if (session.ApplicationId == id) Hide();
    }

    private async void Enter(DockApplicationItem item, bool immediate = false)
    {
        if (disposed || ContextMenuOpen) return;
        pending?.Cancel();
        if (session.ApplicationId == item.Id && session.State is not (WindowPreviewState.Hidden or WindowPreviewState.Waiting)) return;
        Hide();
        var revision = session.Begin(item.Application);
        HoldChanged?.Invoke(this, EventArgs.Empty);
        if (!item.IsRunning) return; // The existing app tooltip is the only UI for stopped apps.
        var delay = new CancellationTokenSource(); pending = delay;
        try
        {
            if (!immediate) await Task.Delay(350, delay.Token);
            if (disposed || ContextMenuOpen || delay.IsCancellationRequested || !session.Show(revision) || !buttons.TryGetValue(item.Id, out var button)) return;
            frameCache.Request(item.Application.Windows);
            ToolTipService.SetToolTip(button, null);
            EnsurePreview();
            preview!.Show(Anchor(button), dockTop());
            foreach (var window in item.Application.Windows) frameCache.Report(window, "preview-cards-shown");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Hide(); ActionFailed?.Invoke(this, "Window preview unavailable: " + error.Message); }
        finally { if (ReferenceEquals(pending, delay)) pending = null; delay.Dispose(); }
    }

    private void EnsurePreview()
    {
        if (preview is not null) return;
        preview = new(dock, session, frameCache);
        preview.Closed += (_, _) =>
        {
            if (ReferenceEquals(pointerOwner, preview)) pointerOwner = null;
            preview = null;
            if (!disposed) Hide();
        };
        preview.PointerArrived += (_, _) => { pointerOwner = preview; pending?.Cancel(); };
        preview.PointerDeparted += (_, _) =>
        {
            WindowPreviewWindow.Trace("COORD DEPART");
            if (!ReferenceEquals(pointerOwner, preview)) return;
            pointerOwner = null; Leave();
        };
        preview.WindowChosen += (_, window) =>
        {
            var selected = session.Activate(window.Handle);
            frameCache.Report(window, "preview-click-before-activation");
            if (selected is not null && !applications.ActivateWindow(selected))
                ActionFailed?.Invoke(this, "Windows could not focus that window; it may have closed.");
            frameCache.Report(window, "preview-click-activation-requested");
            Hide();
        };
        preview.WindowCloseRequested += (_, window) =>
        {
            // WM_CLOSE preserves the application's own save/confirmation handling.
            // The service refreshes the snapshot; OnSnapshot rebuilds only after the HWND disappears.
            if (!applications.CloseWindow(window))
                ActionFailed?.Invoke(this, "That window is no longer available to close.");
        };
    }

    private async void Leave()
    {
        if (disposed || ContextMenuOpen) return;
        WindowPreviewWindow.Trace("LEAVE scheduled");
        pending?.Cancel();
        var delay = new CancellationTokenSource(); pending = delay;
        try { await Task.Delay(400, delay.Token); if (pointerOwner is null) Hide(); }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(pending, delay)) pending = null; delay.Dispose(); }
    }

    private double Anchor(Button button) => button.TransformToVisual(dockRoot).TransformPoint(new(button.ActualWidth / 2, 0)).X;

    private void OnSnapshot(object? sender, EventArgs args)
    {
        TrackFrames();
        foreach (var (appId, snapshot) in menuSnapshots.ToArray())
        {
            var current = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == appId);
            if (current is null || current.IsPinned != snapshot.Pinned ||
                !current.Application.Windows.Select(w => w.Handle).ToHashSet().SetEquals(snapshot.Handles))
                snapshot.Menu.Hide();
        }
        if (session.ApplicationId is not { } id) return;
        var item = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == id);
        if (item is null || !item.IsRunning) { Hide(); return; }
        session.Refresh(item.Application.Windows);
        Reposition();
    }

    private void TrackFrames() => frameCache.Track(
        applications.VisibleDockApplications.SelectMany(item => item.Application.Windows));

    public void Reposition()
    {
        if (session.State is WindowPreviewState.Hidden or WindowPreviewState.Waiting || session.ApplicationId is not { } id) return;
        if (buttons.TryGetValue(id, out var button))
        {
            ToolTipService.SetToolTip(button, null);
            preview?.Refresh(Anchor(button), dockTop());
        }
    }

    private void BuildMenu(MenuFlyout menu, DockApplicationItem item)
    {
        menu.Items.Clear();
        void Add(string title, Func<bool> action)
        {
            var entry = new MenuFlyoutItem { Text = title };
            entry.Click += (_, _) => { if (!action()) ActionFailed?.Invoke(this, $"Could not complete '{title}' for {item.Name}."); };
            menu.Items.Add(entry);
        }
        Add(item.IsRunning ? "Activate" : "Open", () => applications.Activate(item));
        var canLaunch = WindowsApplicationLauncher.Target(item.Application) is not null;
        if (item.IsRunning && canLaunch) Add("New window", () => applications.Launch(item));

        if (item.IsRunning)
        {
            Add("Show All Windows", () => { pendingShowAll = item; return true; });
            if (item.Application.Windows.Count > 1)
            {
                var windows = new MenuFlyoutSubItem { Text = "Windows" };
                foreach (var window in item.Application.Windows)
                {
                    var title = string.IsNullOrWhiteSpace(window.Title) ? window.Name : window.Title;
                    var entry = new MenuFlyoutItem { Text = title };
                    entry.Click += (_, _) =>
                    {
                        if (!applications.ActivateWindow(window)) ActionFailed?.Invoke(this, "Windows could not focus that window; it may have closed.");
                    };
                    windows.Items.Add(entry);
                }
                menu.Items.Add(windows);
                Add("Close All Windows", () =>
                {
                    var windowsToClose = item.Application.Windows.ToArray();
                    var success = windowsToClose.Length > 0;
                    foreach (var window in windowsToClose) success &= applications.CloseWindow(window);
                    return success;
                });
            }
            else
            {
                var window = item.Application.Windows[0];
                Add("Close Window", () => applications.CloseWindow(window));
            }
        }

        if (item.IsPinned || canLaunch)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(item.IsPinned ? "Unpin from Dock" : "Pin to Dock", () => applications.SetPinned(item, !item.IsPinned));
        }

        var canElevate = WindowsApplicationLauncher.CanRunAsAdministrator(item.Application);
        var canLocate = WindowsApplicationLauncher.CanOpenFileLocation(item.Application);
        if (canElevate || canLocate)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            if (canElevate) Add("Run as Administrator", () => applications.RunAsAdministrator(item));
            if (canLocate) Add("Open File Location", () => applications.OpenFileLocation(item));
        }
    }

    public void Hide()
    {
        var held = HoldsDock;
        pending?.Cancel();
        if (session.ApplicationId is { } id && buttons.TryGetValue(id, out var button))
        {
            var item = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == id);
            if (item is not null) ToolTipService.SetToolTip(button, CreateTooltip(item.Name));
        }
        session.Hide(); preview?.Hide();
        if (held != HoldsDock) HoldChanged?.Invoke(this, EventArgs.Empty);
    }

    public void BeginContextMenu(object source)
    {
        if (disposed || !contextMenus.Add(source)) return;
        pending?.Cancel();
        HoldChanged?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    public void EndContextMenu(object source)
    {
        // Defer release one dispatch turn so replacing A with B does not
        // briefly resume/reset hover visuals between the two menu events.
        dockRoot.DispatcherQueue.TryEnqueue(() =>
        {
            if (disposed || source is MenuFlyout { IsOpen: true } || !contextMenus.Remove(source)) return;
            HoldChanged?.Invoke(this, EventArgs.Empty);
            if (!ContextMenuOpen && pendingShowAll is { } requested)
            {
                pendingShowAll = null;
                var current = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == requested.Id && item.IsRunning);
                if (current is not null) Enter(current, immediate: true);
            }
        });
    }

    public void Dispose()
    {
        disposed = true;
        pointerOwner = null;
        pendingShowAll = null;
        foreach (var menu in contextMenus.OfType<MenuFlyout>().ToArray()) menu.Hide();
        contextMenus.Clear();
        menuSnapshots.Clear();
        applications.SnapshotApplied -= OnSnapshot;
        pending?.Cancel();
        preview?.Close(); preview = null;
        frameCache.Dispose();
        buttons.Clear();
    }

    public static ToolTip CreateTooltip(string name) => new()
    {
        Content = name, RequestedTheme = ElementTheme.Dark, CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(global::Windows.UI.Color.FromArgb(40, 235, 242, 255)),
        Background = new AcrylicBrush
        {
            TintColor = global::Windows.UI.Color.FromArgb(255, 35, 40, 52), TintOpacity = .65,
            FallbackColor = global::Windows.UI.Color.FromArgb(245, 35, 40, 52)
        }
    };
}
