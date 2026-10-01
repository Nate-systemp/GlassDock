using Xunit;
namespace GlassDock.Windows.Tests;

public sealed class NotificationBadgeWiringTests
{
    [Fact]
    public void BadgeSharesIconTransformWithoutChangingDockMeasurementOrHitTesting()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string Read(string path) => File.ReadAllText(Path.Combine(root.FullName, path));
        var badge = Read("src/GlassDock.App/Controls/NotificationBadge.cs");
        var icon = Read("src/GlassDock.App/Controls/AdaptiveAppIcon.cs");
        var listener = Read("src/GlassDock.Windows/Applications/WindowsNotificationService.cs");
        Assert.Contains("IsHitTestVisible = false", badge);
        Assert.Contains("Children.Add(badge)", icon);
        Assert.Contains("HorizontalAlignment.Right", icon);
        Assert.Contains("VerticalAlignment.Top", icon);
        Assert.Contains("revision == version", badge);
        Assert.Contains("ui.AnimationsEnabled", badge);
        Assert.DoesNotContain("CreateTimer", badge);
        Assert.Contains("listener.NotificationChanged -= NotificationChanged", listener);
        Assert.Contains("listener.GetNotificationsAsync(NotificationKinds.Toast)", listener);
        Assert.DoesNotContain("notification.Notification", listener);
        Assert.DoesNotContain("ClearNotifications", listener);
    }
}
