using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ControlCenterDesignWiringTests
{
    private static string Read(string file)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src/GlassDock.App/Desktop", file));
    }

    [Fact]
    public void Shared_design_reuses_native_slider_and_media_actions()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("home.Parent.Children.Remove(slider)", source);
        Assert.Contains("track.Children.Add(slider)", source);
        Assert.Contains("slider.Orientation = Orientation.Horizontal", source);
        Assert.Contains("resources[\"SliderTrackValueFill\"] = depth.Fill", source);
        Assert.Contains("slider.Opacity = 1", source);
        Assert.Contains("pair.Key.ValueChanged -= handler", source);
        Assert.Contains("await media.ExecuteAsync(action)", source);
        Assert.Contains("menuButton.Flyout = oldMenuButton.Flyout", source);
        Assert.Contains("quickSettings.SetDetachedLayout(true)", Read("DesktopOverlayWindow.cs"));
    }

    [Fact]
    public void Radio_refresh_does_not_replace_shared_glass_card_materials()
    {
        var source = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("if (!detached)\n            {\n                networkFrame.Background", source.Replace("\r\n", "\n"));
        var shared = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("resources[\"SliderOuterThumbBackground\"] = theme.Primary", shared);
        Assert.Contains("resources[\"SliderHorizontalHeight\"] = 26d", shared);
        Assert.Contains("resources[\"SliderPreContentMargin\"] = 11.5d", shared);
        Assert.Contains("resources[\"SliderPostContentMargin\"] = 11.5d", shared);
        Assert.Contains("var resources = new ResourceDictionary()", shared);
        Assert.Contains("slider.Resources = resources", shared);
        Assert.Contains("pair.Key.Resources = sliderResourceHomes[pair.Key]", shared);
        Assert.DoesNotContain("slider.Resources[", shared);
    }

    [Fact]
    public void Depth_feedback_keeps_native_input_and_reduced_motion_contracts()
    {
        var depth = Read("ControlCenterDepth.cs");
        Assert.Contains("AnimationsEnabled", depth);
        Assert.Contains("PointerCanceled", depth);
        Assert.Contains("PointerCaptureLost", depth);
        Assert.DoesNotContain("CapturePointer", depth);
        Assert.DoesNotContain("Handled = true", depth);
        Assert.DoesNotContain("CreateTimer", depth);
        Assert.DoesNotContain("DesktopCaptureSource", depth);
        Assert.DoesNotContain("ThemeShadow", depth);
        var shared = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("depth.Apply(mode)", shared);
        Assert.Contains("depth.Raise(frame, interactive: true)", shared);
        Assert.Contains("track.Children.Add(slider)", shared);
    }

    [Fact]
    public void Control_center_palette_stays_local_and_tracks_selected_appearance()
    {
        var source = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("theme.Apply(mode)", source);
        Assert.Contains("ApplyControlCenterPalette(mode)", source);
        Assert.Contains("UtilityPopupStyle.ApplyDockMatched(glass, backdrop, appearance, mode, 26)", source);
        Assert.DoesNotContain("ApplyControlCenterPalette", Read("CalendarPopoverWindow.cs"));
    }

    [Fact]
    public void Inactive_materials_are_neutral_and_only_confirmed_radios_activate_cards()
    {
        var depth = Read("ControlCenterDepth.cs");
        Assert.DoesNotContain("Set(Card, Color.FromArgb", depth);
        Assert.DoesNotContain("Set(Icon, Color.FromArgb", depth);
        Assert.Contains("private Brush Resting(Border card)", depth);
        Assert.Contains("card.Background = Resting(card)", depth);
        var shared = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("depth.SetActive(networkFrame, active)", shared);
        Assert.Contains("depth.SetActive(bluetoothFrame, active)", shared);
        Assert.DoesNotContain("depth.Amber", Read("SystemQuickSettingsWindow.Detached.cs"));
    }

    [Fact]
    public void Control_lenses_share_capture_and_release_frame_references()
    {
        var renderer = Read("../Rendering/DokyLiquidGlassSurface.cs");
        var start = renderer.IndexOf("private void DrawControlGlass", StringComparison.Ordinal);
        var end = renderer.IndexOf("private void DrawLens", start, StringComparison.Ordinal);
        var controls = renderer[start..end];
        Assert.Contains("effect.Source1 = controlBlur", controls);
        Assert.Contains("ControlMaterial.RefractionStrength", controls);
        Assert.Contains("controlRim.Draw", controls);
        Assert.Contains("(UseMaterialLighting ? ControlCenterControlBlur : 8) * scale", controls);
        Assert.Contains("if (!UseMaterialLighting)", controls);
        Assert.Contains("UseMaterialLighting ? ControlCenterPanelBlur", renderer);
        Assert.Contains("finally { effect.Source1 = null; }", controls);
        Assert.Contains("if (UseMaterialLighting && popupTarget is not null)", renderer);
        Assert.Contains("liquid.UseMaterialLighting = true", Read("SystemQuickSettingsWindow.cs"));
        Assert.DoesNotContain("UseMaterialLighting = true", Read("DesktopOverlayWindow.cs"));
        Assert.DoesNotContain("UseMaterialLighting = true", Read("FloatingUtilitiesWindow.cs"));
        Assert.Contains("effect.Source1 = null", controls);
        Assert.Contains("CanvasGeometry.CreateRoundedRectangle", controls);
        Assert.Contains("regionOrigin != origin", controls);
        Assert.DoesNotContain("Lighting = Vector4.Zero", controls);
        Assert.DoesNotContain("Lens = Vector4.Zero", controls);
        Assert.DoesNotContain("DesktopCaptureSource", controls);
        Assert.DoesNotContain("CreateTimer", controls);
        Assert.Contains("region.Shader.Dispose(); region.Clip.Dispose()", renderer);
        var adapter = Read("../Rendering/PopupLiquidGlassSurface.cs");
        Assert.Contains("element.Unloaded -= ControlLoaded", adapter);
        Assert.Contains("element.SizeChanged -= ControlSizeChanged", adapter);
        Assert.Contains("TransformToVisual(root)", adapter);
        Assert.Contains("DispatcherQueuePriority.Low", adapter);
        Assert.Contains("root.RemoveHandler(UIElement.PointerMovedEvent, pointerMoved)", adapter);
        Assert.Contains("root.PointerExited -= PointerExited", adapter);
    }

    [Fact]
    public void Layout_one_preserves_plain_battery_footer_and_media_size_hierarchy()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("Text = \"Control Center\"", source);
        Assert.Contains("Shortcut(\"airplane\"", source);
        Assert.Contains("Shortcut(\"hotspot\"", source);
        Assert.Contains("Shortcut(\"battery-saver\"", source);
        Assert.DoesNotContain("Text = \"Settings\"", source);
        Assert.Contains("title + \" details\"", source); // Keep the action's accessible name.
        Assert.Contains("if (name == \"night-light\") v.Detail.Visibility = Visibility.Collapsed", source);
        Assert.Contains("v.Button.Content = icons.Create(name, 23)", source);
        Assert.Contains("Spacing = name is \"wifi\" or \"bluetooth\" or \"focus\" ? 0 : 2", source);
        Assert.Contains("Grid.SetRow(footerRow, 6); panel.Children.Add(footerRow)", source);
        Assert.DoesNotContain("Surface(footerRow)", source);
        Assert.Contains("Action(\"previous\", \"Previous track\", -1, 14)", source);
        Assert.Contains("Action(\"play\", \"Play or pause\", 0, 20)", source);
        Assert.Contains("Action(\"next\", \"Next track\", 1, 14)", source);
        Assert.Contains("Segoe UI Variable", source);
        Assert.Contains("StretchDirection.DownOnly", source);
        Assert.Contains("snapshot.HasBattery ? Visibility.Visible : Visibility.Collapsed", Read("SystemQuickSettingsWindow.cs"));
    }

    [Fact]
    public void Supported_shortcut_states_refresh_on_reopen_without_inventing_airplane_or_night_light_state()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("\"battery-saver\" => shortcutStatus.EnergySaver", source);
        Assert.Contains("\"hotspot\" => shortcutStatus.Hotspot", source);
        Assert.Contains("\"airplane\" => windowsControlState.Airplane", source);
        Assert.Contains("var active = state == true", source);
        Assert.Contains("Status unavailable", source);
        Assert.Contains("Show control availability", source);
        var window = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("var shortcutState = await SystemQuickControlStatus.ReadAsync()", window);
        Assert.Contains("Refresh(); _ = RefreshHardwareAsync();", window);
        Assert.Contains("WindowActivationState.Deactivated", window);
        Assert.Contains("Do Not Disturb is a separate Windows setting", source);
        var commands = Read("SystemQuickSettingsWindow.WindowsControls.cs");
        Assert.Contains("await controls.QuickControls.ToggleAsync(kind, lifetime.Token)", commands);
        Assert.Contains("if (!result.Succeeded)", commands);
        Assert.Contains("Focus = result.State", commands);
        Assert.Contains("NightLight = result.State", commands);
        Assert.Contains("Airplane = result.State", commands);
        Assert.Contains("controlRevision == windowsControlRevision", window);
    }

    [Fact]
    public void Active_artwork_is_used_in_every_appearance_and_control_center_border_is_thinner()
    {
        var icons = Read("ControlCenterIcons.cs");
        Assert.Contains("disabled ? \"-disabled\" : active ? \"-active\" : \"\"", icons);
        Assert.DoesNotContain("active && mode == DockAppearanceMode.Clear ?", icons);
        var window = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("BorderThickness = new Thickness(.6)", window);
        Assert.Contains("liquid.OuterRimStrokeScale = .8f", window);
        Assert.Contains("public float PopupRimStrokeScale { get; set; } = 1", Read("../Rendering/DokyLiquidGlassSurface.cs"));
    }

    [Fact]
    public void Primary_control_clicks_stay_in_panel_and_hotspot_uses_real_service()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("await hotspot.ToggleAsync(lifetime.Token)", source);
        Assert.Contains("if (closed || hotspotBusy) return", source);
        Assert.Contains("await RefreshHardwareAsync()", source);
        Assert.Contains("ShowInformation(\"Hotspot\", result.Message", source);
        Assert.DoesNotContain("controls.OpenFocusSettings();", source);
        Assert.DoesNotContain("controls.OpenNightLightSettings();", source);
        Assert.DoesNotContain("\"Hotspot\", controls.OpenHotspotSettings", source);
        Assert.DoesNotContain("\"Saver\", controls.OpenBatterySettings", source);
        Assert.Contains("\"hotspot\" ? \"Toggle Hotspot\"", source);
    }

    [Fact]
    public void Compact_slider_rows_fit_the_native_thumb_and_share_symmetric_insets()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        Assert.Contains("new[] { 22d, 108d, 40d, 80d, 48d, 48d, 24d }", source);
        Assert.Contains("panel.RowSpacing = 12", source);
        Assert.Contains("card.Padding = new Thickness(15, 3, 15, 3)", source);
        Assert.Contains("card.CornerRadius = new CornerRadius(24)", source);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Right", source);
        Assert.Contains("resources[\"SliderTrackThemeHeight\"] = 3d", source);
        Assert.Contains("slider.CornerRadius = new CornerRadius(1.5)", source);
        Assert.Contains("resources[\"SliderHorizontalThumbHeight\"] = 14d", source);
        Assert.DoesNotContain("card.Margin = new Thickness(0, 2, 0, -2)", source);
        double Resource(string key) => double.Parse(System.Text.RegularExpressions.Regex.Match(source,
            "resources\\[\"" + key + "\"\\] = ([0-9.]+)d").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var trackHeight = Resource("SliderPreContentMargin") + Resource("SliderTrackThemeHeight") + Resource("SliderPostContentMargin");
        Assert.Equal(Resource("SliderHorizontalHeight"), trackHeight);
        Assert.True(Resource("SliderHorizontalThumbHeight") <= trackHeight);
        var rows = System.Text.RegularExpressions.Regex.Match(source, @"new\[\] \{ ([0-9d, ]+) \}").Groups[1].Value
            .Split(',').Select(s => double.Parse(s.Trim().TrimEnd('d'), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(rows[4], rows[5]);
        Assert.True(trackHeight + 6 <= rows[4]);
        var gap = double.Parse(System.Text.RegularExpressions.Regex.Match(source, @"panel.RowSpacing = ([0-9]+)").Groups[1].Value);
        var height = double.Parse(System.Text.RegularExpressions.Regex.Match(source, @"panel.Height = ([0-9]+)").Groups[1].Value);
        Assert.Equal(height, rows.Sum() + (rows.Length - 1) * gap);
    }

    [Fact]
    public void Slider_pills_have_a_centered_icon_track_and_percentage_without_visible_titles()
    {
        var source = Read("SystemQuickSettingsWindow.Detached.cs");
        var start = source.IndexOf("private void AddHorizontalSlider", StringComparison.Ordinal);
        var end = source.IndexOf("private Border BuildMediaCard", start, StringComparison.Ordinal);
        var slider = source[start..end];
        Assert.Contains("var card = Surface(track)", slider);
        Assert.Contains("ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center", slider);
        Assert.Contains("icons.Create(glyph, 16)", slider);
        Assert.Contains("Grid.SetColumn(end, 2)", slider);
        Assert.DoesNotContain("Text = label", slider);
        Assert.DoesNotContain("content.Children.Add", slider);
        // Removing the visible title must not remove the native accessible name.
        var window = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("StyleSlider(volumeSlider, \"System volume\")", window);
        Assert.Contains("AutomationProperties.SetName(slider, name)", window);
    }

    [Fact]
    public void Returning_from_details_restores_card_state_then_arranges_before_collecting_glass()
    {
        var source = Read("SystemQuickSettingsWindow.cs");
        Assert.Contains("\"Back to controls\", ReturnToControls", source);
        var start = source.IndexOf("private void ReturnToControls()", StringComparison.Ordinal);
        var end = source.IndexOf("private int BeginDetails", start, StringComparison.Ordinal);
        var navigation = source[start..end];
        Assert.Contains("detailRevision++", navigation);
        Assert.Contains("detailLifetime?.Cancel()", navigation);
        Assert.Contains("depth.ResetInteraction()", navigation);
        var show = navigation.IndexOf("panel.Visibility = Visibility.Visible", StringComparison.Ordinal);
        var layout = navigation.IndexOf("root.UpdateLayout()", StringComparison.Ordinal);
        var refresh = navigation.IndexOf("liquid.RefreshControls()", StringComparison.Ordinal);
        Assert.True(show >= 0 && layout > show && refresh > layout);
        var depth = Read("ControlCenterDepth.cs");
        var resetStart = depth.IndexOf("public void ResetInteraction()", StringComparison.Ordinal);
        var resetEnd = depth.IndexOf("public void Raise(", resetStart, StringComparison.Ordinal);
        var reset = depth[resetStart..resetEnd];
        Assert.Contains("card.Background = Resting(card)", reset);
        // The input plane must remain the same before and after navigation.
        // Lighting brushes provide depth without translating interactive cards.
        Assert.DoesNotContain("card.Translation", depth);
        Assert.DoesNotContain("StartAnimation(\"Translation\")", depth);
        Assert.DoesNotContain("SetIsTranslationEnabled", depth);
        Assert.Contains("card.PointerEntered", depth);
        Assert.Contains("TimeSpan.FromMilliseconds(180)", depth);
        Assert.DoesNotContain("activeCards.Clear", reset);
        Assert.DoesNotContain("ApplyAppearance", navigation);
    }

    [Fact]
    public void Svg_cache_includes_physical_size_and_reacts_to_dpi_without_hover_reloads()
    {
        var source = Read("ControlCenterIcons.cs");
        Assert.Contains("bool Light, int Pixels", source);
        Assert.Contains("image.XamlRoot?.RasterizationScale", source);
        Assert.Contains("RasterizePixelWidth = pixels, RasterizePixelHeight = pixels", source);
        Assert.Contains("root.Changed += DpiChanged", source);
        Assert.Contains("root.Changed -= DpiChanged", source);
        Assert.Contains("image.SizeChanged", source);
        Assert.Contains("Equals(image.Tag, cacheKey)", source);
        Assert.DoesNotContain("PointerEntered", source);
        var renderer = Read("../Rendering/DokyLiquidGlassSurface.cs");
        Assert.Contains("Source = popupCrop, BorderMode = EffectBorderMode.Hard, CacheOutput = true", renderer);
        Assert.Contains("controlBlur?.Dispose()", renderer);
    }

    [Fact]
    public void Supplied_vector_variants_have_valid_geometry_and_preserve_the_users_colors()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "GlassDock.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var assets = Path.Combine(root.FullName, "src/GlassDock.App/Assets/ControlCenter");
        foreach (var name in new[] { "wifi", "bluetooth", "focus", "night-light", "airplane", "hotspot", "battery-saver" })
        foreach (var variant in new[] { ("", "#ffffff"), ("-active", "#007aff"), ("-disabled", "#8e8e93") })
        {
            var svg = System.Xml.Linq.XDocument.Load(Path.Combine(assets, name + variant.Item1 + ".svg"));
            var expected = variant.Item1 == "-active" ? name switch
            {
                "focus" => "#650094", "airplane" => "#f26507", "night-light" => "#eb670f",
                "battery-saver" => "#ccbb00", _ => variant.Item2
            } : variant.Item2;
            Assert.Equal(expected, svg.Root!.Attribute("fill")!.Value.ToLowerInvariant());
            Assert.Equal("0 0 256 256", svg.Root.Attribute("viewBox")!.Value);
            Assert.Contains(svg.Descendants(), e => e.Name.LocalName == "path" && !string.IsNullOrWhiteSpace((string?)e.Attribute("d")));
        }
        foreach (var name in new[] { "previous", "next", "play", "pause", "brightness", "volume-high", "volume-low", "volume-disabled", "mute", "music", "battery", "settings", "expand", "more" })
            Assert.True(File.Exists(Path.Combine(assets, name + ".svg")), name);
        var project = File.ReadAllText(Path.Combine(root.FullName, "src/GlassDock.App/GlassDock.App.csproj"));
        Assert.Contains("Assets\\ControlCenter\\*.svg\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\"", project);
    }
}
