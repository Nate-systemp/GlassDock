namespace GlassDock.Core.Applications;

public readonly record struct PreviewRect(double X, double Y, double Width, double Height);
public sealed record WindowPreviewLayout(double Width, double Height, int Capacity, IReadOnlyList<PreviewRect> Cards)
{
    /// <summary>Convert dock-relative DIPs once, then clamp physical bounds.
    /// A pinned dock's reserved work area naturally ends at the dock's top.</summary>
    public static PreviewRect Position(PreviewRect workArea, PreviewRect dock,
        double dpi, double anchorX, double dockTop, double width, double height)
    {
        dpi = double.IsFinite(dpi) && dpi > 0 ? dpi : 1;
        var pixelWidth = Math.Min(workArea.Width, Math.Max(1, Math.Ceiling(width * dpi)));
        var pixelHeight = Math.Min(workArea.Height, Math.Max(1, Math.Ceiling(height * dpi)));
        var x = Math.Clamp(dock.X + anchorX * dpi - pixelWidth / 2,
            workArea.X, workArea.X + workArea.Width - pixelWidth);
        var y = Math.Clamp(dock.Y + dockTop * dpi - pixelHeight,
            workArea.Y, workArea.Y + workArea.Height - pixelHeight);
        return new(Math.Floor(x), Math.Floor(y), pixelWidth, pixelHeight);
    }

    public static PreviewRect CloseButtonBounds(PreviewRect card, double size) =>
        new(card.X + card.Width - size - 9, card.Y + 8, size, size);

    public static WindowPreviewLayout Create(int count, double availableWidth, double availableHeight, bool expanded,
        bool reservePager = false)
    {
        var cardWidth = Math.Max(40, Math.Min(count == 1 ? 232 : 176, availableWidth - 50));
        var cardHeight = Math.Max(40, Math.Min(count == 1 ? 155 : 137, availableHeight - 64));
        var columns = Math.Max(1, (int)((availableWidth - 16 + 12) / (cardWidth + 12)));
        // Use a second row before paginating, while keeping every card inside
        // the current monitor's work area (values here are already DIPs).
        var rows = count > columns && availableHeight >= 64 + cardHeight * 2 + 12 ? 2 : 1;
        var capacity = columns * rows;
        var visible = Math.Min(count, capacity);
        var usedColumns = Math.Min(visible, columns);
        var usedRows = Math.Max(1, (int)Math.Ceiling(visible / (double)columns));
        var stackDepth = Math.Min(2, Math.Max(0, visible - 1));
        var top = expanded ? 8 : 8 + stackDepth * 8;
        var width = expanded ? 16 + usedColumns * cardWidth + Math.Max(0, usedColumns - 1) * 12
            : 16 + cardWidth + stackDepth * 16;
        var cards = Enumerable.Range(0, visible).Select(index => expanded
            ? new PreviewRect(8 + index % columns * (cardWidth + 12),
                top + index / columns * (cardHeight + 12), cardWidth, cardHeight)
            : new PreviewRect(8 + Math.Min(index, 2) * 16, top - Math.Min(index, 2) * 8,
                cardWidth * (1 - Math.Min(index, 2) * .06), cardHeight * (1 - Math.Min(index, 2) * .06))).ToArray();
        // Eight DIP body padding plus the existing 12 DIP transparent travel gap.
        // Only paginated groups need the additional button row.
        var footer = count > capacity || reservePager ? 30 : 0;
        return new(width, (expanded ? usedRows * cardHeight + (usedRows - 1) * 12 : cardHeight)
            + top + 20 + footer, capacity, cards);
    }

    public static int NextPage(int page, int count, int capacity) =>
        page + Math.Max(1, capacity) >= count ? 0 : page + Math.Max(1, capacity);
}
