# Experimental Sound and Brightness bridge

Normal Doky EXE launches supervise the reviewed Windhawk runtime inside `GlassDock.Windows`. The runtime is built by `Build-BrightnessBridge.ps1` and copied beside the app during build or publish. Its verified per-user copy stores mutable configuration under LocalAppData. The existing brightness HUD and hardware services publish readiness. Safe and inspection modes skip startup. `DOKY_ENABLE_EXPERIMENTAL_BRIGHTNESS_OSD=0` disables this integration for laboratory runs.

## Actual source identification

The recorded physical Fn-key run produced 31 `ShowWindow(SW_SHOWNA)` requests and one hide request. The first show's host was invisible before the original API. Its stack contains these consecutive return sites in the exact recorded Windows binaries.

| Module and return RVA | Microsoft PDB identification |
| --- | --- |
| `twinui.pcshell.dll + 0x4F977` | Host show caller |
| `Windows.Internal.HardwareConfirmator.dll + 0x2379F` | `HardwareConfirmatorHost::ShowConfirmatorHWND + 0xC3` |
| `Windows.Internal.HardwareConfirmator.dll + 0x22D94` | `HardwareConfirmatorHost::ShowBrightnessAsync$_ResumeCoro$1 + 0x2B4` |

The matching public PDB was downloaded from Microsoft's symbol server and resolved offline with DIA. The PDB identity is `C6B50D252BDF247FDCBCBAEC6677CD6C1`. The classifier requires this complete ordered return-site sequence and the verified native host identity. It does not infer brightness from size, band, elapsed time, WMI activity or absent volume events.

The touchpad reproduction recorded the same first two return sites followed by `Windows.Internal.HardwareConfirmator.dll + 0x24A14`. The matching Microsoft PDB resolves this to `HardwareConfirmatorHost::ShowVolumeAsync$_ResumeCoro$1 + 0x2B4`. Disassembly confirms a call to `ShowConfirmatorHWND` immediately before that return address. This exact ordered stack now identifies volume requests that bypass the media-key hook. The brightness stack remains independently classified. A visible native volume host can otherwise continue displaying subsequent utility updates.

The allowlist requires OS build 26300, Explorer numeric file version 10.0.26100.9818 or 10.0.26100.9974, the exact hardware DLL hash below, and either listed twinui DLL hash. Other binaries fail initialization. The 9974 Explorer binary retains a stale 9818 version string, so compatibility uses `VS_FIXEDFILEINFO` numeric fields.

| Binary | SHA-256 |
| --- | --- |
| `Windows.Internal.HardwareConfirmator.dll` | `6EF846D28A14672725E71B4EB10F09E13E2F5A90B19BF3C9B4E662CA55FE99A9` |
| `twinui.pcshell.dll` | `1894863A224D22BD8D3EF0E1BA382B4D3D3079E0FA9A1FDEEE82F3EBACE003B6` |
| `twinui.pcshell.dll` 10.0.26100.9974 | `0D44E428FE81F68522678F0958DB8B039E1E7DC3175B0D6A641918C3B125A2AE` |

The 9974 update retains the OSD host function at RVA `0x4F940–0x4F994` and the return site `0x4F977`. Offline disassembly against the previously verified 9818 binary shows the same instructions with the relocated delay-import operand. Both calls resolve to `ShowWindow` in `api-ms-win-rtcore-ntuser-window-l1-1-0.dll`. The hardware DLL remains byte-identical. Current-device suppression requires a live lease, the reviewed runtime, and physical input verification.

The runner defaults to the current ordinary Release output and forwards arguments as a one-shot launcher. An explicit `-WindhawkPath` selects the preserved PowerShell laboratory supervisor. Direct EXE startup uses the bundled runtime and retains no PowerShell host.

Opening Quick Settings keeps the native volume lease ready, matching the existing brightness lease. The existing custom control and pill visibility rules continue to handle the UI. The lifecycle probe checks volume readiness while Quick Settings is open.

Status-file I/O failures are isolated from supervision. A reader holding `brightness-active.json` cannot terminate the bridge or unload suppression. `StatusTests.ps1` exercises exclusive file locks and successful writes after release.

The earlier read-only comparison run recorded zero matching native-host show calls with Sound suppression enabled. That limits the volume/airplane/other-flyout comparison evidence. The positive brightness classification comes from the named brightness coroutine and exact binary identity. Current manual regression testing still needs to cover other flyouts.

## Runtime and recovery

`NativeBrightnessOsdLease` creates a mapping named `Local\Doky.NativeOsd.v2.<session>.<pid>.<creation-time>` with an explicit current-user/SYSTEM DACL. The mod receives the expected owner through its private configuration and verifies the published identity. Per-owner mappings permit immediate relaunch after a crash. The laboratory's original session mapping remains supported. The UI heartbeat publishes ready state and a 3-second deadline using aligned atomic writes. Readiness requires the existing hardware and HUD conditions. Screenshot mode, safe/inspection mode, unavailable hardware and volume dragging withdraw readiness.

