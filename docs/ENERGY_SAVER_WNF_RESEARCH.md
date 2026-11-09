# Energy Saver Shell/WNF investigation

Research date: 2026-11-07. **Research only; no setter was executed.**

## Conclusion

A direct, non-elevated experimental prototype is technically plausible on the inspected **Windows 11 26100.9818 x64** installation. Matching Microsoft symbols and offline disassembly establish a real Settings-to-kernel override path, a four-byte three-value payload, and a WNF DACL permitting ordinary-user updates. This is an **undocumented implementation**, not a supported API or a verified working Doky toggle. Newer cumulative updates/builds require separate validation.

## Exact binary evidence

Both files report version `10.0.26100.9818`. Addresses below are RVAs, not process addresses.

| Binary | SHA-256 | Matching Microsoft PDB GUID + age |
| --- | --- | --- |
| `C:\Windows\System32\SettingsHandlers_OneCore_BatterySaver.dll` | `10D369BEC2623A23C92364060B0D1424B0BA0BA29BE65BD852B236D53A107FA8` | `BE702CE3E09A55F212672CE298792C751` |
| `C:\Windows\System32\ntoskrnl.exe` | `1DD53F287E2B1ED6B85044CEF0D3F55607240319E9C9842BC4B24AE353E55510` | `983AD639682E5BD1EFAC90D9275002E21` |

PDBs were obtained from Microsoft's symbol server, using `SettingsHandlers_OneCore_BatterySaver.pdb` and `ntkrnlmp.pdb` respectively. Target files were read as PE data, not loaded to execute their code. Temporary analysis scripts, symbols and traces are under `C:\Users\NathP\AppData\Local\Temp\Doky-energy-research-26100.9818`.

## Actual request and consumer sequence

1. Settings `CBatterySaverOverrideControl::SetValue` (`0x2B920`) calls `CBatteryStateSingleton::SetEnergySaverQuickActionsOverride` (`0x476F0`). The helper initializes its singleton, locks the existing critical section, and chooses a value using effective state, existing override, threshold and power conditions.
2. At `0x477CF–0x477E8`, it calls `RtlPublishWnfStateData(stateName, NULL, &payload, 4, NULL)`. The constant at DLL RVA `0x59328` is exactly `WNF_PO_ENERGY_SAVER_OVERRIDE = 0x41C6013DA3BC3075`. The payload is a little-endian **DWORD**, not a one-byte BOOLEAN or a two-state BOOL contract. Failed NTSTATUS results are converted/reported as failures.
3. Kernel `PopEsWorker` registers `PopEsWnfSubscriptionOverrideCallback` for that state (`0xACFD68–0xACFD89`). The callback (`0xA4CAE0`) reads into a four-byte buffer, rejects values above `2`, stores the accepted value in `PopEsMode` under `PopEsLock`, then schedules energy-state evaluation (`0xA4CB5B–0xA4CB87`).
4. Worker processing reaches `PopEsUpdateSetting` and `PopEsUpdateState`; the latter calls `PopEsEvaluateNextStateV2` (`0xABCB5C`). Policy, battery/power conditions, background policy and host state participate in the result. `PopEsPublishStateV2` publishes resulting state. A successful WNF publication is therefore request acceptance, **not confirmation that Energy Saver engaged**.
5. Settings separately subscribes: `WnfStateEnergySaverOverrideChangedCallback` (`0x47F10`) requires a nonnull four-byte payload, then `UpdateEnergySaverOverride` (`0x47B20`) recalculates/notifies its UI.

| DWORD | Meaning established by publisher and kernel branches | Limitation |
| --- | --- | --- |
| `0` | Manual disable/suppress automatic threshold activation | Not unconditional OFF: policy/other state sources still participate; kernel converts this mode to `2` on AC in the inspected evaluator. |
| `1` | Manual enable override | Requests Energy Saver; effective state still requires independent confirmation. |
| `2` | No manual override / automatic evaluation | Does not mean OFF; the automatic threshold can still engage Energy Saver. |

These names describe observed behavior, not published Microsoft enum identifiers. Override values must not be confused with the separate `ENERGY_SAVER_STATUS` enumeration.

## Quick Settings versus “Always On”

The distinction is **policy, not two independent setters** in this build:

