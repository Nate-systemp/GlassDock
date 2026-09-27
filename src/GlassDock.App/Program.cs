using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace GlassDock.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Installer/update hooks must run before XAML and the OnLaunched mutex.
        VelopackApp.Build().Run();

        // Preserve the initialization performed by WinUI's generated entry point.
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