The native hook checks the current Shell HWND, ordered hardware stack, deadline and verified process handles on each candidate. Doky owns both identities in integrated mode. Doky death immediately restores pass-through, and a stalled UI expires within 3 seconds. The mapping stays open until native unload but cannot renew itself. Suppressed-request counters feed the existing HUD, including unchanged 0/100% presses.

The same 128-byte mapping has an optional volume extension at offsets 64 (ready), 72 (deadline), and 80 (request count). The original brightness fields and version remain compatible. Older publishers leave the extension zeroed, so volume requests pass through. Volume readiness requires the native-volume suppression setting, available Core Audio, and an available Doky HUD presentation state. Each utility has its own 3-second deadline and counter. Disposing the publisher revokes both. The UI coalesces duplicate Core Audio/counter volume notifications using the existing 100 ms brightness coalescing interval. The runner reports `SuppressedVolumeRequests` separately.

`ShowWindow` declines matching Sound or Brightness reveals with their respective live leases and returns prior visibility. Matching `SWP_SHOWWINDOW` requests are converted to `SWP_HIDEWINDOW`. Hide operations and unknown contexts follow Windows' original calls. No retained foreign-window style, region, opacity or registry mutation needs recovery. Normal framework unload removes the API hooks. Recreated Explorer processes repeat all initialization checks. The first physical press after a real crash and first-frame behavior remain manual acceptance checks.

`NativeOsdBridgeHost` waits on engine and Shell process handles after activation. It uses bounded waits during startup and recovery. A private session lock prevents duplicate ownership. A Windows job closes the owned engine if Doky crashes. Normal asynchronous shutdown restores the dock's taskbar lease, revokes OSD readiness, requests Windhawk exit, and restores private configuration. Recovery permits at most three starts per minute. Another Windhawk instance prevents activation.

The reserved lease bytes report supervisor activation at 88, engine PID at 92, Shell PID at 96, and native module acknowledgment at 100. `Stop-BrightnessBridge.ps1` writes a stop request at 104. The existing UI heartbeat consumes it and stops supervision while leaving Doky running. A subsequent EXE launch recreates supervision. The native module only acknowledges startup after its existing compatibility and hook checks succeed.

An earlier read-only adapter crashed Explorer because it supplied stack-local trampoline output addresses to Windhawk's deferred hook registration. The corrected read-only adapter and active bridge pass persistent callback pointer storage directly. The active bridge remains native code in Explorer and carries Shell stability risk.

## Build and opt-in launch

Use the reviewed portable Windhawk 1.7.3 SDK under `artifacts/native-osd-poc/windhawk-portable`.

```powershell
pwsh -NoProfile -File C:\Dev\GlassDock\experiments\NativeOsdSuppression\Build-BrightnessBridge.ps1
dotnet build C:\Dev\GlassDock\GlassDock.sln -c Release
C:\Dev\GlassDock\src\GlassDock.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe
```

The runtime build uses warnings as errors. It prepares the reviewed engine and its transitive runtime dependencies under `artifacts/native-osd-runtime`. App build and publish copy that payload. The compiler and Windhawk UI are excluded from the runtime payload. Windhawk update checks are disabled for this private instance. The lease exposes live activation and request counters. `brightness-active.json` remains a laboratory-supervisor diagnostic.

Pass `-EnableDnd -EnableEnergySaver` to opt into those existing experimental utilities for the launched Doky process. These flags set their required startup environment variables. Without opt-in, clicking either utility displays its unavailable reason. Explicit toggle requests wait for an in-progress hover refresh, while repeated clicks during a toggle remain locked.

To stop from another terminal or Task Manager's Run new task, use this command.

```powershell
pwsh -NoProfile -File C:\Dev\GlassDock\experiments\NativeOsdSuppression\Stop-BrightnessBridge.ps1
```

For manual acceptance, let the native popup finish hiding before the first test. Test Fn brightness once, held/rapid presses, unchanged 0/100%, Task Manager foreground and dragging Doky's HUD. Verify actual brightness changes and that only Doky's HUD appears. Then check volume, airplane indicators, Quick Settings and notifications. Finally stop the bridge and verify normal native brightness OSD recovery. A request counter proves a matching API veto, while rendered-frame absence requires visual verification.

The final Debug solution build passed with 0 warnings and 0 errors. The real Windhawk SDK bridge build passed with warnings treated as errors. After opt-in launch, the live counter recorded 19 matched brightness show vetoes while the same Explorer process and Doky remained responsive. This confirms execution of the brightness-only suppression branch. Visual absence of the native popup, including its first frame, still requires the user's manual confirmation. Existing unrelated taskbar/recovery behavior retains its development safety limits.
