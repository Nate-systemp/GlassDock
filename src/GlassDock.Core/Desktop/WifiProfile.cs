using System.Xml.Linq;
namespace GlassDock.Core.Desktop;

public static class WifiProfile
{
    public static bool Supports(uint authentication, uint cipher) =>
        (authentication == 1 && cipher == 0) || (authentication is 4 or 7 && cipher is 2 or 4) || (authentication == 9 && cipher == 4);
    public static string Create(byte[] ssid, uint authentication, uint cipher, string password, bool autoConnect)
    {
        if (ssid.Length is < 1 or > 32) throw new ArgumentException("The network name is invalid.");
        if (!Supports(authentication, cipher)) throw new ArgumentException("Use Windows settings for this network's security type.");
        var secured = authentication != 1;
        var hexKey = password.Length == 64 && password.All(Uri.IsHexDigit) && authentication != 9;
        if (secured && !hexKey && (password.Length < 8 || password.Length > 63))
            throw new ArgumentException("Enter a password with 8–63 characters.");
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        XElement E(string name, object? value) => new(ns + name, value);
        var auth = authentication switch { 4 => "WPAPSK", 7 => "WPA2PSK", 9 => "WPA3SAE", _ => "open" };
        var encryption = cipher switch { 2 => "TKIP", 4 => "AES", _ => "none" };
        var security = E("security", E("authEncryption", new[] { E("authentication", auth), E("encryption", encryption), E("useOneX", "false") }));
        if (secured) security.Add(E("sharedKey", new[] { E("keyType", hexKey ? "networkKey" : "passPhrase"), E("protected", "false"), E("keyMaterial", password) }));
        return E("WLANProfile", new[] { E("name", "Doky-" + Convert.ToHexString(ssid)),
            E("SSIDConfig", E("SSID", E("hex", Convert.ToHexString(ssid)))), E("connectionType", "ESS"),
            E("connectionMode", autoConnect ? "auto" : "manual"), E("MSM", security) }).ToString(SaveOptions.DisableFormatting);
    }
}
