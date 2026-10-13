using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

internal static class PinTargetProbe
{
    // A missing path is not proof of uninstall: updates, offline drives and
    // restored shortcuts reuse the same pin. Never use this probe to delete data.
    internal static PinTargetAvailability Check(string target, ApplicationIdentity identity)
        => Check(target, identity, 0);

    private static PinTargetAvailability Check(string target, ApplicationIdentity identity, int depth)
    {
        if (depth >= 4) return PinTargetAvailability.Unknown;
        if (identity.AppUserModelId?.Contains('!') == true ||
            target.StartsWith("\\\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(target))
            return PinTargetAvailability.Unknown;
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(target)!);
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) return PinTargetAvailability.Unknown;
            File.GetAttributes(target);
            // Resolve .lnk targets only on the existing discovery STA. Shell or
            // packaged identities remain opaque; URL shortcuts retain their URL.
            if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = ShellApplicationMetadata.ResolveLink(target);
                if (!string.IsNullOrWhiteSpace(resolved.Path) &&
                    !resolved.Path.Equals(target, StringComparison.OrdinalIgnoreCase))
                    return Check(resolved.Path, new(null, null), depth + 1);
                return PinTargetAvailability.Unknown;
            }
            return PinTargetAvailability.Available;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return PinTargetAvailability.Unavailable; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or
            System.Security.SecurityException or System.Runtime.InteropServices.COMException)
        { return PinTargetAvailability.Unknown; }
    }
}
