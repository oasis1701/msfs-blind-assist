namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The landing-exit list the PILOT reads: one row per physical turnoff.
///
/// <see cref="TaxiGraph.GetLandingExits"/> deliberately keeps every node where a taxiway
/// meets the runway on a runway with hold-short markers, and guidance depends on that full
/// list (the arc-end handoff, backtrack planning, retargeting and the vacate resolver all
/// read the siblings). But read aloud it is noise: KDTW 22L listed 57 exits — Y9 four
/// times, Z7 six, Z5 eight — and every copy of a name resolved to the SAME ApronNodeId, the
/// corridor walk's own statement that they are one turnoff.
///
/// So the collapse is for DISPLAY only; the rollout still gets the full list. Same name +
/// same apron node = one row. Genuinely separate same-named turnoffs (EGLL 09R S5W at 5659
/// and 6605 ft, KDTW 22L R left and right) reach different apron nodes and both stay. The
/// row kept is the threshold-nearest FORWARD one (Normal / High-speed): the End-typed
/// siblings are the same connection read backwards, angle forced to NORMAL_MAX_DEG + 20.
/// Measured on KDTW 22L: 57 rows → 19.
/// </summary>
public static class LandingExitDisplayList
{
    /// <param name="exits">GetLandingExits' list, sorted by distance from the threshold.</param>
    /// <returns>A new list in the same order; the input is not modified.</returns>
    public static List<LandingExit> Collapse(IReadOnlyList<LandingExit> exits)
    {
        var chosen = new Dictionary<(string name, int apron), LandingExit>();
        foreach (var e in exits)
        {
            if (!IsGroupable(e)) continue;
            var key = (e.TaxiwayName.ToUpperInvariant(), e.ApronNodeId);
            if (!chosen.TryGetValue(key, out var rep)
                || (rep.ExitType == "End" && e.ExitType != "End"))
                chosen[key] = e;
        }

        var result = new List<LandingExit>(exits.Count);
        foreach (var e in exits)
            if (!IsGroupable(e) || ReferenceEquals(chosen[(e.TaxiwayName.ToUpperInvariant(), e.ApronNodeId)], e))
                result.Add(e);
        return result;
    }

    // An exit with no apron node of its own, or no name, has nothing that proves it is the
    // same turnoff as another row, so it is always shown.
    private static bool IsGroupable(LandingExit e) =>
        e.ApronNodeId > 0 && e.ApronNodeId != e.NodeId && !string.IsNullOrEmpty(e.TaxiwayName);
}
