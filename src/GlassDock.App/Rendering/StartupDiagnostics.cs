namespace GlassDock.App.Rendering;

internal static class StartupDiagnostics
{
    internal static void Write(string stage, Exception? error = null)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDock");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "startup.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {stage} {error}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
