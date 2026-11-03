using GlassDock.App.Controls;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow
{
    private void RefreshUtilitiesPage()
    {
        UtilitiesGrid.Children.Clear();
        var current = settingsSession.Current;
        UtilityThemeBox.SelectedIndex = (int)current.UtilityTheme;
        for (var i = 0; i < UtilityDefinition.All.Count; i++)
        {
            var definition = UtilityDefinition.All[i];
            var added = current.SelectedUtilities.Contains(definition.Kind);
            var icon = new UtilityIcon { Width = 28, Height = 28 };
            _ = icon.SetAsync(definition.Icon, added, UtilityColors.Icon(current.UtilityTheme, false, !definition.Available));
            var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = definition.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center });
            content.Children.Add(new TextBlock { Text = !definition.Available ? "Not available yet" : added ? "Remove" : "Add", FontSize = 12, TextAlignment = TextAlignment.Center });
            var button = new Button { Content = content, IsEnabled = definition.Available, Padding = new(12), Margin = new(4), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            AutomationProperties.SetName(button, definition.Name + (!definition.Available ? " — not available yet" : added ? " — remove" : " — add"));
            button.Click += async (_, _) =>
            {
                if (saving || closed) return;
                var settings = settingsSession.Current;
                var selected = settings.SelectedUtilities.Contains(definition.Kind)
                    ? settings.SelectedUtilities.Where(k => k != definition.Kind).ToArray()
                    : settings.SelectedUtilities.Append(definition.Kind).ToArray();
                await SaveRecoverySettingsAsync(settings with { SelectedUtilities = selected }, "Utilities saved.");
                RefreshUtilitiesPage();
            };
            Grid.SetColumn(button, i % 4); Grid.SetRow(button, i / 4); UtilitiesGrid.Children.Add(button);
        }
    }

    private async void UtilityThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (populating || !shellReady || saving || closed || UtilityThemeBox.SelectedIndex < 0) return;
        var theme = (UtilityTheme)UtilityThemeBox.SelectedIndex;
        if (settingsSession.Current.UtilityTheme == theme) return;
        await SaveRecoverySettingsAsync(settingsSession.Current with { UtilityTheme = theme }, "Settings and utility theme saved.");
        RefreshUtilitiesPage();
    }
}
