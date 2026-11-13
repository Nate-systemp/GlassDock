namespace GlassDock.Core.Desktop;

public sealed class InputLifecycleRecoveryGate
{
    private bool recoveryRequested;
    private bool stopped;

    public void MarkBoundary()
    {
        if (stopped) return;
        recoveryRequested = false;
    }

    public bool RequestRecovery()
    {
        if (stopped) return false;
        if (recoveryRequested) return false;
        recoveryRequested = true;
        return true;
    }

    public void Reset()
    {
        if (!stopped) recoveryRequested = false;
    }

    public void Shutdown()
    {
        stopped = true;
        recoveryRequested = true;
    }
}
