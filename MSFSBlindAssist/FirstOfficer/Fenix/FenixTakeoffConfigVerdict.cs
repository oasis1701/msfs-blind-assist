namespace MSFSBlindAssist.FirstOfficer.Fenix;

/// <summary>One fresh read of both master lights, timed from the observed press.</summary>
public readonly record struct TakeoffConfigSample(TimeSpan SincePress, double? Warning, double? Caution);

/// <summary>
/// What a Fenix ECAM TO CONFIG test says, from the master warning and master caution; the First Officer
/// and the panel's TO CONFIG button speak the same words. The Fenix exposes no TO-CONFIG-only indicator
/// (live L:var search, 2026-07), so the shared lights are the signal. Fenix v3's FWC delays MASTER
/// WARN/CAUT after the button (Fenix V3 changelog), so one cached read at 1.5 s could say "normal" over a
/// bad config. The verdict is now a watch of fresh reads from the observed press, still fail-safe: any
/// light lit is a "check", an unread light is "no result" and never "normal", and there is no pre-press
/// baseline. The caution counts too: an amber takeoff-config fault lights the master caution, not the
/// warning, and a late warning outranks it.
/// </summary>
public static class FenixTakeoffConfigVerdict
{
    public const string MasterWarningKey = "I_MIP_MASTER_WARNING_CAPT";
    public const string MasterCautionKey = "I_MIP_MASTER_CAUTION_CAPT";

    public const string ButtonKey = "S_ECAM_TO";
    public const string ButtonFreshKey = "S_ECAM_TO_READOUT";
    public const string MasterWarningFreshKey = "I_MIP_MASTER_WARNING_CAPT_READOUT";
    public const string MasterCautionFreshKey = "I_MIP_MASTER_CAUTION_CAPT_READOUT";

    public static readonly IReadOnlyList<string> FreshKeys =
        new[] { ButtonFreshKey, MasterWarningFreshKey, MasterCautionFreshKey };

    /// <summary>The warning stays lit about 1 s after release (measured 2026-10-21).</summary>
    public const int HoldMs = 1500;
    /// <summary>The slowest measured press-to-master-warning time on Fenix 3.0.0.7426 (1,015 ms) plus a 1,000 ms margin, rounded up to 100.</summary>
    public const int VerdictAfterMs = 2100;
    public const int PollMs = 200;
    public const int ReadTimeoutMs = 500;
    public static readonly TimeSpan PanelPressWait = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan FirstOfficerPressWait = TimeSpan.FromSeconds(30);

    public static string Decide(IReadOnlyList<TakeoffConfigSample> samples, TimeSpan verdictAfter)
    {
        if (samples.Any(s => s.Warning >= 0.5)) return Text(1, null);
        if (samples.Any(s => s.Caution >= 0.5)) return Text(0, 1);
        bool settled = samples.Any(s => s.SincePress >= verdictAfter
            && s.Warning is double w && !double.IsNaN(w) && s.Caution is double c && !double.IsNaN(c));
        return settled ? Text(0, 0) : Text(null, null);
    }

    /// <param name="cancel">Ends the watch at its next read with "no result" (the First Officer cancels it when
    /// its press was never sent); never throws.</param>
    public static Task<string> WatchAsync(Func<string, Task<double?>> readFresh, TimeSpan pressWait,
        CancellationToken cancel = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        return WatchAsync(readFresh, pressWait, TimeSpan.FromMilliseconds(VerdictAfterMs),
            () => sw.Elapsed, d => Task.Delay(d), cancel);
    }

    internal static async Task<string> WatchAsync(Func<string, Task<double?>> readFresh, TimeSpan pressWait,
        TimeSpan verdictAfter, Func<TimeSpan> clock, Func<TimeSpan, Task> delay, CancellationToken cancel = default)
    {
        var poll = TimeSpan.FromMilliseconds(PollMs);
        var start = clock();
        TimeSpan? t0 = null;
        bool sawReleased = false;
        while (clock() - start < pressWait)
        {
            if (cancel.IsCancellationRequested) return Text(null, null);
            var t = clock();
            double? b = await readFresh(ButtonFreshKey);
            if (b is double bv && !double.IsNaN(bv))
            {
                if (bv < 0.5) sawReleased = true;
                else if (sawReleased) { t0 = t; break; }
            }
            await delay(poll);
        }
        if (t0 is not TimeSpan press) return Text(null, null);

        var samples = new List<TakeoffConfigSample>();
        var cutoff = verdictAfter + TimeSpan.FromMilliseconds(4 * PollMs);
        while (true)
        {
            if (cancel.IsCancellationRequested) return Text(null, null);
            var t = clock();
            double? w = await readFresh(MasterWarningFreshKey);
            double? c = await readFresh(MasterCautionFreshKey);
            samples.Add(new TakeoffConfigSample(t - press, w, c));
            bool read = w is double wv && !double.IsNaN(wv) && c is double cv && !double.IsNaN(cv);
            if (w >= 0.5) return Decide(samples, verdictAfter);
            if (t - press >= verdictAfter && read) return Decide(samples, verdictAfter);
            if (clock() - press >= cutoff) return Decide(samples, verdictAfter);
            await delay(poll);
        }
    }

    public static string Text(double? masterWarning, double? masterCaution)
    {
        if (masterWarning >= 0.5) return "Takeoff config: check configuration.";
        if (masterCaution >= 0.5) return "Takeoff config: caution. Check the ECAM.";
        if (masterWarning is not double w || double.IsNaN(w) || masterCaution is not double c || double.IsNaN(c))
            return "Takeoff config: no result read. Check the ECAM.";
        return "Takeoff config normal.";
    }
}
