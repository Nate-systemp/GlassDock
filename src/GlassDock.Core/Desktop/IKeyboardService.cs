namespace GlassDock.Core.Desktop;

public interface IKeyboardService : IDisposable
{
    event EventHandler? HomeRequested;
    event EventHandler? LauncherRequested;
    event EventHandler? RecoveryRequested;

    bool IsRegistered { get; }
}