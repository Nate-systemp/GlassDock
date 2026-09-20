namespace GlassDock.Core.Desktop;

/// <summary>Coordinates one application-wide shutdown request and cancellation signal.</summary>
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
