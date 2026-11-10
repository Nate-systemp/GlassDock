# Experimental Windows 11 Do Not Disturb

Implemented 2026-11-09. Compilation is verified; no Windows toggle has been exercised by the agent.

## Enable and scope

Set `DOKY_ENABLE_EXPERIMENTAL_DND=1` in Doky's process environment before startup, then add **Do Not Disturb (experimental)** through Utilities. Without opt-in, the service makes no QuietHours COM or notification-mode API calls and reports unavailable. The existing persisted `UtilityKind.Focus` ID and filled moon asset are reused; no enum IDs move. ON uses `#AF52DE` on the existing white active background; OFF retains the existing inactive icon and Liquid Glass treatment.

This controls Do Not Disturb, not Focus sessions. It never starts/stops a focus session or launches Settings/Quick Settings. No direct registry or WNF writes are used, and automatic-rule schedules, priority lists and per-application notification preferences are not edited.

## Native path and verification

`DndNative` follows the interface prefix and profile strings used by [YASB's DND service](https://github.com/amnweb/yasb/blob/37928dfeacc579948c4b4bf12e169bcbf6006ff2/src/core/widgets/services/dnd/dnd_api.py) and [QuietHours bindings](https://github.com/amnweb/yasb/blob/37928dfeacc579948c4b4bf12e169bcbf6006ff2/src/core/utils/win32/bindings/shell32.py):

- CLSID `F53321FA-34F8-4B7F-B9A3-361877CB94CF`, IID `6BFF4732-81EC-4FFB-AE67-B6C1BC29631F`, `CLSCTX_LOCAL_SERVER`.
- A dedicated background STA initializes COM, reads and validates the existing manual profile, invokes `put_UserSelectedProfile`, releases the COM object on that same STA and balances COM initialization. The getter's `LPWSTR` allocation is explicitly freed with `Marshal.FreeCoTaskMem`; it is not treated as BSTR/HSTRING.
- ON selects `Microsoft.QuietHoursProfile.PriorityOnly`; OFF selects `Microsoft.QuietHoursProfile.Unrestricted`. The legacy `AlarmsOnly` profile is recognized when reading, but is never requested as the ON target. Null/unrecognized manual profiles reject the operation before a setter call.

Actual state comes exclusively from `ToastNotificationManager.GetDefault().NotificationMode`, not the COM preference or HRESULT. Unrestricted (`0`) is OFF; PriorityOnly (`1`) and AlarmsOnly (`2`) are restricted/ON. Unknown values are unavailable, never coerced to a Boolean. Every click chooses its direction from a fresh actual-mode read. A successful ON requires an actual read of PriorityOnly; successful OFF requires Unrestricted. An automatic rule keeping AlarmsOnly active therefore cannot be reported as a confirmed OFF or PriorityOnly transition.

The shared service subscribes to [NotificationModeChanged](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.toastnotificationmanagerforuser.notificationmodechanged?view=winrt-26100) when available. Events update all monitor docks. Confirmation rereads the actual getter and waits on change signals without a notification/read race. If the event is absent but the getter exists, selected utilities refresh every 3 seconds; an in-flight confirmation reads every 100 ms until its deadline. No polling runs in that fallback after deselection. Event subscriptions remain read-only until service disposal and are revoked during cleanup.

## Lifetime, failure and compatibility

`DndUtilityService` follows the existing utility service/presentation contract. One process-wide zero-wait gate serializes work across monitor docks and stays held through the existing animation completion; repeated clicks do not queue private COM calls. Initialization is lazy. Removal/disposal cancels pending verification, suppresses late state notifications and makes no additional profile change. The existing retained glass rendering is reused.

Windows 11 build 26100 or later and runtime notification-property support are required. Missing APIs, package/identity access restrictions, class/interface absence, COM/access errors and unknown profiles are reported without elevation or a Settings fallback. The public getter remains subject to its documented availability; no claim is made that every unpackaged/package configuration supports it. Private COM is not a supported Microsoft contract, so 24H2, 25H2 and subsequent cumulative updates still need live validation despite the interface probe.

Operations have a 10-second deadline. A failed request attempts a bounded fresh actual-mode read for recovery. Unsupported formats/APIs or timeout quarantine the integration until Doky restarts; the UI never claims success from HRESULT alone. A private COM call already blocked inside Windows cannot be safely terminated: it can finish after timeout, including a setter already entered. No additional setter workers start after quarantine. External mode notifications can still show actual state while the utility remains unavailable.

Manual profile changes are real user preferences and may persist. There is no automatic rollback on failure or exit, because it could overwrite newer Windows/user choices. Automatic rules and Focus sessions can override the manual selection. Concurrent changes cannot be made transactional with this private COM setter; final effective mode and subsequent notifications remain authoritative.

## Build and manual acceptance

`dotnet build GlassDock.sln -c Debug --no-restore -p:BaseOutputPath=C:\Dev\GlassDock\artifacts\dnd-build\` succeeded after the final source changes with **0 warnings and 0 errors**. Isolated output avoided DLL locks without closing Doky. `git diff --check` passed. Hardware-free regression tests for actual-mode interpretation, exact toggle profile selection and unknown-profile/mode rejection compiled but were **not run**, following the user's manual-test preference.

No COM getter/setter, notification-mode query/subscription, application launch or live Windows state change was exercised by the agent. Build success does not verify actual Do Not Disturb behavior.

Manual acceptance: enable opt-in before launching the new build; compare initial mode with Windows Notification Center; toggle both directions and inspect actual banner suppression/priority exceptions. Test external changes, scheduled/full-screen/game rules and active Focus sessions; unknown/AlarmsOnly modes; policy/access denial; timeouts; rapid and multi-monitor clicks; removal/re-add and shutdown during work; disabled opt-in; and all appearance modes. No result should be accepted merely because the COM setter returned success.
