# Graph Report - GlassDock  (2026-11-18)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 1261 nodes · 2679 edges · 60 communities (42 shown, 18 thin omitted)
- Extraction: 95% EXTRACTED · 5% INFERRED · 0% AMBIGUOUS · INFERRED: 137 edges (avg confidence: 0.85)
- Token cost: 9,020 input · 463 output

## Graph Freshness
- Built from commit: `338926cb`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Monitor Brightness Control
- Desktop Environment Integration
- Window Overlay Management
- Bluetooth Device Management
- Windows Process Management
- Taskbar State Management
- Airplane Mode Service
- Master Volume Service
- Night Light Cloud Sync
- Do Not Disturb Integration
- Wi-Fi Network Control
- Desktop Screen Capture
- Native Power Management
- Interactive Window Hosting
- SystemControlAudio
- Raw Input Device Handling
- Audio Volume Control
- Input Recovery Bridge
- Keyboard Input Messaging
- Window Capture Graphics
- WifiNetwork
- Keyboard Shortcut Handling
- Screen Capture and Power Monitoring
- WifiUtilityService
- AirplaneModeNative
- Audio Device Enumeration
- DeviceCallback
- Device Topology Connections
- Audio Device Management
- Native Brightness and Volume OSD
- InputDesktopMonitor
- WindowsKeyHook
- TaskbarDevelopmentSession
- Task
- Windows Keyboard Gestures
- Application Shutdown State
- CaptureDrawGate
- ERole
- Volume Notification Callbacks
- WifiNetworkCaption.cs
- GlassDock.Core.Desktop
- Dock Hover Animation
- DockPerformanceDiagnostics
- Docking State Machine
- EDataFlow
- Dock Geometry Rendering
- Dock Layout and Sizing
- Dock Window Management
- Drag Motion Animation
- Pointer Gesture Handling
- Volume HUD State
- GlassHomeSession
- BluetoothDeviceKind
- DockIconMotion
- ITaskbarController
- Taskbar Lease Management
- Input Lifecycle Recovery
- WifiProfile.cs
- DesktopPlacement.cs

## God Nodes (most connected - your core abstractions)
1. `BluetoothUtilityService` - 48 edges
2. `GlassDock.Windows.Desktop` - 48 edges
3. `WindowsOverlayManager` - 41 edges
4. `GlassDock.Core.Desktop` - 35 edges
5. `DesktopWindowFocus` - 34 edges
6. `BrightnessHardware` - 32 edges
7. `WifiNetworkControl` - 30 edges
8. `WindowsKeyboardService` - 30 edges
9. `DesktopCaptureSource` - 28 edges
10. `SystemControlAudio` - 27 edges

## Surprising Connections (you probably didn't know these)
- `WindowsKeyboardService` --references--> `UtilityKeyInput`  [EXTRACTED]
  WindowsKeyboardService.cs → UtilityKeyInput.cs
- `WindowsKeyboardService` --references--> `WindowsInputHelperBridge`  [EXTRACTED]
  WindowsKeyboardService.cs → WindowsInputHelperBridge.cs
- `WindowsKeyboardService` --references--> `WindowsKeyHook`  [EXTRACTED]
  WindowsKeyboardService.cs → WindowsKeyHook.cs
- `WifiUtilityService` --references--> `WifiNetwork`  [EXTRACTED]
  WifiUtilityService.cs → WifiNetworkControl.cs
- `WindowsKeyHook` --references--> `VolumeKeyCapture`  [EXTRACTED]
  WindowsKeyHook.cs → NativeVolumeOsdSuppression.cs

## Import Cycles
- None detected.

## Communities (60 total, 18 thin omitted)

### Community 0 - "Monitor Brightness Control"
Cohesion: 0.06
Nodes (18): BrightnessHardware, Targets, BrightnessRange, DisplayDevice, MonitorInfo, PhysicalMonitor, Screen, Target (+10 more)

