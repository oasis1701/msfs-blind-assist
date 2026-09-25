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

    private static readonly Dictionary<string, string> GradedFailures = new(StringComparer.Ordinal)
    {
        ["DA40_FAIL_COOLANT_LEAK_SET"] = "Coolant leak",
        ["DA40_FAIL_TURBO_SET"] = "Turbocharger failure",
        ["DA40_FAIL_BOOST_LEAK_SET"] = "Boost leak",

        // Not a failure, but exactly the same shape: a percentage that climbs, whose ONSET
        // is the news and whose value is not. The DA40 is not approved for flight into
        // known icing, and its induction filter blocks with ice unless alternate air is
        // open - so this is the aeroplane telling the pilot to open it.
        ["DA40_ICE_FILTER"] = "Induction filter icing"
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
        if (!GradedFailures.TryGetValue(varKey, out string? what)) return false;

        // A first reading is not an onset. Without this, connecting to an aeroplane that
        // already has a leak announces it as though it had just happened — the same
        // baseline-first rule every other monitor here follows.
        if (!_gradedSpoken.ContainsKey(varKey))
        {
            _gradedSpoken[varKey] = value;
            return true;
        }

        double last = _gradedSpoken[varKey];

        // Cleared. Silent: the only thing that clears one is the pilot resetting failures,
        // and the Reset button says so itself.
        if (value < 0.5)
        {
            _gradedSpoken[varKey] = value;
            return true;
        }

        bool onset = last < 0.5;
        bool worse = value >= last + GradedFailureStep;
        if (!onset && !worse) return true;

        _gradedSpoken[varKey] = value;

        if (Settings.SettingsManager.Current.DA40DisabledMonitorVariablesSet.Contains(varKey))
        {
            return true;
        }

        announcer.AnnounceImmediate(onset
            ? $"{what}, {value:0} percent"
            : $"{what} worsening, {value:0} percent");
        return true;
    }
}
