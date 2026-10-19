using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace GlassDock.App.Desktop;

internal sealed partial class SystemQuickSettingsWindow
{
    // Explicit local diagnostic launch only; no polling, hooks, or normal-run I/O.
    private void AttachInputTrace()
    {
        var path = Environment.GetEnvironmentVariable("DOKY_CONTROL_INPUT_LOG");
        if (string.IsNullOrWhiteSpace(path)) return;
        var gate = new object();
        var count = 0;
        var geometryDumped = false;
        long lastMove = 0;
        void Write(string message)
        {
            if (++count > 1000) return;
            var line = $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}\n";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { lock (gate) File.AppendAllText(path, line); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            });
        }
        string Describe(DependencyObject? node)
        {
            var parts = new List<string>();
            for (var i = 0; node is not null && i < 6; i++, node = VisualTreeHelper.GetParent(node))
            {
                var part = node.GetType().Name;
                if (node is Control c)
                    part += $"({(panel.Visibility == Visibility.Visible ? AutomationProperties.GetName(c) : "details")},enabled={c.IsEnabled},hit={c.IsHitTestVisible})";
                parts.Add(part);
                if (ReferenceEquals(node, root)) break;
            }
            return string.Join(" > ", parts);
        }
        void Pointer(string kind, PointerRoutedEventArgs args)
        {
            if (kind == "move")
            {
                var now = Environment.TickCount64;
                if (now - lastMove < 500) return;
                lastMove = now;
            }
            var point = args.GetCurrentPoint(root);
            Write($"{kind} xy={point.Position} left={point.Properties.IsLeftButtonPressed} rootHit={root.IsHitTestVisible} panel={panel.Visibility} source={Describe(args.OriginalSource as DependencyObject)}");
        }
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) => Pointer("down", e)), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) => Pointer("up", e)), true);
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) => Pointer("move", e)), true);
        root.PointerEntered += (_, _) =>
        {
            if (geometryDumped || panel.Visibility != Visibility.Visible) return;
            geometryDumped = true;
            Write($"enter size={root.ActualWidth}x{root.ActualHeight} dpi={root.XamlRoot?.RasterizationScale} inputHeight={host.InputHeightPixels}");
            void Walk(DependencyObject parent)
            {
                if (parent is Button button)
                {
                    var bounds = button.TransformToVisual(root).TransformBounds(new(0, 0, button.ActualWidth, button.ActualHeight));
                    Write($"button={AutomationProperties.GetName(button)} bounds={bounds} enabled={button.IsEnabled} hit={button.IsHitTestVisible}");
                    if (button.ActualWidth > 0 && button.ActualHeight > 0 && button.IsEnabled)
                    {
                        var center = new global::Windows.Foundation.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                        var hits = VisualTreeHelper.FindElementsInHostCoordinates(center, root).ToArray();
                        Write($"probe={AutomationProperties.GetName(button)} includesButton={hits.Contains(button)} first={Describe(hits.FirstOrDefault())}");
                    }
                }
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) Walk(VisualTreeHelper.GetChild(parent, i));
            }
            if (panel.Visibility == Visibility.Visible) Walk(panel);
        };
        Write("trace attached");
    }
}
