using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed class WindowsApplicationLauncher
{
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

    public bool OpenWith(DockApplication application, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
            return false;

        var existing = paths
            .Where(path =>
                !string.IsNullOrWhiteSpace(path) &&
                (File.Exists(path) || Directory.Exists(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (existing.Length == 0)
            return false;

        var target = Target(application);
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var arguments = new List<string>();

        if (application.LaunchTarget is null ||
            string.Equals(
                target,
                application.Identity.ExecutablePath,
                StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(application.Identity.Arguments))
                arguments.Add(application.Identity.Arguments!);
        }

        arguments.AddRange(existing.Select(QuoteArgument));

        return LaunchTarget(
            target,
            string.Join(" ", arguments));
    }

    private static string QuoteArgument(string value)
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

    public bool LaunchTarget(string? target, string? arguments = null, string? verb = null)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        var info = new ApplicationNative.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ApplicationNative.ShellExecuteInfo>(), Mask = 0x400,
            Verb = verb, File = target, Parameters = arguments, Show = 1
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
