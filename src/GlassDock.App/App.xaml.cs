using Microsoft.UI.Xaml;
using System.Diagnostics;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Settings;

namespace GlassDock.App;

public partial class App : Application
{
    private Window? window;
    private readonly ApplicationShutdownState shutdown = new();
    private Mutex? singleInstanceMutex;
    private bool ownsSingleInstance;
    private bool? pendingRestartSafeMode;

    private GlassDock.Windows.Desktop.WindowsCompositionSupport? desktopComposition;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            // Development diagnostics only. No transmission; preserve the fail-fast behavior.
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "development-error.log"), e.Exception.ToString()); }
            catch (IOException) { }
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        var labMode = arguments.Contains("--lab", StringComparer.OrdinalIgnoreCase);

        if (!labMode && !TryOwnSingleInstance())
        {
            Exit();
            return;
        }

        if (!labMode)
        {
            var startupLaunch = arguments.Contains("--startup", StringComparer.OrdinalIgnoreCase);
            if (startupLaunch)
                await Task.Delay(TimeSpan.FromMilliseconds(1500));

            desktopComposition = new GlassDock.Windows.Desktop.WindowsCompositionSupport();
            var inspect = arguments.Contains("--controls", StringComparer.OrdinalIgnoreCase);
            var safeMode = arguments.Contains("--safe-mode", StringComparer.OrdinalIgnoreCase);
            var settingsStore = new GlassDockSettingsStore();
            var settingsSession = new GlassDockSettingsSession(
                await settingsStore.LoadAsync());
            var dock = new Desktop.DesktopOverlayWindow(
                settingsSession,
                settingsStore,
                shutdown,
                CompleteShutdown,
                inspect,
                safeMode,
                PrepareRestart);
            window = dock;
            // SetWindowPos shows the non-activating desktop window without taking keyboard focus.
            if (inspect) dock.ShowControls();
            return;
        }

        window = new Window { Title = "GlassDock — Glass Material Laboratory", Content = new Views.GlassLabView() };
        window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
        window.Activate();
    }

    private bool TryOwnSingleInstance()
    {
        singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            name: @"Local\GlassDock.App.SingleInstance",
            createdNew: out var createdNew);

        ownsSingleInstance = createdNew;
        if (createdNew)
            return true;

        singleInstanceMutex.Dispose();
        singleInstanceMutex = null;
        return false;
    }

    private void PrepareRestart(bool safeMode) =>
        pendingRestartSafeMode = safeMode;

    private void CompleteShutdown()
    {
        window = null;

        if (ownsSingleInstance && singleInstanceMutex is not null)
        {
            try { singleInstanceMutex.ReleaseMutex(); }
            catch (ApplicationException) { }
            ownsSingleInstance = false;
        }

        singleInstanceMutex?.Dispose();
        singleInstanceMutex = null;

        var restartSafeMode = pendingRestartSafeMode;
        pendingRestartSafeMode = null;

        if (restartSafeMode is not null)
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                try
                {
                    var info = new ProcessStartInfo(executable)
                    {
                        UseShellExecute = true
                    };
                    if (restartSafeMode.Value)
                        info.ArgumentList.Add("--safe-mode");
                    Process.Start(info);
                }
                catch (Exception error) when (
                    error is InvalidOperationException or
                    System.ComponentModel.Win32Exception)
                {
                    try
                    {
                        File.WriteAllText(
                            Path.Combine(AppContext.BaseDirectory, "restart-error.log"),
                            error.ToString());
                    }
                    catch (IOException) { }
                }
            }
        }

        Exit();
    }
}
