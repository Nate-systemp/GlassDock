using System.Collections.ObjectModel;
using GlassDock.Core.Applications;
using Microsoft.UI.Dispatching;

namespace GlassDock.App.ViewModels;

public sealed class DockApplicationsViewModel : IDisposable
{
    private readonly IApplicationService service;
    private readonly DispatcherQueue dispatcher;
    private readonly DockApplications applications = new();
    private bool disposed;
    public ObservableCollection<DockApplicationItem> VisibleDockApplications => applications.VisibleDockApplications;
    public string? Warning { get; private set; }
    public event EventHandler? WarningChanged;

    public DockApplicationsViewModel(IApplicationService service, DispatcherQueue dispatcher)
    {
        this.service = service;
        this.dispatcher = dispatcher;
        service.SnapshotChanged += OnSnapshot;
    }

    private void OnSnapshot(object? sender, ApplicationSnapshot snapshot) => dispatcher.TryEnqueue(() =>
    {
        if (disposed) return;
        applications.Apply(snapshot);
        if (Warning == snapshot.Warning) return;
        Warning = snapshot.Warning;
        WarningChanged?.Invoke(this, EventArgs.Empty);
    });

    public bool Activate(DockApplicationItem item) => service.LaunchOrActivate(item.Application);
    public void Dispose()
    {
        disposed = true;
        service.SnapshotChanged -= OnSnapshot;
        service.Dispose();
    }
}
