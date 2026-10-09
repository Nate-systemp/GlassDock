# Doky Home dashboard

Upgrades the retained `GlassHomeWindow`; there is no second Hub window. The dashboard opens directly from the existing Home command/Win+Space. The dock's bare-Win behavior is unchanged.

## Ownership and data

- The primary dock retains Home. Secondary dock commands forward to that owner, including All Displays.
- Home builds its card/control tree once. It owns one shared desktop backdrop, a cached search index, one view-model subscriber to the existing application service, and retained theme brushes.
- Settings changes update these brushes, the existing surfaces, and layout. They do not assign another SystemBackdrop or recreate cards. Closing disposes/unsubscribes; hiding retains the window and stops the visible-only status refresh.
- Pins and running apps come from the same canonical dock snapshots, including real stack members. Six retained icon slots per strip page through overflow. Activation uses the existing service and elevated restore helper.
- Search retains the installed-app index, Windows Settings catalog, ranking, keyboard selection, and launcher. File search is not implemented, so the placeholder does not promise it.

## Materials and layout

Seven floating card contours share the dock/utility `DesktopGlassBackdrop` effect graph. Their body mask and optical upper edges use the same continuous geometry. There is no full-window material or shadow host. Each actual card owns its existing GlassSurface shadow.

Explicit Light/Dark select the Home palette; glass modes follow Windows' light/dark preference, including WinUI control resources. Home uses Frosted optics for Light/Dark so its cards remain translucent, without changing the dock's solid finishes. `UISettings.ColorValuesChanged` reapplies that palette to the retained tree while Home is open. Frosted/Acrylic/Clear still retain the dock's optical presets and user blur/opacity/tint/border values; the material selection is independent of the system palette. On user feedback, glass cards add a neutral reading tint derived from the shared solid palette and current opacity. Their shadows are reduced to 30% opacity, 40% blur radius and 25% offset of the dock material to avoid overlapping dark halos. Clear uses the existing inner-edge graph with per-card upper highlights rather than a whole-window specular band.

Layout derives from the selected monitor's work area and DPI, reserving space above the dock. The dashboard has no ScrollViewer. On constrained screens the layout tightens; below 960x620 DIPs the complete arrangement scales to fit. The minimum-size tradeoff is smaller text, not hidden/scrolling cards. Positions align to physical pixels. Search retains its bounded result list and keyboard navigation.

## Supported actions and honest boundaries

- Wi-Fi/Bluetooth: existing permission-aware radio API; read back actual state. Explicit secondary Settings arrows.
- Volume/mute: existing Core Audio service. Brightness: existing WMI provider; unavailable displays show N/A with a disabled control.
- Focus: actual available FocusSession status, Windows Settings for control. Night light: explicit Windows Settings fallback, no claimed toggle state.
- Recent: `No recent items yet`; no private Windows history parsing.
- Media: `Nothing playing`, unavailable/disabled transport; no fabricated track/artwork or new GSMTC provider.
- Profile: current user name and Windows account Settings. Doky Settings opens its existing window, leaving Home available for live appearance checks.
- Lock is explicit. Sleep, shutdown and restart require confirmation with Cancel as the default. No destructive action is executed as part of validation.

## Validation

Debug and Release complete suites are required; see the task report for final totals. Automated coverage includes responsive non-overlapping layouts at 100/125/150/200%, canonical pin/running selection, stack members, shadow/contrast policy, retained subscriptions and shared Home routing, monitor selection, real search wiring and unsupported-control boundaries. Source-wiring tests do not substitute for UI/resource soak tests.

The first runtime build was confirmed by the user to blur correctly and have good Light/Dark appearances. The user requested less-transparent glass and softer shadows; that refinement requires its own final visual check. The helper now initializes, but does not list the collapsed overlay. Final screenshots and repeated live theme switching require a targetable open Home window.

Run:

```powershell
& 'C:\Dev\GlassDock\src\GlassDock.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\GlassDock.App.exe'
Get-Process GlassDock.App | Select-Object Id, Path
```

Manual acceptance: open Home, cycle Dark/Light/Frosted/Acrylic/Clear ten times; change blur/opacity/spacing with Home visible; close/reopen repeatedly; check pins/running activation and search; check All Displays/monitor/DPI placement; confirm normal dock interactions. Check for stale layers, harsh shadows, clipping, invisible input areas and sustained resource growth. Power actions and radio-disconnecting operations are not exercised automatically.

## Changed files

