namespace GlassDock.Core.Desktop;

/// <summary>Exact cubic subcurves from the shared upper outline, for the existing partial rim.</summary>
public static class DockEdgeSlice
{
    public static DockWaveGeometry.Outline Upper(DockWaveGeometry.Outline outline, double left, double right)
    {
        var parts = new List<DockWaveGeometry.Segment>();
        var start = outline.Start;
        var first = start;
        foreach (var segment in outline.Segments)
        {
            // The upper edge ends where the right vertical side starts.
            if (segment.IsLine) break;
            if (segment.End.X > left && start.X < right)
            {
                var t0 = Parameter(start, segment, left);
                var t1 = Parameter(start, segment, right);
                var a = Evaluate(start, segment, t0);
                var b = Evaluate(start, segment, t1);
                var da = Derivative(start, segment, t0);
                var db = Derivative(start, segment, t1);
                var step = (t1 - t0) / 3;
                if (parts.Count == 0) first = a;
                parts.Add(new(new(a.X + da.X * step, a.Y + da.Y * step),
                    new(b.X - db.X * step, b.Y - db.Y * step), b));
            }
            start = segment.End;
        }
        return new(first, parts);
    }

    private static double Parameter(DockWaveGeometry.Point a, DockWaveGeometry.Segment s, double x)
    {
        if (x <= a.X) return 0;
        if (x >= s.End.X) return 1;
        double low = 0, high = 1;
        for (var i = 0; i < 32; i++)
        {
            var mid = (low + high) / 2;
            if (Evaluate(a, s, mid).X < x) low = mid; else high = mid;
        }
        return (low + high) / 2;
    }

    private static DockWaveGeometry.Point Evaluate(DockWaveGeometry.Point a, DockWaveGeometry.Segment s, double t)
    {
        var u = 1 - t;
        return new(u * u * u * a.X + 3 * u * u * t * s.Control1.X + 3 * u * t * t * s.Control2.X + t * t * t * s.End.X,
            u * u * u * a.Y + 3 * u * u * t * s.Control1.Y + 3 * u * t * t * s.Control2.Y + t * t * t * s.End.Y);
    }

    private static DockWaveGeometry.Point Derivative(DockWaveGeometry.Point a, DockWaveGeometry.Segment s, double t)
    {
        var u = 1 - t;
        return new(3 * u * u * (s.Control1.X - a.X) + 6 * u * t * (s.Control2.X - s.Control1.X) + 3 * t * t * (s.End.X - s.Control2.X),
            3 * u * u * (s.Control1.Y - a.Y) + 6 * u * t * (s.Control2.Y - s.Control1.Y) + 3 * t * t * (s.End.Y - s.Control2.Y));
    }
}
