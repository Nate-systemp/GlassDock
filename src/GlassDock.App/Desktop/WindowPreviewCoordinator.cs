using GlassDock.App.ViewModels;
using GlassDock.Core.Applications;
using GlassDock.Core.Settings;
using GlassDock.Windows.Applications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;

namespace GlassDock.App.Desktop;

internal sealed class WindowPreviewCoordinator : IDisposable
{
    private readonly DockApplicationsViewModel applications;
    private readonly FrameworkElement dockRoot;
    private readonly Func<double> dockTop;
    private readonly nint dock;
    private readonly GlassDockSettingsSession settings;
    private readonly Func<bool> canShow;
    private readonly Func<ApplicationWindow, Task<bool>> restoreElevated;
    private readonly Func<DockApplication, IReadOnlyList<string>, Task<bool>> openFiles;
    private bool filePickerActive;
    private double? memberAnchor;
    private readonly WindowPreviewSession session = new();
    private readonly WindowFrameCache frameCache = new();
    private WindowPreviewWindow? preview;
    private readonly Dictionary<string, Button> buttons = [];
    private CancellationTokenSource? pending;
    private object? pointerOwner;
    private DockApplicationItem? pendingShowAll;
    private readonly HashSet<object> contextMenus = [];
    private readonly Dictionary<string, (DockAppContextMenuWindow Menu, DockAppMenuState State)> menuSnapshots = [];
    private DockAppContextMenuWindow? appMenu;
    private string? menuOwner;
    private bool disposed;
    public bool ContextMenuOpen => contextMenus.Count > 0;
    public bool HoldsDock => ContextMenuOpen || session.State != WindowPreviewState.Hidden || preview?.IsClosing == true;
    public event EventHandler? HoldChanged;
    public event EventHandler<string>? ActionFailed;

