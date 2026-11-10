# Native volume OSD suppression investigation

Initial investigation on 2026-11-09, before the Flylet follow-up. This document records the original research-only result. A subsequent [experimental dismissal implementation and runtime report](VOLUME_OSD_SUPPRESSION.md) now supersedes the implementation-status statements below; its actual runtime test still showed a brief native flash.

## Observed ownership on this machine

Read-only Win32 probes used `FindWindowEx`, `GetWindowThreadProcessId`, `GetClassName`, `GetWindowRect`, `IsWindowVisible` and the private read-only `GetWindowBand` query. The user pressed physical volume keys and then a brightness key while the trace ran. No probe hid, moved, styled, clipped or minimized any window, sent keys, changed audio, installed hooks or modified settings.

| Item | Observed value |
| --- | --- |
| Windows version | `26H2`, build `26300.9818` (CurrentVersion registry read; OS API reports `10.0.26300.0`) |
| Owner | `C:\Windows\Explorer.EXE`, PID `8916`, session `1` |
| Native indicator HWND | `198678` / `0x30816` (session-specific, never a constant for implementation) |
| Top-level class / title | `XamlExplorerHostIslandWindow` / empty |
| Composition child | HWND `198680`, class `Windows.UI.Composition.DesktopWindowContentBridge`, title `DesktopWindowXamlSource` |
| Input descendant | HWND `133032`, class `Windows.UI.Input.InputSite.WindowClass` |
| Window band | `18` (private band identity, not a supported volume discriminator) |
| Other candidates | `AsusOSD.exe` was running, but its `ATKOSD2 MainFrm Class` window remained hidden during the captured volume/brightness tests. The observed indicator belonged to Explorer, not that ASUS window or `ShellHost.exe`. |

Volume-key trials repeatedly showed/hid the same root; for example, at `09:31:11 UTC` it became visible at `(843,949)-(1077,1011)`. During the subsequent user-confirmed brightness-key trial, at `09:33:26 UTC`, **the same root HWND** became visible at `(854,950)-(1066,1011)`, then hid again. Volume and brightness therefore share this native indicator host on this machine. Bounds differ by content; those dimensions are observations, not a safe matching rule.

The initial `EnumWindows` probe omitted this window; Microsoft's [documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows) limits that enumeration to desktop-app top-level windows on Windows 8 and later. `FindWindowEx` located the indicator and its composition child. Class matching alone also finds unrelated Shell UI: the observed Task Switching window used the same top-level and composition classes, with band `16` instead of `18`. The computer-use window inventory could not bind the indicator for control-tree inspection. No stable semantic volume-only discriminator was verified.

## Inspected implementations

The following source was read, not compiled or executed. Branch links can change; findings describe the downloaded sources inspected on the date above.

