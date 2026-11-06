using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private readonly Canvas utilityArea = new();
    private readonly Dictionary<UtilityKind, UtilityButton> utilityButtons = [];
    private readonly Dictionary<UtilityKind, double> utilityOffsets = [];
    private WifiUtilityService? utilityWifi;
    private bool ownsWifi;
    private WifiUtilityWindow? wifiWindow;
    private UtilityButton? wifiSettingsOnly;
    private double UtilitySpace => settingsSession.Current.SelectedUtilities.Length * (ExpandedDockHeight + 12 * DockScale);

    private bool IsPointerOverUtility(double x, double y) =>
        utilityArea.Visibility == Visibility.Visible && utilityButtons.Values.Any(button =>
            button.IsHitTestVisible && new global::Windows.Foundation.Rect(
                button.Glass.Bounds.X - 2, button.Glass.Bounds.Y - 2,
                button.Glass.Bounds.Z + 4, button.Glass.Bounds.W + 4).Contains(new(x, y)));

    private void InitializeUtilities(WifiUtilityService? shared)
    {
        ownsWifi = shared is null; utilityWifi = shared ?? new();
        utilityWifi.Changed += WifiStateChanged;
        utilityWifi.NetworksChanged += WifiStateChanged;
        root.Children.Add(utilityArea); Canvas.SetZIndex(utilityArea, 20);
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) DismissWifiSettingsOnly();
        };
        root.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape || wifiSettingsOnly is null) return;
            DismissWifiSettingsOnly(); e.Handled = true;
        }), true);
        root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(root).Position;
            if (wifiSettingsOnly is { } settingsButton && !new global::Windows.Foundation.Rect(settingsButton.Glass.Bounds.X,
                settingsButton.Glass.Bounds.Y, settingsButton.Glass.Bounds.Z, settingsButton.Glass.Bounds.W).Contains(point))
                DismissWifiSettingsOnly();
            if (wifiWindow is not { IsOpen: true }) return;
            if (!utilityButtons.Values.Any(b => new global::Windows.Foundation.Rect(b.Glass.Bounds.X, b.Glass.Bounds.Y,
                b.Glass.Bounds.Z, b.Glass.Bounds.W).Contains(point))) wifiWindow.Hide();
        }), true);
        SynchronizeUtilities();
    }

    private void SynchronizeUtilities()
    {
        if (utilityWifi is null) return;
        var selected = settingsSession.Current.SelectedUtilities;
        foreach (var kind in utilityButtons.Keys.Where(k => !selected.Contains(k)).ToArray())
        {
            if (wifiSettingsOnly?.Definition.Kind == kind) DismissWifiSettingsOnly();
            wifiWindow?.Hide(immediate: true);
            var old = utilityButtons[kind]; utilityButtons.Remove(kind);
            utilityOffsets.Remove(kind);
            old.IsHitTestVisible = false; old.Glass.Active = false;
            FadeUtility(old, 0, () => { utilityArea.Children.Remove(old); old.Dispose(); });
        }
        foreach (var definition in UtilityDefinition.All.Where(d => d.Available && selected.Contains(d.Kind)))
        {
            if (!utilityButtons.TryGetValue(definition.Kind, out var button))
            {
                button = new UtilityButton(definition);
                utilityButtons.Add(definition.Kind, button); utilityArea.Children.Add(button);
                button.PrimaryRequested += async (_, _) =>
                {
                    if (button.Definition.Kind == UtilityKind.Wifi &&
                        (ReferenceEquals(wifiSettingsOnly, button) || wifiWindow is { IsDetailsOpen: true }))
                        await new GlassDock.Windows.Applications.WindowsApplicationLauncher().LaunchTargetAsync("ms-settings:network-wifi");
                    else if (!button.IsMorphing && !utilityWifi.State.Busy) await utilityWifi.ToggleAsync();
                };
                button.DetailsRequested += (_, _) => ShowWifiNetworks(button);
                button.GeometryChanged += (_, _) => { if (!closing) UpdateBackdropBounds(); };
                button.ExpansionChanged += async (_, _) => { if (button.IsExpanded) await RefreshUtilityConnectionAsync(); };
                button.Dragged += (item, delta) =>
                {
                    utilityOffsets[item.Definition.Kind] = utilityOffsets.GetValueOrDefault(item.Definition.Kind) + delta;
                    UpdateBackdropBounds(); UpdateDockWaveOutline();
                };
                button.PointerEntered += async (_, _) => await utilityWifi.RefreshAsync();
                FadeUtility(button, 1);
                _ = utilityWifi.RefreshAsync();
            }
        }
        UpdateUtilitySurfaces();
        WifiStateChanged(this, EventArgs.Empty);
        PositionUtilities();
    }

    private static void FadeUtility(UtilityButton button, double to, Action? complete = null)
    {
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { button.Opacity = to; complete?.Invoke(); return; }
        var animation = new DoubleAnimation { From = 1 - to, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(140)), EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, button); Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard(); story.Children.Add(animation);
        story.Completed += (_, _) => complete?.Invoke(); story.Begin();
    }

    private void PositionUtilities()
    {
        var visible = !closing && !windowManager.FullscreenSuppressed && state.State is DockState.Expanding or DockState.Expanded or DockState.Collapsing;
        utilityArea.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        utilityArea.Opacity = surface.Opacity;
        if (!visible) DismissWifiSettingsOnly();
        if (utilityButtons.Count == 0) return;
        var top = root.ActualHeight - surface.Margin.Bottom - surface.ActualHeight;
        var dock = new HomeCardRect((root.ActualWidth - surface.ActualWidth) / 2, top,
            surface.ActualWidth, Math.Max(1, Math.Min(surface.ActualHeight, root.ActualHeight - top)));
        var mode = settingsSession.Current.DockAppearanceMode;
        var gap = 12 * DockScale;
        var index = 0;
        var minimumLeft = dock.X + dock.Width + gap;
        var buttons = utilityButtons.Values.ToArray();
        foreach (var button in utilityButtons.Values)
        {
            if (button.Definition.Kind == UtilityKind.Wifi) utilityOffsets.Remove(UtilityKind.Wifi);
            var slot = UtilityPlacement.Beside(dock, gap, index);
            var maximumRight = index + 1 < buttons.Length
                ? UtilityPlacement.Beside(dock, gap, index + 1).X + utilityOffsets.GetValueOrDefault(buttons[index + 1].Definition.Kind) - gap
                : root.ActualWidth - 16;
            var rect = UtilityPlacement.WithinRow(slot, slot.X + utilityOffsets.GetValueOrDefault(button.Definition.Kind), minimumLeft, maximumRight);
            utilityOffsets[button.Definition.Kind] = rect.X - slot.X;
            index++;
            if (button.Definition.Kind == UtilityKind.Wifi)
            {
                var diameter = rect.Height * .96;
                rect = new(rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
            }
            button.Configure(rect.Height, Appearance.IconSize, settingsSession.Current.UtilityTheme, mode, maximumRight - rect.X);
            Canvas.SetLeft(button, rect.X); Canvas.SetTop(button, rect.Y);
            button.Glass.Bounds = new((float)rect.X, (float)rect.Y, (float)button.Width, (float)rect.Height);
            minimumLeft = rect.X + button.Width + gap;
            button.Glass.Configure(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, mode == DockAppearanceMode.Clear
                ? settingsSession.Current.ClearCircularHighlightsEnabled : settingsSession.Current.CircularHighlightsEnabled, (float)DockScale);
            button.Glass.Active = visible && mode.GlassStyle() is not null && surface.Opacity > .01 && (Application.Current as App)?.BasicRendering != true;
            button.IsHitTestVisible = state.State == DockState.Expanded;
        }
        if (wifiWindow is { IsOpen: true } window)
        {
            window.ApplyAppearance(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, (float)DockScale, settingsSession.Current.ClearCircularHighlightsEnabled);
            if (!visible) window.Hide(immediate: true);
            else if (utilityButtons.TryGetValue(UtilityKind.Wifi, out var source)) window.Reposition(source.Glass.Bounds);
        }
    }

    private void WifiStateChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (closing || utilityWifi is null) return;
        if (!utilityWifi.State.On && wifiWindow is { IsDetailsOpen: true } &&
            utilityButtons.TryGetValue(UtilityKind.Wifi, out var source)) ShowWifiSettingsOnly(source);
        else if (utilityWifi.State.On) DismissWifiSettingsOnly();
        var connection = utilityWifi.State.On
            ? utilityWifi.CachedNetworks.FirstOrDefault(n => n.Connected)?.Name ?? (utilityWifi.HasNetworkSnapshot ? "Not Connected" : "")
            : utilityWifi.State.Available ? "Wi-Fi Off" : "";
        foreach (var button in utilityButtons.Values)
        {
            button.SetState(utilityWifi.State.On, utilityWifi.State.Available, utilityWifi.State.Message);
            if (button.Definition.Kind == UtilityKind.Wifi) button.SetConnectionText(connection);
        }
        UpdateUtilitySurfaces();
    });

    private async Task RefreshUtilityConnectionAsync()
    {
        var wifi = utilityWifi;
        if (closing || wifi is null) return;
        try
        {
            await wifi.RefreshAsync();
            if (!closing && wifi.State.On) await wifi.ReadNetworksAsync(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is not OutOfMemoryException)
        { StartupDiagnostics.Write("Wi-Fi capsule state refresh failed", error); }
    }

    private void UpdateUtilitySurfaces()
    {
        var surfaces = utilityButtons.Values.Select(b => b.Glass).ToList();
        if (wifiWindow is { IsOpen: true } window) surfaces.AddRange(window.GlassSurfaces);
        liquidGlass.SetUtilities(surfaces);
    }

    private void ShowWifiNetworks(UtilityButton button)
    {
        if (utilityWifi is null || closing) return;
        root.ContextFlyout?.Hide();
        if (!utilityWifi.State.On)
        {
            if (ReferenceEquals(wifiSettingsOnly, button)) DismissWifiSettingsOnly();
            else ShowWifiSettingsOnly(button);
            return;
        }
        DismissWifiSettingsOnly();
        if (wifiWindow is { IsOpen: true } existing) { existing.Show(button.Glass.Bounds); return; }
        var window = new WifiUtilityWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), utilityWifi, settingsSession.Current.UtilityTheme)
        {
            ScreenshotRequested = () => ScreenshotRequested?.Invoke()
        };
        wifiWindow = window;
        window.DetailsVisibilityChanged += (_, _) => _ = button.SetDetailsOpenAsync(
            window.IsDetailsOpen || ReferenceEquals(wifiSettingsOnly, button));
        window.ApplyAppearance(settingsSession.Current.DockAppearanceMode, liquidGlass.BackdropMaterial, liquidGlass.Material, (float)DockScale, settingsSession.Current.ClearCircularHighlightsEnabled);
        var targetsRevision = -1;
        window.GlassChanged += (_, _) =>
        {
            if (targetsRevision != window.GlassTargetsRevision)
            { targetsRevision = window.GlassTargetsRevision; UpdateUtilitySurfaces(); }
            UpdateLiquidGlass();
        };
        window.Hidden += (_, _) =>
        {
            if (ReferenceEquals(wifiWindow, window)) wifiWindow = null;
            UpdateUtilitySurfaces(); previews.EndContextMenu(window);
        };
        // Hold the dock before showing/activating the popup, including its first layout pass.
        previews.BeginContextMenu(window);
        try { window.Show(button.Glass.Bounds); UpdateUtilitySurfaces(); UpdateLiquidGlass(); }
        catch { window.Hide(immediate: true); previews.EndContextMenu(window); throw; }
    }

    private void ShowWifiSettingsOnly(UtilityButton button)
    {
        wifiSettingsOnly = button;
        previews.BeginContextMenu(button);
        wifiWindow?.Hide(immediate: true);
        _ = button.SetDetailsOpenAsync(true);
        // With no popup HWND, the existing dock receives Escape and outside-window deactivation.
        SetForegroundWindowForDockKeyboard(WinRT.Interop.WindowNative.GetWindowHandle(this));
        button.Focus(FocusState.Programmatic);
    }

    private void DismissWifiSettingsOnly()
    {
        if (wifiSettingsOnly is not { } button) return;
        wifiSettingsOnly = null;
        _ = button.SetDetailsOpenAsync(false);
        previews.EndContextMenu(button);
    }

    private void DisposeUtilities()
    {
        DismissWifiSettingsOnly();
        wifiWindow?.Hide(immediate: true);
        if (utilityWifi is not null)
        {
            utilityWifi.Changed -= WifiStateChanged; utilityWifi.NetworksChanged -= WifiStateChanged;
            if (ownsWifi) utilityWifi.Dispose();
        }
        liquidGlass.SetUtilities(Array.Empty<Rendering.UtilityGlassSurface>());
        foreach (var button in utilityButtons.Values) button.Dispose();
        utilityButtons.Clear();
    }
}
