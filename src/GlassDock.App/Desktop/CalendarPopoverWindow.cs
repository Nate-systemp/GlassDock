using System.Globalization;
using GlassDock.Core.Settings;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

/// <summary>
/// Doky's clock/calendar utility. This intentionally owns its small month grid
/// instead of embedding the system CalendarView, so all five Doky appearances
/// share the same spacing, typography and interaction geometry.
/// </summary>
internal sealed class CalendarPopoverWindow : Window
{
    private const double PanelWidth = 554;
    private const double PanelHeight = 374;
    private const double Gutter = UtilityPopupStyle.Gutter;

    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly UtilityPopupTheme theme = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new();
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly TextBlock weekday = new();
    private readonly TextBlock clock = new();
    private readonly TextBlock period = new();
    private readonly TextBlock fullDate = new();
    private readonly TextBlock selectionStatus = new();
    private readonly TextBlock monthLabel = new();
    private readonly Grid weekdayGrid = new() { ColumnSpacing = 0 };
    private readonly Grid dateGrid = new() { ColumnSpacing = 0, RowSpacing = 1 };
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer timer;
    private readonly UtilityPopupPresentation presentation;
    private DateTime visibleMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime selectedDate = DateTime.Today;
    private DateTime lastObservedDate = DateTime.Today;
    private bool closed;

    public CalendarPopoverWindow(DockAppearanceSettings appearance,
        DockAppearanceMode dockMode, Func<bool>? utilityOwnsPointer = null)
    {
        Title = "Doky Calendar";
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

        var layout = new Grid
        {
            Margin = new Thickness(Gutter + 19, Gutter + 17, Gutter + 19, Gutter + 17),
            ColumnSpacing = 17
        };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new Grid { RowSpacing = 9 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new StackPanel { Spacing = 8, Margin = new Thickness(4, 18, 0, 0) };
        weekday.FontFamily = new FontFamily("Segoe UI Variable Display");
        weekday.FontSize = 11.5;
        weekday.CharacterSpacing = 180;
        weekday.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        weekday.Foreground = theme.Secondary;
        heading.Children.Add(weekday);

        var clockLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        clock.FontFamily = new FontFamily("Segoe UI Variable Display");
        clock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        clock.FontSize = 52;
        clock.Foreground = theme.Primary;
        period.FontSize = 15;
        period.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        period.Foreground = theme.Accent;
        period.VerticalAlignment = VerticalAlignment.Bottom;
        period.Margin = new Thickness(0, 0, 0, 13);
        clockLine.Children.Add(clock);
        clockLine.Children.Add(period);
        heading.Children.Add(clockLine);

        fullDate.FontSize = 12;
        fullDate.TextWrapping = TextWrapping.Wrap;
        fullDate.Foreground = theme.Secondary;
        heading.Children.Add(fullDate);
        left.Children.Add(heading);

        var note = new Border
        {
            Padding = new Thickness(11, 11, 9, 11),
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(14)
        };
        var noteContent = new StackPanel { Spacing = 6 };
        noteContent.Children.Add(new TextBlock
        {
            Text = "TODAY",
            FontSize = 10,
            CharacterSpacing = 110,
            Foreground = theme.Accent,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        selectionStatus.FontSize = 12;
        selectionStatus.Foreground = theme.Secondary;
        selectionStatus.TextWrapping = TextWrapping.Wrap;
        noteContent.Children.Add(selectionStatus);
        note.Child = noteContent;
        Grid.SetRow(note, 2);
        left.Children.Add(note);
        layout.Children.Add(left);

        var calendarLayout = new Grid
        {
            Padding = new Thickness(13, 11, 13, 11),
            RowSpacing = 7
        };
        calendarLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(31) });
        calendarLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(23) });
        calendarLayout.RowDefinitions.Add(new RowDefinition());

