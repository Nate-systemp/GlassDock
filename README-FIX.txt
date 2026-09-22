GlassDock — Responsive Dock Width Fix

What this fixes
- Pinned apps no longer overflow the glass surface when the dock gets crowded.
- Removes the old 560-DIP app-section sizing cap from the combined app + utility dock calculation.
- Makes the invisible overlay host responsive to the current monitor width instead of hard-capping it at 960 DIP.
- Keeps the utility/system cluster at its normal size and spacing.
- Preserves existing icon sizes, drag/reorder slot math, magnification, multi-monitor movement, DPI behavior, topmost behavior, Win-key behavior and taskbar watchdog logic.

Files
- src/GlassDock.App/Desktop/DesktopOverlayWindow.cs
- src/GlassDock.Windows/Desktop/WindowsOverlayManager.cs

Mandatory regression checks after launch
1. Pin/unpin enough apps to reproduce the previous crowded state. No icon, divider, utility control or clock may draw outside the glass.
2. Windows taskbar must NOT reveal when the pointer reaches the bottom edge.
3. With Glass Home open, bare Win must control GlassDock and must NOT open Windows Start. Normal Win+ shortcuts must still work.
4. Dock must remain topmost/in front of normal windows, including when launched from Visual Studio.
5. Drag reorder/drop, magnification, multi-monitor movement and DPI scaling must still behave normally.
