using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

internal static class ShellApplicationMetadata
{
    /// <summary>Enumerate the Shell's installed-app namespace, then application shortcuts in both Start menus.</summary>
    internal static void ReadAvailable(Action<GlassSearchResult> add, Func<bool> stopping, Action<string> warn)
    {
        object? shell = null, folder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", true)!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            if (folder is null) throw new InvalidOperationException("AppsFolder is unavailable.");
            items = ((dynamic)folder).Items();
            int count = ((dynamic)items).Count;
            for (var i = 0; i < count && !stopping(); i++)
            {
                object? item = null;
                try
                {
                    item = ((dynamic)items).Item(i);
                    string name = ((dynamic)item!).Name;
                    string path = ((dynamic)item).Path;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    {
                        var target = Path.IsPathRooted(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                            ? path : "shell:AppsFolder\\" + path;
                        add(ReadAvailableEntry(name, target));
                    }
                }
                catch (Exception error) when (IsDiscoveryError(error)) { warn("Some application entries could not be read."); }
                finally { if (item is not null) Marshal.ReleaseComObject(item); }
            }
        }
        catch (Exception error) when (IsDiscoveryError(error)) { warn("AppsFolder is unavailable; searching Start Menu shortcuts."); }
        finally
        {
            if (items is not null) Marshal.ReleaseComObject(items);
            if (folder is not null) Marshal.ReleaseComObject(folder);
            if (shell is not null) Marshal.ReleaseComObject(shell);
        }

        foreach (var location in new[] { Environment.SpecialFolder.Programs, Environment.SpecialFolder.CommonPrograms })
        {
            if (stopping()) return;
            var directory = Environment.GetFolderPath(location);
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) continue;
            try
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var shortcut in Directory.EnumerateFiles(directory, "*.lnk", options))
                {
                    if (stopping()) return;
                    try
                    {
                        var link = ResolveLink(shortcut);
                        // Exclude document and website shortcuts; retain virtual Shell/packaged links.
                        if (!string.IsNullOrWhiteSpace(link.Path) && !link.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            && !link.Path.StartsWith("::", StringComparison.Ordinal) && !link.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) continue;
                        add(ReadAvailableEntry(Path.GetFileNameWithoutExtension(shortcut), shortcut));
                    }
                    catch (Exception error) when (IsDiscoveryError(error)) { warn("Some Start Menu shortcuts could not be read."); }
                }
            }
            catch (Exception error) when (IsDiscoveryError(error)) { warn("Some Start Menu folders could not be read."); }
        }
    }

    internal static bool IsDiscoveryError(Exception error) => error is COMException or InvalidCastException
        or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException
        or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;

    private static GlassSearchResult ReadAvailableEntry(string name, string target)
    {
        string? appId = null, executable = null;
        if (ApplicationNative.SHParseDisplayName(target, 0, out var pidl, 0, out _) >= 0)
        {
            try
            {
                var iid = typeof(ApplicationNative.IPropertyStore).GUID;
                if (ApplicationNative.SHGetPropertyStoreFromIDList(pidl, 0, in iid, out var properties) >= 0)
                {
                    try
                    {
                        appId = Property(properties, "System.AppUserModel.ID");
                        executable = Property(properties, "System.Link.TargetParsingPath");
                    }
                    finally { Marshal.ReleaseComObject(properties); }
                }
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        var link = ResolveLink(target);
        if (!string.IsNullOrWhiteSpace(link.Path)) executable = link.Path;
        var identity = new ApplicationIdentity(appId, executable, link.Arguments, target);
        return new(identity.Key, name, "Application", [], GlassSearchResultType.Application, target);
    }

    internal static string? Property(ApplicationNative.IPropertyStore store, string name)
    {
        if (ApplicationNative.PSGetPropertyKeyFromName(name, out var key) < 0) return null;
        if (store.GetValue(in key, out var value) < 0) return null;
        try
        {
            if (value.Type == 0 || ApplicationNative.PropVariantToStringAlloc(in value, out var text) < 0) return null;
            try { return Marshal.PtrToStringUni(text) is { Length: > 0 } result ? result : null; }
            finally { Marshal.FreeCoTaskMem(text); }
        }
        finally { ApplicationNative.PropVariantClear(ref value); }
    }

    internal static string? Name(nint pidl, uint kind)
    {
        if (ApplicationNative.SHGetNameFromIDList(pidl, kind, out var text) < 0) return null;
        try { return Marshal.PtrToStringUni(text); }
        finally { Marshal.FreeCoTaskMem(text); }
    }

    internal static (string? Path, string? Arguments, string? WorkingDirectory) ResolveLink(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return (null, null, null);
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!);
            ((IPersistFile)instance!).Load(path, 0);
            var link = (ApplicationNative.IShellLink)instance;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            var directory = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(directory, directory.Capacity);
            return (Environment.ExpandEnvironmentVariables(target.ToString()), arguments.ToString(),
                Environment.ExpandEnvironmentVariables(directory.ToString()));
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException) { return (null, null, null); }
        finally { if (instance is not null) Marshal.ReleaseComObject(instance); }
    }

    internal static IReadOnlyList<PinnedApplication> ReadPinned(WindowsApplicationIconService icons)
    {
        try
        {
            var comPins = ReadPinnedFromCom(icons);
            if (comPins.Count > 0) return comPins;
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or IOException or UnauthorizedAccessException)
        {
        }

        return ReadPinnedFromTaskband(icons);
    }

    private static IReadOnlyList<PinnedApplication> ReadPinnedFromCom(WindowsApplicationIconService icons)
    {
        object? instance = null;
        ApplicationNative.IEnumFullIdList? enumeration = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("90AA3A4E-1CBA-4233-B8BB-535773D48449"), true)!);
            if (instance is not ApplicationNative.IPinnedList list) return Array.Empty<PinnedApplication>();
            Marshal.ThrowExceptionForHR(list.EnumObjects(out enumeration));
            var result = new List<PinnedApplication>();
            while (true)
            {
                var status = enumeration.Next(1, out var pidl, out var fetched);
                Marshal.ThrowExceptionForHR(status);
                if (status != 0 || fetched != 1) break;
                try
                {
                    var parsing = Name(pidl, 0x80028000); // SIGDN_DESKTOPABSOLUTEPARSING
                    if (string.IsNullOrWhiteSpace(parsing)) continue;
                    string? appId = null, target = null;
                    var iid = typeof(ApplicationNative.IPropertyStore).GUID;
                    if (ApplicationNative.SHGetPropertyStoreFromIDList(pidl, 0, in iid, out var properties) >= 0)
                    {
                        try
                        {
                            appId = Property(properties, "System.AppUserModel.ID");
                            target = Property(properties, "System.Link.TargetParsingPath");
                        }
                        finally { Marshal.ReleaseComObject(properties); }
                    }
                    var link = ResolveLink(parsing);
                    if (!string.IsNullOrEmpty(link.Path)) target = link.Path;
                    var executable = target?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ? target : null;
                    var identity = new ApplicationIdentity(appId, executable, link.Arguments, parsing);
                    var launchTarget = parsing.Equals(appId, StringComparison.OrdinalIgnoreCase)
                        ? "shell:AppsFolder\\" + parsing : parsing;
                    result.Add(new(identity, Name(pidl, 0) ?? Path.GetFileNameWithoutExtension(parsing), launchTarget,
                        icons.FromShell(identity.Key, parsing, pidl)));
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            return result;
        }
        finally
        {
            if (enumeration is not null) Marshal.ReleaseComObject(enumeration);
            if (instance is not null) Marshal.ReleaseComObject(instance);
        }
    }

    private static IReadOnlyList<PinnedApplication> ReadPinnedFromTaskband(WindowsApplicationIconService icons)
    {
        var result = new List<PinnedApplication>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var userPinned = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch\User Pinned");

        var lnkFiles = new List<string>();
        if (Directory.Exists(userPinned))
        {
            try
            {
                lnkFiles.AddRange(Directory.GetFiles(userPinned, "*.lnk", SearchOption.AllDirectories));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var order = new List<string>();
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Taskband");
            if (key?.GetValue("Favorites") is byte[] bytes)
            {
                for (var i = 0; i < bytes.Length - 10; i += 2)
                {
                    if (bytes[i] >= 32 && bytes[i] < 127 && bytes[i + 1] == 0)
                    {
                        var start = i;
                        while (i < bytes.Length - 1 && bytes[i + 1] == 0 && bytes[i] >= 32 && bytes[i] < 127) i += 2;
                        var s = Encoding.Unicode.GetString(bytes, start, i - start);
                        if ((s.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || s.Contains('!')) &&
                            !s.Contains('\\') && !order.Contains(s, StringComparer.OrdinalIgnoreCase))
                        {
                            order.Add(s);
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException) { }

        var sortedLnks = lnkFiles.OrderBy(f =>
        {
            var fname = Path.GetFileName(f);
            var idx = order.FindIndex(o => o.Equals(fname, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : 1000;
        }).ToList();

        foreach (var path in sortedLnks)
        {
            try
            {
                var link = ResolveLink(path);
                var executable = link.Path?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true ? link.Path : null;

                string? appId = null;
                if (ApplicationNative.SHParseDisplayName(path, 0, out var pidl, 0, out _) >= 0)
                {
                    try
                    {
                        var iid = typeof(ApplicationNative.IPropertyStore).GUID;
                        if (ApplicationNative.SHGetPropertyStoreFromIDList(pidl, 0, in iid, out var properties) >= 0)
                        {
                            try { appId = Property(properties, "System.AppUserModel.ID"); }
                            finally { Marshal.ReleaseComObject(properties); }
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(pidl); }
                }

                var identity = new ApplicationIdentity(appId, executable, link.Arguments, path);
                if (!seen.Add(identity.Key)) continue;

                var name = Path.GetFileNameWithoutExtension(path);
                var icon = icons.FromShell(identity.Key, path);
                result.Add(new PinnedApplication(identity, name, path, icon));
            }
            catch (Exception ex) when (ex is IOException or COMException or UnauthorizedAccessException) { }
        }

        foreach (var item in order)
        {
            if (item.Length > 5 && item.Contains('!') && !item.StartsWith('!') && !item.EndsWith('!') &&
                !item.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var appId = item;
                var launchTarget = "shell:AppsFolder\\" + appId;
                var identity = new ApplicationIdentity(appId, null, null, launchTarget);
                if (!seen.Add(identity.Key)) continue;

                var icon = icons.FromShell(identity.Key, launchTarget);
                var name = appId.Contains('_') ? appId.Split('_')[0] : appId.Split('!')[0];
                result.Add(new PinnedApplication(identity, name, launchTarget, icon));
            }
        }

        return result;
    }
}
