using GlassDock.Core.Applications;
using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class HomeDashboardTests
{
    [Theory]
    [InlineData(1920, 1080, 1)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(1920, 1080, 2)]
    [InlineData(1366, 768, 1)]
    [InlineData(1280, 720, 1.5)]
    [InlineData(1024, 768, 2)]
    public void CardsFitOneScreenWithoutOverlap(double pixelsWide, double pixelsHigh, double dpi)
    {
        var availableWidth = pixelsWide / dpi - 24;
        var availableHeight = pixelsHigh / dpi - 140;
        foreach (var spacing in new[] { 0d, 6, 20 })
        {
            var layout = HomeDashboardLayout.Create(availableWidth, availableHeight, spacing);
            Assert.True(layout.Width <= availableWidth && layout.Height <= availableHeight);
            Assert.Equal(7, layout.Cards.Count);
            foreach (var card in layout.Cards)
            {
                Assert.True(card.X >= 0 && card.Y >= 0 && card.Width > 0 && card.Height > 0);
                Assert.True((card.X + card.Width) * layout.ContentScale <= layout.Width + .001);
                Assert.True((card.Y + card.Height) * layout.ContentScale <= layout.Height + .001);
                foreach (var other in layout.Cards.Where(c => c != card))
                    Assert.True(card.X + card.Width <= other.X + .001 || other.X + other.Width <= card.X + .001 ||
                        card.Y + card.Height <= other.Y + .001 || other.Y + other.Height <= card.Y + .001);
            }
        }
    }

    [Fact]
    public void PinsAndRunningAppsUseActualMembersAndPreserveDockOrder()
    {
        var pinned = App("pinned", true, false);
        var running = App("running", false, true);
        var both = App("both", true, true);
        var stack = App("stack", true, false) with { Stack = new("stack", "Group", [both.Id]), StackApps = [both] };
        var source = new[] { pinned, stack, running, both };
        Assert.Equal(new[] { pinned, both }, HomeApplications.Select(source, false));
        Assert.Equal(new[] { both, running }, HomeApplications.Select(source, true));
        Assert.Same(both.Windows, HomeApplications.Select(source, true)[0].Windows);
        Assert.Empty(HomeApplications.Select([], false));
    }

    [Fact]
    public void RepeatedAppearanceChangesCannotChangeCardGeometry()
    {
        var baseline = HomeDashboardLayout.Create(1100, 650, 6);
        foreach (var _ in Enumerable.Range(0, 50))
        {
            var next = HomeDashboardLayout.Create(1100, 650, 6);
            Assert.Equal(baseline.Cards, next.Cards);
            Assert.Equal(baseline.ContentScale, next.ContentScale);
        }
    }

    private static DockApplication App(string name, bool pin, bool running)
    {
        var identity = new ApplicationIdentity(name, null);
        return new(identity.Key, identity, name, "shell:AppsFolder\\" + name, pin,
            running ? [new(identity, name, 123, 1, 10, false, null)] : [], null);
    }

    [Fact]
    public void GlassCardsReduceOverlappingShadowsWithoutChangingOpticalSettings()
    {
        var material = new GlassMaterial { BlurAmount = 24, Opacity = .7, Tint = 0x183050 };
        foreach (var mode in new[] { DockAppearanceMode.Frosted, DockAppearanceMode.Acrylic, DockAppearanceMode.Clear })
        {
            var card = UtilityMaterial.ForDashboardCard(material, mode);
            Assert.Equal(material.BlurAmount, card.BlurAmount);
            Assert.Equal(material.Opacity, card.Opacity);
            Assert.Equal(material.Tint, card.Tint);
            Assert.True(card.ShadowOpacity < material.ShadowOpacity);
            Assert.True(card.ShadowBlur < material.ShadowBlur);
            Assert.True(card.ShadowOffset < material.ShadowOffset);
        }
        Assert.Same(material, UtilityMaterial.ForDashboardCard(material, DockAppearanceMode.Light));
        Assert.Same(material, UtilityMaterial.ForDashboardCard(material, DockAppearanceMode.Dark));
        Assert.True(UtilityMaterial.DashboardContrast(DockAppearanceMode.Frosted, .7) >
            UtilityMaterial.DashboardContrast(DockAppearanceMode.Clear, .7));
        Assert.True(UtilityMaterial.DashboardContrast(DockAppearanceMode.Acrylic, .8) >
            UtilityMaterial.DashboardContrast(DockAppearanceMode.Acrylic, .4));
    }
}
