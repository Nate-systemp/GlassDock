# Doky grid stacks

Stacks extend the existing shared application service, pin store, per-monitor snapshot filtering,
badge coordinator, and utility glass renderer. They do not add another runtime, launcher, notification
provider, keyboard hook, or user profile. Normal dock geometry and appearance are unchanged.

## Interaction

- Drag a pinned app into the central 60% of another pinned app's slot and hold for 600 ms.
  Release after the target highlights to create a stack or add to one. Merely crossing a slot
  remains reorder. Leaving the target or cancelling capture clears the merge candidate.
  Existing target stacks remain stationary across their entire slot while a pinnable app approaches;
  the narrower central region still requires the intentional hold before merging.
- Stacks hold at most nine apps and occupy one normal dock slot. Their preview shows the first
  four apps; the whole tile uses the existing magnification transform. Closed stacks have no
  window preview. An activity dot indicates notifications without inventing aggregate counts.
- Click to open a bounded grid; click again, outside, or press Escape to dismiss. The dock remains
  expanded while the popup is open. Grid apps use the existing launch/focus path and badge state.
- Right-click a stack for Open, Rename, and Ungroup. Drag members to reorder inside the grid or
  back onto the dock to extract. One remaining member dissolves the stack automatically.
- A single retained popup per dock surface uses the owning monitor, DPI, and work area.
  Live appearance settings flow through UtilityPopupStyle and DesktopGlassBackdrop.
  Opening lasts 160 ms, closing 110 ms, with smoothstep easing, 0.96–1 scale and a 4-DIP settle.

## Data and drag isolation

The original `%LOCALAPPDATA%\GlassDock\dock-pins.json` remains authoritative. Its optional
`Stacks` array stores a stable stack ID, name, and ordered canonical application IDs. Existing
`Pins`, `Excluded`, and `Order` retain their meanings. No stack migration is written just by loading
an old profile. Creation snapshots exact existing launch targets into the same store, preserving
custom shortcuts and arguments. Writes use the existing atomic save and occur only on successful
operations, not pointer movement. Ungroup restores members in their stored order at the stack slot.

The flat application snapshot remains the source for launching and window activation. A stack
projection creates root dock items. The shared service publishes edits to every surface, while the
existing monitor filter limits each child's running windows to that monitor.

`DockStackDrag` distinguishes None, Reorder, StackCandidate, StackMerge, and ExternalFiles.
Its reusable dwell timer runs only during an internal drag. Grid drags carry a separate
`Doky.StackApplication` payload. External StorageItems continue through the existing pin/open-with
handlers; they never enter stack creation. Internal member payloads are consumed before that path.

## Validation — 2026-10-04

- Debug solution build: succeeded, zero warnings/errors.
- Debug tests: **333 total, 333 passed, 0 failed, 0 skipped** (192 Core, 141 Windows).
- Follow-up `scripts/Validate.ps1`: locked restore and Release build passed; **333 total,
  333 passed, 0 failed, 0 skipped** (192 Core, 141 Windows), including the visible-cue regression check.
- Debug rebuild of the cue/payload corrections passed with zero warnings/errors and was launched.
- Existing order, external file-drop, notification, and monitor tests were not weakened.
- Debug app, input helper, and watchdog launched. Live interaction checks are pending user feedback:
  the Windows UI helper does not expose the dock as a targetable window.
- User confirmed stack creation and grid opening, but reported an invisible merge cue and failed
  drag-out. Follow-up corrects cue occlusion and registers the member payload as actual transferable
  data with asynchronous, deferred drop reads. These corrections require a new runtime check.
- User subsequently confirmed the cue but still could not drag members out. No DragStarting trace
  was produced. The grid now observes handled button pointer events and explicitly calls
  StartDragAsync after a 6-DIP gesture, guards against duplicate operations, and suppresses release
  clicks. This follow-up and stationary target behavior are awaiting runtime verification.
- The explicit-gesture/stationary-target build passed Debug and Release validation (333/333 each)
  and was launched successfully for that retest.
- Still pending: add/launch, member/root reorder, extract/dissolve/ungroup, restart persistence,
  outside/Escape dismissal, external file-drop isolation, all-display synchronization, and five themes.
- No commit, release, version change, or profile reset.

## Changed files

```text
C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockStack.cs
C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplication.cs
C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplicationItem.cs
C:\Dev\GlassDock\src\GlassDock.Core\Applications\DockApplicationMonitorFilter.cs
C:\Dev\GlassDock\src\GlassDock.Windows\Applications\DockPinStore.cs
C:\Dev\GlassDock\src\GlassDock.Windows\Applications\WindowsApplicationService.cs
C:\Dev\GlassDock\src\GlassDock.App\Controls\AdaptiveAppIcon.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.Stacks.cs
C:\Dev\GlassDock\src\GlassDock.App\Desktop\DockStackWindow.cs
C:\Dev\GlassDock\tests\GlassDock.Core.Tests\DockStackDragTests.cs
C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\DockStackStoreTests.cs
C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\DockStackWiringTests.cs
C:\Dev\GlassDock\docs\DOCK_STACKS.md
```
