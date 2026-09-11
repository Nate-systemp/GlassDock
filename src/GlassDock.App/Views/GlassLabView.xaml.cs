using GlassDock.App.Rendering;
using GlassDock.App.ViewModels;
using GlassDock.Core.Materials;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace GlassDock.App.Views;

public sealed partial class GlassLabView : UserControl
{
    private readonly GlassLabViewModel viewModel = new();
    private readonly List<(Slider Slider, TextBlock Value, Func<GlassMaterial, double> Read)> sliders = [];
    private readonly List<(Button Button, GlassMaterialPreset Preset)> presetButtons = [];
    private ComboBox? tintPicker;
    private TextBlock? surfaceName;
    private TextBlock? surfaceDescription;
    private bool synchronizing;

    public GlassLabView()
    {
        InitializeComponent();
        presetButtons.AddRange([(FrostedButton, GlassMaterialPreset.Frosted),
            (ClearButton, GlassMaterialPreset.Clear), (RefractiveButton, GlassMaterialPreset.Refractive)]);
        CreatePreviewContent();
        CreateControls();
        DrawScene(0);
        viewModel.MaterialChanged += (_, animate) => Refresh(animate);
        Surface.RenderingModeChanged += (_, _) => UpdateStatus();
        Refresh(false);
    }

