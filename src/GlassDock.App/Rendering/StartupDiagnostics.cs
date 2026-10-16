namespace GlassDock.App.Rendering;

internal static class StartupDiagnostics
{
    private static int performanceWritePending;
    // Opt-in diagnostics must not put disk I/O on the dock's UI thread.
    internal static void WritePerformance(string summary)
    {
        if (!GlassDock.Core.Desktop.DockPerformanceDiagnostics.Enabled ||
            Interlocked.Exchange(ref performanceWritePending, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("DOKY_PERF_LOG");
                if (string.IsNullOrWhiteSpace(path))
                    path = Path.Combine(Path.GetTempPath(), $"Doky-performance-{Environment.ProcessId}.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {summary}\n");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
            { System.Diagnostics.Debug.WriteLine(error.Message); }
            finally { Interlocked.Exchange(ref performanceWritePending, 0); }
        });
    }

    internal static void Write(string stage, Exception? error = null)
    {
        try
        {
            var folder = GlassDock.Windows.Settings.DokyUserData.DirectoryPath;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "startup.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {stage} {error}\n");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
