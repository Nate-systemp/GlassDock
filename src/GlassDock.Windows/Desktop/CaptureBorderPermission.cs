using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace GlassDock.Windows.Desktop;

/// <summary>Windows owns consent. Capture creation never prompts or assumes a setter grants it.</summary>
public static class CaptureBorderPermission
{
    public static bool IsSupported => ApiInformation.IsMethodPresent(
        "Windows.Graphics.Capture.GraphicsCaptureAccess", "RequestAccessAsync") &&
        ApiInformation.IsPropertyPresent("Windows.Graphics.Capture.GraphicsCaptureSession", "IsBorderRequired");

    public static bool IsAllowed
    {
        get
        {
            if (!IsSupported) return false;
            try { return AppCapability.Create("graphicsCaptureWithoutBorder").CheckAccess() == AppCapabilityAccessStatus.Allowed; }
            catch (Exception error) when (IsAccessFailure(error)) { return false; }
        }
    }

    public static async Task<string> RequestAsync()
    {
        if (!IsSupported) return "Windows does not support borderless capture here. The capture indicator will remain visible.";
        try
        {
            var access = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            return access == AppCapabilityAccessStatus.Allowed
                ? "Permission granted. Restart Doky to apply it to all captures. Other recording apps may still require an indicator."
                : $"Windows requires the capture indicator: permission was not granted ({access}).";
        }
        catch (Exception error) when (IsAccessFailure(error))
        {
            return $"Windows requires the capture indicator: permission is unavailable (0x{error.HResult:X8}). The registered Doky package needs the borderless capture capability.";
        }
    }

    public static void Configure(GraphicsCaptureSession session)
    {
        if (!IsAllowed) return;
        try { session.IsBorderRequired = false; }
        catch (Exception error) when (IsAccessFailure(error))
        { System.Diagnostics.Debug.WriteLine($"[CaptureBorder] Indicator retained: 0x{error.HResult:X8}"); }
    }

    private static bool IsAccessFailure(Exception error) =>
        error is COMException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException;
}
