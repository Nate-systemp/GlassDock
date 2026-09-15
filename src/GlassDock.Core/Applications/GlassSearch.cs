namespace GlassDock.Core.Applications;

public enum GlassSearchResultType { Application, Setting }

public sealed record GlassSearchResult(string StableId, string Title, string Subtitle,
    IReadOnlyList<string> Keywords, GlassSearchResultType ResultType, string LaunchTarget,
    ApplicationIcon? Icon = null);

/// <summary>Pure cached-list matching; no filesystem, UI, or asynchronous work.</summary>
public static class GlassSearch
{
    public static IReadOnlyList<GlassSearchResult> Find(IEnumerable<GlassSearchResult> entries, string query)
    {
        query = query.Trim();
        if (query.Length == 0) return [];

        // Rank first, then collapse duplicate visible applications.
        // This is a second safety net in case multiple discovery sources
        // still produce different StableIds for the same app.
        return entries
            .Select(entry => (Entry: entry, Rank: Rank(entry, query)))
            .Where(item => item.Rank < int.MaxValue)
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Entry.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.StableId, StringComparer.Ordinal)
            .GroupBy(
                item => ResultIdentity(item.Entry),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .Select(item => item.Entry)
            .ToArray();
    }

    private static string ResultIdentity(
        GlassSearchResult entry)
    {
        // Settings should remain distinct by StableId. Applications with
        // the same normalized visible title are presented as one app.
        if (entry.ResultType == GlassSearchResultType.Setting)
            return "setting|" + entry.StableId;

        return "app|" + NormalizeTitle(entry.Title);
    }

    private static string NormalizeTitle(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Rank(GlassSearchResult entry, string query) =>
        Math.Min(Match(entry.Title, query), entry.Keywords.Select(keyword => Match(keyword, query))
            .DefaultIfEmpty(int.MaxValue).Min());

    private static int Match(string text, string query)
    {
        if (text.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        for (var i = 1; i < text.Length; i++)
            if (!char.IsLetterOrDigit(text[i - 1]) && text.AsSpan(i).StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        return text.Contains(query, StringComparison.OrdinalIgnoreCase) ? 3 : int.MaxValue;
    }
}

public sealed class GlassSearchSelection
{
    public IReadOnlyList<GlassSearchResult> Results { get; private set; } = [];
    public int Index { get; private set; } = -1;
    public GlassSearchResult? Selected => Index >= 0 && Index < Results.Count ? Results[Index] : null;

    public void Replace(IReadOnlyList<GlassSearchResult> results, bool preserveSelection = false)
    {
        var id = preserveSelection ? Selected?.StableId : null;
        Results = results;
        Index = id is null ? -1 : results.ToList().FindIndex(result => result.StableId == id);
        if (Index < 0) Index = results.Count == 0 ? -1 : 0;
    }

    public void Move(int direction)
    {
        if (Results.Count > 0) Index = Math.Clamp(Index + direction, 0, Results.Count - 1);
    }
}
