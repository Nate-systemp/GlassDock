using System.Text.Json;
using GlassDock.Core.Desktop;

namespace GlassDock.Windows.Settings;

public sealed class TrayOrderStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    public TrayOrderStore(string? path = null) => this.path = path ?? Path.Combine(DokyUserData.DirectoryPath, "tray-order.json");
    public async Task<TrayOrder> LoadAsync()
    {
        try { return new(JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(path))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    public async Task SaveAsync(string[] order)
    {
        await gate.WaitAsync();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(order));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); gate.Release(); }
    }
}
