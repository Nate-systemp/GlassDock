using GlassDock.Windows.Desktop;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class InputDesktopMonitorTests
{
    [Theory]
    [InlineData("Default", "Default", true)]
    [InlineData("default", "Default", true)]
    [InlineData("Winlogon", "Default", false)]
    [InlineData("OtherDesktop", "Default", false)]
    [InlineData(null, "Default", false)]
    [InlineData("Default", null, false)]
    [InlineData("", "", false)]
    public void Capture_resumes_only_on_the_rendering_desktop(string? input, string? rendering, bool expected)
    {
        Assert.Equal(expected, InputDesktopMonitor.CanResumeCapture(input, rendering));
    }
}
