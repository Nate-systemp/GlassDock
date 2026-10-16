using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>Protects Split Dock's click-expansion, auto-collapse inhibition, and calendar layout.</summary>
public sealed class SplitUtilitiesCalendarHoverWiringTests
{
    private static string Read(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "GlassDock.App", "Desktop", file));
    }

    [Fact]
    public void Split_layout_is_explicit_and_has_no_hover_expansion_or_timeout()
    {
        var pill = Read("FloatingUtilitiesWindow.cs");
        Assert.DoesNotContain("SetExpanded(true)", pill);
        Assert.DoesNotContain("autoCollapseTimer", pill);
        Assert.Contains("value.UtilitiesLayout == UtilitiesLayout.Expanded", pill);
        Assert.Contains("settings.UtilitiesLayout != UtilitiesLayout.Hidden", pill);
        Assert.Contains("CalendarRequested?.Invoke()", pill);
        Assert.Contains("ShowLayoutMenu()", pill);
        Assert.Contains("VirtualKey.Escape", pill);
        Assert.Contains("hold.Move(point.X, point.Y, group is null)", pill);
        Assert.Contains("FocusGroup(group)", pill);
        Assert.Contains("LayoutRequested", Read("DesktopOverlayWindow.cs"));
    }
    [Fact]
    public void Split_clock_uses_right_aligned_vertical_doky_calendar_and_other_modes_keep_their_layout()
    {
        var popup = Read("CalendarPopoverWindow.cs");
        Assert.Contains("bool compactSplit = false", popup, StringComparison.Ordinal);
        Assert.Contains("SplitPanelWidth = 342", popup, StringComparison.Ordinal);
        Assert.Contains("SplitPanelHeight = 448", popup, StringComparison.Ordinal);
        Assert.Contains("PositionNearSplitClock(AppWindow owner, double scale)", popup, StringComparison.Ordinal);
        Assert.Contains("if (compactSplit) Grid.SetRow(calendarCard, 1)", popup, StringComparison.Ordinal);
        Assert.Contains("else Grid.SetColumn(calendarCard, 1)", popup, StringComparison.Ordinal);
        var dock = Read("DesktopOverlayWindow.cs");
        Assert.Contains("compactSplit: utilitiesPlacement == UtilitiesPlacement.SplitDock", dock, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_focus_groups_use_soft_bordered_padded_dpi_aware_frost_with_opacity_attenuation()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var focusGroup = File.ReadAllText(Path.Combine(directory!.FullName, "src", "GlassDock.App", "Controls", "UtilitiesFocusGroup.cs"));

        Assert.DoesNotContain("EffectBorderMode.Hard", focusGroup, StringComparison.Ordinal);
        Assert.Contains("BorderMode = EffectBorderMode.Soft", focusGroup, StringComparison.Ordinal);
        Assert.Contains("RasterizationScale", focusGroup, StringComparison.Ordinal);
        Assert.Contains("FrostedBlurDip = 1.2f", focusGroup, StringComparison.Ordinal);
        Assert.Contains("FrostedOpacity = 0.70f", focusGroup, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", focusGroup, StringComparison.Ordinal);
        Assert.Contains("surface.SourceOffset = new Vector2(-bleedPx, -bleedPx)", focusGroup, StringComparison.Ordinal);
    }
}
