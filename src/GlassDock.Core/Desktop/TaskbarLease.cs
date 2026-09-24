namespace GlassDock.Core.Desktop;

/// <summary>A renewable liveness deadline, with an additional cap for manual tests.</summary>
public sealed class TaskbarLease(bool whileAppActive)
{
    private TimeSpan lastHeartbeat;
    private TimeSpan? hiddenAt;

    public void Heartbeat(TimeSpan now) => lastHeartbeat = now;
    public void Hidden(TimeSpan now) => hiddenAt ??= now;

    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(10);

    public bool IsExpired(TimeSpan now) =>
        now - lastHeartbeat >= HeartbeatTimeout ||
        (!whileAppActive && hiddenAt is { } start && now - start >= TimeSpan.FromSeconds(60));
}
