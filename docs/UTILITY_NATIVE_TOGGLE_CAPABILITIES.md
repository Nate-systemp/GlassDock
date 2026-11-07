# Native utility toggle capability audit

Checked 2026-11-07 against the repository's Windows SDK 10.0.26100.0 and Microsoft's public API documentation.

## Result

The requested three direct toggles cannot be implemented faithfully using the supported public APIs identified in this audit. No radio, power, display, registry or shell state was changed. No substitute operation is presented as a successful toggle.

At the original audit, the utility catalog enabled Wi-Fi and Bluetooth only. Airplane mode and Battery saver were unavailable placeholders, with no Night Light entry. The subsequently authorized experimental Airplane implementation below supersedes that catalog snapshot and the original supported-public-API-only boundary for Airplane mode alone.

## Airplane mode

`Windows.Devices.Radios.Radio` supports discovery, access requests, per-device `SetStateAsync` and `StateChanged`. It does not expose a public global airplane-mode getter/setter. Turning off every enumerated radio is not equivalent to setting Windows airplane mode; Windows can retain Wi-Fi/Bluetooth preferences while airplane mode is enabled. Inferring airplane mode from all radios being off is also incorrect.

The driver-facing HID airplane-mode controls are hardware/driver integration, not a general desktop application API. Undocumented Radio Management COM interfaces are outside the requested supported-API constraint.

References:

