GlassDock — smooth hover-wave edge + Hover Wave setting

What this patch changes
- Removes the old visible XAML gradient/rim from the dock wave. The XAML path remains
  geometry-only for native hit-testing; DesktopGlassBackdrop is now the only visible edge.
- Keeps the existing 2x compositor mask supersampling/linear downsampling path.
- Adds persisted Dock Settings option: "Hover wave effect" (default ON).
- Turning the wave OFF clears any active deformation immediately but leaves icon
  magnification, input/hit-testing, auto-hide, drag/reorder and popup behavior intact.
- Turning it ON again waits for the next real pointer move before creating a wave.
- Existing open utility popups continue to inherit appearance through Astra's
  UtilityPopupStyle / ApplyAppearance architecture.

Install
1. Extract this zip over C:\Dev\GlassDock
2. Run:
   dotnet build
   dotnet test
3. Launch GlassDock and verify the runtime checklist below.

Mandatory runtime verification
- Windows taskbar does not reveal on bottom-edge hover.
- Bare Win still controls GlassDock with Glass Home open; Windows Start does not steal it.
- Win+Space still toggles Glass Home.
- Win+R, Win+E, Win+L, Win+D and Win+Tab still pass through.
- Dock remains topmost over normal application windows.
- Maximum-width pinned-app layout has no overflow/clipping/compression.
- Wave ON: hover deformation is smooth and has no visible XAML/gradient stair-step rim.
- Wave OFF: magnification, hover, hit testing, drag/reorder and auto-hide still work.
- Toggle OFF while a wave is visible: deformation disappears immediately with no stale rim.
- Toggle ON again: the next pointer move recreates the wave normally.
- Hidden Tray, Quick Settings and Calendar still open/close and remain appearance-synced.
