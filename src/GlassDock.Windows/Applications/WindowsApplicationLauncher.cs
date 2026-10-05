using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed class WindowsApplicationLauncher
{
    public Task<bool> OpenWithAsync(DockApplication application, IReadOnlyList<string> paths)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(OpenWith(application, paths)); }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
            { completion.SetResult(false); }
        }) { IsBackground = true, Name = "Doky open selected files" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
    public Task<bool> LaunchTargetAsync(string target)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(LaunchTarget(target)); }
            catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException)
            { completion.SetResult(false); }
        }) { IsBackground = true, Name = "GlassDock search launch" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public static string? Target(DockApplication application) => application.LaunchTarget ??
        (application.Identity.AppUserModelId?.Contains('!') == true ? "shell:AppsFolder\\" + application.Identity.AppUserModelId
            : application.Identity.ExecutablePath);

    public static bool CanRunAsAdministrator(DockApplication application) =>
        IsLaunchableExecutable(application.Identity.ExecutablePath);

    public static bool CanOpenFileLocation(DockApplication application) =>
        ResolveFileLocation(application) is not null;

    public bool Launch(DockApplication application)
    {
        var target = Target(application);
        return LaunchTarget(target, application.LaunchTarget is null ||
            string.Equals(target, application.Identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            ? application.Identity.Arguments : null);
    }

    // Cheap metadata-only admission check; shortcut COM resolution happens only on drop.
    public static bool CanOpenWith(DockApplication application)
    {
        var target = Target(application);
        return !string.IsNullOrWhiteSpace(target) && Path.IsPathFullyQualified(target) &&
            (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
             Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase));
    }

    internal sealed record FileLaunch(string Target, string Arguments, string? WorkingDirectory);

    internal static FileLaunch? PrepareFileLaunch(DockApplication application, IReadOnlyList<string> paths)
    {
        if (!CanOpenWith(application) || paths.Count == 0) return null;
        var existing = paths.Where(path => !string.IsNullOrWhiteSpace(path) &&
            Path.IsPathFullyQualified(path) && (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (existing.Length == 0) return null;

        var target = Target(application)!;
        string? directory = null;
        var arguments = application.Identity.Arguments;
        if (Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var link = ShellApplicationMetadata.ResolveLink(target);
            target = link.Path ?? "";
            arguments = link.Arguments;
            directory = string.IsNullOrWhiteSpace(link.WorkingDirectory) ? null : link.WorkingDirectory;
        }
        // Never ShellExecute a document, URL, AppsFolder item or unresolved shortcut as the target.
        if (!IsLaunchableExecutable(target)) return null;
        var files = string.Join(" ", existing.Select(QuoteArgument));
        return new(target, string.IsNullOrWhiteSpace(arguments) ? files : arguments + " " + files, directory);
    }

    public bool OpenWith(DockApplication application, IReadOnlyList<string> paths)
    {
        var launch = PrepareFileLaunch(application, paths);
        return launch is not null && LaunchTarget(launch.Target, launch.Arguments, workingDirectory: launch.WorkingDirectory);
    }
    internal static string QuoteArgument(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        if (!value.Any(character =>
                char.IsWhiteSpace(character) ||
                character == '"'))
        {
            return value;
        }

        var result = new System.Text.StringBuilder();
        result.Append('"');
        var backslashes = 0;

        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
                continue;
            }

            if (backslashes > 0)
            {
                result.Append('\\', backslashes);
                backslashes = 0;
            }

            result.Append(character);
        }

        if (backslashes > 0)
            result.Append('\\', backslashes * 2);

        result.Append('"');
        return result.ToString();
    }

    public bool RunAsAdministrator(DockApplication application)
    {
        var target = application.Identity.ExecutablePath;
        if (!IsLaunchableExecutable(target)) return false;
        return LaunchTarget(target, application.Identity.Arguments, "runas");
    }

    public bool OpenFileLocation(DockApplication application)
    {
        var path = ResolveFileLocation(application);
        if (path is null) return false;
        var info = new ApplicationNative.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ApplicationNative.ShellExecuteInfo>(),
            Mask = 0x400,
            File = "explorer.exe",
            Parameters = $"/select,\"{path}\"",
            Show = 1
        };
        return ApplicationNative.ShellExecuteEx(ref info);
    }

    public bool LaunchTarget(string? target, string? arguments = null, string? verb = null, string? workingDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        var info = new ApplicationNative.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ApplicationNative.ShellExecuteInfo>(), Mask = 0x400,
            Verb = verb, File = target, Parameters = arguments, Directory = workingDirectory, Show = 1
        };
        // Preserve the exact shortcut or AppsFolder item, including its launch arguments.
        return ApplicationNative.ShellExecuteEx(ref info);
    }

    private static bool IsLaunchableExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
        File.Exists(path);

    private static string? ResolveFileLocation(DockApplication application)
    {
        var executable = application.Identity.ExecutablePath;
        if (!string.IsNullOrWhiteSpace(executable) && File.Exists(executable)) return executable;
        var target = application.LaunchTarget;
        return !string.IsNullOrWhiteSpace(target) &&
            string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(target) ? target : null;
    }
}
