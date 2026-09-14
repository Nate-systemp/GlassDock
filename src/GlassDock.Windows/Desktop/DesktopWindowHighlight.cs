namespace GlassDock.Windows.Desktop;

/// <summary>
/// Desktop window highlight has been intentionally disabled.
///
/// The class remains available so any older GlassDock code
/// referencing DesktopWindowHighlight will still compile,
/// but it no longer creates an overlay, border, outline,
/// region, timer, or native window.
/// </summary>
public sealed class DesktopWindowHighlight : IDisposable
{
    private bool disposed;

    public DesktopWindowHighlight()
    {
    }

    /// <summary>
    /// Highlight intentionally disabled.
    /// No visual effect is applied to the target window.
    /// </summary>
    public void Show(nint window)
    {
        if (disposed)
            return;

        // Intentionally empty.
        //
        // Previous implementation created a WS_EX_LAYERED
        // transparent STATIC overlay using SS_WHITERECT
        // and a hollow rounded window region.
        //
        // That implementation was responsible for the
        // white border appearing around the actual window
        // when hovering its GlassDock preview.
    }

    /// <summary>
    /// Nothing needs to be hidden because no overlay exists.
    /// </summary>
    public void Hide()
    {
        if (disposed)
            return;

        // Intentionally empty.
    }

    /// <summary>
    /// Marks this disabled component as disposed.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
    }
}