- Quick Actions turns an effectively OFF state on with `1`. Turning an effectively ON state off can publish `0` or `2`, depending on existing override and threshold/power conditions. A blind `0/1` toggle does not reproduce this behavior.
- `CEnergySaverAlwaysOnToggle::SetValue` (`0x3CCD0`) calls `SetEnergySaverAlwaysOnOverride` (`0x474B0`), publishing to **the same WNF name**. Existing override `1` becomes `2`; existing `0` or `2` becomes `1`.
- `IsAlwaysOnToggleEngaged` (`0x466BC`, decision at `0x46750–0x4676C`) checks group policy or override `1`. It does not simply test effective Energy Saver ON. `IsBatterySaverEngaged` (`0x46B40`) checks the separate effective-state field instead.
- `RecalculateEnergySaverOverride` (`0x4719C`) can clear a manual-disable override to `2` under its power-source conditions. The kernel evaluator independently contains an AC reset of mode `0`.
- Decoding the state name gives well-known lifetime, system scope, **PersistData = 0**. No persistent preference write occurs in either inspected setter. This does not exclude persistence by another Windows component, but **reboot persistence and all reset conditions remain unproven**. Do not promise a separate persistent “Always On” preference or claim Quick Actions preserves it: these paths share override state.

## Permissions: ordinary user is plausible

Read-only inspection of value `41C6013DA3BC3075` under `HKLM\SYSTEM\CurrentControlSet\Control\Notifications` decoded this self-relative security descriptor:

```text
D:(A;;CCDC;;;SY)(A;;CCDC;;;AC)(A;;CCDC;;;WD)
```

Each allow ACE has numeric mask `0x3`: SYSTEM, ALL APPLICATION PACKAGES and Everyone. `CCDC` is the generic SDDL formatter's spelling; it does not mean registry permissions here. In the inspected kernel, `ExpNtUpdateWnfStateData` requests **access mask `0x2`** in `SeAccessCheck` (`0x916DC1–0x916DD5`). Thus the provisioned DACL permits the update access required by an ordinary desktop user; it does not require a Settings-only identity or administrator membership on this installation.

The research process's administrator-role check returned false. Reading the state succeeded without elevation. No update was attempted to test authorization. Policy, live descriptor differences, unsupported environments and subsequent builds may still prevent or override a request. Permission to publish does not establish behavioral safety or compatibility.

## Independent state observation

A passive `PowerSettingRegisterNotification` callback for **`GUID_ENERGY_SAVER_STATUS = 550E8400-E29B-41D4-A716-446655440000`** registered successfully from the non-elevated process and delivered `0` (`ENERGY_SAVER_OFF`), then was unregistered. A separate read-only `NtQueryWnfStateData` returned success, length `4`, override `2`, change stamp `1`. This establishes availability of independent status observation on this machine; no state transition was induced or verified.

For a future authorized experiment, subscribe before requesting a change and require the **actual status notification**, not a WNF readback echo or successful publisher return, to confirm the result. Validate GUID and four-byte data length; handle OFF, STANDARD and HIGH_SAVINGS explicitly. Unknown values, timeout, policy disagreement or an unsupported mechanism must not produce optimistic success. Automatic mode can legitimately remain ON.

## Feasibility and remaining limits

An isolated, opt-in prototype can reasonably be considered for this exact build after explicit implementation authorization. It would need runtime compatibility rejection, Shell-equivalent value selection, serialized requests, independent power-status confirmation and graceful failure. Do not elevate, patch binaries, alter power plans/thresholds or write the notification registry descriptor as a workaround.

Still unverified: successful non-elevated publication and resulting power transition, full reset/reboot persistence behavior, battery versus battery-free/AC devices, policy/adaptive/host interactions, concurrent Shell changes, ARM64, and every build newer than 26100.9818. No production reliability claim is justified.

## References and scope

