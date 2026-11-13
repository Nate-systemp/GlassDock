using Xunit;

namespace GlassDock.Windows.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeDesktopTestCollection
{
    public const string Name = "Native desktop state";
}
