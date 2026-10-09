# P0 Win-key sleep/wake recovery

The failure had three independent stale-state paths. The low-level hook had no
resume re-registration, a key release missed while the lock screen or display
power transition was active could remain in `WindowsKeyGesture`, and the
elevated bridge only attempted its helper connection at startup. A wake could
therefore leave either hook alive but stale, or leave the app on a fallback
that never revalidated the protected helper.

`WindowsKeyboardService` now registers for `WM_POWERBROADCAST`, the current
session's WTS lock/unlock notifications, and console display power changes.
Boundary notifications clear the input mailbox and gesture state and advance
the input revision. Resume/unlock/display-on notifications are coalesced by
`InputLifecycleRecoveryGate` so one physical transition produces one recovery.
The local hook is re-registered on its owning message thread. The elevated
helper receives a `RECOVER` command containing the current state and re-arms
its existing hook on its own message thread. If an older helper does not
advertise recovery, the bridge closes that connection, waits for its process to
release ownership, and starts one bounded reconnect through the existing
protected scheduled task. No second hook or helper is started concurrently.

## Manual Windows validation

Use the current Debug build with the protected helper installed. With the dock
collapsed, press Win once, let it expand, then press Win again to collapse it.
Repeat after each of these transitions:

1. Lock with Win+L, unlock, and press Win.
2. Sleep from the Windows power menu, wake the machine, and press Win.
3. Repeat sleep/wake at least three times, including a rapid Win press within
   two seconds of wake.
4. Turn the display off and on without sleeping, then press Win.

Also test Win+E, Win+R and Win+Space after every wake. Confirm that Start does
not flash, the dock toggles once per press, and no manual helper restart is
needed. Check the local `%LocalAppData%\Doky\input-helper.log` and Debug output
for the `Resume:` and `lifecycle recovery completed` entries. Keep the
independent watchdog recovery command available and do not modify Explorer.

Automated tests cover lifecycle coalescing, stale gesture reset, mailbox
invalidation, and recovery protocol ordering. They cannot certify hardware
sleep/wake, display firmware, or an installed elevated helper; those checks
remain a manual acceptance step.
