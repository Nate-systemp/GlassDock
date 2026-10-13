using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
using GlassDock.Core.Applications;
using Microsoft.Win32;

namespace GlassDock.Windows.Applications;

internal sealed record PinInstallationEvidence(string RegistrationId, string ExecutablePath);
internal sealed record InstalledPinRegistration(
    string Id, string? ExecutablePath, string? InstallLocation, bool MsiRemoved = false,
    string? DisplayName = null, string? UninstallerPath = null)
{
    internal bool Matches(string executable, string? pinnedName = null) =>
        string.Equals(ExecutablePath, executable, StringComparison.OrdinalIgnoreCase) ||
        // InstallLocation can be a shared vendor directory. Only claim direct
        // executables in that folder, never arbitrary nested products.
        (!string.IsNullOrEmpty(InstallLocation) &&
         SameDirectory(Path.GetDirectoryName(executable), InstallLocation)) ||
        // Some installers (notably NSIS/Electron) register neither a usable
        // DisplayIcon nor InstallLocation. Their uninstaller lives alongside
        // the app. Only associate it when the full product name ALSO matches;
        // a shared installer directory or vendor alone is never ownership proof.
        (!string.IsNullOrWhiteSpace(UninstallerPath) &&
         SameDirectory(Path.GetDirectoryName(executable), Path.GetDirectoryName(UninstallerPath)) &&
         SameProductName(pinnedName, DisplayName));

    private static bool SameDirectory(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(second) &&
        string.Equals(Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second), StringComparison.OrdinalIgnoreCase);

    private static bool SameProductName(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second)) return false;
        static string Canonical(string name) => Regex.Replace(name.Trim(), @"[\W_]+", "").ToUpperInvariant();
        return string.Equals(Canonical(first), Canonical(second), StringComparison.Ordinal);
    }
}
internal sealed record InstalledPinSnapshot(bool Complete, IReadOnlyList<InstalledPinRegistration> Registrations);

/// <summary>Runs on the existing discovery worker. Absence alone never establishes an uninstall.</summary>
internal sealed class InstalledPinReconciler
{
    private readonly Func<InstalledPinSnapshot> read;
    private readonly Func<DateTimeOffset> clock;
    private readonly Dictionary<string, DateTimeOffset> missingSince = new(StringComparer.Ordinal);
    private InstalledPinSnapshot snapshot = new(false, []);
    private DateTimeOffset sampledAt = DateTimeOffset.MinValue;
    private readonly HashSet<string> observedPins = new(StringComparer.Ordinal);

    internal InstalledPinReconciler(Func<InstalledPinSnapshot>? read = null, Func<DateTimeOffset>? clock = null)
    { this.read = read ?? ReadRegistrations; this.clock = clock ?? (() => DateTimeOffset.UtcNow); }

    internal (Dictionary<string, PinInstallationEvidence> Evidence, HashSet<string> Removed) Inspect(
        IReadOnlyList<PinnedApplication> pins, IReadOnlyDictionary<string, PinInstallationEvidence> previous)
    {
        var now = clock();
        // A new pin needs an installation baseline immediately. Reusing a
        // cached registry snapshot can miss a short install -> pin -> uninstall
        // sequence and leave only an unrecoverable question-mark tile.
        var newlyPinned = false;
        foreach (var pin in pins)
            newlyPinned |= observedPins.Add(pin.Identity.Key);
        if (newlyPinned || now - sampledAt >= TimeSpan.FromSeconds(10))
        { snapshot = read(); sampledAt = now; }
        var evidence = previous.Where(pair => pins.Any(pin => pin.Identity.Key == pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        if (!snapshot.Complete) { missingSince.Clear(); return (evidence, removed); }
        foreach (var pin in pins)
        {
            var id = pin.Identity.Key;
            var executable = Executable(pin, evidence);
            if (executable is null) { missingSince.Remove(id); continue; }
            var availability = PinTargetProbe.Check(executable, new(null, null));
            var matches = snapshot.Registrations.Where(entry => entry.Matches(executable, pin.Name)).ToArray();
            var match = matches.Length == 1 ? matches[0] : null;
            if (availability == PinTargetAvailability.Available && match is { MsiRemoved: false })
                evidence[id] = new(match.Id, executable);

            // A replacement/updated registration, advertised MSI, inaccessible disk,
            // existing executable, ambiguous ownership, or launcher arguments veto removal.
            var registrationGone = evidence.TryGetValue(id, out var old) &&
                string.Equals(old.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase) &&
                !snapshot.Registrations.Any(entry => entry.Id == old.RegistrationId) && matches.Length == 0;
            var confirmed = availability == PinTargetAvailability.Unavailable &&
                (registrationGone || match is { MsiRemoved: true });
            if (!confirmed) { missingSince.Remove(id); continue; }
            if (!missingSince.TryGetValue(id, out var since)) missingSince[id] = sampledAt;
            else if (sampledAt - since >= TimeSpan.FromSeconds(30)) removed.Add(id);
        }
        foreach (var id in missingSince.Keys.Where(id => !pins.Any(pin => pin.Identity.Key == id)).ToArray()) missingSince.Remove(id);
        foreach (var id in removed) evidence.Remove(id);
        return (evidence, removed);
    }

    private static string? Executable(PinnedApplication pin,
        IReadOnlyDictionary<string, PinInstallationEvidence> previous)
    {
        // Explicit shell/package and argument-driven launcher identities are not
        // evidence that the launcher's own installation is the pinned application.
        if (pin.Identity.AppUserModelId?.Contains('!') == true || !string.IsNullOrWhiteSpace(pin.Identity.Arguments)) return null;
        var target = pin.LaunchTarget;
        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(target))
            {
                // Uninstallers often delete the Desktop .lnk before the next pin scan.
                // COM can no longer resolve its former .exe, but a previously verified
                // installer association is persisted for precisely this pin identity.
                // Never infer ownership from a missing shortcut alone: only reuse
                // the earlier association when the local shortcut is truly missing,
                // not when a drive or network resource is temporarily inaccessible.
                return PinTargetProbe.Check(target, pin.Identity) == PinTargetAvailability.Unavailable &&
                    previous.TryGetValue(pin.Identity.Key, out var verified)
                    ? NormalizeExecutable(verified.ExecutablePath)
                    : null;
            }
            var link = ShellApplicationMetadata.ResolveLink(target);
            if (!string.IsNullOrWhiteSpace(link.Arguments)) return null;
            target = string.IsNullOrWhiteSpace(link.Path) ? pin.Identity.ExecutablePath ?? "" : link.Path;
        }
        return NormalizeExecutable(target);
    }

