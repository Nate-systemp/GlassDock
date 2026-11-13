using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

[assembly: InternalsVisibleTo("GlassDock.Windows.Tests")]

namespace GlassDock.Windows.Applications;

internal sealed class WindowsApplicationIconService
{
    private readonly int targetIconSize;
    private readonly int mediumIconSize;
    private readonly int fallbackIconSize;
    private readonly int cacheCapacity;
    private readonly bool allowLargerIcons;

    private readonly Dictionary<string, ApplicationIcon> cache =
        new(StringComparer.Ordinal);

    private readonly Queue<string> cacheOrder = new();

    internal WindowsApplicationIconService(
        int targetIconSize = 256,
        int cacheCapacity = 128,
        bool allowLargerIcons = true)
    {
        this.targetIconSize = Math.Clamp(targetIconSize, 32, 256);
        this.cacheCapacity = Math.Clamp(cacheCapacity, 8, 256);
        this.allowLargerIcons = allowLargerIcons;

        if (this.targetIconSize >= 128)
        {
            mediumIconSize = 128;
            fallbackIconSize = 96;
        }
        else
        {
            mediumIconSize = Math.Min(this.targetIconSize, 48);
            fallbackIconSize = Math.Min(mediumIconSize, 32);
        }
    }

    internal ApplicationIcon? FromShell(string key, string parsing, nint suppliedPidl = 0)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;

        ApplicationIcon? image = null;
        foreach (var requestedSize in new[] { targetIconSize, mediumIconSize, fallbackIconSize })
        {
            var candidate = FromImageFactory(suppliedPidl, parsing, requestedSize, allowLargerIcons);
            if (candidate is not null && (image is null ||
                Math.Min(candidate.Width, candidate.Height) > Math.Min(image.Width, image.Height))) image = candidate;
            if (image is not null && Math.Min(image.Width, image.Height) >= targetIconSize) break;
        }

        if (image is null && !string.IsNullOrWhiteSpace(parsing) &&
            parsing.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var resolved = ShellApplicationMetadata.ResolveLink(parsing);
            if (!string.IsNullOrWhiteSpace(resolved.Path) && File.Exists(resolved.Path))
            {
                image = FromImageFactory(0, resolved.Path, targetIconSize, allowLargerIcons)
                        ?? FromImageFactory(0, resolved.Path, mediumIconSize, allowLargerIcons)
                        ?? FromExecutable(resolved.Path, targetIconSize);
            }
        }

