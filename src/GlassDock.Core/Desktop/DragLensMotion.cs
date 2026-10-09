using System.Numerics;

namespace GlassDock.Core.Desktop;

/// <summary>Bounded visual following only; never chooses an insertion slot or stack target.</summary>
public sealed class DragLensMotion
{
    public Vector2 Position { get; private set; }
    public Vector4 Bounds { get; private set; }
    public float Opacity { get; private set; }
    public bool Finished => closing && elapsed >= .16;
    private bool closing;
    private double elapsed;
    private Vector2 releaseStart;
    private Vector2? releaseDestination;

    /// <summary>Keep the same point inside the icon under the cursor, in root DIPs.</summary>
    public static Vector2 GrabOffset(Vector2 buttonSize, Vector2 pressInButton) =>
        buttonSize / 2 - pressInButton;

    public static Vector2 PointerTarget(Vector2 pointerInRoot, Vector2 grabOffset) =>
        pointerInRoot + grabOffset;

    public void Begin(Vector2 position, float size)
    {
        closing = false; elapsed = 0; Opacity = 0; releaseDestination = null;
        Position = position; Bounds = new(position.X - size / 2, position.Y - size / 2, size, size);
    }

    public void End(Vector2? destination = null)
    {
        if (closing) return;
        closing = true; elapsed = 0; releaseStart = Position;
        releaseDestination = destination;
    }

    public void Step(double seconds, Vector2 target, float size, Vector2? mergeTarget,
        bool animate = true, bool precisePointer = false)
    {
        seconds = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, .05) : 0;
        elapsed += seconds;
        var follow = animate ? (float)(1 - Math.Exp(-seconds / .022)) : 1;
        // Closing is a finite settle, not a second follower chasing a FLIP
        // animation. Reach the exact slot before returning content to its button.
        var settle = animate ? (float)Math.Clamp(elapsed / .16, 0, 1) : 1;
        var eased = 1 - MathF.Pow(1 - settle, 3);
        // Never smooth the icon itself while the mouse owns it: that creates
        // a distance-dependent grab offset on fast/long drags. The glass lens
        // bounds can still ease independently.
        Position = closing
            ? Vector2.Lerp(releaseStart, releaseDestination ?? target, eased)
            : precisePointer ? target : Vector2.Lerp(Position, target, follow);
        var left = Position.X - size / 2;
        var right = Position.X + size / 2;
        if (!closing && mergeTarget is { } merge)
        {
            left = Math.Min(left, merge.X - size * .48f);
            right = Math.Max(right, merge.X + size * .48f);
        }
        var end = closing ? (float)Math.Clamp(elapsed / .16, 0, 1) : 0;
        var height = size * (1 - .45f * end);
        var goal = new Vector4(left, Position.Y - height / 2, right - left, height);
        Bounds = Vector4.Lerp(Bounds, goal, animate ? (float)(1 - Math.Exp(-seconds / .045)) : 1);
        Opacity = closing ? 1 - end * end * (3 - 2 * end) : (float)Math.Min(1, elapsed / .12);
        if (!animate) { Opacity = closing ? 0 : 1; if (closing) elapsed = .16; }
    }
}
