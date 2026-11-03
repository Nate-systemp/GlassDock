using GlassDock.Core.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace GlassDock.App.Controls;

/// <summary>Scroll extent counts whole entries; visible children measure and flow at their actual edge heights.</summary>
internal sealed class WifiNetworkListPanel : Panel
{
    private double offset;
    private bool scrolling;
    private int visibleSlots = WifiScrollGeometry.VisibleSlots;
    private double viewportHeight = double.PositiveInfinity;
    private FrameworkElement? transitionRow;
    private double transitionFromHeight, transitionToHeight;
    public int LayoutRevision { get; private set; }

    public void SetViewport(double height)
    {
        if (viewportHeight == height) return;
        viewportHeight = Math.Max(0, height); LayoutRevision++; InvalidateMeasure();
    }

    public void SetTransitionRow(FrameworkElement? row, double fromHeight = 0, double toHeight = 0)
    {
        transitionRow = row; transitionFromHeight = fromHeight; transitionToHeight = toHeight;
        LayoutRevision++; InvalidateMeasure();
    }

    private double Progress(FrameworkElement child, int index, double y)
    {
        if (!scrolling) return 1;
        var progress = WifiScrollGeometry.Progress(index, offset, visibleSlots);
        if (child == transitionRow)
        {
            var amount = transitionToHeight == transitionFromHeight ? 1
                : Math.Clamp((viewportHeight - transitionFromHeight) / (transitionToHeight - transitionFromHeight), 0, 1);
            progress = 1 + (progress - 1) * amount;
        }
        var visualY = y + (child.RenderTransform is Microsoft.UI.Xaml.Media.TranslateTransform transform ? transform.Y : 0);
        return Math.Min(progress, Math.Clamp((viewportHeight - visualY) / WifiScrollGeometry.Height, 0, 1));
    }

    public void SetScroll(double value, bool enabled, int slots)
    {
        if (offset == value && scrolling == enabled && visibleSlots == slots) return;
        offset = value; scrolling = enabled; visibleSlots = slots;
        LayoutRevision++; InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = 0;
        double total = 0;
        foreach (FrameworkElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var progress = Progress(child, count, total);
            var height = WifiScrollGeometry.Height * progress;
            if (child.Height != height) { child.Height = height; LayoutRevision++; }
            child.Measure(new(availableSize.Width, height));
            total += height + WifiScrollGeometry.Gap * progress; count++;
        }
        // Logical distance stays stable while the outgoing/incoming heights exchange one full slot.
        return new(availableSize.Width, scrolling ? count * WifiScrollGeometry.Pitch : Math.Max(0, total - WifiScrollGeometry.Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        LayoutRevision++;
        var count = 0;
        var y = scrolling ? offset : 0;
        foreach (FrameworkElement child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) { child.Arrange(new(0, 0, 0, 0)); continue; }
            var progress = Progress(child, count, y - (scrolling ? offset : 0));
            var height = WifiScrollGeometry.Height * progress;
            var width = double.IsNaN(child.Width) ? finalSize.Width : Math.Min(finalSize.Width, child.Width);
            child.Arrange(new((finalSize.Width - width) / 2, y, width, height));
            y += height + WifiScrollGeometry.Gap * progress; count++;
        }
        return finalSize;
    }
}
