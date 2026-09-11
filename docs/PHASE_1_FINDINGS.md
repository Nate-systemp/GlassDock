# Phase 1 findings

The laboratory demonstrates live in-app diffusion and distinct Frosted, Clear and experimental Refractive materials.
Frosted uses 28 px blur and a 0.88 processed mix. Clear uses 5 px and 0.65, retaining recognizable background detail.
Refractive uses 10 px and 0.74 with brighter edges and stronger depth cues. It does not distort or refract source pixels.

## Visual evaluation
Inspected the running native application on colorful, dark, light, high-contrast and fine-line/text scenes.
The final effect chain diffuses detail, preserves rounded clipping and updates when scenes/presets change.
No black artifacts were observed in the native path. The isolated-blur diagnostic also exercised the explicitly labelled solid fallback.
A compositing default initially let sharp content dominate; explicit zero multiply/offset coefficients corrected the blend.
The initially unbounded preview was corrected to fit the window; properties scroll independently.
Fixed contrast for scene captions with dark caption backplates.
Static captures and interaction checks cannot certify absence of frame tearing/flicker on every device. No hardware-wide performance claim is made.

## Performance
The scene is vector XAML; all material effects are Composition-backed. There is no screenshot processing, desktop capture, animation loop, timer or background polling in the lab.
One approximately 8.54-second Debug sample observed 5.85% of one CPU core, 153.9 MiB working set and 102.9 MiB private memory. This is a development observation, not a release benchmark.
Per-process GPU-engine counters read zero during that sample; compositor work may be attributed elsewhere, so this does not establish zero GPU cost.
The machine reported NVIDIA GTX 1650 and AMD Radeon graphics. More controlled profiling is needed before production budgets.

## Limitations and decision
Native composition is sufficient to continue evaluating frosted/clear glass and lighting-based depth.
True optical refraction was not achieved. DisplacementMapEffect is unavailable in a Composition effect graph; custom shader work is deferred.
The preview samples the application scene, not the desktop. White preview text is intentionally not a production contrast solution for arbitrary backgrounds.
No persistent settings, shell changes, hooks, networking, telemetry, licensing or updater were added in Phase 1.

## Sources
- [Composition effects](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects)
- [Composition brushes](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-brushes)
- [Win2D displacement limitation](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Effects_DisplacementMapEffect.htm)
