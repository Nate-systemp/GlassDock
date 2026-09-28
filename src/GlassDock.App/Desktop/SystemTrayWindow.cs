using GlassDock.Core.Settings;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Applications;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace GlassDock.App.Desktop;

/// <summary>
/// GlassDock's hidden-tray surface. Windows does not expose a supported public API
/// for enumerating every third-party notification icon, so this window uses the
/// taskbar accessibility tree as a best-effort bridge and keeps explicit Windows
/// settings fallbacks when Explorer does not expose usable items.
/// </summary>
internal sealed class SystemTrayWindow : Window
{
    private const double PanelWidth = 360;
    private const double PanelHeight = 286;
    private const double Gutter = UtilityPopupStyle.Gutter;

    private readonly WindowsSystemControlService controls;
    private readonly WindowsApplicationService applicationService;
    private readonly DesktopGlassBackdrop backdrop = new();
    private readonly InteractiveGlassWindowHost host;
    private readonly Grid root = new() { Background = Brush(0) };
    private readonly GlassSurface glass = new() { UseDesktopBackdrop = true, Margin = new Thickness(Gutter) };
    private readonly Grid trayGrid = new() { ColumnSpacing = 10, RowSpacing = 10 };
    private readonly TextBlock status = new()
    {
        FontSize = 11.5,
        Foreground = Brush(165),
        TextWrapping = TextWrapping.Wrap
    };
    private readonly UtilityPopupPresentation presentation;
    private bool closed;
    private string[] itemKeys = [];
    private int refreshVersion;
    private bool refreshRunning;
    private CancellationTokenSource? iconLoadCts;

    public SystemTrayWindow(
        WindowsSystemControlService controls,
        WindowsApplicationService applicationService,
        DockAppearanceSettings appearance,
        Func<bool>? utilityOwnsPointer = null)
    {
        this.controls = controls;
        this.applicationService = applicationService;
        Title = "Doky Hidden Tray";
        AppWindow.IsShownInSwitchers = false;
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;

        UtilityPopupStyle.Apply(glass, backdrop, appearance);
        SystemBackdrop = backdrop;
        root.Children.Add(glass);

        var content = new Grid
        {
            Margin = new Thickness(Gutter + 20, Gutter + 18, Gutter + 20, Gutter + 18),
            RowSpacing = 12
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "Hidden tray",
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(245),
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(title);
        var refresh = IconButton("\uE72C", "Refresh tray", () => _ = RefreshAsync());
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        content.Children.Add(header);

        for (var i = 0; i < 4; i++)
            trayGrid.ColumnDefinitions.Add(new ColumnDefinition());

        var scroll = new ScrollViewer
        {
            Content = trayGrid,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        content.Children.Add(scroll);

        var footer = new Grid { ColumnSpacing = 8 };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(status);
        var settings = TextButton("Tray settings", controls.OpenTraySettings);
        Grid.SetColumn(settings, 1);
        footer.Children.Add(settings);
        Grid.SetRow(footer, 2);
        content.Children.Add(footer);

        root.Children.Add(content);
        Content = root;
        presentation = new UtilityPopupPresentation(
            this,
            root,
            backdrop,
            utilityOwnsPointer);

        host = new InteractiveGlassWindowHost(WinRT.Interop.WindowNative.GetWindowHandle(this));
        try { host.Configure(); }
        catch { host.Dispose(); Close(); throw; }

        root.SizeChanged += (_, _) => UpdateBackdrop();
        root.Loaded += (_, _) =>
        {
            UpdateBackdrop();
            _ = RefreshAsync();
        };
        root.KeyDown += (_, e) =>
        {
            if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                Dismiss();
            }
        };
        Closed += (_, _) =>
        {
            closed = true;
            iconLoadCts?.Cancel();
            iconLoadCts?.Dispose();
            iconLoadCts = null;
            host.Dispose();
        };
    }

    public event EventHandler? Hidden
    {
        add => presentation.Hidden += value;
        remove => presentation.Hidden -= value;
    }

    public event EventHandler? Dismissed
    {
        add => presentation.Dismissed += value;
        remove => presentation.Dismissed -= value;
    }

    public void ApplyAppearance(DockAppearanceSettings appearance) =>
        UtilityPopupStyle.Apply(glass, backdrop, appearance);

    public void Present()
    {
        // Cached tray windows must refresh when they are shown again.
        _ = RefreshAsync();
        presentation.Present();
    }

    public void RetargetClosed() => presentation.RetargetClosed();
    public void HideImmediately() => presentation.HideImmediately();
    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(
        AppWindow owner,
        double scale,
        double anchorX,
        double anchorY,
        double dockTop)
    {
        UtilityPopupStyle.Position(
            AppWindow,
            owner,
            scale,
            anchorX,
            dockTop,
            PanelWidth,
            PanelHeight);

        scale = double.IsFinite(scale) && scale > 0
            ? scale
            : 1;

        presentation.SetTargetWindowGeometry(
            owner.Position.X + anchorX * scale,
            owner.Position.Y + anchorY * scale,
            scale);
        host.InputHeightPixels = presentation.InputHeightPixels;
    }


    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        if (closed)
            return;

        // Do not queue refreshes. A later request supersedes the in-flight scan.
        var version = ++refreshVersion;
        if (refreshRunning)
            return;

        refreshRunning = true;
        try
        {
            status.Text = "Reading Windows hidden tray…";

            // Windows 11 does not expose hidden notification icons until the
            // native overflow surface is made visible. The scanner opens that
            // surface off-screen/no-activate, reads accessibility, then closes it.
            var items = await WindowsTrayAccessibility.ReadHiddenItemsAsync();

            if (closed || version != refreshVersion)
                return;

            RenderItems(items);
        }
        finally
        {
            refreshRunning = false;

            // If another refresh request arrived while scanning, run exactly one
            // more pass rather than building an unbounded Task queue.
            if (!closed && version != refreshVersion)
                _ = RefreshAsync();
        }
    }

