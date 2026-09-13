using System.Runtime.InteropServices;
using GlassDock.Core.Applications;

namespace GlassDock.Windows.Applications;

public sealed class WindowsApplicationLauncher
{
    public static string? Target(DockApplication application) => application.LaunchTarget ??
        (application.Identity.AppUserModelId?.Contains('!') == true ? "shell:AppsFolder\\" + application.Identity.AppUserModelId
            : application.Identity.ExecutablePath);

    public bool Launch(DockApplication application)
    {
        var target = Target(application);
        if (string.IsNullOrWhiteSpace(target)) return false;
        var info = new ApplicationNative.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ApplicationNative.ShellExecuteInfo>(), Mask = 0x400,
            File = target, Parameters = application.LaunchTarget is null ||
                string.Equals(target, application.Identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                ? application.Identity.Arguments : null, Show = 1
        };
        // Preserve the exact shortcut or AppsFolder item, including its launch arguments.
        return ApplicationNative.ShellExecuteEx(ref info);
    }
}