- [Microsoft power-setting GUIDs](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids): independent Energy Saver status contract; currently carries a prerelease disclaimer.
- [Microsoft PowerSettingRegisterNotification](https://learn.microsoft.com/en-us/windows/win32/api/powersetting/nf-powersetting-powersettingregisternotification): callback notification API.
- [Microsoft symbol server](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/microsoft-public-symbols): matching-symbol provenance; the binary RVAs above are the primary evidence for this exact build.
- [System Informer native WNF declarations](https://github.com/winsiderss/systeminformer/blob/master/phnt/include/ntexapi.h): native query/update signatures and scope definitions; not a supported Energy Saver setter contract.
- [wnfun well-known names](https://github.com/ionescu007/wnfun/blob/master/script_python/WellKnownWnfNames.py): name reference, independently checked against the local binary bytes.

Only this research document was added. Doky implementation and existing uncommitted work were preserved. No WNF/registry writes, toggles, privilege elevation, Windows patches, build, tests or application launch were performed for this investigation.

## Subsequently authorized experimental implementation (2026-11-09)

`EnergySaverNative` and `EnergySaverUtilityService` now implement an isolated opt-in path. Set `DOKY_ENABLE_EXPERIMENTAL_ENERGY_SAVER=1` in Doky's process environment **before launching Doky**. Without it, the service performs no WNF queries/writes or power-notification registration. The Utilities catalog exposes Energy Saver as experimental; without opt-in its button reports unavailable. The existing `BatterySaver` persisted enum ID is reused without renumbering other utilities.

Compatibility is deliberately restricted to x64 and the exact Settings DLL **and kernel SHA-256 fingerprints above**. Other binaries, including newer builds, are rejected until independently validated. Opt-in is not a compatibility bypass. There is no elevation, Windows patch, registry write, power-plan selection or threshold modification.

The process-wide service is shared across monitor docks. It uses the existing animation/presentation completion contract and a zero-wait gate held through native confirmation and presentation completion. It lazily subscribes to `GUID_ENERGY_SAVER_STATUS`, handles OFF/STANDARD/HIGH_SAVINGS independently of WNF values, and keeps that read-only subscription until service disposal. External power-state notifications synchronize the icon. Temporary subscriptions obtain fresh initial Windows status before and after requests; confirmation waits for notifications rather than polling or sleeping.

ON publishes DWORD `1` through the researched five-argument `RtlPublishWnfStateData` sequence. OFF while override `1` restores a previously observed `0` where applicable; otherwise it requests `2` when automatic threshold activation is inactive, or `0` when the DC battery percentage is at/below the configured threshold. OFF from another override requests `0`. Battery conditions and threshold are queried through `GetSystemPowerStatus` and read-only `PowerGetActiveScheme`/`PowerReadDCValueIndex`; unknown conditions are rejected. Policy, adaptive and host behavior can still prevent OFF. No override value is treated as effective state.

The previous override is remembered in memory for a successful Doky ON/OFF cycle. It is reused only if the current value **and WNF change stamp** still match Doky's confirmed publication. Otherwise that saved intent is discarded. A pre-write value/stamp recheck reduces races, but the verified Rtl publisher has **no compare-and-swap parameter**: an external write can still land between the final query and publication. This limitation is not hidden. A final override mismatch or independent-state mismatch fails confirmation.

A successful write alone never completes a successful toggle. Actual power status must independently match the requested direction; malformed notifications disable the integration. A 10-second deadline quarantines timed-out work until restart. A private native call already in progress cannot be forcibly canceled safely and could finish later; no additional operations are started after quarantine. Permission and policy failures are reported without elevation or success claims. Removing the utility or exiting Doky performs no implicit power changes. Failed attempts are not blindly rolled back, because that could overwrite newer Windows/user intent. A suppression override can remain until Windows or the user changes/resets it; reboot and all reset conditions remain unverified. Schedule/threshold preferences are never edited.

Both states use the existing filled battery-charging Phosphor asset. ON uses the existing green battery accent (`#30D158`) and white active background. Glass, other utilities and their native backends remain unchanged.

Hardware-free regression cases cover override selection, restoration versus automatic threshold activation, effective status enumeration, and rejection of unknown payload/status formats. They are compiled, not executed, following the user's manual-test preference. Build results and hardware limits are reported separately in the implementation response. Manual acceptance must compare actual Windows Energy Saver status in both directions, AC/DC/threshold and battery-free behavior, external changes and concurrent overrides, permission/policy failures, unknown/new binaries, rapid/multi-monitor clicks, removal/shutdown during work and every appearance mode. **No live WNF publication or physical power-state transition has been exercised by the agent.**

Final build verification: `dotnet build GlassDock.sln -c Debug --no-restore -p:BaseOutputPath=C:\Dev\GlassDock\artifacts\energy-saver-build\` succeeded after the final source changes with **0 warnings and 0 errors**. Isolated output avoided overwriting DLLs belonging to a running Doky instance; no process was stopped. Tests compiled but were not run. Build success proves compilation, not compatibility of the private API or actual Energy Saver behavior on hardware.