        var monthHeader = new Grid();
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition());
        monthHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        monthLabel.FontFamily = new FontFamily("Segoe UI Variable Display");
        monthLabel.FontSize = 15;
        monthLabel.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        monthLabel.Foreground = theme.Primary;
        monthLabel.VerticalAlignment = VerticalAlignment.Center;
        monthHeader.Children.Add(monthLabel);

        var monthActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center
        };
        monthActions.Children.Add(MonthButton("\uE76B", "Previous month", -1));
        monthActions.Children.Add(MonthButton("\uE76C", "Next month", 1));
        Grid.SetColumn(monthActions, 1);
        monthHeader.Children.Add(monthActions);
        calendarLayout.Children.Add(monthHeader);

        for (var i = 0; i < 7; i++)
        {
            weekdayGrid.ColumnDefinitions.Add(new ColumnDefinition());
            dateGrid.ColumnDefinitions.Add(new ColumnDefinition());
        }
        for (var i = 0; i < 6; i++)
            dateGrid.RowDefinitions.Add(new RowDefinition());
        BuildWeekdayHeader();
        Grid.SetRow(weekdayGrid, 1);
        calendarLayout.Children.Add(weekdayGrid);
        Grid.SetRow(dateGrid, 2);
        calendarLayout.Children.Add(dateGrid);

        var calendarCard = new Border
        {
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(17),
            Child = calendarLayout
        };
        Grid.SetColumn(calendarCard, 1);
        layout.Children.Add(calendarCard);
        root.Children.Add(layout);

        Content = root;
        presentation = new UtilityPopupPresentation(this, root, backdrop, utilityOwnsPointer);
        host = new InteractiveGlassWindowHost(WinRT.Interop.WindowNative.GetWindowHandle(this))
        {
            EnableHostBackdropBrush = true,
            UseDockLayeredTransparency = true
        };
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        // Attach only after Content and the native desktop client exist.
        // OnTargetConnected needs this root to schedule compositor creation.
        SystemBackdrop = backdrop;

        ApplyAppearance(appearance, dockMode);
        BuildCalendarGrid();
        root.SizeChanged += (_, _) => presentation.UpdateBackdropBounds();
        root.Loaded += (_, _) => { presentation.UpdateBackdropBounds(); Refresh(); };
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
        presentation.Hidden += (_, _) => timer.Stop();
        Closed += (_, _) => { closed = true; timer.Stop(); host.Dispose(); };
    }

    public void Refresh()
    {
        if (closed || !presentation.IsVisible) return;
        var now = DateTime.Now;
        weekday.Text = now.ToString("dddd", CultureInfo.CurrentCulture).ToUpperInvariant();
        clock.Text = now.ToString("h:mm", CultureInfo.CurrentCulture);
        period.Text = now.ToString("tt", CultureInfo.CurrentCulture);
        fullDate.Text = now.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture);
        if (now.Date != lastObservedDate)
        {
            var wasFollowingToday = selectedDate.Date == lastObservedDate;
            lastObservedDate = now.Date;
            if (wasFollowingToday)
            {
                selectedDate = now.Date;
                visibleMonth = new DateTime(now.Year, now.Month, 1);
            }
            BuildCalendarGrid();
        }
        RefreshSelection();
    }

    private void BuildWeekdayHeader()
    {
        weekdayGrid.Children.Clear();
        var dateFormat = CultureInfo.CurrentCulture.DateTimeFormat;
        var start = (int)dateFormat.FirstDayOfWeek;
        for (var column = 0; column < 7; column++)
        {
            var dayOfWeek = (DayOfWeek)((start + column) % 7);
            var name = dateFormat.AbbreviatedDayNames[(int)dayOfWeek];
            if (name.Length > 2) name = name[..2];
            var label = new TextBlock
            {
                Text = name,
                FontSize = 10.5,
                Foreground = theme.Muted,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, column);
            weekdayGrid.Children.Add(label);
        }
    }

    private void BuildCalendarGrid()
    {
        monthLabel.Text = visibleMonth.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        dateGrid.Children.Clear();
        var startDay = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var offset = ((int)visibleMonth.DayOfWeek - (int)startDay + 7) % 7;
        var firstCell = visibleMonth.AddDays(-offset);
        var today = DateTime.Today;
        for (var index = 0; index < 42; index++)
        {
            var date = firstCell.AddDays(index);
            var currentMonth = date.Month == visibleMonth.Month && date.Year == visibleMonth.Year;
            var selected = date.Date == selectedDate.Date;
            var todayCell = date.Date == today;
            var label = new TextBlock
            {
                Text = date.Day.ToString(CultureInfo.CurrentCulture),
                FontSize = 12,
                FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = currentMonth ? (selected ? theme.AccentText : theme.Primary) : theme.Muted,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var button = new Button
            {
                Content = label,
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Background = selected ? theme.AccentFill : TransparentBrush(),
                BorderBrush = todayCell && !selected ? theme.Accent : TransparentBrush(),
                BorderThickness = todayCell && !selected ? new Thickness(1) : new Thickness(0),
                CornerRadius = new CornerRadius(16),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            button.Resources["ButtonBackgroundPointerOver"] = selected ? theme.AccentFill : theme.Hover;
            button.Resources["ButtonBackgroundPressed"] = theme.AccentFill;
            AutomationProperties.SetName(button, date.ToString("D", CultureInfo.CurrentCulture));
            var captured = date;
            button.Click += (_, _) =>
            {
                selectedDate = captured.Date;
                if (captured.Month != visibleMonth.Month || captured.Year != visibleMonth.Year)
                    visibleMonth = new DateTime(captured.Year, captured.Month, 1);
                BuildCalendarGrid();
                RefreshSelection();
            };
            Grid.SetColumn(button, index % 7);
            Grid.SetRow(button, index / 7);
            dateGrid.Children.Add(button);
        }
        RefreshSelection();
    }

    private Button MonthButton(string glyph, string name, int direction)
    {
        var icon = new FontIcon { Glyph = glyph, FontSize = 12, Foreground = theme.Primary };
        var button = new Button
        {
            Content = icon,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = theme.Tile,
            BorderBrush = theme.TileBorder,
            BorderThickness = new Thickness(.7),
            CornerRadius = new CornerRadius(DockControlPalette.ButtonRadius)
        };
        theme.StyleButton(button);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            visibleMonth = visibleMonth.AddMonths(direction);
            BuildCalendarGrid();
        };
        return button;
    }

    private void RefreshSelection()
    {
        selectionStatus.Text = selectedDate.Date == DateTime.Today
            ? "Today is highlighted"
            : "Selected: " + selectedDate.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
    }

    public void ApplyAppearance(DockAppearanceSettings appearance, DockAppearanceMode mode)
    {
        theme.Apply(mode);
        root.RequestedTheme = mode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        UtilityPopupStyle.Apply(glass, backdrop, appearance, mode);
    }

    public event EventHandler? Dismissed
    {
        add => presentation.Dismissed += value;
        remove => presentation.Dismissed -= value;
    }
    public event EventHandler? Hidden { add => presentation.Hidden += value; remove => presentation.Hidden -= value; }
    public void HideImmediately() => presentation.HideImmediately();
    public void RetargetClosed() => presentation.RetargetClosed();
    public void Present() { presentation.Present(); timer.Start(); Refresh(); }
    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(AppWindow owner, double scale, double anchorX,
        double anchorY, double dockTop)
    {
        UtilityPopupStyle.Position(AppWindow, owner, scale, anchorX, dockTop, PanelWidth, PanelHeight);
        presentation.SetTargetWindowGeometry(
            owner.Position.X + anchorX * scale,
            owner.Position.Y + anchorY * scale,
            scale);
        host.InputHeightPixels = presentation.InputHeightPixels;
    }

    private static SolidColorBrush TransparentBrush() =>
        new(Microsoft.UI.Colors.Transparent);
}
