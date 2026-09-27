using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow : Window
{
    private readonly GlassDockSettingsSession settingsSession;
    private readonly GlassDockSettingsStore settingsStore;
    private readonly ApplicationShutdownState shutdown;
    private readonly WindowsStartupService startupService = new();
    private readonly bool safeMode;
    private readonly Action restoreTaskbar;
    private readonly Action resumeTaskbar;
    private readonly Action restartGlassDock;
    private readonly Action restartSafeMode;
    private readonly Action exitGlassDock;
    private bool saving;
    private bool closed;
    private bool populating;

    public SettingsWindow(
        GlassDockSettingsSession settingsSession,
        GlassDockSettingsStore settingsStore,
        ApplicationShutdownState shutdown,
        bool safeMode,
        Action restoreTaskbar,
        Action resumeTaskbar,
        Action restartGlassDock,
        Action restartSafeMode,
        Action exitGlassDock)
    {
        this.settingsSession = settingsSession;
        this.settingsStore = settingsStore;
        this.shutdown = shutdown;
        this.safeMode = safeMode;
        this.restoreTaskbar = restoreTaskbar;
        this.resumeTaskbar = resumeTaskbar;
        this.restartGlassDock = restartGlassDock;
        this.restartSafeMode = restartSafeMode;
        this.exitGlassDock = exitGlassDock;

        InitializeComponent();
        UtilityGlassMaterialModeBox.SelectionChanged += UtilityGlassMaterialChanged;
        Title = "GlassDock Settings";
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(940, 700));

        ShowSettingsPage("Appearance");
        var startupEnabled = startupService.IsEnabled();
        Populate(settingsSession.Current with { LaunchAtStartup = startupEnabled });
        PopulateAbout();
        RecoveryModeStatusText.Text = safeMode
            ? "Safe Mode is active. Taskbar suppression, GlassDock Win-key interception, and Hover Wave are disabled for this session."
            : "Normal mode is active.";
        ResumeTaskbarButton.IsEnabled = !safeMode;
        SetStatus("Settings loaded.", success: true);
        Closed += (_, _) => closed = true;
    }

    private void Populate(GlassDockSettings settings)
    {
        populating = true;

        DockAppearanceModeBox.SelectedIndex = (int)settings.DockAppearanceMode;
        UpdateDockAppearanceDescription(settings.DockAppearanceMode);

        DockDisplayModeBox.SelectedIndex = (int)settings.DockDisplayMode;
        BottomMarginBox.Value = settings.BottomMargin;
        AutoHideDelayBox.Value = settings.AutoHideDelayMilliseconds / 1000d;
        PeekDelayBox.Value = settings.PeekDelayMilliseconds / 1000d;
        IconSizeBox.Value = settings.IconSize;
        MagnificationScaleBox.Value = settings.MagnificationScale;
        IconSpacingBox.Value = settings.IconSpacing;
        UtilityGlassMaterialModeBox.SelectedIndex = (int)settings.GlassMaterialMode;
        GlassBlurAmountBox.Value = settings.GlassBlurAmount;
        DockOpacityBox.Value = settings.DockOpacity * 100;
        BorderThicknessBox.Value = settings.BorderThickness;
        BorderOpacityBox.Value = settings.BorderOpacity * 100;
        HoverWaveToggle.IsOn = settings.HoverWaveEnabled;
        LaunchAtStartupToggle.IsOn = settings.LaunchAtStartup;
        populating = false;
    }

    private GlassMaterialMode SelectedGlassMaterialMode =>
        Enum.IsDefined((GlassMaterialMode)UtilityGlassMaterialModeBox.SelectedIndex)
            ? (GlassMaterialMode)UtilityGlassMaterialModeBox.SelectedIndex
            : GlassMaterialMode.Frosted;

    private void UtilityGlassMaterialChanged(object sender, SelectionChangedEventArgs e)
    {
        if (populating)
            return;

        var preset = DockMaterialStylePresets.Create(SelectedGlassMaterialMode);
        GlassBlurAmountBox.Value = preset.BlurAmount;
        DockOpacityBox.Value = preset.Opacity * 100;
        BorderThicknessBox.Value = preset.BorderThickness;
        BorderOpacityBox.Value = preset.BorderOpacity * 100;
    }

    private void PopulateAbout()
    {
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = version is null
            ? "Version unavailable"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";
        SettingsPathText.Text = settingsStore.SettingsFilePath;
    }

    private DockAppearanceMode SelectedDockAppearance =>
        Enum.IsDefined((DockAppearanceMode)DockAppearanceModeBox.SelectedIndex)
            ? (DockAppearanceMode)DockAppearanceModeBox.SelectedIndex
            : DockAppearanceMode.Dark;

    private void DockAppearanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (populating)
            return;

        var mode = SelectedDockAppearance;
        UpdateDockAppearanceDescription(mode);

        // Selecting one of the main-dock glass styles restores that style's
        // material preset into the editable glass controls. The dock itself
        // changes after Apply, matching the rest of this settings window.
        if (mode.GlassStyle() is not { } style)
            return;

        UtilityGlassMaterialModeBox.SelectedIndex = (int)style;
        var preset = DockMaterialStylePresets.Create(style);
        GlassBlurAmountBox.Value = preset.BlurAmount;
        DockOpacityBox.Value = preset.Opacity * 100;
        BorderThicknessBox.Value = preset.BorderThickness;
        BorderOpacityBox.Value = preset.BorderOpacity * 100;
        SetStatus($"{mode} selected. Select Apply to use it on the dock.", success: true);
    }

    private void UpdateDockAppearanceDescription(DockAppearanceMode mode)
    {
        DockAppearanceDescription.Text = mode switch
        {
            DockAppearanceMode.Light => "Soft off-white solid surface with no glass effect.",
            DockAppearanceMode.Dark => "Deep charcoal solid surface with no glass effect.",
            DockAppearanceMode.Frosted => "Soft blurred glass with stronger diffusion and an opaque feel.",
            DockAppearanceMode.Acrylic => "Lighter acrylic-style glass with more background visibility.",
            DockAppearanceMode.Clear => "The clearest glass preset with minimal blur and higher transparency.",
            _ => "Choose the material used by the main dock."
        };
    }

    private DockDisplayMode SelectedDisplayMode =>
        DockDisplayModeBox.SelectedIndex switch
        {
            (int)DockDisplayMode.Pointer => DockDisplayMode.Pointer,
            (int)DockDisplayMode.Foreground => DockDisplayMode.Foreground,
            _ => DockDisplayMode.Primary
        };

    private void NavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
            ShowSettingsPage(page);
    }

    private void ShowSettingsPage(string page)
    {
        AppearancePage.Visibility = page == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        BehaviorPage.Visibility = page == "Behavior" ? Visibility.Visible : Visibility.Collapsed;
        DisplayPage.Visibility = page == "Display" ? Visibility.Visible : Visibility.Collapsed;
        StartupPage.Visibility = page == "Startup" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = page == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPage.Visibility = page == "Recovery" ? Visibility.Visible : Visibility.Collapsed;
        AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;

        SetNavigationState(AppearanceNavButton, page == "Appearance");
        SetNavigationState(BehaviorNavButton, page == "Behavior");
        SetNavigationState(DisplayNavButton, page == "Display");
        SetNavigationState(StartupNavButton, page == "Startup");
        SetNavigationState(AdvancedNavButton, page == "Advanced");
        SetNavigationState(RecoveryNavButton, page == "Recovery");
        SetNavigationState(AboutNavButton, page == "About");
    }

    private static void SetNavigationState(Button button, bool selected)
    {
        button.Background = new SolidColorBrush(selected
            ? ColorHelper.FromArgb(255, 231, 241, 255)
            : Colors.Transparent);
        button.Foreground = new SolidColorBrush(selected
            ? ColorHelper.FromArgb(255, 15, 95, 168)
            : ColorHelper.FromArgb(255, 51, 51, 51));
    }

    private void ResetClick(object sender, RoutedEventArgs e)
    {
        var defaults = settingsSession.CreateDefaultEditableSettings();
        Populate(defaults);
        ShowSettingsPage("Appearance");
        SetStatus("Defaults are ready. Select Apply to save them.", success: true);
    }

    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        if (saving || closed || shutdown.IsRequested)
            return;

        saving = true;
        ApplyButton.IsEnabled = false;
        SetStatus("Saving…", success: true);

        var startupRegistrationChanged = false;
        var previousStartupRegistration = false;

        try
        {
            var current = settingsSession.Current;
            var desiredStartup = LaunchAtStartupToggle.IsOn;
            previousStartupRegistration = startupService.IsEnabled();

            if (previousStartupRegistration != desiredStartup)
            {
                startupService.SetEnabled(desiredStartup);
                startupRegistrationChanged = true;
            }

            var edited = settingsSession.CreateDockSettingsUpdate(
                BottomMarginBox.Value,
                ToMilliseconds(
                    AutoHideDelayBox.Value,
                    current.AutoHideDelayMilliseconds),
                ToMilliseconds(
                    PeekDelayBox.Value,
                    current.PeekDelayMilliseconds),
                SelectedDockAppearance.GlassStyle() ?? SelectedGlassMaterialMode,
                IconSizeBox.Value,
                MagnificationScaleBox.Value,
                IconSpacingBox.Value,
                GlassBlurAmountBox.Value,
                DockOpacityBox.Value / 100,
                BorderThicknessBox.Value,
                BorderOpacityBox.Value / 100,
                SelectedDisplayMode,
                HoverWaveToggle.IsOn) with
            {
                DockAppearanceMode = SelectedDockAppearance,
                LaunchAtStartup = desiredStartup
            };

            await settingsStore.SaveAsync(edited, shutdown.CancellationToken);
            if (closed || shutdown.IsRequested)
                return;

            settingsSession.Replace(edited);
            Populate(settingsSession.Current);
            SetStatus("Saved. Changes are active now.", success: true);
        }
        catch (OperationCanceledException) when (shutdown.IsRequested)
        {
            if (startupRegistrationChanged)
                TryRestoreStartupRegistration(previousStartupRegistration);
        }
        catch (Exception error)
        {
            if (startupRegistrationChanged)
                TryRestoreStartupRegistration(previousStartupRegistration);

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

    private void TryRestoreStartupRegistration(bool enabled)
    {
        try { startupService.SetEnabled(enabled); }
        catch (Exception error) when (
            error is UnauthorizedAccessException or
            IOException or
            InvalidOperationException or
            System.Security.SecurityException)
        {
        }
    }

    private void RestoreTaskbarClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested)
            return;

        restoreTaskbar();
        SetStatus("Windows taskbar restore requested. Suppression is paused for this session.", success: true);
    }

    private void ResumeTaskbarClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested)
            return;

        if (safeMode)
        {
            SetStatus("Safe Mode keeps taskbar suppression disabled.", success: false);
            return;
        }

        resumeTaskbar();
        SetStatus("Taskbar suppression resume requested.", success: true);
    }

    private void RestartClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested)
            return;

        restartGlassDock();
    }

    private void SafeModeClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested)
            return;

        restartSafeMode();
    }

    private async void ResetAppearanceClick(object sender, RoutedEventArgs e)
    {
        if (saving || closed || shutdown.IsRequested)
            return;

        var current = settingsSession.Current;
        var edited = GlassDockSettings.Normalize(current with
        {
            DockAppearanceMode = GlassDockSettings.DefaultDockAppearanceMode,
            GlassMaterialMode = GlassDockSettings.DefaultGlassMaterialMode,
            IconSize = GlassDockSettings.DefaultIconSize,
            MagnificationScale = GlassDockSettings.DefaultMagnificationScale,
            IconSpacing = GlassDockSettings.DefaultIconSpacing,
            GlassBlurAmount = GlassDockSettings.DefaultGlassBlurAmount,
            DockOpacity = GlassDockSettings.DefaultDockOpacity,
            BorderThickness = GlassDockSettings.DefaultBorderThickness,
            BorderOpacity = GlassDockSettings.DefaultBorderOpacity
        });

        await SaveRecoverySettingsAsync(edited, "Appearance reset to defaults.");
    }

    private async void ResetPlacementClick(object sender, RoutedEventArgs e)
    {
        if (saving || closed || shutdown.IsRequested)
            return;

        var current = settingsSession.Current;
        var edited = GlassDockSettings.Normalize(current with
        {
            DockDisplayMode = GlassDockSettings.DefaultDockDisplayMode,
            BottomMargin = GlassDockSettings.DefaultBottomMargin
        });

        await SaveRecoverySettingsAsync(edited, "Dock placement reset to the primary display defaults.");
    }

    private void ExitClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested)
            return;

        exitGlassDock();
    }

    private async Task SaveRecoverySettingsAsync(GlassDockSettings edited, string successMessage)
    {
        saving = true;
        ApplyButton.IsEnabled = false;
        SetStatus("Saving…", success: true);

        try
        {
            await settingsStore.SaveAsync(edited, shutdown.CancellationToken);
            if (closed || shutdown.IsRequested)
                return;

            settingsSession.Replace(edited);
            Populate(settingsSession.Current);
            SetStatus(successMessage, success: true);
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
            ? ColorHelper.FromArgb(255, 55, 120, 98)
            : Colors.IndianRed);
    }
}
