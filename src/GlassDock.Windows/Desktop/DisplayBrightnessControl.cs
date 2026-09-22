using System.Management;

namespace GlassDock.Windows.Desktop;

/// <summary>Windows monitor provider, normally exposed by the built-in panel. No synthetic dimming.</summary>
public static class DisplayBrightnessControl
{
    public static Task<int?> ReadAsync() => Task.Run(() => Execute(null));
    public static Task<int?> SetAsync(int percent) => Task.Run(() => Execute(Math.Clamp(percent, 0, 100)));

    private static int? Execute(int? percent)
    {
        try
        {
            using var query = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var monitors = query.Get();
            // Do not choose an arbitrary monitor when several brightness providers exist.
            if (monitors.Count != 1) return null;
            string? instance = null;
            int? current = null;
            foreach (ManagementObject monitor in monitors)
            {
                using (monitor)
                {
                    instance = (string)monitor["InstanceName"];
                    current = Convert.ToInt32(monitor["CurrentBrightness"]);
                }
            }
            if (percent is null || instance is null) return current;
            using var methodQuery = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE");
            using var methods = methodQuery.Get();
            foreach (ManagementObject method in methods)
            {
                using (method)
                {
                    if ((string)method["InstanceName"] != instance) continue;
                    using var parameters = method.GetMethodParameters("WmiSetBrightness");
                    parameters["Timeout"] = 0u;
                    parameters["Brightness"] = (byte)percent.Value;
                    using var result = method.InvokeMethod("WmiSetBrightness", parameters, null);
                    // Some monitor providers return no output object despite applying the write.
                    // An explicit error still fails; otherwise verify the actual value below.
                    if (result is not null && Convert.ToUInt32(result["ReturnValue"]) != 0) return null;
                    return Execute(null); // Display the provider's actual readback.
                }
            }
        }
        catch (Exception) { /* Driver/provider/access failure: disable the control. */ }
        return null;
    }
}
