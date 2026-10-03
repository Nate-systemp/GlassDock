using GlassDock.Core.Applications;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

public sealed class FileDropTests
{
    [Fact]
    public void Custom_shortcut_preserves_target_arguments_and_working_directory()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "Doky shortcut " + Guid.NewGuid());
            Directory.CreateDirectory(root);
            object? shell = null, shortcut = null;
            try
            {
                var exe = Path.Combine(root, "Editor.exe");
                var file = Path.Combine(root, "sample.txt");
                var link = Path.Combine(root, "Custom.lnk");
                File.WriteAllText(exe, ""); File.WriteAllText(file, "");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(link);
                ((dynamic)shortcut).TargetPath = exe;
                ((dynamic)shortcut).Arguments = "--profile personal";
                ((dynamic)shortcut).WorkingDirectory = root;
                ((dynamic)shortcut).Save();
                var identity = new ApplicationIdentity("Vendor.Editor", exe, "stale ignored");
                var app = new DockApplication(identity.Key, identity, "Editor", link, true, [], null);
                var launch = WindowsApplicationLauncher.PrepareFileLaunch(app, [file]);
                Assert.NotNull(launch);
                Assert.Equal(exe, launch.Target);
                Assert.Equal(root, launch.WorkingDirectory);
                Assert.Equal("--profile personal " + WindowsApplicationLauncher.QuoteArgument(file), launch.Arguments);
                // A document shortcut is not an application and must never execute its document.
                ((dynamic)shortcut).TargetPath = file;
                ((dynamic)shortcut).Save();
                Assert.Null(WindowsApplicationLauncher.PrepareFileLaunch(app, [file]));
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (shortcut is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                if (shell is not null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
                Directory.Delete(root, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        Assert.Null(failure);
    }

    [Fact]
    public void Files_and_folder_keep_spaces_unicode_and_existing_arguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "Doky drop " + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var exe = Path.Combine(root, "App.exe");
            var first = Path.Combine(root, "report one.txt");
            var second = Path.Combine(root, "日本語.txt");
            File.WriteAllText(exe, ""); File.WriteAllText(first, ""); File.WriteAllText(second, "");
            var identity = new ApplicationIdentity("Vendor.Editor", exe, "--profile custom");
            foreach (var pinned in new[] { true, false })
            {
                var app = new DockApplication(identity.Key, identity, "Editor", pinned ? exe : null, pinned, [], null);
                var result = WindowsApplicationLauncher.PrepareFileLaunch(app, [first, second, root, first]);
                Assert.NotNull(result);
                Assert.Equal(exe, result.Target);
                Assert.Equal("--profile custom " + string.Join(" ", new[] { first, second, root }.Select(WindowsApplicationLauncher.QuoteArgument)), result.Arguments);
                Assert.NotNull(WindowsApplicationLauncher.PrepareFileLaunch(app, [first]));
                Assert.Null(WindowsApplicationLauncher.PrepareFileLaunch(app, [Path.Combine(root, "missing.txt")]));
                Assert.Null(WindowsApplicationLauncher.PrepareFileLaunch(app, []));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("shell:AppsFolder\\Vendor.App!Main")]
    [InlineData("C:\\Files\\document.txt")]
    [InlineData("https://example.com")]
    public void Unsupported_targets_do_not_advertise_file_activation(string target)
    {
        var identity = new ApplicationIdentity("Vendor.App!Main", target);
        Assert.False(WindowsApplicationLauncher.CanOpenWith(new(identity.Key, identity, "App", target, true, [], null)));
    }

    [Theory]
    [InlineData("C:\\folder with spaces\\", "\"C:\\folder with spaces\\\\\"")]
    [InlineData("C:\\日本語.txt", "C:\\日本語.txt")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    public void Quotes_windows_arguments_without_shell_interpretation(string value, string expected) =>
        Assert.Equal(expected, WindowsApplicationLauncher.QuoteArgument(value));
}
