using System.Diagnostics;
using System.Runtime.InteropServices;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

/// <summary>Retained, interactive Home HWND. Only the visible session polls the pointer.</summary>
internal sealed class GlassHomeWindow : Window
{
    private const double CompactHeight = 78;
    private const double TargetWidth = 680;
    private const double TargetExpandedHeight = 440;
    private const double CompactCornerRadius = 39;

    private static readonly FontFamily UiFont =
        new("Segoe UI Variable Text");

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private readonly GlassHomeSession session = new();
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly GlassSurface surface = new() { UseDesktopBackdrop = true };
    private readonly Grid root = new() { RequestedTheme = ElementTheme.Dark };
    private readonly TextBox search = new()
    {
        PlaceholderText = "Search apps and settings…",
        FontFamily = UiFont,
        FontSize = 18,
        FontWeight = Microsoft.UI.Text.FontWeights.Normal,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0, 6, 0, 6),
        BorderBrush = Brush(0),
        Background = Brush(0),
        Foreground = Brush(238),
        VerticalAlignment = VerticalAlignment.Center,
        UseSystemFocusVisuals = false
    };
    private readonly StackPanel extra = new() { Spacing = 18, Margin = new Thickness(24, 12, 24, 24) };
    private readonly WindowsApplicationIndex applicationIndex = new();
    private readonly WindowsApplicationLauncher launcher = new();
    private readonly GlassSearchSelection selection = new();
    private readonly IReadOnlyList<GlassSearchResult> settings = WindowsSettingsCatalog.Entries.Select(entry => entry.ToSearchResult()).ToArray();
    private readonly StackPanel resultsPanel = new()
    {
        Margin = new Thickness(18, 2, 18, 14),
        Spacing = 5,
        Visibility = Visibility.Collapsed
    };

    private readonly ListView resultsList = new()
    {
        SelectionMode = ListViewSelectionMode.Single,
        IsItemClickEnabled = true,
        IsTabStop = false,
        SingleSelectionFollowsFocus = false,
        Background = Brush(0),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0)
    };

    private readonly TextBlock searchStatus = new()
    {
        Margin = new Thickness(12, 4, 12, 4),
        FontFamily = UiFont,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush(165)
    };
    private ScrollViewer browsePanel = null!;
    private bool launching;
    private string? launchError;
    private int refreshQueued;
    private string renderedResultsKey = string.Empty;
    private bool HasQuery => !string.IsNullOrWhiteSpace(search.Text);
    private readonly TranslateTransform contentOffset = new();
    private readonly DispatcherQueueTimer polling;
    private readonly DispatcherQueueTimer animation;
    private readonly DispatcherQueueTimer focusRetry;
    private readonly InteractiveGlassWindowHost host;
    private readonly nint hwnd;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double animationStart, progress, scale = 1, width = TargetWidth, expandedHeight = TargetExpandedHeight;
    private int x, y;
    private bool closed, changingVisibility;
    private bool active, searchFocusPending;
    private int focusAttempts;

    public bool IsVisible => !closed && session.State != GlassHomeState.Hidden;
    public event EventHandler? HomeVisibilityChanged;

    public GlassHomeWindow()
    {
        Title = "Doky — Glass Home";
        WindowBranding.Apply(this);
        var material = GlassMaterialPresets.Create(GlassMaterialPreset.Clear) with
        {
            // Reference direction: dark acrylic / luminous glass, not milky frosted glass.
            BlurAmount = 18,
            Opacity = .56,
            Saturation = 1.12,
            Brightness = 1.05,
            Tint = 0xC8DAFF,
            CornerRadius = CompactCornerRadius,
            ShadowOpacity = .26,
            ShadowBlur = 34,
            ShadowOffset = 8,
            EdgeHighlight = .10,

            // The main luminous outline is drawn locally in GlassHomeWindow
            // so the global GlassSurface/dock styling stays untouched.
            BorderOpacity = 0,
            BorderThickness = 0
        };

        surface.Apply(material);
        backdrop.Apply(material);

        root.Children.Add(surface);
        root.Children.Add(CreateAcrylicChrome());
        root.Children.Add(CreateContent());
        Content = root;
        SystemBackdrop = backdrop;
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        host = new InteractiveGlassWindowHost(hwnd);
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        polling = DispatcherQueue.CreateTimer();
        polling.Interval = TimeSpan.FromMilliseconds(25);
        polling.Tick += Poll;
        animation = DispatcherQueue.CreateTimer();
        animation.Interval = TimeSpan.FromMilliseconds(16);
        animation.Tick += Animate;

        // Win + Space can be pressed while another application owns keyboard focus.
        // Retry for a very short window while WinUI finishes showing/activating the HWND.
        focusRetry = DispatcherQueue.CreateTimer();
        focusRetry.Interval = TimeSpan.FromMilliseconds(20);
        focusRetry.Tick += RetrySearchFocus;

        search.TextChanged += OnQueryChanged;
        search.PreviewKeyDown += OnSearchKeyDown;
        resultsList.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is ListViewItem { Tag: GlassSearchResult result }) _ = LaunchResultAsync(result);
        };
        applicationIndex.Changed += OnIndexChanged;
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape) return;
            HideHome(); e.Handled = true;
        }), true);
        Activated += OnActivated;
        root.Loaded += (_, _) => { UpdateGlassBounds(); RequestSearchFocus(); };
        surface.SizeChanged += (_, _) => UpdateGlassBounds();
        Closed += (_, _) =>
        {
            closed = true; session.Hide();
            searchFocusPending = false;
            Activated -= OnActivated;
            polling.Stop();
            animation.Stop();
            focusRetry.Stop();

            polling.Tick -= Poll;
            animation.Tick -= Animate;
            focusRetry.Tick -= RetrySearchFocus;

            search.TextChanged -= OnQueryChanged;
            search.PreviewKeyDown -= OnSearchKeyDown;
            applicationIndex.Changed -= OnIndexChanged;
            applicationIndex.Dispose();
            host.Dispose();
        };
    }

    private Grid CreateContent()
    {
        var content = new Grid();

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(CompactHeight)
            });

        content.RowDefinitions.Add(
            new RowDefinition
            {
                Height = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        //
        // Compact Spotlight row.
        //
        var searchRow = new Grid
        {
            Margin = new Thickness(26, 0, 20, 0),
            ColumnSpacing = 12
        };

        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(
                    1,
                    GridUnitType.Star)
            });

        searchRow.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = GridLength.Auto
            });

        var searchIcon = new FontIcon
        {
            Glyph = "\uE721",
            FontSize = 21,
            Foreground = Brush(205),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(searchIcon, 0);
        searchRow.Children.Add(searchIcon);

        var divider = new Border
        {
            Width = 1,
            Height = 26,
            CornerRadius = new CornerRadius(.5),
            Background = Brush(62),
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(divider, 1);
        searchRow.Children.Add(divider);

        //
        // Native editor/caret/IME, with all default WinUI box chrome removed.
        //
        foreach (var key in new[]
                 {
                     "TextControlBackground",
                     "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused",
                     "TextControlBackgroundDisabled",
                     "TextControlBorderBrush",
                     "TextControlBorderBrushPointerOver",
                     "TextControlBorderBrushFocused",
                     "TextControlBorderBrushDisabled",
                     "TextControlElevationBorderBrush"
                 })
        {
            search.Resources[key] = Brush(0);
        }

        search.Resources["TextControlBorderThemeThickness"] =
            new Thickness(0);

        search.Resources["TextControlPlaceholderForeground"] =
            Brush(178);

        search.Resources["TextControlPlaceholderForegroundFocused"] =
            Brush(178);

        search.Resources["TextControlPlaceholderForegroundPointerOver"] =
            Brush(192);

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            search,
            "Search apps and settings");

        Grid.SetColumn(search, 2);
        searchRow.Children.Add(search);

        var shortcutBadge =
            CreateShortcutBadge();

        Grid.SetColumn(shortcutBadge, 3);
        searchRow.Children.Add(shortcutBadge);

        content.Children.Add(searchRow);

        //
        // Search result styling.
        //
        resultsList.Resources["ListViewItemBackgroundSelected"] =
            Brush(26);

        resultsList.Resources["ListViewItemBackgroundSelectedPointerOver"] =
            Brush(36);

        resultsList.Resources["ListViewItemBackgroundSelectedPressed"] =
            Brush(44);

        resultsList.Resources["ListViewItemBackgroundPointerOver"] =
            Brush(15);

        //
        // Expanded browse content.
        //
        extra.RenderTransform = contentOffset;

        extra.Children.Add(
            Label(
                "PINNED",
                11));

        var categories = new Grid
        {
            ColumnSpacing = 10
        };

        foreach (var name in new[]
                 {
                     "Apps",
                     "Files",
                     "Projects",
                     "More"
                 })
        {
            var index =
                categories.ColumnDefinitions.Count;

            categories.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(
                        1,
                        GridUnitType.Star)
                });

            var tile = new Border
            {
                Height = 72,
                CornerRadius = new CornerRadius(16),
                Background =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                1,
                                1),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        24,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        10,
                                        140,
                                        190,
                                        255)
                            }
                        }
                    },
                BorderBrush = Brush(24),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = name,
                    FontFamily = UiFont,
                    FontSize = 13,
                    Foreground = Brush(195),
                    HorizontalAlignment =
                        HorizontalAlignment.Center,
                    VerticalAlignment =
                        VerticalAlignment.Center
                }
            };

            Grid.SetColumn(
                tile,
                index);

            categories.Children.Add(
                tile);
        }

        extra.Children.Add(categories);

        extra.Children.Add(
            Label(
                "RECENT",
                11));

        extra.Children.Add(
            Label(
                "No recent items yet",
                14));

        var footer = new Grid
        {
            Margin = new Thickness(
                0,
                16,
                0,
                0)
        };

        footer.Children.Add(
            Label(
                "Doky",
                12));

        footer.Children.Add(
            new TextBlock
            {
                Text = "Power",
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = Brush(130),
                HorizontalAlignment =
                    HorizontalAlignment.Right
            });

        extra.Children.Add(footer);

        browsePanel = new ScrollViewer
        {
            Content = extra,
            HorizontalScrollBarVisibility =
                ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Auto
        };

        Grid.SetRow(
            browsePanel,
            1);

        content.Children.Add(
            browsePanel);

        resultsPanel.Children.Add(resultsList);
        resultsPanel.Children.Add(searchStatus);

        Grid.SetRow(resultsPanel, 1);
        content.Children.Add(resultsPanel);

        return content;
    }

    private Grid CreateShortcutBadge()
    {
        //
        // The shortcut is intentionally a second "mini acrylic" capsule,
        // not a flat button. It cannot sample a separate backdrop layer,
        // but the stacked translucent gradients + luminous rim make it read
        // as an independent piece of glass on top of the main acrylic bar.
        //
        var host = new Grid
        {
            Width = 104,
            Height = 38,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        host.Children.Add(
            new Border
            {
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(19),
                BorderThickness = new Thickness(2),
                BorderBrush =
                    new SolidColorBrush(
                        global::Windows.UI.Color.FromArgb(
                            26,
                            110,
                            190,
                            255))
            });

        host.Children.Add(
            new Border
            {
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Background =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                1),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        42,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .45,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        24,
                                        40,
                                        57,
                                        78)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        34,
                                        18,
                                        30,
                                        46)
                            }
                        }
                    },
                BorderBrush =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                1,
                                1),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        135,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .55,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        62,
                                        190,
                                        225,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        88,
                                        255,
                                        255,
                                        255)
                            }
                        }
                    },
                BorderThickness = new Thickness(1),
                Child =
                    new TextBlock
                    {
                        Text = "WIN + SPACE",
                        FontFamily = UiFont,
                        FontSize = 10,
                        FontWeight =
                            Microsoft.UI.Text.FontWeights.SemiBold,
                        CharacterSpacing = 55,
                        Foreground = Brush(215),
                        HorizontalAlignment =
                            HorizontalAlignment.Center,
                        VerticalAlignment =
                            VerticalAlignment.Center
                    }
            });

        return host;
    }

    private UIElement CreateAcrylicChrome()
    {
        //
        // Dedicated Glass Home chrome.
        // This is intentionally local so the dock's own glass material
        // remains untouched.
        //
        var chrome = new Grid
        {
            IsHitTestVisible = false
        };

        //
        // Wide, low-opacity cyan halo just inside the client bounds.
        // Multiple strokes create the soft luminous bloom from the reference
        // without changing the shared GlassSurface implementation.
        //
        chrome.Children.Add(
            new Border
            {
                Margin = new Thickness(.5),
                CornerRadius =
                    new CornerRadius(
                        CompactCornerRadius - .5),
                BorderThickness = new Thickness(3),
                BorderBrush =
                    new SolidColorBrush(
                        global::Windows.UI.Color.FromArgb(
                            26,
                            72,
                            168,
                            255))
            });

        chrome.Children.Add(
            new Border
            {
                Margin = new Thickness(1.5),
                CornerRadius =
                    new CornerRadius(
                        CompactCornerRadius - 1.5),
                BorderThickness = new Thickness(2),
                BorderBrush =
                    new SolidColorBrush(
                        global::Windows.UI.Color.FromArgb(
                            48,
                            125,
                            205,
                            255))
            });

        //
        // Bright core rim: roughly 1.4px and much brighter at the top.
        //
        chrome.Children.Add(
            new Border
            {
                Margin = new Thickness(1),
                CornerRadius =
                    new CornerRadius(
                        CompactCornerRadius - 1),
                BorderThickness = new Thickness(1.35),
                BorderBrush =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                0.8,
                                1),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        225,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .40,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        150,
                                        235,
                                        248,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .72,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        118,
                                        125,
                                        205,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        170,
                                        235,
                                        248,
                                        255)
                            }
                        }
                    }
            });

        //
        // A thin specular top highlight similar to acrylic catching light.
        //
        chrome.Children.Add(
            new Border
            {
                Margin = new Thickness(
                    22,
                    1.5,
                    22,
                    0),
                Height = 1.25,
                CornerRadius =
                    new CornerRadius(.625),
                VerticalAlignment =
                    VerticalAlignment.Top,
                Background =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                1,
                                0),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        0,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .18,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        120,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .52,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        180,
                                        185,
                                        225,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .82,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        110,
                                        255,
                                        255,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        0,
                                        255,
                                        255,
                                        255)
                            }
                        }
                    }
            });

        //
        // Cool bottom glow; lower opacity than the top edge.
        //
        chrome.Children.Add(
            new Border
            {
                Margin = new Thickness(
                    70,
                    0,
                    70,
                    1.5),
                Height = 1.5,
                CornerRadius =
                    new CornerRadius(.75),
                VerticalAlignment =
                    VerticalAlignment.Bottom,
                Background =
                    new LinearGradientBrush
                    {
                        StartPoint =
                            new global::Windows.Foundation.Point(
                                0,
                                0),
                        EndPoint =
                            new global::Windows.Foundation.Point(
                                1,
                                0),
                        GradientStops =
                        {
                            new GradientStop
                            {
                                Offset = 0,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        0,
                                        80,
                                        170,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = .5,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        118,
                                        80,
                                        175,
                                        255)
                            },
                            new GradientStop
                            {
                                Offset = 1,
                                Color =
                                    global::Windows.UI.Color.FromArgb(
                                        0,
                                        80,
                                        170,
                                        255)
                            }
                        }
                    }
            });

        return chrome;
    }

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
            if (!closed && session.State != GlassHomeState.Hidden) RefreshResults(true);
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
                            Foreground = Brush(220)
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
                        Foreground = Brush(238),
                        TextTrimming =
                            TextTrimming.CharacterEllipsis
                    });

                labels.Children.Add(
                    new TextBlock
                    {
                        Text = result.Subtitle,
                        FontFamily = UiFont,
                        FontSize = 11.5,
                        Foreground = Brush(165),
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

        if (session.State != GlassHomeState.Hidden)
            ApplyFrame();
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
        if (launching || closed || session.State == GlassHomeState.Hidden) return;
        launching = true;
        var revision = session.Revision;
        try
        {
            var success = await launcher.LaunchTargetAsync(result.LaunchTarget);
            if (closed || session.Revision != revision) return;
            if (success) HideHome();
            else
            {
                launchError = $"Could not open {result.Title}. It may have been moved or removed.";
                searchStatus.Text = launchError;
                searchStatus.Visibility = Visibility.Visible;
                ApplyFrame(); TryFocusSearch();
            }
        }
        finally { launching = false; }
    }

    public void Toggle()
    {
        if (closed || changingVisibility) return;
        if (session.State != GlassHomeState.Hidden) { HideHome(); return; }
        changingVisibility = true;
        try
        {
            // Capture before activation/layout so a stationary pointer inside the new window stays Compact.
            session.Open(GlassHomeInput.Read(hwnd));
            searchFocusPending = true;
            focusAttempts = 0;
            focusRetry.Stop();

            animation.Stop();
            progress = 0;
            search.Text = "";
            ConfigurePlacement(); RefreshResults(false); ApplyFrame();
            AppWindow.Show();

            // Explicitly make Glass Home the foreground keyboard window.
            // This is what lets Win + Space immediately redirect typing away
            // from whatever app/editor was focused before the launcher opened.
            Activate();
            SetForegroundWindow(hwnd);

            ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = true;

            UpdateGlassBounds();
            RequestSearchFocus();
            polling.Start();
            HomeVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch { HideHome(); throw; }
        finally { changingVisibility = false; }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (active) RequestSearchFocus();
    }

    private void RequestSearchFocus()
    {
        if (closed ||
            !searchFocusPending ||
            session.State == GlassHomeState.Hidden)
        {
            return;
        }

        //
        // First attempt immediately. Win + Space may have been pressed while
        // VS Code, a preview window, browser, etc. owned keyboard focus.
        //
        SetForegroundWindow(hwnd);
        Activate();

        if (TryFocusSearch())
        {
            searchFocusPending = false;
            focusRetry.Stop();
            return;
        }

        //
        // WinUI can restore XAML focus a few milliseconds after Window.Activate().
        // Queue one attempt for the next dispatcher turn, then use a tiny bounded
        // retry window instead of an arbitrary long delay.
        //
        var revision = session.Revision;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed ||
                !searchFocusPending ||
                session.Revision != revision ||
                session.State == GlassHomeState.Hidden)
            {
                return;
            }

            SetForegroundWindow(hwnd);

            if (TryFocusSearch())
            {
                searchFocusPending = false;
                focusRetry.Stop();
                return;
            }

            focusAttempts = 0;

            if (!focusRetry.IsRunning)
                focusRetry.Start();
        });
    }

    private void RetrySearchFocus(
        DispatcherQueueTimer sender,
        object args)
    {
        if (closed ||
            !searchFocusPending ||
            session.State == GlassHomeState.Hidden)
        {
            sender.Stop();
            return;
        }

        //
        // Keep this short: 12 × 20 ms = at most ~240 ms.
        // Usually the first or second attempt succeeds.
        //
        if (++focusAttempts > 12)
        {
            sender.Stop();
            return;
        }

        SetForegroundWindow(hwnd);

        if (!active)
            Activate();

        if (!TryFocusSearch())
            return;

        searchFocusPending = false;
        sender.Stop();

        // Put the caret at the end in case this method is reused without
        // clearing the query in a future Glass Home state.
        search.SelectionStart = search.Text.Length;
        search.SelectionLength = 0;
    }

    private bool TryFocusSearch()
    {
        if (!active ||
            !search.IsLoaded ||
            search.XamlRoot is null ||
            session.State == GlassHomeState.Hidden)
        {
            return false;
        }

        if (!search.Focus(FocusState.Keyboard))
            return false;

        search.SelectionStart = search.Text.Length;
        search.SelectionLength = 0;

        return true;
    }

    public void HideHome()
    {
        if (closed) return;
        var wasVisible = session.State != GlassHomeState.Hidden;
        searchFocusPending = false;
        focusAttempts = 0;
        focusRetry.Stop();

        active = false;
        session.Hide();
        polling.Stop();
        animation.Stop();

        // Release transient search-result controls while Home is hidden.
        // The metadata index and its small bounded icon cache remain reusable.
        resultsList.Items.Clear();
        renderedResultsKey = string.Empty;

        AppWindow.Hide(); // Keep the HWND and SystemBackdrop; reconnect restores the retained mask.
        if (wasVisible)
            HomeVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Poll(DispatcherQueueTimer sender, object args)
    {
        if (closed || changingVisibility || session.State == GlassHomeState.Hidden) return;
        var before = session.State;
        if (GlassHomeInput.Read(hwnd) is { } pointer) session.Observe(pointer);
        if (session.State == GlassHomeState.Hidden) { HideHome(); return; }
        if (Math.Abs(GlassHomeInput.Scale(hwnd) - scale) > .001) { ConfigurePlacement(); ApplyFrame(); }
        if (before == GlassHomeState.Compact && session.State == GlassHomeState.Expanded)
        {
            animationStart = clock.Elapsed.TotalMilliseconds;
            animation.Start();
        }
    }

    private void Animate(DispatcherQueueTimer sender, object args)
    {
        if (closed || session.State != GlassHomeState.Expanded) { animation.Stop(); return; }
        var t = Math.Clamp((clock.Elapsed.TotalMilliseconds - animationStart) / 200, 0, 1);
        progress = t * t * (3 - 2 * t);
        ApplyFrame();
        if (t == 1) animation.Stop();
    }

    private void ConfigurePlacement()
    {
        scale = GlassHomeInput.Scale(hwnd);
        var area = DisplayArea.Primary.WorkArea;
        width =
            Math.Min(
                TargetWidth,
                Math.Max(
                    1,
                    area.Width / scale - 24));

        expandedHeight =
            Math.Min(
                TargetExpandedHeight,
                Math.Max(
                    CompactHeight,
                    area.Height / scale - 24));

        x =
            area.X +
            (int)Math.Round(
                (area.Width - width * scale) / 2);

        // Keep the compact pill centered. Expansion grows mainly downward.
        y =
            area.Y +
            (int)Math.Max(
                0,
                Math.Min(
                    (area.Height - CompactHeight * scale) / 2,
                    area.Height -
                    expandedHeight * scale -
                    12 * scale));
    }

    private void ApplyFrame()
    {
        var browseHeight =
            CompactHeight +
            (expandedHeight - CompactHeight) *
            progress;

        var workArea =
            DisplayArea.Primary.WorkArea;

        var availableHeight =
            Math.Max(
                CompactHeight,
                (workArea.Y +
                 workArea.Height -
                 y) /
                scale -
                12);

        var statusHeight =
            searchStatus.Visibility ==
            Visibility.Visible
                ? 54
                : 0;

        var searchHeight =
            Math.Min(
                availableHeight,
                CompactHeight +
                Math.Max(
                    58,
                    selection.Results.Count *
                    60 +
                    statusHeight) +
                12);

        var height =
            HasQuery
                ? searchHeight
                : browseHeight;

        resultsList.MaxHeight =
            Math.Max(
                0,
                height -
                CompactHeight -
                12 -
                statusHeight);

        resultsPanel.Visibility =
            HasQuery
                ? Visibility.Visible
                : Visibility.Collapsed;

        browsePanel.Visibility =
            HasQuery
                ? Visibility.Collapsed
                : Visibility.Visible;

        surface.Width =
            root.Width =
                width;

        surface.Height =
            root.Height =
                height;

        root.Clip =
            new RectangleGeometry
            {
                Rect =
                    new global::Windows.Foundation.Rect(
                        0,
                        0,
                        width,
                        height)
            };

        extra.Visibility =
            session.State ==
            GlassHomeState.Compact
                ? Visibility.Collapsed
                : Visibility.Visible;

        extra.Opacity =
            progress;

        extra.IsHitTestVisible =
            progress == 1;

        contentOffset.Y =
            8 *
            (1 - progress);

        AppWindow.MoveAndResize(
            new global::Windows.Graphics.RectInt32(
                x,
                y,
                (int)Math.Ceiling(
                    width *
                    scale),
                (int)Math.Ceiling(
                    height *
                    scale)));

        UpdateGlassBounds();
    }

    private void UpdateGlassBounds() => backdrop.SetBounds(width, surface.Height, width, surface.Height, 0,
        surface.XamlRoot?.RasterizationScale ?? scale);

    private static SolidColorBrush Brush(byte alpha) => new(global::Windows.UI.Color.FromArgb(alpha, 245, 248, 255));
    private static TextBlock Label(string text, double size) =>
        new()
        {
            Text = text,
            FontFamily = UiFont,
            FontSize = size,
            Foreground = Brush(180)
        };
}
