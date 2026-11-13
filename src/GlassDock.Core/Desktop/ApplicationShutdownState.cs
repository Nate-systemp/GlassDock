namespace GlassDock.Core.Desktop;

public sealed class ApplicationShutdownState
{
    private readonly CancellationTokenSource cancellation = new();
    private int requested;

    public bool IsRequested => Volatile.Read(ref requested) != 0;
    public CancellationToken CancellationToken => cancellation.Token;

    public bool TryBegin()
    {
        if (Interlocked.CompareExchange(ref requested, 1, 0) != 0)
            return false;

        cancellation.Cancel();
        return true;
    }
}
