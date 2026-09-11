using Microsoft.UI.Xaml;

namespace GlassDock.App;

public partial class App : Application
{
    private Window? window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = new Window
        {
            Title = "GlassDock — Glass Material Laboratory",
            Content = new Views.GlassLabView()
        };
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 900));
        window.Activate();
    }
}
