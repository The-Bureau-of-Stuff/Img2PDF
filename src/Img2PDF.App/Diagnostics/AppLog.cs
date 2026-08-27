namespace Img2PDF.App.Diagnostics;

/// <summary>
/// Local-only diagnostic log — never transmitted anywhere (see privacy-policy: no network
/// calls, no telemetry). Purely so a user hitting a bug has something concrete to attach to a
/// support email instead of "it just closed".
/// </summary>
public static class AppLog
{
    // Same base folder as AppSettings ("ClickTo PDF" under LocalApplicationData) and same
    // reasoning for using Environment.SpecialFolder over Windows.Storage.ApplicationData —
    // that throws when running unpackaged.
    public static readonly string LogDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickTo PDF", "logs");

    private const int MaxLogFilesToKeep = 14;

    private static readonly object WriteLock = new();

    public static void LogError(string context, Exception ex) => Write("ERROR", context, ex.ToString());

    public static void LogWarning(string context, string message) => Write("WARN", context, message);

    // Best-effort, like AppSettings.Save — logging itself must never be the thing that crashes
    // the app, or throws during an unhandled-exception handler and masks the original error.
    private static void Write(string level, string context, string detail)
    {
        try
        {
            lock (WriteLock)
            {
                Directory.CreateDirectory(LogDirectoryPath);
                string path = Path.Combine(LogDirectoryPath, $"{DateTime.Now:yyyy-MM-dd}.log");
                string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {context}: {detail}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>One file per day; call once at startup so the log folder doesn't grow forever.</summary>
    public static void PruneOldLogs()
    {
        try
        {
            if (!Directory.Exists(LogDirectoryPath))
            {
                return;
            }

            IEnumerable<FileInfo> stale = new DirectoryInfo(LogDirectoryPath)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(MaxLogFilesToKeep);

            foreach (FileInfo file in stale)
            {
                file.Delete();
            }
        }
        catch (Exception)
        {
        }
    }
}
