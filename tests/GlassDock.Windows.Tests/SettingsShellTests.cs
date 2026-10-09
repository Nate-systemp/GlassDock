using System.Xml.Linq;
using GlassDock.Core.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class SettingsShellTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln"))) directory = directory.Parent;
        return directory!.FullName;
    }

    [Fact]
    public void Every_search_and_navigation_destination_exists_once_in_the_retained_tree()
    {
        var document = XDocument.Load(Path.Combine(Root(), "src/GlassDock.App/Desktop/SettingsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var names = document.Descendants().Select(element => (string?)element.Attribute(x + "Name")).Where(name => name is not null).ToArray();
        Assert.Equal(names.Length, names.Distinct().Count());
        foreach (var page in SettingsCatalog.Pages) Assert.Contains(page.Id + "Page", names);
        foreach (var option in SettingsCatalog.Options) Assert.Contains(option.Control, names);
        Assert.Single(document.Descendants(), element => element.Name.LocalName == "ScrollViewer");
    }

    [Fact]
    public void Display_options_preserve_the_persisted_enum_order()
    {
        var document = XDocument.Load(Path.Combine(Root(), "src/GlassDock.App/Desktop/SettingsWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var box = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "DockDisplayModeBox");
        var options = box.Elements().Select(element => (string?)element.Attribute("Content")).ToArray();
        Assert.Equal(4, options.Length);
        Assert.Contains("Primary", options[(int)DockDisplayMode.Primary]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pointer", options[(int)DockDisplayMode.Pointer]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active", options[(int)DockDisplayMode.Foreground]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All displays", options[(int)DockDisplayMode.AllDisplays]!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WinUI_lifetime_uses_the_shared_session_and_confirms_resets()
    {
        // WinUI is not loaded by this test project; only the UI-only wiring needs a source check.
        var shell = File.ReadAllText(Path.Combine(Root(), "src/GlassDock.App/Desktop/SettingsWindow.Shell.cs"));
        var code = File.ReadAllText(Path.Combine(Root(), "src/GlassDock.App/Desktop/SettingsWindow.xaml.cs"));
        Assert.Contains("settingsSession.Changed += SettingsSessionChanged", shell);
        Assert.Contains("settingsSession.Changed -= SettingsSessionChanged", shell);
        Assert.DoesNotContain("new GlassDockSettingsSession", shell + code);
        Assert.Contains("ConfirmResetAsync(\"editable preferences\")", code);
        Assert.Contains("ConfirmResetAsync(\"appearance\")", code);
        Assert.Contains("ConfirmResetAsync(\"dock placement\")", code);
        Assert.Contains("DefaultButton = ContentDialogButton.Close", shell);
    }
}
