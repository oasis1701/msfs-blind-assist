namespace MSFSBlindAssist.Navigation;

/// <summary>
/// What the Select Destination Runway window opens with, so the pilot doesn't have to
/// type an airport the app already knows.
/// </summary>
/// <param name="Icao">Airport to put in the ICAO box, upper-case. Null leaves the box empty.</param>
/// <param name="RunwayId">Runway to highlight once the list loads, or null.</param>
public sealed record DestinationPrefill(string? Icao, string? RunwayId)
{
    public static readonly DestinationPrefill None = new(null, null);

    /// <summary>
    /// The flight plan's arrival airport wins (it describes the flight being flown now; a
    /// destination left over from the previous leg of a turnaround does not). Otherwise
    /// the destination already set. The runway is only carried over when it belongs to the
    /// airport chosen, so a stale runway from a different airport is never highlighted.
    /// </summary>
    public static DestinationPrefill Choose(
        string? flightPlanArrivalIcao, string? flightPlanArrivalRunway,
        string? currentDestinationIcao, string? currentDestinationRunway)
    {
        string? planIcao = Clean(flightPlanArrivalIcao);
        string? currentIcao = Clean(currentDestinationIcao);

        string? icao = planIcao ?? currentIcao;
        if (icao == null) return None;

        // Same airport on both sides: the runway the pilot already picked is the more
        // recent choice, so it beats the plan's.
        string? runway = null;
        if (currentIcao == icao) runway = Clean(currentDestinationRunway);
        if (runway == null && planIcao == icao) runway = Clean(flightPlanArrivalRunway);

        return new DestinationPrefill(icao, runway);
    }

    /// <summary>True when a runway id from the list matches the prefill (ignores case and a "RW" prefix).</summary>
    public bool MatchesRunway(string? runwayId)
    {
        string? want = NormalizeRunway(RunwayId);
        return want != null && want == NormalizeRunway(runwayId);
    }

    private static string? Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim().ToUpperInvariant();
    }

    private static string? NormalizeRunway(string? s)
    {
        string? r = Clean(s);
        if (r == null) return null;
        if (r.StartsWith("RW")) r = r.Substring(2);
        // "06L" and "6L" are the same runway.
        r = r.TrimStart('0');
        return r.Length == 0 ? null : r;
    }
}
