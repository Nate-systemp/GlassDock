using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class DockAppearanceRuntimeWiringTests
{
    [Fact]
    public void Applied_appearance_reconfigures_layout_and_uses_opaque_plain_dock()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var dock = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GlassDock.App",
            "Desktop",
            "DesktopOverlayWindow.cs")).ReplaceLineEndings("\n");
        var animation = File.ReadAllText(Path.Combine(
            directory.FullName,
            "src",
            "GlassDock.App",
            "Desktop",
            "DockAnimationController.cs")).ReplaceLineEndings("\n");

        Assert.Contains("icons.Spacing = appearance.IconSpacing;", dock, StringComparison.Ordinal);
        // Responsive dock sizing must remain wired to the current appearance geometry
        // without relying on the legacy app-only TargetDockWidth cap.
        Assert.Contains("count * Appearance.ButtonWidth", dock, StringComparison.Ordinal);
        Assert.Contains("Math.Max(0, count - 1) * Appearance.IconSpacing", dock, StringComparison.Ordinal);
        Assert.Contains("windowManager.CurrentHostWidthDips", dock, StringComparison.Ordinal);
        Assert.Contains("applicationWidth + UtilityClusterWidth", dock, StringComparison.Ordinal);
        Assert.Contains("new AdaptiveAppIcon(Appearance.IconSize, DockHoverMagnification<FrameworkElement>.HoverScale, showTile: false)", dock, StringComparison.Ordinal);
        Assert.Contains("icon.Configure(appearance.IconSize, DockHoverMagnification<FrameworkElement>.HoverScale);", dock, StringComparison.Ordinal);
        Assert.DoesNotContain("animation.SetMaximumMagnificationScale", dock, StringComparison.Ordinal);
        Assert.Contains("UseSolidSurface = true", dock, StringComparison.Ordinal);
        Assert.Contains("? settingsSession.Current.ClearCircularHighlightsEnabled : settingsSession.Current.CircularHighlightsEnabled;", dock, StringComparison.Ordinal);
        Assert.Contains("liquidGlass.CircularHighlightsEnabled = settingsSession.Current.ClearCircularHighlightsEnabled;", dock, StringComparison.Ordinal);
        Assert.Contains("desktopBackdrop.SetSolidAppearance(mode, opacity, cornerRadius);", dock, StringComparison.Ordinal);
        Assert.Contains("const double opacity = 1;", dock, StringComparison.Ordinal);
        Assert.Contains("dockWaveRim.StrokeThickness = 0;", dock, StringComparison.Ordinal);
        // The detached Utilities pill uses Frosted glass, while docked Utilities
        // follow the selected dock appearance. Validate the mapping and each
        // popup's use of its resolved mode (rather than requiring the old call).
        Assert.Contains("private DockAppearanceMode UtilityPopupMode(DockAppearanceMode dockMode)", dock, StringComparison.Ordinal);
        Assert.Contains("utilitiesPlacement == UtilitiesPlacement.InsideDock ? dockMode : DockAppearanceMode.Frosted;", dock, StringComparison.Ordinal);
        Assert.Contains("var utilityMode = UtilityPopupMode(dockAppearance);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemQuickSettingsWindow?.ApplyAppearance(appearance, utilityMode);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemTrayWindow?.ApplyAppearance(appearance, utilityMode);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedCalendarPopoverWindow?.ApplyAppearance(appearance, utilityMode);", dock, StringComparison.Ordinal);
        // Live placement changes must refresh already-created popup windows.
        Assert.Contains("var mode = UtilityPopupMode(settingsSession.Current.DockAppearanceMode);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemQuickSettingsWindow?.ApplyAppearance(Appearance, mode);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedSystemTrayWindow?.ApplyAppearance(Appearance, mode);", dock, StringComparison.Ordinal);
        Assert.Contains("cachedCalendarPopoverWindow?.ApplyAppearance(Appearance, mode);", dock, StringComparison.Ordinal);
        Assert.Contains("double targetHeight = 68", animation, StringComparison.Ordinal);
        Assert.Contains("DockHoverMagnification<FrameworkElement>", animation, StringComparison.Ordinal);
        Assert.DoesNotContain("MagnificationSigma", animation, StringComparison.Ordinal);
        var hoverFrame = animation[animation.IndexOf("private void TickMagnification()", StringComparison.Ordinal)..
            animation.IndexOf("private static AdaptiveAppIcon? Artwork", StringComparison.Ordinal)];
        Assert.DoesNotContain("surface.Width", hoverFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("surface.Height", hoverFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("SetExpandedSize", hoverFrame, StringComparison.Ordinal);
        var adaptiveIcon = File.ReadAllText(Path.Combine(
            directory.FullName, "src", "GlassDock.App", "Controls", "AdaptiveAppIcon.cs"));
        // Hover spacing must use icon slots, not the sometimes tiny alpha bounds.
        Assert.Contains("internal double HoverArtworkWidth => Width;", adaptiveIcon, StringComparison.Ordinal);
        Assert.Contains("artworkVisual.Scale = Vector3.One;", hoverFrame, StringComparison.Ordinal);
        Assert.Contains("ElementCompositionPreview.GetElementVisual(element).Scale = Vector3.One;", hoverFrame, StringComparison.Ordinal);
        Assert.Contains("DrawHoverArtworkInForeground(artwork, frame.Scale, liveImages);", hoverFrame, StringComparison.Ordinal);
        // Only the icon artwork animates; button positions and running
        // indicators remain in their original XAML slots.
        Assert.Contains("element.Translation = Vector3.Zero;", hoverFrame, StringComparison.Ordinal);
        Assert.DoesNotContain("new Vector3((float)frame.OffsetX", hoverFrame, StringComparison.Ordinal);
        // The original wave stays active but is always below icon artwork.
        Assert.Contains("Canvas.SetZIndex(liquidGlass.Panel, 1);", dock, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetZIndex(icons, 20);", dock, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(hoverArtworkForeground);", dock, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetZIndex(hoverArtworkForeground, 1000);", dock, StringComparison.Ordinal);
        Assert.Contains("new DockAnimationController(surface, icons, indicator, hoverArtworkForeground)", dock, StringComparison.Ordinal);
        // The full-size foreground mirror is rendered at root level rather
        // than scaled inside the Button/ContentPresenter's clipping viewport.
        Assert.Contains("hoverArtworkForeground.Children.Add(mirror);", animation, StringComparison.Ordinal);
        Assert.Contains("new global::Windows.Foundation.Rect(0, 0, width, height)", animation, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetLeft(mirror, anchor.X + (bounds.Left - anchor.X) * scale);", animation, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetTop(mirror, anchor.Y + (bounds.Top - anchor.Y) * scale);", animation, StringComparison.Ordinal);
        Assert.Contains("image.Opacity = 0;", animation, StringComparison.Ordinal);
        Assert.Contains("pair.Key.Opacity = 1;", animation, StringComparison.Ordinal);
        // Notification badges need an independently mirrored foreground
        // visual: leaving the badge in the unscaled button detached it from
        // the enlarged icon and sometimes hid it under the glass wave.
        Assert.Contains("DrawHoverBadgeInForeground(artwork, frame.Scale, liveBadges);", hoverFrame, StringComparison.Ordinal);
        Assert.Contains("badge.TransformToVisual(hoverArtworkForeground)", animation, StringComparison.Ordinal);
        Assert.Contains("icon.HoverArtworkBaseline", animation, StringComparison.Ordinal);
        Assert.Contains("mirror.Visual.SetBadge(display);", animation, StringComparison.Ordinal);
        Assert.Contains("mirror.Scale.ScaleX = mirror.Scale.ScaleY = scale;", animation, StringComparison.Ordinal);
        Assert.Contains("Canvas.SetZIndex(mirror.Visual, 10_000 +", animation, StringComparison.Ordinal);
        Assert.Contains("badge.Opacity = 0;", animation, StringComparison.Ordinal);
        Assert.Contains("pair.Key.Opacity = 1;", animation, StringComparison.Ordinal);
        Assert.Contains("icon.HoverBadgeChanged -= OnHoverBadgeChanged;", animation, StringComparison.Ordinal);
        Assert.DoesNotContain("ElementCompositionPreview.GetElementVisual(badge).Scale", animation, StringComparison.Ordinal);
        Assert.Contains("internal event EventHandler? HoverBadgeChanged;", adaptiveIcon, StringComparison.Ordinal);
        Assert.Contains("HoverBadgeChanged?.Invoke(this, EventArgs.Empty);", adaptiveIcon, StringComparison.Ordinal);
        Assert.Contains("badge.SizeChanged +=", adaptiveIcon, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = false", dock, StringComparison.Ordinal);
        // Icon magnification must not own the window or pill geometry, but
        // the separate compositor wave must still follow the physical pointer.
        var hoverRefresh = dock[dock.IndexOf("private void RefreshHoverVisuals()", StringComparison.Ordinal)..
            dock.IndexOf("private void ResumeAfterHold()", StringComparison.Ordinal)];
        var pointerMove = dock[dock.IndexOf("private void Moved(object sender, PointerRoutedEventArgs e)", StringComparison.Ordinal)..
            dock.IndexOf("private void HideDockWave()", StringComparison.Ordinal)];
        Assert.Contains("ShowDockWave(x);", hoverRefresh, StringComparison.Ordinal);
        Assert.Contains("ShowDockWave(e.GetCurrentPoint(root).Position.X);", pointerMove, StringComparison.Ordinal);
        // Pointer motion updates the wave target; the render tick applies the
        // compositor wave and outline. Those calls are intentionally not made
        // directly in Moved, so animation remains smooth between pointer events.
        var waveTick = dock[dock.IndexOf("private void TickDockWave(", StringComparison.Ordinal)..
            dock.IndexOf("private void UpdateDockWaveOutline()", StringComparison.Ordinal)];
        Assert.Contains("StartDockWaveTimer();", pointerMove, StringComparison.Ordinal);
        Assert.Contains("desktopBackdrop.SetDockWave(", waveTick, StringComparison.Ordinal);
        Assert.Contains("UpdateDockWaveOutline();", waveTick, StringComparison.Ordinal);
        Assert.DoesNotContain("surface.Width =", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("surface.Height =", pointerMove, StringComparison.Ordinal);
        Assert.DoesNotContain("animation.ResizeExpanded(", pointerMove, StringComparison.Ordinal);
    }
}
