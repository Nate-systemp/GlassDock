using GlassDock.Core.Applications;

namespace GlassDock.Core.Desktop;

public readonly record struct HomeCardRect(double X, double Y, double Width, double Height);

/// <summary>One screen of cards, in DIPs. No primary-monitor assumptions or scrolling.</summary>
public sealed record HomeDashboardLayout(double Width, double Height, double ContentScale,
    IReadOnlyList<HomeCardRect> Cards)
{
    public static HomeDashboardLayout Create(double availableWidth, double availableHeight, double spacing = 6)
    {
        var width = Math.Min(960, Math.Max(1, availableWidth));
        var height = Math.Min(620, Math.Max(1, availableHeight));
        // At extreme work-area sizes keep a coherent dashboard rather than clipping controls.
        var fit = Math.Min(1, Math.Min(width / 960, height / 620));
        var w = width / fit;
        var h = height / fit;
        var gap = Math.Clamp(8 + spacing * .5, 8, 18);
        const double inset = 14, search = 62;
        var inner = w - inset * 2;
        var left = (inner - gap) * .56;
        var right = inner - left - gap;
        var top = inset + search + gap;
        var body = h - top - inset;
        var pinHeight = 174d;
        var runningHeight = 134d;
        var quickHeight = (body - gap * 2) * .33;
        var mediaHeight = (body - gap * 2) * .32;
        return new(width, height, fit,
        [
            new(inset, inset, inner, search),
            new(inset, top, left, pinHeight),
            new(inset, top + pinHeight + gap, left, runningHeight),
            new(inset, top + pinHeight + runningHeight + gap * 2, left, 112),
            new(inset + left + gap, top, right, quickHeight),
            new(inset + left + gap, top + quickHeight + gap, right, mediaHeight),
            new(inset + left + gap, top + quickHeight + mediaHeight + gap * 2, right, body - quickHeight - mediaHeight - gap * 2)
        ]);
    }
}

public static class HomeApplications
{
    // Expand stacks into their real members; never create another pin store or identity rule.
    public static IReadOnlyList<DockApplication> Select(IEnumerable<DockApplication> source, bool running) =>
        source.SelectMany(app => app.Stack is null ? new[] { app } : app.StackApps)
            .Where(app => running ? app.IsRunning : app.IsPinned)
            .DistinctBy(app => app.Id, StringComparer.OrdinalIgnoreCase).ToArray();
}
