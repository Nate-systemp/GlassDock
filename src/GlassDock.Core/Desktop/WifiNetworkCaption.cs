using System.Globalization;

namespace GlassDock.Core.Desktop;

public static class WifiNetworkCaption
{
    public static string Truncate(string ssid)
    {
        var characters = StringInfo.ParseCombiningCharacters(ssid);
        return characters.Length > 14 ? ssid[..characters[14]] + "..." : ssid;
    }
}
