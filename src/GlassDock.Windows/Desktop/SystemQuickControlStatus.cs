using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;
using Windows.System.Power;

namespace GlassDock.Windows.Desktop;

public sealed record QuickControlStatus(bool? EnergySaver, bool? Hotspot);

/// <summary>Read-only public Windows status. Unknown is never presented as Off.</summary>
public static class SystemQuickControlStatus
{
    // Profile/capability queries need not block the window dispatcher. No retained
    // profile handles, extra polling timer, capability escalation or state writes.
    public static Task<QuickControlStatus> ReadAsync() => Task.Run(() => new QuickControlStatus(
        ReadSafely(() => EnergySaverEnabled(PowerManager.EnergySaverStatus)), ReadSafely(ReadHotspot)));

    internal static bool? EnergySaverEnabled(EnergySaverStatus status) => status switch
        {
            EnergySaverStatus.On => true,
            EnergySaverStatus.Off or EnergySaverStatus.Disabled => false,
            _ => null
        };

    private static bool? ReadHotspot()
    {
        var profiles = NetworkInformation.GetConnectionProfiles();
        // Hotspot may share a different adapter from the default internet route.
        return Combine(profiles.Select(profile => ReadSafely(() =>
        {
            if (NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile) != TetheringCapability.Enabled)
                return null;
            return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile).TetheringOperationalState switch
            {
                TetheringOperationalState.On => true,
                TetheringOperationalState.Off => false,
                _ => null
            };
        })));
    }

    internal static bool? ReadSafely(Func<bool?> read)
    {
        try { return read(); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        { return null; }
    }

    internal static bool? Combine(IEnumerable<bool?> states)
    {
        var any = false;
        var unknown = false;
        foreach (var state in states)
        {
            if (state == true) return true;
            any = true;
            unknown |= !state.HasValue;
        }
        return any && !unknown ? false : null;
    }
}