    private void RenderItems(IReadOnlyList<WindowsTrayAccessibility.TrayItem> items)
    {
        var nextKeys = items
            .Select(item => item.Name + "\n" + item.DefaultAction + "\n" + item.ExecutablePath + "\n" + item.Source)
            .ToArray();

        if (itemKeys.SequenceEqual(nextKeys) && trayGrid.Children.Count > 0)
        {
            // Preserve focus, pointer capture, and scroll position during refresh.
            var buttons = trayGrid.Children.OfType<Button>().ToArray();
            for (var i = 0; i < buttons.Length && i < items.Count; i++)
                buttons[i].Tag = items[i];
            return;
        }

        itemKeys = nextKeys;
        iconLoadCts?.Cancel();
        iconLoadCts?.Dispose();
        var iconLoad = new CancellationTokenSource();
        iconLoadCts = iconLoad;
        trayGrid.Children.Clear();
        trayGrid.RowDefinitions.Clear();

        if (items.Count == 0)
        {
            status.Text =
                "Windows did not expose any active hidden tray items.";

            trayGrid.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = GridLength.Auto
                });

            var empty = new TextBlock
            {
                Text = "No active hidden tray apps are available right now.",
                FontSize = 13,
                Foreground = Brush(205),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 34, 12, 12)
            };

            Grid.SetColumnSpan(empty, 4);
            trayGrid.Children.Add(empty);
            return;
        }

        status.Text =
            $"{items.Count} hidden tray item{(items.Count == 1 ? "" : "s")} · click to invoke";

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var row = index / 4;
            var column = index % 4;

            while (trayGrid.RowDefinitions.Count <= row)
                trayGrid.RowDefinitions.Add(
                    new RowDefinition
                    {
                        Height = GridLength.Auto
                    });

            var tile = TrayButton(item, iconLoad.Token);
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            trayGrid.Children.Add(tile);
        }
    }

    private Button TrayButton(
        WindowsTrayAccessibility.TrayItem item,
        CancellationToken iconToken)
    {
        var initial = item.Name.Trim().FirstOrDefault();
        var glyph = char.IsLetterOrDigit(initial)
            ? char.ToUpperInvariant(initial).ToString()
            : "•";

        var fallback = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Variable Display"),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(245),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var realIcon = new AdaptiveAppIcon(28, 1, showTile: false)
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        var icon = new Grid
        {
            Width = 28,
            Height = 28
        };
        icon.Children.Add(fallback);
        icon.Children.Add(realIcon);

        if (!string.IsNullOrWhiteSpace(item.ExecutablePath))
            _ = LoadTrayIconAsync(item, realIcon, fallback, iconToken);

        var label = new TextBlock
        {
            Text = item.Name,
            FontSize = 10.5,
            Foreground = Brush(210),
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
            Width = 66
        };

        var stack = new StackPanel
        {
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(icon);
        stack.Children.Add(label);

        var button = new Button
        {
            Content = stack,
            Background = Brush(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(4, 6, 4, 6),
            Tag = item,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

        button.Resources["ButtonBackgroundPointerOver"] = Brush(32);
        button.Resources["ButtonBackgroundPressed"] = Brush(48);
        ToolTipService.SetToolTip(
            button,
            string.IsNullOrWhiteSpace(item.DefaultAction)
                ? item.Name
                : $"{item.Name} · {item.DefaultAction}");

        button.Click += async (_, _) =>
        {
            var current = (WindowsTrayAccessibility.TrayItem)button.Tag;
            button.IsEnabled = false;
            try
            {
                if (await WindowsTrayAccessibility.InvokeAsync(current))
                    Dismiss();
                else
                    status.Text = $"Could not open {current.Name}.";
            }
            finally
            {
                if (!closed)
                    button.IsEnabled = true;
            }
        };

        button.RightTapped += (_, e) =>
        {
            e.Handled = true;
            controls.OpenTraySettings();
        };

        return button;
    }

    private async Task LoadTrayIconAsync(
        WindowsTrayAccessibility.TrayItem item,
        AdaptiveAppIcon target,
        TextBlock fallback,
        CancellationToken token)
    {
        var executablePath = item.ExecutablePath;
        if (string.IsNullOrWhiteSpace(executablePath) || token.IsCancellationRequested)
            return;

        try
        {
            var icon = await Task.Run(
                () => applicationService.GetIconForExternalTarget(executablePath),
                token);

            if (icon is null || token.IsCancellationRequested || closed)
                return;

            if (!DispatcherQueue.TryEnqueue(() =>
                {
                    if (token.IsCancellationRequested || closed)
                        return;

                    target.SetIcon(icon);
                    target.Visibility = Visibility.Visible;
                    fallback.Visibility = Visibility.Collapsed;
                }))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateBackdrop() =>
        presentation.UpdateBackdropBounds();

    private static Button IconButton(string glyph, string tooltip, Action action)
    {
        return SystemControlStyle.Button(SystemControlStyle.Icon(glyph, 15), tooltip, action, 34, 34);
    }

    private static Button TextButton(string text, Action action)
    {
        var button = new Button
        {
            Content = text,
            FontSize = 11.5,
            Foreground = Brush(225),
            Background = Brush(18),
            BorderBrush = Brush(54),
            BorderThickness = new Thickness(.6),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 5, 10, 5)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static SolidColorBrush Brush(byte alpha, byte r = 255, byte g = 255, byte b = 255) =>
        new(global::Windows.UI.Color.FromArgb(alpha, r, g, b));
}

internal static class WindowsTrayAccessibility
{
    internal enum TrayItemSource
    {
        Accessibility,
        Registry
    }

    internal sealed record TrayItem(
        string Name,
        string? DefaultAction,
        string? ExecutablePath,
        TrayItemSource Source);

    private const int ObjIdClient = -4;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private static readonly Guid IidAccessible =
        new("618736E0-3C3D-11CF-810C-00AA00389B71");

    /// <summary>
    /// Hidden notification icons are not exposed to accessibility while the
    /// Windows overflow flyout is closed. Open the native overflow off-screen,
    /// let Explorer realize its XAML children, read them, then hide it again.
    /// </summary>
    public static async Task<IReadOnlyList<TrayItem>> ReadHiddenItemsAsync()
    {
        var result = new List<TrayItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var session = TryOpenExistingOverflow();
        try
        {
            if (!session.Opened && TryInvokeOverflowChevron())
            {
                // Explorer creates the Windows 11 XAML overflow asynchronously.
                // Wait once for that explicit user-triggered scan, then move the
                // realized native flyout off-screen before enumerating it.
                await Task.Delay(70);
                session = TryOpenExistingOverflow();
            }

            // Give Explorer/XAML a short realization window only when the real
            // overflow surface is present. No background polling is introduced.
            if (session.Opened)
                await Task.Delay(90);

            foreach (var hwnd in OverflowAccessibilityRoots())
                ReadAccessibleRoot(hwnd, result, seen);

            // Legacy/bridge fallback. Do this after the true overflow root so
            // visible taskbar icons do not take precedence over hidden ones.
            if (result.Count == 0)
            {
                foreach (var hwnd in LegacyOverflowRoots())
                    ReadAccessibleRoot(hwnd, result, seen);
            }
        }
        finally
        {
            session.Dispose();
        }

        var accessibleItems = result
            .Where(item => !IsGlassDockSystemItem(item.Name))
            .Take(32)
            .ToArray();

        if (accessibleItems.Length > 0)
        {
            var metadata = ReadRegistryMetadataRecords();
            return accessibleItems
                .Select(item => item with
                {
                    ExecutablePath = MatchRegistryExecutable(item.Name, metadata)
                })
                .ToArray();
        }

        // GlassDock deliberately suppresses/disables the native Windows taskbar.
        // In that state Explorer may not expose the hidden-icon overflow through
        // accessibility at all. Fall back to Windows 11's per-user tray metadata
        // so the GlassDock panel is still useful instead of permanently empty.
        return ReadRegistryFallbackItems();
    }

    private static IReadOnlyList<TrayItem> ReadRegistryFallbackItems()
    {
        var records = ReadRegistryMetadataRecords();

        // Prefer Windows records explicitly marked hidden. Some current Windows
        // 11 builds leave IsPromoted unset for active overflow icons, so if the
        // strict hidden set produces nothing, allow only non-promoted/unknown
        // records whose owning process is CURRENTLY running. The live-process
        // requirement remains the guard against historical registry junk.
        var explicitlyHidden = BuildRegistryItems(
            records.Where(record => record.IsPromoted == 0));

        if (explicitlyHidden.Count > 0)
            return explicitlyHidden;

        return BuildRegistryItems(
            records.Where(record => record.IsPromoted != 1));
    }

    private static List<TrayMetadataRecord> ReadRegistryMetadataRecords()
    {
        const string keyPath = @"Control Panel\NotifyIconSettings";

        using var root = Registry.CurrentUser.OpenSubKey(keyPath);
        if (root is null)
            return [];

        var records = new List<TrayMetadataRecord>();

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            using var entry = root.OpenSubKey(subKeyName);
            if (entry is null)
                continue;

            var executablePath = ExpandExecutablePath(
                entry.GetValue("ExecutablePath")?.ToString());

            if (string.IsNullOrWhiteSpace(executablePath))
                continue;

            var tooltip = entry.GetValue("InitialTooltip")?.ToString()?.Trim();
            var name = !string.IsNullOrWhiteSpace(tooltip)
                ? CleanTooltip(tooltip)
                : Path.GetFileNameWithoutExtension(executablePath);

            if (string.IsNullOrWhiteSpace(name) ||
                IsGlassDockSystemItem(name))
            {
                continue;
            }

            records.Add(
                new TrayMetadataRecord(
                    executablePath,
                    name,
                    ConvertRegistryNullableInt(entry.GetValue("IsPromoted"))));
        }

        return records
            .GroupBy(
                record => record.ExecutablePath + "\n" + record.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string? MatchRegistryExecutable(
        string accessibleName,
        IReadOnlyList<TrayMetadataRecord> metadata)
    {
        var normalized = NormalizeTrayName(accessibleName);
        if (normalized.Length == 0)
            return null;

        var exact = metadata.FirstOrDefault(record =>
            NormalizeTrayName(record.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact.ExecutablePath;

        var contains = metadata.FirstOrDefault(record =>
        {
            var candidate = NormalizeTrayName(record.Name);
            return candidate.Length >= 3 &&
                   (normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains(normalized, StringComparison.OrdinalIgnoreCase));
        });

        return contains?.ExecutablePath;
    }

    private static string NormalizeTrayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value
                .Split(
                    [' ', '\t', '\r', '\n', '-', '–', '—', '|', ','],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
            .Trim();
    }

    private static IReadOnlyList<TrayItem> BuildRegistryItems(
        IEnumerable<TrayMetadataRecord> records)
    {
        var items = new List<TrayItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (items.Count >= 32)
                break;

            var running = IsLikelyRunning(record.ExecutablePath);

            // NotifyIconSettings contains historical entries. Requiring a live
            // matching process prevents old installed versions/apps from flooding
            // GlassDock when Explorer's accessibility tree is unavailable.
            if (!running)
                continue;

            if (!seen.Add(record.ExecutablePath))
                continue;

            var capturedPath = record.ExecutablePath;
            items.Add(
                new TrayItem(
                    record.Name,
                    "Open app",
                    capturedPath,
                    TrayItemSource.Registry));
        }

        return items;
    }

    private static bool IsLikelyRunning(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        try
        {
            var processes = Process.GetProcessesByName(processName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    private sealed record TrayMetadataRecord(
        string ExecutablePath,
        string Name,
        int? IsPromoted);

    private static int ConvertRegistryInt(object? value)
    {
        try
        {
            return value is null
                ? 0
                : Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static int? ConvertRegistryNullableInt(object? value)
    {
        if (value is null)
            return null;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string ExpandExecutablePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());

        // NotifyIconSettings commonly stores paths rooted at Known Folder GUIDs
        // instead of normal drive paths. Resolve the Windows 11 forms observed
        // on this machine before checking the executable.
        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{6D809377-6AF0-444B-8957-A3773F02200E}",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}",
            Environment.GetFolderPath(Environment.SpecialFolder.System));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{F38BF404-1D43-42F2-9305-67DE0B28FC23}",
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        // NotifyIconSettings can include a quoted executable or command-line
        // arguments. Keep only the executable path so icon loading and ShellExecute
        // receive a stable file identity.
        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
                expanded = expanded[1..closingQuote];
        }
        else
        {
            var executableEnd = expanded.IndexOf(
                ".exe",
                StringComparison.OrdinalIgnoreCase);
            if (executableEnd >= 0)
                expanded = expanded[..(executableEnd + 4)];
        }

        return expanded.Trim();
    }

    private static string ReplaceKnownFolderPrefix(
        string value,
        string prefix,
        string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) ||
            !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var remainder = value[prefix.Length..]
            .TrimStart('\\', '/');

        return Path.Combine(
            folder,
            remainder.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CleanTooltip(string value)
    {
        var firstLine = value
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine)
            ? value.Trim()
            : firstLine;
    }

    private static Process? FindMatchingProcess(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return null;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited)
                {
                    process.Dispose();
                    continue;
                }

                // Prefer an exact module path when Windows allows it, but keep
                // the process-name match as a fallback for packaged/protected
                // tray apps whose MainModule cannot be queried.
                try
                {
                    var candidate = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(candidate) &&
                        !string.Equals(
                            candidate,
                            executablePath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        process.Dispose();
                        continue;
                    }
                }
                catch
                {
                    // Access denied is normal for some packaged/elevated apps.
                }

                return process;
            }
            catch
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static bool OpenOrActivate(string executablePath)
    {
        try
        {
            var process = FindMatchingProcess(executablePath);
            if (process is not null)
            {
                try
                {
                    if (process.MainWindowHandle != 0)
                    {
                        ShowWindowAsync(process.MainWindowHandle, 9); // SW_RESTORE
                        SetForegroundWindow(process.MainWindowHandle);
                        return true;
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (!File.Exists(executablePath))
                return false;

            return Process.Start(
                new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true
                }) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> InvokeAsync(TrayItem item)
    {
        if (item.Source == TrayItemSource.Registry)
            return !string.IsNullOrWhiteSpace(item.ExecutablePath) &&
                   OpenOrActivate(item.ExecutablePath);

        var session = TryOpenExistingOverflow();
        try
        {
            if (!session.Opened && TryInvokeOverflowChevron())
            {
                await Task.Delay(70);
                session = TryOpenExistingOverflow();
            }

            if (session.Opened)
                await Task.Delay(50);

            foreach (var hwnd in OverflowAccessibilityRoots())
            {
                if (InvokeNamedAccessibleItem(hwnd, item.Name))
                    return true;
            }
        }
        finally
        {
            session.Dispose();
        }

        // Accessibility elements are only valid while the native overflow is
        // realized. If Windows still refuses the live action, fall back to the
        // owning application when registry metadata gave us a stable path.
        return !string.IsNullOrWhiteSpace(item.ExecutablePath) &&
               OpenOrActivate(item.ExecutablePath);
    }

    private static bool InvokeNamedAccessibleItem(nint hwnd, string expectedName)
    {
        if (hwnd == 0 || string.IsNullOrWhiteSpace(expectedName))
            return false;

        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(
                hwnd,
                ObjIdClient,
                ref iid,
                out var accessible) < 0 ||
            accessible is null)
        {
            return false;
        }

        return InvokeMatching(
            accessible,
            0,
            name => string.Equals(
                NormalizeTrayName(name),
                NormalizeTrayName(expectedName),
                StringComparison.OrdinalIgnoreCase));
    }

    private static void ReadAccessibleRoot(
        nint hwnd,
        List<TrayItem> result,
        HashSet<string> seen)
    {
        if (hwnd == 0)
            return;

        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(
                hwnd,
                ObjIdClient,
                ref iid,
                out var accessible) < 0 ||
            accessible is null)
        {
            return;
        }

        Walk(accessible, 0, result, seen);
    }

    private static IEnumerable<nint> OverflowAccessibilityRoots()
    {
        // Windows 11 modern overflow:
        // TopLevelWindowForOverflowXamlIsland
        var modern = FindWindowW(
            "TopLevelWindowForOverflowXamlIsland",
            null);

        if (modern != 0)
        {
            // XAML accessibility is exposed by the desktop content bridge.
            var bridge = FindWindowExW(
                modern,
                0,
                "Windows.UI.Composition.DesktopWindowContentBridge",
                null);

            if (bridge != 0)
                yield return bridge;

            yield return modern;
        }

        // Older/compatibility implementation.
        var legacy = FindWindowW(
            "NotifyIconOverflowWindow",
            null);

        if (legacy != 0)
            yield return legacy;
    }

    private static IEnumerable<nint> LegacyOverflowRoots()
    {
        var overflow = FindWindowW(
            "NotifyIconOverflowWindow",
            null);

        if (overflow != 0)
            yield return overflow;
    }

    private static OverflowScanSession TryOpenExistingOverflow()
    {
        var modern = FindWindowW(
            "TopLevelWindowForOverflowXamlIsland",
            null);

        if (modern != 0)
            return OverflowScanSession.Open(modern);

        var legacy = FindWindowW(
            "NotifyIconOverflowWindow",
            null);

        return legacy != 0
            ? OverflowScanSession.Open(legacy)
            : default;
    }

    private static bool TryInvokeOverflowChevron()
    {
        var shell = FindWindowW(
            "Shell_TrayWnd",
            null);

        if (shell == 0)
            return false;

        nint trayNotify = 0;

        EnumChildWindows(
            shell,
            (child, _) =>
            {
                var buffer = new StringBuilder(96);
                var length = GetClassNameW(
                    child,
                    buffer,
                    buffer.Capacity);

                if (length > 0 &&
                    buffer.ToString().Equals(
                        "TrayNotifyWnd",
                        StringComparison.Ordinal))
                {
                    trayNotify = child;
                    return false;
                }

                return true;
            },
            0);

        if (trayNotify == 0)
            return false;

        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(
                trayNotify,
                ObjIdClient,
                ref iid,
                out var accessible) < 0 ||
            accessible is null)
        {
            return false;
        }

        return InvokeMatching(
            accessible,
            0,
            name =>
            {
                var value = name.ToLowerInvariant();
                return value.Contains("show hidden icons") ||
                       value.Contains("hidden icon menu") ||
                       value.Contains("overflow chevron") ||
                       value.Contains("notification chevron");
            });
    }

    private static bool InvokeMatching(
        object accessible,
        int depth,
        Func<string, bool> predicate)
    {
        if (depth > 7)
            return false;

        var count = ConvertToInt(
            Get(accessible, "accChildCount"));

        if (count <= 0)
            return false;

        for (var childId = 1; childId <= count; childId++)
        {
            var name = Get(
                    accessible,
                    "accName",
                    childId)
                ?.ToString()
                ?.Trim();

            if (!string.IsNullOrWhiteSpace(name) &&
                predicate(name) &&
                Invoke(accessible, childId))
            {
                return true;
            }

            var child = Get(
                accessible,
                "accChild",
                childId);

            if (child is not null &&
                Marshal.IsComObject(child) &&
                InvokeMatching(child, depth + 1, predicate))
            {
                return true;
            }
        }

        return false;
    }

    private static void Walk(
        object accessible,
        int depth,
        List<TrayItem> result,
        HashSet<string> seen)
    {
        if (depth > 8 ||
            result.Count >= 48)
        {
            return;
        }

        var count = ConvertToInt(
            Get(accessible, "accChildCount"));

        if (count <= 0)
            return;

        for (var childId = 1;
             childId <= count &&
             result.Count < 48;
             childId++)
        {
            var name = Get(
                    accessible,
                    "accName",
                    childId)
                ?.ToString()
                ?.Trim();

            var action = Get(
                    accessible,
                    "accDefaultAction",
                    childId)
                ?.ToString()
                ?.Trim();

            var role = ConvertToInt(
                Get(
                    accessible,
                    "accRole",
                    childId));

            // Modern Windows 11 XAML tray providers don't always report the same
            // MSAA role as the legacy ToolbarWindow32. Prefer actionable named
            // elements and keep the old known-role check as a strong signal.
            var actionable =
                IsActionRole(role) ||
                !string.IsNullOrWhiteSpace(action);

            if (!string.IsNullOrWhiteSpace(name) &&
                actionable &&
                !IsGlassDockSystemItem(name) &&
                seen.Add(name))
            {
                result.Add(
                    new TrayItem(
                        name,
                        action,
                        null,
                        TrayItemSource.Accessibility));
            }

            var child = Get(
                accessible,
                "accChild",
                childId);

            if (child is not null &&
                Marshal.IsComObject(child))
            {
                Walk(
                    child,
                    depth + 1,
                    result,
                    seen);
            }
        }
    }

    private static object? Get(
        object target,
        string member,
        params object[]? args)
    {
        try
        {
            return target
                .GetType()
                .InvokeMember(
                    member,
                    BindingFlags.GetProperty |
                    BindingFlags.Public |
                    BindingFlags.Instance,
                    null,
                    target,
                    args is { Length: > 0 }
                        ? args
                        : null);
        }
        catch (Exception error)
            when (error is COMException or
                  TargetInvocationException or
                  MissingMethodException)
        {
            return null;
        }
    }

    private static bool Invoke(
        object target,
        int childId)
    {
        try
        {
            target
                .GetType()
                .InvokeMember(
                    "accDoDefaultAction",
                    BindingFlags.InvokeMethod |
                    BindingFlags.Public |
                    BindingFlags.Instance,
                    null,
                    target,
                    [childId]);

            return true;
        }
        catch (Exception error)
            when (error is COMException or
                  TargetInvocationException or
                  MissingMethodException)
        {
            return false;
        }
    }

    private static int ConvertToInt(object? value)
    {
        try
        {
            return value is null
                ? 0
                : Convert.ToInt32(value);
        }
        catch (Exception error)
            when (error is FormatException or
                  InvalidCastException or
                  OverflowException)
        {
            return 0;
        }
    }

    private static bool IsActionRole(int role) =>
        role is
            0x2B or // push button
            0x2C or // check button
            0x0C or // menu item
            0x28 or // graphic
            0x1E or // link
            0x2D;   // radio button

    private static bool IsGlassDockSystemItem(
        string name)
    {
        var value =
            name.ToLowerInvariant();

        return
            value.Contains("start") ||
            value.Contains("search") ||
            value.Contains("task view") ||
            value.Contains("system tray") ||
            value.Contains("notification chevron") ||
            value.Contains("overflow chevron") ||
            value.Contains("show hidden icons") ||
            value.Contains("hidden icon menu") ||
            value.Contains("clock") ||
            value.Contains("date and time") ||
            value.Contains("network") ||
            value.Contains("volume") ||
            value.Contains("battery");
    }

    private readonly struct OverflowScanSession : IDisposable
    {
        private readonly nint window;
        private readonly NativeRect originalRect;
        private readonly bool hadRect;
        private readonly bool wasVisible;

        public bool Opened =>
            window != 0;

        private OverflowScanSession(
            nint window,
            NativeRect originalRect,
            bool hadRect,
            bool wasVisible)
        {
            this.window = window;
            this.originalRect = originalRect;
            this.hadRect = hadRect;
            this.wasVisible = wasVisible;
        }

        public static OverflowScanSession Open(
            nint window)
        {
            if (window == 0)
                return default;

            var hadRect =
                GetWindowRect(
                    window,
                    out var rect);

            var wasVisible =
                IsWindowVisible(window);

            // Keep Windows' real overflow UI out of sight. It still becomes
            // realized for accessibility, but GlassDock remains the visible UI.
            SetWindowPos(
                window,
                0,
                -32000,
                -32000,
                0,
                0,
                SwpNoSize |
                SwpNoZOrder |
                SwpNoActivate);

            ShowWindowAsync(
                window,
                SwShowNoActivate);

            return new OverflowScanSession(
                window,
                rect,
                hadRect,
                wasVisible);
        }

        public void Dispose()
        {
            if (window == 0)
                return;

            if (!wasVisible)
                ShowWindowAsync(
                    window,
                    SwHide);

            if (hadRect)
            {
                SetWindowPos(
                    window,
                    0,
                    originalRect.Left,
                    originalRect.Top,
                    0,
                    0,
                    SwpNoSize |
                    SwpNoZOrder |
                    SwpNoActivate);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private delegate bool EnumWindowsProc(
        nint hwnd,
        nint lParam);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        int objectId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)]
        out object? accessible);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(
        string? className,
        string? windowName);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(
        nint parent,
        EnumWindowsProc callback,
        nint lParam);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(
        nint hwnd,
        StringBuilder className,
        int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(
        nint hwnd,
        int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(
        nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(
        nint hwnd,
        out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hwnd,
        nint insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
