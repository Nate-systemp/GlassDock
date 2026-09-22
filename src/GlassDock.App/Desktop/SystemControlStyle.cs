using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal static class SystemControlStyle
{
    public static SolidColorBrush Brush(byte alpha, byte r = 235, byte g = 244, byte b = 255) =>
        new(global::Windows.UI.Color.FromArgb(alpha, r, g, b));

    public static FontIcon Icon(string glyph, double size = 19) => new()
    {
        Glyph = glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = size,
        Foreground = Brush(240), IsHitTestVisible = false
    };

    public static Button Button(UIElement content, string name, Action action, double width = 36, double height = 40)
    {
        var button = new Button
        {
            Content = content, Width = width, Height = height, MinWidth = 0,
            Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
            Background = Brush(0), BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Resources["ButtonBackgroundPointerOver"] = Brush(28);
        button.Resources["ButtonBackgroundPressed"] = Brush(44);
        button.Resources["ButtonForegroundPointerOver"] = new SolidColorBrush(Colors.White);
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
