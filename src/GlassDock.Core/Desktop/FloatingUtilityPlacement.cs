namespace GlassDock.Core.Desktop;

public static class FloatingUtilityPlacement
{
    /// <summary>Fixed native HWND width for Split Utilities in DIPs.
    /// Animation changes the GLASS geometry, never this native window width.</summary>
    public static double SplitHostWidth(double expandedPillWidth, double gutter) =>
        Math.Max(0, expandedPillWidth) + Math.Max(0, gutter) * 2;

    /// <summary>Right-anchored reveal of the controls, with no change to
    /// their measured width or the time label's horizontal coordinate.</summary>
    public static (double Left, double Width) SplitRevealClip(double contentWidth, double reveal)
    {
        var width = Math.Max(0, contentWidth);
        var visible = width * Math.Clamp(double.IsFinite(reveal) ? reveal : 0, 0, 1);
        return (width - visible, visible);
    }

    /// <summary>Opaque input region width in native physical pixels. Only
    /// the compact clock is hit-testable when the left controls are hidden.</summary>
    public static (int Left, int Width) SplitInputRegion(int hostWidth, int activeWidth)
    {
        hostWidth = Math.Max(1, hostWidth);
        activeWidth = Math.Clamp(activeWidth, 1, hostWidth);
        return (hostWidth - activeWidth, activeWidth);
    }

    /// <summary>Monotonic, zero-velocity-at-endpoints UI width easing.</summary>
    public static double EaseSplitExpansion(double progress)
    {
        if (!double.IsFinite(progress)) return 0;
        var t = Math.Clamp(progress, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Test the stable time-only trigger bounds, rather than transient expanded
    /// HWND/button bounds that shift while the native window is resized.
    /// All arguments are physical screen pixels (including negative monitors).
    /// </summary>
    public static bool IsInsideCompactHoverArea(
        double pointerX, double pointerY, double windowRight, double windowTop,
        double windowHeight, double compactWindowWidth)
    {
        if (!double.IsFinite(pointerX) || !double.IsFinite(pointerY) ||
            !double.IsFinite(windowRight) || !double.IsFinite(windowTop) ||
            !double.IsFinite(windowHeight) || !double.IsFinite(compactWindowWidth) ||
            windowHeight <= 0 || compactWindowWidth <= 0) return false;
        return pointerX >= windowRight - compactWindowWidth && pointerX < windowRight &&
               pointerY >= windowTop && pointerY < windowTop + windowHeight;
    }

    /// <summary>
    /// Place the Split Utilities pill at the monitor's far-right edge.
    /// Both compact and expanded widths share the SAME right anchor so the
    /// clock does not move horizontally during the hover animation.
    /// </summary>
    public static double RightEdgeX(double screenLeft, double screenRight, double itemWidth, double inset)
    {
        inset = Math.Max(0, double.IsFinite(inset) ? inset : 0);
        if (!double.IsFinite(screenLeft) || !double.IsFinite(screenRight) ||
            !double.IsFinite(itemWidth)) return 0;
        var left = screenLeft + inset;
        return Math.Max(left, screenRight - Math.Max(0, itemWidth) - inset);
    }

    /// <summary>
    /// Center the utilities vertically against the actual dock surface,
    /// with a small downward optical adjustment. Stay within the monitor.
    /// All values are physical pixels.
    /// </summary>
    public static double AlignToDockCenterY(
        double dockSurfaceTop, double dockSurfaceHeight, double itemHeight,
        double downwardOffset, double screenTop, double screenBottom)
    {
        if (!double.IsFinite(dockSurfaceTop) || !double.IsFinite(dockSurfaceHeight) ||
            !double.IsFinite(itemHeight) || !double.IsFinite(downwardOffset) ||
            !double.IsFinite(screenTop) || !double.IsFinite(screenBottom)) return 0;
        var top = dockSurfaceTop + (dockSurfaceHeight - itemHeight) / 2 + downwardOffset;
        return Math.Clamp(top, screenTop, Math.Max(screenTop, screenBottom - Math.Max(0, itemHeight)));
    }

    /// <summary>
    /// Keep the floating Utilities pill entirely within the reserved top strip.
    /// Vertical dragging cannot escape into applications below the work area;
    /// horizontal dragging and left/right edge snapping remain available.
    /// All coordinates and dimensions are physical screen pixels.
    /// </summary>
    public static (double X, double Y) ClampToTopStrip(
        double x, double left, double top, double right, double bottom,
        double itemWidth, double itemHeight, double inset, bool snap)
    {
        inset = Math.Max(0, inset);
        var minX = left + inset;
        var maxX = Math.Max(minX, right - itemWidth - inset);
        x = Math.Clamp(double.IsFinite(x) ? x : maxX, minX, maxX);
        if (snap)
            x = x - minX <= maxX - x ? minX : maxX;
        // Center vertically in the reserved strip, never leave the actual
        // display or overlap the maximized application's workspace.
        var y = top + Math.Max(0, (bottom - top - itemHeight) / 2);
        return (x, y);
    }

    /// <summary>
    /// Anchor the separate Utilities pill beside the app dock. Check available
    /// space using the EXPANDED width so a hover never flips the pill to the
    /// other side halfway through the animation.
    /// All positions are in the same physical-pixel coordinate system.
    /// </summary>
    public static double BesideDockX(
        double screenLeft, double screenRight, double dockCenter, double dockWidth,
        double pillWidth, double expandedWidth, double gap, double inset)
    {
        if (!double.IsFinite(dockCenter) || !double.IsFinite(dockWidth) ||
            !double.IsFinite(pillWidth) || !double.IsFinite(expandedWidth))
            return screenLeft;
        inset = Math.Max(0, inset);
        gap = Math.Max(0, gap);
        var rightX = dockCenter + dockWidth / 2 + gap;
        if (rightX + expandedWidth + inset <= screenRight)
            return rightX + expandedWidth - pillWidth;
        var leftX = dockCenter - dockWidth / 2 - gap - pillWidth;
        if (leftX >= screenLeft + inset)
            return leftX;
        // Very narrow displays cannot fit two complete pills side-by-side.
        // Clamp safely; callers may choose a smaller dock size in Settings.
        return Math.Clamp(rightX, screenLeft + inset,
            Math.Max(screenLeft + inset, screenRight - pillWidth - inset));
    }

    public static bool IsDrag(double deltaX, double deltaY, double scale) =>
        deltaX * deltaX + deltaY * deltaY >= 36 * scale * scale;

    public static (double X, double Y) Clamp(double x, double y, double left, double top, double width, double height, double size, bool snap)
        => Clamp(x, y, left, top, width, height, size, size, snap);

    public static (double X, double Y) Clamp(double x, double y, double left, double top, double width, double height, double itemWidth, double itemHeight, bool snap)
    {
        var right = left + Math.Max(0, width - itemWidth);
        var bottom = top + Math.Max(0, height - itemHeight);
        x = Math.Clamp(double.IsFinite(x) ? x : right, left, right);
        y = Math.Clamp(double.IsFinite(y) ? y : bottom, top, bottom);
        if (snap)
        {
            var distances = new[] { x - left, right - x, y - top, bottom - y };
            switch (Array.IndexOf(distances, distances.Min()))
            {
                case 0: x = left; break;
                case 1: x = right; break;
                case 2: y = top; break;
                case 3: y = bottom; break;
            }
        }
        return (x, y);
    }
}
