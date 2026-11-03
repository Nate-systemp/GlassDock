namespace GlassDock.Core.Desktop;

/// <summary>One pointer gesture can toggle, drag, or request details, never more than one.</summary>
public sealed class UtilityPointerGesture
{
    public bool PrimaryDown { get; private set; }
    public bool SecondaryDown { get; private set; }
    public bool IsDragging { get; private set; }
    public bool SuppressPrimary { get; private set; }
    private bool detailsRequested;

    public void Press(bool primary, bool secondary)
    {
        PrimaryDown = primary && !secondary;
        SecondaryDown = secondary && !primary;
        IsDragging = false; detailsRequested = primary && secondary;
        SuppressPrimary = !PrimaryDown;
    }

    public bool BeginDrag()
    {
        if (!PrimaryDown || detailsRequested) return false;
        IsDragging = SuppressPrimary = true;
        return true;
    }

    public bool RequestDetails()
    {
        if (IsDragging || detailsRequested) return false;
        detailsRequested = SuppressPrimary = true;
        return true;
    }

    public bool Hold() => PrimaryDown && RequestDetails();
    public bool ReleaseSecondary()
    {
        if (!SecondaryDown) return false;
        SecondaryDown = false;
        return RequestDetails();
    }
    public void Release() { PrimaryDown = SecondaryDown = IsDragging = false; }
    public void Cancel() { Release(); SuppressPrimary = true; detailsRequested = true; }
    public void Keyboard() { Release(); SuppressPrimary = detailsRequested = false; }
}
