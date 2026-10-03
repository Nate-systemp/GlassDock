# Per-monitor reorder and external file drop

The existing shared WindowsApplicationService remains authoritative. Each monitor submits only its visible IDs; MergeRequestedOrder replaces those IDs in their global slots, leaving hidden IDs in place. ReorderApplications now applies the latest persisted order before merging, covering a second monitor's reorder before reconciliation publishes the first change. No per-monitor persistence store was added.

Internal reorder uses captured pointer state, not an OLE DataPackage. External handling requires StorageItems and rejects input while a reorder candidate, active reorder or commit owns the gesture. External drag also blocks starting reorder and preview opening. Existing dock expansion and drag target outline are reused; leave/drop restore the visuals. Ordinary documents dropped on empty space are not pinned. Existing executable/shortcut drag-to-pin remains available.

WindowsApplicationLauncher handles file arguments. A Win32 EXE or filesystem .lnk is required. On drop, IShellLink resolves the exact target, existing arguments and working directory; only an executable target is accepted. Every dropped existing file/folder path is independently quoted, duplicate paths are removed, and no file contents are read. ShellExecuteEx uses the selected application, never the dropped document as its executable, and does not request elevation. Normal application handling determines whether an existing window or new instance receives the file.

Generic packaged AppsFolder file activation is not implemented. Those targets are rejected rather than pretending command-line arguments perform file activation. Squirrel launchers retain their original shortcut arguments; whether that launcher forwards trailing file arguments is application-dependent.

## Files changed in this task (pre-existing per-monitor edits preserved)

- C:\Dev\GlassDock\src\GlassDock.App\Desktop\DesktopOverlayWindow.cs
- C:\Dev\GlassDock\src\GlassDock.Windows\Applications\WindowsApplicationService.cs
- C:\Dev\GlassDock\src\GlassDock.Windows\Applications\WindowsApplicationLauncher.cs
- C:\Dev\GlassDock\src\GlassDock.Windows\Applications\ShellApplicationMetadata.cs
- C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\DockOrderTests.cs
- C:\Dev\GlassDock\tests\GlassDock.Windows.Tests\FileDropTests.cs
- C:\Dev\GlassDock\docs\FILE_DROP_AND_MONITOR_ORDER.md

## Validation

Tests exercise single/multiple files, Unicode/spaces, folders, invalid paths, unsupported targets, quoting, pinned/running launch plans, real Shell .lnk metadata, rejection of document shortcuts, and two monitor-local reorder sequences persisted/reloaded through the same store. Automated tests do not prove physical Explorer OLE input, hover visuals, application-specific Squirrel forwarding or a physical second monitor.

Runtime fixtures: C:\Dev\GlassDock\artifacts\file-drop-check. Test report one.txt and 日本語.txt against a compatible Win32 editor, then reorder in All Displays and verify after restart. The user confirmed single-file and multiple-file drops, target highlighting, opening the exact files, and All Displays reorder on each monitor all worked. These are user-observed runtime results; restart persistence remains manually unverified.

Latest validation: Debug build succeeded with 0 warnings/errors; Debug tests 315 passed (183 Core + 132 Windows), 0 failed/skipped. Release Validate.ps1 initially failed two cursor-sensitive Peek input-region cases; both passed isolated (2/2), then the full Validate.ps1 rerun passed 315/315. No test assertions were weakened. git diff --check passed. Debug app and watchdog launched successfully; the UI helper did not enumerate the dock overlay, so physical interaction checks were delegated to the user. The user subsequently confirmed the requested file-drop and two-monitor reorder checks all worked. Full application-restart persistence and application-specific Squirrel forwarding remain manually unverified.
