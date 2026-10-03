namespace GlassDock.Core.Applications;

public sealed record ApplicationIdentity(string? AppUserModelId, string? ExecutablePath, string? Arguments = null, string? ShellPath = null)
{
    public string Key => !string.IsNullOrWhiteSpace(AppUserModelId) ? "app:" + AppUserModelId.ToUpperInvariant()
        : !string.IsNullOrWhiteSpace(ExecutablePath) ? "exe:" + NormalizePath(ExecutablePath) + "|" + (Arguments ?? "")
        : "shell:" + NormalizePath(ShellPath ?? "");
    public static string NormalizePath(string path) => path.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
}

/// <summary>Premultiplied BGRA pixels supplied by the OS icon service, independent of UI frameworks.</summary>
public sealed record ApplicationIcon(int Width, int Height, byte[] Pixels);
public sealed record PinnedApplication(ApplicationIdentity Identity, string Name, string LaunchTarget, ApplicationIcon? Icon);
public sealed record ApplicationWindow(ApplicationIdentity Identity, string Name, long Handle, int ProcessId,
    long ProcessStartTicks, bool IsActive, ApplicationIcon? Icon, string Title = "", bool IsMinimized = false,
    long LastActivatedTicks = 0);
public sealed record DockApplication(string Id, ApplicationIdentity Identity, string Name, string? LaunchTarget,
    bool IsPinned, IReadOnlyList<ApplicationWindow> Windows, ApplicationIcon? Icon)
{
    public DockStack? Stack { get; init; }
    public IReadOnlyList<DockApplication> StackApps { get; init; } = [];
    public bool IsRunning => Windows.Count > 0;
    public bool IsActive => Windows.Any(window => window.IsActive);
}
public sealed record ApplicationSnapshot(IReadOnlyList<DockApplication> Applications, string? Warning = null);

public interface IApplicationService : IDisposable
{
    event EventHandler<ApplicationSnapshot>? SnapshotChanged;
    void Start();
    void RequestRefresh();
    bool LaunchOrActivate(DockApplication application);
    bool Launch(DockApplication application);
    bool ActivateWindow(ApplicationWindow window);
    bool CloseWindow(ApplicationWindow window);
    bool SetPinned(DockApplication application, bool pinned);
    bool RunAsAdministrator(DockApplication application);
    bool OpenFileLocation(DockApplication application);
}
