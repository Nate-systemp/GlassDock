using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

[assembly: InternalsVisibleTo("GlassDock.Windows.Tests")]

namespace GlassDock.Windows.Applications;

internal sealed class WindowsApplicationIconService
{
    private const int TargetIconSize = 96;
    private readonly Dictionary<string, ApplicationIcon> cache = new(StringComparer.Ordinal);

    internal ApplicationIcon? FromShell(string key, string parsing, nint suppliedPidl = 0)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;

        // 1. High-resolution Shell item image factory (supports PIDLs, shortcuts, UWP shell:AppsFolder)
        var image = FromImageFactory(suppliedPidl, parsing, TargetIconSize);

        // 2. High-resolution executable/resource extraction
        if (image is null && !string.IsNullOrWhiteSpace(parsing) &&
            (parsing.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            image = FromExecutable(parsing, TargetIconSize);
        }

        // 3. Fallback to SHGetFileInfo
        if (image is null)
        {
            var pidl = suppliedPidl;
            if (pidl == 0 && ApplicationNative.SHParseDisplayName(parsing, 0, out pidl, 0, out _) < 0) return null;
            try
            {
                if (ApplicationNative.SHGetFileInfo(pidl, 0, out var info, (uint)Marshal.SizeOf<ApplicationNative.ShellFileInfo>(), 0x100 | 0x8) != 0 && info.Icon != 0)
                {
                    try
                    {
                        image = RenderIcon(info.Icon, TargetIconSize);
                    }
                    finally { ApplicationNative.DestroyIcon(info.Icon); }
                }
            }
            finally { if (suppliedPidl == 0) Marshal.FreeCoTaskMem(pidl); }
        }

        if (image is not null) cache[key] = image;
        return image;
    }

    internal ApplicationIcon? FromWindow(string key, nint window, string? executable, string? appId)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;

        // 1. Packaged store app (UWP / MSIX)
        if (!string.IsNullOrWhiteSpace(appId) && appId.Contains('!'))
        {
            var packaged = FromShell(key, "shell:AppsFolder\\" + appId);
            if (packaged is not null) return packaged;
        }

        // 2. High-resolution icon from executable if available
        if (!string.IsNullOrWhiteSpace(executable))
        {
            var exeIcon = FromShell(key, executable);
            if (exeIcon is not null) return exeIcon;
        }

