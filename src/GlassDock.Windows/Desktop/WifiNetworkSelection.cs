namespace GlassDock.Windows.Desktop;

/// <summary>Only one of the visible, real WLAN entries can expose connection controls.</summary>
public sealed class WifiNetworkSelection
{
    public IReadOnlyList<WifiNetwork> Networks { get; private set; } = Array.Empty<WifiNetwork>();
    public string? SelectedKey { get; private set; }
    public WifiNetwork? Selected => Networks.FirstOrDefault(n => n.Key == SelectedKey);
    public bool Update(IEnumerable<WifiNetwork> available)
    {
        Networks = available.Where(n => n.Ssid.Length > 0).GroupBy(n => n.Key)
            .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).First())
            .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).Take(3).ToArray();
        var disappeared = SelectedKey is not null && Selected is null;
        if (disappeared) SelectedKey = null;
        return disappeared;
    }
    public void Select(string? key) => SelectedKey = Networks.Any(n => n.Key == key) ? key : null;
    public void Clear() { Networks = Array.Empty<WifiNetwork>(); SelectedKey = null; }
}
