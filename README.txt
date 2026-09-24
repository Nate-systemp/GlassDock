GlassDock collapse content/shell synchronization patch
=======================================================

Problem fixed:
When collapsing the expanded dock, the glass body visibly started shrinking
before the application icons disappeared. The icons looked delayed / left
behind inside an already-collapsing shell.

Cause:
The previous collapse used EaseIn for icon opacity and held full opacity for
the first 60 ms:
    60 ms -> still fully visible
    220 ms -> finally reaches opacity 0
At the same time the glass width/height had already begun collapsing.

New choreography:
- Icons start fading IMMEDIATELY at t=0.
- Icon fade uses Sine EaseOut so the visual response is immediate but smooth.
- Icons reach opacity 0 at ~145 ms.
- Glass width/height hold for only 32 ms, then begin their existing smooth
  EaseIn collapse.
- Surface fade and home-indicator timing remain unchanged.

Perceived sequence:
    click collapse
      -> icons/content immediately begin receding
      -> glass shell follows a fraction later
      -> shell finishes collapsing into the indicator

This is intentional overlap, not a hard "icons vanish first" cut.

Performance:
- same existing Storyboard
- no new timer
- no new render loop
- no polling
- no additional animation controller
- no background work
- no new recurring allocation path

Replace:
  src\GlassDock.App\Desktop\DockAnimationController.cs

Then:
  dotnet build
  dotnet test

Runtime validation:
- Expand/collapse dock at least 20 times.
- Icons should never appear to lag behind the shrinking glass.
- Opening animation must remain unchanged.
- Utility Tray / Quick Settings / Calendar animation must remain unchanged.
- Verify taskbar suppression, bare Win / Win+Space / normal Win shortcuts,
  dock topmost, max-width pinned layout, drag/drop, and hover wave.
- Check repeated collapse/expand does not increase RAM continuously.
