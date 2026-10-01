using Xunit;

namespace GlassDock.Windows.Tests;

/// <summary>
/// Tests in this collection interact with process-global/native desktop state
/// (real cursor position, foreground/z-order, HWND hit testing and timers).
/// Running them beside other native-window tests makes otherwise-correct
/// assertions depend on scheduling and external desktop state.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NativeDesktopTestCollection
{
    public const string Name = "Native desktop state";
}
