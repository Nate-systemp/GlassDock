using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

[Collection(NativeDesktopTestCollection.Name)]
public sealed class PinRecoveryTests
{
    [Fact]
    public void Shortcut_with_deleted_target_is_recoverable_and_checked_on_sta()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "doky-shortcut-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "app.exe");
            var link = Path.Combine(folder, "app.lnk");
            object? shell = null, shortcut = null;
            try
            {
                File.WriteAllText(target, "fixture");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(link);
                ((dynamic)shortcut).TargetPath = target;
                ((dynamic)shortcut).Save();
                Assert.Equal(PinTargetAvailability.Available, PinTargetProbe.Check(link, new(null, target)));
                File.Delete(target);
                Assert.Equal(PinTargetAvailability.Unavailable, PinTargetProbe.Check(link, new(null, target)));
                Assert.True(File.Exists(link));
                File.WriteAllText(target, "recovered");
                Assert.Equal(PinTargetAvailability.Available, PinTargetProbe.Check(link, new(null, target)));
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (shortcut is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                if (shell is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                File.Delete(link); File.Delete(target); Directory.Delete(folder);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void Disconnected_drive_is_unknown_not_removed()
    {
        var drives = DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
        var letter = Enumerable.Range('D', 'Z' - 'D' + 1).Select(value => (char)value).FirstOrDefault(c => !drives.Contains(c));
        if (letter == default) return; // No unused drive letter on this host.
        Assert.Equal(PinTargetAvailability.Unknown, PinTargetProbe.Check($"{letter}:\\offline\\app.exe", new(null, null)));
    }

    [Fact]
    public void Missing_stack_member_has_visible_artwork_and_explicit_removal_dissolves_singleton()
    {
        var path = Path.Combine(Path.GetTempPath(), "doky-stack-recovery-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            DockApplication App(string name) => new("shell:" + name.ToUpperInvariant(),
                new(null, null, ShellPath: name), name, "https://example.test/" + name, true, [], null);
            var first = App("first"); var second = App("second");
            var store = new DockPinStore(path);
            Assert.True(store.MergeStack(first.Id, second.Id, [first, second]));
            var restoredStore = new DockPinStore(path);
            var apps = DockApplicationCollection.Combine(restoredStore.Apply([], new WindowsApplicationIconService()), []);
            var stack = Assert.Single(restoredStore.ApplyStacks(apps));
            Assert.Equal(2, stack.StackApps.Count);
            Assert.All(stack.StackApps, member => Assert.True(ApplicationIconFallback.HasArtwork(member.Icon)));
            Assert.True(restoredStore.Set(apps.Single(app => app.Id == first.Id), false));
            var after = new DockPinStore(path);
            var remaining = Assert.Single(after.ApplyStacks(DockApplicationCollection.Combine(after.Apply([], new WindowsApplicationIconService()), [])));
            Assert.Equal(second.Id, remaining.Id);
            Assert.Null(remaining.Stack);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("removed.exe")]
    [InlineData("removed.lnk")]
    [InlineData("removed-folder")]
    public void Missing_local_resources_are_unavailable_not_a_deletion_instruction(string name)
    {
        var target = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), name);
        Assert.Equal(PinTargetAvailability.Unavailable, PinTargetProbe.Check(target, new(null, target)));
    }

    [Fact]
    public void Url_shortcut_is_available_without_probing_its_remote_destination()
    {
        var target = Path.Combine(Path.GetTempPath(), "doky-url-" + Guid.NewGuid().ToString("N") + ".url");
        try
        {
            File.WriteAllText(target, "[InternetShortcut]\nURL=https://offline.example.test/\n");
            Assert.Equal(PinTargetAvailability.Available, PinTargetProbe.Check(target, new(null, null)));
        }
        finally { File.Delete(target); }
    }

    [Theory]
    [InlineData("https://example.test/app")]
    [InlineData("shell:AppsFolder\\Example!App")]
    [InlineData("\\\\offline-server\\share\\app.exe")]
    public void Opaque_or_offline_targets_are_never_classified_as_permanently_removed(string target)
    {
        Assert.Equal(PinTargetAvailability.Unknown, PinTargetProbe.Check(target, new(null, null)));
    }

    [Fact]
    public void Packaged_identity_is_not_treated_as_a_missing_executable()
    {
        Assert.Equal(PinTargetAvailability.Unknown,
            PinTargetProbe.Check(@"C:\missing\app.exe", new("Package!App", null)));
    }

    [Fact]
    public void Deleted_pin_survives_restart_and_explicit_unpin_persists_without_deleting_targets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "doky-pin-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, "app.txt");
        var preferences = Path.Combine(folder, "pins.json");
        try
        {
            File.WriteAllText(target, "test target");
            Assert.Equal(PinTargetAvailability.Available, PinTargetProbe.Check(target, new(null, target)));
            var app = new DockApplication("exe:" + ApplicationIdentity.NormalizePath(target) + "|",
                new(null, target), "Test pin", target, false, [], null);
            Assert.True(new DockPinStore(preferences).Set(app, true));
            File.Delete(target);
            Assert.Equal(PinTargetAvailability.Unavailable, PinTargetProbe.Check(target, app.Identity));
            var store = new DockPinStore(preferences);
            var pins = store.Apply([], new WindowsApplicationIconService());
            var restored = Assert.Single(DockApplicationCollection.Combine(pins, []));
            Assert.Equal(app.Id, restored.Id);
            Assert.True(ApplicationIconFallback.HasArtwork(restored.Icon));
            Assert.True(store.Set(restored, false));
            Assert.Empty(new DockPinStore(preferences).Apply([], new WindowsApplicationIconService()));
            File.WriteAllText(target, "recovered");
            Assert.Equal(PinTargetAvailability.Available, PinTargetProbe.Check(target, app.Identity));
            Assert.Equal("recovered", File.ReadAllText(target));
        }
        finally
        {
            File.Delete(target); File.Delete(preferences); Directory.Delete(folder);
        }
    }
}
