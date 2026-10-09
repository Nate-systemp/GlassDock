using GlassDock.App.Rendering;
using GlassDock.App.ViewModels;
using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Applications;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace GlassDock.App.Desktop;

/// <summary>One retained Home HWND, dashboard, material and search index for the dock's lifetime.</summary>
internal sealed partial class GlassHomeWindow : Window
{
    private static readonly FontFamily UiFont = new("Segoe UI Variable Text");
    private readonly GlassDockSettingsSession settingsSession;
    private readonly DockApplicationsViewModel applications;
    private readonly Func<DockApplication, Task<bool>> activateApplication;
    private readonly Action showSettings;
    private readonly nint owner;
    private readonly nint hwnd;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly UtilityPopupTheme theme = new();
    private readonly Grid root = new() { UseLayoutRounding = true };
    private readonly Canvas dashboard = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly UISettings systemTheme = new();
    private readonly WindowsApplicationIndex applicationIndex = new();
    private readonly WindowsApplicationLauncher launcher = new();
    private readonly GlassSearchSelection selection = new();
    private readonly IReadOnlyList<GlassSearchResult> settings = WindowsSettingsCatalog.Entries.Select(e => e.ToSearchResult()).ToArray();
    private readonly TextBox search = new()
    {
        PlaceholderText = "Search apps and settings…", FontFamily = UiFont, FontSize = 17,
        BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        VerticalAlignment = VerticalAlignment.Center, UseSystemFocusVisuals = false
    };
    private readonly StackPanel resultsPanel = new() { Spacing = 5, Visibility = Visibility.Collapsed };
    private readonly ListView resultsList = new()
    {
        SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, IsTabStop = false,
        SingleSelectionFollowsFocus = false, BorderThickness = new Thickness(0), Padding = new Thickness(0),
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
    };
    private readonly TextBlock searchStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherQueueTimer statusTimer;
    private readonly DispatcherQueueTimer focusRetry;
    private HomeDashboardLayout layout = HomeDashboardLayout.Create(960, 660);
    private HomeMonitorArea area = new(0, 0, 960, 660, 1);
    private bool closed, launching, active;
    private DockDisplayMode lastDisplayMode;
    private int visibilityRevision, refreshQueued, focusAttempts;
    private string? launchError;
    private string renderedResultsKey = string.Empty;
    private bool HasQuery => !string.IsNullOrWhiteSpace(search.Text);
    public bool IsVisible { get; private set; }
    public event EventHandler? HomeVisibilityChanged;

