using Windows.UI.ViewManagement;

namespace GlassDock.Windows.Desktop;

public static class WindowsSystemTheme
{
    public static bool IsDark()
    {
        try
        {
            var background = new UISettings().GetColorValue(UIColorType.Background);
            var luminance = (background.R * .2126) + (background.G * .7152) + (background.B * .0722);
            return luminance < 128;
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            return true;
        }
    }
}