- [Radio class](https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio?view=winrt-26100)
- [Airplane mode radio management](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/airplane-mode-radio-management)
- [Windows network settings and airplane-mode preferences](https://support.microsoft.com/en-gb/windows/experience/connectivity-networking/essential-network-settings-and-tasks-in-windows)

### Authorized experimental implementation

The user subsequently authorized private COM integration for Airplane mode only. `AirplaneModeNative` in GlassDock.Windows owns the private `RadioManagementAPI` coclass (`581333f6-28db-41be-bc7a-ff201f12f3f6`) and `IRadioManager` interface (`db3afbfb-08e6-46c6-aa70-bf9a34c30ab7`). The IUnknown vtable prefix includes IsRMSupported, GetUIRadioInstances, GetSystemRadioState, then SetSystemRadioState; the unused slot is retained to preserve ABI layout.

Verified source references:

- [RadioMan interface declarations and global mode example](https://github.com/fafalone/RadioMan#how-it-works)
- [SleepToAirPlane pinned implementation](https://github.com/peter-tanner/SleepToAirPlane/blob/d9ee8ad7c5f06f0bfe940ca662ab1b6579c19cad/SleepToAirPlane/main.cpp)

The parameter means **global radios enabled**, not airplane enabled: `0` means Airplane ON, and `1` means Airplane OFF. Both references were inspected; SleepToAirPlane writes false when entering airplane mode and true when leaving. Unknown getter values are rejected. This verifies the implementation against source, not against a live adapter.

Every toggle reads the actual global state before calculating its inverse, invokes the global setter, then reads until Windows confirms that state. There is no individual-radio loop, registry write, Settings launch or optimistic success state. Setter failures attempt a getter recovery. If state cannot be read, the service retains an explicit unknown state and the button reports unavailable rather than claiming OFF.

One service is shared by monitor docks. Refresh and toggle requests use a zero-wait gate; clicks do not queue. A dedicated background STA owns each COM session, including release and CoUninitialize. The existing utility animation and click lock are reused; the shared gate remains held through presentation completion. Only Wi-Fi/Bluetooth retain details/Settings morph behavior.

While selected, the utility refreshes global state every 3 seconds, and also refreshes on hover. Deselecting stops periodic refresh. No private notification sink, global hook or change to the existing Wi-Fi/Bluetooth monitoring is introduced. External state changes can therefore take up to one refresh interval plus the native read to appear.

The experimental path requires Windows 11 build 22000 or later and successful runtime COM/capability probing; this is not certification of every Windows 11 release. Set `DOKY_DISABLE_EXPERIMENTAL_AIRPLANE_MODE=1` in Doky's process environment before starting Doky to prevent all Airplane COM access. The utility can also be removed through the existing Utilities settings.

The native operation has a 10-second deadline. Missing class/interface, unsupported state/capability, or timeout quarantines the feature until Doky restarts. Access denial is reported without elevation; later refresh may recover if policy changes. A blocking private COM call cannot be safely force-aborted: a timed-out background call may finish later, including a setter already in progress. No further native workers are started after quarantine, so repeated clicks cannot accumulate hung operations. There is no claim of process isolation against a native access violation or a changed private ABI.

Validation: `dotnet build GlassDock.sln -c Debug --no-restore` succeeded with 0 warnings and 0 errors. New hardware-free regression cases cover polarity, duplicate requests through native/presentation completion, recovered state after failure, external refresh, timeout quarantine, and disposal. They were compiled, not run, following the user's manual-verification preference. No live getter, setter or radio operation was exercised by the agent.

Manual hardware acceptance:

1. On a Windows 11 device with actual radio-management support, add the experimental utility and compare its initial state with Quick Settings.
2. Toggle both directions. Confirm `0` produces Airplane ON and `1` OFF in Windows; Wi-Fi/Bluetooth may retain Windows' own airplane-mode preferences.
3. Change airplane mode externally and confirm synchronization within the refresh interval. Test rapid clicks, multiple monitor docks, and reduced motion.
4. Check hardware airplane switches, policy/access denial, radio/service unavailability, timeout/quarantine, shutdown/removal during work, and the environment disable switch.
5. Verify stable glass and existing Wi-Fi/Bluetooth behavior across all appearance modes. Do not treat a successful HRESULT alone as acceptance; compare actual Windows global state.

## Night Light

No documented public Night Light state getter/setter or change notification suitable for this desktop utility was identified. `ms-settings:nightlight` opens the settings page, which the request excludes. CloudStore serialization edits, private BlueLightReduction interfaces, display gamma manipulation and scripted Quick Settings clicks do not establish the requested reliable, supported Night Light API contract.

Do not display a fabricated OFF state or imitate Night Light with an independent color effect. Without a supported state API, external-state synchronization cannot be promised either.

References:

- [Documented Windows Settings URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings)
- [Windows display brightness and color](https://support.microsoft.com/en-us/windows/hardware/display-graphics/change-display-brightness-and-color-in-windows)

## Energy Saver

`Windows.System.Power.PowerManager.EnergySaverStatus` is read-only, with an `EnergySaverStatusChanged` event. The legacy status describes Battery Saver, including battery/AC restrictions; it must not be assumed to fully represent newer Windows 11 Energy Saver behavior on AC or battery-free devices.

SDK 26100 also declares `GUID_ENERGY_SAVER_STATUS` for newer Energy Saver notifications, replacing the older `GUID_POWER_SAVING_STATUS` on newer systems. Status notification support does not provide a setter. The available public documentation marks the newer notification contract as prerelease, so compatibility must be verified before relying on it for a future status-only feature.

`PowerSetUserConfiguredACPowerMode` and `PowerSetUserConfiguredDCPowerMode` select Best Power Efficiency, Balanced or Best Performance on Windows 11. They do not set the Energy Saver switch. `PowerSetActiveScheme`, power throttling requests and modified automatic activation thresholds are not equivalent either. No supported public direct Energy Saver setter was identified in this SDK/documentation audit.

References:

- [EnergySaverStatus property](https://learn.microsoft.com/en-us/uwp/api/windows.system.power.powermanager.energysaverstatus?view=winrt-26100)
- [Legacy EnergySaverStatus and battery restrictions](https://learn.microsoft.com/en-us/uwp/api/windows.system.power.energysaverstatus?view=winrt-26100)
- [Power setting notification GUIDs](https://learn.microsoft.com/en-us/windows/win32/power/power-setting-guids)
- [PowerSetUserConfiguredDCPowerMode](https://learn.microsoft.com/en-us/windows/win32/api/powrprof/nf-powrprof-powersetuserconfigureddcpowermode)

## Implementation boundary

The existing shared Wi-Fi/Bluetooth transition and zero-queue service gates remain available for real supported toggles. No new animation, request queue, polling loop or speculative service is added for unavailable operations. Sound and Brightness remain untouched.

These findings are scoped to the checked SDK and documentation, not a claim that a future Windows release can never introduce these APIs. Functional implementation remains blocked by the supported-public-API requirement. Hardware capability and policy checks cannot supply missing API contracts.

## Validation

`dotnet build GlassDock.sln -c Debug --no-restore` passed with 0 warnings and 0 errors. `git diff --check` passed. Automated tests and runtime/hardware operations were not run, per the user's manual-verification preference. A successful build verifies compilation of the current working tree; it does not prove these unavailable toggles exist or verify earlier UI changes visually.