### Community 1 - "Desktop Environment Integration"
Cohesion: 0.08
Nodes (3): GlassDock.Windows.Desktop, WindowsInputHelperRegistration, WindowsSystemTheme

### Community 2 - "Window Overlay Management"
Cohesion: 0.06
Nodes (12): GlassHomeInput, HomeDesktopEnvironment, HomeMonitorArea, DesktopMonitor, Key, WindowsMonitorService, WindowsOverlayManager, CurrentHostWidthDips (+4 more)

### Community 3 - "Bluetooth Device Management"
Cohesion: 0.08
Nodes (7): BluetoothDeviceStore, Snapshot, BluetoothRadioState, BluetoothUtilityService, Devices, State, RadioStateChange

### Community 4 - "Windows Process Management"
Cohesion: 0.15
Nodes (5): BasicLimits, ExtendedLimits, ProcessInformation, StartupInfo, WindowsChildProcessJob

### Community 5 - "Taskbar State Management"
Cohesion: 0.15
Nodes (4): TaskbarAutoHide, Journal, OwnedTaskbar, WindowsTaskbarController

### Community 6 - "Airplane Mode Service"
Cohesion: 0.08
Nodes (11): AirplaneModeResult, AirplaneModeState, AirplaneModeUtilityService, State, CaptureBorderPermission, IsAllowed, IsSupported, NightLightRegistry (+3 more)

### Community 7 - "Master Volume Service"
Cohesion: 0.07
Nodes (10): MasterVolumeChangedEventArgs, External, State, MasterVolumeService, State, MasterVolumeState, Request, NativeVolumeOsdSuppression (+2 more)

### Community 8 - "Night Light Cloud Sync"
Cohesion: 0.17
Nodes (7): Field, NightLightCloudStore, LastTransition, Modified, On, Usable, Reader

### Community 9 - "Do Not Disturb Integration"
Cohesion: 0.13
Nodes (6): DndNative, HasNotifications, IQuietHoursSettings, DndState, DndUtilityService, State

### Community 10 - "Wi-Fi Network Control"
Cohesion: 0.12
Nodes (8): Available, Connection, ConnectionAttributes, NotificationCallback, Session, Handle, Ssid, WifiNetworkControl

### Community 11 - "Desktop Screen Capture"
Cohesion: 0.13
Nodes (3): DesktopCaptureSource, Placement, Rect

### Community 12 - "Native Power Management"
Cohesion: 0.14
Nodes (6): EnergySaverNative, EnergySaverOverride, PowerStatus, EnergySaverState, EnergySaverUtilityService, State

### Community 13 - "Interactive Window Hosting"
Cohesion: 0.12
Nodes (9): InteractiveGlassWindowHost, EnableHostBackdropBrush, HostBackdropAvailable, InputHeightPixels, MinimumHeightDips, MinimumWidthDips, PreserveWindowChrome, UseDockLayeredTransparency (+1 more)

### Community 14 - "SystemControlAudio"
Cohesion: 0.18
Nodes (5): IKsControl, KsProperty, SystemControlAudio, ComLease, Value

### Community 15 - "Raw Input Device Handling"
Cohesion: 0.17
Nodes (4): Device, Header, UtilityKeyInput, UtilityKeyState

### Community 17 - "Input Recovery Bridge"
Cohesion: 0.14
Nodes (4): RecoveryRequest, RestorePending, WindowsInputHelperBridge, WindowsInputHelperProtocol

### Community 18 - "Keyboard Input Messaging"
Cohesion: 0.11
Nodes (6): InputSignal, InputSignalMailbox, WindowsKeyboardService, CaptureBareWindowsKey, IsRegistered, SuppressDockToggle

### Community 19 - "Window Capture Graphics"
Cohesion: 0.06
Nodes (12): MonitorInfo, BitmapInfo, DesktopWindowFocus, MonitorInfo, PhysicalCoordinates, Point, WindowPlacement, DesktopWindowHighlight (+4 more)

