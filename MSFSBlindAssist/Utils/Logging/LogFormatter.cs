using System;
using System.Globalization;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>Pure formatting of one log line. No I/O, no clock — timestamp is passed in so it is deterministically testable.</summary>
public static class LogFormatter
{
    public static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info  => "INFO ",
        LogLevel.Warn  => "WARN ",
        LogLevel.Error => "ERROR",
        _              => "?????",
    };

    public static string Format(DateTime tsLocal, LogLevel level, string category, string message, Exception? ex = null)
    {
        string ts  = tsLocal.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string cat = string.IsNullOrEmpty(category) ? "-" : category;
        string line = $"{ts} [{LevelTag(level)}] [{cat}] {message}";
        if (ex != null)
            line += Environment.NewLine + "    " + ex.ToString().Replace("\n", "\n    ");
        return line;
    }
}
