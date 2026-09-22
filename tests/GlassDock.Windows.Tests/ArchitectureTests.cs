using System.Xml.Linq;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void App_build_contains_the_current_watchdog_and_its_dependencies()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent!.Name;
        var framework = directory.Name;
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GlassDock.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = Path.Combine(directory.FullName, "src", "GlassDock.Watchdog", "bin", configuration, framework);
        var destination = Path.Combine(directory.FullName, "src", "GlassDock.App", "bin", configuration, framework, "win-x64", "Recovery");
        Assert.True(File.Exists(Path.Combine(destination, "GlassDock.Watchdog.exe")));
        var files = Directory.GetFiles(source).AsEnumerable();
        if (Directory.Exists(Path.Combine(source, "runtimes")))
            files = files.Concat(Directory.GetFiles(Path.Combine(source, "runtimes"), "*", SearchOption.AllDirectories));
        foreach (var file in files)
        {
            var copied = Path.Combine(destination, Path.GetRelativePath(source, file));
            Assert.True(File.Exists(copied), $"Missing recovery dependency: {copied}");
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)),
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(copied)));
        }
    }

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
