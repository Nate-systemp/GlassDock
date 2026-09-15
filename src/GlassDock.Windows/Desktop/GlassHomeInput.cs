using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Desktop;

/// <summary>Reads the existing Home mouse-button polling inputs without installing hooks.</summary>
public static class GlassHomeInput
{
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativeMethods.Point point);

    public static GlassHomePointer? Read(nint window)
    {
        var previous = NativeMethods.SetThreadDpiAwarenessContext(-4);
        try
        {
            if (!GetCursorPos(out var cursor) || !NativeMethods.GetWindowRect(window, out var bounds)) return null;
            var buttons = (Down(1) ? 1 : 0) | (Down(2) ? 2 : 0) | (Down(4) ? 4 : 0);
            return new(cursor.X, cursor.Y, buttons,
                cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom);
        }
        finally { if (previous != 0) NativeMethods.SetThreadDpiAwarenessContext(previous); }
    }

    private static bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    public static double Scale(nint window)
    {
        var dpi = NativeMethods.GetDpiForWindow(window);
        return dpi == 0 ? 1 : dpi / 96d;
    }
}
