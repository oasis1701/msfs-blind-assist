using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Utils.Logging;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The 757's flight mode annunciator as a reader of <see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>: its layouts
/// from the shared module session's builds, a <see cref="Bbs757FmaReader"/> per located build on its own lease, which
/// reads memory alone (no simulator input), no power state, and the 737's words (<see cref="Bbs757FmaSpeech"/>). A value
/// with no word is logged once per column and value on the reader's own log, the first time a change sees it. Called on
/// the UI thread only.
/// </summary>
public sealed class Bbs757FmaDisplay : IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>
{
    public const string Name = "FMA";
    public const string MuteKey = "MSFSBA_BBS757_FMA";
    public const string LogChannel = "bbs757-fma";
    public const int TickMs = 100;

    /// <summary>The Ctrl+M row's label.</summary>
    public const string MonitorRowLabel = "Flight Mode Annunciator";

    private readonly Action<string> _log;
    private readonly HashSet<(string Column, int Value)> _loggedNoWord = new();

    /// <summary>Logs to <see cref="LogChannel"/> (CORE-14/15).</summary>
    public Bbs757FmaDisplay()
        : this(null)
    {
    }

    /// <param name="log">Where the reader's own lines go; null for <see cref="LogChannel"/>'s named log.</param>
    internal Bbs757FmaDisplay(Action<string>? log) => _log = log ?? Log.Channel(LogChannel).Info;

    string IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>.Name => Name;

    string IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>.MuteKey => MuteKey;

    string IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>.LogChannel => LogChannel;

    int IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading>.TickMs => TickMs;

    /// <summary>The distinct FMA layouts of the analyzed builds, in file order; a build without one is skipped (another
    /// reader's layout found it).</summary>
    public IReadOnlyList<Bbs757FmaLayout> LayoutsIn(ModuleAnalysis analysis)
    {
        var layouts = new List<Bbs757FmaLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Bbs757FmaLayout>(Bbs757Module.Fma) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        return layouts;
    }

    /// <summary>A sampler when <paramref name="module"/>'s build has <paramref name="layout"/>, holding its own lease of
    /// the shared simulator handle; else null. A handle closed meanwhile throws <see cref="ObjectDisposedException"/>,
    /// which the monitor counts as "not found yet".</summary>
    public IBbs757DisplaySampler<Bbs757FmaReading>? SamplerFor(LocatedModule module, Bbs757FmaLayout layout)
    {
        if (!layout.Equals(module.Build.Layout<Bbs757FmaLayout>(Bbs757Module.Fma))) return null;
        return new Bbs757FmaReader(module.Memory, module.AcquireLease(), layout);
    }

    /// <summary>The FMA has no power state of its own.</summary>
    public bool? Lit(Bbs757FmaReading reading) => null;

    /// <summary>One line on the FMA's Ctrl+M row ("FMA: Roll: LOC, Pitch: G/S"; without "FMA: " after "resumed"), or none
    /// when nothing BlueBird draws changed.</summary>
    public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757FmaReading heard, Bbs757FmaReading now, bool withPrefix)
    {
        LogNoWord(now);
        string? change = Bbs757FmaSpeech.DescribeChange(heard, now);
        if (change is null) return Array.Empty<Bbs757SpokenLine>();
        return new[] { new Bbs757SpokenLine(MuteKey, withPrefix ? Bbs757FmaSpeech.ChangePrefix + change : change) };
    }

    public IReadOnlyList<string> Rows(Bbs757FmaReading reading) => Bbs757FmaSpeech.Rows(reading);

    /// <summary>Each value with no word, once per column and value (a word BlueBird added in an update).</summary>
    private void LogNoWord(Bbs757FmaReading reading)
    {
        foreach (var (column, value) in Bbs757FmaSpeech.Unrecognised(reading))
        {
            if (!_loggedNoWord.Add((column, value))) continue;
            try
            {
                _log(Invariant($"value with no word, spoken as unrecognised: {column} = {value}"));
            }
            catch
            {
                // The log itself failed: the change is still spoken.
            }
        }
    }
}
