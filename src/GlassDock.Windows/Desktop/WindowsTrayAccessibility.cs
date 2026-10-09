using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GlassDock.Core.Desktop;

namespace GlassDock.Windows.Desktop;

public static class WindowsTrayAccessibility
{
    public static uint DoubleClickMilliseconds => GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    public enum TrayItemSource
    {
        Accessibility,
        Registry
    }

    public sealed record TrayItem(
        string Name,
        string? DefaultAction,
        string? ExecutablePath,
        TrayItemSource Source,
        nint ExplorerRoot = 0, int ExplorerPid = 0, long ExplorerStart = 0);

    private const int ObjIdClient = -4;
    private static readonly Guid IidAccessible =
        new("618736E0-3C3D-11CF-810C-00AA00389B71");

    // Read existing accessibility providers only. Never show, move or focus Explorer.
    public static Task<IReadOnlyList<TrayItem>> ReadHiddenItemsAsync() => OnSta(ReadHiddenItems);

    private static IReadOnlyList<TrayItem> ReadHiddenItems()
    {
        var result = new List<TrayItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hwnd in OverflowAccessibilityRoots())
            ReadAccessibleRoot(hwnd, result, seen);
        var metadata = ReadRegistryMetadataRecords();
        var live = result.Where(item => !IsGlassDockSystemItem(item.Name)).Take(32).ToArray();
        var represented = live.Select(item => MatchRegistryExecutable(item.Name, metadata))
            .Where(path => path is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Live imagery cannot be obtained through MSAA. Do not guess an EXE
        // image for a live item from its tooltip; show the named placeholder.
        return live.Concat(BuildRegistryItems(metadata.Where(record =>
            record.IsPromoted != 1 && !represented.Contains(record.ExecutablePath)))).ToArray();
    }

    private static Task<T> OnSta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "Doky tray operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static List<TrayMetadataRecord> ReadRegistryMetadataRecords()
    {
        const string keyPath = @"Control Panel\NotifyIconSettings";

        using var root = Registry.CurrentUser.OpenSubKey(keyPath);
        if (root is null)
            return [];

        var records = new List<TrayMetadataRecord>();

        foreach (var subKeyName in root.GetSubKeyNames())
        {
            using var entry = root.OpenSubKey(subKeyName);
            if (entry is null)
                continue;

            var executablePath = ExpandExecutablePath(
                entry.GetValue("ExecutablePath")?.ToString());

            if (string.IsNullOrWhiteSpace(executablePath))
                continue;

            var tooltip = entry.GetValue("InitialTooltip")?.ToString()?.Trim();
            var name = !string.IsNullOrWhiteSpace(tooltip)
                ? CleanTooltip(tooltip)
                : Path.GetFileNameWithoutExtension(executablePath);

            if (string.IsNullOrWhiteSpace(name) ||
                IsGlassDockSystemItem(name))
            {
                continue;
            }

            records.Add(
                new TrayMetadataRecord(
                    executablePath,
                    name,
                    ConvertRegistryNullableInt(entry.GetValue("IsPromoted"))));
        }

        return records
            .GroupBy(
                record => record.ExecutablePath + "\n" + record.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static string? MatchRegistryExecutable(
        string accessibleName,
        IReadOnlyList<TrayMetadataRecord> metadata)
    {
        var normalized = NormalizeTrayName(accessibleName);
        if (normalized.Length == 0)
            return null;

        var paths = metadata.Where(record =>
                NormalizeTrayName(record.Name).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Select(record => record.ExecutablePath).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return paths.Length == 1 ? paths[0] : null;
    }

    private static string NormalizeTrayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            " ",
            value
                .Split(
                    [' ', '\t', '\r', '\n', '-', '–', '—', '|', ','],
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
            .Trim();
    }

    private static IReadOnlyList<TrayItem> BuildRegistryItems(
        IEnumerable<TrayMetadataRecord> records)
    {
        var items = new List<TrayItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (items.Count >= 32)
                break;

            var running = IsLikelyRunning(record.ExecutablePath);

            // NotifyIconSettings contains historical entries. Requiring a live
            // matching process prevents old installed versions/apps from flooding
            // GlassDock when Explorer's accessibility tree is unavailable.
            if (!running)
                continue;

            if (!seen.Add(record.ExecutablePath))
                continue;

            var capturedPath = record.ExecutablePath;
            items.Add(
                new TrayItem(
                    record.Name,
                    "Open app",
                    capturedPath,
                    TrayItemSource.Registry));
        }

        return items;
    }

    internal static bool IsLikelyRunning(string executablePath)
    {
        using var process = FindMatchingProcess(executablePath);
        return process is not null;
    }

    private sealed record TrayMetadataRecord(
        string ExecutablePath,
        string Name,
        int? IsPromoted);

    private static int ConvertRegistryInt(object? value)
    {
        try
        {
            return value is null
                ? 0
                : Convert.ToInt32(value);
        }
        catch
        {
            return 0;
        }
    }

    private static int? ConvertRegistryNullableInt(object? value)
    {
        if (value is null)
            return null;

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    private static string ExpandExecutablePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());

        // NotifyIconSettings commonly stores paths rooted at Known Folder GUIDs
        // instead of normal drive paths. Resolve the Windows 11 forms observed
        // on this machine before checking the executable.
        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{6D809377-6AF0-444B-8957-A3773F02200E}",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}",
            Environment.GetFolderPath(Environment.SpecialFolder.System));

        expanded = ReplaceKnownFolderPrefix(
            expanded,
            "{F38BF404-1D43-42F2-9305-67DE0B28FC23}",
            Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        // NotifyIconSettings can include a quoted executable or command-line
        // arguments. Keep only the executable path so icon loading and ShellExecute
        // receive a stable file identity.
        if (expanded.StartsWith('"'))
        {
            var closingQuote = expanded.IndexOf('"', 1);
            if (closingQuote > 1)
                expanded = expanded[1..closingQuote];
        }
        else
        {
            var executableEnd = expanded.IndexOf(
                ".exe",
                StringComparison.OrdinalIgnoreCase);
            if (executableEnd >= 0)
                expanded = expanded[..(executableEnd + 4)];
        }

        return expanded.Trim();
    }

    private static string ReplaceKnownFolderPrefix(
        string value,
        string prefix,
        string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) ||
            !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var remainder = value[prefix.Length..]
            .TrimStart('\\', '/');

        return Path.Combine(
            folder,
            remainder.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CleanTooltip(string value)
    {
        var firstLine = value
            .Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine)
            ? value.Trim()
            : firstLine;
    }

    private static Process? FindMatchingProcess(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(processName))
            return null;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return null;
        }

