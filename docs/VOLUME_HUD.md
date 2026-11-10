# Native volume HUD

The Sound utility uses the existing `UtilityKind.Volume` ID and Phosphor speaker assets. It is now selectable in Add Utilities. A retained floating HUD is available whether Sound is pinned or not; clicking a pinned Sound button explicitly opens its expanded state. Showing, resizing and hiding the HUD do not change dock/utility layout. Existing add/remove ordering and screen-space limits still apply when pinning a utility.

## Architecture and actual audio

`SystemControlAudio.Volume.cs` extends the existing Core Audio interop, retaining one default render/multimedia endpoint and registering endpoint-volume and default-device callbacks. All enumeration, activation, writes and COM cleanup run on `MasterVolumeService`'s dedicated MTA worker. Native callbacks only enqueue work; they do not wait, unregister listeners or release endpoint RCWs. Public COM callback contracts are necessary for CLR-generated CCWs; callback classes have no automatic class interface.

State comes from `GetMasterVolumeLevelScalar` and `GetMute`. No mock level or optimistic volume state is displayed. Device notifications rebuild the endpoint subscription; unavailable output hides the HUD and makes Sound report unavailable. Queued pointer writes are coalesced and associated with the observed endpoint ID, preventing already-rebound outputs from receiving stale drag commands. Native endpoint switching and user input cannot be made transactional; an external switch can race an already-entered audio API call.

Writes use `SetMasterVolumeLevelScalar` with a unique event-context GUID. Dragging above zero also requests unmute, then rereads the actual state. Self-originated notifications synchronize state without reopening/compacting the HUD or issuing another write. External scalar or mute changes show the HUD. Initial enumeration and endpoint rebinding synchronize state without creating a spurious volume-change HUD. The filled amount represents the retained master-volume scalar; mute is identified by the speaker-x glyph and Sound button. No independent per-application or microphone volume controls were added.

The service is process-wide and event-driven even when Sound is not selected. It does not intercept volume keys or modify taskbar/watchdog handling. Optional [native OSD dismissal](VOLUME_OSD_SUPPRESSION.md) observes Shell volume requests and uses the same actual audio state/HUD; it is off by default and still permits an observed brief native flash. With that option off, volume-limit key repeats without actual scalar/mute changes produce no Core Audio change notification and do not extend Doky's idle timer.

## Visual and interaction sequence

The supplied iPhone images guide the silhouette, refined to the requested smaller horizontal dimensions. `VolumeHudWindow` uses a 200×36 DIP expanded capsule and a 200×6 DIP compact capsule within a fixed 240×76 DIP transparent host (pixels at 100% scaling). A single continuous, rounded clip contains an off-white proportional fill and a 16-DIP gray speaker glyph. Compression anchors the capsule's bottom edge and smoothly fades out the glyph; fill animation remains independent. Entrance and exit retain their spring easing, with travel scaled to the smaller capsule and the pivot at its bottom edge. There is no visible percentage, thumb or separate control. A RangeValue automation peer exposes the real master volume to assistive technology without conventional slider chrome.

First actual change after hidden: spring in expanded. A subsequent change in the same burst compresses the same capsule. Further changes animate only fill. The idle timeout is 1.5 seconds, followed by a short retracting spring. Explicit Sound clicks expand the capsule. Pointer hover expands it; pointer capture keeps it expanded and prevents idle hide/compaction while dragging. Release/cancel/lost capture restore the idle behavior. A new change interrupts retraction from its current presentation rather than creating another window or resetting opacity. Reduced-motion settings remove timed transitions.

