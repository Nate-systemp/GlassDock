using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GlassDock.Core.Applications;

public sealed class DockApplicationItem(DockApplication application) : INotifyPropertyChanged
{
    public DockApplication Application { get; private set; } = application;
    public string Id => Application.Id;
    public string Name => Application.Name;
    public bool IsPinned => Application.IsPinned;
    public bool IsRunning => Application.IsRunning;
    public bool IsActive => Application.IsActive;
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(DockApplication application)
    {
        var previous = Application;
        Application = application;
        if (previous.Name != Name || previous.IsPinned != IsPinned || previous.IsRunning != IsRunning ||
            previous.IsActive != IsActive || !ReferenceEquals(previous.Icon, application.Icon) ||
            !previous.Windows.SequenceEqual(application.Windows))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>Apply snapshots on the owning UI thread; retain item identity across running-state changes.</summary>
public sealed class DockApplications
{
    public ObservableCollection<DockApplicationItem> VisibleDockApplications { get; } = [];

    public void Apply(ApplicationSnapshot snapshot)
    {
        var desired = snapshot.Applications.Select(app => app.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = VisibleDockApplications.Count - 1; index >= 0; index--)
            if (!desired.Contains(VisibleDockApplications[index].Id)) VisibleDockApplications.RemoveAt(index);
        for (var index = 0; index < snapshot.Applications.Count; index++)
        {
            var application = snapshot.Applications[index];
            var item = VisibleDockApplications.FirstOrDefault(item => item.Id == application.Id);
            if (item is null) VisibleDockApplications.Insert(index, new(application));
            else
            {
                var previousIndex = VisibleDockApplications.IndexOf(item);
                if (previousIndex != index) VisibleDockApplications.Move(previousIndex, index);
                item.Update(application);
            }
        }
    }
}
