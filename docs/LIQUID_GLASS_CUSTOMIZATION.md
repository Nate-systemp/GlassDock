# Liquid Glass visual customization

Appearance provides Basic and Advanced controls, a fixed live preview, per-section reset, and a full visual reset. Changes remain in the preview until **Apply changes**. Other settings retain their existing save behavior and do not apply pending glass edits.

The preview uses the same compiled LiquidGlass shader, shader-parameter writer, rim renderer, and native material effect graph as the dock. Its sample background makes distortion and diffusion visible without creating a screen capture session. Application buttons use the existing native GlassSurface. Preview changes do not move or animate the real dock.

## Controls

| Control | Renderer effect |
| --- | --- |
| Light Angle | Direction of opposing specular catches |
| Light Intensity | Specular and inner-light brightness; consolidated with highlight opacity |
| Refraction | Edge displacement in DIP |
| Depth | Extent of the curved edge profile |
| Dispersion | RGB separation distance; consolidated with dispersion radius |
| Frost | Local diffusion radius; consolidated with roughness |
| Splay | Redistribution of the optical field into the interior, independent of depth and falloff |
| Surface opacity / transparency | Alpha of the surface, without fading app icons |
| Material opacity | Blend of processed and original backdrop |
| Blur strength / quality | Gaussian blur and speed/balanced/quality optimization |
| Saturation / brightness / contrast | Background color processing, without configurable colors |
| Refraction scale | Interior magnification |
| Edge focus / distortion falloff / softness | Location, concentration and profile of displacement |
| Highlight thickness / softness / spread / position | Width and distribution of rim catches |
| Inner / outer glints, glint falloff | Broad inner catch and fine perimeter light |
| Reflection intensity / softness | Interior reflection band |
| Inner shine / glow | Soft internal light |
| Edge opacity / thickness | Continuous edge definition |
| Bevel | Curved edge shading |
| Corner radius | Visual contour only; native input regions and dock placement are unchanged |
| Top / bottom intensity | Opposing edge lighting |

Clear supports all optical controls. Frosted and Acrylic retain the native compositor pipeline and expose the supported surface and rim controls. Dark and Light retain their solid preset colors and supported opacity, rounding and rim controls. Unsupported controls are disabled rather than presenting controls with no renderer effect. Reflection softness requires nonzero reflection intensity; edge thickness requires nonzero edge opacity; blur quality matters when blur is enabled.

## Presets and components

Built-in Dark, Light, Frosted, Acrylic and Clear presets remain separate from the custom library. Save as new, rename, duplicate, select and delete operate only on custom presets. Apply persists the preview and library together through the existing atomic settings store. Closing without Apply leaves the saved profile intact.

Main material is the inheritance source. Dock surface can override it. Individual application buttons can override native surface opacity, material blend, blur/quality, saturation, brightness, contrast, edge definition and rounding. Button refraction remains supplied by the dock beneath; no additional capture is created. Running/active indicators support opacity, edge definition and rounding. Removing an override restores inheritance. Component selection brings the selected application into the representative preview.

Old settings retain their existing refraction, light angle, material mode, colors and behavior. Missing customization data leaves the old rendering path active. Values are finite and bounded before reaching rendering APIs. Preset names and library sizes are bounded; custom presets do not write built-in definitions.

No shadow/color pickers, interaction settings, positioning changes or animation customization are added.

## Verification

- Compile shader: `scripts/Build-LiquidShader.ps1`.
- Build solution: `dotnet build GlassDock.sln -c Debug`.
- Core/Windows tests cover independent parameters, normalization, inheritance, legacy settings and restart persistence.
- `dotnet run --project tests/GlassDock.Rendering.Probe -c Debug` renders the actual shared shader and rim. Each of the 34 exposed properties must change GPU-rendered pixels; the three native material graphs must also render. This probe does not open windows, capture the screen or touch user settings.
- Live Settings smoke check observed the Clear preview, Basic/Advanced selection, fixed preview while scrolling, and Frost updating the preview without Apply.

Cross-GPU/HDR performance, mixed-DPI visual comparison, and a complete manual preset/component lifecycle remain outside this smoke check.
