using GlassDock.App.Controls;
using GlassDock.Core.Applications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GlassDock.App.Desktop;
internal sealed partial class GlassHomeWindow
{
    private void OnQueryChanged(object sender, TextChangedEventArgs args)
    {
        launchError = null;

        // Do not index the machine merely because Glass Home was opened.
        // The metadata worker starts only when the user actually searches.
        if (HasQuery)
            applicationIndex.Start();

        RefreshResults(false);
    }

    private void OnIndexChanged(object? sender, EventArgs args)
    {
        // Coalesce worker notifications; always filter the CURRENT query on the UI thread.
        if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref refreshQueued, 0);
            if (!closed && IsVisible) RefreshResults(true);
        })) Interlocked.Exchange(ref refreshQueued, 0);
    }

    private void RefreshResults(bool preserveSelection)
    {
        if (closed)
            return;

        var snapshot = applicationIndex.Snapshot;

        selection.Replace(
            GlassSearch.Find(
                snapshot.Applications.Concat(settings),
                search.Text),
            preserveSelection);

        // Only the at-most-eight visible matches request real app icons.
        // The installed-app index itself stays metadata-only.
        if (HasQuery && selection.Results.Count > 0)
            applicationIndex.RequestIcons(selection.Results);

        var renderKey =
            string.Join(
                "\u001F",
                selection.Results.Select(
                    result =>
                    {
                        var icon =
                            result.Icon ??
                            applicationIndex.GetIcon(result.StableId);

                        return
                            result.StableId +
                            "|" +
                            result.Title +
                            "|" +
                            result.Subtitle +
                            "|" +
                            (icon is null ? "0" : "1");
                    }));

        // Avoid throwing away/recreating the same WinUI tree for every
        // metadata/icon worker notification.
        if (!string.Equals(
                renderedResultsKey,
                renderKey,
                StringComparison.Ordinal))
        {
            renderedResultsKey = renderKey;
            resultsList.Items.Clear();

            foreach (var result in selection.Results)
            {
                var row = new Grid
                {
                    ColumnSpacing = 12,
                    Height = 54
                };

                row.ColumnDefinitions.Add(
                    new()
                    {
                        Width = new GridLength(36)
                    });

                row.ColumnDefinitions.Add(
                    new()
                    {
                        Width = new GridLength(
                            1,
                            GridUnitType.Star)
                    });

                var appIcon =
                    result.Icon ??
                    applicationIndex.GetIcon(result.StableId);

                if (appIcon is not null)
                {
                    var icon =
                        new AdaptiveAppIcon(
                            32,
                            1);

                    icon.SetIcon(appIcon);
                    row.Children.Add(icon);
                }
                else
                {
                    row.Children.Add(
                        new FontIcon
                        {
                            Glyph =
                                result.ResultType ==
                                GlassSearchResultType.Setting
                                    ? "\uE713"
                                    : "\uE71D",
                            FontSize = 24,
                            Foreground = theme.Primary
                        });
                }

                var labels = new StackPanel
                {
                    VerticalAlignment =
                        VerticalAlignment.Center,
                    Spacing = 2
                };

                labels.Children.Add(
                    new TextBlock
                    {
                        Text = result.Title,
                        FontFamily = UiFont,
                        FontSize = 15,
                        FontWeight =
                            Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = theme.Primary,
                        TextTrimming =
                            TextTrimming.CharacterEllipsis
                    });

                labels.Children.Add(
                    new TextBlock
                    {
                        Text = result.Subtitle,
                        FontFamily = UiFont,
                        FontSize = 11.5,
                        Foreground = theme.Secondary,
                        TextTrimming =
                            TextTrimming.CharacterEllipsis
                    });

                Grid.SetColumn(
                    labels,
                    1);

                row.Children.Add(labels);

                var item =
                    new ListViewItem
                    {
                        Content = row,
                        Tag = result,
                        IsTabStop = false,
                        CornerRadius =
                            new CornerRadius(12),
                        HorizontalContentAlignment =
                            HorizontalAlignment.Stretch,
                        Padding =
                            new Thickness(
                                10,
                                2,
                                10,
                                2)
                    };

                Microsoft.UI.Xaml.Automation
                    .AutomationProperties.SetName(
                        item,
                        result.Title +
                        ", " +
                        result.Subtitle);

                resultsList.Items.Add(item);
            }
        }

        resultsList.SelectedIndex =
            selection.Index;

        searchStatus.Text =
            launchError ??
            (snapshot.IsIndexing
                ? "Indexing applications…"
                : snapshot.Warning ??
                  (selection.Results.Count == 0
                      ? "No results"
                      : ""));

        searchStatus.Visibility =
            HasQuery &&
            searchStatus.Text.Length > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (IsVisible)
            UpdateDashboardVisibility();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is global::Windows.System.VirtualKey.Down or global::Windows.System.VirtualKey.Up)
        {
            selection.Move(args.Key == global::Windows.System.VirtualKey.Down ? 1 : -1);
            resultsList.SelectedIndex = selection.Index;
            if (selection.Index >= 0) resultsList.ScrollIntoView(resultsList.Items[selection.Index]);
            args.Handled = true;
        }
        else if (args.Key == global::Windows.System.VirtualKey.Enter)
        {
            if (selection.Selected is { } result) _ = LaunchResultAsync(result);
            args.Handled = true;
        }
    }

    private async Task LaunchResultAsync(GlassSearchResult result)
    {
        if (launching || closed || !IsVisible) return;
        launching = true;
        var revision = visibilityRevision;
        try
        {
            var success = await launcher.LaunchTargetAsync(result.LaunchTarget);
            if (closed || visibilityRevision != revision) return;
            if (success) HideHome();
            else
            {
                launchError = $"Could not open {result.Title}. It may have been moved or removed.";
                searchStatus.Text = launchError;
                searchStatus.Visibility = Visibility.Visible;
                UpdateDashboardVisibility(); search.Focus(FocusState.Keyboard);
            }
        }
        finally { launching = false; }
    }

}

