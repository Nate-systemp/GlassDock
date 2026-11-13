using GlassDock.App.Controls;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private BluetoothUtilityService? utilityBluetooth;
    private bool ownsBluetooth;
    private UtilityPopupWindow? bluetoothWindow;
    private UtilityButton? bluetoothSettingsOnly;

    private void InitializeBluetooth(BluetoothUtilityService? shared)
    {
        ownsBluetooth = shared is null;
        utilityBluetooth = shared ?? new();
        utilityBluetooth.Changed += BluetoothStateChanged;
    }

    private void BluetoothStateChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (closing || utilityBluetooth is null) return;
        if (utilityBluetooth.State.On) DismissBluetoothSettingsOnly();
        if (utilityButtons.TryGetValue(UtilityKind.Bluetooth, out var button))
        {
            var state = utilityBluetooth.State;
            button.SetState(state.On, state.Available, state.Message);
            _ = button.SetDetailsOpenAsync(ReferenceEquals(bluetoothSettingsOnly, button) ||
                bluetoothWindow is { IsDetailsOpen: true });
        }
        UpdateUtilitySurfaces();
        UpdateLiquidGlass();
    });

    private async Task ToggleBluetoothAsync(UtilityButton button)
    {
        var bluetooth = utilityBluetooth;
        if (bluetooth is null || closing || button.IsMorphing || button.IsToggling || bluetooth.State.Busy) return;
        if (!GridUtilities && (ReferenceEquals(bluetoothSettingsOnly, button) ||
            bluetoothWindow is { IsDetailsOpen: true }))
            await new GlassDock.Windows.Applications.WindowsApplicationLauncher().LaunchTargetAsync("ms-settings:bluetooth");
        else await RunUtilityToggleAsync(button, bluetooth.ToggleAsync, () =>
        {
            var radio = bluetooth.State;
            button.SetState(radio.On, radio.Available, radio.Message);
        });
    }

    private void PositionBluetoothWindow(DockAppearanceMode mode, bool visible)
    {
        if (bluetoothWindow is not { IsOpen: true } window) return;
        window.ApplyAppearance(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, (float)DockScale,
            settingsSession.Current.ClearCircularHighlightsEnabled);
        if (!visible) window.Hide(immediate: true);
        else if (utilityButtons.TryGetValue(UtilityKind.Bluetooth, out var button)) window.Reposition(button.Glass.Bounds);
    }

    private void ShowBluetoothDevices(UtilityButton button)
    {
        if (utilityBluetooth is null || closing) return;
        if (GridUtilities && !PrepareQuickSettingsList(UtilityKind.Bluetooth)) return;
        root.ContextFlyout?.Hide();
        wifiWindow?.Hide(immediate: true); DismissWifiSettingsOnly();
        if (!utilityBluetooth.State.On && !GridUtilities)
        {
            if (ReferenceEquals(bluetoothSettingsOnly, button)) DismissBluetoothSettingsOnly();
            else
            {
                bluetoothSettingsOnly = button;
                previews.BeginContextMenu(button);
                bluetoothWindow?.Hide();
                _ = button.SetDetailsOpenAsync(true);
                SetForegroundWindowForDockKeyboard(WinRT.Interop.WindowNative.GetWindowHandle(this));
                button.Focus(FocusState.Programmatic);
            }
            return;
        }
        DismissBluetoothSettingsOnly();
        if (bluetoothWindow is { IsOpen: true } existing) { existing.Show(button.Glass.Bounds); return; }
        var window = new UtilityPopupWindow(WinRT.Interop.WindowNative.GetWindowHandle(this),
            new BluetoothDevicePanel(utilityBluetooth, settingsSession.Current.UtilityTheme),
            settingsSession.Current.UtilityTheme, "Bluetooth")
        { ScreenshotRequested = () => ScreenshotRequested?.Invoke() };
        bluetoothWindow = window;
        if (GridUtilities)
        {
            window.LeftOfColumn = quickSettingsPanel?.ColumnBounds;
            window.UseQuickSettingsMotion = true;
            window.RelatedWindowHasForeground = () => quickSettingsWindow?.HasForeground == true;
            window.Hiding += (_, _) => { if (!switchingQuickSettingsList && quickSettingsList == UtilityKind.Bluetooth) quickSettingsButton?.SetOpen(false); };
        }
        window.DetailsVisibilityChanged += (_, _) => _ = button.SetDetailsOpenAsync(
            ReferenceEquals(bluetoothSettingsOnly, button) || window.IsDetailsOpen);
        window.ApplyAppearance(settingsSession.Current.DockAppearanceMode, liquidGlass.BackdropMaterial, liquidGlass.Material,
            (float)DockScale, settingsSession.Current.ClearCircularHighlightsEnabled);
        var targetsRevision = -1;
        window.GlassChanged += (_, _) =>
        {
            if (targetsRevision != window.GlassTargetsRevision)
            { targetsRevision = window.GlassTargetsRevision; UpdateUtilitySurfaces(); }
            UpdateLiquidGlass();
        };
        window.Hidden += (_, _) =>
        {
            if (ReferenceEquals(bluetoothWindow, window)) bluetoothWindow = null;
            UpdateUtilitySurfaces(); previews.EndContextMenu(window);
            QuickSettingsListHidden(UtilityKind.Bluetooth);
        };
        previews.BeginContextMenu(window);
        try { window.Show(button.Glass.Bounds); UpdateUtilitySurfaces(); UpdateLiquidGlass(); }
        catch { window.Hide(immediate: true); previews.EndContextMenu(window); throw; }
    }

    private void DismissBluetoothSettingsOnly()
    {
        if (bluetoothSettingsOnly is not { } button) return;
        bluetoothSettingsOnly = null;
        _ = button.SetDetailsOpenAsync(false);
        previews.EndContextMenu(button);
    }

    private void DisposeBluetooth()
    {
        DismissBluetoothSettingsOnly();
        bluetoothWindow?.Hide(immediate: true);
        if (utilityBluetooth is null) return;
        utilityBluetooth.Changed -= BluetoothStateChanged;
        if (ownsBluetooth) utilityBluetooth.Dispose();
    }
}
