namespace GlassDock.Core.Desktop;

/// <summary>Recognizes a bare Windows-key release without consuming shortcut keys.</summary>
public sealed class WindowsKeyGesture
{
    private readonly HashSet<int> held = [];
    private bool chord;

    public bool Process(int key, bool down)
    {
        var windows = key is 0x5B or 0x5C;
        if (down)
        {
            if (!held.Add(key)) return false;
            if (windows && held.Count == 1) chord = false;
            else if (held.Contains(0x5B) || held.Contains(0x5C)) chord = true;
            return false;
        }
        var known = held.Remove(key);
        return known && windows && !chord && held.Count == 0;
    }
}