| Project and source | How it works | Limits for Doky |
| --- | --- | --- |
| [FluentFlyout: VolumeMixerWindow.xaml.cs](https://github.com/unchihugo/FluentFlyout/blob/master/FluentFlyoutWPF/Windows/VolumeMixerWindow.xaml.cs), `HideVolumeOsd` / `ShowVolumeOsd` | Searches XAML island + composition bridge + input-site hierarchy. Caches the parent HWND and extended styles, adds `WS_EX_LAYERED`/`WS_EX_TRANSPARENT`, moves it to `(-99999,-99999)`, and minimizes without activation. Restores cached styles, moves to `(0,0)` and shows without activation when its own flyout hides. | Changes a shared Shell host; lookup does not establish volume-specific content. Restoration is not an exact original-placement snapshot. The inspected app unhandled-exception handler logs; it is not an independent crash-recovery process. Source also queues hide/show work on background tasks. |
| [ModernFlyouts: NativeFlyoutHandler.cs](https://github.com/ModernFlyouts-Community/ModernFlyouts/blob/main/ModernFlyouts.Core/Interop/NativeFlyoutHandler.cs), [ShellMessageHookHandler.cs](https://github.com/ModernFlyouts-Community/ModernFlyouts/blob/main/ModernFlyouts/Interop/ShellMessageHookHandler.cs) | Finds `NativeHWNDHost`/`DirectUIHWND`, or the newer XAML/bridge pair, validates Explorer ownership and `AboveLockUX` band, and monitors Shell window events with `SetWinEventHook(...WINEVENT_OUTOFCONTEXT)`. Hiding minimizes the child and hides/minimizes the parent; restoration restores the child. Shell messages select replacement flyout types. | Private Shell identity and band assumptions. The event path still has a `NativeHWNDHost` creation check even though lookup recognizes XAML. The native handler finalizer unregisters its hook; it does not supply independent restoration after process termination. The inspected [24H2 fork](https://github.com/0x1-1/Modern-Flyouts-24H2/blob/main/ModernFlyouts.Core/Interop/NativeFlyoutHandler.cs) retains these relevant lookup/hide paths; its name alone does not prove compatibility here. |
| [ReMOSD / ReVOSD: Program.cs](https://github.com/qt-kaneko/ReMOSD/blob/netfw-cswin32/src/Program.cs) | Sends two mute app commands to instantiate the indicator, searches `NativeHWNDHost`, and changes its window region. ReMOSD retains a fixed DPI-scaled volume portion; ReVOSD applies an empty region. Rerunning toggles back to no custom region. It then exits. | Legacy class/proportion assumptions. Mutates audio during discovery and leaves window-region state beyond the tool's lifetime; no Doky lifetime/crash lease. Does not preserve an arbitrary original region. Unsuitable to copy for this shared XAML host. |

FluentFlyout's [brightness issue #791](https://github.com/unchihugo/FluentFlyout/issues/791) records the consequence of suppressing the shared native host. Its [hide/show optimization PR #1229](https://github.com/unchihugo/FluentFlyout/pull/1229) provides additional context; successful use by another app is not proof of volume-only behavior or crash recovery in Doky.

## Runtime-only options and exact blocker

1. **Persistently hide/minimize, move, cloak, make transparent or clip the host.** These avoid binary/registry patches but act on the entire native indicator window. Here that includes brightness. Normal exit could restore a captured original window state after revalidating HWND, owner PID/process lifetime and identity. `Dispose`, finalizers and exception handlers cannot restore another process's window after Doky is forcibly terminated. An independent recovery owner would need to record restoration state before mutation, monitor Doky's lifetime, handle HWND reuse and Shell replacement, and have an emergency restore path. That would address some cleanup failures, but would not make the shared window volume-specific.
2. **Transient hide on a volume event / native SHOW event.** This avoids permanent style/region changes and can stop when Doky exits. It remains a best-effort race: Core Audio callbacks have no documented ordering before Shell rendering, and [`WINEVENT_OUTOFCONTEXT` callbacks are asynchronous](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook). They provide no pre-render veto. The native popup can flash before the hide. Matching a SHOW to a recent volume callback also does not prove the shared window currently contains volume instead of brightness. No prototype was run; neither flash-free behavior nor next-key recovery after a crash was established.
3. **Intercept volume keys and perform Core Audio changes instead.** This avoids one native trigger but changes Windows' input path and must reproduce repeat, mute, limits, OEM/HID and elevated/secure-context behavior. It is not window-only suppression and was not chosen. No new input hook was installed.

The exact blocker is **shared window identity plus no verified volume-specific pre-render suppression contract**, not an inability to locate the popup. The standard window APIs used by these projects are supported APIs; relying on private Shell classes, band numbers and indicator lifetime/content is not a supported volume-OSD integration contract. No supported audio API opt-out was found in the inspected references. This is a bounded finding, not a claim that all future private mechanisms are impossible.

Consequently no optional setting was added that would suggest reliable suppression. Both volume indicators can still appear. A future experimental implementation would first need a verified volume-only discriminator and acceptable rendering behavior on target builds, then independent cleanup if it retains any foreign-window mutation. No third-party utility was installed and no Explorer process, notification policy, Windows binary or persistent preference was changed.

## Verification

- Actual runtime observation: user-assisted volume/brightness window ownership and visibility correlation, as described above. The raw local trace is `%TEMP%\doky-osd-ownership-trace.jsonl`; the temporary read-only probe is `%TEMP%\Doky-OsdOwnershipProbe.ps1`. Neither is part of Doky or a shipped service.
- Build: `dotnet build GlassDock.sln -c Debug --no-restore -p:BaseOutputPath=C:\Dev\GlassDock\artifacts\volume-hud-build\` succeeded with **0 warnings and 0 errors**. Output was isolated; no running Doky instance was closed or replaced.
- Implementation changes: documentation only. No suppression prototype, suppression setting, cleanup helper or extra test was introduced. Previous HUD tests were not rerun for this documentation-only investigation.
- Unverified: actual native-popup suppression, absence of transient flashes, Doky-only HUD display, and exit/crash restoration. Compilation proves none of those behaviors. No hardware volume/mute/audio endpoint behavior was altered by the probes.
