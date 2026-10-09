using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal sealed partial class GlassHomeWindow
{
    private readonly List<HomeDashboardCard> cards = [];
    private HomeAppStrip pinnedStrip = null!, runningStrip = null!;
    private TextBlock actionStatus = null!;

    private HomeDashboardCard Card()
    {
        var card = new HomeDashboardCard(theme);
        cards.Add(card); dashboard.Children.Add(card.Host);
        return card;
    }
    private TextBlock Label(string text, double size = 13, bool secondary = false) => new()
    {
        Text = text, FontFamily = UiFont, FontSize = size,
        Foreground = secondary ? theme.Secondary : theme.Primary,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
    private Button ActionButton(string text, string glyph, Action action)
    {
        var row = new Grid { ColumnSpacing = 9 };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 17 });
        if (text.Length > 0)
        {
            row.ColumnDefinitions.Add(new());
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var title = Label(text, 12); title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(title, 1); row.Children.Add(title);
            var arrow = new FontIcon { Glyph = "\uE76C", FontSize = 10, Foreground = theme.Secondary };
            Grid.SetColumn(arrow, 2); row.Children.Add(arrow);
        }
        var button = new Button { Content = row, Background = theme.Tile, BorderBrush = theme.TileBorder,
            BorderThickness = new(.6), Padding = new(10, 7, 10, 7), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = text.Length > 0 ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch, MinWidth = 0, MinHeight = 0 };
        theme.StyleButton(button);
        button.Click += (_, _) => action();
        AutomationProperties.SetName(button, text);
        return button;
    }
    private void BuildDashboard()
    {
        var searchCard = Card();
        searchCard.Content.Padding = new(18, 8, 18, 8);
        var searchRow = new Grid { ColumnSpacing = 12 };
        searchRow.ColumnDefinitions.Add(new() { Width = new GridLength(24) });
        searchRow.ColumnDefinitions.Add(new()); searchRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        searchRow.Children.Add(new FontIcon { Glyph = "\uE721", FontSize = 22, Foreground = theme.Secondary });
        search.Foreground = theme.Primary;
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused",
            "TextControlBorderBrush", "TextControlBorderBrushFocused", "TextControlBorderBrushPointerOver", "TextControlElevationBorderBrush" })
            search.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        search.Resources["TextControlPlaceholderForeground"] = theme.Secondary;
        search.Resources["TextControlPlaceholderForegroundFocused"] = theme.Secondary;
        search.Resources["TextControlPlaceholderForegroundPointerOver"] = theme.Secondary;
        search.Resources["TextControlForegroundFocused"] = theme.Primary;
        AutomationProperties.SetName(search, "Search apps and Windows settings");
        Grid.SetColumn(search, 1); searchRow.Children.Add(search);
        var hint = Label("Win + Space", 11, true); hint.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hint, 2); searchRow.Children.Add(hint);
        searchCard.Content.Children.Add(searchRow);

        pinnedStrip = new("Pinned Apps", theme, ActivateAppAsync);
        Card().Content.Children.Add(pinnedStrip.Content);
        runningStrip = new("Running Apps", theme, ActivateAppAsync, compact: true);
        Card().Content.Children.Add(runningStrip.Content);

        var recent = Card();
        var recentLayout = new Grid { RowSpacing = 14 };
        recentLayout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        recentLayout.RowDefinitions.Add(new());
        recentLayout.Children.Add(Label("Continue", 15));
        var recentEmpty = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        recentEmpty.Children.Add(new FontIcon { Glyph = "\uE823", FontSize = 22, Foreground = theme.Muted });
        recentEmpty.Children.Add(Label("No recent items yet", 14, true));
        Grid.SetRow(recentEmpty, 1); recentLayout.Children.Add(recentEmpty); recent.Content.Children.Add(recentLayout);

        Card().Content.Children.Add(CreateQuickControls());
        var media = Card();
        var mediaLayout = new Grid { RowSpacing = 10 };
        mediaLayout.RowDefinitions.Add(new()); mediaLayout.RowDefinitions.Add(new() { Height = new GridLength(32) });
        var track = new Grid { ColumnSpacing = 14 };
        track.ColumnDefinitions.Add(new() { Width = new GridLength(64) }); track.ColumnDefinitions.Add(new());
        track.Children.Add(new Border { CornerRadius = new(14), Background = theme.Tile, Child = new FontIcon { Glyph = "\uE8D6", FontSize = 28, Foreground = theme.Muted } });
        var metadata = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(Label("Nothing playing", 16)); metadata.Children.Add(Label("Media controls unavailable", 11, true));
        Grid.SetColumn(metadata, 1); track.Children.Add(metadata); mediaLayout.Children.Add(track);
        var transport = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var (glyph, name) in new[] { ("\uE892", "Previous track"), ("\uE768", "Play"), ("\uE893", "Next track") })
        {
            var button = ActionButton("", glyph, () => { }); button.IsEnabled = false;
            button.Resources["ButtonForegroundDisabled"] = theme.Muted;
            button.Resources["ButtonBackgroundDisabled"] = theme.Tile;
            AutomationProperties.SetName(button, name + ", unavailable"); transport.Children.Add(button);
        }
        Grid.SetRow(transport, 1); mediaLayout.Children.Add(transport); media.Content.Children.Add(mediaLayout);
        Card().Content.Children.Add(CreateLowerControls());
        var resultCard = Card(); resultCard.Host.Visibility = Visibility.Collapsed;
        searchStatus.Foreground = theme.Secondary;
        resultsList.Resources["ListViewItemBackgroundSelected"] = theme.Pressed;
        resultsList.Resources["ListViewItemBackgroundSelectedPointerOver"] = theme.Pressed;
        resultsList.Resources["ListViewItemBackgroundPointerOver"] = theme.Hover;
        resultsPanel.Children.Add(resultsList); resultsPanel.Children.Add(searchStatus);
        resultCard.Content.Children.Add(resultsPanel);
        LayoutCards();
    }

    private void ApplyAppearance()
    {
        var selectedMode = settingsSession.Current.DockAppearanceMode;
        // Preserve the selected material: Dark/Light are solid, only the three
        // explicit glass modes may sample the desktop behind Home.
        var materialMode = selectedMode;
        var paletteMode = selectedMode == DockAppearanceMode.Light ? DockAppearanceMode.Light :
            selectedMode == DockAppearanceMode.Dark ? DockAppearanceMode.Dark :
            WindowsSystemTheme.IsDark() ? DockAppearanceMode.Dark : DockAppearanceMode.Light;
        theme.Apply(paletteMode);
        theme.ApplyReadingContrast(settingsSession.Current.DockOpacity, materialMode);
        root.RequestedTheme = paletteMode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        var material = UtilityMaterial.CreateForPopup(settingsSession.Appearance, materialMode);
        // Initialize the disjoint mask before Clear is applied: no full-window specular layer.
        // Reuse the current, already pixel-aligned card bounds during live changes.
        UpdateMaterialBounds();
        UtilityPopupStyle.Apply(cards[0].Surface, backdrop, settingsSession.Appearance, materialMode);
        foreach (var card in cards) card.Apply(UtilityMaterial.ForDashboardCard(material, materialMode));
        if ((Application.Current as App)?.BasicRendering == true)
            foreach (var card in cards) card.Surface.SetDesktopFallback(true);
        UpdateMaterialBounds();
    }

    private void LayoutCards()
    {
        for (var i = 0; i < 7; i++) Place(cards[i].Host, layout.Cards[i]);
        var first = layout.Cards[1]; var last = layout.Cards[6];
        Place(cards[7].Host, new(first.X, first.Y, layout.Cards[0].Width, last.Y + last.Height - first.Y));
        pinnedStrip.SetWidth(first.Width - 36); runningStrip.SetWidth(first.Width - 36);
        resultsList.MaxHeight = Math.Max(50, cards[7].Host.Height - 80);
        UpdateDashboardVisibility();
    }
    private void Place(FrameworkElement element, HomeCardRect rect)
    {
        var pixelsPerUnit = Math.Max(.01, area.Scale * layout.ContentScale);
        double Align(double value) => Math.Round(value * pixelsPerUnit) / pixelsPerUnit;
        var left = Align(rect.X); var top = Align(rect.Y);
        Canvas.SetLeft(element, left); Canvas.SetTop(element, top);
        element.Width = Align(rect.X + rect.Width) - left;
        element.Height = Align(rect.Y + rect.Height) - top;
    }
    private void UpdateDashboardVisibility()
    {
        for (var i = 1; i < 7; i++) cards[i].Host.Visibility = HasQuery ? Visibility.Collapsed : Visibility.Visible;
        cards[7].Host.Visibility = resultsPanel.Visibility = HasQuery ? Visibility.Visible : Visibility.Collapsed;
        UpdateMaterialBounds();
    }
    private void RefreshApplications()
    {
        var apps = applications.VisibleDockApplications.Select(item => item.Application);
        pinnedStrip.SetApps(HomeApplications.Select(apps, running: false));
        runningStrip.SetApps(HomeApplications.Select(apps, running: true));
    }
    private async Task ActivateAppAsync(DockApplication app)
    {
        if (launching || !IsVisible) return;
        launching = true;
        // Hide before foreground transfer; hiding after activation can steal focus from the target.
        HideHome();
        try
        {
            if (!await activateApplication(app) && !closed)
            {
                Toggle(); actionStatus.Text = $"Could not open {app.Name}.";
            }
        }
        finally { launching = false; }
    }
}
