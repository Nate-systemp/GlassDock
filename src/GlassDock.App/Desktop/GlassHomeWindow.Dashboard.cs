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

        var runningCard = cards[2];
        actionStatus = Label("", 10, true);
        actionStatus.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(actionStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        runningCard.Content.Children.Add(actionStatus);
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
        var materialMode = selectedMode;
        var paletteMode = selectedMode == DockAppearanceMode.Light ? DockAppearanceMode.Light :
            selectedMode == DockAppearanceMode.Dark ? DockAppearanceMode.Dark :
            WindowsSystemTheme.IsDark() ? DockAppearanceMode.Dark : DockAppearanceMode.Light;
        theme.Apply(paletteMode);
        theme.ApplyReadingContrast(settingsSession.Current.DockOpacity, materialMode);
        root.RequestedTheme = paletteMode == DockAppearanceMode.Light ? ElementTheme.Light : ElementTheme.Dark;
        var material = PopupMaterial.CreateForPopup(settingsSession.Appearance, materialMode);
        UpdateMaterialBounds();
        PopupStyle.Apply(cards[0].Surface, backdrop, settingsSession.Appearance, materialMode);
        foreach (var card in cards) card.Apply(PopupMaterial.ForDashboardCard(material, materialMode));
        if ((Application.Current as App)?.BasicRendering == true)
            foreach (var card in cards) card.Surface.SetDesktopFallback(true);
        UpdateMaterialBounds();
    }

    private void LayoutCards()
    {
        for (var i = 0; i < 3; i++) Place(cards[i].Host, layout.Cards[i]);
        var first = layout.Cards[1]; var last = layout.Cards[2];
        Place(cards[3].Host, new(first.X, first.Y, layout.Cards[0].Width, last.Y + last.Height - first.Y));
        pinnedStrip.SetWidth(first.Width - 36); runningStrip.SetWidth(first.Width - 36);
        resultsList.MaxHeight = Math.Max(50, cards[3].Host.Height - 80);
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
        for (var i = 1; i < 3; i++) cards[i].Host.Visibility = HasQuery ? Visibility.Collapsed : Visibility.Visible;
        cards[3].Host.Visibility = resultsPanel.Visibility = HasQuery ? Visibility.Visible : Visibility.Collapsed;
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
