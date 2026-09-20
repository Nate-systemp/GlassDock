using System.Text.Json;
using GlassDock.Core.Settings;

namespace GlassDock.Windows.Settings;

/// <summary>Persists GlassDock settings under the current user's LocalAppData.</summary>
public sealed class GlassDockSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip
    };

    private readonly string settingsFilePath;
    private readonly SemaphoreSlim saveGate = new(1, 1);

    public GlassDockSettingsStore(string? settingsFilePath = null)
    {
        settingsFilePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GlassDock",
            "settings.json");

        if (string.IsNullOrWhiteSpace(settingsFilePath))
            throw new ArgumentException("A settings file path is required.", nameof(settingsFilePath));

        this.settingsFilePath = Path.GetFullPath(settingsFilePath);
    }

    public string SettingsFilePath => settingsFilePath;

    public async Task<GlassDockSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(settingsFilePath))
                return new();

            await using var stream = new FileStream(
                settingsFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var settings = await JsonSerializer.DeserializeAsync<GlassDockSettings>(
                stream,
                JsonOptions,
                cancellationToken);

            return GlassDockSettings.Normalize(settings);
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            JsonException or
            NotSupportedException)
        {
            return new();
        }
    }

    public async Task SaveAsync(
        GlassDockSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await saveGate.WaitAsync(cancellationToken);
        string? temporaryPath = null;

        try
        {
            var directory = Path.GetDirectoryName(settingsFilePath)
                ?? throw new InvalidOperationException("The settings path has no parent directory.");
            Directory.CreateDirectory(directory);

            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(settingsFilePath)}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    GlassDockSettings.Normalize(settings),
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsFilePath, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            }

            saveGate.Release();
        }
    }
}
