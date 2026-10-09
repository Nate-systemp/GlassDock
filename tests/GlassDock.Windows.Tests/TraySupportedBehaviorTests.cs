using GlassDock.Core.Desktop;
using GlassDock.Windows.Desktop;
using GlassDock.Windows.Settings;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class TraySupportedBehaviorTests
{
    [Theory]
    [InlineData(1, "Open", 0, true)]
    [InlineData(2, "Open", 0, false)]
    [InlineData(1, "", 0, false)]
    [InlineData(1, "Open", 1, false)]
    [InlineData(0, "Open", 0, false)]
    public void Provider_default_action_requires_one_exact_enabled_match(int count, string action, int state, bool expected)
    {
        var invoked = 0;
        object? Read(object _, string member, object[] args) => member switch
        {
            "accChildCount" => count,
            "accName" => "Discord",
            "accDefaultAction" => action,
            "accState" => state,
            _ => null
        };
        var result = WindowsTrayAccessibility.InvokeUnique(new object(), "Discord", Read, (_, id) => { invoked++; return true; });
        Assert.Equal(expected, result);
        Assert.Equal(expected ? 1 : 0, invoked);
    }

    [Fact]
    public void Failed_or_disappeared_provider_does_not_invoke_another_action()
    {
        Assert.False(WindowsTrayAccessibility.InvokeUnique(new object(), "Discord", (_, _, _) => null, (_, _) => throw new Exception("must not invoke")));
    }

    [Fact]
    public async Task Explorer_unavailable_or_stale_identity_never_launches_a_live_item_executable()
    {
        var stale = new WindowsTrayAccessibility.TrayItem("Stale", "Open", Environment.ProcessPath,
            WindowsTrayAccessibility.TrayItemSource.Accessibility);
        Assert.False(await WindowsTrayAccessibility.InvokeAsync(stale));
    }

    [Fact]
    public void Registry_discovery_requires_the_real_running_executable()
    {
        Assert.True(WindowsTrayAccessibility.IsLikelyRunning(Environment.ProcessPath!));
        Assert.False(WindowsTrayAccessibility.IsLikelyRunning(Path.Combine(Path.GetTempPath(), "not-doky", Path.GetFileName(Environment.ProcessPath!))));
    }

    [Fact]
    public async Task Saved_order_roundtrips_independently_of_settings_and_survives_bad_json()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DokyTrayTest-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "tray-order.json");
        try
        {
            var store = new TrayOrderStore(path);
            Assert.Empty((await store.LoadAsync()).Snapshot());
            await store.SaveAsync(["c", "a", "b"]);
            Assert.Equal(new[] { "c", "a", "b" }, (await new TrayOrderStore(path).LoadAsync()).Snapshot());
            await File.WriteAllTextAsync(path, "broken");
            Assert.Empty((await store.LoadAsync()).Snapshot());
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
