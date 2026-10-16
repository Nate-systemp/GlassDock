using GlassDock.Core.Settings;

namespace GlassDock.Core.Desktop;

public enum PinnedDockPlacement { BottomAttached, Floating }

/// <summary>Logical dock dimensions; Windows converts the reservation to monitor pixels.</summary>
public readonly record struct DockSizing(double Scale, double IconSize, PinnedDockPlacement Placement, double FloatingGap)
{
    public double Height => Math.Max(68, IconSize + 40) * Scale;
    public double Radius => 34 * Scale;
    public double ClipDepth => Placement == PinnedDockPlacement.BottomAttached ? Radius : 0;
    public double Gap => Placement == PinnedDockPlacement.Floating ? FloatingGap * Scale : 0;
    public double SurfaceBottom => Gap - ClipDepth;
    public double SurfaceHeight => Height + ClipDepth;
    // Hover artwork and badge overflow above the normal content row.
    public double Clearance => Math.Max(8 * Scale, (IconSize * .5 + 8) * Scale - (Height - (IconSize + 16) * Scale) / 2);
    public double ReservedHeight => Height + Gap + Clearance;
    public static int AdditionalReservationPixels(double heightDips, double dpiScale, int existingBottomPixels) =>
        Math.Max(1, (int)Math.Ceiling(heightDips * dpiScale) - Math.Max(0, existingBottomPixels));
}
