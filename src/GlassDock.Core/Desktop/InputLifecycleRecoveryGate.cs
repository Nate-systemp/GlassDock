namespace GlassDock.Core.Desktop;

/// <summary>
/// Coalesces the paired suspend/resume and lock/unlock notifications that
/// Windows can deliver for one lifecycle transition. A recovery is still
/// requested when a resume arrives without a preceding boundary notification;
/// this covers display-only wakeups and missed power broadcasts.
/// </summary>
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
