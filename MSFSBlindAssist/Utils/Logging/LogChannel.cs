using System;

namespace MSFSBlindAssist.Utils.Logging;

/// <summary>A handle to one named log file (e.g. "taxi_guidance.log"). The channel's base name is its category tag.</summary>
public sealed class LogChannel
{
    private readonly string _fileName; // e.g. "taxi_guidance.log"
    private readonly string _category; // e.g. "taxi_guidance"
    internal LogChannel(string fileName, string category) { _fileName = fileName; _category = category; }

    public void Debug(string message)                 => Log.Emit(_fileName, LogLevel.Debug, _category, message, null);
    public void Info(string message)                  => Log.Emit(_fileName, LogLevel.Info,  _category, message, null);
    public void Warn(string message)                  => Log.Emit(_fileName, LogLevel.Warn,  _category, message, null);
    public void Error(string message, Exception? ex = null) => Log.Emit(_fileName, LogLevel.Error, _category, message, ex);
}
