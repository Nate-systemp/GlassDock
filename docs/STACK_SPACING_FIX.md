# Expanded stack spacing correction

After removing application slots during a merge, `SynchronizeItems` changed
`surface.Width` directly. `DockIconTransition` still held the expanded width
from the last open/close animation. Its geometry-derived scale and opacity
therefore treated the smaller open surface as a partially collapsed dock.
The next open/close transition refreshed the reference dimensions, explaining
why minimizing/reopening cleared the mismatch.

`DockAnimationController.ResizeExpanded` now updates the reference dimensions
and both retained surface/indicator sizes together. Item synchronization and
appearance resizing use that path. No independent icon animation or timer was
introduced, and in-flight reversal logic remains unchanged.

Regression coverage demonstrates the stale-width scale error and verifies full
scale/opacity through repeated shrink/grow endpoints. Debug/Release builds passed
with zero warnings/errors; 488 tests passed in each configuration, including
`scripts/Validate.ps1`. The corrected Debug build was launched on 2026-10-09.
Visual acceptance is pending: create several stacks without minimizing, then
unstack and verify immediate spacing, full-size icons, and normal reversals.
