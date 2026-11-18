using GlassDock.App.Controls;
using GlassDock.Core.Desktop;
using GlassDock.Core.Settings;
using GlassDock.Windows.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GlassDock.App.Desktop;

public sealed partial class DesktopOverlayWindow
{
    private MusicControlPill? musicPill;
    private double MusicScale => Math.Min(DockScale,
        ((root.ActualWidth > 0 ? root.ActualWidth : windowManager.CurrentHostWidthDips) - 32) / MusicControlPill.MaximumWidth);
    private double MusicSideReservation => musicPill is null ? 0 : 2 * (16 + 12 * DockScale + MusicControlPill.MaximumWidth * MusicScale);

    internal void SetMusicControls(MusicSessionService? service)
    {
        if (musicPill is { } previous)
        {
            musicPill = null;
            utilityArea.Children.Remove(previous);
            previous.Dispose();
        }
        else if (service is null) return;

        if (service is not null && !closing)
        {
            var pill = new MusicControlPill(service);
            musicPill = pill;
            ConfigureMusicPill(settingsSession.Current.DockAppearanceMode);
            pill.SizeChanged += (_, args) =>
            {
                if (!closing && musicPill == pill && args.NewSize.Width != args.PreviousSize.Width)
                    UpdateBackdropBounds();
            };
            utilityArea.Children.Add(pill);
        }

        if (closing) return;
        UpdateUtilitySurfaces();
        UpdateApplicationSizing();
        var width = CalculateTargetDockWidth();
        var height = ExpandedDockHeight + (pinnedPlacementActive ? PinnedDockClipDepth : 0);
        if (state.State == DockState.Expanded) animation.ResizeExpanded(width, height);
        else if (state.State == DockState.Expanding) animation.RetargetExpandedSize(width, height);
        UpdateBackdropBounds();
    }

    private void ConfigureMusicPill(DockAppearanceMode appearance) =>
        musicPill?.Configure(MusicScale, settingsSession.Current.UtilityTheme, appearance, Appearance.IconSize);

    private bool IsPointerOverMusicPill(double x, double y)
    {
        if (musicPill is not { IsHitTestVisible: true } pill || utilityArea.Visibility != Visibility.Visible) return false;
        var bounds = pill.Glass.Bounds;
        return x >= bounds.X && x < bounds.X + bounds.Z && y >= bounds.Y && y < bounds.Y + bounds.W;
    }

    private void PositionMusicPill(bool visible, HomeCardRect dock, double gap)
    {
        if (musicPill is not { } pill) return;
        var glass = pill.Glass;
        pill.IsHitTestVisible = visible && state.State == DockState.Expanded;
        if (!visible)
        {
            glass.Active = false;
            glass.Hide();
            return;
        }

        var width = pill.ActualWidth > 0 ? pill.ActualWidth : pill.Width;
        var x = dock.X - gap - width;
        var y = dock.Y;
        if (x < 16)
        {
            x = 16;
            y = Math.Max(0, dock.Y - 2 * (gap + dock.Height));
        }

        pill.SetUtilityHeight(dock.Height);
        Canvas.SetLeft(pill, x);
        Canvas.SetTop(pill, y);
        glass.Bounds = new((float)x, (float)y, (float)width, (float)dock.Height);
        var mode = settingsSession.Current.DockAppearanceMode;
        glass.Configure(mode, liquidGlass.BackdropMaterial, liquidGlass.Material, mode == DockAppearanceMode.Clear
            ? settingsSession.Current.ClearCircularHighlightsEnabled : settingsSession.Current.CircularHighlightsEnabled,
            (float)DockScale);
        glass.Active = pill.IsLoaded && mode.GlassStyle() is not null && surface.Opacity > .01 &&
            !glass.UsesNativeDockBackdrop && (Application.Current as App)?.BasicRendering != true;
        if (!glass.Active) glass.Hide();
    }
}
