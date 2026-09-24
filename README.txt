GlassDock dock expand/collapse animation restore
================================================

Finding:
The dock expansion code was NOT removed. The current DockAnimationController
still animates Width/Height/Opacity, but its motion is inconsistent with the
new approved utility-popup motion:
- shell keyframes use CubicEase
- vertical placement uses smoothstep (ease-in-out)

This patch makes the dock transition visibly directional and smooth while
keeping the existing architecture and durations.

Changes:
- OPEN / RAISE:
  - SineEase EaseOut
  - smooth immediate movement, gentle settle
- CLOSE / LOWER:
  - SineEase EaseIn
  - smooth departure, natural fold back
- Vertical bottom movement uses the same directional sine behavior instead of
  smoothstep ease-in-out.
- Existing Width/Height/Opacity durations remain unchanged.
- Magnification behavior is unchanged.
- Utility Tray / Quick Settings / Calendar animation is untouched.

Performance:
- no new timer
- no new render loop
- no new animation controller
- no new polling
- no extra caches
- no new per-frame allocations
- existing 16 ms placement timer is reused

Replace:
  src\GlassDock.App\Desktop\DockAnimationController.cs

Then:
  dotnet build
  dotnet test

Runtime check:
1. Collapse dock to the home-indicator/pill.
2. Hover/click to expand it repeatedly.
3. Expansion should be visibly smooth instead of appearing instant/flat.
4. Collapse should smoothly return to the pill.
5. Verify taskbar suppression, bare-Win behavior, Win+Space and normal Win
   shortcuts, dock topmost, max-width pinned layout, utility popups and hover
   wave remain working.
