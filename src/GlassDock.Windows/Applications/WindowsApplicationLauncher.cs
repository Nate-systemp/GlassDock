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

    public bool Launch(DockApplication application)
    {
        var target = Target(application);
        return LaunchTarget(target, application.LaunchTarget is null ||
            string.Equals(target, application.Identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            ? application.Identity.Arguments : null);
    }

    public bool LaunchTarget(string? target, string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        var info = new ApplicationNative.ShellExecuteInfo
        {
            Size = (uint)Marshal.SizeOf<ApplicationNative.ShellExecuteInfo>(), Mask = 0x400,
            File = target, Parameters = arguments, Show = 1
        };
        // Preserve the exact shortcut or AppsFolder item, including its launch arguments.
        return ApplicationNative.ShellExecuteEx(ref info);
    }
}
