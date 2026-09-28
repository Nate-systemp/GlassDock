using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal sealed class DevelopmentWindow : Window
{
    public DevelopmentWindow(DesktopOverlayWindow dock)
    {
        Title = "Doky — Development Controls";
        var panel = new StackPanel { Padding = new Thickness(28), Spacing = 16, RequestedTheme = ElementTheme.Dark };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.LightGreen) };
        var rendering = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        void Refresh() { status.Text = dock.Status; rendering.Text = dock.RenderingMode; }
        EventHandler changed = (_, _) => Refresh();
        dock.StatusChanged += changed;
        Closed += (_, _) => dock.StatusChanged -= changed;
        panel.Children.Add(new TextBlock { Text = "Desktop foundation", FontSize = 27 });
        panel.Children.Add(new TextBlock
        {
            Text = "Hover the bottom-center pill to raise it; click to open the dock, or press Windows to toggle.\nTaskbar suppression is protected by the recovery watchdog.\nCtrl+Alt+F12: restore taskbar · Ctrl+Alt+Space: toggle dock.",
            TextWrapping = TextWrapping.Wrap
        });
        var margin = new Slider { Header = "Bottom margin (DIP)", Minimum = 16, Maximum = 100,
            StepFrequency = 1, Value = dock.BottomMargin };
        margin.ValueChanged += (_, e) => dock.SetBottomMargin(e.NewValue);
        panel.Children.Add(margin);
        var hide = new Button { Content = "Start 60-second taskbar test", IsEnabled = dock.HotkeysAvailable };
        hide.Click += async (_, _) =>
        {
            hide.IsEnabled = false;
            await dock.StartTaskbarTestAsync();
            if (!dock.IsShuttingDown) hide.IsEnabled = dock.HotkeysAvailable;
        };
        panel.Children.Add(hide);
        var restore = new Button { Content = "Restore Windows taskbar now" };
        restore.Click += (_, _) => dock.RestoreTaskbar();
        panel.Children.Add(restore);
        var home = new Button { Content = "Test Glass Home event" };
        home.Click += (_, _) => dock.ShowHome();
        panel.Children.Add(home);
        var lab = new Button { Content = "Open Glass Material Laboratory" };
        lab.Click += (_, _) => dock.ShowLab();
        panel.Children.Add(lab);
        panel.Children.Add(status);
        panel.Children.Add(rendering);
        panel.Children.Add(new TextBlock { Text = "Bare Windows key toggles the dock; Windows-key shortcuts pass through. Dock items launch pinned apps or focus existing windows.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var exit = new Button { Content = "Exit Doky" };
        exit.Click += (_, _) => dock.RequestShutdown();
        panel.Children.Add(exit);
        Refresh();
        Content = new ScrollViewer { Content = panel, Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 20, 24, 32)) };
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(620, 680));
    }
}
