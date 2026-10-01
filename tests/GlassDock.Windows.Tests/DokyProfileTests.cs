using GlassDock.Windows.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DokyProfileTests
{
    [Fact]
    public void SharedLeaseExcludesSecondOwnerAndReleasesWithoutDeletingProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "DokyProfileTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var settings = Path.Combine(directory, "settings.json");
        File.WriteAllText(settings, "preserve");
        try
        {
            using (var first = DokyInstanceLease.TryAcquire(directory))
            {
                Assert.NotNull(first);
                Assert.Null(DokyInstanceLease.TryAcquire(directory));
                Assert.Equal("preserve", File.ReadAllText(settings));
            }
            using var next = DokyInstanceLease.TryAcquire(directory);
            Assert.NotNull(next);
            Assert.Equal("preserve", File.ReadAllText(settings));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SettingsAndPinsUseTheSameOriginalProfile()
    {
        Assert.Equal(Path.Combine(DokyUserData.LocalAppData, "GlassDock"), DokyUserData.DirectoryPath);
        Assert.Equal(Path.Combine(DokyUserData.DirectoryPath, "settings.json"), new GlassDockSettingsStore().SettingsFilePath);
        Assert.Equal(Path.Combine(DokyUserData.DirectoryPath, "dock-pins.json"), DokyUserData.PinsPath);
        Assert.DoesNotContain("LocalCache", DokyUserData.DirectoryPath, StringComparison.OrdinalIgnoreCase);
    }
}