### Community 20 - "WifiNetwork"
Cohesion: 0.12
Nodes (11): WifiNetwork, Authentication, CanConnect, Cipher, Key, RequiresPassword, Security, WifiNetworkSelection (+3 more)

### Community 22 - "Screen Capture and Power Monitoring"
Cohesion: 0.14
Nodes (9): ICaptureInterop, EnergySaverStatusSubscription, Initial, Version, PowerCallback, PowerSettingHeader, Subscription, Interface (+1 more)

### Community 23 - "WifiUtilityService"
Cohesion: 0.20
Nodes (5): WifiRadioState, WifiUtilityService, CachedNetworks, HasNetworkSnapshot, State

### Community 26 - "DeviceCallback"
Cohesion: 0.25
Nodes (3): AudioNotificationPropertyKey, DeviceCallback, IDefaultAudioDeviceCallback

### Community 29 - "Native Brightness and Volume OSD"
Cohesion: 0.15
Nodes (6): NativeBrightnessOsdLease, BridgeActive, Enabled, EngineProcessId, Status, SecurityAttributes

### Community 32 - "WindowsKeyHook"
Cohesion: 0.13
Nodes (3): WindowsInputHelperHost, State, WindowsKeyHook

### Community 33 - "TaskbarDevelopmentSession"
Cohesion: 0.22
Nodes (5): TaskbarDevelopmentSession, IsActive, TaskbarRecovery, EventName, LeaseName

### Community 34 - "Task"
Cohesion: 0.23
Nodes (3): NativeOsdBridgeHost, Available, Status

### Community 35 - "Windows Keyboard Gestures"
Cohesion: 0.20
Nodes (5): WindowsKeyGesture, IsAltHeld, IsControlHeld, IsShiftHeld, IsWindowsHeld

### Community 36 - "Application Shutdown State"
Cohesion: 0.29
Nodes (3): ApplicationShutdownState, CancellationToken, IsRequested

### Community 38 - "ERole"
Cohesion: 0.50
Nodes (4): ERole, Communications, Console, Multimedia

### Community 41 - "GlassDock.Core.Desktop"
Cohesion: 0.10
Nodes (7): BluetoothListGeometry, GlassDock.Core.Desktop, DockTransitionGeometry, FullscreenPolicy, IKeyboardService, IsRegistered, WifiScrollGeometry

### Community 42 - "Dock Hover Animation"
Cohesion: 0.07
Nodes (13): DockHoverMagnification, ContentWidth, HasPointer, IsAnimating, Frame, Item, Footprint, Motion (+5 more)

### Community 44 - "Docking State Machine"
Cohesion: 0.11
Nodes (10): DockState, Collapsing, Expanded, Expanding, Hidden, Hovering, Idle, DockStateMachine (+2 more)

### Community 45 - "EDataFlow"
Cohesion: 0.50
Nodes (4): EDataFlow, All, Capture, Render

### Community 46 - "Dock Geometry Rendering"
Cohesion: 0.13
Nodes (8): DockEdgeSlice, DockWaveGeometry, Outline, Point, Segment, UtilityHudBounce, Active, Held

### Community 47 - "Dock Layout and Sizing"
Cohesion: 0.15
Nodes (12): DockSizing, Clearance, ClipDepth, Gap, Height, Radius, ReservedHeight, SurfaceBottom (+4 more)

### Community 48 - "Dock Window Management"
Cohesion: 0.14
Nodes (4): DockRelocationGuard, RetainedWindowSlot, Current, IsShutdown

### Community 49 - "Drag Motion Animation"
Cohesion: 0.24
Nodes (5): DragLensMotion, Bounds, Finished, Opacity, Position

### Community 50 - "Pointer Gesture Handling"
Cohesion: 0.19
Nodes (5): UtilityPointerGesture, IsDragging, PrimaryDown, SecondaryDown, SuppressPrimary

