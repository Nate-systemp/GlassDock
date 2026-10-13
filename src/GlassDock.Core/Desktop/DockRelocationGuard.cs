namespace GlassDock.Core.Desktop;

/// <summary>Serializes UI-thread relocation, including reentrant window messages.</summary>
public sealed class DockRelocationGuard
{
    private bool running;
    private bool stopped;

    public void Stop() => stopped = true;

    public bool Run(Action operation, Func<Exception, bool> recoverable, Action<Exception> failed)
    {
        if (running || stopped) return false;
        running = true;
        try { operation(); return true; }
        catch (Exception error) when (recoverable(error)) { failed(error); return false; }
        finally { running = false; }
    }
}
