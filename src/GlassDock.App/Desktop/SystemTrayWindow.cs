using GlassDock.Core.Settings;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Materials;
using GlassDock.Windows.Desktop;
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

    public SystemTrayWindow(WindowsSystemControlService controls, DockAppearanceSettings appearance, Func<bool>? utilityOwnsPointer = null)
    {
        this.controls = controls;
        Title = "GlassDock Hidden Tray";
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
        var refresh = IconButton("\uE72C", "Refresh tray", Refresh);
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
        presentation = new UtilityPopupPresentation(this, root, backdrop, utilityOwnsPointer);

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
        Closed += (_, _) => { closed = true; host.Dispose(); };
    }

    public void ApplyAppearance(DockAppearanceSettings appearance) => UtilityPopupStyle.Apply(glass, backdrop, appearance);

    public event EventHandler? Dismissed
    {
        add => presentation.Dismissed += value;
        remove => presentation.Dismissed -= value;
    }
    public event EventHandler? Hidden { add => presentation.Hidden += value; remove => presentation.Hidden -= value; }
    public void HideImmediately() => presentation.HideImmediately();
    public void RetargetClosed() => presentation.RetargetClosed();
    public void Present() { presentation.Present(); Refresh(); }
    public void CloseImmediately() => presentation.CloseImmediately();
    public void Dismiss() => presentation.Dismiss();

    public void PositionNear(
        AppWindow owner,
        double scale,
        double anchorX,
        double anchorY,
        double dockTop)
    {
        UtilityPopupStyle.Position(AppWindow, owner, scale, anchorX, dockTop, PanelWidth, PanelHeight);
        presentation.SetTargetWindowGeometry(
            owner.Position.X + anchorX * scale,
            owner.Position.Y + anchorY * scale,
            scale);
        host.InputHeightPixels = presentation.InputHeightPixels;
    }


    public void Refresh()
    {
        if (closed || !presentation.IsVisible) return;
        var items = WindowsTrayAccessibility.ReadItems();
        var nextKeys = items.Select(item => item.Name + "\n" + item.DefaultAction).ToArray();
        if (itemKeys.SequenceEqual(nextKeys) && trayGrid.Children.Count > 0)
        {
            // Preserve focus, pointer capture, and scroll position during periodic refresh.
            var buttons = trayGrid.Children.OfType<Button>().ToArray();
            for (var i = 0; i < buttons.Length && i < items.Count; i++) buttons[i].Tag = items[i];
            return;
        }
        itemKeys = nextKeys;
        trayGrid.Children.Clear();
        trayGrid.RowDefinitions.Clear();
        if (items.Count == 0)
        {
            status.Text = "Explorer did not expose tray items through accessibility. Windows tray settings remain available.";
            trayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var empty = new TextBlock
            {
                Text = "No accessible hidden tray items right now.",
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

        status.Text = $"{items.Count} tray item{(items.Count == 1 ? "" : "s")} · click to invoke";
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var row = index / 4;
            var column = index % 4;
            while (trayGrid.RowDefinitions.Count <= row)
                trayGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tile = TrayButton(item);
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, column);
            trayGrid.Children.Add(tile);
        }
    }

    private Button TrayButton(WindowsTrayAccessibility.TrayItem item)
    {
        var initial = item.Name.Trim().FirstOrDefault();
        var glyph = char.IsLetterOrDigit(initial) ? char.ToUpperInvariant(initial).ToString() : "•";
        var icon = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(9),
            Background = Brush(16),
            BorderThickness = new Thickness(0),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Variable Display"),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(245),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
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
        var stack = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
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
        ToolTipService.SetToolTip(button,
            string.IsNullOrWhiteSpace(item.DefaultAction) ? item.Name : $"{item.Name} · {item.DefaultAction}");
        button.Click += (_, _) =>
        {
            var current = (WindowsTrayAccessibility.TrayItem)button.Tag;
            if (current.Invoke())
                Close();
            else
                status.Text = $"Windows did not accept the action for {current.Name}.";
        };
        button.RightTapped += (_, e) =>
        {
            e.Handled = true;
            controls.OpenTraySettings();
        };
        return button;
    }

    private void UpdateBackdrop() => presentation.UpdateBackdropBounds();

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
    internal sealed record TrayItem(string Name, string? DefaultAction, Func<bool> Invoke);

    private const int ObjIdClient = -4;
    private static readonly Guid IidAccessible = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    public static IReadOnlyList<TrayItem> ReadItems()
    {
        var result = new List<TrayItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hwnd in CandidateTrayWindows())
        {
            if (hwnd == 0)
                continue;

            var iid = IidAccessible;
            if (AccessibleObjectFromWindow(hwnd, ObjIdClient, ref iid, out var accessible) < 0 || accessible is null)
                continue;

            Walk(accessible, 0, result, seen);
        }

        return result
            .Where(item => !IsGlassDockSystemItem(item.Name))
            .Take(20)
            .ToArray();
    }

    private static IEnumerable<nint> CandidateTrayWindows()
    {
        var overflow = FindWindowW("NotifyIconOverflowWindow", null);
        if (overflow != 0) yield return overflow;

        var shell = FindWindowW("Shell_TrayWnd", null);
        if (shell == 0) yield break;

        nint trayNotify = 0;
        EnumChildWindows(shell, (child, _) =>
        {
            var buffer = new StringBuilder(96);
            var length = GetClassNameW(child, buffer, buffer.Capacity);
            if (length > 0 && buffer.ToString().Equals("TrayNotifyWnd", StringComparison.Ordinal))
            {
                trayNotify = child;
                return false;
            }
            return true;
        }, 0);

        if (trayNotify != 0) yield return trayNotify;
    }

    private static void Walk(object accessible, int depth, List<TrayItem> result, HashSet<string> seen)
    {
        if (depth > 5 || result.Count >= 32) return;
        var count = ConvertToInt(Get(accessible, "accChildCount"));
        if (count <= 0) return;

        for (var childId = 1; childId <= count && result.Count < 32; childId++)
        {
            var name = Get(accessible, "accName", childId)?.ToString()?.Trim();
            var action = Get(accessible, "accDefaultAction", childId)?.ToString()?.Trim();
            var role = ConvertToInt(Get(accessible, "accRole", childId));

            if (!string.IsNullOrWhiteSpace(name) && IsActionRole(role) && seen.Add(name))
            {
                var parent = accessible;
                var id = childId;
                result.Add(new TrayItem(name, action, () => Invoke(parent, id)));
            }

            var child = Get(accessible, "accChild", childId);
            if (child is not null && Marshal.IsComObject(child))
                Walk(child, depth + 1, result, seen);
        }
    }

    private static object? Get(object target, string member, params object[]? args)
    {
        try
        {
            return target.GetType().InvokeMember(
                member,
                BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                args is { Length: > 0 } ? args : null);
        }
        catch (Exception error) when (error is COMException or TargetInvocationException or MissingMethodException)
        {
            return null;
        }
    }

    private static bool Invoke(object target, int childId)
    {
        try
        {
            target.GetType().InvokeMember(
                "accDoDefaultAction",
                BindingFlags.InvokeMethod | BindingFlags.Public | BindingFlags.Instance,
                null,
                target,
                [childId]);
            return true;
        }
        catch (Exception error) when (error is COMException or TargetInvocationException or MissingMethodException)
        {
            return false;
        }
    }

    private static int ConvertToInt(object? value)
    {
        try { return value is null ? 0 : Convert.ToInt32(value); }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException) { return 0; }
    }

    private static bool IsActionRole(int role) => role is 0x2B or 0x2C or 0x0C or 0x28 or 0x1E or 0x2D;

    private static bool IsGlassDockSystemItem(string name)
    {
        var value = name.ToLowerInvariant();
        return value.Contains("start") || value.Contains("search") || value.Contains("task view") ||
               value.Contains("system tray") || value.Contains("notification chevron") ||
               value.Contains("show hidden icons") || value.Contains("clock") || value.Contains("date and time") ||
               value.Contains("network") || value.Contains("volume") || value.Contains("battery");
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        int objectId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object? accessible);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint hwnd, StringBuilder className, int maxCount);
}
