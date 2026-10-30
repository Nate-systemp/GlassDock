# Drag lens and stack preview restoration

The retained drag lens failed when the shared material graph gained a ContrastEffect between the composite and exposure stages. Its old direct ExposureEffect cast was invalid. DokyLiquidGlassSurface now traverses and disposes that contrast stage and applies the current native contrast and blur quality.

Dragging continues to use the existing capture source, LiquidGlass shader, retained GPU artwork scene, insertion targets, and release/cancel state machine. The lens translates with the pointer-owned icon before easing its shape, so rapid movement cannot leave the glass behind. Stack merge widens the lens without shifting the icon away from its original grab point. No new capture session or rendering pipeline was added.

AdaptiveAppIcon displays up to four real stack members in a 2×2 grid with equal rounded tiles and an 8% center gap. Smaller stacks leave unused cells empty. Native icon artwork, aspect-preserving fitting, DPI/hover pixel budgets, and original-source resampling remain shared with normal dock icons.

Validation:

- Debug solution build: 0 warnings, 0 errors.
- Debug tests: 402 Core and 211 Windows passed.
- scripts/Validate.ps1: Release build clean; 402 Core and 211 Windows tests passed.
- Rendering probe: 40 GPU checks passed, including capsule clipping, refraction pixel changes, transparent dismissal, and the native contrast graph traversal.
- Safe-mode UI started and reported active GPU desktop refraction. Capture exclusion prevented observing the dock through automated screenshots; end-to-end drag, external file drops, and mixed-DPI appearance were not visually verified. Safe mode also disables hover wave, so that interaction was covered by existing automated checks rather than this UI smoke test.

No user presets, pin order, or stack membership were changed by validation.
