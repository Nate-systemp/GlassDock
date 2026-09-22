using System.Xml.Linq;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Windows_references_only_Core_and_the_Windows_WMI_provider()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName, "src", "GlassDock.Windows", "GlassDock.Windows.csproj"));
        var reference = Assert.Single(project.Descendants("ProjectReference"));
        Assert.Equal("../GlassDock.Core/GlassDock.Core.csproj", reference.Attribute("Include")?.Value);
        // The authorized control center uses Microsoft's WMI wrapper for monitor brightness.
        // Keep this an explicit allowlist; UI/framework dependencies still do not belong here.
        var package = Assert.Single(project.Descendants("PackageReference"));
        Assert.Equal("System.Management", package.Attribute("Include")?.Value);
    }
}
