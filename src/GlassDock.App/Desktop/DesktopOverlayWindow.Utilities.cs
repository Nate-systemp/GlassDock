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
    private readonly List<UtilityButton> fadingUtilities = [];
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

    private void InitializeUtilities(WifiUtilityService? shared, BluetoothUtilityService? sharedBluetooth)
    {
        ownsWifi = shared is null; utilityWifi = shared ?? new();
        utilityWifi.Changed += WifiStateChanged;
        utilityWifi.NetworksChanged += WifiStateChanged;
        InitializeBluetooth(sharedBluetooth);
        root.Children.Add(utilityArea); Canvas.SetZIndex(utilityArea, 20);
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated)
            { DismissWifiSettingsOnly(); DismissBluetoothSettingsOnly(); }
        };
        root.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) =>
        {
            if (e.Key != global::Windows.System.VirtualKey.Escape || (wifiSettingsOnly is null && bluetoothSettingsOnly is null)) return;
            DismissWifiSettingsOnly(); DismissBluetoothSettingsOnly(); e.Handled = true;
        }), true);
        root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(root).Position;
            if (wifiSettingsOnly is { } settingsButton && !new global::Windows.Foundation.Rect(settingsButton.Glass.Bounds.X,
                settingsButton.Glass.Bounds.Y, settingsButton.Glass.Bounds.Z, settingsButton.Glass.Bounds.W).Contains(point))
                DismissWifiSettingsOnly();
            if (bluetoothSettingsOnly is { } bluetoothButton && !new global::Windows.Foundation.Rect(bluetoothButton.Glass.Bounds.X,
                bluetoothButton.Glass.Bounds.Y, bluetoothButton.Glass.Bounds.Z, bluetoothButton.Glass.Bounds.W).Contains(point))
                DismissBluetoothSettingsOnly();
            if (wifiWindow is not { IsOpen: true } && bluetoothWindow is not { IsOpen: true }) return;
            if (!utilityButtons.Values.Any(b => new global::Windows.Foundation.Rect(b.Glass.Bounds.X, b.Glass.Bounds.Y,
                b.Glass.Bounds.Z, b.Glass.Bounds.W).Contains(point))) { wifiWindow?.Hide(); bluetoothWindow?.Hide(); }
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
            if (bluetoothSettingsOnly?.Definition.Kind == kind) DismissBluetoothSettingsOnly();
            if (kind == UtilityKind.Wifi) wifiWindow?.Hide(immediate: true);
            if (kind == UtilityKind.Bluetooth) bluetoothWindow?.Hide(immediate: true);
            var old = utilityButtons[kind]; utilityButtons.Remove(kind);
            utilityOffsets.Remove(kind);
            old.IsHitTestVisible = false; old.Glass.Active = false;
            fadingUtilities.Add(old);
            FadeUtility(old, 0, () =>
            {
                fadingUtilities.Remove(old); utilityArea.Children.Remove(old); old.Dispose();
                UpdateNativeUtilitySurfaces();
            });
        }
        foreach (var definition in UtilityDefinition.All.Where(d => d.Available && selected.Contains(d.Kind))
            .OrderBy(d => Array.IndexOf(selected, d.Kind)))
        {
            if (!utilityButtons.TryGetValue(definition.Kind, out var button))
            {
                button = new UtilityButton(definition);
                utilityButtons.Add(definition.Kind, button); utilityArea.Children.Add(button);
                button.PrimaryRequested += async (_, _) =>
                {
                    if (button.Definition.Kind == UtilityKind.Bluetooth)
                    { await ToggleBluetoothAsync(button); return; }
                    if (button.Definition.Kind == UtilityKind.Wifi &&
                        (ReferenceEquals(wifiSettingsOnly, button) || wifiWindow is { IsDetailsOpen: true }))
                    {
                        wifiWindow?.Hide();
                        DismissWifiSettingsOnly();
                        await new GlassDock.Windows.Applications.WindowsApplicationLauncher().LaunchTargetAsync("ms-settings:network-wifi");
                    }
                    else if (!button.IsMorphing && !utilityWifi.State.Busy)
                        await RunUtilityToggleAsync(button, utilityWifi.ToggleAsync, () =>
                        {
                            var radio = utilityWifi.State;
                            button.SetState(radio.On, radio.Available, radio.Message);
                        });
                };
                button.DetailsRequested += (_, _) =>
                { if (button.Definition.Kind == UtilityKind.Bluetooth) ShowBluetoothDevices(button); else ShowWifiNetworks(button); };
                button.GeometryChanged += (_, _) => { if (!closing) UpdateBackdropBounds(); };
                button.ExpansionChanged += async (_, _) => { if (button.IsExpanded) await RefreshUtilityConnectionAsync(); };
                button.Dragged += (item, delta) =>
                {
                    utilityOffsets[item.Definition.Kind] = utilityOffsets.GetValueOrDefault(item.Definition.Kind) + delta;
                    UpdateBackdropBounds(); UpdateDockWaveOutline();
                };
                button.PointerEntered += async (_, _) =>
                { if (button.Definition.Kind == UtilityKind.Bluetooth) await utilityBluetooth!.RefreshAsync(); else await utilityWifi.RefreshAsync(); };
                FadeUtility(button, 1);
                if (button.Definition.Kind == UtilityKind.Bluetooth) _ = utilityBluetooth!.RefreshAsync();
                else _ = utilityWifi.RefreshAsync();
            }
        }
        UpdateUtilitySurfaces();
        WifiStateChanged(this, EventArgs.Empty);
        BluetoothStateChanged(this, EventArgs.Empty);
        PositionUtilities();
    }

    private async Task RunUtilityToggleAsync(UtilityButton button, Func<Func<Task>, Task> toggle, Action synchronizeActualState)
    {
        if (closing || !button.TryBeginToggle()) return;
        Task? completion = null;
        Task Finish() => completion ??= button.CompleteToggleAsync(synchronizeActualState);
        try { await toggle(Finish); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { StartupDiagnostics.Write($"{button.Definition.Name} toggle failed", error); }
        finally
        {
            // A skipped native request must also finish the already-started UI transition.
            try { await Finish(); }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                button.CancelToggle();
                if (!closing) synchronizeActualState();
                StartupDiagnostics.Write($"{button.Definition.Name} toggle animation failed", error);
            }
        }
    }

    private void FadeUtility(UtilityButton button, double to, Action? complete = null)
    {
        if (!new global::Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        { button.Opacity = to; complete?.Invoke(); return; }
        var animation = new DoubleAnimation { From = 1 - to, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(140)), EnableDependentAnimation = true };
        Storyboard.SetTarget(animation, button); Storyboard.SetTargetProperty(animation, "Opacity");
        var story = new Storyboard(); story.Children.Add(animation);
        void Frame(object? sender, object args)
        {
            if (closing) { CompositionTarget.Rendering -= Frame; return; }
            UpdateNativeUtilitySurfaces();
        }
        CompositionTarget.Rendering += Frame;
        story.Completed += (_, _) =>
        {
            CompositionTarget.Rendering -= Frame; complete?.Invoke(); UpdateNativeUtilitySurfaces();
        };
        story.Begin();
    }

    private void UpdateNativeUtilitySurfaces()
    {
        var surfaces = new List<(System.Numerics.Vector4 Bounds, float Opacity)>();
        if (!closing && utilityArea.Visibility == Visibility.Visible && desktopBackdrop.SupportsUtilitySurfaces &&
            (Application.Current as App)?.BasicRendering != true)
        {
            foreach (var button in utilityButtons.Values.Concat(fadingUtilities))
            {
                if (!button.IsLoaded || !(button.Glass.UsesNativeDockBackdrop || button.NeedsNativeToggleUnderlay)) continue;
                if (button.Glass.UsesNativeDockBackdrop)
                {
                    button.Glass.Active = false;
                    button.Glass.Element.Visibility = Visibility.Collapsed;
                }
                var bounds = button.GlassPresentationBounds();
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                surfaces.Add((new((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height),
                    (float)(utilityArea.Opacity * button.Opacity)));
            }
        }
        desktopBackdrop.SetUtilitySurfaces(surfaces);
    }

    private void PositionUtilities()
    {
        var visible = !closing && !windowManager.FullscreenSuppressed && state.State is DockState.Expanding or DockState.Expanded or DockState.Collapsing;
        utilityArea.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        utilityArea.Opacity = surface.Opacity;
        if (!visible) { DismissWifiSettingsOnly(); DismissBluetoothSettingsOnly(); }
        if (utilityButtons.Count == 0) { UpdateNativeUtilitySurfaces(); return; }
        var top = root.ActualHeight - surface.Margin.Bottom - surface.ActualHeight;
        var dock = new HomeCardRect((root.ActualWidth - surface.ActualWidth) / 2, top,
            surface.ActualWidth, Math.Max(1, Math.Min(surface.ActualHeight, root.ActualHeight - top)));
        var mode = settingsSession.Current.DockAppearanceMode;
        var gap = 12 * DockScale;
        var index = 0;
        var expansionShift = 0d;
        var minimumLeft = dock.X + dock.Width + gap;
        var buttons = settingsSession.Current.SelectedUtilities.Where(utilityButtons.ContainsKey)
            .Select(kind => utilityButtons[kind]).ToArray();
        foreach (var button in buttons)
        {
            if (button.Definition.Kind is UtilityKind.Wifi or UtilityKind.Bluetooth) utilityOffsets.Remove(button.Definition.Kind);
            var slot = UtilityPlacement.Beside(dock, gap, index);
            slot = slot with { X = slot.X + expansionShift };
            // Reserve the remaining circles, allowing Wi-Fi to push them along its width animation.
            var maximumRight = root.ActualWidth - 16 - (buttons.Length - index - 1) * (dock.Height + gap);
            var rect = UtilityPlacement.WithinRow(slot, slot.X + utilityOffsets.GetValueOrDefault(button.Definition.Kind), minimumLeft, maximumRight);
            utilityOffsets[button.Definition.Kind] = rect.X - slot.X;
            index++;
            if (button.Definition.Kind is UtilityKind.Wifi or UtilityKind.Bluetooth)
            {
                var diameter = rect.Height * .96;
                rect = new(rect.X + (rect.Width - diameter) / 2, rect.Y + (rect.Height - diameter) / 2, diameter, diameter);
            }
            button.Configure(rect.Height, Appearance.IconSize, settingsSession.Current.UtilityTheme, mode, maximumRight - rect.X);
            expansionShift += Math.Max(0, button.Width - rect.Width);
            Canvas.SetLeft(button, rect.X); Canvas.SetTop(button, rect.Y);
            button.Glass.Bounds = new((float)rect.X, (float)rect.Y, (float)button.Width, (float)rect.Height);
            minimumLeft = rect.X + button.Width + gap;
            button.Glass.Configure(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, mode == DockAppearanceMode.Clear
                ? settingsSession.Current.ClearCircularHighlightsEnabled : settingsSession.Current.CircularHighlightsEnabled, (float)DockScale);
            button.Glass.Active = visible && mode.GlassStyle() is not null && surface.Opacity > .01 && (Application.Current as App)?.BasicRendering != true;
            button.IsHitTestVisible = state.State == DockState.Expanded;
        }
        UpdateNativeUtilitySurfaces();
        if (wifiWindow is { IsOpen: true } window)
        {
            window.ApplyAppearance(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, (float)DockScale, settingsSession.Current.ClearCircularHighlightsEnabled);
            if (!visible) window.Hide(immediate: true);
            else if (utilityButtons.TryGetValue(UtilityKind.Wifi, out var source)) window.Reposition(source.Glass.Bounds);
        }
        PositionBluetoothWindow(mode, visible);
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
        if (utilityButtons.TryGetValue(UtilityKind.Wifi, out var button))
        {
            button.SetState(utilityWifi.State.On, utilityWifi.State.Available, utilityWifi.State.Message);
            button.SetConnectionText(connection);
        }
        UpdateUtilitySurfaces();
        UpdateLiquidGlass();
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
        if (bluetoothWindow is { IsOpen: true } bluetooth) surfaces.AddRange(bluetooth.GlassSurfaces);
        liquidGlass.SetUtilities(surfaces);
    }

    private void ShowWifiNetworks(UtilityButton button)
    {
        if (utilityWifi is null || closing) return;
        bluetoothWindow?.Hide(immediate: true); DismissBluetoothSettingsOnly();
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
        DisposeBluetooth();
        DismissWifiSettingsOnly();
        wifiWindow?.Hide(immediate: true);
        if (utilityWifi is not null)
        {
            utilityWifi.Changed -= WifiStateChanged; utilityWifi.NetworksChanged -= WifiStateChanged;
            if (ownsWifi) utilityWifi.Dispose();
        }
        liquidGlass.SetUtilities(Array.Empty<Rendering.UtilityGlassSurface>());
        desktopBackdrop.SetUtilitySurfaces([]);
        foreach (var button in utilityButtons.Values.Concat(fadingUtilities)) button.Dispose();
        fadingUtilities.Clear();
        utilityButtons.Clear();
    }
}
