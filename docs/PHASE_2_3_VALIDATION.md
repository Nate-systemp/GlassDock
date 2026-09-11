# Phase 2–3 desktop foundation verification

Verified locally on 2026-09-11/12, Windows 11 x64, primary 1920×1080 development display at 125% scaling. Scope follows the user's real desktop dock request, including its permission to ship a safe development partial for system integration. This is not production shell replacement.

## Build and tests

`scripts/Validate.ps1` completed locked restore, Release solution build and both test projects: **0 build warnings, 0 errors; 19 tests passed, 0 failed, 0 skipped** (18 Core, 1 Windows).
Core tests cover material normalization, dependency isolation, dock lifecycle, interrupted/stale animation completions, screen-coordinate placement, DPI and invalid input. The Windows test checks architecture isolation. These automated tests do not manipulate the taskbar.

Intermediate copy failures occurred when a previous App build was still running. Closing that process resolved the file locks; the final validation build is clean. No dependency or toolchain downgrade was needed.

## Desktop and material observations

- App launches as a real top-level desktop overlay. The default surface is a 120×5 DIP pill, with no icon or text. Its 200×44 DIP native hit region is larger than the visible indicator.
- Placement uses primary monitor bounds and physical coordinates, independently of the laboratory/control window. The configured margin was changed from 24 to 41 DIP and the indicator moved upward while staying horizontally centered.
- Entering the activation region expands the same surface into a 560×84 DIP rounded dock with seven placeholder icons. Moving away collapses to the pill after the exit delay. Repeated expansion/collapse was inspected. PointerEntered owns activation; the test tool moved the pointer with a click in the empty activation area, so the observation is not a separate mouse-move-only automation test.
- Desktop pixels remain visible outside the material. Light/dark window content and large file icons behind the dock visibly diffuse through the surface. Icons and rim remain sharp. The final renderer uses a system-backdrop CreateBackdropBrush; the host-backdrop variant produced black on this machine and was removed.
- Shared GlassEffectGraph provides blur, saturation, exposure, tint and raw/processed blending. GlassSurface provides highlight, rim and independent shadow. No desktop screenshot capture or CPU pixel-processing loop exists in the product.
- Frosted and Clear laboratory presets still render and update after opening the lab from development controls. The lab can also run independently with --lab. Its optical-refraction limitation remains explicit.
- Ctrl+Alt+Space opened the minimal Glass Home event placeholder. Bare Windows key is not intercepted. No real applications are launched by dock items.

Visual inspection used --controls, which deliberately makes the overlay available to window-inspection tools. Normal launch uses a non-activating tool window excluded from switchers. Hover/click processing does not activate the overlay; intentional development windows may activate normally.

## Recovery experiments

Each experiment began with one visible Windows taskbar. The independent recovery executable's --status command reported `{Available:true, Visible:false, Count:1}` during suppression and `{Available:true, Visible:true, Count:1}` after recovery.

| Experiment | Observation |
| --- | --- |
| Explicit development test | Watchdog handshake succeeded and taskbar became hidden. |
| Emergency keyboard route | Ctrl+Alt+F12 restored the taskbar. |
| App terminated unexpectedly | Only the verified GlassDock.App process was terminated; watchdog restored within the following two-second observation window. |
| Watchdog terminated unexpectedly | Only the verified GlassDock.Watchdog process was terminated; responsive App restored within the following two-second observation window. |
| Heartbeats withheld | A helper with a verified live App parent received HIDE but no PING; it exited/restored after the five-second timeout, within an eight-second test bound. |
| Maximum lease with live App | Taskbar remained hidden near 59 seconds and was visible after the 60-second lease expired; helper exited while App remained alive. |
| Normal App exit | Exit GlassDock closed the dock, controls, laboratory and Home placeholder; taskbar was visible and both App/helper processes were gone at the following two-second check. |
| Independent command | --restore returned RESTORED and --status verified visibility without relying on App. |
| Invalid parent | --watch with a nonexistent parent returned an error and exit code 1; taskbar remained visible. |

Process termination was confined to GlassDock's own test processes. No Explorer process was killed, restarted, modified or injected into. All taskbar operations are visibility changes isolated in GlassDock.Windows. No registry or appbar auto-hide configuration is changed.

## Limits and deferred work

- Default launch leaves the Windows taskbar visible. Opt-in suppression lasts at most 60 seconds; it is not persistent active desktop mode. The taskbar can transiently reappear before the 250 ms maintenance check. Preventing every edge reveal is not claimed.
- Multiple taskbars cause refusal. Explorer/taskbar handle replacement ends the lease. Explorer restart, multi-monitor topology changes, auto-hide variants and all taskbar configurations were not fault-injected; Explorer was deliberately left untouched.
- The recovery pair covers one process failing while the other works. Simultaneous App/watchdog loss, OS/compositor failure and a hung shell are outside that guarantee; an independently runnable --restore command remains available. No persistent shell setting has been altered.
- Bare Windows-key routing remains deferred. RegisterHotKey reserves only Ctrl+Alt+Space and Ctrl+Alt+F12; conflicts refuse taskbar tests. No low-level/global keyboard hook exists. Windows modifier/security shortcuts were not synthesized during automation; code does not intercept them.
- Single primary monitor only. Placement/DPI arithmetic is unit-tested; hotplug, monitor migration, HDR, Windows 10, GPU/device loss, high contrast and a full accessibility/performance matrix are not verified. Native composition is used, but GPU frame-time and long-duration power consumption are not measured here.
- Refraction is still a lighting approximation. The laboratory's startup size may need maximizing on scaled displays.
- No full Glass Home, discovery, real launching, running-app detection, system status, Control Center, commercial licensing/payments, networking, telemetry, updater, installer, startup registration or shell replacement was added. Licensing, installer and website remain placeholders.

## Primary references

- [Microsoft: CreateBackdropBrush](https://learn.microsoft.com/en-us/uwp/api/windows.ui.composition.compositor.createbackdropbrush) — samples pixels behind a composition visual; used in the system-backdrop adapter.
- [Microsoft: custom SystemBackdrop](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.systembackdrop) — custom brush integration for the window backdrop.
- [WinUIEx author documentation and source](https://dotmorten.github.io/WinUIEx/concepts/CustomBackdrops.html) — transparent/custom composition backdrops; own-window alpha initialization informed by TransparentTintBackdrop. No WinUIEx runtime dependency was added.
- [Microsoft: RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey) — supported registered development chords; Windows-key combinations are reserved by the OS.
- [Microsoft: ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow) — reversible window visibility; does not provide a production taskbar-replacement contract.

Stop after this desktop foundation. The next step is user visual acceptance in --controls mode; future launcher/system-integration work requires a separate milestone.
