using Microsoft.UI.Xaml;

namespace GlassDock.App;

public partial class App : Application
{
    private Window? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window { Title = "GlassDock — Phase 0 foundation" };
        window.Activate();
    }
}