    internal static string? NormalizeExecutable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = Environment.ExpandEnvironmentVariables(value.Trim());
        if (value.StartsWith('"'))
        {
            var end = value.IndexOf('"', 1);
            if (end < 0) return null;
            value = value[1..end];
        }
        else value = Regex.Replace(value, @",\s*-?\d+$", "");
        if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || value.StartsWith(@"\\") || !Path.IsPathFullyQualified(value)) return null;
        try { return Path.GetFullPath(value); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return null; }
    }

    internal static InstalledPinSnapshot ReadRegistrations()
    {
        const string branch = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var entries = new List<InstalledPinRegistration>();
        try
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(branch);
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(name);
                    if (entry is null) return new(false, []); // Concurrent uninstall: retry a complete snapshot.
                    var icon = NormalizeExecutable(entry.GetValue("DisplayIcon") as string);
                    var location = entry.GetValue("InstallLocation") as string;
                    location = SafeInstallDirectory(location);
                    var command = entry.GetValue("UninstallString") as string ?? "";
                    var displayName = entry.GetValue("DisplayName") as string;
                    var uninstallExecutable = UninstallerExecutable(command);
                    var product = Regex.Match(command, @"(?i)\bmsiexec(?:\.exe)?\b.*?(\{[0-9a-f-]{36}\})");
                    // UNKNOWN only. ABSENT means installed for another user and must be retained.
                    var msiRemoved = product.Success && Guid.TryParse(product.Groups[1].Value, out var code) &&
                        MsiQueryProductState(code.ToString("B").ToUpperInvariant()) == -1;
                    entries.Add(new($"{hive}/{view}/{name}", icon, location, msiRemoved,
                        displayName, uninstallExecutable));
                }
            }
            // Some registry branches are shared between views. Treat identical
            // registrations as one owner while retaining a stable preferred ID.
            return new(true, entries.DistinctBy(e => (e.Id.Split('/')[0], e.Id.Split('/')[2], e.ExecutablePath, e.InstallLocation)).ToArray());
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        { return new(false, []); }
    }

    /// <summary>Extract only a local executable from a registered uninstall command.</summary>
    internal static string? UninstallerExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = Environment.ExpandEnvironmentVariables(command.Trim());
        string? executable;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            executable = end > 1 ? text[1..end] : null;
        }
        else
        {
            // Some uninstall entries omit quotes even when their path has spaces.
            var match = Regex.Match(text, @"^(?<path>.+?\.exe)(?=\s|$)", RegexOptions.IgnoreCase);
            executable = match.Success ? match.Groups["path"].Value : null;
        }
        var normalized = NormalizeExecutable(executable);
        if (normalized is null) return null;
        // A system executable (msiexec/rundll32) doesn't identify the
        // application's installation directory and must never match a pin.
        var file = Path.GetFileName(normalized);
        if (file.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("rundll32.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return normalized;
    }

    private static string? SafeInstallDirectory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (value.StartsWith(@"\\") || !Path.IsPathFullyQualified(value)) return null;
        var path = Path.GetFullPath(value).TrimEnd('\\') + "\\";
        // Never infer ownership from a drive root or shared installation container.
        var containers = new[] { Path.GetPathRoot(path), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
        return containers.Any(p => string.Equals(p?.TrimEnd('\\') + "\\", path, StringComparison.OrdinalIgnoreCase)) ? null : path;
    }

    [DllImport("msi.dll", EntryPoint = "MsiQueryProductStateW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MsiQueryProductState(string product);
}
