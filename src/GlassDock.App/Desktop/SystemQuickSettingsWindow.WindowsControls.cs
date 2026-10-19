using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GlassDock.App.Desktop;

internal sealed partial class SystemQuickSettingsWindow
{
    private WindowsQuickControlState windowsControlState = new(null, null, null);
    private bool windowsControlBusy;
    private int windowsControlRevision;

    private async Task ToggleWindowsControlAsync(WindowsQuickControl kind)
    {
        if (closed || windowsControlBusy || radioBusy || hotspotBusy) return;
        windowsControlBusy = true;
        windowsControlRevision++;
        SetWindowsControlsEnabled(false);
        try
        {
            var result = await controls.QuickControls.ToggleAsync(kind, lifetime.Token);
            if (closed) return;
            windowsControlState = kind switch
            {
                WindowsQuickControl.Focus => windowsControlState with { Focus = result.State },
                WindowsQuickControl.Airplane => windowsControlState with { Airplane = result.State },
                _ => windowsControlState with { NightLight = result.State }
            };
            UpdateWindowsControlStates();
            UpdateShortcutStates();
            // Never bring a dismissed popup back or steal focus from an unrelated app.
            if (result.CanReturnToCaller && presentation.IsVisible && SystemControlAutomation.IsSettingsForeground()) Activate();
            if (!result.Succeeded)
            {
                Action settings = kind switch
                {
                    WindowsQuickControl.Focus => controls.OpenFocusSettings,
                    WindowsQuickControl.Airplane => controls.OpenAirplaneSettings,
                    _ => controls.OpenNightLightSettings
                };
                ShowInformation(kind == WindowsQuickControl.NightLight ? "Night Light" : kind.ToString(), result.Message, settings);
            }
        }
        finally
        {
            windowsControlBusy = false;
            windowsControlRevision++;
            if (!closed)
            {
                SetWindowsControlsEnabled(true);
                UpdateWindowsControlStates();
                nextHardwareRefresh = DateTime.MinValue;
            }
        }
    }

    private void SetWindowsControlsEnabled(bool enabled)
    {
        foreach (var view in tileLayouts.Values.Where(v => v.Label.Text is "Focus" or "Night Light")) view.Button.IsEnabled = enabled;
        if (shortcutVisuals.TryGetValue("airplane", out var airplane)) airplane.Button.IsEnabled = enabled;
    }

    private void UpdateWindowsControlStates()
    {
        focusDetail.Text = windowsControlState.Focus is { } focusOn ? focusOn ? "On" : "Off" : "Unavailable";
        foreach (var (frame, view) in tileLayouts)
        {
            if (view.Label.Text is not ("Focus" or "Night Light")) continue;
            var isFocus = view.Label.Text == "Focus";
            var state = isFocus ? windowsControlState.Focus : windowsControlState.NightLight;
            var status = state is { } on ? on ? "On" : "Off" : "Status unavailable";
            ToolTipService.SetToolTip(view.Button, $"{view.Label.Text}: {status}. Toggle using Windows controls");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(view.Button, status);
            if (!detached) continue;
            UpdateControlIcon(view.Button, isFocus ? "focus" : "night-light");
            depth.SetActive(frame, state == true);
            var clearActive = currentMode == DockAppearanceMode.Clear && state == true;
            view.Label.Foreground = clearActive ? activeTitle : theme.Primary;
            view.Detail.Foreground = clearActive ? activeSubtitle : theme.Secondary;
            if (frame.Child is Grid row && row.Children.Count > 2 && row.Children[2] is Button more && more.Content is Image arrow)
                icons.Set(arrow, "expand", darkInk: clearActive);
        }
    }
}
