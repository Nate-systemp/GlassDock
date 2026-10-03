using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class NotificationBadgeWiringTests
{
    [Fact]
    public void BadgeSharesIconTransformWithoutChangingDockMeasurementOrHitTesting()
    {
        var root = FindRoot();
        string Read(string path) => File.ReadAllText(Path.Combine(root.FullName, path));
        var badge = Read("src/GlassDock.App/Controls/NotificationBadge.cs");
        var icon = Read("src/GlassDock.App/Controls/AdaptiveAppIcon.cs");

        Assert.Contains("IsHitTestVisible = false", badge);
        Assert.Contains("Children.Add(badge)", icon);
        Assert.Contains("HorizontalAlignment.Right", icon);
        Assert.Contains("VerticalAlignment.Top", icon);
        Assert.Contains("SetNotificationBadge", icon);
        Assert.Contains("BadgeKind.Activity", badge);
        Assert.Contains("revision == version", badge);
        Assert.Contains("ui.AnimationsEnabled", badge);
        Assert.DoesNotContain("CreateTimer", badge);
    }

    [Fact]
    public void WindowsToastProviderIsReadOnlyEventDrivenAndMetadataOnly()
    {
        var root = FindRoot();
        var provider = File.ReadAllText(Path.Combine(root.FullName,
            "src/GlassDock.Windows/Applications/WindowsToastBadgeProvider.cs"));

        Assert.Contains("IBadgeProvider", provider);
        Assert.Contains("listener.NotificationChanged += NotificationChanged", provider);
        Assert.Contains("listener.NotificationChanged -= NotificationChanged", provider);
        Assert.Contains("listener.GetNotificationsAsync(NotificationKinds.Toast)", provider);
        Assert.Contains("notification.AppInfo?.AppUserModelId", provider);
        Assert.DoesNotContain("notification.Notification.", provider);
        Assert.DoesNotContain("ClearNotifications", provider);
        Assert.DoesNotContain("CreateTimer", provider);
    }

    [Fact]
    public void DesktopUsesCoordinatorInsteadOfReadingProviderCountsDirectly()
    {
        var root = FindRoot();
        var desktop = File.ReadAllText(Path.Combine(root.FullName,
            "src/GlassDock.App/Desktop/DesktopOverlayWindow.cs"));

        Assert.Contains("sharedBadges ?? new BadgeCoordinator([new WindowsToastBadgeProvider()])", desktop);
        Assert.Contains("badges.ForApplication(item.Application.Identity)", desktop);
        Assert.Contains("SetNotificationBadge", desktop);
        Assert.Contains("QueueNotificationBadgeRefresh", desktop);
        Assert.DoesNotContain("NotificationCounts.ForApplication(item.Application.Identity", desktop);
        Assert.DoesNotContain("badges.Counts", desktop);
    }

    [Fact]
    public void CompatibilityServiceContainsNoListenerLogic()
    {
        var root = FindRoot();
        var compatibility = File.ReadAllText(Path.Combine(root.FullName,
            "src/GlassDock.Windows/Applications/WindowsNotificationService.cs"));
        Assert.Contains("WindowsToastBadgeProvider", compatibility);
        Assert.DoesNotContain("UserNotificationListener", compatibility);
        Assert.DoesNotContain("GetNotificationsAsync", compatibility);
    }

    private static DirectoryInfo FindRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return root!;
    }
}
