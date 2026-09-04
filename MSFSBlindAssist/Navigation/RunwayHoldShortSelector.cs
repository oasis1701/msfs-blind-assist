namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which hold-short line <see cref="RunwayHoldShortSelector.Select"/> picked for
/// a runway-destination route.
/// </summary>
public enum RunwayHoldChoice
{
    /// <summary>No HoldShort / ILSHoldShort node on the route at all.</summary>
    None,
    /// <summary>Truncated at a plain hold-short (HS/HSND) node.</summary>
    PlainHold,
    /// <summary>Truncated at an ILS hold-short (IHS/IHSND) node.</summary>
    IlsHold,
}

/// <summary>
/// Pure decision core for TaxiGuidanceManager.TruncateToHoldShort: given the
/// LATEST (closest-to-runway) ILS hold-short and plain hold-short segment
/// indices a runway-destination route passes through, picks the index to
/// truncate the route at.
///
///   • DEFAULT (<c>preferIlsHold == false</c>): the hold CLOSEST to the runway
///     — the full-length line (e.g. EGKK A1/M1), matching a normal ATC
///     clearance. This is a deliberate user decision (2026-07): do NOT make
///     the IHS preference the default again.
///   • CAT III / LVP (<c>preferIlsHold == true</c>, the Taxi planner's
///     "CAT III / low-visibility hold" checkbox): honour the ILS hold when it
///     sits just behind the full-length hold on the SAME final approach —
///     within <see cref="SAME_APPROACH_IHS_MAX_M"/>. Geometric proximity, NOT
///     route-segment count, gates "same approach": a real CAT II/III hold sits
///     only tens of metres behind the CAT I line, whereas a transit hold on a
///     prior taxiway is hundreds of metres away. Without the gate, a route
///     that merely CROSSES an ILS-critical-area hold on a transit taxiway
///     before turning onto the final connector stops a whole taxiway early
///     (OMDB 30R via N12, fs2024: taxiway N carries IHS nodes ~620 m from
///     N12's real 30R hold; the N IHS hijacked the truncation).
///
/// Pure static (indices + separation in, index + choice out) so the
/// EGKK/OMDB matrix is pinned by RunwayHoldShortSelectorTests.
/// </summary>
public static class RunwayHoldShortSelector
{
    /// <summary>
    /// LVP only: the ILS hold is honoured over the full-length hold only when
    /// the two holds are within this distance of each other (same final
    /// approach). See the class remarks for the OMDB failure this prevents.
    /// </summary>
    public const double SAME_APPROACH_IHS_MAX_M = 150.0;

    /// <summary>
    /// Picks the route-segment index to truncate a runway-destination route at.
    /// </summary>
    /// <param name="ihsIndex">Latest segment index whose ToNode is an
    /// ILSHoldShort, or -1 when the route has none.</param>
    /// <param name="hsIndex">Latest segment index whose ToNode is a plain
    /// HoldShort, or -1 when the route has none.</param>
    /// <param name="holdSeparationMeters">Distance between the two hold nodes.
    /// Only consulted when both indices are valid and the IHS sits behind the
    /// HS; pass any value (e.g. <see cref="double.MaxValue"/>) otherwise.</param>
    /// <param name="preferIlsHold">The Taxi planner's "CAT III /
    /// low-visibility hold" opt-in.</param>
    /// <returns>The truncation index (-1 = no hold node found) and which kind
    /// of hold line it is.</returns>
    public static (int TruncateAt, RunwayHoldChoice Choice) Select(
        int ihsIndex, int hsIndex, double holdSeparationMeters, bool preferIlsHold)
    {
        int truncateAt;
        if (preferIlsHold &&
            ihsIndex >= 0 && hsIndex >= 0 && ihsIndex < hsIndex)
        {
            // LVP only: the IHS is further from the runway than the HS. Honour
            // it only when the two holds are physically close (same final
            // approach); otherwise the HS on the final connector is the real
            // runway hold.
            truncateAt = holdSeparationMeters <= SAME_APPROACH_IHS_MAX_M ? ihsIndex : hsIndex;
        }
        else
        {
            // DEFAULT full-length hold, or (LVP with) no IHS / no HS / the IHS
            // already the closest hold to the runway → take whichever
            // hold-short is latest in the route (closest to the runway).
            truncateAt = Math.Max(ihsIndex, hsIndex);
        }

        var choice = truncateAt < 0 ? RunwayHoldChoice.None
                   : truncateAt == ihsIndex ? RunwayHoldChoice.IlsHold
                   : RunwayHoldChoice.PlainHold;
        return (truncateAt, choice);
    }

