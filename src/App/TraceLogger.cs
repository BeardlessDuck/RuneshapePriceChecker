using System;
using System.IO;

namespace RuneshapePriceChecker.Startup;

public static class TraceLogger
{
    private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash-trace.txt");
    private static readonly object SyncRoot = new object();
    private static StreamWriter? _writer;

    public static void Log(string message)
    {
        try
        {
            lock (SyncRoot)
            {
                if (_writer == null)
                {
                    _writer = new StreamWriter(new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));
                    _writer.AutoFlush = true;
                }
                _writer.WriteLine($"{DateTime.UtcNow:O} [{Environment.CurrentManagedThreadId}] {message}");
            }
        }
        catch { }
    }
}
