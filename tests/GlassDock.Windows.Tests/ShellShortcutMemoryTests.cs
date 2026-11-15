using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GlassDock.Windows.Applications;
using Xunit;

namespace GlassDock.Windows.Tests;

[Collection(NativeDesktopTestCollection.Name)]
public sealed class ShellShortcutMemoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(259)]
    [InlineData(260)]
    [InlineData(519)]
    [InlineData(520)]
    [InlineData(4096)]
    [InlineData(32767)]
    public void Shortcut_text_preserves_unicode_and_buffer_boundaries(int length)
    {
        var arguments = length == 0 ? "" : new string('x', length - 1) + "界";
        WithShortcut(arguments, (link, target, directory) =>
        {
            var resolved = ShellApplicationMetadata.ResolveLink(link);
            Assert.Equal(target, resolved.Path);
            Assert.Equal(arguments, resolved.Arguments);
            Assert.Equal(directory, resolved.WorkingDirectory);
        });
    }

    [Fact]
    public void Repeated_shortcut_reads_do_not_allocate_maximum_path_buffers()
    {
        WithShortcut("--fixture", (link, _, _) =>
        {
            _ = ShellApplicationMetadata.ResolveLink(link);
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var read = 0; read < 16; read++)
                _ = ShellApplicationMetadata.ResolveLink(link);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.InRange(allocated, 1, 512 * 1024);
        });
    }

    private static void WithShortcut(string arguments, Action<string, string, string> verify)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "doky-shortcut-memory-" + Guid.NewGuid().ToString("N"));
            var target = Path.Combine(directory, "app.exe");
            var link = Path.Combine(directory, "app.lnk");
            object? shell = null, shortcut = null, native = null;
            try
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(target, "fixture");
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(link);
                ((dynamic)shortcut).TargetPath = target;
                ((dynamic)shortcut).WorkingDirectory = directory;
                ((dynamic)shortcut).Save();
                native = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!);
                ((IPersistFile)native!).Load(link, 0);
                ((ApplicationNative.IShellLink)native).SetArguments(arguments);
                ((IPersistFile)native).Save(link, true);
                verify(link, target, directory);
            }
            catch (Exception error) { failure = error; }
            finally
            {
                if (native is not null) Marshal.ReleaseComObject(native);
                if (shortcut is not null) Marshal.ReleaseComObject(shortcut);
                if (shell is not null) Marshal.ReleaseComObject(shell);
                File.Delete(link);
                File.Delete(target);
                Directory.Delete(directory);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        Assert.Null(failure);
    }
}
