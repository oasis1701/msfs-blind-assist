namespace MSFSBlindAssist.Utils.Logging;

internal readonly struct LogEntry
{
    public readonly string FileName;
    public readonly string Line;
    public LogEntry(string fileName, string line) { FileName = fileName; Line = line; }
}
