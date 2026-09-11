# Roadmap
Phase 0 is verified. Phase 1 material laboratory is implemented and visually inspected. Every phase requires a successful build, run, tests, documentation and Git commit.

- PHASE 0 → Project foundation
- PHASE 1 → Glass visual laboratory
- PHASE 2 → Home indicator
- PHASE 3 → Expandable dock
- PHASE 4 → Glass Home launcher
- PHASE 5 → Application discovery & launching
- PHASE 6 → Windows key integration
- PHASE 7 → Running-app/taskbar behavior
- PHASE 8 → System status / Control Center
- PHASE 9 → Multi-monitor + DPI
- PHASE 10 → Shell/taskbar replacement
- PHASE 11 → Crash recovery + watchdog
- PHASE 12 → Settings/customization
- PHASE 13 → Performance/polish
- PHASE 14 → Licensing / Free + Pro
- PHASE 15 → Installer / updater
- PHASE 16 → Beta testing
- PHASE 17 → Commercial release

## Phase 0 acceptance
Seven projects and solution; documented boundaries; successful restore/build/tests and startup smoke check; clean initial commit. No product features.

## Phase 1 laboratory
Create an isolated glass material laboratory. Compare frosted, clear and refractive glass; backdrop blur, translucent tint, opacity, saturation, brightness, borders, shadows, rounded corners, animation and GPU rendering.
Measure visual fidelity and rendering cost to decide whether native WinUI/Windows Composition is sufficient or custom rendering/shaders are required. Document evidence and stop before the home indicator.
Do not add dock behavior or shell integration to the laboratory.

Implemented: three presets, live controls, five stress-test backgrounds, native blur/color effects, independent rounded shadow and explicit refraction limitation.
The user has separately authorized a Phase 2–3 desktop-foundation milestone, including carefully gated recovery/taskbar experiments. It must preserve the laboratory and does not authorize the full launcher or other future functionality.

## Safety gate
Phase 10 cannot hide the taskbar until restoration and emergency recovery are proven. If recovery requires Phase 11 work, keep Phase 10 non-destructive and disabled until that gate is met. Preserve the phase sequence without exposing an unsafe shell state.
