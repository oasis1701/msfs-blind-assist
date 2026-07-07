using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>The single logging entry point. Log.Debug/Info/Warn/Error write the app-wide debug.log;
/// Log.Channel(name) returns a handle to a dedicated named file. Formatting+enqueue only — one
/// background thread does all disk I/O, so callers never block and logging never adds hot-path latency.</summary>
public static class Log
{
    private const string DebugFile = "debug.log";
    // Created eagerly so calls BEFORE Init() still enqueue; Init() starts the drain thread.
    private static readonly LogWriter Writer = new(MSFSBlindAssist.Utils.AppLogs.PathFor);
    private static readonly ConcurrentDictionary<string, LogChannel> Channels = new(StringComparer.OrdinalIgnoreCase);
    private static int _started;

    public static void Init()
    {
        if (System.Threading.Interlocked.Exchange(ref _started, 1) != 0) return;
        Writer.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Writer.DrainSynchronously();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Writer.DrainSynchronously();
    }

    public static void Shutdown() => Writer.Shutdown(TimeSpan.FromSeconds(2));

    public static void Debug(string category, string message) { System.Diagnostics.Debug.WriteLine(message); Emit(DebugFile, LogLevel.Debug, category, message, null); }
    public static void Info (string category, string message) => Emit(DebugFile, LogLevel.Info,  category, message, null);
    public static void Warn (string category, string message) => Emit(DebugFile, LogLevel.Warn,  category, message, null);
    public static void Error(string category, string message, Exception? ex = null) => Emit(DebugFile, LogLevel.Error, category, message, ex);

    /// <summary>Handle to a dedicated named file. `name` may be given with or without ".log"; a bare name gets ".log".</summary>
    public static LogChannel Channel(string name, bool truncateOnLaunch = false)
        => Channels.GetOrAdd(name, n =>
        {
            bool hasExt = n.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
            string file = hasExt ? n : n + ".log";
            string category = System.IO.Path.GetFileNameWithoutExtension(file);
            if (truncateOnLaunch) Writer.RegisterTruncateOnLaunch(file);
            return new LogChannel(file, category);
        });

    internal static void Emit(string fileName, LogLevel level, string category, string message, Exception? ex)
    {
        try { Writer.Enqueue(new LogEntry(fileName, LogFormatter.Format(DateTime.Now, level, category, message, ex))); }
        catch { /* logging must never throw into the caller */ }
    }
}
