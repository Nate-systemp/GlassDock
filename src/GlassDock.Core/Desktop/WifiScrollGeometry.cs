namespace GlassDock.Core.Desktop;

/// <summary>One reversible layout curve for both edges of the Wi-Fi list.</summary>
public static class WifiScrollGeometry
{
    public const double Height = 36;
    public const double Gap = 6;
    public const double Pitch = Height + Gap;
    public const int VisibleSlots = 4;

    public static double Progress(int index, double offset, int visibleSlots)
    {
        var position = Math.Max(0, offset) / Pitch;
        var first = (int)Math.Floor(position);
        var t = position - first;
        var entering = t * t * (3 - 2 * t);
        if (entering < .0001) entering = 0;
        else if (entering > .9999) entering = 1;
        if (index < first || index > first + visibleSlots) return 0;
        if (index == first) return 1 - entering;
        return index == first + visibleSlots ? entering : 1;
    }
}
