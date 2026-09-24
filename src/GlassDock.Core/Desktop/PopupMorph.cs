using System.Numerics;

namespace GlassDock.Core.Desktop;

public readonly record struct PopupMorphFrame(double ScaleX, double ScaleY, double Travel, double Opacity);

public static class PopupMorph
{
    // Smooth directional timing. Opening and closing intentionally use
    // separate one-way curves rather than a single ease-in-out curve.
    public const double OpenDurationSeconds = .22;
    public const double CloseDurationSeconds = .21;

    public static Matrix4x4 Funnel(double progress, double width, double height,
        double sourceX, double sourceY, double gutter)
    {
        var p = Math.Clamp(progress, 0, 1);
        var phase = 1 - Math.Cbrt(1 - p);
        var unwind = Math.Clamp((phase - .4) / .5, 0, 1);
        var funnel = 1 - unwind * unwind * (3 - 2 * unwind);
        var bodyWidth = Math.Pow(p, .35);
        var bodyHeight = Math.Pow(p, .7);
        var panelWidth = width - 2 * gutter;
        var panelHeight = height - 2 * gutter;
        var bottomWidth = bodyWidth * ((1 - funnel) + funnel * Math.Min(1, 24 / panelWidth));
        var ratio = bodyWidth > 0 ? bottomWidth / bodyWidth : Math.Min(1, 24 / panelWidth);
        var centerX = width / 2;
        var bottom = height - gutter;
        var anchorWeight = (1 - p) + p * funnel;
        var bx = centerX + (sourceX - centerX) * anchorWeight;
        var by = bottom + (sourceY - bottom) * anchorWeight;
        var tx = sourceX + (centerX - sourceX) * p;
        var ty = sourceY + (bottom - sourceY) * p - panelHeight * bodyHeight;
        // Lift the body while the tapered connection is visible. Never overshoot
        // the final host's top gutter, so the stable HWND cannot clip the glass.
        ty = Math.Max(gutter, ty - 16 * Math.Sin(Math.PI * p) * funnel);
        var matrix = Matrix4x4.Identity;
        matrix.M11 = (float)bottomWidth;
        matrix.M21 = (float)((bx - tx * ratio) / panelHeight);
        matrix.M22 = (float)((by - ty * ratio) / panelHeight);
        matrix.M24 = (float)((1 - ratio) / panelHeight);
        matrix.M41 = (float)bx;
        matrix.M42 = (float)by;
        return Matrix4x4.CreateTranslation((float)-centerX, (float)-bottom, 0) * matrix;
    }

    public static Matrix4x4 Transform(double progress, double originX, double originY,
        double offsetX, double offsetY, double panelHeight)
    {
        var frame = Frame(progress);
        // The first/last ~35% of the timeline fans out/in. Perspective narrows
        // the source-facing edge while keeping both wings and all content joined.
        var t = Math.Clamp((progress - .25) / (.725 - .25), 0, 1);
        var fold = .82 * (1 - t * t * (3 - 2 * t));
        // A bottom-hinged forward tilt persists beyond the initial V unfold.
        // Perspective increases continuously with distance above the hinge:
        // the upper rows project most, while the bottom has zero added motion.
        // Resolve to the ordinary panel at rest and retrace on close.
        var p = Math.Clamp(progress, 0, 1);
        var depthEnvelope = 4 * p * (1 - p);
        var depth = .12 * depthEnvelope;
        var tilt = 20 * Math.PI / 180 * depthEnvelope;
        var fan = Matrix4x4.Identity;
        fan.M11 = (float)(frame.ScaleX * (1 - fold));
        fan.M22 = (float)(frame.ScaleY * (1 - fold) * Math.Cos(tilt));
        fan.M24 = (float)((fold + (1 - fold) * depth) / Math.Max(1, panelHeight));
        var origin = new Vector3((float)originX, (float)originY, 0);
        return Matrix4x4.CreateTranslation(-origin) * fan *
            Matrix4x4.CreateTranslation(origin + new Vector3((float)offsetX, (float)offsetY, 0));
    }

    public static double Progress(double elapsed, bool closing, double from)
    {
        var duration = closing ? CloseDurationSeconds : OpenDurationSeconds;
        var t = Math.Clamp(elapsed / duration, 0, 1);

        // Preserve exact endpoints. Trigonometric easing can otherwise leave
        // tiny floating-point residues such as 5.55E-17 at the end of a close,
        // which is visually irrelevant but breaks exact state/reversal tests.
        if (t <= 0)
            return from;
        if (t >= 1)
            return closing ? 0 : 1;

        // Separate directional curves:
        // OPEN  -> sine ease-out: immediate but gentle movement, smooth settle.
        // CLOSE -> sine ease-in: gentle departure, then folds naturally inward.
        // This is deliberately NOT an ease-in-out curve.
        var eased = closing
            ? 1 - Math.Cos(t * Math.PI / 2)
            : Math.Sin(t * Math.PI / 2);

        return from + ((closing ? 0 : 1) - from) * eased;
    }

    public static PopupMorphFrame Frame(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        // Nonuniform growth reads as upward expansion; both axes reach zero at
        // the source. All layers use this one opacity, only at tiny sizes.
        return new(Math.Pow(p, .55), Math.Pow(p, 1.2), 1 - p, Math.Clamp(p / .08, 0, 1));
    }
}
