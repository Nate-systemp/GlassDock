# Reversible dock transitions

## Findings and scope

The previous overlay serialized toggles with dockToggleInFlight and a parity bit.
While an animation ran, additional presses changed that bit; only after completion
could one follow-up animation start. This suppressed the requested interactive reversal.

The controller already sampled visible properties, but completed its previous task
synchronously while replacing shared storyboard state. An awaiting toggle loop could
therefore re-enter it inline. Completion callbacks also lacked ownership checks, and
callers could continue into material/capture cleanup after an obsolete transition.
These are code-identified hazards, not a reproduced crash stack: the available
development-error.log contains an older settings-bounds exception, not this crash.

## Implementation

- Each eligible request executes on the UI dispatcher and immediately selects the
  opposite transition target from Expanding/Expanded versus Collapsing/Idle.
  No animation queue, cooldown or parity bit remains.
- One controller samples animated Width, Height, surface/content/indicator opacity,
  indicator width and current bottom margin before stopping its previous clocks.
  The new storyboard starts at those values. Only one shape storyboard and the
  existing placement timer own their respective properties.
- Normal endpoint choreography retains its 380-ms opening and 300-ms closing.
  Retargeting removes the initial content/indicator holds and uses sine ease-out.
  Duration is base duration times remaining geometric progress, bounded below at
  45 ms. This preserves position and gives an immediate response without overshoot;
  it does not claim exact physical velocity conservation between storyboards.
- Task continuations are asynchronous, storyboard completion checks ownership,
  and overlay continuations check revision, shutdown and closing before finalizing.
  Expected interruption resolves false; unexpected toggle errors are logged.
- The state machine permits Idle/Collapsing directly to Expanding. A context-menu
  hold resumes from its partial state instead of declaring an endpoint first.
- Clear capture is active throughout Expanding/Expanded/Collapsing, rather than
  being repeatedly stopped at near-zero opacity/height during rapid reversals.
  Collapse cleanup runs only after a current transition actually reaches Idle.
  Reopening during collapse reuses the current material/capture resources.
- Input transport retains each distinct recent key-release event, rather than
  replacing it with the latest one. Transport delivery never awaits animation.
  The existing 500-ms stale-event expiry and ownership/revision filtering remain.
  WindowsKeyHook and WindowsKeyGesture are unchanged: key repeat is not a new
  deliberate bare-Win gesture, and shortcut forwarding/suppression are preserved.
- Existing Pin Dock, HoverToExpandOnly, popup ownership and shutdown guards remain.

## Files changed in this task

- src/GlassDock.App/Desktop/DesktopOverlayWindow.cs
- src/GlassDock.App/Desktop/DockAnimationController.cs
- src/GlassDock.Core/Desktop/DockStateMachine.cs
- src/GlassDock.Core/Desktop/DockTransitionTiming.cs
- src/GlassDock.Core/Desktop/InputSignalMailbox.cs
- src/GlassDock.Windows/Desktop/WindowsKeyboardService.cs
- src/GlassDock.Windows/Desktop/WindowsInputHelperHost.cs
- tests/GlassDock.Core.Tests/DockReversalTests.cs
- tests/GlassDock.Core.Tests/InputSignalMailboxTests.cs
- docs/REVERSIBLE_DOCK_TRANSITIONS.md

## Validation and limits

Debug build: zero warnings/errors, 428/428 tests passed (272 Core, 156 Windows).
scripts/Validate.ps1: locked restore and Release build succeeded with zero
warnings/errors; all 428 Release tests passed. The final Debug build also includes
the follow-up local exception-log message. Sixteen regression cases were added.

Regression coverage exercises direct reversals, stale completions, odd/even rapid
targets, shutdown, progress/duration math, ordered input delivery and ownership reset.
Visual acceptance needs normal single presses, 20 presses at roughly 250 ms, 50+
rapid presses, stopping mid-burst, all five appearances (especially Clear), pointer
hover, Pin Dock/auto-hide, popups, a second monitor and DPI changes. Also verify
Win+E, Win+R and Win+Space. Automated tests do not establish animation feel or GPU
stability; no claim of stress-test acceptance is made before interactive testing.

The protected Program Files input helper is not replaced by a normal Debug build.
Its transport change requires a separately deployed updated helper for complete
elevated-input burst validation. Its protocol is unchanged. No helper installation,
package, release, settings migration, commit or push is performed in this task.
