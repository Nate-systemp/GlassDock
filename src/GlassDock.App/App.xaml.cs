using Microsoft.UI.Xaml;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Settings;

namespace GlassDock.App;

public partial class App : Application
{
    private Window? window;
    private readonly ApplicationShutdownState shutdown = new();

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
        if (!arguments.Contains("--lab", StringComparer.OrdinalIgnoreCase))
        {
            desktopComposition = new GlassDock.Windows.Desktop.WindowsCompositionSupport();
            var inspect = arguments.Contains("--controls", StringComparer.OrdinalIgnoreCase);
            var settingsStore = new GlassDockSettingsStore();
            var settingsSession = new GlassDockSettingsSession(
                await settingsStore.LoadAsync());
            var dock = new Desktop.DesktopOverlayWindow(
                settingsSession,
                settingsStore,
                shutdown,
                CompleteShutdown,
                inspect);
            window = dock;
            // SetWindowPos shows the non-activating desktop window without taking keyboard focus.
            if (inspect) dock.ShowControls();
            return;
        }
        window = new Window { Title = "GlassDock — Glass Material Laboratory", Content = new Views.GlassLabView() };
        window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
        window.Activate();
    }

    private void CompleteShutdown()
    {
        window = null;
        Exit();
    }
}
