using System.Xml.Linq;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Windows_references_only_Core()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName, "src", "GlassDock.Windows", "GlassDock.Windows.csproj"));
        var reference = Assert.Single(project.Descendants("ProjectReference"));
        Assert.Equal("../GlassDock.Core/GlassDock.Core.csproj", reference.Attribute("Include")?.Value);
        Assert.Empty(project.Descendants("PackageReference"));
    }
}