Modified:
- `src/GlassDock.App/Desktop/GlassHomeWindow.cs`: retained dashboard lifecycle, source subscriptions, settings and selected-monitor layout.
- `src/GlassDock.App/Desktop/DesktopOverlayWindow.cs`: supplies existing services and exact-window activation to Home.
- `src/GlassDock.App/Desktop/MonitorDockCoordinator.cs`: secondary docks reuse the primary Home owner.
- `src/GlassDock.App/Desktop/WindowsSystemControlService.cs`: exposes nullable real volume readback, distinguishing unsupported audio from 0%.
- `src/GlassDock.App/Desktop/DockControlPalette.cs`: shared solid surface color accessor.
- `src/GlassDock.App/Desktop/UtilityPopupTheme.cs`: retained reading-surface brush derived from that palette.
- `src/GlassDock.App/Rendering/DesktopGlassBackdrop.cs`: opt-in disjoint card mask/upper-edge geometry; single-surface dock/utility paths retained.
- `src/GlassDock.Core/Materials/UtilityMaterial.cs`: dashboard-only reading contrast and reduced neighboring-card shadow policy.
- `tests/GlassDock.Windows.Tests/UtilityPopupAppearanceWiringTests.cs`: follows shared color accessor while preserving exact solid-color assertions.

Added:
- `src/GlassDock.App/Desktop/GlassHomeWindow.Dashboard.cs`: card layout, theme propagation, app data/activation.
- `src/GlassDock.App/Desktop/GlassHomeWindow.Controls.cs`: existing system-service bindings and intentional power actions.
- `src/GlassDock.App/Desktop/GlassHomeWindow.Search.cs`: extracted existing real search implementation.
- `src/GlassDock.App/Desktop/HomeDashboardCard.cs`: retained card and paged icon-strip controls.
- `src/GlassDock.Core/Desktop/HomeDashboardLayout.cs`: responsive geometry and canonical application projection.
- `src/GlassDock.Windows/Desktop/HomeDesktopEnvironment.cs`: monitor/DPI/work-area and supported system actions.
- `tests/GlassDock.Core.Tests/HomeDashboardTests.cs`: layout, application membership and appearance-policy tests.
- `tests/GlassDock.Windows.Tests/HomeDashboardWiringTests.cs`: lifecycle/source/material/control integration guards.
- `docs/HOME_DASHBOARD.md`: architecture, support boundaries and validation checklist.

## Visual correction pass

The palette previously passed a solid Light/Dark mode into DashboardContrast, making its reading surface fully opaque even with glass selected. It now passes the optical mode independently and uses a restrained reading tint. No duplicate legacy Home tree was found in source. Canvas descendants could paint outside their allocations; card content is bounded separately from its shadow. The minimum design canvas now preserves control widths across DPI, pinned icons are 40 DIPs, running and empty Continue sections are compact, radio tiles share one rounded container, and native volume/brightness sliders use wide tracks. Runtime evidence is required before attributing every reported fragment to these sources.


Latest correction validation: Debug build and full tests pass (355/355); scripts/Validate.ps1 passes Release build and full tests (355/355). Both builds have zero warnings/errors. Debug process 22268 launched and responds. After the user opened Home, both helper inventories still omitted Doky, so screenshot, ten live theme switches, and visual-tree verification remain pending; no visual match is claimed.


## Screenshot follow-up

The supplied current screenshot shows Open/Cancel above Home and clipped background text in the transparent center gutter. The user identified ChatGPT as the window behind Home. Home has no Open/Cancel pair; its only ContentDialog is a user-confirmed power action. Source review found no second Home tree or root fill/shadow. The background text is consistent with uncovered desktop-window pixels, not evidence of obsolete Home controls. A transparent gap exposes whatever window is underneath; it cannot guarantee wallpaper above another app. No external window is hidden or modified.

Removed the previous content-clip workaround. Pinned labels now have two lines, running rows use compact explicit geometry, and the pager owns a separate Grid column. Dark Home adds no charcoal reading wash, card shadows are 15% of the dock shadow opacity, and disabled media/slider thumb brushes use the retained theme palette. One existing backdrop and tree are retained. Exact background text ownership and the latest finish still require a desktop-only visual comparison; screenshot alone cannot name truncated ChatGPT child controls.


Finishing-pass validation: Debug build 0 warnings/errors and 355/355 tests; scripts/Validate.ps1 Release build 0 warnings/errors and 355/355 tests. Latest Debug launched as PID 24816. No automated screenshot is available because the UI helper does not enumerate Home. Desktop-only artifact comparison, ten live theme switches, and visual DPI checks remain pending; no screenshot-level completion is claimed.


## Dark/Light correction (supersedes earlier glass-in-all-modes direction)

Home now passes the selected appearance unchanged to UtilityPopupStyle. Dark and Light have fully opaque shared-palette card fills; Frosted, Acrylic and Clear retain their glass treatment. The root/gaps stay transparent and the same Home instance is updated in place. Removed the explicit Dark/Light-to-Frosted conversion that caused both solid modes to appear glassy.

