namespace GlassDock.Core.Desktop;

/// <summary>One pending destination, never a FIFO of old clicks.</summary>
public sealed class UtilityPopupRequests
{
    public static bool ShouldDismissOnDeactivation(bool utilityOwnsPointer, int observedVersion, int currentVersion) =>
        !utilityOwnsPointer && observedVersion == currentVersion;

    public int? Active { get; private set; }
    public int? Pending { get; private set; }
    public bool TargetOpen { get; private set; }
    public void Click(int key)
    {
        if (Active == key)
        {
            TargetOpen = Pending.HasValue || !TargetOpen;
            Pending = null;
        }
        else if (Active.HasValue)
        {
            Pending = Pending == key ? null : key;
            TargetOpen = false;
        }
        else { Active = key; TargetOpen = true; }
    }
    public void ClosingExternally() { TargetOpen = false; }
    public void Closed()
    {
        Active = Pending;
        Pending = null;
        TargetOpen = Active.HasValue;
    }
    public void Reset() { Active = Pending = null; TargetOpen = false; }
}
