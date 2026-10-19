using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

namespace GlassDock.Windows.Desktop;

public sealed record HotspotChangeResult(bool Succeeded, string Message);

/// <summary>Uses Windows' existing hotspot configuration; never changes credentials or radio policy.</summary>
public sealed class MobileHotspotControl
{
    private int busy;
    internal sealed record Endpoint(Func<TetheringOperationalState> Read, Func<bool, Task<HotspotChangeResult>> Set);

    public Task<HotspotChangeResult> ToggleAsync(CancellationToken cancellation = default) =>
        ToggleAsync(Resolve, cancellation);

    internal async Task<HotspotChangeResult> ToggleAsync(Func<Endpoint?> resolve, CancellationToken cancellation = default)
    {
        if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            return new(false, "Hotspot is already changing. Please wait.");
        try
        {
            return await Task.Run(async () =>
            {
                cancellation.ThrowIfCancellationRequested();
                var endpoint = resolve();
                if (endpoint is null) return new HotspotChangeResult(false, "Windows cannot control Hotspot for the current connection.");
                var state = endpoint.Read();
                if (state is not (TetheringOperationalState.On or TetheringOperationalState.Off))
                    return new(false, "Hotspot state is changing or unavailable. Please try again shortly.");
                cancellation.ThrowIfCancellationRequested();
                // Once Windows accepts a request, let it complete. Canceling the
                // popup must not issue a second operation or reverse the request.
                return await endpoint.Set(state == TetheringOperationalState.Off);
            }, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return new(false, "Hotspot change canceled."); }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
        { return new(false, "Windows denied or could not complete the Hotspot change."); }
        finally { Volatile.Write(ref busy, 0); }
    }

    private static Endpoint? Resolve()
    {
        NetworkOperatorTetheringManager? active = null;
        foreach (var profile in NetworkInformation.GetConnectionProfiles())
        {
            var manager = Manager(profile);
            if (manager?.TetheringOperationalState == TetheringOperationalState.On) { active = manager; break; }
            if (manager?.TetheringOperationalState == TetheringOperationalState.InTransition)
                return Bind(manager);
        }
        // Stop the running hotspot even if the default internet route changed.
        // Start only on Windows' current internet connection, never an arbitrary saved profile.
        var target = active ?? Manager(NetworkInformation.GetInternetConnectionProfile());
        return target is null ? null : Bind(target);
    }

    private static NetworkOperatorTetheringManager? Manager(ConnectionProfile? profile)
    {
        if (profile is null) return null;
        try
        {
            return NetworkOperatorTetheringManager.GetTetheringCapabilityFromConnectionProfile(profile) == TetheringCapability.Enabled
                ? NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile) : null;
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or ArgumentException)
        { return null; }
    }

    private static Endpoint Bind(NetworkOperatorTetheringManager manager) => new(
        () => manager.TetheringOperationalState,
        async on =>
        {
            var result = on ? await manager.StartTetheringAsync() : await manager.StopTetheringAsync();
            return result.Status == TetheringOperationStatus.Success
                ? new(true, "Hotspot change completed.")
                : new(false, "Windows could not change Hotspot: " + result.Status + ".");
        });
}
