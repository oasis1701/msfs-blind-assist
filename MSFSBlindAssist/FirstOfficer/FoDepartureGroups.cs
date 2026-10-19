namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Which checklist groups belong to the departure — power-up through After Takeoff — across
/// every First Officer profile, so an in-flight start (InFlightStartDetector) can mark them
/// complete (owner, 2026-10-14: "Through After Takeoff"); Descent onwards are the arrival and
/// never touched. FoDepartureGroupsTests fails on any profile group in neither set.
/// </summary>
public static class FoDepartureGroups
{
    /// <summary>Spoken once, queued, when an in-flight start marks the departure lists.</summary>
    public const string Announcement = "In flight: checklists through After Takeoff marked complete.";

    public static readonly IReadOnlySet<string> Departure = new HashSet<string>(StringComparer.Ordinal)
    {
        "ELEC_POWER_UP", "POWER_UP", "COCKPIT_PREP", "COCKPIT_PREP_CL", "COCKPIT_ENTRY_CL",
        "PREFLIGHT", "PREFLIGHT_CL", "BEFORE_START", "BEFORE_START_CL", "ENGINE_START", "ENGINE_START_CL",
        "AFTER_START", "AFTER_START_CL", "BEFORE_TAXI", "BEFORE_TAXI_CL", "TAXI", "TAXI_CL",
        "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL", "BEFORE_TKOF_CL", "LINEUP", "LINEUP_CL",
        "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "AFTER_TKOF_CL",
        // The COWS DA40: its XLS's flooded and reprime starts.
        "ENGINE_START_HOT", "ENGINE_START_REPRIME",
    };

    public static readonly IReadOnlySet<string> Arrival = new HashSet<string>(StringComparer.Ordinal)
    {
        "DESCENT", "DESCENT_CL", "APPROACH", "APPROACH_CL", "BEFORE_LANDING", "BEFORE_LANDING_CL",
        "LANDING", "LANDING_CL", "AFTER_LANDING", "AFTER_LANDING_CL", "PARKING", "PARKING_CL",
        "SHUTDOWN", "SHUTDOWN_CL", "SECURE", "SECURE_CL", "SECURING_CL", "ELEC_POWER_DOWN",
        // The COWS DA40's cruise: past After Takeoff, so never marked by an in-flight start.
        "CRUISE", "CRUISE_CL",
    };

    /// <summary>Reference pages, not checklists (the COWS DA40's weights and speeds and its tips):
    /// nothing to tick, so an in-flight start never marks them.</summary>
    public static readonly IReadOnlySet<string> Reference = new HashSet<string>(StringComparer.Ordinal)
    {
        "DA40_WEIGHTS_SPEEDS", "DA40_TIPS",
    };

    public static bool IsDeparture(string groupId) => Departure.Contains(groupId);

    /// <summary>Marks every departure group <paramref name="mgr"/> has complete and latched, as a
    /// finished flow does (MarkGroupComplete, nothing excluded). Returns how many.</summary>
    public static int MarkComplete<TExec, TState>(ChecklistManager<TExec, TState> mgr)
        where TExec : IFoActionExecutor
        where TState : IFoStateEvaluator
    {
        var ids = mgr.Groups.Select(g => g.Id).Where(IsDeparture).ToList();
        foreach (var id in ids)
            mgr.MarkGroupComplete(id);
        return ids.Count;
    }
}