        // 3. Window handle fallback (WM_GETICON or class icon)
        ApplicationNative.SendMessageTimeout(window, 0x7F, 1, 0, 2, 75, out var icon);
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -14);
        var image = icon == 0 ? null : RenderIcon(icon, TargetIconSize);
        if (image is not null) cache[key] = image;
        return image;
    }

    internal void Retain(IEnumerable<string> identities)
    {
        var live = identities.ToHashSet(StringComparer.Ordinal);
        foreach (var key in cache.Keys.Where(key => !live.Contains(key)).ToArray()) cache.Remove(key);
    }

    private static ApplicationIcon? FromImageFactory(nint pidl, string? parsing, int size)
    {
        ApplicationNative.IShellItemImageFactory? factory = null;
        try
        {
            var iid = typeof(ApplicationNative.IShellItemImageFactory).GUID;
            var hr = -1;
            if (pidl != 0)
            {
                hr = ApplicationNative.SHCreateItemFromIDList(pidl, in iid, out factory);
            }
            if (hr < 0 && !string.IsNullOrWhiteSpace(parsing))
            {
                hr = ApplicationNative.SHCreateItemFromParsingName(parsing, 0, in iid, out factory);
            }
            if (hr >= 0 && factory is not null)
            {
                var flags = ApplicationNative.SIIGBF.IconOnly | ApplicationNative.SIIGBF.BiggerSizeOk;
                if (factory.GetImage(new ApplicationNative.SIZE(size, size), flags, out var hbitmap) >= 0 && hbitmap != 0)
                {
                    try
                    {
                        return FromHBitmap(hbitmap);
                    }
                    finally
                    {
                        ApplicationNative.DeleteObject(hbitmap);
                    }
                }
            }
        }
        catch (Exception error) when (error is COMException or InvalidCastException or DllNotFoundException)
        {
        }
        finally
        {
            if (factory is not null) Marshal.ReleaseComObject(factory);
        }
        return null;
    }

    private static ApplicationIcon? FromExecutable(string path, int size)
    {
        if (!File.Exists(path)) return null;
        var icons = new nint[1];
        var ids = new uint[1];
        if (ApplicationNative.PrivateExtractIcons(path, 0, 256, 256, icons, ids, 1, 0) > 0 && icons[0] != 0)
        {
            try
            {
                return RenderIcon(icons[0], size);
            }
            finally
            {
                ApplicationNative.DestroyIcon(icons[0]);
            }
        }
        if (ApplicationNative.PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0) > 0 && icons[0] != 0)
        {
            try
            {
                return RenderIcon(icons[0], size);
            }
            finally
            {
                ApplicationNative.DestroyIcon(icons[0]);
            }
        }
        return null;
    }

    private static ApplicationIcon? FromHBitmap(nint bitmap)
    {
        if (bitmap == 0) return null;
        if (ApplicationNative.GetObject(bitmap, Marshal.SizeOf<ApplicationNative.BitmapObject>(), out var obj) == 0) return null;
        var width = obj.Width;
        var height = Math.Abs(obj.Height);
        if (width <= 0 || height <= 0) return null;

        var dc = ApplicationNative.CreateCompatibleDC(0);
        if (dc == 0) return null;
        try
        {
            var info = new ApplicationNative.BitmapInfo
            {
                Size = 40,
                Width = width,
                Height = height, // GetDIBits requires positive height
                Planes = 1,
                Bits = 32
            };
            var pixels = new byte[width * height * 4];
            if (ApplicationNative.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;

            // GetDIBits with positive height fills scan lines bottom-to-top.
            // Flip rows vertically into top-to-bottom order expected by UI frameworks.
            var stride = width * 4;
            var topDown = new byte[pixels.Length];
            for (var y = 0; y < height; y++)
            {
                Buffer.BlockCopy(pixels, (height - 1 - y) * stride, topDown, y * stride, stride);
            }

            var hasAlpha = false;
            for (var i = 3; i < topDown.Length; i += 4)
            {
                if (topDown[i] != 0) { hasAlpha = true; break; }
            }
            if (!hasAlpha)
            {
                for (var i = 0; i < topDown.Length; i += 4)
                {
                    if ((topDown[i] | topDown[i + 1] | topDown[i + 2]) != 0) topDown[i + 3] = 255;
                }
            }
            return new ApplicationIcon(width, height, topDown);
        }
        finally
        {
            ApplicationNative.DeleteDC(dc);
        }
    }

    private static ApplicationIcon? RenderIcon(nint icon, int size)
    {
        if (icon == 0) return null;
        var dc = ApplicationNative.CreateCompatibleDC(0);
        if (dc == 0) return null;
        var info = new ApplicationNative.BitmapInfo { Size = 40, Width = size, Height = -size, Planes = 1, Bits = 32 };
        var bitmap = ApplicationNative.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        nint previous = 0;
        try
        {
            if (bitmap == 0 || bits == 0) return null;
            var pixels = new byte[size * size * 4];
            previous = ApplicationNative.SelectObject(dc, bitmap);
            if (!ApplicationNative.DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, 3)) return null;
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }
            if (!hasAlpha)
            {
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0) pixels[i + 3] = 255;
                }
            }
            return new ApplicationIcon(size, size, pixels);
        }
        finally
        {
            if (previous != 0) ApplicationNative.SelectObject(dc, previous);
            if (bitmap != 0) ApplicationNative.DeleteObject(bitmap);
            ApplicationNative.DeleteDC(dc);
        }
    }
}
