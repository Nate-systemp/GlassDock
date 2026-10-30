# Shared Clear popup rendering

Dock app menus, stacks, and window previews use PopupLiquidGlassSurface in Clear mode. Home and Settings use their existing desktop backdrops. PopupLiquidGlassSurface binds the retained GPU renderer to each popup's visibility, geometry, and appearance; the native backdrop supplies the same outline for clipping.

Clear uses the dock's shader, dispersion, diffusion, and rim highlights. The popup source is cropped to its bounds plus a sampling halo before blur and exposure. Content, controls, and native thumbnails remain above the glass and are never shader inputs. Hiding or changing away from Clear stops capture and restores the native material; a successful GPU draw alone suppresses the native body.

The same PopupStyle, PopupTheme, and PopupMaterial are shared by these windows. App menus and stacks retain their presentation animations; preview paging reattaches one retained glass layer. Capture/device failures leave the native fallback usable. Each visible Clear popup has its own capture pool, so sustained GPU and memory behavior requires live measurement.

The source and test suite verify wiring and lifecycle. Visual quality, multi-monitor placement, and repeated open/close behavior require live desktop validation.
