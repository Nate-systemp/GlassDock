using Windows.UI.ViewManagement;

namespace GlassDock.Windows.Desktop;

/// <summary>Reads Windows' current Apps/system light or dark preference.</summary>
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
            // Keep the existing Doky default if the shell theme API is unavailable.
            return true;
        }
    }
}
