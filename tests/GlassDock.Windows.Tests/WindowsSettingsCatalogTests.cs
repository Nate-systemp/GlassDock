using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class WindowsSettingsCatalogTests
{
    [Theory]
    [InlineData("bluetooth", "ms-settings:bluetooth")]
    [InlineData("display", "ms-settings:display")]
    [InlineData("wifi", "ms-settings:network-wifi")]
    [InlineData("sound", "ms-settings:sound")]
    [InlineData("startup", "ms-settings:startupapps")]
    [InlineData("update", "ms-settings:windowsupdate")]
    [InlineData("battery", "ms-settings:powersleep")]
    [InlineData("storage", "ms-settings:storagesense")]
    [InlineData("apps", "ms-settings:appsfeatures")]
    public void RequestedTermsSelectTheCorrectPage(string query, string target)
    {
        var results = GlassSearch.Find(WindowsSettingsCatalog.Entries.Select(entry => entry.ToSearchResult()), query);
        Assert.Equal(target, results[0].LaunchTarget);
        Assert.Equal(GlassSearchResultType.Setting, results[0].ResultType);
        Assert.Null(results[0].Icon); // UI supplies a consistent system glyph without native handles.
    }
}
