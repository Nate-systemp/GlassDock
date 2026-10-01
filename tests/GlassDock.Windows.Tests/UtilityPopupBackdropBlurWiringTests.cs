using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>Source-level guard: the blur has to be connected to the native
/// desktop compositor, not drawn as an opaque fake frosted XAML panel.</summary>
public sealed class UtilityPopupBackdropBlurWiringTests
{
    [Fact]
    public void All_three_utilities_share_the_native_blur_and_retained_theme_path()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);

        var app = Path.Combine(directory!.FullName, "src", "GlassDock.App");
        var desktop = Path.Combine(app, "Desktop");
        var style = File.ReadAllText(Path.Combine(desktop, "UtilityPopupStyle.cs"));
        var backdrop = File.ReadAllText(Path.Combine(app, "Rendering", "DesktopGlassBackdrop.cs"));

        Assert.Contains("UtilityMaterial.CreateForPopup(appearance, mode)", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.UsePopupBlur = false", style, StringComparison.Ordinal);
        Assert.Contains("backdrop.ApplyMainDock(style, material", style, StringComparison.Ordinal);
        Assert.Contains("UsePopupBlur && dockStyle == GlassMaterialMode.Acrylic", backdrop, StringComparison.Ordinal);
        Assert.Contains("effect.Properties.InsertScalar(\"BaseBlur.BlurAmount\"", backdrop, StringComparison.Ordinal);
        Assert.Contains("public void RebuildConnectedSurface()", backdrop, StringComparison.Ordinal);
        Assert.Contains("compositor.CreateHostBackdropBrush()", backdrop, StringComparison.Ordinal);
        Assert.Contains("backdrop.RefreshPopupBackdropSource()", style, StringComparison.Ordinal);
        Assert.Contains("connectedWithHostBackdrop == wantsHost", backdrop, StringComparison.Ordinal);
        Assert.Contains("compositor.CreateBackdropBrush()", backdrop, StringComparison.Ordinal);
        Assert.Contains("UseHostBackdropForPopup", backdrop, StringComparison.Ordinal);
        Assert.Contains("effect.SetSourceParameter(\"Backdrop\", replacement)", backdrop, StringComparison.Ordinal);
        Assert.Contains("effect.SetSourceParameter(\"BaseBackdrop\", replacement)", backdrop, StringComparison.Ordinal);
        var host = File.ReadAllText(Path.Combine(directory.FullName,
            "src", "GlassDock.Windows", "Desktop", "InteractiveGlassWindowHost.cs"));
        Assert.Contains("DWMWA_USE_HOSTBACKDROPBRUSH = 17", host, StringComparison.Ordinal);
        Assert.Contains("exStyle |= 0x00080000L", host, StringComparison.Ordinal);
        Assert.Contains("NativeMethods.SetLayeredWindowAttributes(hwnd, 0, 255, 2)", host, StringComparison.Ordinal);
        Assert.Contains("UseDockLayeredTransparency", host, StringComparison.Ordinal);
        Assert.DoesNotContain("utility-host-backdrop.flag", style, StringComparison.Ordinal);

        foreach (var name in new[]
                 { "SystemQuickSettingsWindow", "SystemTrayWindow", "CalendarPopoverWindow" })
        {
            var source = File.ReadAllText(Path.Combine(desktop, name + ".cs"));
            var attachment = source.IndexOf("SystemBackdrop = backdrop;", StringComparison.Ordinal);
            Assert.True(attachment > source.IndexOf("Content = root;", StringComparison.Ordinal),
                name + " must supply XamlRoot.Content before the backdrop connects.");
            Assert.True(attachment > source.IndexOf("host.Configure();", StringComparison.Ordinal),
                name + " must configure the desktop sampling client before attaching glass.");
            Assert.Contains("UseDesktopBackdrop = true", source, StringComparison.Ordinal);
            Assert.Contains("EnableHostBackdropBrush = true", source, StringComparison.Ordinal);
            Assert.Contains("UseDockLayeredTransparency = true", source, StringComparison.Ordinal);
            Assert.Contains("UtilityPopupStyle.Apply(glass, backdrop, appearance, mode)", source,
                StringComparison.Ordinal);
        }
    }
}
