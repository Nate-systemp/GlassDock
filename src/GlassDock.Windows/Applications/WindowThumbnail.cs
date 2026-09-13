using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Applications;

/// <summary>A DWM-managed live relationship; no capture buffers or background frame loop.</summary>
public sealed class WindowThumbnail : IDisposable
{
    private nint thumbnail;
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct Properties
    {
        public uint Flags;
        public NativeMethods.Rect Destination, Source;
        public byte Opacity;
        public int Visible, ClientOnly;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out Size size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);

    public WindowThumbnail(nint destination, ApplicationWindow window)
    {
        if (WindowsApplicationService.IsEligible(window)) DwmRegisterThumbnail(destination, (nint)window.Handle, out thumbnail);
    }

    public bool Update(PreviewRect rect, double dpi, double opacity, bool visible)
    {
        if (thumbnail == 0 || DwmQueryThumbnailSourceSize(thumbnail, out var size) < 0 || size.Width <= 0 || size.Height <= 0) return false;
        var ratio = Math.Min(rect.Width * dpi / size.Width, rect.Height * dpi / size.Height);
        var width = size.Width * ratio;
        var height = size.Height * ratio;
        var x = rect.X * dpi + (rect.Width * dpi - width) / 2;
        var y = rect.Y * dpi + (rect.Height * dpi - height) / 2;
        var properties = new Properties
        {
            Flags = 1 | 4 | 8 | 16, Opacity = (byte)(Math.Clamp(opacity, 0, 1) * 255), Visible = visible ? 1 : 0,
            Destination = new() { Left = (int)Math.Round(x), Top = (int)Math.Round(y), Right = (int)Math.Round(x + width), Bottom = (int)Math.Round(y + height) }
        };
        return DwmUpdateThumbnailProperties(thumbnail, ref properties) >= 0;
    }

    public void Dispose()
    {
        if (thumbnail != 0) DwmUnregisterThumbnail(thumbnail);
        thumbnail = 0;
    }
}
