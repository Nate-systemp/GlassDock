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

        // 1. High-resolution Shell item image factory (supports PIDLs, shortcuts, UWP shell:AppsFolder)
        ApplicationIcon? image = null;
        foreach (var requestedSize in new[] { targetIconSize, mediumIconSize, fallbackIconSize })
        {
            var candidate = FromImageFactory(suppliedPidl, parsing, requestedSize, allowLargerIcons);
            if (candidate is not null && (image is null ||
                Math.Min(candidate.Width, candidate.Height) > Math.Min(image.Width, image.Height))) image = candidate;
            // A successful small result must not prevent trying another native Shell size.
            // 256px covers the padded tile at 1.24x hover through 400% DPI without enlargement.
            if (image is not null && Math.Min(image.Width, image.Height) >= targetIconSize) break;
        }

        // 2. If it's a .lnk shortcut and image is null or small, resolve link target executable
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

        // 3. High-resolution executable/resource extraction
        if (image is null && !string.IsNullOrWhiteSpace(parsing) &&
            (parsing.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
             parsing.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            image = FromExecutable(parsing, targetIconSize);
        }

        // 4. Fallback to SHGetFileInfo
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

        // 3. Window handle fallback (WM_GETICON big/small or class icon)
        ApplicationNative.SendMessageTimeout(window, 0x7F, 1 /* ICON_BIG */, 0, 2, 75, out var icon);
        if (icon == 0) ApplicationNative.SendMessageTimeout(window, 0x7F, 2 /* ICON_SMALL2 */, 0, 2, 75, out icon);
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -14 /* GCLP_HICON */);
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -34 /* GCLP_HICONSM */);
        var image = icon == 0 ? null : FromHIcon(icon);
        if (image is not null) Store(key, image);
        return image;
    }

    internal void Retain(IEnumerable<string> identities)
    {
        var live = identities.ToHashSet(StringComparer.Ordinal);

        foreach (var key in cache.Keys.Where(key => !live.Contains(key)).ToArray())
            cache.Remove(key);

        // Rebuild the FIFO bookkeeping so removed identities are not retained
        // indirectly by the queue.
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

                // Home uses this path with allowLargerIcons=false, which asks
                // Shell to resize to the small requested search-icon size.
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

        // Query 256x256 first for crisp high-DPI assets
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

        // Fallback to 128x128
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

        // Fallback to 96x96
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
                    // Check if color bitmap already had genuine alpha values
                    var hasRealAlpha = false;
                    for (var i = 3; i < colorIcon.Pixels.Length; i += 4)
                    {
                        var a = colorIcon.Pixels[i];
                        if (a != 0) { hasRealAlpha = true; break; }
                    }

                    if (hasRealAlpha) return colorIcon;

                    // If color bitmap had no alpha variation, use 1-bit mask bitmap to accurately set transparency
                    if (iconInfo.hbmMask != 0)
                    {
                        ApplyMask(colorIcon.Pixels, colorIcon.Width, colorIcon.Height, iconInfo.hbmMask);
                    }
                    return colorIcon;
                }
            }

            // Fallback for monochrome icons
            if (iconInfo.hbmMask != 0 && ApplicationNative.GetObject(iconInfo.hbmMask, Marshal.SizeOf<ApplicationNative.BitmapObject>(), out var maskObj) != 0)
            {
                var width = maskObj.Width;
                var height = maskObj.Height / 2; // Monochrome icon masks have double height (AND mask + XOR mask)
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
                // Mask fills bottom-up. Flip vertically to match pixels
                var stride = width * 4;
                for (var y = 0; y < height; y++)
                {
                    var srcOffset = (height - 1 - y) * stride;
                    var dstOffset = y * stride;
                    for (var x = 0; x < width; x++)
                    {
                        var pIdx = dstOffset + x * 4;
                        var mIdx = srcOffset + x * 4;
                        // In GDI icon mask: white (0xFFFFFF) indicates transparent; black (0x000000) indicates opaque.
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
                Height = height, // Positive height returns bottom-up scan lines.
                Planes = 1,
                Bits = 32
            };
            var pixels = new byte[width * height * 4];
            if (ApplicationNative.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, 0) != height) return null;

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
                // HICON transparency must come from its mask, never from RGB == black.
                // A Shell bitmap without alpha falls back to the mask-aware HICON path.
                if (!preserveMissingAlpha) return null;
            }
            else
            {
                // Verify if straight alpha needs premultiplication for WinUI 3 WriteableBitmap
                var isStraight = false;
                for (var i = 0; i < topDown.Length; i += 4)
                {
                    var a = topDown[i + 3];
                    if (a == 0) { topDown[i] = topDown[i + 1] = topDown[i + 2] = 0; continue; }
                    if (a < 255 && (topDown[i] > a || topDown[i + 1] > a || topDown[i + 2] > a))
                    {
                        isStraight = true;
                        break;
                    }
                }
                if (isStraight)
                {
                    for (var i = 0; i < topDown.Length; i += 4)
                    {
                        var a = topDown[i + 3];
                        if (a == 0)
                        {
                            topDown[i] = 0;
                            topDown[i + 1] = 0;
                            topDown[i + 2] = 0;
                        }
                        else if (a < 255)
                        {
                            topDown[i] = (byte)((topDown[i] * a + 127) / 255);
                            topDown[i + 1] = (byte)((topDown[i + 1] * a + 127) / 255);
                            topDown[i + 2] = (byte)((topDown[i + 2] * a + 127) / 255);
                        }
                    }
                }
            }
            return new ApplicationIcon(width, height, topDown);
        }
        finally
        {
            ApplicationNative.DeleteDC(dc);
        }
    }
}
