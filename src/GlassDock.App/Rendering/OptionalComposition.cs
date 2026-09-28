using System.Runtime.InteropServices;

namespace GlassDock.App.Rendering;

// App-lifetime, UI-thread state. A failed optional native initialization is not retried
// for each popup. Unrelated application errors still propagate normally.
internal sealed class OptionalComposition(bool disabled)
{
    internal bool Disabled { get; private set; } = disabled;
    private bool initialized;

    internal bool TryInitialize(Action initialize, Action<Exception> report)
    {
        if (Disabled) return false;
        if (initialized) return true;
        try { initialize(); initialized = true; return true; }
        catch (Exception error) when (IsRenderingFailure(error))
        {
            Disabled = true;
            report(error);
            return false;
        }
    }

    internal void Disable() => Disabled = true;
    internal static bool IsRenderingFailure(Exception error) => error is
        COMException or ArgumentException or NotSupportedException or
        DllNotFoundException or EntryPointNotFoundException;
}
