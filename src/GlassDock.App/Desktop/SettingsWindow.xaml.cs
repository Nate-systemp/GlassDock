using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow : Window
{
    private readonly GlassDockSettingsSession settingsSession;
    private readonly GlassDockSettingsStore settingsStore;
    private readonly ApplicationShutdownState shutdown;
    private bool saving;
    private bool closed;

    public SettingsWindow(
        GlassDockSettingsSession settingsSession,
        GlassDockSettingsStore settingsStore,
        ApplicationShutdownState shutdown)
    {
        this.settingsSession = settingsSession;
        this.settingsStore = settingsStore;
        this.shutdown = shutdown;

        InitializeComponent();
        Title = "GlassDock Settings";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(690, 610));
        Populate(settingsSession.Current);
        SetStatus("Settings loaded.", success: true);
        Closed += (_, _) => closed = true;
    }

    private void Populate(GlassDockSettings settings)
    {
        BottomMarginBox.Value = settings.BottomMargin;
        AutoHideDelayBox.Value = settings.AutoHideDelayMilliseconds / 1000d;
        PeekDelayBox.Value = settings.PeekDelayMilliseconds / 1000d;
    }

    private void ResetClick(object sender, RoutedEventArgs e)
    {
        var defaults = settingsSession.CreateDefaultDockBehavior();
        Populate(defaults);
        SetStatus("Defaults are ready. Select Apply to save them.", success: true);
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (saving || closed || shutdown.IsRequested)
            return;

        saving = true;
        ApplyButton.IsEnabled = false;
        SetStatus("Saving…", success: true);

        try
        {
            var current = settingsSession.Current;
            var edited = settingsSession.CreateDockBehaviorUpdate(
                BottomMarginBox.Value,
                ToMilliseconds(
                    AutoHideDelayBox.Value,
                    current.AutoHideDelayMilliseconds),
                ToMilliseconds(
                    PeekDelayBox.Value,
                    current.PeekDelayMilliseconds));

            await settingsStore.SaveAsync(edited, shutdown.CancellationToken);
            if (closed || shutdown.IsRequested)
                return;

            settingsSession.Replace(edited);
            Populate(settingsSession.Current);
            SetStatus("Saved. Changes are active now.", success: true);
        }
        catch (OperationCanceledException) when (shutdown.IsRequested)
        {
        }
        catch (Exception error)
        {
            if (!closed)
                SetStatus($"Settings could not be saved: {error.Message}", success: false);
        }
        finally
        {
            saving = false;
            if (!closed && !shutdown.IsRequested)
                ApplyButton.IsEnabled = true;
        }
    }

    private static int ToMilliseconds(double seconds, int fallback)
    {
        if (!double.IsFinite(seconds))
            return fallback;

        var milliseconds = seconds * 1000;
        if (milliseconds <= int.MinValue)
            return int.MinValue;
        if (milliseconds >= int.MaxValue)
            return int.MaxValue;
        return (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
    }

    private void SetStatus(string message, bool success)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(success
            ? ColorHelper.FromArgb(255, 151, 208, 192)
            : Colors.IndianRed);
    }
}
