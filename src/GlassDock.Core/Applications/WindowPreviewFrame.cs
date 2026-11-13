namespace GlassDock.Core.Applications;

public sealed record WindowPreviewFrame(WindowPreviewLayout Compact, WindowPreviewLayout Expanded,
    IReadOnlyList<ApplicationWindow> Windows, int Page, int Total)
{
    public static WindowPreviewFrame Create(IReadOnlyList<ApplicationWindow> source, int page, double width, double height)
    {
        var snapshot = source.ToArray();
        page = page < 0 || page >= snapshot.Length ? 0 : page;
        var count = snapshot.Length - page;
        var compact = WindowPreviewLayout.Create(count, width, height, false, page > 0);
        var expanded = WindowPreviewLayout.Create(count, width, height, true, page > 0);
        return new(compact, expanded, snapshot.Skip(page).Take(expanded.Capacity).ToArray(), page, snapshot.Length);
    }
}
