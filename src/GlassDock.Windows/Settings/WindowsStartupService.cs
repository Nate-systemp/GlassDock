using Microsoft.Win32;

namespace GlassDock.Windows.Settings;

/// <summary>
/// Owns GlassDock's per-user Windows sign-in registration.
/// No polling or background worker is used.
/// </summary>
public sealed class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GlassDock";
    private readonly string executablePath;

    public WindowsStartupService(string? executablePath = null)
    {
        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new InvalidOperationException("Doky executable path is unavailable.");

        executablePath = Path.GetFullPath(executablePath);
        if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Doky startup registration requires the application executable.");

        this.executablePath = executablePath;
    }

    public string StartupCommand => $"\"{executablePath}\" --startup";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
               string.Equals(value.Trim(), StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows startup registration could not be opened.");
            key.SetValue(ValueName, StartupCommand, RegistryValueKind.String);
            return;
        }

        using var existing = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existing?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