`MonitorDockCoordinator` owns one HUD across all monitor docks. A new burst chooses a dock on the foreground monitor when available, otherwise the primary dock. An active burst keeps its source; explicit Sound clicks can choose another dock. The source supplies actual dock and visible utility bounds, including expanded utilities, in host-relative DIPs. These are converted to physical screen coordinates using the dock HWND origin and monitor DPI. `VolumeHudPlacement` prefers a 12-DIP gap above pinned Sound, otherwise above the main dock. It tests shifted/flipped alternatives against all visible utility bounds, dock bounds and the monitor work area (excluding reserved taskbar space). When no legal placement exists, it hides rather than overlaps. The expanded envelope determines placement in both phases, so compression does not reposition the window. Transparent animation gutters may extend beyond the work area; the visible capsule stays inside it.

While open, placement is reevaluated on rendering frames for source movement, utility changes and display/DPI changes; the existing native placement cache avoids redundant HWND moves. The HUD does not move or resize the source dock or buttons. Content scales proportionally on undersized work areas; representative high-DPI/tiny-display layouts still need manual validation.

`PopupStyle`, `GlassSurface`, `DesktopGlassBackdrop` and `PopupLiquidGlassSurface` supply the selected appearance, including the dock's Frosted/Acrylic material settings and Clear capture/refraction. Appearance updates do not recreate the HUD. The existing layered, topmost, non-activating window host is reused. Clear mode retains reversible capture exclusion; screenshot handling freezes HUD presentation before the dock becomes screenshot-visible, then re-excludes/resumes in reverse order. Disposal closes only the HUD and unregisters its own audio/capture resources.

## Verification and limits

Incremental audio build and full isolated Debug solution builds succeeded. Final command:

```powershell
dotnet build GlassDock.sln -c Debug --no-restore -p:BaseOutputPath=C:\Dev\GlassDock\artifacts\volume-hud-build\
```

Focused tests cover burst/reopen state transitions, interaction locking, bounded pointer mapping, actual Core Audio callback GUID/CCW visibility, callback exception containment and default-role filtering. The passive monitor test initialized a real Core Audio endpoint and read its state without writing volume. No master-volume setter, mute setter, device switch or audio playback was exercised by the agent. No app/HUD runtime visual comparison or pointer-drag verification has been performed; successful compilation and interop tests do not establish visual fidelity or animation smoothness on the user's GPU.

Refinement verification: **0 build warnings, 0 build errors; 13 Core HUD-state/placement tests and 4 Windows audio/callback tests passed**. Placement coverage includes bottom anchoring, pinned Sound at 100/150/200% DPI, negative monitor coordinates, top-edge alternatives, neighboring utilities, work-area constraints and unavailable space. `git diff --check` passed. All output is isolated under the ignored `artifacts/volume-hud-build/` directory; no running Doky instance was stopped or overwritten.

Manual acceptance: compare the two silhouettes with the references in all appearance modes; test first change, held keys, muted/zero/max volume, repeated changes and reopening mid-retraction; pinned/unpinned Sound; dragging outside and lost capture; actual Windows volume/mute readback; output removal/replacement; multiple monitors, DPI and display edges; screenshot start/cancel and UAC return; reduced motion, unavailable endpoints and shutdown.

## Native Windows volume OSD

The optional suppression backend now consumes volume virtual keys before Shell dispatch and applies the operation through the existing Core Audio worker. The previous post-show dismissal mechanism was removed because it visibly flashed. HUD visuals, pointer interaction, appearance materials and animation state are unchanged. See [the implementation and runtime report](VOLUME_OSD_SUPPRESSION.md) for opt-in behavior, recovery, source references and verification limits. OEM/direct app-command paths that bypass volume virtual keys remain outside its guarantee.

Exit refinement (2026-11-10): normal idle hiding keeps presentation opacity at 1 and animates both scale axes to zero around the existing bottom-center origin. A bounded 6-DIP downward retraction stays inside the existing 12-DIP placement clearance, so the capsule disappears before reaching the dock or pinned Sound button. The same retained window/path serves pinned and unpinned Sound. Entrance opacity/spring, fill animation, bottom-anchored compression and pointer interaction are unchanged; interrupted exits still use the existing revision guard and current animated values.
