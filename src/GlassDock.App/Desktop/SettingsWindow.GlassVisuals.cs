using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

public sealed partial class SettingsWindow
{
    private GlassVisualProfile glassDraft = new();
    private CustomGlassPreset[] glassPresets = [];
    private Guid? selectedGlassPreset;
    private bool glassDirty, refreshingGlass, savingNonVisual, advancedGlass;
    private string glassTarget = "main";
    private readonly StackPanel glassRows = new() { Spacing = 18 };
    private readonly ComboBox glassPresetBox = new() { Header = "Custom presets", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox inheritGlass = new() { Content = "Inherit main dock material" };
    private readonly TextBlock glassNotice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private sealed record GlassTarget(string Id, string Label) { public override string ToString() => Label; }

    private void LoadGlassDraft(GlassDockSettings settings)
    {
        glassDraft = GlassVisualProfile.FromSettings(settings);
        glassPresets = settings.CustomGlassPresets;
        selectedGlassPreset = settings.SelectedCustomGlassPreset;
        if (shellReady) RefreshGlassEditor();
    }

    private GlassDockSettings ApplyGlassDraft(GlassDockSettings settings)
    {
        var profile = glassDraft.Normalize();
        var material = profile.DockMaterial;
        return GlassDockSettings.Normalize(settings with
        {
            GlassVisuals = profile, CustomGlassPresets = glassPresets,
            SelectedCustomGlassPreset = selectedGlassPreset,
            DockAppearanceMode = profile.Mode,
            GlassMaterialMode = profile.Mode.GlassStyle() ?? settings.GlassMaterialMode,
            ClearRefractionStrength = material.RefractionStrength,
            SpecularHighlightAngle = material.SpecularAngleDegrees,
            CircularHighlightsEnabled = profile.NativeHighlights,
            ClearCircularHighlightsEnabled = profile.ClearHighlights
        });
    }

    private void SelectBuiltInGlass(DockAppearanceMode mode)
    {
        glassDraft = GlassVisualProfile.BuiltIn(mode);
        selectedGlassPreset = null;
        glassDirty = true;
        RefreshGlassEditor();
    }

    private void GlassEdited()
    {
        if (populating || refreshingGlass || closed || !shellReady) return;
        glassDraft = glassDraft with
        {
            NativeHighlights = CircularHighlightsToggle.IsOn,
            ClearHighlights = ClearCircularHighlightsToggle.IsOn
        };
        glassDirty = true;
        UpdateDockPreview();
        SetStatus("Glass preview changed. Apply to save and update the dock.", true);
    }

    private void BuildGlassEditor()
    {
        var view = new ComboBox { Header = "Detail", ItemsSource = new[] { "Basic", "Advanced" }, SelectedIndex = 0 };
        view.SelectionChanged += (_, _) => { advancedGlass = view.SelectedIndex == 1; RefreshGlassRows(); };
        GlassControls.Children.Add(view);
        GlassControls.Children.Add(glassPresetBox);
        glassPresetBox.DisplayMemberPath = "Name";
        glassPresetBox.SelectionChanged += (_, _) =>
        {
            if (refreshingGlass || glassPresetBox.SelectedItem is not CustomGlassPreset preset) return;
            glassDraft = preset.Profile;
            selectedGlassPreset = preset.Id;
            glassDirty = true;
            RefreshGlassEditor();
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        void Action(string title, Func<Task> run)
        {
            var button = new Button { Content = title };
            button.Click += async (_, _) => await run();
            actions.Children.Add(button);
        }
        Action("Save as new", () => SaveGlassPresetAsync(false));
        Action("Rename", RenameGlassPresetAsync);
        Action("Duplicate", () => SaveGlassPresetAsync(true));
        Action("Delete", DeleteGlassPresetAsync);
        GlassControls.Children.Add(actions);
        var targets = new List<GlassTarget>
        {
            new("main", "Main material"), new("dock", "Dock surface"),
            new("indicators", "Running / active indicators")
        };
        targets.AddRange(previewApplications.Select(app => new GlassTarget(app.Id, "Button · " + app.Name)));
        var target = new ComboBox { Header = "Component", ItemsSource = targets, SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        target.SelectionChanged += (_, _) =>
        {
            if (target.SelectedItem is GlassTarget item) { glassTarget = item.Id; RefreshGlassRows(); UpdateDockPreview(); }
        };
        GlassControls.Children.Add(target);
        inheritGlass.Checked += (_, _) => SetGlassInheritance(true);
        inheritGlass.Unchecked += (_, _) => SetGlassInheritance(false);
        GlassControls.Children.Add(inheritGlass);
        GlassControls.Children.Add(glassNotice);
        GlassControls.Children.Add(glassRows);
        var reset = new Button { Content = "Reset all glass visuals" };
        reset.Click += (_, _) =>
        {
            glassDraft = GlassVisualProfile.BuiltIn(glassDraft.Mode);
            selectedGlassPreset = null; glassDirty = true;
            RefreshGlassEditor();
        };
        GlassControls.Children.Add(reset);
        var discard = new Button { Content = "Discard preview changes" };
        discard.Click += (_, _) => { glassDirty = false; LoadGlassDraft(settingsSession.Current); };
        GlassControls.Children.Add(discard);
        RefreshGlassEditor();
    }

    private LiquidGlassMaterial TargetGlass => glassTarget switch
    {
        "main" => glassDraft.Material,
        "dock" => glassDraft.DockMaterial,
        _ => glassDraft.Component(glassTarget) ?? glassDraft.Material
    };

    private void WriteTargetGlass(LiquidGlassMaterial value)
    {
        glassDraft = glassTarget switch
        {
            "main" => glassDraft with { Material = value.Normalize() },
            "dock" => glassDraft with { DockOverride = value.Normalize() },
            _ => glassDraft.WithComponent(glassTarget, value.Normalize())
        };
        glassDirty = true;
        selectedGlassPreset = null;
        UpdateDockPreview();
        SetStatus("Glass preview changed. Apply to save and update the dock.", true);
    }

    private void SetGlassInheritance(bool inherit)
    {
        if (refreshingGlass || glassTarget == "main") return;
        glassDraft = glassTarget == "dock"
            ? glassDraft with { DockOverride = inherit ? null : glassDraft.Material }
            : glassDraft.WithComponent(glassTarget, inherit ? null : glassDraft.Material);
        glassDirty = true;
        RefreshGlassRows(); UpdateDockPreview();
    }

    private bool SupportsTarget(GlassVisualControl control)
    {
        if (glassTarget is "main" or "dock") return control.Supported(glassDraft.Mode);
        if (glassTarget == "indicators")
            return control.Id is "SurfaceOpacity" or "CornerRadius" or "EdgeWidth" or "EdgeOpacity";
        return control.Id is "SurfaceOpacity" or "MaterialBlend" or "BlurAmount" or "BlurQuality"
            or "Saturation" or "Brightness" or "Contrast" or "CornerRadius" or "EdgeWidth" or "EdgeOpacity";
    }

    private void RefreshGlassEditor()
    {
        refreshingGlass = true;
        glassPresetBox.ItemsSource = glassPresets;
        glassPresetBox.SelectedItem = glassPresets.FirstOrDefault(p => p.Id == selectedGlassPreset);
        DockAppearanceModeBox.SelectedIndex = (int)glassDraft.Mode;
        CircularHighlightsToggle.IsOn = glassDraft.NativeHighlights;
        ClearCircularHighlightsToggle.IsOn = glassDraft.ClearHighlights;
        UpdateDockAppearanceDescription(glassDraft.Mode);
        foreach (var (mode, button) in appearanceButtons)
        {
            button.BorderThickness = new(mode == glassDraft.Mode ? 2 : 1);
            button.BorderBrush = mode == glassDraft.Mode
                ? new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 99, 174, 255)) : Brush("DividerBrush");
            AutomationProperties.SetItemStatus(button, mode == glassDraft.Mode ? "Selected" : "Not selected");
        }
        refreshingGlass = false;
        RefreshGlassRows();
        UpdateDockPreview();
    }

    private void RefreshGlassRows()
    {
        refreshingGlass = true;
        inheritGlass.Visibility = glassTarget == "main" ? Visibility.Collapsed : Visibility.Visible;
        inheritGlass.IsChecked = glassTarget == "dock" ? glassDraft.DockOverride is null : glassDraft.Component(glassTarget) is null;
        var inherited = glassTarget != "main" && inheritGlass.IsChecked == true;
        glassNotice.Text = glassTarget == "indicators"
            ? "Indicators support opacity, edge definition and rounding. Other material properties are inherited from the dock beneath them."
            : glassTarget is not ("main" or "dock")
                ? "Button overrides change the surface over the dock. Refraction is inherited from the dock beneath."
                : glassDraft.Mode == DockAppearanceMode.Clear
                    ? "Clear supports all optical controls. Preset colors stay fixed. Apply saves the preview and preset library."
                    : "Native and solid presets retain their renderer. Refraction, dispersion and diffusion require Clear; unsupported controls are disabled.";
        glassRows.Children.Clear();
        var controls = GlassVisualControl.All.Where(c => advancedGlass || c.Basic);
        if (glassTarget is not ("main" or "dock")) controls = controls.Where(SupportsTarget);
        foreach (var group in controls.GroupBy(c => c.Section))
        {
            var section = new StackPanel { Spacing = 8 };
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            header.Children.Add(new TextBlock { Text = group.Key, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            var reset = new Button { Content = "Reset", IsEnabled = !inherited };
            reset.Click += (_, _) =>
            {
                var defaults = GlassVisualProfile.BuiltIn(glassDraft.Mode).Material;
                var value = TargetGlass;
                foreach (var control in GlassVisualControl.All.Where(c => c.Section == group.Key && SupportsTarget(c)))
                    value = control.Write(value, control.Read(defaults));
                WriteTargetGlass(value); RefreshGlassRows();
            };
            header.Children.Add(reset); section.Children.Add(header);
            foreach (var control in group)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new() { Width = new(62) });
                var label = new TextBlock { Text = control.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(label, control.Description);
                row.Children.Add(label);
                var maximum = glassTarget == "indicators" && control.Id == "CornerRadius" ? 2.25 :
                    glassTarget == "indicators" && control.Id == "EdgeWidth" ? 1.5 : control.Maximum;
                var slider = new Slider { Minimum = control.Minimum, Maximum = maximum,
                    StepFrequency = control.Id == "BlurQuality" ? 1 : control.Maximum > 10 ? .5 : .01,
                    Value = Math.Min(maximum, control.Read(TargetGlass)), IsEnabled = !inherited && SupportsTarget(control) };
                AutomationProperties.SetName(slider, control.Label);
                AutomationProperties.SetHelpText(slider, control.Description);
                ToolTipService.SetToolTip(slider, control.Description);
                var number = new TextBlock { Text = slider.Value.ToString("0.##"), VerticalAlignment = VerticalAlignment.Center };
                slider.ValueChanged += (_, _) =>
                {
                    if (refreshingGlass) return;
                    number.Text = slider.Value.ToString("0.##");
                    WriteTargetGlass(control.Write(TargetGlass, (float)slider.Value));
                };
                Grid.SetColumn(slider, 1); Grid.SetColumn(number, 2);
                row.Children.Add(slider); row.Children.Add(number); section.Children.Add(row);
            }
            glassRows.Children.Add(section);
        }
        refreshingGlass = false;
    }

    private async Task<string?> GlassPresetNameAsync(string title, string initial)
    {
        var input = new TextBox { Text = initial, MaxLength = 60, Header = "Preset name" };
        var dialog = new ContentDialog { XamlRoot = SettingsRoot.XamlRoot, RequestedTheme = SettingsRoot.RequestedTheme,
            Title = title, Content = input, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim() : null;
    }

    private async Task SaveGlassPresetAsync(bool duplicate)
    {
        if (glassPresets.Length >= 64) { SetStatus("The preset library is full (64). Delete a custom preset first.", false); return; }
        var name = await GlassPresetNameAsync(duplicate ? "Duplicate glass preset" : "Save custom glass", duplicate ? "Glass copy" : "My glass");
        if (name is null || closed) return;
        var preset = new CustomGlassPreset(Guid.NewGuid(), name, glassDraft.Normalize());
        glassPresets = glassPresets.Append(preset).ToArray();
        selectedGlassPreset = preset.Id; glassDirty = true;
        RefreshGlassEditor();
        SetStatus("Preset added to preview library. Apply to save.", true);
    }

    private async Task RenameGlassPresetAsync()
    {
        var preset = glassPresets.FirstOrDefault(p => p.Id == selectedGlassPreset);
        if (preset is null) { SetStatus("Select a custom preset to rename.", false); return; }
        var name = await GlassPresetNameAsync("Rename custom preset", preset.Name);
        if (name is null || closed) return;
        glassPresets = glassPresets.Select(p => p.Id == preset.Id ? p with { Name = name } : p).ToArray();
        glassDirty = true; RefreshGlassEditor();
    }

    private async Task DeleteGlassPresetAsync()
    {
        var preset = glassPresets.FirstOrDefault(p => p.Id == selectedGlassPreset);
        if (preset is null) { SetStatus("Select a custom preset to delete.", false); return; }
        var dialog = new ContentDialog { XamlRoot = SettingsRoot.XamlRoot, RequestedTheme = SettingsRoot.RequestedTheme,
            Title = $"Delete {preset.Name}?", Content = "The current preview is kept. Apply to save the library change.",
            PrimaryButtonText = "Delete", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || closed) return;
        glassPresets = glassPresets.Where(p => p.Id != preset.Id).ToArray();
        selectedGlassPreset = null; glassDirty = true; RefreshGlassEditor();
    }
}
