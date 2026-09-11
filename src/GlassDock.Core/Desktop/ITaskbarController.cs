namespace GlassDock.Core.Desktop;

public readonly record struct TaskbarStatus(bool Available, bool Visible, int Count, bool Enabled = true, bool AutoHide = false);

public interface ITaskbarController
{
    TaskbarStatus Inspect();
    void HideForTest();
    bool MaintainHidden();
    bool Restore();
    bool EmergencyRestore();
}
