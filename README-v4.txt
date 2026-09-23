GlassDock Utility Interaction Polish v4

What changed from v3:
- The actual popup AppWindow/HWND rectangle now moves and resizes every animation frame.
- All four window edges converge on the exact clicked utility control.
- Popup content stays arranged at final size and scales with the HWND so it cannot vanish/reflow before the glass rectangle.
- DesktopGlassBackdrop mask is rebuilt for the current animated window size every frame, keeping glass body and inner edge locked to the window.
- Content opacity remains fully visible until the window is nearly icon-sized, eliminating the "content closes first, rectangle follows" effect.
- Open/close use a zero-velocity SmootherStep curve for softer starts/stops.
- Existing popup switching, selected utility state, wave suppression, Escape/outside-click dismissal, multi-monitor positioning and settings inheritance are preserved.

Install:
Extract this ZIP directly over C:\Dev\GlassDock and replace existing files.
Then run:
  cd C:\Dev\GlassDock
  dotnet build
  dotnet test

Runtime validation remains mandatory after build/tests.
