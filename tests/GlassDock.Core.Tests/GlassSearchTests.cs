using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class GlassSearchTests
{
    private static GlassSearchResult App(string title, string? id = null) =>
        new(id ?? title, title, "Application", [], GlassSearchResultType.Application, "test-target");

    [Fact]
    public void RankingIsExactThenStartThenWordThenSubstring()
    {
        var entries = new[] { App("Recalculator"), App("My Calculator"), App("Calculator tools"), App("Calculator") };
        Assert.Equal(new[] { "Calculator", "Calculator tools", "My Calculator", "Recalculator" },
            GlassSearch.Find(entries, "  CALCULATOR ").Select(item => item.Title));
    }

    [Fact]
    public void KeywordsSupportAliasesAndEmptyOrUnknownQueriesHaveNoResults()
    {
        var entry = new GlassSearchResult("wifi", "Wi-Fi", "Settings", ["wifi", "wireless"], GlassSearchResultType.Setting, "ms-settings:network-wifi");
        Assert.Single(GlassSearch.Find([entry], "WIFI"));
        Assert.Empty(GlassSearch.Find([entry], "   "));
        Assert.Empty(GlassSearch.Find([entry], "zxyunknown"));
    }

    [Fact]
    public void SelectionClampsResetsAndSurvivesIconUpdates()
    {
        var selection = new GlassSearchSelection();
        selection.Move(1);
        Assert.Null(selection.Selected);
        selection.Replace([App("One"), App("Two")]);
        Assert.Equal("One", selection.Selected!.Title);
        selection.Move(-1); Assert.Equal(0, selection.Index);
        selection.Move(1); selection.Move(1); Assert.Equal(1, selection.Index);
        selection.Replace([App("Zero"), App("One"), App("Two")], true);
        Assert.Equal("Two", selection.Selected!.Title);
        selection.Replace([App("Other")], true); Assert.Equal(0, selection.Index);
        selection.Replace([App("One"), App("Two")]); Assert.Equal(0, selection.Index);
        selection.Replace([]); Assert.Null(selection.Selected);
    }

    [Fact]
    public void ResultsAreBoundedAndSuccessiveQueriesNeverChangeHomeState()
    {
        var entries = Enumerable.Range(0, 20).Select(i => App($"App {i:D2}")).ToArray();
        var home = new GlassHomeSession(); home.Open(new(100, 100, 0, false));
        Assert.Equal(8, GlassSearch.Find(entries, "app").Count);
        foreach (var query in new[] { "a", "app", "app 0", "app 01", "", "unknown" })
        {
            GlassSearch.Find(entries, query);
            home.Observe(new(100, 100, 0, true)); // Window grew around the stationary pointer.
            Assert.Equal(GlassHomeState.Compact, home.State);
        }
        home.Observe(new(129, 100, 0, true)); Assert.Equal(GlassHomeState.Compact, home.State);
        home.Observe(new(131, 100, 0, true)); Assert.Equal(GlassHomeState.Expanded, home.State);
    }
}
