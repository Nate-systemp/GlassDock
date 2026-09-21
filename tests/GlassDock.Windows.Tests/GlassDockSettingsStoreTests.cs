using System.Text.Json;
using GlassDock.Core.Settings;
using GlassDock.Windows.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class GlassDockSettingsStoreTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"IconSize\":\"obsolete\"}")]
    [InlineData("{\"GlassMaterialMode\":\"unsupported-old-value\"}")]
    public async Task Incompatible_settings_fall_back_without_rewriting_user_data(string json)
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        await File.WriteAllTextAsync(store.SettingsFilePath, json);

        Assert.Equal(new GlassDockSettings(), await store.LoadAsync());
        Assert.Equal(json, await File.ReadAllTextAsync(store.SettingsFilePath));
    }

    [Fact]
    public async Task Missing_file_returns_defaults_without_writing_to_LocalAppData()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();

        var settings = await store.LoadAsync();

        Assert.Equal(new GlassDockSettings(), settings);
        Assert.False(File.Exists(store.SettingsFilePath));
    }

    [Fact]
    public async Task Save_creates_directory_and_round_trips_human_readable_json()
    {
        using var location = new TemporarySettingsDirectory(createDirectory: false);
        var store = location.CreateStore();
        var expected = new GlassDockSettings
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            BottomMargin = 42,
            AutoHideDelayMilliseconds = 2500,
            PeekDelayMilliseconds = 4500,
            IconSize = 34,
            MagnificationScale = 1.42,
            IconSpacing = 11,
            GlassBlurAmount = 38,
            DockOpacity = 0.66,
            BorderThickness = 1.4,
            BorderOpacity = 0.52,
            GlassMaterialMode = GlassMaterialMode.Acrylic
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();
        var json = await File.ReadAllTextAsync(store.SettingsFilePath);

        Assert.Equal(expected, actual);
        Assert.Contains(Environment.NewLine, json);
        Assert.Contains("\"SchemaVersion\": 1", json);
        Assert.Single(Directory.EnumerateFiles(location.DirectoryPath));
    }

    [Fact]
    public async Task Invalid_json_falls_back_without_modifying_the_corrupt_file()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        const string corrupt = "{ definitely-not-json";
        await File.WriteAllTextAsync(store.SettingsFilePath, corrupt);

        var settings = await store.LoadAsync();

        Assert.Equal(new GlassDockSettings(), settings);
        Assert.Equal(corrupt, await File.ReadAllTextAsync(store.SettingsFilePath));
    }

    [Fact]
    public async Task Loaded_values_are_normalized()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        await File.WriteAllTextAsync(store.SettingsFilePath, """
            {
              "SchemaVersion": 999,
              "BottomMargin": -100,
              "AutoHideDelayMilliseconds": 999999,
              "PeekDelayMilliseconds": -50,
              "GlassMaterialMode": 99
            }
            """);

        var settings = await store.LoadAsync();

        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(GlassDockSettings.MinimumBottomMargin, settings.BottomMargin);
        Assert.Equal(GlassDockSettings.MaximumAutoHideDelayMilliseconds, settings.AutoHideDelayMilliseconds);
        Assert.Equal(GlassDockSettings.MinimumPeekDelayMilliseconds, settings.PeekDelayMilliseconds);
        Assert.Equal(GlassMaterialMode.Frosted, settings.GlassMaterialMode);
    }

    [Fact]
    public async Task Unknown_json_properties_are_ignored()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        await File.WriteAllTextAsync(store.SettingsFilePath, """
            {
              "SchemaVersion": 1,
              "LaunchAtStartup": true,
              "FutureSetting": { "Nested": "value" }
            }
            """);

        var settings = await store.LoadAsync();

        Assert.True(settings.LaunchAtStartup);
        Assert.Equal(GlassDockSettings.DefaultBottomMargin, settings.BottomMargin);
    }

    [Fact]
    public async Task Older_json_without_appearance_fields_loads_current_defaults()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        await File.WriteAllTextAsync(store.SettingsFilePath, """
            {
              "SchemaVersion": 1,
              "LaunchAtStartup": true,
              "SuppressWindowsTaskbar": false,
              "BottomMargin": 40,
              "AutoHideDelayMilliseconds": 2500,
              "PeekDelayMilliseconds": 5000
            }
            """);

        var settings = await store.LoadAsync();

        Assert.True(settings.LaunchAtStartup);
        Assert.False(settings.SuppressWindowsTaskbar);
        Assert.Equal(40, settings.BottomMargin);
        Assert.Equal(GlassDockSettings.DefaultIconSize, settings.IconSize);
        Assert.Equal(GlassDockSettings.DefaultMagnificationScale, settings.MagnificationScale);
        Assert.Equal(GlassDockSettings.DefaultIconSpacing, settings.IconSpacing);
        Assert.Equal(GlassDockSettings.DefaultGlassBlurAmount, settings.GlassBlurAmount);
        Assert.Equal(GlassDockSettings.DefaultDockOpacity, settings.DockOpacity);
        Assert.Equal(GlassDockSettings.DefaultBorderThickness, settings.BorderThickness);
        Assert.Equal(GlassDockSettings.DefaultBorderOpacity, settings.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Frosted, settings.GlassMaterialMode);
    }

    [Fact]
    public async Task Repeated_saves_replace_the_complete_file_without_temporary_files()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        await store.SaveAsync(new() { BottomMargin = 32, LaunchAtStartup = true });
        await store.SaveAsync(new()
        {
            BottomMargin = 64,
            LaunchAtStartup = false,
            PeekDelayMilliseconds = 7000
        });

        var settings = await store.LoadAsync();
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(store.SettingsFilePath));

        Assert.Equal(64, settings.BottomMargin);
        Assert.False(settings.LaunchAtStartup);
        Assert.Equal(7000, settings.PeekDelayMilliseconds);
        Assert.Equal(GlassDockSettings.CurrentSchemaVersion, json.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Single(Directory.EnumerateFiles(location.DirectoryPath));
    }

    [Fact]
    public async Task Saving_safe_dock_edits_preserves_unexposed_preferences()
    {
        using var location = new TemporarySettingsDirectory();
        var store = location.CreateStore();
        var session = new GlassDockSettingsSession(new()
        {
            LaunchAtStartup = true,
            SuppressWindowsTaskbar = false,
            IconSize = 36,
            MagnificationScale = 1.5,
            IconSpacing = 12,
            GlassBlurAmount = 44,
            DockOpacity = 0.58,
            BorderThickness = 1.7,
            BorderOpacity = 0.43,
            GlassMaterialMode = GlassMaterialMode.Clear
        });

        var edited = session.CreateDockBehaviorUpdate(36, 2800, 5200);
        await store.SaveAsync(edited);
        var loaded = await store.LoadAsync();

        Assert.True(loaded.LaunchAtStartup);
        Assert.False(loaded.SuppressWindowsTaskbar);
        Assert.Equal(36, loaded.BottomMargin);
        Assert.Equal(2800, loaded.AutoHideDelayMilliseconds);
        Assert.Equal(5200, loaded.PeekDelayMilliseconds);
        Assert.Equal(36, loaded.IconSize);
        Assert.Equal(1.5, loaded.MagnificationScale);
        Assert.Equal(12, loaded.IconSpacing);
        Assert.Equal(44, loaded.GlassBlurAmount);
        Assert.Equal(0.58, loaded.DockOpacity);
        Assert.Equal(1.7, loaded.BorderThickness);
        Assert.Equal(0.43, loaded.BorderOpacity);
        Assert.Equal(GlassMaterialMode.Clear, loaded.GlassMaterialMode);
    }

    private sealed class TemporarySettingsDirectory : IDisposable
    {
        public TemporarySettingsDirectory(bool createDirectory = true)
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                "GlassDock.Tests",
                Guid.NewGuid().ToString("N"));
            if (createDirectory)
                Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public GlassDockSettingsStore CreateStore() =>
            new(Path.Combine(DirectoryPath, "settings.json"));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                    Directory.Delete(DirectoryPath, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
