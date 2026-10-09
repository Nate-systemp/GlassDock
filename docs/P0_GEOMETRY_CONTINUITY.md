# Dock geometry continuity — 2026-10-09

The prior icon curve held full opacity through 65% of collapse and retained at
least 82% scale while the surface approached 120 by 5 DIP. The separate fixed-size
indicator had delayed fades; placement used another timer. These produced a
material handoff and icons visibly outlasting the shrinking silhouette.

The controller now animates one built-in RangeBase Value channel and derives
width, height, bottom position, pill radius and material blend from that progress.
The retained indicator shares the changing silhouette, returning exactly to
120 by 5 DIP with radius 2.5. Closing returns to the recorded pill bottom margin.
Icons use the current size ratios to fit the surface and smoothstep opacity
through the entire path, without late-fade delays or a separate motion clock.
Bottom anchoring moves their centers downward as they shrink. Existing reduced
motion, revision ownership, disposal and Liquid Glass rendering remain in use.

An initial custom dependency-property clock compiled but failed the startup
smoke check with a WinUI crash. It was replaced by a built-in Slider Value target
that is not attached to the visual tree. The corrected Debug app remained running
with its existing helper and watchdog. This is not proof of visual acceptance.

Validation: Debug and Release builds, zero warnings/errors; 445 tests passed in
each configuration. Geometry tests cover 0/25/50/75/100 percent, icon containment,
opacity and reversible paths. Existing interrupted-transition/stale-completion
and helper ordering tests remain passing.

Manual acceptance remains required for all five appearances: settled opening,
closing, reversals at each checkpoint, repeated Win presses, minimized pill
placement, hover magnification and Pin Dock interactions. Confirm no flicker,
clipping or black frames. Follow the 500-toggle procedure in
P0_WIN_KEY_STABILITY_VALIDATION.md. No commit or push performed.
