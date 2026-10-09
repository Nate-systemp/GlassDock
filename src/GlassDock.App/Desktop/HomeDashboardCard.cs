using GlassDock.App.Controls;
using GlassDock.Core.Applications;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

/// <summary>A retained card: one shadow surface and one content host; desktop pixels come from Home's shared backdrop.</summary>
internal sealed class HomeDashboardCard
{
    public Grid Host { get; } = new();
    public GlassSurface Surface { get; } = new() { UseDesktopBackdrop = true, IsHitTestVisible = false };
    public Grid Content { get; } = new() { Padding = new Thickness(18, 14, 18, 14), MinWidth = 0, MinHeight = 0 };
    private readonly Border solid;
    public HomeDashboardCard(UtilityPopupTheme theme)
    {
        solid = new() { Background = theme.ReadingSurface, CornerRadius = new(28), IsHitTestVisible = false };
        Host.Children.Add(Surface);
        Host.Children.Add(solid);
        Host.Children.Add(Content);
    }
    public void Apply(GlassMaterial material)
    {
        Surface.Apply(material with { BorderOpacity = 0, EdgeHighlight = 0 });
        solid.CornerRadius = new(material.CornerRadius);
    }
}

/// <summary>Six retained slots, paged from the dock's canonical snapshot. No second pin database.</summary>
internal sealed class HomeAppStrip
{
    private readonly Button[] buttons = new Button[6];
    private readonly AdaptiveAppIcon[] icons = new AdaptiveAppIcon[6];
    private readonly TextBlock[] labels = new TextBlock[6];
    private readonly Border[] indicators = new Border[6];
    private readonly ApplicationIcon?[] renderedIcons = new ApplicationIcon?[6];
    private readonly Grid row = new() { ColumnSpacing = 8 };
    private readonly TextBlock empty;
    private readonly TextBlock pageLabel;
    private readonly Button previous, next;
    private IReadOnlyList<DockApplication> apps = [];
    private int page, capacity = 6;
    public Grid Content { get; } = new() { RowSpacing = 12 };

    public HomeAppStrip(string title, UtilityPopupTheme theme, Func<DockApplication, Task> activate, bool compact = false)
    {
        Content.RowDefinitions.Add(new() { Height = new GridLength(26) });
        Content.RowDefinitions.Add(new());
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new());
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = theme.Primary });
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right };
        pageLabel = new() { FontSize = 11, Foreground = theme.Muted, VerticalAlignment = VerticalAlignment.Center };
        previous = new() { Content = new FontIcon { Glyph = "\uE76B", FontSize = 10 }, Width = 24, Height = 24, Padding = new(0), Background = theme.Tile, BorderThickness = new(0) };
        next = new() { Content = new FontIcon { Glyph = "\uE76C", FontSize = 10 }, Width = 24, Height = 24, Padding = new(0), Background = theme.Tile, BorderThickness = new(0) };
        theme.StyleButton(previous); theme.StyleButton(next);
        AutomationProperties.SetName(previous, "Previous " + title); AutomationProperties.SetName(next, "Next " + title);
        previous.Click += (_, _) => { page = Math.Max(0, page - 1); Render(); };
        next.Click += (_, _) => { page++; Render(); };
        pager.Children.Add(pageLabel); pager.Children.Add(previous); pager.Children.Add(next);
        Grid.SetColumn(pager, 1); header.Children.Add(pager);
        Content.Children.Add(header);
        Grid.SetRow(row, 1); Content.Children.Add(row);
        empty = new() { Foreground = theme.Secondary, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(empty, 1); Content.Children.Add(empty);
        for (var i = 0; i < 6; i++)
        {
            row.ColumnDefinitions.Add(new());
            var column = new Grid { RowSpacing = compact ? 4 : 8, VerticalAlignment = VerticalAlignment.Center };
            column.RowDefinitions.Add(new() { Height = new GridLength(40) });
            column.RowDefinitions.Add(new() { Height = new GridLength(compact ? 16 : 30) });
            column.RowDefinitions.Add(new() { Height = new GridLength(4) });
            icons[i] = new(40, 1) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            labels[i] = new() { FontSize = 11, LineHeight = 14, MaxLines = compact ? 1 : 2,
                TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap,
                Foreground = theme.Secondary, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            indicators[i] = new() { Width = 5, Height = 3, CornerRadius = new(2), Background = theme.Primary, HorizontalAlignment = HorizontalAlignment.Center };
            column.Children.Add(icons[i]); Grid.SetRow(labels[i], 1); column.Children.Add(labels[i]); Grid.SetRow(indicators[i], 2); column.Children.Add(indicators[i]);
            var button = buttons[i] = new() { Content = column, Padding = new(3), HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0) };
            theme.StyleButton(button);
            button.Click += async (_, _) => { if (button.Tag is DockApplication app) await activate(app); };
            Grid.SetColumn(button, i); row.Children.Add(button);
        }
    }
    public void SetApps(IReadOnlyList<DockApplication> source) { apps = source; Render(); }
    public void SetWidth(double width) { capacity = Math.Clamp((int)(width / 65), 3, 6); Render(); }
    private void Render()
    {
        var pages = Math.Max(1, (apps.Count + capacity - 1) / capacity);
        page = Math.Clamp(page, 0, pages - 1);
        pageLabel.Text = pages > 1 ? $"{page + 1}/{pages}" : "";
        previous.Visibility = next.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
        previous.IsEnabled = page > 0; next.IsEnabled = page < pages - 1;
        empty.Text = "No applications yet";
        empty.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var i = 0; i < 6; i++)
        {
            row.ColumnDefinitions[i].Width = i < capacity ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            var app = i < capacity ? apps.ElementAtOrDefault(page * capacity + i) : null;
            buttons[i].Tag = app; buttons[i].Visibility = app is null ? Visibility.Collapsed : Visibility.Visible;
            if (app is null) continue;
            labels[i].Text = app.Name;
            AutomationProperties.SetName(buttons[i], app.Name);
            ToolTipService.SetToolTip(buttons[i], app.Name);
            if (!ReferenceEquals(renderedIcons[i], app.Icon)) { renderedIcons[i] = app.Icon; icons[i].SetIcon(app.Icon); }
            indicators[i].Opacity = app.IsRunning ? (app.IsActive ? 1 : .45) : 0;
        }
    }
}
