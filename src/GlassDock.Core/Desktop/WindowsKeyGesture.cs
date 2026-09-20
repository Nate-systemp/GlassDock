namespace GlassDock.Core.Desktop;

/// <summary>
/// Recognizes a bare Windows-key release without consuming real Windows shortcuts.
/// Escape is ignored as a Win-key chord so Esc -> Win remains responsive.
/// </summary>
public sealed class WindowsKeyGesture
{
    private readonly HashSet<int> held = [];
    private bool chord;

    private const int LeftWindows = 0x5B;
    private const int RightWindows = 0x5C;
    private const int Escape = 0x1B;

    public bool Process(int key, bool down)
    {
        var windows = key is LeftWindows or RightWindows;

        if (down)
        {
            if (!held.Add(key))
                return false;

            if (windows)
            {
                // Treat Win as "bare" when no meaningful shortcut key
                // is currently held. Escape is intentionally ignored.
                // A second Windows key belongs to the same gesture. Keep any
                // chord disqualification until both Windows keys are released.
                chord |= held.Any(k => k != key && k != Escape);
            }
            else if (key != Escape &&
                     (held.Contains(LeftWindows) ||
                      held.Contains(RightWindows)))
            {
                // A real key pressed while Win is held makes this
                // a Windows shortcut such as Win+E, Win+R, Win+D, etc.
                chord = true;
            }

            return false;
        }

        var known = held.Remove(key);

        if (!known)
            return false;

        if (!windows)
            return false;

        var bareWindowsKey =
            !chord &&
            !held.Any(k => k != Escape);

        if (bareWindowsKey)
        {
            chord = false;
            return true;
        }

        // Reset once neither Windows key remains held.
        if (!held.Contains(LeftWindows) &&
            !held.Contains(RightWindows))
        {
            chord = false;
        }

        return false;
    }
}
