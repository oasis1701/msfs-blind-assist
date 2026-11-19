using System;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Route-start helpers for <c>TaxiGuidanceManager.LoadRoute</c>: does the route just built
/// actually begin where the aircraft is standing, and which stand would the pilot have meant?
/// <para>
/// Normally yes — the start node snaps to the aircraft or its first cleared taxiway.
/// But when the DESTINATION sits on a disconnected section of the taxiway network,
/// the component filter (the GCLP S5 defence — correct, keep it) snaps the whole
/// route onto that island: LMML 2026-08-31, live — the P/N/NW aprons are a separate
/// island 192 m of bare, path-less apron away from the B..G network an arriving
/// aircraft is on, so a route to "Parking 1" was a 58 m stub beginning 480 m from
/// the aircraft, the steering tone pointed across the empty apron, the pilot
/// followed it into the middle of the airfield, and nothing said anything was wrong.
/// That case is now routed across the apron (TaxiRouter.FindCrossComponentPath) or refused by
/// RouteReachability; what remains here is the far-start warning threshold and the stand-label
/// parser the "did you mean" suggestion uses. Warnings never block the route from loading.
/// </para>
/// </summary>
public static class RouteStartGate
{
    /// <summary>Start gap beyond which "the route begins X away" is worth a warning.</summary>
    public const double FAR_START_WARN_M = 150.0;

    // "Parking 1 - Ramp GA Large" → prefix "Parking", number 1, suffix "".
    // The label's descriptor tail begins at the first SPACED dash — same convention
    // as NormalizeParkingName (a bare hyphen is part of the stand name, "A-9").
    private static readonly Regex StandLabelRegex = new(
        @"^(?<prefix>.*?)\s*(?<num>\d+)\s*(?<suffix>[A-Za-z]?)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses the ramp-name prefix, stand number, and one-letter suffix out of a gate
    /// destination label ("Southeast 22 - Ramp GA Medium" → "Southeast", 22, "").
    /// False when the label carries no number (the island suggestion then has no key
    /// to search other ramps by).
    /// </summary>
    public static bool TryParseStandNumber(
        string destinationName, out string prefix, out int number, out string suffix)
    {
        prefix = string.Empty;
        number = 0;
        suffix = string.Empty;
        if (string.IsNullOrWhiteSpace(destinationName)) return false;

        string head = destinationName;
        int dash = head.IndexOf(" - ", StringComparison.Ordinal);
        if (dash >= 0) head = head.Substring(0, dash);
        head = head.Trim();

        var m = StandLabelRegex.Match(head);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups["num"].Value, out number) || number <= 0) return false;
        prefix = m.Groups["prefix"].Value.Trim();
        suffix = m.Groups["suffix"].Value.Trim().ToUpperInvariant();
        return true;
    }
}