    public GlassHomeWindow(GlassDockSettingsSession settingsSession, WindowsApplicationService applicationService,
        WindowsSystemControlService systemControls, nint owner, Action showSettings,
        Func<DockApplication, Task<bool>> activateApplication)
    {
        this.settingsSession = settingsSession;
        lastDisplayMode = settingsSession.DisplayMode;
        applications = new DockApplicationsViewModel(applicationService, DispatcherQueue, ownsService: false);
        this.systemControls = systemControls;
        this.owner = owner;
        this.showSettings = showSettings;
        this.activateApplication = activateApplication;
        Title = "Doky Home";
        WindowBranding.Apply(this);
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        // Configure the desktop client BEFORE connecting the backdrop, as on utilities.
        host = new(hwnd) { UseDockLayeredTransparency = true };
        host.Configure();
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        BuildDashboard();
        root.Children.Add(dashboard);
        Content = root;
        ApplyAppearance();
        if ((Application.Current as App)?.BasicRendering != true) SystemBackdrop = backdrop;
        statusTimer = DispatcherQueue.CreateTimer();
        statusTimer.Interval = TimeSpan.FromSeconds(3);
        statusTimer.Tick += StatusTick;
        focusRetry = DispatcherQueue.CreateTimer();
        focusRetry.Interval = TimeSpan.FromMilliseconds(20);
        focusRetry.Tick += FocusTick;
        search.TextChanged += OnQueryChanged;
        search.PreviewKeyDown += OnSearchKeyDown;
        resultsList.ItemClick += ResultClicked;
        applicationIndex.Changed += OnIndexChanged;
        applications.SnapshotApplied += ApplicationsChanged;
        applicationService.RequestRefresh();
        settingsSession.Changed += SettingsChanged;
        systemTheme.ColorValuesChanged += OnSystemThemeChanged;
        root.KeyDown += RootKeyDown;
        root.Loaded += RootLoaded;
        AppWindow.Changed += WindowChanged;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    public void Toggle()
    {
        if (closed) return;
        if (IsVisible) { HideHome(); return; }
        visibilityRevision++;
        IsVisible = true;
        search.Text = "";
        ConfigurePlacement(resolveMonitor: true);
        RefreshApplications();
        RefreshResults(false);
        AppWindow.Show();
        Activate();
        host.ActivateForUserInput();
        focusAttempts = 0;
        focusRetry.Start();
        statusTimer.Start();
        _ = RefreshControlsAsync();
        HomeVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    public void HideHome()
    {
        if (closed || !IsVisible) return;
        IsVisible = false;
        visibilityRevision++;
        statusTimer.Stop();
        focusRetry.Stop();
        controlCancellation?.Cancel();
        AppWindow.Hide();
        HomeVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ConfigurePlacement(bool resolveMonitor)
    {
        if (resolveMonitor) area = HomeDesktopEnvironment.Resolve(settingsSession.DisplayMode, owner, settingsSession.Current.BottomMargin);
        layout = HomeDashboardLayout.Create(area.Width / area.Scale, area.Height / area.Scale, settingsSession.Current.IconSpacing);
        root.Width = layout.Width; root.Height = layout.Height;
        dashboard.Width = layout.Width / layout.ContentScale;
        dashboard.Height = layout.Height / layout.ContentScale;
        dashboard.HorizontalAlignment = HorizontalAlignment.Left;
        dashboard.VerticalAlignment = VerticalAlignment.Top;
        dashboard.RenderTransform = new ScaleTransform { ScaleX = layout.ContentScale, ScaleY = layout.ContentScale };
        LayoutCards();
        var w = (int)Math.Round(layout.Width * area.Scale);
        var h = (int)Math.Round(layout.Height * area.Scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + area.Height - h, w, h));
        UpdateMaterialBounds();
    }

    private void UpdateMaterialBounds()
    {
        var rects = cards.Where(c => c.Host.Visibility == Visibility.Visible).Select(c =>
            new HomeCardRect(Canvas.GetLeft(c.Host) * layout.ContentScale, Canvas.GetTop(c.Host) * layout.ContentScale,
                c.Host.Width * layout.ContentScale, c.Host.Height * layout.ContentScale));
        backdrop.SetSurfaceRegions(rects, layout.ContentScale);
        backdrop.SetBounds(layout.Width, layout.Height, layout.Width, layout.Height, 0,
            root.XamlRoot?.RasterizationScale ?? area.Scale);
    }

    private void SettingsChanged(object? sender, GlassDockSettingsChangedEventArgs e)
    {
        if (closed) return;
        var displayChanged = lastDisplayMode != e.Settings.DockDisplayMode;
        lastDisplayMode = e.Settings.DockDisplayMode;
        // Recolor retained brushes/surfaces, never create another window or UI tree.
        ApplyAppearance();
        if (IsVisible) ConfigurePlacement(resolveMonitor: displayChanged);
    }
    private void ApplicationsChanged(object? sender, EventArgs e) { if (IsVisible && !closed) RefreshApplications(); }
    private void StatusTick(DispatcherQueueTimer sender, object e) => _ = RefreshControlsAsync();
    private void RootLoaded(object sender, RoutedEventArgs e) => UpdateMaterialBounds();
    private void WindowChanged(AppWindow sender, AppWindowChangedEventArgs e)
    {
        if (!closed && IsVisible && root.XamlRoot is not null) UpdateMaterialBounds();
    }
    private void RootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!dialogOpen && e.Key == global::Windows.System.VirtualKey.Escape) { HideHome(); e.Handled = true; }
    }
    private void OnActivated(object sender, WindowActivatedEventArgs e) => active = e.WindowActivationState != WindowActivationState.Deactivated;
    private void FocusTick(DispatcherQueueTimer sender, object e)
    {
        if (!IsVisible || closed || ++focusAttempts > 12) { sender.Stop(); return; }
        if (active && search.Focus(FocusState.Keyboard)) sender.Stop();
    }
    private void ResultClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ListViewItem { Tag: GlassSearchResult result }) _ = LaunchResultAsync(result);
    }
    private void OnClosed(object sender, WindowEventArgs e)
    {
        closed = true; IsVisible = false; visibilityRevision++;
        statusTimer.Stop(); focusRetry.Stop();
        statusTimer.Tick -= StatusTick; focusRetry.Tick -= FocusTick;
        controlCancellation?.Cancel(); controlCancellation?.Dispose();
        settingsSession.Changed -= SettingsChanged;
        systemTheme.ColorValuesChanged -= OnSystemThemeChanged;
        applications.SnapshotApplied -= ApplicationsChanged;
        applications.Dispose();
        applicationIndex.Changed -= OnIndexChanged;
        search.TextChanged -= OnQueryChanged; search.PreviewKeyDown -= OnSearchKeyDown;
        resultsList.ItemClick -= ResultClicked;
        root.KeyDown -= RootKeyDown; root.Loaded -= RootLoaded;
        AppWindow.Changed -= WindowChanged; Activated -= OnActivated;
        applicationIndex.Dispose();
        SystemBackdrop = null;
        host.Dispose();
    }

    private void OnSystemThemeChanged(UISettings sender, object args)
    {
        if (closed) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!closed) ApplyAppearance();
        });
    }
}
