using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using GlassDock.Windows.Desktop;
using Velopack;

namespace GlassDock.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Installer/update hooks must run before XAML and the OnLaunched mutex.
        VelopackApp.Build()
            .OnBeforeUpdateFastCallback(_ =>
                WindowsInputHelperRegistration.StopBestEffort())
            .OnBeforeUninstallFastCallback(_ =>
                WindowsInputHelperRegistration.RemoveBestEffort())
            .Run();

        Rendering.StartupDiagnostics.Write($"Main OS={Environment.OSVersion} base={AppContext.BaseDirectory}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Rendering.StartupDiagnostics.Write("Unhandled managed startup/runtime exception", e.ExceptionObject as Exception);

        // Preserve the initialization performed by WinUI's generated entry point.
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Rendering.StartupDiagnostics.Write("Application.Start entering");
        Application.Start(p =>
        {
            Rendering.StartupDiagnostics.Write("WinUI dispatcher ready");
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
