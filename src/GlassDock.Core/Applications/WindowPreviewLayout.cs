namespace GlassDock.Core.Applications;

public readonly record struct PreviewRect(double X, double Y, double Width, double Height);
public sealed record WindowPreviewLayout(double Width, double Height, int Capacity, IReadOnlyList<PreviewRect> Cards)
{
    public static WindowPreviewLayout Create(int count, double availableWidth, double availableHeight, bool expanded)
    {
        var cardWidth = Math.Max(40, Math.Min(208, availableWidth - 50));
        var cardHeight = Math.Max(40, Math.Min(130, availableHeight - 46));
        var capacity = Math.Clamp((int)((availableWidth - 24) / (cardWidth + 12)), 1, 4);
        var visible = Math.Min(count, capacity);
        var width = expanded ? 24 + visible * cardWidth + Math.Max(0, visible - 1) * 12
            : 24 + cardWidth + Math.Min(2, Math.Max(0, count - 1)) * 16;
        var cards = Enumerable.Range(0, visible).Select(index => expanded
            ? new PreviewRect(12 + index * (cardWidth + 12), 26, cardWidth, cardHeight)
            : new PreviewRect(12 + Math.Min(index, 2) * 16, 26 - Math.Min(index, 2) * 8,
                cardWidth * (1 - Math.Min(index, 2) * .06), cardHeight * (1 - Math.Min(index, 2) * .06))).ToArray();
        return new(width, cardHeight + 64, capacity, cards);
    }
}
