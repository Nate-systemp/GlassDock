# Desktop foundation recovery design
Historical initial design. The subsequent user-authorized active lease, recoverable auto-hide suspension, and bare-key activation are documented in [TASKBAR_AND_WINDOWS_KEY_FIX.md](TASKBAR_AND_WINDOWS_KEY_FIX.md).
Written before enabling taskbar suppression.

Default launch creates the floating indicator but leaves the Windows taskbar unchanged. Explicit development tests may suppress the primary taskbar for at most 60 seconds. This is not production shell replacement.
The watchdog alone owns suppression. App must receive READY before requesting HIDE. The child verifies App process identity and startup time, uses a single-session lease mutex, and restores in finally.
The UI supplies one-second heartbeats. Parent death, input EOF, missing heartbeat for five seconds, the 60-second deadline, RESTORE, or the independent emergency event all end the test.
App also watches watchdog termination and attempts emergency restoration if the child exits unexpectedly. No restart policy exists, so no crash loop is possible.
GlassDock.Watchdog --restore signals the emergency event and shows the current taskbar without requiring App. Ctrl+Alt+F12 invokes the same emergency path while App is responsive.
No Explorer termination, shell replacement, registry writes, appbar auto-hide setting changes, taskbar disabling or configuration persistence is permitted.

ShowWindow(SW_HIDE) is a reversible window operation, not a supported permanent taskbar replacement API. Explorer may recreate or re-show its taskbar. During a bounded test the watchdog checks visibility at 250 ms; handle/process changes abort and restore rather than taking ownership of a new Explorer instance.
Multiple taskbars are detected but suppression is refused. Auto-hide/taskbar geometry policies remain Windows-owned. Some shell surfaces or transient taskbar flashes may still appear.
Simultaneous App/watchdog termination or an OS failure is outside the process-pair guarantee; the separate recovery command remains available, and no persistent shell settings were changed.
Bare Windows key behavior remains native. RegisterHotKey is used only for Ctrl+Alt+Space (placeholder) and Ctrl+Alt+F12 (recovery); no low-level keyboard hook is introduced.
