using GlassDock.Core.Applications;

namespace GlassDock.Core.Desktop;

/// <summary>Physical-pixel placement of the expanded envelope; phase never affects window position.</summary>
public static class VolumeHudPlacement
{
    public const double Width = 200, ExpandedHeight = 36, CompactHeight = 6, Padding = 20;
    public const double HostWidth = Width + Padding * 2, HostHeight = ExpandedHeight + Padding * 2;

    public static PreviewRect CapsuleBounds(double height, double scale = 1) =>
        new(Padding * scale, (HostHeight - Padding) * scale - height, Width * scale, height);

    public static bool Overlaps(PreviewRect a, PreviewRect b, double gap = 0) =>
        a.X < b.X + b.Width + gap && a.X + a.Width > b.X - gap &&
        a.Y < b.Y + b.Height + gap && a.Y + a.Height > b.Y - gap;

    public static PreviewRect? Position(PreviewRect work, PreviewRect dock,
        IReadOnlyList<PreviewRect> utilities, PreviewRect? sound, double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0 || work.Width <= 0 || work.Height <= 0) return null;
        var w = Width * scale; var h = ExpandedHeight * scale;
        var gap = 12 * scale; var inset = 12 * scale;
        var left = work.X + inset; var right = work.X + work.Width - inset - w;
        var top = work.Y + inset; var bottom = work.Y + work.Height - inset - h;
        if (right < left || bottom < top) return null;
        var obstacles = utilities.Prepend(dock).Where(r => r.Width > 0 && r.Height > 0).ToArray();
        var preferred = sound ?? dock;
        var idealX = preferred.X + preferred.Width / 2 - w / 2;
        var idealY = preferred.Y - gap - h;
        var xs = new List<double> { idealX, left, right };
        var ys = new List<double> { idealY, preferred.Y + preferred.Height + gap, top, bottom };
        foreach (var rect in obstacles)
        {
            xs.Add(rect.X + rect.Width / 2 - w / 2);
            xs.Add(rect.X - w - gap); xs.Add(rect.X + rect.Width + gap);
            ys.Add(rect.Y - h - gap); ys.Add(rect.Y + rect.Height + gap);
            ys.Add(rect.Y + rect.Height / 2 - h / 2);
        }
        PreviewRect? best = null;
        var score = double.PositiveInfinity;
        foreach (var x in xs.Select(x => Math.Clamp(x, left, right)).Distinct())
        foreach (var y in ys.Select(y => Math.Clamp(y, top, bottom)).Distinct())
        {
            var candidate = new PreviewRect(x, y, w, h);
            if (obstacles.Any(rect => Overlaps(candidate, rect, gap))) continue;
            var nextScore = Math.Pow(x - idealX, 2) + Math.Pow(y - idealY, 2);
            if (nextScore >= score) continue;
            score = nextScore; best = candidate;
        }
        if (best is not { } body) return null;
        var local = CapsuleBounds(h, scale);
        return new(Math.Floor(body.X - local.X), Math.Floor(body.Y - local.Y),
            Math.Ceiling(HostWidth * scale), Math.Ceiling(HostHeight * scale));
    }
}
