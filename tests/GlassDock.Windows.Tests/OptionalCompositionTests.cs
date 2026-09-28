using System.Runtime.InteropServices;
using GlassDock.App.Rendering;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class OptionalCompositionTests
{
    [Fact]
    public void Basic_mode_never_initializes_optional_native_components()
    {
        var session = new OptionalComposition(true);
        Assert.False(session.TryInitialize(() => throw new Exception("Must not run"), _ => { }));
    }

    [Fact]
    public void Successful_initialization_is_shared_by_later_popups()
    {
        var session = new OptionalComposition(false);
        var count = 0;
        Assert.True(session.TryInitialize(() => count++, _ => { }));
        Assert.True(session.TryInitialize(() => count++, _ => { }));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Native_failure_disables_retries_and_reports_original_error()
    {
        var session = new OptionalComposition(false);
        var error = new COMException("Device unavailable", unchecked((int)0x80070057));
        Exception? reported = null;
        Assert.False(session.TryInitialize(() => throw error, e => reported = e));
        Assert.Same(error, reported);
        Assert.True(session.Disabled);
        Assert.False(session.TryInitialize(() => throw new Exception("Must not retry"), _ => { }));
    }

    [Fact]
    public void Unrelated_errors_are_not_hidden_as_graphics_failures()
    {
        var session = new OptionalComposition(false);
        Assert.Throws<InvalidOperationException>(() => session.TryInitialize(
            () => throw new InvalidOperationException("Application bug"), _ => { }));
        Assert.False(session.Disabled);
    }
}
