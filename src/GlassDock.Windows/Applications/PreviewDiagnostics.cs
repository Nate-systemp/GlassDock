using System.Diagnostics;
using System.Text.Json;
using GlassDock.Core.Applications;
using GlassDock.Windows.Interop;

namespace GlassDock.Windows.Applications;

/// <summary>Bounded local metadata log; never writes captured pixels or window titles.</summary>
internal sealed class PreviewDiagnostics
{
    private readonly object gate = new();
    public string Path { get; } = System.IO.Path.Combine(
        Settings.DokyUserData.DirectoryPath, "preview-diagnostics.jsonl");

    public void Write(ApplicationWindow? window, string stage, bool active, WindowFrameCache.Frame? frame, object? detail = null)
    {
        try
        {
            string? process = null;
            if (window is not null)
            {
                try { using var owner = Process.GetProcessById(window.ProcessId); process = owner.ProcessName; }
                catch (ArgumentException) { process = "exited"; }
            }
            var line = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow, stage,
                hwnd = window is null ? null : $"0x{window.Handle:X}", process,
                processId = window?.ProcessId, processStarted = window?.ProcessStartTicks,
                minimized = window is not null && NativeMethods.IsIconic((nint)window.Handle),
                foregroundHwnd = $"0x{ApplicationNative.GetForegroundWindow():X}",
                captureActive = active, lastValidFrame = frame?.CapturedAt,
                frameWidth = frame?.Width, frameHeight = frame?.Height, detail
            });
            lock (gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 4 * 1024 * 1024)
                    File.Move(Path, Path + ".previous", true);
                File.AppendAllText(Path, line + Environment.NewLine);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Debug.WriteLine($"Preview diagnostics unavailable: {error.Message}"); }
    }
}
