# Doky product specification
This document describes the long-term product. Implemented scope is limited to the Phase 0 foundation, Phase 1 material laboratory, and separately authorized Phase 2–3 desktop dock foundation. See ROADMAP.md for current gates; requirements below do not authorize future features.

## 1. Product overview
Doky — A cleaner way to use Windows. A premium Windows interaction layer, intended for commercial distribution.
## 2. Target user experience
A calm, minimal and responsive desktop with a small dock and full-screen launcher.
## 3. Doky philosophy
Feel spatial, elegant and distinctly Doky while respecting Windows conventions. Visual quality, accessibility and reliability are product requirements.
## 4. Dock behavior
Eventually expand smoothly on proximity, support pinned and running apps, and minimize when appropriate. Taskbar replacement requires proven recovery.
## 5. Home indicator
A tiny iOS-style bottom-center indicator will provide a discoverable dock entry point.
## 6. Glass Home
A full-screen launcher, eventually invoked by the bare Windows key, preferably on the active monitor.
## 7. Application launcher
Installed, pinned, recent and system apps; Settings, Control Panel, File Explorer, Terminal and appropriate utilities. Discovery and launching start in Phase 5.
## 8. Search
The separately authorized Glass Home search follow-up searches cached installed applications and a Windows Settings catalog. Matching ranks exact, starts-with, word-prefix, then substring matches. Compact search results and keyboard navigation are independent of the existing mouse-driven Expanded Home state. Pinned apps, recent files, power actions, Task View, and web search are outside this follow-up.
## 9. Running applications
Visual running/active indicators and window activation behavior, without unsafe shell interference.
## 10. System status
Time, battery, volume, Wi-Fi, Bluetooth and useful status, with graceful behavior when hardware is unavailable.
## 11. Control Center
Future quick controls; capabilities and permissions will be defined in Phase 8.
## 12. Multi-monitor support
Monitor-aware placement, active-monitor launcher behavior and correct per-monitor DPI.
## 13. Customization
Themes, accents, glass styles, dock layout and animation preferences; a theme marketplace is only a future possibility.
## 14. Profiles
Potential Work, Gaming, Creative, Focus and minimal modes. Scope remains uncommitted.
## 15. Future commercial Free/Pro model
Feature tiers, activation, caching and offline grace are future decisions. A website may eventually provide accounts, checkout, downloads and documentation. No commercial services in Phase 0.
## 16. Reliability requirements
Safe startup/shutdown, crash detection, emergency recovery and bounded restart policies. Unexpected termination must restore the normal taskbar whenever it has been hidden. Never create an unrecoverable shell state.
## 17. Accessibility considerations
Keyboard navigation, screen-reader semantics, contrast, text scaling, reduced motion and non-hover alternatives must accompany future visuals.
## 18. Performance goals
Low input latency, smooth GPU animations, low idle CPU and memory, no unnecessary background polling. Establish measurable budgets with representative hardware in later phases.
## 19. Security considerations
Least privilege; no Explorer termination/modification or system-file changes. Preserve Windows shortcuts and security mechanisms. No secrets in client binaries; client license state is not authoritative. Network and telemetry require explicit scope.
