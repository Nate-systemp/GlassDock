namespace GlassDock.Core.Desktop;

public interface IKeyboardService : IDisposable
{
    event EventHandler? HomeRequested;
    event EventHandler? RecoveryRequested;
    bool IsRegistered { get; }
}
