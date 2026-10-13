namespace GlassDock.Core.Applications;

/// <summary>Missing artwork must never become an invisible, still-interactive dock slot.</summary>
public static class ApplicationIconFallback
{
    private static readonly ApplicationIcon fallback = Create();

    public static bool HasArtwork(ApplicationIcon? icon)
    {
        if (icon is null || icon.Width <= 0 || icon.Height <= 0 ||
            (long)icon.Width * icon.Height * 4 != icon.Pixels.Length) return false;
        for (var i = 3; i < icon.Pixels.Length; i += 4)
            if (icon.Pixels[i] != 0) return true;
        return false;
    }

    public static ApplicationIcon Visible(ApplicationIcon? icon) => HasArtwork(icon) ? icon! : fallback;

    private static ApplicationIcon Create()
    {
        // Neutral application tile, with a high-contrast question mark. This is
        // shared artwork, not evidence that an offline pin was uninstalled.
        const int size = 64;
        var pixels = new byte[size * size * 4];
        for (var y = 4; y < 60; y++)
        for (var x = 4; x < 60; x++)
        {
            var mark = (y is >= 16 and < 21 && x is >= 23 and < 41) ||
                (y is >= 20 and < 31 && x is >= 36 and < 42) ||
                (y is >= 28 and < 34 && x is >= 29 and < 40) ||
                (y is >= 32 and < 39 && x is >= 29 and < 35) ||
                (y is >= 44 and < 50 && x is >= 29 and < 35);
            var offset = (y * size + x) * 4;
            pixels[offset] = mark ? (byte)255 : (byte)100;
            pixels[offset + 1] = mark ? (byte)255 : (byte)85;
            pixels[offset + 2] = mark ? (byte)255 : (byte)72;
            pixels[offset + 3] = 255;
        }
        return new(size, size, pixels);
    }
}
