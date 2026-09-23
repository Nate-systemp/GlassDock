using GlassDock.Core.Settings;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

internal sealed class CalendarPopoverWindow : Window
{
    private const double PanelWidth = 390;
    private const double PanelHeight = 432;
    private const double Gutter = UtilityPopupStyle.Gutter;

    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new() { Background = Brush(0) };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly TextBlock day = new();
    private readonly TextBlock time = new();
    private readonly CalendarView calendar = new()
    {
        SelectionMode = CalendarViewSelectionMode.Single,
        IsTodayHighlighted = true,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Background = Brush(0),
        Foreground = Brush(240),
        BorderThickness = new Thickness(0),
        CalendarItemBackground = Brush(0),
        CalendarItemForeground = Brush(235),
        CalendarItemBorderBrush = Brush(0),
        CalendarItemHoverBackground = Brush(22),
        TodayBackground = Brush(90, 55, 145, 230),
        TodayForeground = Brush(255),
        DayItemFontSize = 13,
        FirstOfMonthLabelFontSize = 12
    };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly UtilityPopupPresentation presentation;
    private bool closed;

    public CalendarPopoverWindow(DockAppearanceSettings appearance)
    {
        Title = "GlassDock Calendar";
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;

        UtilityPopupStyle.Apply(glass, backdrop, appearance);
        SystemBackdrop = backdrop;
        root.Children.Add(glass);

        var panel = new Grid
        {
            Margin = new Thickness(Gutter + 22, Gutter + 18, Gutter + 22, Gutter + 18),
            RowSpacing = 12
        };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerText = new StackPanel { Spacing = 1 };
        day.FontFamily = new FontFamily("Segoe UI Variable Display");
        day.FontSize = 20;
        day.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        day.Foreground = Brush(245);
        headerText.Children.Add(day);
        var subtitle = new TextBlock
        {
            Text = "Calendar",
            FontSize = 11.5,
            Foreground = Brush(160)
        };
        headerText.Children.Add(subtitle);
        header.Children.Add(headerText);

        time.FontFamily = new FontFamily("Segoe UI Variable Display");
        time.FontSize = 17;
        time.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        time.Foreground = Brush(232);
        time.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(time, 1);
        header.Children.Add(time);
        panel.Children.Add(header);

        var calendarCard = new Border
        {
            Background = Brush(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(0),
            Child = calendar
        };
        Grid.SetRow(calendarCard, 1);
        panel.Children.Add(calendarCard);

        var footer = new TextBlock
        {
            Text = "Today is highlighted · Esc closes",
            FontSize = 11,
            Foreground = Brush(145),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(footer, 2);
        panel.Children.Add(footer);

        root.Children.Add(panel);
        Content = root;
        presentation = new UtilityPopupPresentation(this, root, backdrop);

        host = new InteractiveGlassWindowHost(WinRT.Interop.WindowNative.GetWindowHandle(this));
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }

        root.SizeChanged += (_, _) => UpdateBackdrop();
        root.Loaded += (_, _) => { UpdateBackdrop(); Refresh();  };
        root.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Dismiss();
            }
        };

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => Refresh();
        Activated += (_, _) => { if (!closed) timer.Start(); };
        Closed += (_, _) => { closed = true; timer.Stop(); host.Dispose(); };
    }

    public void Refresh()
    {
        if (closed) return;
        var now = DateTimeOffset.Now;
        day.Text = now.ToString("dddd, MMMM d");
        time.Text = now.ToString("h:mm tt");
        if (calendar.SelectedDates.Count == 0)
            calendar.SelectedDates.Add(now);
    }

    public void ApplyAppearance(DockAppearanceSettings appearance) => UtilityPopupStyle.Apply(glass, backdrop, appearance);

    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(AppWindow owner, double scale, double anchorX, double dockTop)
    {
        UtilityPopupStyle.Position(AppWindow, owner, scale, anchorX, dockTop, PanelWidth, PanelHeight);
        presentation.AnchorX = (owner.Position.X + anchorX * scale - AppWindow.Position.X) / scale;
    }


    private void UpdateBackdrop() => backdrop.SetBounds(
        root.ActualWidth,
        root.ActualHeight,
        Math.Max(0, root.ActualWidth - Gutter * 2),
        Math.Max(0, root.ActualHeight - Gutter * 2),
        Gutter,
        root.XamlRoot?.RasterizationScale ?? 1);

    private static SolidColorBrush Brush(byte alpha, byte r = 255, byte g = 255, byte b = 255) =>
        new(global::Windows.UI.Color.FromArgb(alpha, r, g, b));
}
