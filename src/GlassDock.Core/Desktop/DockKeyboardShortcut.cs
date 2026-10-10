namespace GlassDock.Core.Desktop;

/// <summary>Win+1–9 selects a dock position; Win+T starts keyboard navigation.</summary>
public static class DockKeyboardShortcut
{
    public const int Navigate = 0;
    public static int FromVirtualKey(int key) => key switch
    {
        0x54 => Navigate, // T
        >= 0x31 and <= 0x39 => key - 0x30,
        _ => -1
    };

    /// <summary>Read one helper shortcut using the existing helper event sequence.</summary>
    public static (int Index, uint Revision)? Read(string? line, ref long sequence, long now)
    {
        if (string.IsNullOrEmpty(line)) return null;
        var fields = line.Split('|');
        if (fields.Length != 5 || fields[0] != "SHORTCUT" ||
            !long.TryParse(fields[1], out var next) || next <= sequence ||
            !long.TryParse(fields[2], out var timestamp) ||
            timestamp > now || now - timestamp > 500 ||
            !uint.TryParse(fields[3], out var revision) ||
            !int.TryParse(fields[4], out var index) || index is < 0 or > 9)
            return null;
        sequence = next;
        return (index, revision);
    }
}
