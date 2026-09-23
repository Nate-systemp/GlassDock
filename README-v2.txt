GlassDock Utility Interaction Polish v2
========================================

Fix for v1 entrance animation not being visible.

Cause fixed:
- v1 auto-started entrance motion from root.Loaded.
- In WinUI, that can happen before the popup HWND has produced its first visible frame.
- The ~225 ms animation can therefore finish before the user ever sees it.

v2 changes:
- Entrance animation starts explicitly AFTER DesktopOverlayWindow calls Window.Activate().
- Present() waits for XamlRoot + non-zero layout before starting, so it is safe on slower machines/DPI changes.
- Root stays transparent/non-hit-testable until the explicit presentation begins.
- Tray, Quick Settings, and Calendar all use the same explicit Present() path.
- Close animation and outside-click behavior are preserved.
- Glass and content remain transformed together.
- Animation is slightly more visible: stronger initial compression, 235 ms open / 185 ms close.

Important accessibility behavior:
- Windows Settings > Accessibility > Visual effects > Animation effects must be ON.
- GlassDock intentionally respects Windows' animation-disabled preference.

Apply by extracting this ZIP directly over C:\Dev\GlassDock and replacing files.
Then run:
    cd C:\Dev\GlassDock
    dotnet build
    dotnet test

Runtime validation is required after build/tests.
