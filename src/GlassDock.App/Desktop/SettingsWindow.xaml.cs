using GlassDock.Core.Settings;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Settings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using GlassDock.App.Updates;

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
    private ManualUpdateService? updates;
    private bool updateBusy;
    private readonly CancellationTokenSource updateLifetime;

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
        updateLifetime = CancellationTokenSource.CreateLinkedTokenSource(shutdown.CancellationToken);

        InitializeComponent();
        ConfigureNumberFormatting();
        Title = "Doky Settings";
        WindowBranding.Apply(this);
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(940, 700));

        ShowSettingsPage("Appearance");
        var startupEnabled = startupService.IsEnabled();
        Populate(settingsSession.Current with { LaunchAtStartup = startupEnabled });
        PopulateAbout();
        RecoveryModeStatusText.Text = safeMode
            ? "Safe Mode is active. Taskbar suppression, Doky Win-key interception, and Hover Wave are disabled for this session."
            : "Normal mode is active.";
        ResumeTaskbarButton.IsEnabled = !safeMode;
        SetStatus("Settings loaded.", success: true);
        Closed += (_, _) =>
        {
            closed = true;
            updateLifetime.Cancel();
            updateLifetime.Dispose();
        };
    }

    private void ConfigureNumberFormatting()
    {
        // Format text only: NumberBox.Value and persisted doubles retain precision.
        // A rounder is needed because FractionDigits alone is a minimum, not a cap.
        SetNumberFormat(IconSizeBox, 0);
        SetNumberFormat(IconSpacingBox, 0);
        SetNumberFormat(BottomMarginBox, 0);
        SetNumberFormat(GlassBlurAmountBox, 0);
        SetNumberFormat(MagnificationScaleBox, 2);
        SetNumberFormat(BorderThicknessBox, 2);
        SetNumberFormat(DockOpacityBox, 1);
        SetNumberFormat(BorderOpacityBox, 1);
        SetNumberFormat(AutoHideDelayBox, 2);
        SetNumberFormat(PeekDelayBox, 2);
    }

    private static void SetNumberFormat(NumberBox box, int decimals)
    {
        box.NumberFormatter = new global::Windows.Globalization.NumberFormatting.DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = decimals,
            IsGrouped = false,
            NumberRounder = new global::Windows.Globalization.NumberFormatting.IncrementNumberRounder
            {
                Increment = Math.Pow(10, -decimals),
                RoundingAlgorithm = global::Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp
            }
        };
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
        GlassBlurAmountBox.Value = settings.GlassBlurAmount;
        DockOpacityBox.Value = settings.DockOpacity * 100;
        BorderThicknessBox.Value = settings.BorderThickness;
        BorderOpacityBox.Value = settings.BorderOpacity * 100;
        HoverWaveToggle.IsOn = settings.HoverWaveEnabled;
        PinDockToggle.IsOn = settings.PinDock;
        LaunchAtStartupToggle.IsOn = settings.LaunchAtStartup;
        UpdateBehaviorControlAvailability();
        populating = false;
    }

    private async void PinDockToggleToggled(object sender, RoutedEventArgs e)
    {
        if (populating || saving || closed || shutdown.IsRequested)
            return;

        UpdateBehaviorControlAvailability();
        var desired = PinDockToggle.IsOn;
        var edited = GlassDockSettings.Normalize(settingsSession.Current with { PinDock = desired });

        saving = true;
        ApplyButton.IsEnabled = false;
        PinDockToggle.IsEnabled = false;

        try
        {
            await settingsStore.SaveAsync(edited, shutdown.CancellationToken);
            if (closed || shutdown.IsRequested)
                return;

            settingsSession.Replace(edited);
            SetStatus(desired
                ? "Pin Dock enabled. Doky is attaching to the bottom edge."
                : "Pin Dock disabled. Doky is returning to its floating position.",
                success: true);
        }
        catch (OperationCanceledException) when (shutdown.IsRequested)
        {
        }
        catch (Exception error)
        {
            if (!closed)
            {
                populating = true;
                PinDockToggle.IsOn = settingsSession.Current.PinDock;
                populating = false;
                UpdateBehaviorControlAvailability();
                SetStatus($"Pin Dock could not be saved: {error.Message}", success: false);
            }
        }
        finally
        {
            saving = false;
            if (!closed && !shutdown.IsRequested)
            {
                ApplyButton.IsEnabled = true;
                PinDockToggle.IsEnabled = true;
            }
        }
    }

    private void UpdateBehaviorControlAvailability()
    {
        var autoHideAvailable = !PinDockToggle.IsOn;
        AutoHideDelayBox.IsEnabled = autoHideAvailable;
        PeekDelayBox.IsEnabled = autoHideAvailable;
    }

    private void PopulateAbout()
    {
        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = version is null
            ? "Version unavailable"
            : $"Version {version.Major}.{version.Minor}.{version.Build}";
        SettingsPathText.Text = settingsStore.SettingsFilePath;
        try
        {
            updates = new ManualUpdateService();
            if (updates.CurrentVersion is { } installedVersion)
                VersionText.Text = $"Version {installedVersion}";
            else
                VersionText.Text += " (development build)";
            CheckUpdatesButton.IsEnabled = updates.CanUpdate;
            if (!updates.CanUpdate)
                UpdateStatusText.Text = "Updates are available in the installed Doky build. Development builds are not updated.";
        }
        catch (Exception)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Update information is unavailable. Reopen Settings to retry.";
        }
    }

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested || updateBusy || updates is null) return;
        SetUpdateBusy(true, "Checking for updates…");
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateProgress.IsIndeterminate = true;
        try
        {
            await updates.CheckAsync(updateLifetime.Token);
            if (closed || shutdown.IsRequested) return;
            UpdateStatusText.Text = updates.AvailableVersion is { } version
                ? $"Doky {version} is available."
                : "Doky is up to date.";
            InstallUpdateButton.Visibility = updates.AvailableVersion is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (OperationCanceledException) when (closed || shutdown.IsRequested) { }
        catch (TimeoutException)
        {
            if (!closed && !shutdown.IsRequested)
                UpdateStatusText.Text = "GitHub did not respond within 15 seconds. Please try again.";
        }
        catch (Exception)
        {
            if (!closed && !shutdown.IsRequested)
                UpdateStatusText.Text = "Could not check for updates. Check your connection and try again.";
        }
        finally { if (!closed && !shutdown.IsRequested) SetUpdateBusy(false); }
    }

    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (closed || shutdown.IsRequested || updateBusy || updates is null) return;
        SetUpdateBusy(true, "Downloading update…");
        UpdateProgress.IsIndeterminate = false;
        UpdateProgress.Value = 0;
        var progress = new Progress<int>(value =>
        {
            if (closed || shutdown.IsRequested || !updateBusy) return;
            UpdateProgress.Value = Math.Clamp(value, 0, 100);
            UpdateStatusText.Text = $"Downloading update… {Math.Clamp(value, 0, 100)}%";
        });
        try
        {
            await updates.DownloadAsync(value => ((IProgress<int>)progress).Report(value), updateLifetime.Token);
            if (closed || shutdown.IsRequested) return;
            UpdateStatusText.Text = "Installing update… Doky will restart.";
            updates.ApplyAfterExit(safeMode);
            exitGlassDock();
        }
        catch (OperationCanceledException) when (closed || shutdown.IsRequested) { }
        catch (Exception)
        {
            if (!closed && !shutdown.IsRequested)
                UpdateStatusText.Text = "Could not install the update. Try Update again or check for updates.";
        }
        finally { if (!closed && !shutdown.IsRequested) SetUpdateBusy(false); }
    }

    private void SetUpdateBusy(bool busy, string? message = null)
    {
        updateBusy = busy;
        CheckUpdatesButton.IsEnabled = !busy && updates?.CanUpdate == true;
        InstallUpdateButton.IsEnabled = !busy;
        UpdateProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (message is not null) UpdateStatusText.Text = message;
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
                SelectedDockAppearance.GlassStyle() ?? current.GlassMaterialMode,
                IconSizeBox.Value,
                MagnificationScaleBox.Value,
                IconSpacingBox.Value,
                GlassBlurAmountBox.Value,
                DockOpacityBox.Value / 100,
                BorderThicknessBox.Value,
                BorderOpacityBox.Value / 100,
                SelectedDisplayMode,
                HoverWaveToggle.IsOn,
                PinDockToggle.IsOn) with
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
            BottomMargin = GlassDockSettings.DefaultBottomMargin,
            PinDock = GlassDockSettings.DefaultPinDock
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
