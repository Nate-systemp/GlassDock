using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

/// <summary>
/// Resolves the canonical Shell AppUserModelID for Win32 windows that expose no
/// window/process AUMID. This is especially common for Squirrel/Electron apps:
/// Windows Notification Center identifies them by the Start Menu shortcut AUMID,
/// while the running executable itself reports no AUMID.
/// </summary>
internal sealed class ShellApplicationIdentityResolver
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);
    private readonly object gate = new();
    private IReadOnlyList<ShellApplicationIdentityHint> cached = Array.Empty<ShellApplicationIdentityHint>();
    private DateTime refreshedUtc = DateTime.MinValue;

    internal string? Resolve(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        return Resolve(executablePath, Snapshot());
    }

    /// <summary>
    /// Resolves a user-supplied .lnk pin to the same canonical identity used by
    /// the running application. The launch target remains the original shortcut
    /// so custom arguments/profile behavior is preserved.
    /// </summary>
    internal ApplicationIdentity? ResolveShortcut(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath) ||
            !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(shortcutPath))
            return null;

        try
        {
            var appId = ReadAppUserModelId(shortcutPath);
            var link = ShellApplicationMetadata.ResolveLink(shortcutPath);
            if (string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(link.Path))
                appId = Resolve(link.Path);

            if (string.IsNullOrWhiteSpace(appId))
                return null;

            return new ApplicationIdentity(appId, link.Path, link.Arguments, shortcutPath);
        }
        catch (Exception error) when (ShellApplicationMetadata.IsDiscoveryError(error))
        {
            return null;
        }
    }

    internal static string? Resolve(
        string executablePath,
        IEnumerable<ShellApplicationIdentityHint> hints)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        var all = hints
            .Where(hint => !string.IsNullOrWhiteSpace(hint.AppUserModelId))
            .ToArray();

        // Prefer an exact executable target. This covers conventional Win32
        // shortcuts that carry an explicit System.AppUserModel.ID property.
        var exact = all.Where(hint =>
            SamePath(hint.ExecutablePath, executablePath)).ToArray();

        if (exact.Length > 0)
            return UniqueAppId(exact); // Ambiguous direct evidence must not fall through.

        // Squirrel/Electron shortcuts commonly take one of two forms:
        //
        // 1. Update.exe --processStart Foo.exe
        // 2. <app-root>\Foo.exe, while the real window belongs to
        //    <app-root>\app-x.y.z\Foo.exe
        //
        // Treat both as strong evidence only when the executable stays inside
        // the same versioned application root. Combine the candidates before
        // selecting an AUMID so conflicting Shell registrations remain
        // ambiguous instead of being guessed.
        return UniqueAppId(all.Where(hint =>
            MatchesProcessStartShortcut(hint, executablePath) ||
            MatchesVersionedChildShortcut(hint, executablePath)));
    }

    private IReadOnlyList<ShellApplicationIdentityHint> Snapshot()
    {
        lock (gate)
        {
            if (DateTime.UtcNow - refreshedUtc < CacheLifetime)
                return cached;

            cached = Discover();
            refreshedUtc = DateTime.UtcNow;
            return cached;
        }
    }

    private static IReadOnlyList<ShellApplicationIdentityHint> Discover()
    {
        var result = new List<ShellApplicationIdentityHint>();
        var seenShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in ShortcutRoots())
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                continue;

            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint
                };

                foreach (var shortcut in Directory.EnumerateFiles(directory, "*.lnk", options))
                {
                    if (!seenShortcuts.Add(shortcut))
                        continue;

                    try
                    {
                        var appId = ReadAppUserModelId(shortcut);
                        if (string.IsNullOrWhiteSpace(appId))
                            continue;

                        var link = ShellApplicationMetadata.ResolveLink(shortcut);
                        result.Add(new(
                            appId,
                            link.Path,
                            link.Arguments,
                            shortcut));
                    }
                    catch (Exception error) when (ShellApplicationMetadata.IsDiscoveryError(error))
                    {
                        // A single stale or third-party shortcut must not affect
                        // discovery for the rest of the dock.
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }

        return result
            .DistinctBy(
                hint => $"{hint.AppUserModelId}\0{ApplicationIdentity.NormalizePath(hint.ExecutablePath ?? "")}\0{hint.Arguments}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ShortcutRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

        var pinned = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned");

        yield return pinned;
    }

    private static string? ReadAppUserModelId(string shortcut)
    {
        if (ApplicationNative.SHParseDisplayName(shortcut, 0, out var pidl, 0, out _) < 0)
            return null;

        try
        {
            var iid = typeof(ApplicationNative.IPropertyStore).GUID;
            if (ApplicationNative.SHGetPropertyStoreFromIDList(pidl, 0, in iid, out var properties) < 0)
                return null;

            try
            {
                return ShellApplicationMetadata.Property(properties, "System.AppUserModel.ID");
            }
            finally
            {
                Marshal.ReleaseComObject(properties);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    private static string? UniqueAppId(IEnumerable<ShellApplicationIdentityHint> hints)
    {
        var ids = hints
            .Select(hint => hint.AppUserModelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return ids.Length == 1 ? ids[0] : null;
    }

    private static bool SamePath(string? candidate, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return false;

        return string.Equals(
            ApplicationIdentity.NormalizePath(candidate),
            ApplicationIdentity.NormalizePath(executablePath),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesVersionedChildShortcut(
        ShellApplicationIdentityHint hint,
        string executablePath)
    {
        if (string.IsNullOrWhiteSpace(hint.ExecutablePath) ||
            string.IsNullOrWhiteSpace(executablePath))
            return false;

        var shortcutTarget = ApplicationIdentity.NormalizePath(hint.ExecutablePath);
        var running = ApplicationIdentity.NormalizePath(executablePath);

        if (!string.Equals(
                Path.GetFileName(shortcutTarget),
                Path.GetFileName(running),
                StringComparison.OrdinalIgnoreCase))
            return false;

        var appRoot = Path.GetDirectoryName(shortcutTarget);
        var runningDirectory = Path.GetDirectoryName(running);
        if (string.IsNullOrWhiteSpace(appRoot) ||
            string.IsNullOrWhiteSpace(runningDirectory))
            return false;

        // Only accept the common Squirrel layout:
        //   <root>\App.exe
        //   <root>\app-1.2.3\App.exe
        // This intentionally rejects deeper/nested or sibling paths.
        if (!Path.GetFileName(runningDirectory).StartsWith("app-", StringComparison.OrdinalIgnoreCase))
            return false;

        return string.Equals(
            ApplicationIdentity.NormalizePath(Path.GetDirectoryName(runningDirectory) ?? ""),
            ApplicationIdentity.NormalizePath(appRoot),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesProcessStartShortcut(
        ShellApplicationIdentityHint hint,
        string executablePath)
    {
        if (string.IsNullOrWhiteSpace(hint.ExecutablePath) ||
            string.IsNullOrWhiteSpace(hint.Arguments) ||
            string.IsNullOrWhiteSpace(executablePath))
            return false;

        if (!string.Equals(Path.GetFileName(hint.ExecutablePath), "Update.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        var processStart = ProcessStartExecutable(hint.Arguments);
        if (string.IsNullOrWhiteSpace(processStart) ||
            processStart.IndexOfAny(['\\', '/', ':']) >= 0 ||
            !string.Equals(
                Path.GetFileName(processStart),
                Path.GetFileName(executablePath),
                StringComparison.OrdinalIgnoreCase))
            return false;

        var updaterDirectory = Path.GetDirectoryName(hint.ExecutablePath);
        if (string.IsNullOrWhiteSpace(updaterDirectory))
            return false;

        var root = ApplicationIdentity.NormalizePath(updaterDirectory);
        var running = ApplicationIdentity.NormalizePath(executablePath);

        if (root.Length == 0 || running.Length <= root.Length)
            return false;

        var runningDirectory = Path.GetDirectoryName(running);
        return string.Equals(runningDirectory, root, StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(Path.GetDirectoryName(runningDirectory), root, StringComparison.OrdinalIgnoreCase) &&
             Path.GetFileName(runningDirectory)?.StartsWith("app-", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string? ProcessStartExecutable(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return null;

        var match = Regex.Match(
            arguments,
            @"(?:^|\s)--processStart(?:AndWait)?(?:=|\s+)(?:""(?<value>[^""]+)""|(?<value>\S+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }
}

internal sealed record ShellApplicationIdentityHint(
    string AppUserModelId,
    string? ExecutablePath,
    string? Arguments,
    string? ShellPath);
