# Control center interactions

The existing glass popup has primary controls and separate chevron/detail actions. Opening details replaces only the popup content; Back returns to the controls without resizing the dock.

- Wi-Fi/Bluetooth main tiles use `Windows.Devices.Radios`. Permission, missing hardware, hardware switches, and policy failures are reported rather than displayed as successful toggles. Tile colors reflect observed radio state, not Internet connectivity. Permission is requested on user action, not application startup.
- Wi-Fi details use Native Wi-Fi (`WlanScan`, scan-complete notifications, `WlanGetAvailableNetworkList`). Scans respect Windows location/access restrictions. Saved profiles and open networks can connect with `WlanConnect`; connected flags are checked before reporting success. New secured/enterprise networks and hidden-network setup use an explicit Settings fallback. No credentials are collected or logged. Navigating away cancels pending scan/read/wait work; it does not undo an already-submitted connection request.
- Bluetooth details enumerate paired classic/LE devices. Pairing/device-specific connection management remains an explicit Windows Settings action.
- Volume retains Core Audio master volume and mute, with no Settings primary action.
- Brightness uses `System.Management` and the supported `root/wmi` monitor provider. It controls a single supported built-in display and reads back its value. Missing/ambiguous providers or write errors disable the slider. It does not apply a fake dim overlay or change an arbitrary external display. Display settings are available in details.
- Focus state uses `FocusSessionManager` when available. Mutation requires restricted API access that GlassDock does not have. Airplane mode and do-not-disturb control/status are not implemented using undocumented APIs or registry writes. These controls explain their limitation and expose explicit secondary Settings actions.

Hardware reads run only while the popup is open. Radio/brightness refresh is throttled; scans and device lists are requested through details. Writes are serialized/coalesced, and stale detail completions cannot replace another page. No radio or brightness preference is persisted by GlassDock.

## Validation

Automated coverage checks WLAN x64 ABI sizes/offsets, connection eligibility, access-denial messaging, and refusal to attempt unconfigured secured-network connections. Tests do not disable radios or change a user's active connection.

Local capability probes verified radio enumeration, paired devices, scan completion/current-connection flags, focus status, and brightness read/write/readback at the existing value. Physical toggle/reconnect tests remain manual because they can disconnect networking or Bluetooth input devices. Check popup controls, chevrons, Back, and close/reopen on the target machine; permission and hardware availability can differ on other machines.

API references:
- https://learn.microsoft.com/en-us/uwp/api/windows.devices.radios.radio.setstateasync
- https://learn.microsoft.com/en-us/windows/win32/nativewifi/wi-fi-access-location-changes
- https://learn.microsoft.com/en-us/windows/win32/api/wlanapi/nf-wlanapi-wlanconnect
- https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmisetbrightness-method-in-class-wmimonitorbrightnessmethods
- https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager
