# Win-key input ownership

The previous helper kept the app's low-level hook installed and merely bypassed
its handler. Windows still delivered that callback on the WinUI thread. The
helper duplicated the classification/replay code, synchronously wrote/flushed
its pipe from the hook, advertised HELLO before installing the hook, and polled
connection/message-loop state. Startup could also run elevated task registration.

There is now one SetWindowsHookEx call site: WindowsKeyHook. Either the app fallback
or the helper owns it. HELLO2 authenticates the actual pipe client PID/path; the
app removes its hook before granting GO. The helper then installs the shared hook
and responds READY. After a disconnect, the app waits for the helper process to
exit before restoring fallback. No simultaneous fallback hook remains installed.
The helper has a per-session single-instance mutex and an event-driven native
message pump. Async pipe workers use a bounded latest-event channel. Sequence
numbers reject duplicates, and 500-ms age checks discard stale events after stalls;
this is an expiry limit, not a debounce or response delay. UI dispatch has one
outstanding message and retains only the latest intent.

Bare Win is recognized on release so Win+E/R/D/L/I/Tab/arrows remain chords. Key
state comes from the recognizer during the callback (GetAsyncKeyState is used
only once when installing a hook). Bare Win toggles the existing dock transition;
Win+Space toggles the existing Home window; Ctrl+Alt+Space and Ctrl+Alt+F12 retain
their registered actions. No UI or IO runs in the shared hook callback.

Normal startup only runs an existing `Doky Input Helper` scheduled task. If absent,
normal-privilege fallback remains available; elevated-app input requires setup.
Run `scripts/Register-InputHelper.ps1 -HelperPath <installed-helper-path>` once
from an elevated PowerShell under the same user. Registration is on-demand,
interactive, highest privilege, IgnoreNew, and permits battery operation. This
script is not invoked automatically during app startup. Existing Velopack stop /
uninstall hooks and app build/publish helper references are preserved. The task
path must match the app-adjacent helper; mismatched old installations are rejected.

Microsoft documents that low-level callbacks are delivered to the installing
thread and run before asynchronous key state is updated:
https://learn.microsoft.com/windows/win32/winmsg/lowlevelkeyboardproc

Validation includes key classification/repeats, bounded UI dispatch, duplicate /
stale protocol events, single-instance mutex and shared-hook ownership wiring.
Physical Windows-key shortcuts require a manual keyboard test; the desktop UI
helper does not support sending them. Do not infer elevation/Start-flash success
from build or unit tests alone.

Final validation: clean, restore and Debug/Release builds passed with no compiler
warnings/errors; 141 Core + 80 Windows tests passed (221 total). Validate.ps1 and
git diff --check passed. The solution now includes InputHelper explicitly, fixing
the previous Release-build mapping to its Debug output.

The user confirmed rapid bare-Win dock toggling, elevated Task Manager focus,
Win+E/R and Doky Win+Space work without the reported lag/Start flash. Normal exit
stopped app/helper/watchdog and restored the taskbar in the preceding run. The
final Debug run has exactly one helper; two bounded observations showed about
31 MB working set and 0.31 seconds accumulated helper CPU. This is not a long-term
soak or a measured physical-key-to-display latency benchmark. The current run is
left open for use. Every Windows shortcut and battery-mode task registration have
not been individually verified interactively.
