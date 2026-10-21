using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>The airport and runway the Landing Exit Planner opens with. Both null: it opens empty.</summary>
public readonly record struct LandingExitPlannerPresetResult(string? Icao, string? RunwayId);

/// <summary>
/// Which runway row the planner selects, and what to tell the pilot about it.
/// <paramref name="Index"/> is -1 when there are no runways to select from.
/// </summary>
public readonly record struct PresetRunwaySelection(int Index, bool PresetApplied, string? Notice);

/// <summary>
/// What the Landing Exit Planner pre-fills (PR #236 review, finding F11). The ILS destination wins
/// when one is set; otherwise the loaded flight plan's arrival airport and runway, when it names
/// both. With neither, the runway box used to fall back to the airport's first runway, and a plan
/// made against that default is how issue #234 began (OMDB: planned on 12L, landed on 30L).
///
/// <para>Pure — <c>LandingExitPlannerPresetTests</c>.</para>
/// </summary>
public static class LandingExitPlannerPreset
{
    public static LandingExitPlannerPresetResult Resolve(
        string? ilsIcao, string? ilsRunwayId,
        string? flightPlanArrivalIcao, string? flightPlanArrivalRunway)
    {
        if (!string.IsNullOrWhiteSpace(ilsIcao))
            return new LandingExitPlannerPresetResult(
                ilsIcao.Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(ilsRunwayId) ? null : ilsRunwayId.Trim());

        if (!string.IsNullOrWhiteSpace(flightPlanArrivalIcao) && !string.IsNullOrWhiteSpace(flightPlanArrivalRunway))
            return new LandingExitPlannerPresetResult(
                flightPlanArrivalIcao.Trim().ToUpperInvariant(), flightPlanArrivalRunway.Trim());

        return new LandingExitPlannerPresetResult(null, null);
    }

    /// <summary>
    /// The airport Select Destination Runway opens with: the preset's, else a flight plan's
    /// arrival airport even when the plan names no runway. The planner opens empty then (its
    /// runway would quietly default to the first one, issue #234); the destination window only
    /// needs the airport, and its list starts on the first runway whenever no runway is known.
    /// Never a different airport from the planner's, only one where the planner has none.
    /// </summary>
    public static string? DestinationWindowIcao(LandingExitPlannerPresetResult preset, string? flightPlanArrivalIcao)
        => preset.Icao
           ?? (string.IsNullOrWhiteSpace(flightPlanArrivalIcao) ? null : flightPlanArrivalIcao.Trim().ToUpperInvariant());

    /// <summary>
    /// The runway row a load selects, and whether the pilot needs telling about it.
    ///
    /// <para>Pre-filling the runway exists BECAUSE issue #234 began with the box quietly defaulting
    /// to the airport's first runway. It still did exactly that whenever the pre-filled runway was
    /// not in the list — and the box now tells the pilot it is "pre-filled from your ILS
    /// destination or flight plan", so they have more reason to trust what they see. Two database
    /// shapes cause it: the list leaves out runways the database marks closed, and a runway renamed
    /// for magnetic drift is spelled one way in the flight plan and another in the database.</para>
    /// </summary>
    public static PresetRunwaySelection SelectRunway(
        IReadOnlyList<string> runwayIds, string? presetRunwayId)
    {
        if (runwayIds == null || runwayIds.Count == 0)
            return new PresetRunwaySelection(-1, false, null);

        if (string.IsNullOrWhiteSpace(presetRunwayId))
            return new PresetRunwaySelection(0, false, null);

        for (int i = 0; i < runwayIds.Count; i++)
            if (DesignatorsMatch(runwayIds[i], presetRunwayId))
                return new PresetRunwaySelection(i, true, null);

        return new PresetRunwaySelection(0, false,
            // Worded for both windows that share this selector (the Landing Exit Planner and
            // Select Destination Runway), so no "plan an exit" here.
            $"Runway {presetRunwayId.Trim()} is not in this airport's runway list. " +
            $"Showing runway {runwayIds[0]} instead — check the runway before you continue.");
    }

    /// <summary>True when two runway designators name the same runway end after
    /// <see cref="RouteRunwayCrossings.NormalizeDesignator"/>: "09" equals "9", "30L" equals "30l".
    /// A blank designator matches nothing.</summary>
    public static bool DesignatorsMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        return string.Equals(RouteRunwayCrossings.NormalizeDesignator(a),
                             RouteRunwayCrossings.NormalizeDesignator(b),
                             StringComparison.Ordinal);
    }
}
