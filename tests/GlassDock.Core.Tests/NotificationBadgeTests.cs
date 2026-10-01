using GlassDock.Core.Applications;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class NotificationBadgeTests
{
    [Fact]
    public void IntroOnlyOnZeroToPositiveAndRemovalCanReverse()
    {
        var state = new NotificationBadgeState();
        Assert.Equal(BadgeTransition.None, state.SetCount(0));
        Assert.Equal(BadgeTransition.Appear, state.SetCount(1));
        Assert.Equal(BadgeTransition.Increase, state.SetCount(2));
        Assert.Equal(BadgeTransition.None, state.SetCount(2));
        Assert.Equal(BadgeTransition.Update, state.SetCount(1));
        Assert.Equal(BadgeTransition.Remove, state.SetCount(0));
        Assert.Equal(BadgeTransition.Appear, state.SetCount(3));
    }

    [Fact]
    public void ActivityDotNeverInventsACountAndCanBecomeExactCount()
    {
        var state = new NotificationBadgeState();
        var activity = BadgeDisplayState.Activity("activity-provider", 10);
        Assert.Equal(BadgeTransition.Appear, state.SetDisplay(activity));
        Assert.True(state.HasActivity);
        Assert.Equal(0, state.Count);
        Assert.Equal(string.Empty, state.Text);

        var counted = BadgeDisplayState.Counted(4, "count-provider", 20);
        Assert.Equal(BadgeTransition.Update, state.SetDisplay(counted));
        Assert.False(state.HasActivity);
        Assert.Equal(4, state.Count);
        Assert.Equal("4", state.Text);
    }

    [Fact]
    public void NegativeCountsClearAndLargeCountsStayBoundedVisually()
    {
        var state = new NotificationBadgeState();
        state.SetCount(int.MaxValue);
        Assert.Equal("99+", state.Text);
        Assert.Equal(int.MaxValue, state.Count);
        Assert.Equal(BadgeTransition.Remove, state.SetCount(-1));
        Assert.Equal(0, state.Count);
    }

    [Fact]
    public void CountsUseExactAumidAndNeverGuessFromExecutableName()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["App.One"] = 3 };
        Assert.Equal(3, NotificationCounts.ForApplication(new("app.one", null), counts));
        Assert.Equal(0, NotificationCounts.ForApplication(new(null, "App.One.exe"), counts));
        Assert.Equal(0, NotificationCounts.ForApplication(new("App.Two", null), counts));
    }

    [Fact]
    public void CoordinatorUsesCanonicalAumidAndDoesNotAddDuplicateProviders()
    {
        var older = DateTimeOffset.UtcNow.AddMinutes(-1);
        var low = new FakeProvider("toast-a", 100, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["com.example.App"] = BadgeSignal.Counted(2, older)
        });
        var high = new FakeProvider("native", 300, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["COM.EXAMPLE.APP"] = BadgeSignal.Counted(5)
        });

        using var coordinator = new BadgeCoordinator([low, high]);
        var selected = coordinator.ForApplication(new("com.example.app", "C:\\Apps\\Example.exe"));

        Assert.Equal(BadgeKind.Count, selected.Kind);
        Assert.Equal(5, selected.Count);
        Assert.Equal("native", selected.SourceId);
        Assert.Equal(300, selected.SourcePriority);
        Assert.NotEqual(7, selected.Count);
    }

    [Fact]
    public void ExactCountBeatsActivityOnlySignalEvenWhenActivityProviderHasHigherPriority()
    {
        var count = new FakeProvider("toast", 100, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["App.One"] = BadgeSignal.Counted(3)
        });
        var activity = new FakeProvider("activity", 500, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["App.One"] = BadgeSignal.Activity()
        });

        using var coordinator = new BadgeCoordinator([count, activity]);
        var selected = coordinator.ForApplication(new("app.one", null));

        Assert.Equal(BadgeKind.Count, selected.Kind);
        Assert.Equal(3, selected.Count);
        Assert.Equal("toast", selected.SourceId);
    }

    [Fact]
    public void CoordinatorFallsBackToActivityDotWhenNoExactCountExists()
    {
        var provider = new FakeProvider("activity", 20, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["App.One"] = BadgeSignal.Activity()
        });

        using var coordinator = new BadgeCoordinator([provider]);
        var selected = coordinator.ForApplication(new("APP.ONE", null));

        Assert.Equal(BadgeKind.Activity, selected.Kind);
        Assert.Equal(0, selected.Count);
        Assert.Equal("Unread activity", selected.AccessibilityText);
    }

    [Fact]
    public void CoordinatorNeverGuessesWithoutCanonicalAumid()
    {
        var provider = new FakeProvider("toast", 100, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["Discord.App"] = BadgeSignal.Counted(8)
        });

        using var coordinator = new BadgeCoordinator([provider]);
        var selected = coordinator.ForApplication(new(null, "C:\\Discord\\Discord.exe"));

        Assert.Equal(BadgeKind.None, selected.Kind);
        Assert.False(selected.IsVisible);
    }

    [Fact]
    public void DiagnosticsContainOnlyIdentityAndBadgeMetadata()
    {
        var provider = new FakeProvider("toast", 100, new Dictionary<string, BadgeSignal>(StringComparer.OrdinalIgnoreCase)
        {
            ["App.One"] = BadgeSignal.Counted(2)
        });

        using var coordinator = new BadgeCoordinator([provider]);
        var raw = Assert.Single(coordinator.GetProviderDiagnostics());
        Assert.Equal("toast", raw.ProviderId);
        Assert.Equal("App.One", raw.AppUserModelId);
        Assert.Equal(2, raw.Count);

        var selected = Assert.Single(coordinator.GetApplicationDiagnostics([new("app.one", null)]));
        Assert.Equal(BadgeKind.Count, selected.Kind);
        Assert.Equal(2, selected.Count);
        Assert.Equal("toast", selected.SelectedSourceId);
    }

    private sealed class FakeProvider : IBadgeProvider
    {
        public FakeProvider(string id, int priority, IReadOnlyDictionary<string, BadgeSignal> snapshot)
        {
            Id = id;
            Priority = priority;
            Snapshot = snapshot;
        }

        public string Id { get; }
        public int Priority { get; }
        public string Status { get; private set; } = "Ready.";
        public IReadOnlyDictionary<string, BadgeSignal> Snapshot { get; private set; }
        public event EventHandler? Changed;
        public event EventHandler? RefreshRequested;

        public Task StartAsync(bool requestPermission = false) => Task.CompletedTask;
        public Task RefreshAsync() => Task.CompletedTask;
        public void Stop() { }
        public void Dispose() { }

        // Keep events real so interface regressions remain compile-time visible.
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
        public void RequestRefresh() => RefreshRequested?.Invoke(this, EventArgs.Empty);
    }
}
