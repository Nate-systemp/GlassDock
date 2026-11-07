using GlassDock.App.Controls;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;

namespace GlassDock.App.Desktop;

internal sealed class WifiUtilityWindow(nint dock, WifiUtilityService wifi, UtilityTheme theme)
    : UtilityPopupWindow(dock, new WifiNetworkPanel(wifi, theme), theme, "Wi-Fi");
