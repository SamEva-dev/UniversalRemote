using System.Diagnostics;
using System.Text;

namespace UniversalRemote.Maui;

/// <summary>
/// Best-effort startup diagnostics. It must never throw: diagnostics must not become another crash source.
/// </summary>
internal static class StartupDiagnostics
{
    private static int installed;
    private static readonly object Sync = new();

    public static string LogPath
    {
        get
        {
            try { return Path.Combine(FileSystem.AppDataDirectory, "startup-diagnostics.log"); }
            catch { return "startup-diagnostics.log"; }
        }
    }

    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) != 0) return;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Record("AppDomain.UnhandledException", ex);
            else
                Record("AppDomain.UnhandledException", args.ExceptionObject?.ToString() ?? "Unknown exception");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Record("TaskScheduler.UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    public static void Record(string stage, Exception exception)
        => Record(stage, exception.ToString());

    public static void Record(string stage, string details)
    {
        try
        {
            var line = new StringBuilder()
                .AppendLine("----------------------------------------")
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" | ")
                .AppendLine(stage)
                .AppendLine(details)
                .ToString();

            Debug.WriteLine(line);
            lock (Sync)
            {
                File.AppendAllText(LogPath, line);
            }
        }
        catch
        {
            // Never fail application startup because diagnostics could not be persisted.
        }
    }
}
