using System.Runtime.InteropServices;

namespace GlassDock.Windows.Settings;

/// <summary>The original desktop profile, regardless of sparse-package activation.</summary>
public static class DokyUserData
{
    public static string LocalAppData
    {
        get
        {
            var id = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
            // KF_FLAG_NO_PACKAGE_REDIRECTION: never silently create a package-local profile.
            Marshal.ThrowExceptionForHR(SHGetKnownFolderPath(in id, 0x10000, 0, out var path));
            try { return Marshal.PtrToStringUni(path) ?? throw new IOException("Windows returned no desktop profile path."); }
            finally { Marshal.FreeCoTaskMem(path); }
        }
    }
    public static string DirectoryPath => Path.Combine(LocalAppData, "GlassDock");
    public static string SettingsPath => Path.Combine(DirectoryPath, "settings.json");
    public static string PinsPath => Path.Combine(DirectoryPath, "dock-pins.json");

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
}
