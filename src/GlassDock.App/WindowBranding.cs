using Microsoft.UI.Xaml;

namespace GlassDock.App;

internal static class WindowBranding
{
    internal static void Apply(Window window) =>
        window.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Doky.ico"));
}
