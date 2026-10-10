# Doky — screenshot-compatible Clear mode (experimental)

## Why the previous change caused vertical glass stripes

The Clear shader captures the monitor through Windows Graphics Capture (WGC). Turning off `WDA_EXCLUDEFROMCAPTURE` permanently makes the monitor capture see Doky's own rendered glass and feed it back into itself.

This patch **keeps capture exclusion on during normal Clear operation** and implements a bounded, one-shot Snipping Tool mode:

1. The existing Windows key hook observes **Win+Shift+S** but does not consume the shortcut. The protected elevated helper sends a distinct `SNIP` event via its existing IPC channel (and `SNIPEND` on Escape).
2. On the UI thread, Doky's Clear renderer freezes its **last clean presented GPU frame**, releases its live WGC frame references, and **stops its capture session**.
3. Only after the capture session stops, the dock window's display affinity is temporarily set to `WDA_NONE`, permitting normal screenshots.
4. On a clipboard update, Escape, or the **45-second safety timeout**, Doky sets `WDA_EXCLUDEFROMCAPTURE` **before** recreating and restarting the GPU capture pool. No screenshots or clipboard contents are read or stored by Doky.
5. During the snip, Doky skips fullscreen-policy refresh so the Snipping selection overlay is not mistaken for a fullscreen game. The original taskbar recovery and Win-key handling remain in place.

## Install / validate on Windows

Close Doky completely. Extract the ZIP over `C:\Dev\GlassDock`, preserving folders and overwriting only included files. In PowerShell:

```powershell
cd C:\Dev\GlassDock
dotnet build GlassDock.sln -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
dotnet test GlassDock.sln -c Debug
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
```

**Mandatory for elevated-app Win-key coverage:** the installed protected helper under `C:\Program Files\Doky\InputHelper\` must be updated from the same build as Doky. After building, use the repository's supported `scripts\Install-InputHelper.ps1` procedure **as Administrator**, check its output, then restart Doky. An old running helper does **not** send the new `SNIP` message. Do not copy DLLs directly into Program Files.

Launch the Debug `GlassDock.App.exe` built in `src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\`.

Manual test in **Clear** while dock is expanded:

- Win+Shift+S, capture a region including the dock. It should be visible with no recursive stripes. Normal animated glass should resume afterward.
- Win+Shift+S, then press Escape. The frozen frame should be released immediately; no Windows Start menu.
- Repeat 10 times; test at 100% and 150% DPI, with Snipping from a non-elevated and elevated foreground window.
- Repeat with Dark, Light, Frosted and Acrylic modes. Their materials should not change.
- Confirm bare Win, Win+L, Win+E and Win+Shift+S retain their original functions before and after sleep/wake.
- Test two monitors if using All Displays mode; verify each dock is screenshottable and returns to normal rendering.

## Limitations and manual acceptance

- This implements Win+Shift+S only; **arbitrary third-party screen recordings/capture utilities are not covered** by the temporary snapshot mode.
- The frozen frame does not animate during the Snipping selection. That is intentional: drawing a new WGC frame while temporarily screenshot-visible would bring back the capture recursion.
- If Snipping Tool does not update the clipboard (for example an abandoned selection), normal capture automatically resumes after at most 45 seconds. Another application changing the clipboard while the selection is open may resume the glass early.
- Independent Clear utility popups may still be excluded from screenshots; this patch targets the main dock(s).
- Windows GPU/WinUI behavior and capture permissions vary; the feature is **not verified on physical Windows hardware** in this environment. Revert the included files if build or screenshot validation fails.