### Community 51 - "Volume HUD State"
Cohesion: 0.17
Nodes (7): VolumeHudPhase, Compact, Expanded, Hidden, VolumeHudState, Interacting, Phase

### Community 52 - "GlassHomeSession"
Cohesion: 0.24
Nodes (8): GlassHomePointer, GlassHomeSession, Revision, State, GlassHomeState, Compact, Expanded, Hidden

### Community 53 - "BluetoothDeviceKind"
Cohesion: 0.22
Nodes (9): BluetoothDeviceInfo, BluetoothDeviceKind, Controller, Generic, Headphones, Headset, Keyboard, Mouse (+1 more)

## Knowledge Gaps
- **133 isolated node(s):** `Targets`, `Display`, `External`, `Displays`, `Status` (+128 more)
  These have ≤1 connection - possible missing edges. (Counts symbols only; 330 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **18 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `GlassDock.Core.Desktop` connect `GlassDock.Core.Desktop` to `Keyboard Input Messaging`, `Keyboard Shortcut Handling`, `Windows Keyboard Gestures`, `Application Shutdown State`, `CaptureDrawGate`, `WifiNetworkCaption.cs`, `Dock Hover Animation`, `DockPerformanceDiagnostics`, `Docking State Machine`, `Dock Geometry Rendering`, `Dock Layout and Sizing`, `Dock Window Management`, `Pointer Gesture Handling`, `Volume HUD State`, `GlassHomeSession`, `BluetoothDeviceKind`, `DockIconMotion`, `ITaskbarController`, `Taskbar Lease Management`, `Input Lifecycle Recovery`, `WifiProfile.cs`, `DesktopPlacement.cs`?**
  _High betweenness centrality (0.256) - this node is a cross-community bridge._
- **What connects `Targets`, `Display`, `External` to the rest of the system?**
  _133 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Monitor Brightness Control` be split into smaller, more focused modules?**
  _Cohesion score 0.05683563748079877 - nodes in this community are weakly interconnected._
- **Why does `GlassDock.Windows.Desktop` connect `Desktop Environment Integration` to `Monitor Brightness Control`, `Bluetooth Device Management`, `Airplane Mode Service`, `Master Volume Service`, `Night Light Cloud Sync`, `Do Not Disturb Integration`, `Native Power Management`, `Window Capture Graphics`, `WifiNetwork`, `WifiUtilityService`?**
  _High betweenness centrality (0.117) - this node is a cross-community bridge._
- **Should `Desktop Environment Integration` be split into smaller, more focused modules?**
  _Cohesion score 0.0773892773892774 - nodes in this community are weakly interconnected._
- **Why does `BluetoothUtilityService` connect `Bluetooth Device Management` to `Monitor Brightness Control`, `TaskbarDevelopmentSession`, `Night Light Cloud Sync`, `Window Capture Graphics`, `Keyboard Shortcut Handling`, `WifiUtilityService`?**
  _High betweenness centrality (0.080) - this node is a cross-community bridge._
- **Should `Window Overlay Management` be split into smaller, more focused modules?**
  _Cohesion score 0.06497175141242938 - nodes in this community are weakly interconnected._
## Task scope and integrity

This graph covers src/GlassDock.Windows/Desktop and src/GlassDock.Core/Desktop only, from before the fullscreen HUD patch. It is a retrieval aid; the changed behavior was verified against source files and automated tests. The post-build health check found 6 self-loops, 0 dangling endpoints and 0 missing endpoints; raw producer edge loss cannot be measured from the merged graph. Some relationships may be incomplete or misleading. See health.json.

Generated graphify-out directories are untracked working-tree artifacts. The initial broad corpus check skipped two utility icon files as sensitive by filename: src/GlassDock.App/Assets/Utilities/Fill/password-fill.svg and src/GlassDock.App/Assets/Utilities/Regular/password.svg. They are outside the selected Desktop scope. Code extraction used no LLM; community labeling cost is recorded above.
