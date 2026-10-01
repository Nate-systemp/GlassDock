namespace GlassDock.Windows.Settings;

/// <summary>Physical-file exclusion works across packaged and ordinary object namespaces.</summary>
public sealed class DokyInstanceLease : IDisposable
{
    private readonly FileStream stream;
    private DokyInstanceLease(FileStream stream) => this.stream = stream;
    public static DokyInstanceLease? TryAcquire(string? directory = null)
    {
        directory ??= DokyUserData.DirectoryPath;
        Directory.CreateDirectory(directory);
        try
        {
            return new(new FileStream(Path.Combine(directory, "runtime.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33) { return null; }
    }
    // Keep the empty lock file: deleting it introduces a release/acquire race.
    public void Dispose() => stream.Dispose();
}
