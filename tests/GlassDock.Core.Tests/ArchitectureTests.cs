using System.Xml.Linq;
using Xunit;

namespace GlassDock.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void Core_has_no_platform_or_project_dependencies()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName, "src", "GlassDock.Core", "GlassDock.Core.csproj"));
        Assert.Equal("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
    }
}
