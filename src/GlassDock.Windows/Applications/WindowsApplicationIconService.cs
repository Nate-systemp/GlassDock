using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

internal sealed class WindowsApplicationIconService
{
    private readonly Dictionary<string, ApplicationIcon> cache = new(StringComparer.Ordinal);

    internal ApplicationIcon? FromShell(string key, string parsing, nint suppliedPidl = 0)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        var pidl = suppliedPidl;
        if (pidl == 0 && ApplicationNative.SHParseDisplayName(parsing, 0, out pidl, 0, out _) < 0) return null;
        try
        {
            if (ApplicationNative.SHGetFileInfo(pidl, 0, out var info, (uint)Marshal.SizeOf<ApplicationNative.ShellFileInfo>(), 0x100 | 0x8) == 0 || info.Icon == 0) return null;
            try
            {
                var image = Render(info.Icon);
                if (image is not null) cache[key] = image;
                return image;
            }
            finally { ApplicationNative.DestroyIcon(info.Icon); }
        }
        finally { if (suppliedPidl == 0) Marshal.FreeCoTaskMem(pidl); }
    }

    internal ApplicationIcon? FromWindow(string key, nint window, string? executable, string? appId)
    {
        if (cache.TryGetValue(key, out var cached)) return cached;
        if (!string.IsNullOrWhiteSpace(appId) && appId.Contains('!'))
        {
            var packaged = FromShell(key, "shell:AppsFolder\\" + appId);
            if (packaged is not null) return packaged;
        }
        ApplicationNative.SendMessageTimeout(window, 0x7F, 1, 0, 2, 75, out var icon);
        if (icon == 0) icon = ApplicationNative.GetClassLongPtr(window, -14);
        var image = icon == 0 ? null : Render(icon); // Window/class icons are borrowed handles.
        if (image is not null) cache[key] = image;
        return image ?? (executable is null ? null : FromShell(key, executable));
    }

    internal void Retain(IEnumerable<string> identities)
    {
        var live = identities.ToHashSet(StringComparer.Ordinal);
        foreach (var key in cache.Keys.Where(key => !live.Contains(key)).ToArray()) cache.Remove(key);
    }

    private static ApplicationIcon? Render(nint icon)
    {
        const int size = 48;
        var dc = ApplicationNative.CreateCompatibleDC(0);
        if (dc == 0) return null;
        var info = new ApplicationNative.BitmapInfo { Size = 40, Width = size, Height = -size, Planes = 1, Bits = 32 };
        var bitmap = ApplicationNative.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
        nint previous = 0;
        try
        {
            if (bitmap == 0 || bits == 0) return null;
            var pixels = new byte[size * size * 4];
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            previous = ApplicationNative.SelectObject(dc, bitmap);
            if (!ApplicationNative.DrawIconEx(dc, 0, 0, icon, size, size, 0, 0, 3)) return null;
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            if (!Enumerable.Range(0, size * size).Any(index => pixels[index * 4 + 3] != 0))
                for (var index = 0; index < pixels.Length; index += 4)
                    if ((pixels[index] | pixels[index + 1] | pixels[index + 2]) != 0) pixels[index + 3] = 255;
            return new(size, size, pixels);
        }
        finally
        {
            if (previous != 0) ApplicationNative.SelectObject(dc, previous);
            if (bitmap != 0) ApplicationNative.DeleteObject(bitmap);
            ApplicationNative.DeleteDC(dc);
        }
    }
}