    private static SolidColorBrush Solid(uint rgb, byte alpha = 255) =>
        new(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private void CreatePreviewContent()
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var top = new Grid();
        top.Children.Add(new TextBlock { Text = "G / MATERIAL", FontSize = 11,
            CharacterSpacing = 180, Foreground = Solid(0xFFFFFF, 220) });
        top.Children.Add(new TextBlock { Text = "01", FontSize = 13, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Solid(0xFFFFFF, 200) });
        content.Children.Add(top);
        var middle = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 8 };
        surfaceName = new TextBlock { FontSize = 38, FontWeight = FontWeights.SemiBold, Foreground = Solid(0xFFFFFF) };
        surfaceDescription = new TextBlock { FontSize = 13, Foreground = Solid(0xFFFFFF, 230), TextWrapping = TextWrapping.Wrap };
        middle.Children.Add(surfaceName);
        middle.Children.Add(surfaceDescription);
        Grid.SetRow(middle, 1);
        content.Children.Add(middle);
        var bottom = new Grid();
        bottom.Children.Add(new TextBlock { Text = "GLASS, WITHOUT THE NOISE", FontSize = 9,
            CharacterSpacing = 120, Foreground = Solid(0xFFFFFF, 210) });
        bottom.Children.Add(new TextBlock { Text = "↗", FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = Solid(0xFFFFFF) });
        Grid.SetRow(bottom, 2);
        content.Children.Add(bottom);
        Surface.PreviewContent = content;
    }

    private void CreateControls()
    {
        AddSlider("Blur", "px", 0, 64, 1, m => m.BlurAmount, (m, v) => m with { BlurAmount = v });
        AddSlider("Opacity", "", 0, 1, .01, m => m.Opacity, (m, v) => m with { Opacity = v },
            "Mixes the processed material with the original backdrop. Text and edge lighting remain separate.");
        AddSlider("Saturation", "×", 0, 2, .01, m => m.Saturation, (m, v) => m with { Saturation = v });
        AddSlider("Brightness", "×", .25, 2, .01, m => m.Brightness, (m, v) => m with { Brightness = v });
        tintPicker = new ComboBox { Header = "Tint", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { "Ice · cool white", "Lavender", "Sea glass", "Champagne" }, SelectedIndex = 0 };
        AutomationProperties.SetName(tintPicker, "Tint");
        tintPicker.SelectionChanged += (_, _) =>
        {
            if (!synchronizing && tintPicker.SelectedIndex >= 0)
                viewModel.Update(viewModel.Material with { Tint = Tints[tintPicker.SelectedIndex] });
        };
        PropertyControls.Children.Add(tintPicker);
        AddSlider("Border opacity", "", 0, 1, .01, m => m.BorderOpacity, (m, v) => m with { BorderOpacity = v });
        AddSlider("Border thickness", "px", 0, 6, .25, m => m.BorderThickness, (m, v) => m with { BorderThickness = v });
        AddSlider("Corner radius", "px", 0, 100, 1, m => m.CornerRadius, (m, v) => m with { CornerRadius = v });
        AddSlider("Shadow opacity", "", 0, 1, .01, m => m.ShadowOpacity, (m, v) => m with { ShadowOpacity = v });
        AddSlider("Shadow blur", "px", 0, 100, 1, m => m.ShadowBlur, (m, v) => m with { ShadowBlur = v });
        AddSlider("Shadow offset", "px", -40, 60, 1, m => m.ShadowOffset, (m, v) => m with { ShadowOffset = v });
        var refraction = new Slider { Header = "Refraction · unavailable", Minimum = 0, Maximum = 1,
            Value = 0, IsEnabled = RefractionLayer.IsSupported };
        AutomationProperties.SetName(refraction, "Refraction amount, unavailable in native backend");
        PropertyControls.Children.Add(refraction);
        PropertyControls.Children.Add(new TextBlock
        {
            Text = "No optical displacement. The Refractive preset explores stronger edge lighting and depth.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Solid(0xABB4C6)
        });
    }

    private static readonly uint[] Tints = [0xDCEAFF, 0xE9DCFF, 0xD9FFF6, 0xFFE9C7];

    private void AddSlider(string name, string unit, double min, double max, double step,
        Func<GlassMaterial, double> read, Func<GlassMaterial, double, GlassMaterial> write, string? help = null)
    {
        var group = new StackPanel { Spacing = 0 };
        var header = new Grid();
        header.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = Solid(0xCCD2DF) });
        var valueText = new TextBlock { FontSize = 11, Foreground = Solid(0x9DCBC3), HorizontalAlignment = HorizontalAlignment.Right };
        header.Children.Add(valueText);
        group.Children.Add(header);
        var slider = new Slider { Minimum = min, Maximum = max, StepFrequency = step,
            SmallChange = step, LargeChange = (max - min) / 10, Value = read(viewModel.Material),
            Tag = unit, MinHeight = 30 };
        AutomationProperties.SetName(slider, name);
        if (help is not null) AutomationProperties.SetHelpText(slider, help);
        slider.ValueChanged += (_, args) =>
        {
            if (!synchronizing) viewModel.Update(write(viewModel.Material, args.NewValue));
        };
        group.Children.Add(slider);
        PropertyControls.Children.Add(group);
        sliders.Add((slider, valueText, read));
    }

    private void Refresh(bool animate)
    {
        synchronizing = true;
        try
        {
            var material = viewModel.Material;
            foreach (var (slider, value, read) in sliders)
            {
                slider.Value = read(material);
                value.Text = $"{read(material):0.##} {slider.Tag}";
            }
            if (tintPicker is not null) tintPicker.SelectedIndex = Array.IndexOf(Tints, material.Tint);
            foreach (var (button, preset) in presetButtons)
            {
                var selected = viewModel.PresetName == preset.ToString();
                button.Background = Solid(selected ? 0xD0EAE3u : 0x202630u);
                button.Foreground = Solid(selected ? 0x182B2Au : 0xD3DAE7u);
                AutomationProperties.SetHelpText(button, selected ? "Selected material preset" : "Apply material preset");
            }
            if (surfaceName is not null) surfaceName.Text = viewModel.PresetName;
            if (surfaceDescription is not null) surfaceDescription.Text = viewModel.PresetName switch
            {
                "Frosted" => "Soft diffusion. Quiet detail.\nA little distance from the world behind.",
                "Clear" => "Less between you and the light.\nA lighter touch on the world behind.",
                "Refractive" => "Light along the edge. Depth within.\nAn optical impression, not true refraction.",
                _ => "Your material, in real time.\nFollow the light. Find the balance."
            };
            Surface.Apply(material, animate);
            UpdateStatus();
        }
        finally { synchronizing = false; }
    }

    private void UpdateStatus()
    {
        PresetStatus.Text = $"{viewModel.PresetName} / session only";
        RenderStatus.Text = $"{Surface.RenderingMode}   ·   Blur {viewModel.Material.BlurAmount:0.#} px   ·   Opacity {viewModel.Material.Opacity:0.##}";
    }

    private void FrostedClick(object sender, RoutedEventArgs e) => viewModel.SelectPreset(GlassMaterialPreset.Frosted);
    private void ClearClick(object sender, RoutedEventArgs e) => viewModel.SelectPreset(GlassMaterialPreset.Clear);
    private void RefractiveClick(object sender, RoutedEventArgs e) => viewModel.SelectPreset(GlassMaterialPreset.Refractive);
    private void ResetClick(object sender, RoutedEventArgs e) => viewModel.SelectPreset(GlassMaterialPreset.Frosted);
    private void SceneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SceneCanvas is not null && ScenePicker.SelectedIndex >= 0) DrawScene(ScenePicker.SelectedIndex);
    }

    private void DrawScene(int scene)
    {
        SceneCanvas.Children.Clear();
        SceneTitle.Text = new[] { "CHROMATIC FIELD", "AFTER HOURS", "DAYLIGHT STUDY", "CONTRAST TEST", "FREQUENCY STUDY" }[scene];
        SceneCanvas.Background = Solid(scene == 2 ? 0xDCE5EDu : 0x121929u);
        if (scene == 0)
        {
            SceneCanvas.Background = Gradient(0x131D45, 0x3649A5, 0x865CAF);
            Add(new Ellipse { Width = 900, Height = 650, Fill = Gradient(0x194CD7, 0x54DCDC, 0xCEFFFF) }, -320, -210);
            Add(new Ellipse { Width = 760, Height = 720, Fill = Gradient(0xE09DF2, 0x865EE2, 0x303277) }, 520, -130);
            Add(new Ellipse { Width = 700, Height = 340, Fill = Gradient(0xFCE3BC, 0xFE9A69, 0xCB596E),
                RenderTransform = new RotateTransform { Angle = -26, CenterX = 350, CenterY = 170 } }, 180, 380);
            Add(new Ellipse { Width = 450, Height = 450, Stroke = Solid(0xFFFFFF, 110), StrokeThickness = 1 }, 630, 180);
        }
        else if (scene == 1)
        {
            Add(new Ellipse { Width = 700, Height = 650, Fill = Gradient(0x141D42, 0x253754, 0x0B0E16) }, 450, 20);
            Add(new Rectangle { Width = 34, Height = 740, Fill = Solid(0x7ED6D3) }, 260, 0);
        }
        else if (scene == 2)
        {
            Add(new Ellipse { Width = 650, Height = 650, Fill = Gradient(0xFFFFFF, 0xCCD6E0, 0xADBCCE) }, 480, -100);
            Add(new Rectangle { Width = 180, Height = 740, Fill = Solid(0xFFFFFF) }, 180, 0);
        }
        else if (scene == 3)
        {
            for (var x = 0; x < 1000; x += 100)
                Add(new Rectangle { Width = 100, Height = 740, Fill = Solid(x % 200 == 0 ? 0xF8F9FCu : 0x080B10u) }, x, 0);
            Add(new Rectangle { Width = 1000, Height = 30, Fill = Solid(0xF07D48) }, 0, 320);
        }
        if (scene is 0 or 4)
        {
            for (var i = 0; i < 38; i++)
                Add(new Line { X1 = 0, X2 = 1000, Y1 = 0, Y2 = 0, Stroke = Solid(0xFFFFFF, (byte)(scene == 4 ? 100 : 28)),
                    StrokeThickness = i % 5 == 0 ? 2 : 1 }, 0, 30 + i * 18);
        }
        var foreground = scene == 2 ? 0x29354Cu : 0xFFFFFFu;
        Add(new TextBlock { Text = scene == 4 ? "Aa / 0123456789" : "FORM\n& LIGHT", FontSize = 104,
            FontWeight = FontWeights.Bold, Foreground = Solid(foreground, 190), LineHeight = 105 }, 100, 190);
        Add(new TextBlock { Text = "THE WORLD BEHIND THE GLASS", FontSize = 12,
            CharacterSpacing = 220, Foreground = Solid(foreground, 200) }, 280, 490);
        for (var i = 0; i < 7; i++)
            Add(new Rectangle { Width = 8, Height = 80, Fill = Solid(foreground, 200) }, 720 + i * 14, 300);
        SceneTitle.Foreground = Solid(0xFFFFFF);
    }

    private static LinearGradientBrush Gradient(uint first, uint middle, uint last) => new()
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop { Color = Solid(first).Color, Offset = 0 },
            new GradientStop { Color = Solid(middle).Color, Offset = 0.55 },
            new GradientStop { Color = Solid(last).Color, Offset = 1 }
        }
    };

    private void Add(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        SceneCanvas.Children.Add(element);
    }
}
