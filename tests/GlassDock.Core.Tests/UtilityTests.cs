using GlassDock.Core.Desktop;
using GlassDock.Core.Materials;
using GlassDock.Core.Settings;
using System.Xml.Linq;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class UtilityTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("Home", "Home")]
    [InlineData("12345678901234", "12345678901234")]
    [InlineData("123456789012345", "12345678901234...")]
    [InlineData("1234567890123\U0001F600x", "1234567890123\U0001F600...")]
    [InlineData("1234567890123e\u0301x", "1234567890123e\u0301...")]
    public void Wifi_caption_limits_display_to_14_complete_characters(string ssid, string expected)
    {
        Assert.Equal(expected, WifiNetworkCaption.Truncate(ssid));
    }

    [Fact]
    public void Utilities_are_opt_in_deduplicated_and_do_not_change_dock_material()
    {
        Assert.Empty(GlassDockSettings.Normalize(new()).SelectedUtilities);
        foreach (var mode in Enum.GetValues<DockAppearanceMode>())
        {
            var settings = GlassDockSettings.Normalize(new() { DockAppearanceMode = mode,
                SelectedUtilities = new[] { UtilityKind.Bluetooth, UtilityKind.Wifi, UtilityKind.Bluetooth, (UtilityKind)99 } });
            Assert.Equal(new[] { UtilityKind.Bluetooth, UtilityKind.Wifi }, settings.SelectedUtilities);
            Assert.Equal(mode, settings.DockAppearanceMode);
            Assert.Equal(settings, GlassDockSettings.Normalize(settings));
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(1.25)] [InlineData(1.5)] [InlineData(2)]
    public void Independent_circles_remain_on_row_with_gap_and_equal_diameter(double dpi)
    {
        var dock = new HomeCardRect(120 * dpi, 80 * dpi, 600 * dpi, 68 * dpi);
        var first = UtilityPlacement.Beside(dock, 12 * dpi);
        var second = UtilityPlacement.Beside(dock, 12 * dpi, 1);
        Assert.Equal(12 * dpi, first.X - dock.X - dock.Width);
        Assert.Equal(first.Width, first.Height);
        Assert.Equal(dock.Height, first.Height);
        Assert.Equal(dock.Y, first.Y);
        Assert.Equal(dock.Y + dock.Height / 2, first.Y + first.Height / 2);
        Assert.Equal(12 * dpi, second.X - first.X - first.Width);
    }

    [Fact]
    public void Dragging_stays_on_row_between_neighbor_and_monitor_edges()
    {
        var circle = UtilityPlacement.Beside(new(100, 200, 600, 84), 12);
        var left = UtilityPlacement.WithinRow(circle, -500, 712, 1000);
        var right = UtilityPlacement.WithinRow(circle, 2000, 712, 1000);
        Assert.Equal(712, left.X); Assert.Equal(916, right.X);
        Assert.Equal(circle.Y, right.Y); Assert.Equal(circle.Height, right.Height);
        Assert.Equal(circle.Width, right.Width);
    }

    [Fact]
    public void Svg_tint_replaces_hardcoded_colors_without_filling_transparent_paths()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg' fill='currentColor'><path fill='#000' d='M0 0'/><path fill='none' stroke='#fff'/></svg>";
        var output = UtilitySvg.Tint(svg, 0xFF0A84FF);
        Assert.DoesNotContain("currentColor", output);
        Assert.DoesNotContain("#000", output);
        Assert.Contains("fill=\"none\"", output);
        Assert.Contains("stroke=\"#0A84FF\"", output);
        Assert.Throws<FormatException>(() => UtilitySvg.Tint("<svg><image href='file:///secret'/></svg>", 0));
        Assert.Throws<System.Xml.XmlException>(() => UtilitySvg.Tint("<!DOCTYPE svg SYSTEM 'file:///secret'><svg/>", 0));
    }

    [Fact]
    public void Password_and_ssid_cannot_inject_profile_xml()
    {
        var ssid = System.Text.Encoding.UTF8.GetBytes("A<&\" network");
        var password = "p<&\"assword";
        var xml = XDocument.Parse(WifiProfile.Create(ssid, 7, 4, password, true));
        XNamespace ns = "http://www.microsoft.com/networking/WLAN/profile/v1";
        Assert.Equal(password, xml.Descendants(ns + "keyMaterial").Single().Value);
        Assert.Equal(Convert.ToHexString(ssid), xml.Descendants(ns + "hex").Single().Value);
        Assert.Equal("auto", xml.Descendants(ns + "connectionMode").Single().Value);
        Assert.Single(xml.Descendants(ns + "keyMaterial"));
    }

    [Fact]
    public void Open_network_requires_no_password_and_enterprise_is_not_faked()
    {
        var xml = WifiProfile.Create(new byte[] { 65 }, 1, 0, "", false);
        Assert.DoesNotContain("sharedKey", xml);
        Assert.Contains("manual", xml);
        Assert.Throws<ArgumentException>(() => WifiProfile.Create(new byte[] { 65 }, 7, 4, "short", false));
        Assert.Throws<ArgumentException>(() => WifiProfile.Create(new byte[] { 65 }, 6, 4, "password", false));
        Assert.Contains("networkKey", WifiProfile.Create(new byte[] { 65 }, 7, 4, new string('A', 64), true));
    }
}
