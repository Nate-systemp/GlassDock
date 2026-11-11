# Experimental brightness-only bridge

The opt-in runner starts the experimental Doky build and the isolated portable Windhawk instance. Exit Doky or run the stop script to restore native handling. The bridge uses the existing brightness HUD and hardware service. `DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD=1` enables the Doky lease publisher for this child process. The Windhawk mod's `enabled` setting defaults to false. The runner enables it for the current session and disables it during cleanup.

## Actual source identification

The recorded physical Fn-key run produced 31 `ShowWindow(SW_SHOWNA)` requests and one hide request. The first show's host was invisible before the original API. Its stack contains these consecutive return sites in the exact recorded Windows binaries.

| Module and return RVA | Microsoft PDB identification |
| --- | --- |
| `twinui.pcshell.dll + 0x4F977` | Host show caller |
| `Windows.Internal.HardwareConfirmator.dll + 0x2379F` | `HardwareConfirmatorHost::ShowConfirmatorHWND + 0xC3` |
| `Windows.Internal.HardwareConfirmator.dll + 0x22D94` | `HardwareConfirmatorHost::ShowBrightnessAsync$_ResumeCoro$1 + 0x2B4` |

The matching public PDB was downloaded from Microsoft's symbol server and resolved offline with DIA. The PDB identity is `C6B50D252BDF247FDCBCBAEC6677CD6C1`. The classifier requires this complete ordered return-site sequence and the verified native host identity. It does not infer brightness from size, band, elapsed time, WMI activity or absent volume events.

The allowlist requires OS build 26300, Explorer file version 10.0.26100.9818 and both exact SHA-256 values below. A different update fails initialization.

| Binary | SHA-256 |
| --- | --- |
| `Windows.Internal.HardwareConfirmator.dll` | `6EF846D28A14672725E71B4EB10F09E13E2F5A90B19BF3C9B4E662CA55FE99A9` |
| `twinui.pcshell.dll` | `1894863A224D22BD8D3EF0E1BA382B4D3D3079E0FA9A1FDEEE82F3EBACE003B6` |

The earlier read-only comparison run recorded zero matching native-host show calls with Sound suppression enabled. That limits the volume/airplane/other-flyout comparison evidence. The positive brightness classification comes from the named brightness coroutine and exact binary identity. Current manual regression testing still needs to cover other flyouts.

## Runtime and recovery

`NativeBrightnessOsdLease` creates a session-local mapping with an explicit current-user/SYSTEM DACL. PID, process creation time, session and protocol version are immutable after publication. The UI heartbeat publishes ready state and a 3-second deadline using aligned atomic writes. Normal heartbeat updates never invalidate the identity sequence. Readiness requires an available WMI display and a UI state that can present the HUD. Screenshot mode, safe/inspection mode, unavailable hardware and volume dragging withdraw readiness.

The native hook checks the current Shell HWND, ordered brightness stack, deadline and both verified process handles on each candidate. Doky death or launcher/bridge death immediately makes subsequent requests pass through. A stalled UI expires within 3 seconds. The mapping stays open until native unload but cannot renew itself. A counter in the mapping delivers suppressed requests to the existing HUD, including unchanged 0/100% presses. The UI coalesces duplicate WMI/counter events.

`ShowWindow` declines only matching brightness reveals and returns prior visibility. Matching `SWP_SHOWWINDOW` requests are converted to `SWP_HIDEWINDOW`. Hide operations and unknown contexts follow Windows' original calls. No retained foreign-window style, region, opacity or registry mutation needs recovery. Normal framework unload removes the API hooks. Recreated Explorer processes repeat all initialization checks. The first physical press after a real crash and first-frame behavior remain manual acceptance checks.

The runner owns the temporary Windhawk session, watches Doky exit and restores its saved portable engine configuration. `Stop-BrightnessBridge.ps1` disables this mod and requests normal Windhawk exit. It leaves Doky running. No Explorer termination is part of either script.

An earlier read-only adapter crashed Explorer because it supplied stack-local trampoline output addresses to Windhawk's deferred hook registration. The corrected read-only adapter and active bridge pass persistent callback pointer storage directly. The active bridge remains native code in Explorer and carries Shell stability risk.

## Build and opt-in launch

Use the reviewed portable Windhawk 1.7.3 SDK under `artifacts/native-osd-poc/windhawk-portable`.

```powershell
dotnet build C:\Dev\GlassDock\GlassDock.sln -c Debug --artifacts-path C:\Dev\GlassDock\artifacts\brightness-suppression-build
pwsh -NoProfile -File C:\Dev\GlassDock\experiments\NativeOsdSuppression\Build-BrightnessBridge.ps1
pwsh -NoProfile -File C:\Dev\GlassDock\experiments\NativeOsdSuppression\Run-BrightnessBridge.ps1 -Enable
```

The runner requires existing Doky and Windhawk instances to be closed. The bundled SDK build uses warnings as errors. It installs no service or startup entry. Its running status and matched-request count are written to `artifacts/native-osd-poc/brightness-active.json`.

Pass `-EnableDnd -EnableEnergySaver` to opt into those existing experimental utilities for the launched Doky process. These flags set their required startup environment variables. Without opt-in, clicking either utility displays its unavailable reason. Explicit toggle requests wait for an in-progress hover refresh, while repeated clicks during a toggle remain locked.

To stop from another terminal or Task Manager's Run new task, use this command.

```powershell
pwsh -NoProfile -File C:\Dev\GlassDock\experiments\NativeOsdSuppression\Stop-BrightnessBridge.ps1
```

For manual acceptance, let the native popup finish hiding before the first test. Test Fn brightness once, held/rapid presses, unchanged 0/100%, Task Manager foreground and dragging Doky's HUD. Verify actual brightness changes and that only Doky's HUD appears. Then check volume, airplane indicators, Quick Settings and notifications. Finally stop the bridge and verify normal native brightness OSD recovery. A request counter proves a matching API veto, while rendered-frame absence requires visual verification.

The final Debug solution build passed with 0 warnings and 0 errors. The real Windhawk SDK bridge build passed with warnings treated as errors. After opt-in launch, the live counter recorded 19 matched brightness show vetoes while the same Explorer process and Doky remained responsive. This confirms execution of the brightness-only suppression branch. Visual absence of the native popup, including its first frame, still requires the user's manual confirmation. Existing unrelated taskbar/recovery behavior retains its development safety limits.