        Process? selected = null;
        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited && TrayIdentity.SameExecutable(executablePath, process.MainModule?.FileName))
                {
                    selected ??= process;
                    if (process.MainWindowHandle != 0) { selected = process; break; }
                }
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
        foreach (var process in processes)
            if (!ReferenceEquals(process, selected)) process.Dispose();
        return selected;
    }

    private static bool OpenOrActivate(string executablePath)
    {
        try
        {
            var process = FindMatchingProcess(executablePath);
            if (process is not null)
            {
                try
                {
                    if (process.MainWindowHandle != 0)
                    {
                        ShowWindowAsync(process.MainWindowHandle, 9); // SW_RESTORE
                        return SetForegroundWindow(process.MainWindowHandle);
                    }
                    // A background instance is already running. Launching its EXE
                    // again is not a portable way to request its UI.
                    return false;
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (!File.Exists(executablePath))
                return false;

            return Process.Start(
                new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = true,
                    // Match an interactive launch from the application's folder.
                    // Tray apps such as GHelper use this to distinguish UI launch
                    // from background startup. Do not inherit Doky's directory.
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!
                }) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static Task<bool> InvokeAsync(TrayItem item) => OnSta(() =>
    {
        if (item.Source == TrayItemSource.Registry) return OpenApplication(item);
        if (!IsCurrentExplorer(item)) return false;
        return OverflowAccessibilityRoots().Contains(item.ExplorerRoot) &&
            InvokeNamedAccessibleItem(item.ExplorerRoot, item.Name);
    });

    private static bool IsCurrentExplorer(TrayItem item)
    {
        try
        {
            using var owner = Process.GetProcessById(item.ExplorerPid);
            GetWindowThreadProcessId(item.ExplorerRoot, out var currentPid);
            return currentPid == item.ExplorerPid && owner.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                owner.StartTime.ToUniversalTime().Ticks == item.ExplorerStart;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public static bool OpenApplication(TrayItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ExecutablePath))
            return false;

        Trace($"Explicit app open: {item.Name}; source={item.Source}");
        return OpenOrActivate(item.ExecutablePath);
    }

    public static void Trace(string message)
    {
        try
        {
            var path = Path.Combine(GlassDock.Windows.Settings.DokyUserData.DirectoryPath, "tray-activation.log");
            if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}\n");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static bool InvokeNamedAccessibleItem(nint hwnd, string expectedName)
    {
        if (hwnd == 0 || string.IsNullOrWhiteSpace(expectedName))
            return false;

        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(
                hwnd,
                ObjIdClient,
                ref iid,
                out var accessible) < 0 ||
            accessible is null)
        {
            return false;
        }

        try { return InvokeUnique(accessible, expectedName); }
        finally { Release(accessible); }
    }

    internal static bool InvokeUnique(object accessible, string expectedName,
        Func<object, string, object[], object?>? read = null, Func<object, int, bool>? invoke = null)
    {
        read ??= (node, member, args) => Get(node, member, args);
        invoke ??= Invoke;
        var matches = new List<(object Target, int Child)>();
        var acquired = new List<object>();
        try
        {
            Find(accessible, 0);
            return matches.Count == 1 && invoke(matches[0].Target, matches[0].Child);
        }
        finally { foreach (var child in acquired) Release(child); }

        void Find(object node, int depth)
        {
            if (depth > 8 || matches.Count > 1) return;
            var count = Math.Min(256, ConvertToInt(read(node, "accChildCount", [])));
            for (var id = 1; id <= count; id++)
            {
                var name = read(node, "accName", [id])?.ToString()?.Trim();
                var action = read(node, "accDefaultAction", [id])?.ToString();
                if (string.Equals(name, expectedName.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(action) && (ConvertToInt(read(node, "accState", [id])) & 1) == 0)
                    matches.Add((node, id));
                var child = read(node, "accChild", [id]);
                if (child is not null && Marshal.IsComObject(child)) { acquired.Add(child); Find(child, depth + 1); }
            }
        }
    }

    private static void Release(object value)
    {
        if (Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    private static void ReadAccessibleRoot(
        nint hwnd,
        List<TrayItem> result,
        HashSet<string> seen)
    {
        if (hwnd == 0)
            return;

        var iid = IidAccessible;
        if (AccessibleObjectFromWindow(
                hwnd,
                ObjIdClient,
                ref iid,
                out var accessible) < 0 ||
            accessible is null)
        {
            return;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var started = process.StartTime.ToUniversalTime().Ticks;
            var first = result.Count;
            Walk(accessible, 0, result, seen);
            for (var i = first; i < result.Count; i++)
                result[i] = result[i] with { ExplorerRoot = hwnd, ExplorerPid = (int)pid, ExplorerStart = started };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
        finally { Release(accessible); }
    }

    private static bool IsExplorerWindow(nint hwnd)
    {
        if (hwnd == 0) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

    private static IEnumerable<nint> OverflowAccessibilityRoots()
    {
        // Windows 11 modern overflow:
        // TopLevelWindowForOverflowXamlIsland
        var modern = FindWindowW(
            "TopLevelWindowForOverflowXamlIsland",
            null);

        if (IsExplorerWindow(modern))
        {
            // XAML accessibility is exposed by the desktop content bridge.
            var bridge = FindWindowExW(
                modern,
                0,
                "Windows.UI.Composition.DesktopWindowContentBridge",
                null);

            yield return bridge != 0 ? bridge : modern;
            yield break;
        }

        // Older/compatibility implementation.
        var legacy = FindWindowW(
            "NotifyIconOverflowWindow",
            null);

        if (IsExplorerWindow(legacy))
            yield return legacy;
    }

    private static void Walk(
        object accessible,
        int depth,
        List<TrayItem> result,
        HashSet<string> seen)
    {
        if (depth > 8 ||
            result.Count >= 48)
        {
            return;
        }

        var count = Math.Min(256, ConvertToInt(
            Get(accessible, "accChildCount")));

        if (count <= 0)
            return;

        for (var childId = 1;
             childId <= count &&
             result.Count < 48;
             childId++)
        {
            var name = Get(
                    accessible,
                    "accName",
                    childId)
                ?.ToString()
                ?.Trim();

            var action = Get(
                    accessible,
                    "accDefaultAction",
                    childId)
                ?.ToString()
                ?.Trim();

            // Modern and legacy providers use different roles. Require an
            // explicitly exposed, enabled default action instead.
            var actionable =
                !string.IsNullOrWhiteSpace(action) &&
                (ConvertToInt(Get(accessible, "accState", childId)) & 1) == 0;

            if (!string.IsNullOrWhiteSpace(name) &&
                actionable &&
                !IsGlassDockSystemItem(name) &&
                seen.Add(name))
            {
                result.Add(
                    new TrayItem(
                        name,
                        action,
                        null,
                        TrayItemSource.Accessibility));
            }

            var child = Get(
                accessible,
                "accChild",
                childId);

            if (child is not null &&
                Marshal.IsComObject(child))
            {
                try { Walk(child, depth + 1, result, seen); }
                finally { Release(child); }
            }
        }
    }

    private static object? Get(
        object target,
        string member,
        params object[]? args)
    {
        try
        {
            return target
                .GetType()
                .InvokeMember(
                    member,
                    BindingFlags.GetProperty |
                    BindingFlags.Public |
                    BindingFlags.Instance,
                    null,
                    target,
                    args is { Length: > 0 }
                        ? args
                        : null);
        }
        catch (Exception error)
            when (error is COMException or
                  TargetInvocationException or
                  MissingMethodException)
        {
            return null;
        }
    }

    private static bool Invoke(
        object target,
        int childId)
    {
        try
        {
            target
                .GetType()
                .InvokeMember(
                    "accDoDefaultAction",
                    BindingFlags.InvokeMethod |
                    BindingFlags.Public |
                    BindingFlags.Instance,
                    null,
                    target,
                    [childId]);

            return true;
        }
        catch (Exception error)
            when (error is COMException or
                  TargetInvocationException or
                  MissingMethodException)
        {
            return false;
        }
    }

    private static int ConvertToInt(object? value)
    {
        try
        {
            return value is null
                ? 0
                : Convert.ToInt32(value);
        }
        catch (Exception error)
            when (error is FormatException or
                  InvalidCastException or
                  OverflowException)
        {
            return 0;
        }
    }

    private static bool IsActionRole(int role) =>
        role is
            0x2B or // push button
            0x2C or // check button
            0x0C or // menu item
            0x28 or // graphic
            0x1E or // link
            0x2D;   // radio button

    private static bool IsGlassDockSystemItem(
        string name)
    {
        var value =
            name.ToLowerInvariant();

        // Broad Contains("network"/"search"/"start"/"battery") accidentally
        // hides real third-party apps such as Network Speed Monitor and
        // Everything Search. Match only standalone Windows control labels.
        return IsSystemLabel(value, "start") ||
               IsSystemLabel(value, "search") ||
               IsSystemLabel(value, "task view") ||
               IsSystemLabel(value, "system tray") ||
               IsSystemLabel(value, "notification chevron") ||
               IsSystemLabel(value, "overflow chevron") ||
               IsSystemLabel(value, "show hidden icons") ||
               IsSystemLabel(value, "hidden icon menu") ||
               IsSystemLabel(value, "clock") ||
               IsSystemLabel(value, "date and time") ||
               IsSystemLabel(value, "network") ||
               IsSystemLabel(value, "volume") ||
               IsSystemLabel(value, "battery");
    }

    private static bool IsSystemLabel(string value, string label)
    {
        if (value.Equals(label, StringComparison.Ordinal))
            return true;

        if (!value.StartsWith(label, StringComparison.Ordinal) || value.Length <= label.Length)
            return false;

        // Labels like "Volume, 50%" or "Network (connected)" are shell
        // status. "Volume2" and "Network Speed Monitor" are not.
        var separator = value[label.Length];
        return separator is ',' or ':' or '(' or '\n' or '\r';
    }

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(
        nint hwnd,
        int objectId,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)]
        out object? accessible);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(
        string? className,
        string? windowName);

    [DllImport(
        "user32.dll",
        CharSet = CharSet.Unicode)]
    private static extern nint FindWindowExW(
        nint parent,
        nint childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(
        nint hwnd,
        int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(
        nint hwnd);

}
