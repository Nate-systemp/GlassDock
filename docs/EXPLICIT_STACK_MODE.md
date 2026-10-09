# Explicit Stack Mode

Normal left dragging only reorders. Icon centers choose insertion boundaries;
the valid row/end padding remains bounded. Hovering an app cannot create a stack.
Right-click while left-dragging toggles Stack Mode. In Stack Mode, neighbors
return to their original slots using the existing 130 ms gap animation, pointer
hitboxes select eligible stack targets, and the existing Liquid Glass pill/lens
preview is reused. Release over a target saves a stack; release anywhere else
cancels, without falling back to reorder.

`DockDragIntent` owns the opt-in flag and right-button edge latch. WinUI can
deliver additional mouse-button changes through PointerMoved while capture is
held, so both pressed and moved paths reach that latch. Duplicate events do not
toggle twice. Right release cannot finish the operation. Left release delivered
through PointerMoved does finish it, even when right remains down. Reset/end,
Escape, pointer cancellation, genuine capture loss and deactivation clear intent.
App and stack context handlers suppress menus during the gesture; a subsequent
normal press restores regular context-menu behavior. Right-release capture loss
may reacquire the same pointer while left remains down; other capture loss cancels.

Existing source eligibility, pointer-coordinate transforms, persistence stores,
Drag Lens and preview animations are reused. No external-drop, tray, keyboard,
watchdog or rendering-policy changes.

Manual acceptance (pending):
- Left-drag across every icon: reorder only, never a stack.
- Left-drag + right press/release: one toggle, no menu/drop/capture loss.
- Toggle repeatedly over an icon/gap: smooth gap restoration and pill response.
- Stack-mode left release on target creates a stack; in gap/outside cancels.
- Release left while still holding right, then release right: no delayed menu.
- Escape, capture loss and window deactivation cancel; next drag starts normal.
- Normal right-click afterward still opens app/stack menus.
- Verify extraction, persistence after restart, reduced motion, DPI/multiple
  monitors, all appearances, and normal click-to-minimize/Win-key behavior.

Modified implementation: DockDragIntent.cs, DockPointerTarget.cs,
DesktopOverlayWindow.cs, DesktopOverlayWindow.Stacks.cs, WindowPreviewCoordinator.cs.
Tests: DockDragIntentTests.cs and DockStackWiringTests.cs; existing pointer/store
tests remain to verify stack hitboxes and persistence contracts.

2026-10-09 validation: Debug and Release builds passed with zero warnings/errors;
512 tests passed in each configuration (333 Core, 179 Windows).
`scripts/Validate.ps1` passed. The final Debug build was launched and its process
remains responsive. The interactive checks above are pending user validation.

## Release animation correction

The lifted icon previously followed an empty button's separate FLIP animation
while the lens outro ran its own follower and fixed-duration fade. Returning the
content to the button could expose the follower's remaining position error.
The lifted source now skips FLIP (neighbors retain it), and the lens settles to
the fixed destination with a finite 160 ms cubic ease-out reaching the exact
endpoint. Pointer-move magnification is deferred until the handoff completes.
Tests cover forward/backward/vertical settles and exact endpoint continuity.
Debug/Release builds and 515 tests per configuration passed with zero warnings
or errors; `scripts/Validate.ps1` passed. The corrected Debug app is running;
visual acceptance still needs actual dragging.

The follow-up release fix captures the source button's post-reorder visual slot
after layout and passes that fixed point to the lens outro. The dragged source is
excluded from neighbor FLIP transforms, so it cannot chase a moving empty slot.
The outro reaches that point before content is returned to the button. Pointer
hover/magnification is suppressed during the handoff. Debug/Release builds and
516 tests per configuration passed after this correction; the Debug process was
stopped for replacement and should be relaunched for the manual check.
