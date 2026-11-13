namespace GlassDock.Windows.Desktop;

public sealed class DesktopWindowHighlight : IDisposable
{
    private bool disposed;

    public DesktopWindowHighlight()
    {
    }

    public void Show(nint window)
    {
        if (disposed)
            return;

    }

    public void Hide()
    {
        if (disposed)
            return;

    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
    }
}