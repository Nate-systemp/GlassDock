# Main dock materials and Clear rim

Settings already selects absolute editable preset values. Apply them once: the
discarded relative-to-Frosted adjustment double-scaled Acrylic/Clear values.
Main-dock mode now reaches the native renderer explicitly. Its base branch uses
full diffusion for Frosted, at most the existing Clear preset's four-DIP diffusion
for Acrylic, and sharp backdrop for Clear. The processed branch keeps the selected
blur, opacity, saturation, brightness and tint. Popup/laboratory graphs are unchanged.

ClearDockSpecular owns a separate stroke capture, directional light and composite.
The stroke borrows the same CompositionPathGeometry as the body mask. Only coverage
is supersampled; the light gradient is evaluated in final window pixels. Smooth
top-to-bottom falloff reaches zero before the bottom. Border opacity/thickness zero
disable the catch. The outer body mask applies presentation opacity once.
Switching away restores the normal edge brush and hides the cached Clear layer;
disconnect disposes its resources. No extra timer or independent wave is used.

The existing Frosted/Acrylic partial catches previously used straight resting-edge
paths, which could appear inside a raised wave. Their spans and brightness are
preserved, but DockEdgeSlice now extracts exact cubic subcurves from the body outline.
The bottom partial catch is unchanged for those modes; Clear never uses that layer.

Regression tests cover preset inputs, distinct base diffusion, non-dock preservation,
Clear fade/zero border opacity, and partial rims at left/center/right wave positions.
Live mode switching and rapid wave motion remain visual checks; test results alone
do not establish visual parity with the reference.

## Active-source audit and runtime correction (2026-09-30)

MSBuild's evaluated Compile/Page/ApplicationDefinition items contain one
DesktopOverlayWindow, one DesktopGlassBackdrop and one SettingsWindow code-behind.
The App resource dictionary merges XamlControlsResources once. SDK default source
items exclude bin/obj; no linked renderer or custom wildcard pulls published or
installer payloads into the app. The only explicit linked C# files are
OptionalComposition and ManualUpdateService in the Windows test project.

Inactive backups were identified and retained (not compiled):
- src/GlassDock.App/Desktop/DesktopOverlayWindow.cs.20260922-153833.bak
- src/GlassDock.Windows/Desktop/WindowsTaskbarController.cs.20260922-153833.bak
- src/GlassDock.App/Desktop/SettingsWindow.xaml.pre-wave-toggle.bak
- src/GlassDock.App/Desktop/SettingsWindow.xaml.cs.pre-wave-toggle.bak

GlassCompositionBrush is the laboratory/in-app adapter, not a second registered
desktop renderer. GlassMaterialMode remains the utility/internal material enum;
DockAppearanceMode owns the main dock, including solid Light/Dark modes.

The live startup log exposed E_INVALIDARG: unsupported source brush type in
ClearDockSpecular. A CompositionMaskBrush cannot feed the effect input directly.
The masked light now renders into a CompositionVisualSurface and supplies a
supported surface brush to the composite. Its resources are retained and disposed
with the backdrop, not allocated per wave frame. Native fallback failures now log
the stage and exception rather than silently reporting only to Debug output.

Removed the Advanced Material selector, description, selected-value property and
change handler. Appearance alone selects the main-dock preset. Legacy
GlassMaterialMode remains loadable for existing utility settings; solid-mode edits
preserve it. Unknown Material JSON is safely ignored by the existing store.
No backend properties or user files were deleted/reset.

Runtime: corrected Clear reports Native system backdrop, and the captured dock
shows wallpaper through the body with a top highlight. The user confirmed the
Acrylic/Frosted/Clear/Acrylic Apply sequence looks distinct, no detached hover line,
and no Advanced Material selector. Restart persistence and conflicting legacy
material are covered by settings-store tests; exhaustive per-icon/mixed-DPI visual
coverage is still manual.
