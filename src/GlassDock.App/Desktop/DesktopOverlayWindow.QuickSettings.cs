using System.Numerics;
using GlassDock.App.Controls;
using GlassDock.App.Rendering;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private QuickSettingsGridButton? quickSettingsButton;
    private QuickSettingsPanel? quickSettingsPanel;
    private UtilityPopupWindow? quickSettingsWindow;
    private UtilityKind? quickSettingsList;
    private bool switchingQuickSettingsList;
    private bool GridUtilities => settingsSession.Current.UtilityDisplayMode == UtilityDisplayMode.Grid;
    internal bool IsQuickSettingsOpen => quickSettingsWindow?.IsOpen == true;
    internal event EventHandler? QuickSettingsVisibilityChanged;
    internal Func<UtilityHudValue>? ReadQuickSettingsBrightness { get; set; }
    internal Action<float>? WriteQuickSettingsBrightness { get; set; }

    private void SynchronizeQuickSettingsMode()
    {
        CloseQuickSettings(immediate: true);
        quickSettingsButton ??= CreateQuickSettingsButton();
        quickSettingsButton.Visibility = GridUtilities ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in utilityButtons.Values)
        {
            button.Visibility = GridUtilities ? Visibility.Collapsed : Visibility.Visible;
            if (GridUtilities) button.Glass.Active = false;
        }
    }

    private QuickSettingsGridButton CreateQuickSettingsButton()
    {
        var button = new QuickSettingsGridButton(); utilityArea.Children.Add(button);
        button.Click += (_, _) =>
        {
            if (quickSettingsWindow?.IsDetailsOpen == true) CloseQuickSettings();
            else OpenQuickSettings();
        };
        return button;
    }

    private void PositionQuickSettingsButton(bool visible)
    {
        if (quickSettingsButton is not { } button) return;
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        button.IsHitTestVisible = visible && state.State == GlassDock.Core.Desktop.DockState.Expanded;
        if (!visible) { CloseQuickSettings(immediate: true); button.Glass.Active = false; return; }
        var top = root.ActualHeight - surface.Margin.Bottom - surface.ActualHeight;
        var dockHeight = Math.Max(0, Math.Min(surface.ActualHeight, root.ActualHeight - top));
        var x = Math.Max(0, root.ActualWidth - 16 - 36);
        var y = Math.Clamp(top + (dockHeight - 36) / 2, 0, Math.Max(0, root.ActualHeight - 36));
        Canvas.SetLeft(button, x); Canvas.SetTop(button, y);
        button.Glass.Bounds = new((float)x, (float)y, 36, 36);
        button.Configure(settingsSession.Current.DockAppearanceMode, settingsSession.Current.UtilityTheme, liquidGlass.BackdropMaterial,
            liquidGlass.Material, settingsSession.Current.ClearCircularHighlightsEnabled);
        button.Glass.Active = settingsSession.Current.DockAppearanceMode.GlassStyle() is not null && (Application.Current as App)?.BasicRendering != true;
        if (quickSettingsWindow is { IsOpen: true } popup)
        {
            popup.Reposition(button.Glass.Bounds);
            RepositionQuickSettingsList();
        }
    }

    private void OpenQuickSettings()
    {
        if (closing || !GridUtilities || quickSettingsButton is not { } source) return;
        if (quickSettingsWindow is { } retained)
        {
            _ = quickSettingsPanel?.ShowCapsulesAsync(true);
            retained.Show(source.Glass.Bounds); source.SetOpen(true); return;
        }
        wifiWindow?.Hide(immediate: true); bluetoothWindow?.Hide(immediate: true);
        DismissWifiSettingsOnly(); DismissBluetoothSettingsOnly();
        try
        {
            foreach (var kind in QuickSettingsPanel.CircleOrder)
                if (utilityButtons.TryGetValue(kind, out var button)) { utilityArea.Children.Remove(button); button.Visibility = Visibility.Visible; }
            var panel = new QuickSettingsPanel(utilityButtons, settingsSession.Current.SelectedUtilities,
                settingsSession.Current.DockAppearanceMode, settingsSession.Current.UtilityTheme, ExpandedDockHeight,
                settingsSession.Appearance.IconSize * DockScale, (float)DockScale,
                () => utilityVolume is { } audio ? UtilityHudValue.Volume(audio.State) : new(0, false, "speaker-x", "Audio unavailable"),
                level => utilityVolume?.SetVolume(level),
                () => ReadQuickSettingsBrightness?.Invoke() ?? new(0, false, "sun", "Brightness unavailable"),
                level => WriteQuickSettingsBrightness?.Invoke(level));
            quickSettingsPanel = panel;
            panel.DismissRequested += (_, _) => CloseQuickSettings();
            panel.BrightnessMonitorRequested += (_, _) => BrightnessMonitorsRequested?.Invoke(this, EventArgs.Empty);
            var popup = new UtilityPopupWindow(WinRT.Interop.WindowNative.GetWindowHandle(this), panel,
                settingsSession.Current.UtilityTheme, "Quick Settings")
            {
                RightAlignToAnchor = true, UseDockOptics = true, AnimateControls = panel.AnimateAsync,
                ScreenshotRequested = () => ScreenshotRequested?.Invoke(),
                RelatedWindowHasForeground = () => wifiWindow?.HasForeground == true || bluetoothWindow?.HasForeground == true
            };
            quickSettingsWindow = popup;
            popup.Hiding += (_, _) => source.SetOpen(false);
            popup.GlassChanged += (_, _) => { RepositionQuickSettingsList(); UpdateLiquidGlass(); };
            popup.Hidden += (_, _) =>
            {
                if (!ReferenceEquals(quickSettingsWindow, popup)) return;
                CloseQuickSettingsLists(); RestoreQuickSettingsButtons();
                quickSettingsWindow = null; quickSettingsPanel = null;
                source.SetOpen(false); previews.EndContextMenu(popup);
                QuickSettingsVisibilityChanged?.Invoke(this, EventArgs.Empty);
                UpdateUtilitySurfaces(); UpdateBackdropBounds();
            };
            popup.ApplyAppearance(settingsSession.Current.DockAppearanceMode, liquidGlass.BackdropMaterial, liquidGlass.Material,
                (float)DockScale, settingsSession.Current.ClearCircularHighlightsEnabled);
            UpdateUtilitySurfaces(); previews.BeginContextMenu(popup);
            popup.Show(source.Glass.Bounds); source.SetOpen(true);
            QuickSettingsVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            StartupDiagnostics.Write("Quick Settings could not open", error);
            CloseQuickSettings(immediate: true); RestoreQuickSettingsButtons();
            if (quickSettingsWindow is null) { quickSettingsPanel?.Dispose(); quickSettingsPanel = null; UpdateUtilitySurfaces(); }
        }
    }

    private void RestoreQuickSettingsButtons()
    {
        quickSettingsPanel?.ReleaseButtons();
        foreach (var button in utilityButtons.Values)
        {
            if (button.FloatingPresentation && button.Parent is Border wrapper)
            { wrapper.Child = null; button.SetFloatingPresentation(false); }
            if (button.Parent is null) utilityArea.Children.Add(button);
            button.Visibility = GridUtilities ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void CloseQuickSettings(bool immediate = false)
    {
        if (quickSettingsWindow is not { } popup) return;
        CloseQuickSettingsLists(); quickSettingsButton?.SetOpen(false); popup.Hide(immediate);
        if (immediate) RestoreQuickSettingsButtons();
    }

    private void CloseQuickSettingsLists()
    {
        switchingQuickSettingsList = true;
        try { wifiWindow?.Hide(immediate: true); bluetoothWindow?.Hide(immediate: true); }
        finally { switchingQuickSettingsList = false; }
        quickSettingsList = null;
        foreach (var button in utilityButtons.Values) button.HighlightSource(false);
    }

    private bool PrepareQuickSettingsList(UtilityKind kind)
    {
        if (quickSettingsWindow?.IsDetailsOpen != true || quickSettingsPanel is null) return false;
        var same = quickSettingsList == kind;
        CloseQuickSettingsLists();
        if (same) { _ = quickSettingsPanel.ShowCapsulesAsync(true); return false; }
        quickSettingsList = kind;
        if (utilityButtons.TryGetValue(kind, out var button)) button.HighlightSource(true);
        _ = quickSettingsPanel.ShowCapsulesAsync(false);
        return true;
    }

    private void RepositionQuickSettingsList()
    {
        if (quickSettingsPanel is not { } panel) return;
        foreach (var popup in new[] { (UtilityPopupWindow?)wifiWindow, bluetoothWindow })
        {
            if (popup is not { IsOpen: true }) continue;
            popup.LeftOfColumn = panel.ColumnBounds;
            if (quickSettingsList is { } kind && utilityButtons.TryGetValue(kind, out var button)) popup.Reposition(button.Glass.Bounds);
        }
    }

    private void QuickSettingsListHidden(UtilityKind kind)
    {
        if (!switchingQuickSettingsList && quickSettingsList == kind) CloseQuickSettings();
    }
}
