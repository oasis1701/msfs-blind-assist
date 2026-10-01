using System;
using System.Collections.Generic;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// The three failures that are a SEVERITY rather than a state, and were silent because of it.
///
/// Ninety-four failure variables are defined on this aeroplane and eighty-five of them speak
/// the moment the model raises one — they are flags, so the ordinary announcer handles them.
/// Six more are the reset buttons and have no state to speak.
///
/// ⚠️ THE REMAINING THREE ARE PERCENTAGES, AND PERCENTAGES DO NOT SPEAK. A coolant leak, a
/// turbocharger failure and a boost leak are all graded 0 to 100 on this engine, so the
/// numeric-silence rule that keeps oil temperature quiet was keeping THEM quiet too — and
/// they are three of the most serious things that can happen to a turbo-diesel. A pilot would
/// have learned about a coolant leak from the temperature climbing, eventually, if they
/// happened to be scanning.
///
/// So the ONSET is announced, and a material WORSENING after it. Not the value: a leak that
/// ramps from 30 to 31 percent is not news, and announcing every percent would bury the one
/// that mattered. Nothing is said when it returns to zero — the only way that happens is the
/// pilot resetting failures, and the Reset button already confirms itself.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>How much worse it has to get before it is worth saying again.</summary>
    private const double GradedFailureStep = 25;

    /// <summary>
    /// Key to what it is called and the factor that turns its RAW value into a percentage.
    ///
    /// ⚠️ THE THREE FAILURES ARE 0-TO-1 FACTORS IN THE AEROPLANE (the panel writes the typed
    /// percentage divided by 100), while the filter builds to 100. The onset and worsening
    /// tests are in percent, so a raw 0.35 coolant leak never passed "0.5" and could never be
    /// announced.
    /// </summary>
    private static readonly Dictionary<string, (string What, double ToPercent)> GradedFailures = new(StringComparer.Ordinal)
    {
        ["DA40_FAIL_COOLANT_LEAK_SET"] = ("Coolant leak", 100),
        ["DA40_FAIL_TURBO_SET"] = ("Turbocharger failure", 100),
        ["DA40_FAIL_BOOST_LEAK_SET"] = ("Boost leak", 100),

        // Not a failure, but exactly the same shape: a percentage that climbs, whose ONSET
        // is the news and whose value is not. The DA40 is not approved for flight into
        // known icing, and its induction filter blocks with ice unless alternate air is
        // open - so this is the aeroplane telling the pilot to open it.
        ["DA40_ICE_FILTER"] = ("Induction filter icing", 1)
    };

    /// <summary>Exposed for the tests, which check every one exists and is polled.</summary>
    public static IReadOnlyCollection<string> GradedFailureKeys => GradedFailures.Keys;

    private readonly Dictionary<string, double> _gradedSpoken = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns true for a graded failure, which keeps it off the generic path — the generic
    /// announcer would either say nothing (it is a number) or say everything (every percent).
    /// </summary>
    private bool NoteGradedFailure(string varKey, double value, ScreenReaderAnnouncer announcer)
    {
        if (!GradedFailures.TryGetValue(varKey, out var graded)) return false;

        double pct = value * graded.ToPercent;
        double? last = _gradedSpoken.TryGetValue(varKey, out double l) ? l : null;
        string? call = GradedFailureCall(graded.What, last, pct, out bool record);
        if (record) _gradedSpoken[varKey] = pct;

        if (call != null && !Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(varKey))
            announcer.AnnounceImmediate(call);
        return true;
    }

    /// <summary>
    /// What one reading of a graded failure says, in PERCENT. A first reading is a baseline,
    /// never an onset (connecting to an aeroplane that already has a leak must not announce it
    /// as new); a return to zero is recorded silently (only the pilot's reset clears one, and
    /// the Reset button confirms itself); otherwise the onset, and every 25 points worse.
    /// </summary>
    internal static string? GradedFailureCall(string what, double? lastPct, double pct, out bool record)
    {
        record = true;
        if (lastPct is null || pct < 0.5) return null;

        bool onset = lastPct.Value < 0.5;
        bool worse = pct >= lastPct.Value + GradedFailureStep;
        if (!onset && !worse) { record = false; return null; }

        return onset ? $"{what}, {pct:0} percent" : $"{what} worsening, {pct:0} percent";
    }
}
