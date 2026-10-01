using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class ShellApplicationIdentityResolverTests
{
    [Fact]
    public void Ambiguous_direct_evidence_cannot_fall_through_to_squirrel()
    {
        var path = @"C:\Apps\Product\app-1.0\Product.exe";
        var hints = new[]
        {
            new ShellApplicationIdentityHint("A", path, null, "a.lnk"),
            new ShellApplicationIdentityHint("B", path, null, "b.lnk"),
            new ShellApplicationIdentityHint("C", @"C:\Apps\Product\Update.exe", "--processStart Product.exe", "c.lnk")
        };
        Assert.Null(ShellApplicationIdentityResolver.Resolve(path, hints));
    }

    [Theory]
    [InlineData("Launcher.exe", "--processStart Product.exe", @"app-1.0\Product.exe")]
    [InlineData("Update.exe", "--processStart ../Product.exe", @"app-1.0\Product.exe")]
    [InlineData("Update.exe", "--processStart Product.exe", @"Other\Product.exe")]
    [InlineData("Update.exe", "--processStart Product.exe", @"app-1.0\Other\Product.exe")]
    public void Unrelated_launcher_or_nested_target_is_not_claimed(string launcher, string arguments, string relative)
    {
        var hint = new ShellApplicationIdentityHint("Vendor.Product", @"C:\Apps\Product\" + launcher, arguments, "p.lnk");
        Assert.False(ShellApplicationIdentityResolver.MatchesProcessStartShortcut(hint, @"C:\Apps\Product\" + relative));
    }

    [Fact]
    public void Canonical_squirrel_identity_merges_pin_and_windows_and_matches_badge_coordinator()
    {
        var path = @"C:\Apps\Product\app-1.0\Product.exe";
        var appId = ShellApplicationIdentityResolver.Resolve(path,
            [new("Vendor.Product", @"C:\Apps\Product\Update.exe", "--processStart Product.exe", "p.lnk")]);
        var identity = new GlassDock.Core.Applications.ApplicationIdentity(appId, path);
        var applications = GlassDock.Core.Applications.DockApplicationCollection.Combine(
            [new(new("Vendor.Product", @"C:\Apps\Product\Update.exe"), "Product", "p.lnk", null)],
            [new(identity, "Product", 1, 1, 1, true, null), new(identity, "Product", 2, 1, 1, false, null)]);
        var app = Assert.Single(applications);
        Assert.True(app.IsPinned);
        Assert.Equal(2, app.Windows.Count);

        using var coordinator = new GlassDock.Core.Applications.BadgeCoordinator([
            new TestBadgeProvider(new Dictionary<string, GlassDock.Core.Applications.BadgeSignal>(StringComparer.OrdinalIgnoreCase)
            {
                ["Vendor.Product"] = GlassDock.Core.Applications.BadgeSignal.Counted(3)
            })
        ]);
        var badge = coordinator.ForApplication(app.Identity);
        Assert.Equal(GlassDock.Core.Applications.BadgeKind.Count, badge.Kind);
        Assert.Equal(3, badge.Count);
    }

    [Fact]
    public void Exact_executable_path_resolves_start_menu_aumid()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "Vendor.Product",
                @"C:\Apps\Product\Product.exe",
                null,
                @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Product.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Apps\Product\Product.exe",
            hints);

        Assert.Equal("Vendor.Product", result);
    }

    [Fact]
    public void Ambiguous_exact_path_does_not_guess_between_app_ids()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint("Vendor.Product.ProfileA", @"C:\Apps\Product\Product.exe", null, "a.lnk"),
            new ShellApplicationIdentityHint("Vendor.Product.ProfileB", @"C:\Apps\Product\Product.exe", null, "b.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Apps\Product\Product.exe",
            hints);

        Assert.Null(result);
    }

    [Fact]
    public void Squirrel_process_start_shortcut_resolves_running_electron_executable()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "com.squirrel.Discord.Discord",
                @"C:\Users\Test\AppData\Local\Discord\Update.exe",
                "--processStart Discord.exe",
                @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Discord Inc\Discord.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Users\Test\AppData\Local\Discord\app-1.0.9257\Discord.exe",
            hints);

        Assert.Equal("com.squirrel.Discord.Discord", result);
    }

    [Fact]
    public void Quoted_process_start_and_wait_is_supported()
    {
        var hint = new ShellApplicationIdentityHint(
            "Vendor.Editor",
            @"C:\Users\Test\AppData\Local\Editor\Update.exe",
            "--processStartAndWait \"Editor App.exe\"",
            "Editor.lnk");

        Assert.True(ShellApplicationIdentityResolver.MatchesProcessStartShortcut(
            hint,
            @"C:\Users\Test\AppData\Local\Editor\app-2.0.0\Editor App.exe"));
    }

    [Fact]
    public void Process_start_shortcut_cannot_claim_executable_outside_its_app_root()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "Vendor.Product",
                @"C:\Users\Test\AppData\Local\Product\Update.exe",
                "--processStart Product.exe",
                "Product.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Other\Product\app-1.0\Product.exe",
            hints);

        Assert.Null(result);
    }

    [Fact]
    public void Ambiguous_squirrel_matches_do_not_guess()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "Vendor.Product.One",
                @"C:\Users\Test\AppData\Local\Product\Update.exe",
                "--processStart Product.exe",
                "One.lnk"),
            new ShellApplicationIdentityHint(
                "Vendor.Product.Two",
                @"C:\Users\Test\AppData\Local\Product\Update.exe",
                "--processStart Product.exe",
                "Two.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Users\Test\AppData\Local\Product\app-1.0\Product.exe",
            hints);

        Assert.Null(result);
    }

    [Fact]
    public void Squirrel_root_launcher_resolves_versioned_running_executable()
    {
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "com.squirrel.Discord.Discord",
                @"C:\Users\Test\AppData\Local\Discord\Discord.exe",
                null,
                @"C:\Users\Test\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Discord.lnk")
        };

        var result = ShellApplicationIdentityResolver.Resolve(
            @"C:\Users\Test\AppData\Local\Discord\app-1.0.9260\Discord.exe",
            hints);

        Assert.Equal("com.squirrel.Discord.Discord", result);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\AppData\Local\Discord\other\Discord.exe")]
    [InlineData(@"C:\Users\Test\AppData\Local\Discord\app-1.0.9260\resources\Discord.exe")]
    [InlineData(@"C:\Users\Test\AppData\Local\Other\app-1.0.9260\Discord.exe")]
    [InlineData(@"C:\Users\Test\AppData\Local\Discord\app-1.0.9260\Other.exe")]
    public void Squirrel_root_launcher_does_not_claim_unrelated_paths(string runningPath)
    {
        var hint = new ShellApplicationIdentityHint(
            "com.squirrel.Discord.Discord",
            @"C:\Users\Test\AppData\Local\Discord\Discord.exe",
            null,
            "Discord.lnk");

        Assert.False(ShellApplicationIdentityResolver.MatchesVersionedChildShortcut(
            hint,
            runningPath));
    }

    [Fact]
    public void Ambiguous_squirrel_root_launchers_do_not_guess()
    {
        var path = @"C:\Users\Test\AppData\Local\Product\app-1.0\Product.exe";
        var hints = new[]
        {
            new ShellApplicationIdentityHint(
                "Vendor.Product.One",
                @"C:\Users\Test\AppData\Local\Product\Product.exe",
                null,
                "One.lnk"),
            new ShellApplicationIdentityHint(
                "Vendor.Product.Two",
                @"C:\Users\Test\AppData\Local\Product\Product.exe",
                null,
                "Two.lnk")
        };

        Assert.Null(ShellApplicationIdentityResolver.Resolve(path, hints));
    }
    private sealed class TestBadgeProvider : GlassDock.Core.Applications.IBadgeProvider
    {
        public TestBadgeProvider(IReadOnlyDictionary<string, GlassDock.Core.Applications.BadgeSignal> snapshot) => Snapshot = snapshot;
        public string Id => "test";
        public int Priority => 100;
        public string Status => "Ready.";
        public IReadOnlyDictionary<string, GlassDock.Core.Applications.BadgeSignal> Snapshot { get; }
        public event EventHandler? Changed { add { } remove { } }
        public event EventHandler? RefreshRequested { add { } remove { } }
        public Task StartAsync(bool requestPermission = false) => Task.CompletedTask;
        public Task RefreshAsync() => Task.CompletedTask;
        public void Stop() { }
        public void Dispose() { }
    }

}
