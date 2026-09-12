using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class IconAlphaTests
{
    [Fact]
    public void TransparentRgbDoesNotCausePremultipliedEdgesToBeMultipliedAgain()
    {
        var dc = ApplicationNative.CreateCompatibleDC(0);
        var info = new ApplicationNative.BitmapInfo { Size = 40, Width = 2, Height = -1, Planes = 1, Bits = 32 };
        var bitmap = ApplicationNative.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        try
        {
            Assert.NotEqual(0, bitmap);
            Marshal.Copy(new byte[] { 64, 32, 16, 128, 120, 120, 120, 0 }, 0, bits, 8);
            var convert = typeof(WindowsApplicationIconService).GetMethod("FromHBitmap", BindingFlags.NonPublic | BindingFlags.Static)!;
            var image = Assert.IsType<ApplicationIcon>(convert.Invoke(null, [bitmap, false]));
            Assert.Equal(new byte[] { 64, 32, 16, 128, 0, 0, 0, 0 }, image.Pixels);
        }
        finally
        {
            if (bitmap != 0) ApplicationNative.DeleteObject(bitmap);
            ApplicationNative.DeleteDC(dc);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeIconPreservesOpaqueBlackAndTransparentPixels(bool binaryAlpha)
    {
        var dc = ApplicationNative.CreateCompatibleDC(0);
        var info = new ApplicationNative.BitmapInfo { Size = 40, Width = 2, Height = -1, Planes = 1, Bits = 32 };
        var color = ApplicationNative.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        var mask = CreateBitmap(2, 1, 1, 1, [binaryAlpha ? (byte)0 : (byte)0x40, 0]);
        nint icon = 0;
        try
        {
            Assert.NotEqual(0, color);
            Assert.NotEqual(0, mask);
            Marshal.Copy(new byte[] { 0, 0, 0, binaryAlpha ? (byte)255 : (byte)0, 0, 0, 0, 0 }, 0, bits, 8);
            var native = new ApplicationNative.IconInfo { fIcon = true, hbmColor = color, hbmMask = mask };
            icon = CreateIconIndirect(ref native);
            Assert.NotEqual(0, icon);
            var convert = typeof(WindowsApplicationIconService).GetMethod("FromHIcon", BindingFlags.NonPublic | BindingFlags.Static)!;
            var image = Assert.IsType<ApplicationIcon>(convert.Invoke(null, [icon]));
            Assert.Equal(2, image.Width);
            Assert.Equal(1, image.Height);
            Assert.Equal(new byte[] { 0, 0, 0, 255, 0, 0, 0, 0 }, image.Pixels);
        }
        finally
        {
            if (icon != 0) ApplicationNative.DestroyIcon(icon);
            if (color != 0) ApplicationNative.DeleteObject(color);
            if (mask != 0) ApplicationNative.DeleteObject(mask);
            ApplicationNative.DeleteDC(dc);
        }
    }

    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, uint planes, uint bits, byte[] pixels);
    [DllImport("user32.dll")] private static extern nint CreateIconIndirect(ref ApplicationNative.IconInfo info);
}
