using GlassDock.App.Controls;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow
{
    private async void NativeVolumeOsdChanged(object sender, RoutedEventArgs e)
    {
        if (populating || !shellReady || saving || closed || settingsSession.Current.SuppressNativeVolumeOsd == NativeVolumeOsdToggle.IsOn) return;
        await SaveRecoverySettingsAsync(settingsSession.Current with { SuppressNativeVolumeOsd = NativeVolumeOsdToggle.IsOn }, "Volume popup preference saved.");
        if (!closed)
        {
            populating = true;
            try { NativeVolumeOsdToggle.IsOn = settingsSession.Current.SuppressNativeVolumeOsd; }
            finally { populating = false; }
        }
    }

    private void RefreshUtilitiesPage()
    {
        UtilitiesGrid.Children.Clear();
        UtilitiesGrid.RowDefinitions.Clear();
        const int columns = 3;
        for (var row = 0; row <= UtilityDefinition.All.Count / columns; row++)
            UtilitiesGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var current = settingsSession.Current;
        UtilityDisplayModeBox.SelectedIndex = (int)current.UtilityDisplayMode;
        UtilityThemeBox.SelectedIndex = (int)current.UtilityTheme;
        for (var i = 0; i < UtilityDefinition.All.Count; i++)
        {
            var definition = UtilityDefinition.All[i];
            var added = current.SelectedUtilities.Contains(definition.Kind);
            var filled = definition.Kind is UtilityKind.Brightness or UtilityKind.AirplaneMode or UtilityKind.NightLight or UtilityKind.BatterySaver or UtilityKind.Focus || added;
            AddTile(i, definition.Name, definition.Icon, filled, added, definition.Available, settings =>
            {
                var selected = settings.SelectedUtilities.Contains(definition.Kind)
                    ? settings.SelectedUtilities.Where(k => k != definition.Kind).ToArray()
                    : settings.SelectedUtilities.Append(definition.Kind).ToArray();
                return settings with { SelectedUtilities = selected };
            });
        }

        AddTile(UtilityDefinition.All.Count, "Music", "music-note", true, current.ShowMusicControlPill, true,
            settings => settings with { ShowMusicControlPill = !settings.ShowMusicControlPill });

        void AddTile(int index, string name, string asset, bool filled, bool added, bool available,
            Func<GlassDockSettings, GlassDockSettings> update)
        {
            var icon = new UtilityIcon { Width = 24, Height = 24 };
            _ = icon.SetAsync(asset, filled, UtilityColors.Icon(current.UtilityTheme, false, !available));
            var content = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            content.Children.Add(new TextBlock { Text = !available ? "Unavailable" : added ? "Added · Remove" : "+ Add",
                FontSize = 12, TextAlignment = TextAlignment.Center, Foreground = Brush(added ? "AccentTextBrush" : "SecondaryBrush") });
            var button = new Button { Content = content, IsEnabled = available, MinHeight = 104, Padding = new(12),
                Background = Brush(added ? "SelectionBrush" : "CardBrush"), BorderBrush = Brush("DividerBrush"),
                CornerRadius = new(10), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, name + (!available ? " — not available yet" : added ? " — remove" : " — add"));
            button.Click += async (_, _) =>
            {
                if (saving || closed) return;
                await SaveRecoverySettingsAsync(update(settingsSession.Current), "Utilities saved.");
                if (!closed) RefreshUtilitiesPage();
            };
            Grid.SetColumn(button, index % columns); Grid.SetRow(button, index / columns); UtilitiesGrid.Children.Add(button);
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

    private async void UtilityDisplayModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (populating || !shellReady || saving || closed || UtilityDisplayModeBox.SelectedIndex < 0) return;
        var mode = (UtilityDisplayMode)UtilityDisplayModeBox.SelectedIndex;
        if (settingsSession.Current.UtilityDisplayMode == mode) return;
        await SaveRecoverySettingsAsync(settingsSession.Current with { UtilityDisplayMode = mode }, "Utility display mode saved.");
        RefreshUtilitiesPage();
    }
}
