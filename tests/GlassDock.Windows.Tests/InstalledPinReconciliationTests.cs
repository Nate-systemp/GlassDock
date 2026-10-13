using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

[Collection(NativeDesktopTestCollection.Name)]
public sealed class InstalledPinReconciliationTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "doky-uninstall-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    private InstalledPinSnapshot snapshot = new(true, []);
    public InstalledPinReconciliationTests() => Directory.CreateDirectory(folder);
    private InstalledPinReconciler Monitor() => new(() => snapshot, () => now);
    private PinnedApplication Pin(string name = "app", bool exists = true)
    {
        var path = Path.Combine(folder, name + ".exe");
        if (exists) File.WriteAllText(path, "fixture");
        return new(new(null, path), name, path, new(1, 1, [255, 0, 0, 255]));
    }
    private void Installed(PinnedApplication pin, string id = "registered") => snapshot = new(true, [new(id, pin.LaunchTarget, null)]);
    private void Advance() => now += TimeSpan.FromSeconds(31);

    [Fact]
    public void Cached_icon_does_not_prevent_confirmed_removal_and_restart_persistence()
    {
        var pin = Pin(); Installed(pin);
        var path = Path.Combine(folder, "pins.json");
        var store = new DockPinStore(path, Monitor());
        var icons = new WindowsApplicationIconService();
        Assert.Single(store.Apply([pin], icons)); // imported pin's evidence is persisted too
        File.Delete(pin.LaunchTarget); snapshot = new(true, []); Advance();
        store = new DockPinStore(path, Monitor()); // uninstall while Doky is stopped
        var pending = Assert.Single(store.Apply([pin], icons));
        Assert.NotSame(pin.Icon, pending.Icon);
        Advance(); Assert.Empty(store.Apply([pin], icons));
        Assert.Empty(new DockPinStore(path, Monitor()).Apply([pin], icons));
    }

    [Fact]
    public void Portable_deleted_executable_is_retained_with_fallback_even_with_cached_artwork()
    {
        var pin = Pin(); var store = new DockPinStore(Path.Combine(folder, "pins.json"), Monitor());
        var icons = new WindowsApplicationIconService();
        Assert.Single(store.Apply([pin], icons)); File.Delete(pin.LaunchTarget); Advance();
        var unavailable = Assert.Single(store.Apply([pin], icons));
        Assert.Equal(PinTargetAvailability.Unavailable, unavailable.Availability);
        Assert.NotSame(pin.Icon, unavailable.Icon);
        Advance(); Assert.Single(store.Apply([pin], icons));
        File.WriteAllText(pin.LaunchTarget, "recovered"); Advance();
        Assert.Same(pin.Icon, Assert.Single(store.Apply([pin], icons)).Icon);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Incomplete_scan_or_replacement_registration_never_deletes(bool replacement)
    {
        var pin = Pin(); Installed(pin); var monitor = Monitor();
        var baseline = monitor.Inspect([pin], new Dictionary<string, PinInstallationEvidence>()).Evidence;
        File.Delete(pin.LaunchTarget); Advance();
        snapshot = replacement ? new(true, [new("replacement", pin.LaunchTarget, null)]) : new(false, []);
        Assert.Empty(monitor.Inspect([pin], baseline).Removed);
        Advance(); Assert.Empty(monitor.Inspect([pin], baseline).Removed);
    }

    [Fact]
    public void Install_location_matches_direct_executable_but_not_another_nested_product()
    {
        var root = Path.Combine(folder, "shared-vendor");
        var registration = new InstalledPinRegistration("removed-msi", null, root + Path.DirectorySeparatorChar,
            MsiRemoved: true);
        Assert.True(registration.Matches(Path.Combine(root, "application.exe")));
        Assert.False(registration.Matches(Path.Combine(root, "another-product", "other.exe")));
    }

    [Fact]
    public void Removed_vendor_registration_must_not_delete_a_different_product_in_a_subfolder()
    {
        var root = Path.Combine(folder, "vendor");
        var executable = Path.Combine(root, "other-product", "other.exe");
        var pin = new PinnedApplication(new(null, executable), "other", executable, null);
        snapshot = new(true, [new("removed-vendor-msi", null, root + Path.DirectorySeparatorChar,
            MsiRemoved: true)]);
        var monitor = Monitor();
        Assert.Empty(monitor.Inspect([pin], new Dictionary<string, PinInstallationEvidence>()).Removed);
        Advance();
        Assert.Empty(monitor.Inspect([pin], new Dictionary<string, PinInstallationEvidence>()).Removed);
    }

    [Fact]
    public void Msi_leftover_registration_can_confirm_absence_without_old_baseline()
    {
        var pin = Pin(exists: false);
        snapshot = new(true, [new("msi", pin.LaunchTarget, null, MsiRemoved: true)]);
        var monitor = Monitor(); var baseline = new Dictionary<string, PinInstallationEvidence>();
        Assert.Empty(monitor.Inspect([pin], baseline).Removed);
        Advance(); Assert.Contains(pin.Identity.Key, monitor.Inspect([pin], baseline).Removed);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\QMK Toolbox\\Uninstall QMK Toolbox.exe\" /S", @"C:\Program Files\QMK Toolbox\Uninstall QMK Toolbox.exe")]
    [InlineData(@"C:\Program Files\QMK Toolbox\Uninstall QMK Toolbox.exe /S", @"C:\Program Files\QMK Toolbox\Uninstall QMK Toolbox.exe")]
    public void Installer_uninstall_command_extracts_local_executable(string command, string expected)
    {
        Assert.Equal(expected, InstalledPinReconciler.UninstallerExecutable(command));
        Assert.Null(InstalledPinReconciler.UninstallerExecutable(@"msiexec.exe /X {00000000-0000-0000-0000-000000000000}"));
    }

    [Fact]
    public void Uninstaller_folder_without_matching_product_name_cannot_own_a_pin()
    {
        var executable = Path.Combine(folder, "QMK Toolbox.exe");
        var unrelated = new InstalledPinRegistration("other", null, null, DisplayName: "Different Product",
            UninstallerPath: Path.Combine(folder, "Uninstall Different Product.exe"));
        Assert.False(unrelated.Matches(executable, "QMK Toolbox"));
        var matched = unrelated with { DisplayName = "QMK Toolbox" };
        Assert.True(matched.Matches(executable, "QMK Toolbox"));
        Assert.False(matched.Matches(Path.Combine(folder, "other-app", "QMK Toolbox.exe"), "QMK Toolbox"));
    }

    [Fact]
    public void New_pin_forces_fresh_installer_snapshot_instead_of_older_cached_scan()
    {
        var reads = 0;
        var pin = Pin("new-registration");
        var monitor = new InstalledPinReconciler(() => { reads++; return snapshot; }, () => now);
        Assert.Empty(monitor.Inspect([], new Dictionary<string, PinInstallationEvidence>()).Removed);
        Assert.Equal(1, reads);
        snapshot = new(true, [new("installed", pin.LaunchTarget, null)]);
        var evidence = monitor.Inspect([pin], new Dictionary<string, PinInstallationEvidence>()).Evidence;
        Assert.Equal(2, reads);
        Assert.Equal("installed", evidence[pin.Identity.Key].RegistrationId);
    }

    [Fact]
    public void Qmk_style_shortcut_with_registered_uninstaller_removes_after_confirmed_uninstall()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = Pin("QMK Toolbox");
                snapshot = new(true, [new("qmk-registration", null, null,
                    DisplayName: "QMK Toolbox",
                    UninstallerPath: Path.Combine(folder, "Uninstall QMK Toolbox.exe"))]);
                var link = CreateDesktopShortcut(Path.Combine(folder, "QMK Toolbox.lnk"), application.LaunchTarget);
                var saved = Path.Combine(folder, "qmk-style-pins.json");
                var store = new DockPinStore(saved, Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService();
                var pinned = Assert.Single(store.Apply([], icons));
                Assert.Equal(PinTargetAvailability.Available, pinned.Availability);
                Assert.Contains("qmk-registration", File.ReadAllText(saved));

                // Windows Settings uninstall deletes the desktop link and the
                // executable, and removes the registered uninstaller record.
                File.Delete(link);
                File.Delete(application.LaunchTarget);
                snapshot = new(true, []);
                Advance();
                Assert.Empty(store.Apply([], icons)); // Hidden while removal is being confirmed.
                Assert.Single(store.GetRecoverablePins());
                Advance();
                Assert.Empty(store.Apply([], icons));
                Assert.Empty(store.GetRecoverablePins()); // Confirmed uninstall is removed from saved pins.
                Assert.Empty(new DockPinStore(saved, Monitor()).Apply([], icons));
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void Surviving_executable_vetoes_removal_even_if_registration_is_gone()
    {
        var pin = Pin(); Installed(pin); var monitor = Monitor();
        var baseline = monitor.Inspect([pin], new Dictionary<string, PinInstallationEvidence>()).Evidence;
        snapshot = new(true, []); Advance(); Assert.Empty(monitor.Inspect([pin], baseline).Removed);
        Advance(); Assert.Empty(monitor.Inspect([pin], baseline).Removed);
    }

    [Theory]
    [InlineData("https://example.test/app")]
    [InlineData(@"\\offline\share\app.exe")]
    [InlineData("folder")]
    [InlineData("app.url")]
    public void Non_application_targets_are_never_removed(string target)
    {
        if (target == "folder") { target = Path.Combine(folder, target); Directory.CreateDirectory(target); }
        if (target == "app.url") { target = Path.Combine(folder, target); File.WriteAllText(target, "[InternetShortcut]\nURL=https://example.test"); }
        var pin = new PinnedApplication(new(null, target), "keep", target, null);
        var monitor = Monitor(); var baseline = new Dictionary<string, PinInstallationEvidence> { [pin.Identity.Key] = new("gone", target) };
        Assert.Empty(monitor.Inspect([pin], baseline).Removed); Advance(); Assert.Empty(monitor.Inspect([pin], baseline).Removed);
    }

    [Fact]
    public void Automatic_removal_compacts_stack_and_preserves_surviving_order()
    {
        var pin = Pin(); var keep = Pin("keep"); var last = Pin("last"); Installed(pin);
        var store = new DockPinStore(Path.Combine(folder, "pins.json"), Monitor());
        var icons = new WindowsApplicationIconService();
        var apps = DockApplicationCollection.Combine(store.Apply([pin, keep, last], icons), []);
        Assert.True(store.MergeStack(pin.Identity.Key, keep.Identity.Key, apps));
        var dock = new DockApplications(); dock.Apply(new(store.ApplyStacks(apps)));
        File.Delete(pin.LaunchTarget); snapshot = new(true, []); Advance(); store.Apply([pin, keep, last], icons);
        Advance(); apps = DockApplicationCollection.Combine(store.Apply([pin, keep, last], icons), []);
        var layout = store.ApplyStacks(apps); dock.Apply(new(layout));
        Assert.Equal(new[] { keep.Identity.Key, last.Identity.Key }, dock.VisibleDockApplications.Select(item => item.Id));
        Assert.All(layout, app => Assert.Null(app.Stack));
        var restored = new DockPinStore(Path.Combine(folder, "pins.json"), Monitor());
        Assert.Equal(new[] { keep.Identity.Key, last.Identity.Key }, restored.ApplyStacks(
            DockApplicationCollection.Combine(restored.Apply([pin, keep, last], icons), [])).Select(app => app.Id));
    }

    [Fact]
    public void Leftover_desktop_shortcut_is_not_deleted_but_its_confirmed_uninstalled_target_pin_is()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            object? shell = null, shortcut = null;
            try
            {
                var pin = Pin(); Installed(pin);
                var link = Path.Combine(folder, "desktop.lnk");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(link);
                ((dynamic)shortcut).TargetPath = pin.LaunchTarget; ((dynamic)shortcut).Save();
                var store = new DockPinStore(Path.Combine(folder, "pins.json"), Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService(); Assert.Single(store.Apply([], icons));
                File.Delete(pin.LaunchTarget); snapshot = new(true, []); Advance(); store.Apply([], icons);
                Advance(); Assert.Empty(store.Apply([], icons)); Assert.True(File.Exists(link));
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (shortcut is not null) MarshalRelease(shortcut);
                if (shell is not null) MarshalRelease(shell);
            }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }

    [Fact]
    public void Deleted_desktop_shortcut_keeps_installed_app_but_removes_confirmed_uninstalled_target()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = Pin("removed-desktop-app");
                Installed(application);
                var link = CreateDesktopShortcut(Path.Combine(folder, "removed-desktop-app.lnk"), application.LaunchTarget);
                var saved = Path.Combine(folder, "removed-desktop-pins.json");
                var store = new DockPinStore(saved, Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService();
                Assert.Single(store.Apply([], icons)); // Remember the verified installer association.

                // The shortcut disappears, but the executable survives: never
                // unpin merely because a shortcut was deleted or the registry changed.
                File.Delete(link);
                snapshot = new(true, []);
                Advance();
                Assert.Empty(store.Apply([], icons));
                Assert.Single(store.GetRecoverablePins()); // Still saved; executable survives.
                Advance();
                Assert.Empty(store.Apply([], icons));
                Assert.Single(store.GetRecoverablePins());

                // Once the executable is also missing, removal is confirmed from
                // the original installer association even though the .lnk is gone.
                File.Delete(application.LaunchTarget);
                Advance();
                Assert.Empty(store.Apply([], icons));
                Assert.Single(store.GetRecoverablePins());
                Advance();
                Assert.Empty(store.Apply([], icons));
                Assert.Empty(store.GetRecoverablePins());
                Assert.Empty(new DockPinStore(saved, Monitor()).Apply([], icons));
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void Deleted_shortcut_without_earlier_installer_evidence_is_recoverable_not_auto_removed()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var application = Pin("unknown-portable-app");
                // No known installed-product registration was ever matched.
                var link = CreateDesktopShortcut(Path.Combine(folder, "unknown-portable-app.lnk"), application.LaunchTarget);
                var store = new DockPinStore(Path.Combine(folder, "unknown-portable-pins.json"), Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService();
                Assert.Single(store.Apply([], icons));

                File.Delete(link);
                File.Delete(application.LaunchTarget);
                Advance();
                Assert.Empty(store.Apply([], icons));
                Advance();
                Assert.Empty(store.Apply([], icons));
                var retained = Assert.Single(store.GetRecoverablePins());
                Assert.True(retained.Hidden);
                Assert.Contains("unknown-portable-app", File.ReadAllText(Path.Combine(folder, "unknown-portable-pins.json")));
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void Missing_custom_shortcut_hides_from_dock_but_restores_when_target_returns()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Pin("recoverable-desktop");
                var link = CreateDesktopShortcut(Path.Combine(folder, "recoverable-desktop.lnk"), app.LaunchTarget);
                var saved = Path.Combine(folder, "recoverable-pins.json");
                var store = new DockPinStore(saved, Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService();
                Assert.Single(store.Apply([], icons));

                File.Delete(link);
                Assert.Empty(store.Apply([], icons));
                Assert.True(Assert.Single(store.GetRecoverablePins()).Hidden);
                var restarted = new DockPinStore(saved, Monitor());
                Assert.Empty(restarted.Apply([], icons));
                Assert.True(Assert.Single(restarted.GetRecoverablePins()).Hidden);

                CreateDesktopShortcut(link, app.LaunchTarget);
                Assert.Single(store.Apply([], icons));
                Assert.Empty(store.GetRecoverablePins());
                Assert.Single(restarted.Apply([], icons));
                Assert.Empty(restarted.GetRecoverablePins());
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }

    [Fact]
    public void Hidden_stack_member_does_not_reserve_an_empty_slot_and_returns_when_restored()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var hiddenApp = Pin("stack-missing");
                var keep = Pin("stack-kept");
                var link = CreateDesktopShortcut(Path.Combine(folder, "stack-missing.lnk"), hiddenApp.LaunchTarget);
                var store = new DockPinStore(Path.Combine(folder, "stack-pins.json"), Monitor());
                Assert.True(store.AddExternalTarget(link));
                Assert.True(store.AddExternalTarget(keep.LaunchTarget));
                var icons = new WindowsApplicationIconService();
                var initial = DockApplicationCollection.Combine(store.Apply([], icons), []);
                Assert.Equal(2, initial.Count);
                Assert.True(store.MergeStack(initial[0].Id, initial[1].Id, initial));
                Assert.Single(store.ApplyStacks(initial));

                File.Delete(link);
                var visible = DockApplicationCollection.Combine(store.Apply([], icons), []);
                Assert.Single(visible);
                var layout = store.ApplyStacks(visible);
                Assert.Single(layout);
                Assert.Equal(keep.Identity.Key, layout[0].Id);
                Assert.Null(layout[0].Stack); // No blank card or ghost stack.
                Assert.Single(store.GetRecoverablePins());

                CreateDesktopShortcut(link, hiddenApp.LaunchTarget);
                var restored = DockApplicationCollection.Combine(store.Apply([], icons), []);
                Assert.Equal(2, restored.Count);
                Assert.Single(store.ApplyStacks(restored)); // Saved stack membership survives.
                Assert.Empty(store.GetRecoverablePins());
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }

    [Fact]
    public void Unavailable_imported_shortcut_is_not_hidden_or_added_to_recovery_list()
    {
        var link = Path.Combine(folder, "windows-imported.lnk");
        var imported = new PinnedApplication(new(null, link), "Windows imported pin", link, null);
        var store = new DockPinStore(Path.Combine(folder, "imported.json"), Monitor());
        var icons = new WindowsApplicationIconService();
        var pending = Assert.Single(store.Apply([imported], icons));
        Assert.Equal(PinTargetAvailability.Unavailable, pending.Availability);
        Assert.Empty(store.GetRecoverablePins());
    }

    [Fact]
    public void Hidden_shortcut_can_be_shown_hidden_and_forgotten_from_settings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = Pin("settings-recovery");
                var link = CreateDesktopShortcut(Path.Combine(folder, "settings-recovery.lnk"), app.LaunchTarget);
                var saved = Path.Combine(folder, "settings-recovery.json");
                var store = new DockPinStore(saved, Monitor());
                Assert.True(store.AddExternalTarget(link));
                var icons = new WindowsApplicationIconService();
                Assert.Single(store.Apply([], icons));
                File.Delete(link);
                Assert.Empty(store.Apply([], icons));
                var id = Assert.Single(store.GetRecoverablePins()).Id;

                Assert.True(store.SetMissingPinVisible(id, true));
                Assert.False(Assert.Single(store.GetRecoverablePins()).Hidden);
                Assert.Single(store.Apply([], icons)); // Deliberate show restores question-mark tile.
                var restarted = new DockPinStore(saved, Monitor());
                Assert.Single(restarted.Apply([], icons)); // Explicit preference survives restart.

                Assert.True(store.SetMissingPinVisible(id, false));
                Assert.Empty(store.Apply([], icons));
                Assert.True(Assert.Single(store.GetRecoverablePins()).Hidden);
                Assert.True(store.RemoveRecoverablePin(id));
                Assert.Empty(store.GetRecoverablePins());
                Assert.Empty(store.Apply([], icons));
                Assert.Empty(new DockPinStore(saved, Monitor()).Apply([], icons));
                Assert.False(File.Exists(link)); // Never changes the user's files.
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); Assert.True(thread.Join(TimeSpan.FromSeconds(15))); Assert.Null(failure);
    }

    private static string CreateDesktopShortcut(string linkPath, string executable)
    {
        object? shell = null, shortcut = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)shell!).CreateShortcut(linkPath);
            ((dynamic)shortcut).TargetPath = executable;
            ((dynamic)shortcut).Save();
            return linkPath;
        }
        finally
        {
            if (shortcut is not null) MarshalRelease(shortcut);
            if (shell is not null) MarshalRelease(shell);
        }
    }

    private static void MarshalRelease(object instance) => System.Runtime.InteropServices.Marshal.ReleaseComObject(instance);
    public void Dispose() => Directory.Delete(folder, true);
}
