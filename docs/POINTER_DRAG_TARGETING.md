# Pointer-based stack and reorder targeting

Historical first iteration. The automatic icon-versus-gap mode selection below
is superseded by `EXPLICIT_STACK_MODE.md`: normal dragging now only reorders;
right-click while left-dragging explicitly toggles Stack Mode.

The former path selected a nearest horizontal slot (including positions outside
the dock), overrode it with a centered dwell target, and selected a nearest slot
again on release. That could reorder on an icon or release outside the dock.

The new Core resolver classifies the pointer as Stack, Reorder or Outside.
Eligible button hitboxes take precedence; gaps and bounded end padding are the
only insertion targets. The source and ineligible icons cannot become stacks or
accidental insertion zones. Releasing over a stack target commits immediately;
there is no dwell requirement in this interaction. Preview itself never saves.

Hitboxes are actual laid-out button bounds transformed into the same icon-panel
DIP space as pointer events. Existing behavior resets magnification at drag
start before measuring. Dock transforms/DPI are handled by XAML coordinates.
Insertion-preview translations are deliberately excluded from target geometry:
moving a neighbor to show a gap must not move the gap's own decision boundary.
Overlapping hitboxes retain the previous stack target while still contained;
retention never extends into an actual gap or permits a gap release to stack.

Neighbors use 130 ms cubic ease-out shifts, retargeting from their current drawn
translation when the mode changes. Windows reduced motion uses direct placement.
The existing Drag Lens handles the source visual; its bounds are never inputs to
target selection. Existing stack/reorder stores and release-time persistence are
reused. Escape/capture loss retain cancellation behavior.

Tests cover icon edges, gaps, ineligible/self targets, outside releases,
coordinate scaling, target switching/cancellation, insertion indices and store
reload after release. Wiring checks connect the tested resolver to pointer-up
handling and reject the old expanded stack-boundary heuristic.

Manual checks still required:
- Drag from each direction across icon -> gap -> icon; confirm smooth restoration
  and insertion, without target flicker or source jumping.
- Release promptly over an eligible icon, a gap, the source, an ineligible icon,
  above/below the row and beyond the end padding. Check exactly one operation.
- Escape/capture loss; repeat rapidly; create/add/extract/unstack; restart and
  verify order and membership.
- Check 100/125/150/200% DPI, both monitors, hover magnification, reduced motion,
  Drag Lens, all appearance modes and Win-key reversal after a drag.

No taskbar/input-helper/watchdog policy changes, commits or pushes.

Validation (2026-10-09): Debug and Release builds passed with zero warnings/errors;
507 tests passed per configuration (328 Core, 179 Windows). `scripts/Validate.ps1`
passed. Debug was launched; interactive acceptance above remains pending.

Files changed for this task:
- `src/GlassDock.Core/Applications/DockPointerTarget.cs`
- `src/GlassDock.Core/Applications/DockStack.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs`
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.Stacks.cs`
- `tests/GlassDock.Core.Tests/DockPointerTargetTests.cs`
- `tests/GlassDock.Windows.Tests/DockStackStoreTests.cs`
- `tests/GlassDock.Windows.Tests/DockStackWiringTests.cs`
- this document
