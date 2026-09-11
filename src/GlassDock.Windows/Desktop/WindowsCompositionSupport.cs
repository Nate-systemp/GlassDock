using System.Runtime.InteropServices;

namespace GlassDock.Windows.Desktop;

/// <summary>WinUI's dispatcher is separate from the OS compositor's dispatcher queue.</summary>
public sealed class WindowsCompositionSupport : IDisposable
{
    private nint controller;
    [StructLayout(LayoutKind.Sequential)]
    private struct Options { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(Options options, out nint controller);

    public WindowsCompositionSupport()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null) return;
        var options = new Options { Size = Marshal.SizeOf<Options>(), ThreadType = 2, ApartmentType = 2 };
        Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out controller));
    }

    public void Dispose()
    {
        if (controller != 0) { Marshal.Release(controller); controller = 0; }
    }
}