        if (image is null && !string.IsNullOrWhiteSpace(parsing) &&
            (parsing.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            image = FromExecutable(parsing, targetIconSize);
        }

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
                        image = FromHIcon(info.Icon);
                    }
                    finally { ApplicationNative.DestroyIcon(info.Icon); }
                }
            }
            finally { if (suppliedPidl == 0) Marshal.FreeCoTaskMem(pidl); }
        }

        if (image is not null) Store(key, image);
        return image;
    }

    internal ApplicationIcon? FromWindow(string key, nint window, string? executable, string? appId)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;

        if (!string.IsNullOrWhiteSpace(appId) && appId.Contains('!'))
        {
            var packaged = FromShell(key, "shell:AppsFolder\\" + appId);
            if (packaged is not null) return packaged;
        }

        if (!string.IsNullOrWhiteSpace(executable))
        {
            var exeIcon = FromShell(key, executable);
            if (exeIcon is not null) return exeIcon;
        }

        ApplicationNative.SendMessageTimeout(window, 0x7F, 1               , 0, 2, 75, out var icon);
        if (icon == 0) ApplicationNative.SendMessageTimeout(window, 0x7F, 2                  , 0, 2, 75, out icon);
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -14                 );
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -34                   );
        var image = icon == 0 ? null : FromHIcon(icon);
        if (image is not null) Store(key, image);
        return image;
    }

    internal void Invalidate(string identity) => cache.Remove(identity);

    internal void Retain(IEnumerable<string> identities)
    {
        var live = identities.ToHashSet(StringComparer.Ordinal);

        foreach (var key in cache.Keys.Where(key => !live.Contains(key)).ToArray())
            cache.Remove(key);

        var survivors = cacheOrder.Where(cache.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();
        cacheOrder.Clear();

        foreach (var key in survivors)
            cacheOrder.Enqueue(key);
    }

    private void Store(string key, ApplicationIcon image)
    {
        if (cache.ContainsKey(key))
        {
            cache[key] = image;
            return;
        }

        cache[key] = image;
        cacheOrder.Enqueue(key);

        while (cache.Count > cacheCapacity && cacheOrder.Count > 0)
        {
            var oldest = cacheOrder.Dequeue();
            cache.Remove(oldest);
        }
    }

    private static ApplicationIcon? FromImageFactory(nint pidl, string? parsing, int size, bool allowLargerIcons)
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
                nint hbitmap = 0;

                if (allowLargerIcons)
                {
                    var flags =
                        ApplicationNative.SIIGBF.IconOnly |
                        ApplicationNative.SIIGBF.BiggerSizeOk;

                    if (factory.GetImage(
                            new ApplicationNative.SIZE(size, size),
                            flags,
                            out hbitmap) >= 0 &&
                        hbitmap != 0)
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

                if (factory.GetImage(new ApplicationNative.SIZE(size, size), ApplicationNative.SIIGBF.IconOnly, out hbitmap) >= 0 && hbitmap != 0)
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

    private ApplicationIcon? FromExecutable(string path, int size)
    {
        if (!File.Exists(path)) return null;
        var icons = new nint[1];
        var ids = new uint[1];

        if (ApplicationNative.PrivateExtractIcons(path, 0, size, size, icons, ids, 1, 0) > 0 && icons[0] != 0)
        {
            try
            {
                return FromHIcon(icons[0]);
            }
            finally
            {
                ApplicationNative.DestroyIcon(icons[0]);
            }
        }

        if (size > mediumIconSize && ApplicationNative.PrivateExtractIcons(path, 0, mediumIconSize, mediumIconSize, icons, ids, 1, 0) > 0 && icons[0] != 0)
        {
            try
            {
                return FromHIcon(icons[0]);
            }
            finally
            {
                ApplicationNative.DestroyIcon(icons[0]);
            }
        }

        if (ApplicationNative.PrivateExtractIcons(path, 0, fallbackIconSize, fallbackIconSize, icons, ids, 1, 0) > 0 && icons[0] != 0)
        {
            try
            {
                return FromHIcon(icons[0]);
            }
            finally
            {
                ApplicationNative.DestroyIcon(icons[0]);
            }
        }
        return null;
    }

    private static ApplicationIcon? FromHIcon(nint icon)
    {
        if (icon == 0) return null;
        if (!ApplicationNative.GetIconInfo(icon, out var iconInfo)) return null;
        try
        {
            if (iconInfo.hbmColor != 0)
            {
                var colorIcon = FromHBitmap(iconInfo.hbmColor, preserveMissingAlpha: true);
                if (colorIcon is not null)
                {
                    var hasRealAlpha = false;
                    for (var i = 3; i < colorIcon.Pixels.Length; i += 4)
                    {
                        var a = colorIcon.Pixels[i];
                        if (a != 0) { hasRealAlpha = true; break; }
                    }

                    if (hasRealAlpha) return colorIcon;

                    if (iconInfo.hbmMask != 0)
                    {
                        ApplyMask(colorIcon.Pixels, colorIcon.Width, colorIcon.Height, iconInfo.hbmMask);
                    }
                    return colorIcon;
                }
            }

            if (iconInfo.hbmMask != 0 && ApplicationNative.GetObject(iconInfo.hbmMask, Marshal.SizeOf<ApplicationNative.BitmapObject>(), out var maskObj) != 0)
            {
                var width = maskObj.Width;
                var height = maskObj.Height / 2;
                if (width > 0 && height > 0)
                {
                    var pixels = new byte[width * height * 4];
                    ApplyMask(pixels, width, height, iconInfo.hbmMask);
                    return new ApplicationIcon(width, height, pixels);
                }
            }
            return null;
        }
        finally
        {
            if (iconInfo.hbmColor != 0) ApplicationNative.DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != 0) ApplicationNative.DeleteObject(iconInfo.hbmMask);
        }
    }

    private static void ApplyMask(byte[] pixels, int width, int height, nint hbmMask)
    {
        var dc = ApplicationNative.CreateCompatibleDC(0);
        if (dc == 0) return;
        try
        {
            var maskInfo = new ApplicationNative.BitmapInfo
            {
                Size = 40,
                Width = width,
                Height = height,
                Planes = 1,
                Bits = 32
            };
            var maskPixels = new byte[width * height * 4];
            if (ApplicationNative.GetDIBits(dc, hbmMask, 0, (uint)height, maskPixels, ref maskInfo, 0) != 0)
            {
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    var srcOffset = (height - 1 - y) * stride;
                    var dstOffset = y * stride;
                    for (var x = 0; x < width; x++)
                    {
                        var pIdx = dstOffset + x * 4;
                        var mIdx = srcOffset + x * 4;
                        var isTransparent = maskPixels[mIdx] != 0 || maskPixels[mIdx + 1] != 0 || maskPixels[mIdx + 2] != 0;
                        if (isTransparent)
                        {
                            pixels[pIdx] = 0;
                            pixels[pIdx + 1] = 0;
                            pixels[pIdx + 2] = 0;
                            pixels[pIdx + 3] = 0;
                        }
                        else
                        {
                            pixels[pIdx + 3] = 255;
                        }
                    }
                }
            }
        }
        finally
        {
            ApplicationNative.DeleteDC(dc);
        }
    }

    private static ApplicationIcon? FromHBitmap(nint bitmap, bool preserveMissingAlpha = false)
    {
        if (bitmap == 0) return null;
        if (ApplicationNative.GetObject(bitmap, Marshal.SizeOf<ApplicationNative.BitmapObject>(), out var obj) == 0) return null;
        var width = obj.Width;
        var height = Math.Abs(obj.Height);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

        var dc = ApplicationNative.CreateCompatibleDC(0);
        if (dc == 0) return null;
        try
        {
            var info = new ApplicationNative.BitmapInfo
            {
                Size = 40,
                Width = width,
                Height = -height,
                Planes = 1,
                Bits = 32
            };
            var pixels = new byte[width * height * 4];
            if (ApplicationNative.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return null;

            var hasAlpha = false;
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }

            if (!hasAlpha)
            {
                if (!preserveMissingAlpha) return null;
            }
            else
            {
                var isStraight = false;
                for (var i = 0; i < pixels.Length; i += 4)
                {
                    var a = pixels[i + 3];
                    if (a == 0) { pixels[i] = pixels[i + 1] = pixels[i + 2] = 0; continue; }
                    if (a < 255 && (pixels[i] > a || pixels[i + 1] > a || pixels[i + 2] > a))
                    {
                        isStraight = true;
                        break;
                    }
                }
                if (isStraight)
                {
                    for (var i = 0; i < pixels.Length; i += 4)
                    {
                        var a = pixels[i + 3];
                        if (a == 0)
                        {
                            pixels[i] = 0;
                            pixels[i + 1] = 0;
                            pixels[i + 2] = 0;
                        }
                        else if (a < 255)
                        {
                            pixels[i] = (byte)((pixels[i] * a + 127) / 255);
                            pixels[i + 1] = (byte)((pixels[i + 1] * a + 127) / 255);
                            pixels[i + 2] = (byte)((pixels[i + 2] * a + 127) / 255);
                        }
                    }
                }
            }
            return new ApplicationIcon(width, height, pixels);
        }
        finally
        {
            ApplicationNative.DeleteDC(dc);
        }
    }
}
