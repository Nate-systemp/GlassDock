namespace GlassDock.Core.Desktop;

public static class DockKeyboardShortcut
{
    public const int Navigate = 0;
    public static int FromVirtualKey(int key) => key switch
    {
        0x54 => Navigate,
        >= 0x31 and <= 0x39 => key - 0x30,
        _ => -1
    };

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