    /// <summary>
    /// Pure core for TruncateToHoldShort's SYNTHETIC back-off pass — the fallback used
    /// when <see cref="Select"/> found no hold node at all (common at small airports,
    /// new-numbered runways, and older navdatareader snapshots that carry no hold-short
    /// data). Returns the route-segment index to truncate at, or -1 when no node sits far
    /// enough back, in which case TruncateToHoldShort must tag NOTHING and leave the stop
    /// to HandleArrival's runway-arrival fallback.
    /// </summary>
    /// <param name="candidates">The route's segment END nodes as (segment index, lat, lon),
    /// in ascending segment order. The caller omits any segment whose ToNode is missing, so
    /// the index travels with the point rather than being the list position.</param>
    public static int SelectSyntheticBackoff(
        IReadOnlyList<(int Index, double Lat, double Lon)> candidates,
        bool hasRunwayEntry, double entryLat, double entryLon,
        double lineupLat, double lineupLon,
        double backoffMeters)
    {
        // The reference is the point where the route MEETS THE RUNWAY, not the lineup
        // target. For an ordinary full-length departure the two coincide, so this is a
        // no-op there; they diverge by hundreds of metres on a backtrack departure, a
        // named-holding-point departure, and any runway the entrance picker retargets —
        // and measuring from the lineup target on those selects the last segment, so
        // nothing is truncated and the on-pavement entry node is tagged "hold short".
        double refLat = hasRunwayEntry ? entryLat : lineupLat;
        double refLon = hasRunwayEntry ? entryLon : lineupLon;

        // Backwards from the runway end: the LATEST node that is still a full back-off
        // clear of the runway. -1 (nothing far enough back) is a meaningful answer, not a
        // failure — the caller must then tag NOTHING rather than fall back to the last
        // segment, which is a node on the pavement.
        for (int i = candidates.Count - 1; i >= 0; i--)
        {
            var c = candidates[i];
            if (TaxiGraph.FastDistanceMeters(c.Lat, c.Lon, refLat, refLon) >= backoffMeters)
                return c.Index;
        }
        return -1;
    }

    /// <summary>
    /// The route-summary feedback sentence for an LVP request — a blind pilot
    /// has no other way to know whether the CAT III hold was actually honoured
    /// (navdata hold coverage is patchy, and the same-approach gate can
    /// legitimately reject the IHS). Null when LVP wasn't requested, so the
    /// default route summary is untouched. Wording is speech-first ("CAT
    /// three", never the roman numeral, which screen readers mispronounce —
    /// same convention as the checkbox's AccessibleName).
    /// </summary>
    public static string? DescribeLvpOutcome(bool preferIlsHold, RunwayHoldChoice choice)
    {
        if (!preferIlsHold) return null;
        return choice switch
        {
            RunwayHoldChoice.IlsHold =>
                "Holding at the CAT three ILS hold.",
            RunwayHoldChoice.PlainHold =>
                "No CAT three ILS hold found at this holding point. Holding at the full-length line.",
            // No marked hold nodes at all — the synthetic back-off may still
            // stop the aircraft, so don't claim a "full-length line" exists.
            _ => "No CAT three ILS hold found before the runway.",
        };
    }
}
