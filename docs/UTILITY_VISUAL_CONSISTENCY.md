# Dock and utility visual consistency

Calendar, Quick Settings and Hidden Tray use the main dock's `DesktopGlassBackdrop`
and `DockMaterialStylePresets`. `UtilityMaterial.CreateForPopup` applies the same
live blur, opacity and border settings without popup-specific blur floors or a
second colored overlay. Panel geometry remains rounded with a 28 DIP radius;
the dock retains its pill/wave geometry. Material shadows come from the shared preset.

`DockControlPalette` supplies the dock's foreground and selected/hover/pressed
colors to both the dock and the retained utility brushes. Utility controls use
`UtilityPopupTheme.StyleButton` rather than inherited unrelated button states.
Functional layouts and typography hierarchy remain specific to each utility.

## Backdrop connection order

All three utility constructors must assign `Content`, configure their native
desktop client, and only then assign `SystemBackdrop`. The custom backdrop's
connection callback requires `XamlRoot.Content` to schedule its loaded renderer.
Attaching before content can return without creating any native glass, leaving
only XAML controls and transparent background. Increasing blur cannot fix that.
The connection is retained across appearance changes and popup animations.

Regression coverage checks attachment order, shared palette/material routing,
user blur values (including zero), border values and preset shadow inheritance.
Runtime visual verification must still check actual blur over text in all three
utilities, live Frosted/Acrylic/Clear switching, and unified open/close animation.

## Validation (2026-10-01)

Debug and Release builds passed with zero warnings/errors. Validate.ps1 passed:
153 Core and 88 Windows tests. The first runtime pass still had no blur; moving
the backdrop attachment after Content/native setup produced utility connection
and initialization entries in startup.log. The user then confirmed actual blur
in Calendar, Quick Settings and Hidden Tray in Frosted/Acrylic. Clear-mode
switching and a detailed animation comparison remain unverified in this pass.
