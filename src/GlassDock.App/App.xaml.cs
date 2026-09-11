using Microsoft.UI.Xaml;

namespace GlassDock.App;

public partial class App : Application
{
    private Window? window;

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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (!arguments.Contains("--lab", StringComparer.OrdinalIgnoreCase))
        {
            desktopComposition = new GlassDock.Windows.Desktop.WindowsCompositionSupport();
            var inspect = arguments.Contains("--controls", StringComparer.OrdinalIgnoreCase);
            var dock = new Desktop.DesktopOverlayWindow(inspect);
            window = dock;
            // SetWindowPos shows the non-activating desktop window without taking keyboard focus.
            if (inspect) dock.ShowControls();
            return;
        }
        window = new Window { Title = "GlassDock — Glass Material Laboratory", Content = new Views.GlassLabView() };
        window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(1320, 900));
        window.Activate();
    }
}
