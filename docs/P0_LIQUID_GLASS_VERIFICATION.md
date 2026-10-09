# P0 #3 — Liquid Glass verification

## Confirmed source defect and focused correction

The shared renderer compared monitor handles only when deciding whether to
recreate its capture pool. A same-monitor resolution/orientation change retained
the old pool. A subsequent frame-size mismatch entered persistent fallback.

Capture source identity now includes physical monitor dimensions. Window size,
position and DPI-only changes retain the existing capture. A frame arriving ahead
of display notification triggers a source re-query; a real source change releases
the old resources and starts capture with current bounds. A mismatch against
unchanged monitor bounds still logs failure and retains native fallback rather
than retrying indefinitely. A changed source can recover an earlier failed state.

No shader, material, approved layout, animation, keyboard, fullscreen policy or
watchdog design was changed. Capture permissions remain explicit and Windows-owned.

## Evidence and limits

The existing Settings window was visually inspected through the computer-use
helper: text was readable and the backdrop blurred. Its accessibility tree was
unavailable. This does not establish the selected dock mode, exact DPI, popup
appearance, or five-mode acceptance. The helper did not expose the native dock
or utilities. No new ghost-shadow/corner defect was visually reproduced.

Regression tests exercise capture-source identity at scale factors 1, 1.25,
1.5, 1.75 and 2, plus resolution, orientation and monitor changes. These are
behavioral geometry tests, not screenshots or hardware scaling verification.

## Manual acceptance matrix (pending)

At 100/125/150/175/200 percent, inspect Dark, Light, Frosted, Acrylic and Clear:
dock, app menu, previews, stack, Calendar, Hidden Tray, Quick Settings and Home.
Check text/icon clarity, corners, intended soft shadows, popup work-area clamping,
live mode changes on retained windows, refraction/specular/dispersion in Clear,
rapid reversal and popup opening/closing. Settings intentionally uses Frosted
when Clear is selected for readability; its preview is representative.

Change monitor resolution/orientation with Clear visible, then move between
mixed-DPI monitors. Confirm refraction returns without a black/stretched frame.
Check capture denied/unavailable fallback, explicit borderless permission denied
and granted states, lock/resume and device interruption. Do not bypass permission.
Repeat hidden/open cycles and measure CPU/GPU and resource growth; no performance
budget or long-duration leak result is claimed by source inspection.

P0 #3 remains incomplete until this visual/hardware matrix passes. Existing P0
animation and core behavior tests must pass; gameplay and elevated-app acceptance
remain separate manual gates.
