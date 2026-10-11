namespace MSFSBlindAssist.Services.Geography;

/// <summary>
/// Spoken download progress: each milestone (25, 50, 75 percent by default) once, never
/// backwards, never 100 (finishing has its own sentence). A download resumed from a partial
/// file calls <see cref="Restart"/> first, so the milestones it already passed stay quiet.
/// </summary>
internal sealed class DownloadProgressGate
{
    private readonly int _step;
    private int _lastSpoken;

    internal DownloadProgressGate(int stepPercent = 25) => _step = Math.Clamp(stepPercent, 1, 99);

    /// <summary>The milestone to speak now, or null.</summary>
    internal int? Observe(long done, long total)
    {
        int milestone = WholePercent(done, total) / _step * _step;
        if (milestone <= _lastSpoken || milestone >= 100) return null;
        _lastSpoken = milestone;
        return milestone;
    }

    /// <summary>Starts counting from <paramref name="done"/> without speaking the milestones before it.</summary>
    internal void Restart(long done, long total) =>
        _lastSpoken = WholePercent(done, total) / _step * _step;

    internal static int WholePercent(long done, long total) =>
        total <= 0 ? 0 : (int)Math.Clamp(done * 100 / total, 0, 100);
}

internal enum StallVerdict
{
    Flowing,
    /// <summary>No bytes for <see cref="StallDetector.AnnounceAfter"/>: speak the stall once.</summary>
    AnnounceStall,
    /// <summary>Still stalled, already announced.</summary>
    Stalled,
    /// <summary>No bytes for <see cref="StallDetector.AbortAfter"/>: cancel this attempt and resume.</summary>
    Abort,
}

/// <summary>
/// Watches a download for silence. A stalled body read never returns on its own, so the
/// downloader polls <see cref="Check"/> on a timer and cancels the attempt on
/// <see cref="StallVerdict.Abort"/>; the retry resumes from the partial file.
/// </summary>
internal sealed class StallDetector
{
    internal static readonly TimeSpan AnnounceAfter = TimeSpan.FromSeconds(20);
    internal static readonly TimeSpan AbortAfter = TimeSpan.FromSeconds(60);

    private DateTime _lastBytesUtc;
    private bool _announced;

    internal StallDetector(DateTime nowUtc) => _lastBytesUtc = nowUtc;

    /// <summary>Records data. True when it ends a stall that was announced ("Download resumed").</summary>
    internal bool OnBytes(DateTime nowUtc)
    {
        _lastBytesUtc = nowUtc;
        bool wasAnnounced = _announced;
        _announced = false;
        return wasAnnounced;
    }

    internal StallVerdict Check(DateTime nowUtc)
    {
        var silent = nowUtc - _lastBytesUtc;
        if (silent >= AbortAfter) return StallVerdict.Abort;
        if (silent < AnnounceAfter) return StallVerdict.Flowing;
        if (_announced) return StallVerdict.Stalled;
        _announced = true;
        return StallVerdict.AnnounceStall;
    }

    /// <summary>A new attempt starts the clock again; an announced stall stays announced until data flows.</summary>
    internal void Restart(DateTime nowUtc) => _lastBytesUtc = nowUtc;

    internal bool StallAnnounced => _announced;
}

/// <summary>
/// How many failed attempts in a row a download may have. Only attempts that brought no real
/// progress count, so a slow line that keeps moving never uses up its retries.
/// </summary>
internal sealed class RetryBudget
{
    internal const int MaxNoProgressRetries = 5;
    /// <summary>An attempt that brought at least this much counts as progress.</summary>
    internal const long ProgressBytes = 1024 * 1024;

    private static readonly int[] BackoffSeconds = { 2, 4, 8, 16, 30 };
    private int _noProgress;

    /// <summary>Records a failed attempt; true while another attempt is allowed.</summary>
    internal bool AttemptFailed(long bytesThisAttempt)
    {
        _noProgress = bytesThisAttempt >= ProgressBytes ? 1 : _noProgress + 1;
        return _noProgress <= MaxNoProgressRetries;
    }

    /// <summary>1-based retry number, for "Retrying, attempt 2 of 5".</summary>
    internal int RetryNumber => _noProgress;

    internal TimeSpan Backoff => TimeSpan.FromSeconds(BackoffSeconds[Math.Clamp(_noProgress - 1, 0, BackoffSeconds.Length - 1)]);
}

public enum ResumeOutcome
{
    /// <summary>No partial file: a normal full download.</summary>
    Fresh,
    /// <summary>The server sent exactly the missing tail of the same file.</summary>
    Append,
    /// <summary>The file on the server is not the one the partial file came from: start over.</summary>
    RestartChanged,
    /// <summary>The partial file already holds the whole file.</summary>
    AlreadyComplete,
    /// <summary>Any other answer: an error to report.</summary>
    Fail,
}

/// <summary>
/// Reads the server's answer to a resumed request. The request carries <c>If-Range</c> with the
/// validator saved when the partial file was started, so a server that rebuilt the file in the
/// meantime (GeoNames rebuilds its dump daily) answers 200 with the new file instead of a 206
/// tail that would splice two different files together.
/// </summary>
internal static class ResumeDecision
{
    internal static ResumeOutcome Decide(int statusCode, long partLength,
        long? contentRangeFrom, long? contentRangeTotal, long? knownTotal)
    {
        switch (statusCode)
        {
            case 200:
                return partLength > 0 ? ResumeOutcome.RestartChanged : ResumeOutcome.Fresh;
            case 206:
                if (partLength > 0 && contentRangeFrom == partLength
                    && (knownTotal is null || contentRangeTotal == knownTotal))
                    return ResumeOutcome.Append;
                return ResumeOutcome.RestartChanged;
            case 416:
                return knownTotal is long total && partLength == total
                    ? ResumeOutcome.AlreadyComplete
                    : ResumeOutcome.RestartChanged;
            default:
                return ResumeOutcome.Fail;
        }
    }
}

/// <summary>
/// Free disk space the download and build need at their peak, on the drive holding the
/// databases folder. The live database (during an update) is already on disk and is not
/// counted; it is replaced only after the new one is complete.
/// </summary>
internal static class DiskSpaceRequirement
{
    // Measured on 2026-10-10 with the full dump (8.36 M places): Natural Earth 63 MB on disk,
    // staging peaked at 372 MB, the packed database is 332 MB. Rounded up for growth.
    /// <summary>Natural Earth GeoJSON as stored on disk (decompressed).</summary>
    internal const long NaturalEarthBytes = 70L * 1024 * 1024;
    /// <summary>The unsorted staging database the importer writes first.</summary>
    internal const long StagingBytes = 420L * 1024 * 1024;
    /// <summary>The clustered copy that becomes the live database.</summary>
    internal const long BuiltBytes = 380L * 1024 * 1024;
    internal const long MarginBytes = 300L * 1024 * 1024;

    /// <summary>Bytes needed before starting (the rest of the download plus the build).</summary>
    internal static long BeforeDownload(long remainingDownloadBytes) =>
        Math.Max(0, remainingDownloadBytes) + NaturalEarthBytes + StagingBytes + BuiltBytes + MarginBytes;

    /// <summary>Bytes needed once the downloads are on disk, checked again before building.</summary>
    internal static long BeforeBuild() => StagingBytes + BuiltBytes + MarginBytes;

    /// <summary>"1.4 gigabytes" / "850 megabytes", for speech and the status box.</summary>
    internal static string Spoken(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1000
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{mb / 1024.0:0.0} gigabytes")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(mb):0} megabytes");
    }
}
