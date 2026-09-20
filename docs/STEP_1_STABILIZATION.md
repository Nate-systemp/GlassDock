# Step 1 stabilization — 2026-09-20

This supersedes the two-test-failure status in the earlier audit and historical validation notes. No new product feature or taskbar behavior was added.

- WindowsKeyGesture now latches chord disqualification when the other Windows key is held. Previously both Win keys were excluded from the other-key check and each key-down overwrote the chord flag, allowing the final release to appear bare. Existing modifier and shortcut tests are preserved. Added coverage exercises both press/release orders, re-press while the other Win remains held, recovery to a true bare gesture, and Space as a non-bare chord.
- DesktopWindowHighlight remains intentionally disabled with no production callers. Its obsolete private-overlay reflection test was replaced one-for-one with a native no-op contract test: no new thread window, unchanged target bounds/z-order/foreground, minimized target remains iconic, and invalid-handle/disposed calls are harmless. DesktopWindowFocus production code is unchanged.
- Preview close controls now use top-right card bounds with a 9-DIP right and 8-DIP top inset. They remain separate sibling buttons, preserve their existing close-only handler, and remain visible when the pointer is over the close control. A layout regression covers narrow/wide and compact/expanded cards.
- OnInteractionHoldChanged already contained only one guarded refresh/enqueue block on reinspection. No edit was necessary. The earlier audit's duplicate-block claim was incorrect. Existing ContextMenuOpen, HoldsDock, keyboard suppression and pointer-leave guards remain in place.
- Removed only the two malformed here-string lines from .gitignore.
- Corrected current shortcut/taskbar descriptions in README, architecture and roadmap. These are documentation corrections; suppression and hotkey implementations were not changed.

## Validation

The requested locked restore, full Release solution build and both explicit test commands passed:

| Check | Result |
|---|---|
| Locked restore | PASS |
| Release build | PASS, 0 warnings, 0 errors |
| Core tests | 88 passed, 0 failed, 0 skipped |
| Windows tests | 33 passed, 0 failed, 0 skipped |
| Total | 121/121 passed |

The original count was 118. The stale native test was replaced without changing the count. Two new facts (dual-Win permutations and close-control layout), plus one additional Space theory row, add three cases.

Physical keyboard pass-through, Win+Space Home delivery, registered Ctrl+Alt hotkeys, menu holds during actual mouse/keyboard use, and close-button clicking/appearance still need an interactive pass in the rebuilt WinUI app. Unit/native tests and source review establish their scoped contracts, not end-to-end visual behavior. No new app session or taskbar recovery experiment was started for this step.
