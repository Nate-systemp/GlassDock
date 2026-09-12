using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

internal static class ShellApplicationMetadata
{
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

    internal static (string? Path, string? Arguments) ResolveLink(string path)
    {
        if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return (null, null);
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!);
            ((IPersistFile)instance!).Load(path, 0);
            var link = (ApplicationNative.IShellLink)instance;
            var target = new StringBuilder(32768);
            var arguments = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, 0, 4);
            link.GetArguments(arguments, arguments.Capacity);
            return (Environment.ExpandEnvironmentVariables(target.ToString()), arguments.ToString());
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException) { return (null, null); }
        finally { if (instance is not null) Marshal.ReleaseComObject(instance); }
    }

    internal static IReadOnlyList<PinnedApplication> ReadPinned(WindowsApplicationIconService icons)
    {
        object? instance = null;
        ApplicationNative.IEnumFullIdList? enumeration = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("90AA3A4E-1CBA-4233-B8BB-535773D48449"), true)!);
            var list = (ApplicationNative.IPinnedList)instance!;
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
                    // Packaged pins can return a bare AUMID instead of an absolute Shell parsing path.
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
}