    public WindowPreviewCoordinator(DockApplicationsViewModel applications, FrameworkElement dockRoot, nint dock,
        Func<double> dockTop, GlassDockSettingsSession settings, Func<bool> canShow,
        Func<ApplicationWindow, Task<bool>> restoreElevated,
        Func<DockApplication, IReadOnlyList<string>, Task<bool>> openFiles)
    {
        this.applications = applications; this.dockRoot = dockRoot; this.dock = dock; this.dockTop = dockTop;
        this.settings = settings;
        this.canShow = canShow;
        this.restoreElevated = restoreElevated;
        this.openFiles = openFiles;
        settings.Changed += SettingsChanged;
        applications.SnapshotApplied += OnSnapshot;
        dockRoot.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(DockPointerPressed), true);
        TrackFrames();
    }

    private void DockPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The dock intentionally does not activate on every click. Such an outside
        // click cannot produce the panel's Window.Activated(Deactivated) event.
        appMenu?.Hide(immediate: true);
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
        button.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ShowAppActions(item, Anchor(button));
        };
    }

    public void ShowAppActions(DockApplicationItem item, double anchor, Func<bool>? extract = null)
    {
        if (disposed || filePickerActive) return;
        appMenu ??= CreateAppMenu();
        pendingShowAll = null; menuSnapshots.Clear(); menuOwner = item.Id;
        memberAnchor = extract is null ? null : anchor;
        BeginContextMenu(appMenu);
        Hide(immediate: true);
        appMenu.ApplyAppearance(settings.Appearance, settings.Current.DockAppearanceMode);
        var state = MenuState(item);
        var model = new AppActionPanelModel(item.Application, WindowsApplicationLauncher.CanOpenWith(item.Application), extract is not null);
        appMenu.Show(item, BuildMenu(item, state, model, extract), anchor, dockTop(), model.Status);
        menuSnapshots[item.Id] = (appMenu, state);
    }

    private async void PickFiles(DockApplication application)
    {
        if (disposed || filePickerActive) return;
        filePickerActive = true;
        var hold = new object(); BeginContextMenu(hold);
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(dock));
            picker.FileTypeFilter.Add("*");
            var selected = await picker.PickMultipleFilesAsync();
            if (disposed || selected.Count == 0) return;
            if (!await openFiles(application, selected.Select(file => file.Path).ToArray()) && !disposed)
                ActionFailed?.Invoke(this, $"{application.Name} could not open the selected files.");
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        { if (!disposed) ActionFailed?.Invoke(this, "Could not open the file picker: " + error.Message); }
        finally { filePickerActive = false; EndContextMenu(hold); }
    }

    private DockAppContextMenuWindow CreateAppMenu()
    {
        var menu = new DockAppContextMenuWindow(dock);
        menu.Hidden += (_, _) =>
        {
            menuOwner = null;
            menuSnapshots.Clear();
            EndContextMenu(menu);
        };
        menu.Closed += (_, _) =>
        {
            menuOwner = null;
            menuSnapshots.Clear();
            appMenu = null;
            EndContextMenu(menu);
        };
        return menu;
    }

    public void Detach(string id)
    {
        if (buttons.Remove(id, out var button))
        {
            if (ReferenceEquals(pointerOwner, button)) pointerOwner = null;
            if (menuOwner == id) appMenu?.Hide(immediate: true);
            button.ContextFlyout = null;
        }
        menuSnapshots.Remove(id);
        if (session.ApplicationId == id) Hide();
    }

    private async void Enter(DockApplicationItem item, bool immediate = false)
    {
        if (disposed || ContextMenuOpen || !canShow()) return;
        pending?.Cancel();
        if (session.ApplicationId == item.Id && session.State is not (WindowPreviewState.Hidden or WindowPreviewState.Waiting)) return;
        Hide();
        var revision = session.Begin(item.Application);
        HoldChanged?.Invoke(this, EventArgs.Empty);
        if (!item.IsRunning) return; // The existing app tooltip is the only UI for stopped apps.
        var delay = new CancellationTokenSource(); pending = delay;
        try
        {
            if (!immediate) await Task.Delay(300, delay.Token);
            if (disposed || ContextMenuOpen || !canShow() || delay.IsCancellationRequested ||
                !session.Show(revision) || !buttons.TryGetValue(item.Id, out var button)) return;
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
        preview = new(dock, session, frameCache, settings.Appearance, settings.Current.DockAppearanceMode);
        preview.Hidden += (_, _) => HoldChanged?.Invoke(this, EventArgs.Empty);
        preview.DismissRequested += (_, _) => { pointerOwner = null; Hide(); };
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
        preview.WindowChosen += async (_, window) =>
        {
            var selected = session.Activate(window.Handle);
            frameCache.Report(window, "preview-click-before-activation");
            // Remove the topmost desktop peek before requesting foreground.
            // A snapshot/activation event must not leave the mirror over the real app.
            Hide(immediate: true);
            var activated = selected is not null &&
                (applications.ActivateWindow(selected) || await restoreElevated(selected));
            if (disposed) return;
            if (selected is not null && !activated)
                ActionFailed?.Invoke(this, "Windows could not focus that window; it may have closed.");
            frameCache.Report(window, "preview-click-activation-requested", new { activated });
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
            var current = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == appId)
                ?? applications.VisibleDockApplications.SelectMany(item => item.Application.StackApps)
                    .Where(app => app.Id == appId).Select(app => new DockApplicationItem(app)).FirstOrDefault();
            if (current is null || !snapshot.State.Matches(current.Application))
                snapshot.Menu.Hide(immediate: true);
        }
        if (session.ApplicationId is not { } id) return;
        var item = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == id);
        if (item is null || !item.IsRunning) { Hide(); return; }
        session.Refresh(item.Application.Windows);
        Reposition();
    }

    private void TrackFrames() => frameCache.Track(
        applications.VisibleDockApplications.SelectMany(item => item.Application.Windows));

    private void SettingsChanged(object? sender, GlassDockSettingsChangedEventArgs args)
    {
        preview?.ApplyAppearance(settings.Appearance, args.Settings.DockAppearanceMode);
        appMenu?.ApplyAppearance(settings.Appearance, args.Settings.DockAppearanceMode);
        Reposition();
    }

    public void Reposition()
    {
        if (memberAnchor is { } fixedAnchor && menuOwner is not null) appMenu?.Reposition(fixedAnchor, dockTop());
        else if (menuOwner is { } owner && buttons.TryGetValue(owner, out var source))
            appMenu?.Reposition(Anchor(source), dockTop());
        if (session.State is WindowPreviewState.Hidden or WindowPreviewState.Waiting || session.ApplicationId is not { } id) return;
        if (buttons.TryGetValue(id, out var button))
        {
            ToolTipService.SetToolTip(button, null);
            preview?.Refresh(Anchor(button), dockTop());
        }
    }

    private static DockAppMenuState MenuState(DockApplicationItem item) => DockAppMenuState.Create(item.Application,
        WindowsApplicationLauncher.Target(item.Application) is not null,
        WindowsApplicationLauncher.CanRunAsAdministrator(item.Application),
        WindowsApplicationLauncher.CanOpenFileLocation(item.Application));

    private IReadOnlyList<DockAppMenuEntry> BuildMenu(DockApplicationItem item, DockAppMenuState state,
        AppActionPanelModel model, Func<bool>? extract)
    {
        var entries = new List<DockAppMenuEntry>();
        void Add(string title, Func<bool> action)
        {
            entries.Add(new(title, Glyph(title), () =>
            {
                if (!action()) ActionFailed?.Invoke(this, $"Could not complete '{title}' for {item.Name}.");
            }));
        }
        if (model.RunningWindows.Count > 0)
        {
            entries.Add(new("Open Windows", "", IsHeading: true));
            foreach (var window in model.RunningWindows.Take(4))
                entries.Add(WindowEntry(window));
            if (model.RunningWindows.Count > 4)
                entries.Add(new("More windows", "\uE737", Children: model.RunningWindows.Skip(4).Select(WindowEntry).ToArray()));
            entries.Add(new("", ""));
        }
        else if (state.CanLaunch) Add("Open", () => applications.Activate(item));
        if (state.ShowNewWindow) Add("New window", () => applications.Launch(item));
        if (model.CanOpenFile) entries.Add(new("Open file…", "\uE8E5", () => PickFiles(item.Application)));

        if (item.IsRunning)
        {
            if (extract is null) Add("Show All Windows", () => { pendingShowAll = item; return true; });
            if (state.HasMultipleWindows)
            {
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

        if (state.ShowPin)
        {
            entries.Add(new("", ""));
            Add(model.PinAction, extract ?? (() => applications.SetPinned(item, !state.IsPinned)));
        }

        var canElevate = state.CanElevate;
        var canLocate = state.CanLocate;
        if (canElevate || canLocate)
        {
            entries.Add(new("", ""));
            if (canElevate) Add("Run as Administrator", () => applications.RunAsAdministrator(item));
            if (canLocate) Add("Open File Location", () => applications.OpenFileLocation(item));
        }
        return entries;
    }

    private DockAppMenuEntry WindowEntry(ApplicationWindow window) => new(
        string.IsNullOrWhiteSpace(window.Title) ? window.Name : window.Title, "\uE737", async () =>
        {
            var activated = applications.ActivateWindow(window) || await restoreElevated(window);
            if (!disposed && !activated) ActionFailed?.Invoke(this, "Windows could not focus that window; it may have closed.");
        });

    private static string Glyph(string title) => title switch
    {
        "Activate" or "Open" => "\uE737",
        "New window" => "\uE710",
        "Show All Windows" => "\uE8A7",
        "Close Window" or "Close All Windows" => "\uE711",
        "Pin to Dock" or "Unpin from Dock" or "Pin to Doky" or "Unpin from Doky" or "Move out of stack" => "\uE718",
        "Run as Administrator" => "\uEA18",
        _ => "\uE8B7"
    };

    public void Hide(bool immediate = false)
    {
        var held = HoldsDock;
        pending?.Cancel();
        if (session.ApplicationId is { } id && buttons.TryGetValue(id, out var button))
        {
            var item = applications.VisibleDockApplications.FirstOrDefault(item => item.Id == id);
            if (item is not null) ToolTipService.SetToolTip(button, CreateTooltip(item.Name));
        }
        session.Hide(); preview?.Hide(immediate);
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
            if (disposed || source is MenuFlyout { IsOpen: true } ||
                source is DockAppContextMenuWindow { IsOpen: true } || !contextMenus.Remove(source)) return;
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
        appMenu?.Close(); appMenu = null;
        contextMenus.Clear();
        menuSnapshots.Clear();
        applications.SnapshotApplied -= OnSnapshot;
        dockRoot.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(DockPointerPressed));
        settings.Changed -= SettingsChanged;
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
