namespace GlassDock.Windows.Desktop;

public sealed class WifiNetworkSelection
{
    public IReadOnlyList<WifiNetwork> Networks { get; private set; } = Array.Empty<WifiNetwork>();
    public string? SelectedKey { get; private set; }
    public WifiNetwork? Selected => Networks.FirstOrDefault(n => n.Key == SelectedKey);
    public bool Update(IEnumerable<WifiNetwork> available)
    {
        var discovered = available.Where(n => n.Ssid.Length > 0).GroupBy(n => n.Key)
            .Select(g => g.OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).First())
            .OrderByDescending(n => n.Connected).ThenByDescending(n => n.Signal).ToArray();
        var retained = SelectedKey is not null && discovered.Any(n => n.Key == SelectedKey)
            ? Networks.Select(old => discovered.FirstOrDefault(n => n.Key == old.Key)).OfType<WifiNetwork>().ToArray()
            : Array.Empty<WifiNetwork>();
        Networks = retained.Concat(discovered.Where(n => retained.All(old => old.Key != n.Key))).ToArray();
        var disappeared = SelectedKey is not null && Selected is null;
        if (disappeared) SelectedKey = null;
        return disappeared;
    }
    public void Select(string? key) => SelectedKey = Networks.Any(n => n.Key == key) ? key : null;
    public void Clear() { Networks = Array.Empty<WifiNetwork>(); SelectedKey = null; }
}
