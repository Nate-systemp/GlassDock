# Reversible icon motion

The previous icon row faded out in 145 ms while the dock continued its 300-ms
collapse. It now has one retained parent CompositeTransform. The actual animated
dock width/height determine normalized progress, which directly drives icon scale,
downward translation and opacity. There is no independent icon progress clock,
storyboard, timer, asynchronous transition or stagger.

Expanded is scale 1, offset 0, opacity 1. Collapsed is scale .82, downward offset
18% of the row height (clamped to 8–14 DIP; 12.24 DIP at the usual 68-DIP height),
and opacity 0. Scale contracts gently with a power curve. Opacity stays 1 through
the first 65% of the motion path and uses smoothstep for the final 35%. Expansion
retraces the same position-dependent values. These fractions describe motion
progress, not wall-clock percentages after easing.

Normal opening/closing retain their 380/300-ms total durations. Width and height
now share their endpoint times (previously opening geometry finished at 320 ms
while icons continued to 380 ms; collapse height finished at 280 versus width at
300 ms). Retargeting and context-menu freezing preserve geometry; icons immediately
derive their presentation from it. There is no independent progress to accumulate
drift over repeated reversals. Existing remaining-distance timing still applies.

The parent transform includes icon content, stack layers, notification badges,
running indicators and the utility cluster. Individual child Composition scales
still provide hover magnification and their existing smooth return to 1. Nothing
resets those scales to 1 at collapse start. XAML applies the transform in DIPs.

An internal drag is canceled through the existing reorder path and its lifted
content returned from Drag Lens before the parent moves. External file dragging
keeps its existing interaction ownership until completion. No icons, brushes,
images or visual trees are rebuilt by the motion callback.

Files changed in this follow-up:
- src/GlassDock.App/Desktop/DockAnimationController.cs
- src/GlassDock.App/Desktop/DesktopOverlayWindow.cs
- src/GlassDock.App/Desktop/DockIconTransition.cs
- src/GlassDock.Core/Desktop/DockIconMotion.cs
- tests/GlassDock.Core.Tests/DockIconMotionTests.cs
- docs/DOCK_ICON_MOTION.md

Ten pure regression cases cover endpoints, delayed fading, monotonic movement,
identical reverse sampling, rapid geometry reversals and travel bounds. Runtime
acceptance still requires normal/reversed/rapid Win transitions, magnified icons,
badges, stack layers, Clear and the other appearances, Pin Dock, auto-hide and DPI
checks. Compilation and pure tests cannot establish the visual result.

The opening-failure follow-up removed a code-only custom Progress animation
target. The subsequent synchronization fix also removes its replacement native
scalar clock entirely: only existing dock geometry properties are animated.

Validation: Debug build succeeded with zero warnings/errors; 438/438 Debug tests
passed (282 Core, 156 Windows). Validate.ps1 also passed the Release build with
zero warnings/errors and all 438 tests. The newest Debug build was launched for
interactive acceptance. No commit, push, packaging or release was performed.
