using System.Collections.ObjectModel;
using GlassDock.Core.Applications;
using Microsoft.UI.Dispatching;

namespace GlassDock.App.ViewModels;

public sealed class DockApplicationsViewModel : IDisposable
{
    private readonly IApplicationService service;
    private readonly ApplicationSnapshotPump snapshots;
    private readonly DockApplications applications = new();
    private readonly bool ownsService;
    private readonly Func<ApplicationSnapshot, ApplicationSnapshot>? snapshotTransform;
    private bool disposed;
    public ObservableCollection<DockApplicationItem> VisibleDockApplications => applications.VisibleDockApplications;
    public string? Warning { get; private set; }
    public event EventHandler? WarningChanged;
    public event EventHandler? SnapshotApplied;

    public DockApplicationsViewModel(
        IApplicationService service,
        DispatcherQueue dispatcher,
        bool ownsService = true,
        Func<ApplicationSnapshot, ApplicationSnapshot>? snapshotTransform = null)
    {
        this.service = service;
        snapshots = new(action => dispatcher.TryEnqueue(() => action()), ApplySnapshot);
        this.ownsService = ownsService;
        this.snapshotTransform = snapshotTransform;
        service.SnapshotChanged += OnSnapshot;
    }

    private void OnSnapshot(object? sender, ApplicationSnapshot snapshot)
    {
        snapshots.Publish(snapshot);
    }

    private void ApplySnapshot(ApplicationSnapshot snapshot)
    {
        if (disposed) return;
        var visible = snapshotTransform?.Invoke(snapshot) ?? snapshot;
        applications.Apply(visible);
        SnapshotApplied?.Invoke(this, EventArgs.Empty);
        if (Warning == visible.Warning) return;
        Warning = visible.Warning;
        WarningChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshFilter()
    {
        snapshots.Reapply();
    }

    public bool Activate(DockApplicationItem item) => service.LaunchOrActivate(item.Application);
    public bool Launch(DockApplicationItem item) => service.Launch(item.Application);
    public bool ActivateWindow(ApplicationWindow window) => service.ActivateWindow(window);
    public bool CloseWindow(ApplicationWindow window) => service.CloseWindow(window);
    public bool SetPinned(DockApplicationItem item, bool pinned) => service.SetPinned(item.Application, pinned);
    public bool RunAsAdministrator(DockApplicationItem item) => service.RunAsAdministrator(item.Application);
    public bool OpenFileLocation(DockApplicationItem item) => service.OpenFileLocation(item.Application);
    public void Dispose()
    {
        disposed = true;
        snapshots.Dispose();
        service.SnapshotChanged -= OnSnapshot;
        if (ownsService)
            service.Dispose();
    }
}
