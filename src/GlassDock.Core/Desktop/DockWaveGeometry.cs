namespace GlassDock.Core.Desktop;

/// <summary>Continuous DIP-space outline shared by glass, rim and native input.</summary>
public static class DockWaveGeometry
{
    public readonly record struct Point(double X, double Y);
    public readonly record struct Segment(Point Control1, Point Control2, Point End, bool IsLine = false);
    public sealed record Outline(Point Start, IReadOnlyList<Segment> Segments);

    public static Outline Create(double left, double top, double width, double height,
        double radius, double center, double halfWidth, double rise, double strength)
    {
        radius = Math.Clamp(radius, 0, Math.Min(width, height) / 2);
        halfWidth = Math.Max(24, Math.Min(halfWidth, Math.Max(24, (width - 2 * radius) * .46)));
        center = Math.Clamp(center, left + radius, left + width - radius);
        strength = Math.Clamp(strength, 0, 1);
        var amplitude = rise * strength * strength * (3 - 2 * strength);
        var right = left + width;
        var bottom = top + height;
        var startX = left + radius;
        var endX = right - radius;
        const double k = .5522847498307936;
        var tangent = radius * k;
        var segments = new List<Segment>();
        (double Y, double Slope) Sample(double x)
        {
            var u = (x - center) / halfWidth;
            if (Math.Abs(u) >= 1) return (top, 0);
            // Compact C2 profile: height, slope and curvature meet the flat edge
            // continuously. No edge-merge threshold or pixel snapping.
            var q = 1 - u * u;
            return (top - amplitude * q * q * q, 6 * amplitude * u * q * q / halfWidth);
        }
        var first = Sample(startX);
        var start = new Point(left, top + radius);
        segments.Add(new(new(left, top + radius - tangent),
            new(startX - tangent, first.Y - first.Slope * tangent), new(startX, first.Y)));
        // Only subdivide the wave footprint, not the long straight dock edges.
        var knots = new SortedSet<double> { startX, endX };
        for (var i = -4; i <= 4; i++)
            knots.Add(Math.Clamp(center + halfWidth * i / 4, startX, endX));
        var previous = startX;
        foreach (var x in knots.Skip(1))
        {
            var a = Sample(previous);
            var b = Sample(x);
            var third = (x - previous) / 3;
            segments.Add(new(new(previous + third, a.Y + third * a.Slope),
                new(x - third, b.Y - third * b.Slope), new(x, b.Y)));
            previous = x;
        }
        var last = Sample(endX);
        segments.Add(new(new(endX + tangent, last.Y + last.Slope * tangent),
            new(right, top + radius - tangent), new(right, top + radius)));
        void Line(double x, double y) => segments.Add(new(default, default, new(x, y), true));
        Line(right, bottom - radius);
        segments.Add(new(new(right, bottom - radius + tangent), new(right - radius + tangent, bottom), new(right - radius, bottom)));
        Line(left + radius, bottom);
        segments.Add(new(new(left + radius - tangent, bottom), new(left, bottom - radius + tangent), new(left, bottom - radius)));
        Line(left, top + radius);
        return new(start, segments);
    }

    public static double Follow(double current, double target, double seconds, double timeConstant) =>
        current + (target - current) * (1 - Math.Exp(-Math.Max(0, seconds) / timeConstant));
}
