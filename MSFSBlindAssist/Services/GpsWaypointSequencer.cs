using System;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The TO-waypoint readout, and the call a pilot cannot make without being told: THAT THEY HAVE
/// JUST PASSED ONE.
///
/// ⚠️ THIS EXISTS BECAUSE "REPORT PASSING WAYPOINT" WAS THE ONE IFR INSTRUCTION THE AEROPLANE
/// COULD NOT ANSWER. A readout on a key answers "where am I going"; it does not answer "you are
/// there now", and a controller expects that call within a few seconds of the event. A sighted
/// pilot gets it for free — the flight plan sequences on the map in front of them. A blind pilot
/// was left polling a hotkey and hoping.
///
/// ⚠️ MSFSBA DOES NOT MAKE THE CALL, AND MUST NOT. Deciding what to say to a controller, and
/// when, is flying — it is the pilot's job and their judgement. What was missing was never the
/// radio call; it was the CUE the call is made from. This announces the cue and stops there,
/// which is exactly the line every other announcement in this app sits on: say what a sighted
/// pilot would have seen, then get out of the way.
///
/// THE SEQUENCE TEST IS STRUCTURAL, NEVER "the ident changed". The aeroplane publishes both ends
/// of its active leg, so a real sequence is recognisable rather than inferred: the waypoint we
/// were flying TO has become the waypoint we are flying FROM. Nothing else satisfies that.
/// A Direct-To, a plan edit, a procedure load and the first activation of a plan ALL change the
/// TO-waypoint and none of them is a passing — an "it changed" rule would announce a passing for
/// every one of them, which is worse than silence: a pilot would report passing a fix they had
/// merely re-routed to.
/// </summary>
public static class GpsWaypointSequencer
{
    private const double MetresPerNauticalMile = 1852.0;

    /// <summary>What one frame of GPS waypoint data means, once compared with the frame before.</summary>
    public readonly struct Reading
    {
        /// <summary>The waypoint being flown to, empty when there is none.</summary>
        public string NextId { get; init; }
        /// <summary>Distance to it, nautical miles.</summary>
        public double DistanceNm { get; init; }
        /// <summary>Magnetic bearing to it, degrees.</summary>
        public double BearingDeg { get; init; }
        /// <summary>Time to it in seconds, 0 when stopped (the navigator publishes 0 below 1 knot).</summary>
        public double EteSeconds { get; init; }
        /// <summary>True when a flight plan is active at all.</summary>
        public bool HasPlan { get; init; }
        /// <summary>
        /// True when the navigator has actually COMPUTED the leg geometry.
        ///
        /// ⚠️ A PLAN CAN BE LOADED AND ACTIVE WITH NO GEOMETRY BEHIND IT, and reporting the
        /// zeros as though they were a reading is worse than saying nothing. Measured on the
        /// ground at VCBI with the whole route loaded and the TO waypoint correctly reading
        /// LIKRA: distance, bearing, desired track, cross-track and ETE were ALL exactly zero,
        /// because the G1000's LNAV does not begin computing until it is tracking a leg.
        /// The readout said "LIKRA, 0.0 miles, bearing 000", which a pilot hears as BEING ON
        /// TOP OF THE FIX — the opposite of the truth, spoken with the confidence of a
        /// measurement. Found by pressing the key rather than by reading the code.
        ///
        /// Distance is the discriminator, never bearing: 000 is a real bearing, and 0.0 miles
        /// is not a real distance — LNAV that is tracking always publishes something above
        /// zero, even overhead the fix.
        /// </summary>
        public bool HasGeometry { get; init; }
        /// <summary>The waypoint just passed, empty unless this frame IS a passing.</summary>
        public string PassedId { get; init; }
    }

    /// <summary>
    /// Reads one frame against the previous TO-waypoint. <paramref name="previousNextId"/> is
    /// null before the first frame, which is what makes the first one a BASELINE: connecting to
    /// an aeroplane already established on a leg must not announce a passing that happened
    /// before MSFSBA was watching, the same rule every other monitor here follows.
    /// </summary>
    /// <param name="legNext">
    /// The active leg's name read from the FLIGHT PLAN, which overrides the SimVar when it has
    /// one. ⚠️ The SimVar idents are EMPTY ON EVERY PROCEDURE - the navigator writes them off a
    /// plan-change event that does not fire as a SID or STAR sequences - so on real IFR flying
    /// this is the only source there is. Measured live: both SimVars blank while the plan's own
    /// getLeg(activeLateralLeg).name returned "BI583".
    /// </param>
    /// <param name="legPrev">The leg before it, same source and same reason.</param>
    public static Reading Read(SimConnectManager.GpsWaypointData data, string? previousNextId,
                               string? legNext = null, string? legPrev = null)
    {
        // The plan wins when it has a name; the SimVar is the fallback, not the other way
        // round. Neither is trusted to be non-empty - a fix-less path/terminator leg genuinely
        // has no name in EITHER, and inventing one would be worse than the silence.
        string next = (legNext ?? string.Empty).Trim();
        if (next.Length == 0) next = (data.NextId ?? string.Empty).Trim();

        string prev = (legPrev ?? string.Empty).Trim();
        if (prev.Length == 0) prev = (data.PrevId ?? string.Empty).Trim();

        bool passed =
            previousNextId != null &&                       // not the baseline frame
            previousNextId.Length > 0 &&
            next.Length > 0 &&
            !string.Equals(next, previousNextId, StringComparison.OrdinalIgnoreCase) &&
            // ⚠️ PREV VALID guards the SIMVAR's previous ident only. When the name came from
            // the flight plan the flag is irrelevant - and on a procedure it is the ONLY path
            // that produces a name at all, so requiring the flag would keep the call silent
            // for exactly the flights it was built for.
            (!string.IsNullOrWhiteSpace(legPrev) || data.PrevValid > 0.5) &&
            string.Equals(prev, previousNextId, StringComparison.OrdinalIgnoreCase);

        return new Reading
        {
            NextId = next,
            DistanceNm = data.DistanceMeters / MetresPerNauticalMile,
            BearingDeg = data.BearingDegrees,
            EteSeconds = data.EteSeconds,
            HasPlan = data.IsActiveFlightPlan > 0.5,
            HasGeometry = data.DistanceMeters > 0,
            PassedId = passed ? prev : string.Empty
        };
    }

    /// <summary>
    /// The passing cue. Names the fix passed and the one now being flown to, because "passing
    /// SOXOM" and "next VEKIN" are the two halves of the same position report and asking for
    /// the second on a separate keypress wastes the moment. No bearing and no time: this fires
    /// unprompted, and a pilot who wants the rest presses the key.
    /// </summary>
    public static string ComposePassing(Reading r, Func<double, string>? distance = null)
    {
        if (r.PassedId.Length == 0) return string.Empty;
        string next = r.NextId.Length == 0
            ? string.Empty
            : r.HasGeometry
                ? $" Next {Spell(r.NextId)}, {(distance ?? DefaultDistance)(r.DistanceNm)}."
                : $" Next {Spell(r.NextId)}.";
        return $"Passing {Spell(r.PassedId)}.{next}";
    }

    /// <summary>
    /// The on-demand readout. Everything a position report needs in one sentence: which fix,
    /// how far, which way, and how long — the last being what "estimating SOXOM at 34" is
    /// built from, and free here because the navigator already publishes it.
    /// </summary>
    public static string ComposeReadout(Reading r, Func<double, string>? distance = null)
    {
        if (!r.HasPlan) return "No active flight plan.";

        // ⚠️ AN UNNAMED LEG IS NOT AN ABSENT ONE, AND SAYING SO WAS WRONG ON EVERY SID.
        // ARINC 424 path/terminator legs - climb to an altitude, fly a heading to an
        // intercept - carry NO FIX, so the navigator publishes an empty ident for them. They
        // are most of a departure: measured live at VCBI on the ANUT1D, where the active leg
        // was DER22 with LNAV computing a perfectly good 1.0 miles and the ident blank.
        //
        // "No active waypoint" there is a lie a pilot would act on - it says the flight plan
        // has run out - so the distance and bearing are given instead, which are exactly what
        // the aeroplane is flying. This codebase already learned the same lesson in the EFB,
        // where dropping fix-less legs silently deleted the initial climb of most SIDs.
        if (r.NextId.Length == 0)
        {
            return r.HasGeometry
                ? $"Unnamed leg, {(distance ?? DefaultDistance)(r.DistanceNm)}, bearing {r.BearingDeg:000}."
                : "No active waypoint.";
        }

        // The fix is known, the geometry is not. Naming it is still worth saying - it answers
        // "what am I going to next" - but the numbers must not be invented from the zeros.
        if (!r.HasGeometry) return $"{Spell(r.NextId)}, distance not computed.";

        string text = $"{Spell(r.NextId)}, {(distance ?? DefaultDistance)(r.DistanceNm)}, bearing {r.BearingDeg:000}";
        if (r.EteSeconds >= 1)
        {
            int minutes = (int)Math.Round(r.EteSeconds / 60.0);
            text += minutes >= 1
                ? $", {minutes} minute{(minutes == 1 ? "" : "s")}"
                : ", less than a minute";
        }
        return text + ".";
    }

    /// <summary>
    /// Distance and time to the DESTINATION — what "D" answers, and what a controller means by
    /// "how far are you from the field".
    ///
    /// ⚠️ THE DISTANCE IS RECOVERED, NOT READ, AND THAT IS NOT A HACK. The G1000 never publishes
    /// route distance to the destination as a SimVar: `onLnavDistanceToDestinationChanged` takes
    /// the distance and writes only `GPS ETE` and `GPS ETA` from it, using
    /// <c>ete = 3600 * distance / groundSpeed</c>. Inverting that returns the distance the
    /// navigator actually computed, exactly — it is the same number, algebraically, not an
    /// estimate of it.
    ///
    /// ⚠️ IT IS UNRECOVERABLE BELOW ABOUT A KNOT, and honestly so: the same method writes ETE as
    /// a flat 0 whenever ground speed is at or under 1, so on the ground there is nothing to
    /// invert. That says "not computed" rather than "zero miles", for the same reason the
    /// waypoint readout does — a zero that means absence must never be spoken as a measurement.
    /// </summary>
    public static string ComposeDestination(SimConnectManager.GpsWaypointData data,
                                            Func<double, string>? distance = null)
    {
        if (data.IsActiveFlightPlan <= 0.5) return "No active flight plan.";

        double ete = data.RouteEteSeconds;
        double gs = data.GroundSpeedKnots;
        if (ete < 1 || gs <= 1) return "Distance to destination not computed.";

        double nm = ete * gs / 3600.0;
        int minutes = (int)Math.Round(ete / 60.0);
        string time = minutes >= 60
            ? $"{minutes / 60} hour{(minutes / 60 == 1 ? "" : "s")} {minutes % 60} minutes"
            : minutes >= 1 ? $"{minutes} minute{(minutes == 1 ? "" : "s")}"
                           : "less than a minute";

        return $"Destination {(distance ?? DefaultDistance)(nm)}, {time}.";
    }

    /// <summary>
    /// Top of descent — what "Shift+D" answers.
    ///
    /// This one IS published, but only by the avionics rather than the sim: the G1000's own VNAV
    /// publisher carries <c>L:WTAP_VNav_Distance_To_TOD</c> alongside a
    /// <c>L:WTAP_VNav_Path_Available</c> flag. Both names were read out of the running
    /// instrument's publisher table, never guessed.
    ///
    /// ⚠️ A DESCENT THAT HAS NOT BEEN COMPUTED IS NOT A DESCENT ZERO MILES AWAY. Without a VNAV
    /// path there is no top of descent at all, and the flag is what tells the two apart — the
    /// distance alone would read 0 in both cases, which is the exact trap the waypoint readout
    /// fell into.
    /// </summary>
    public static string ComposeTopOfDescent(bool pathAvailable, double todMetres,
                                             Func<double, string>? distance = null)
    {
        if (!pathAvailable) return "No vertical path computed.";
        if (todMetres <= 0) return "Already past top of descent.";
        return $"Top of descent in {(distance ?? DefaultDistance)(todMetres / MetresPerNauticalMile)}.";
    }

    private static string DefaultDistance(double nm) =>
        nm < 10 ? $"{nm:0.0} miles" : $"{nm:0} miles";

    /// <summary>
    /// A waypoint ident is SPELT, letter by letter, exactly as a pilot reads one back to a
    /// controller. "SOXOM" run together through a speech synthesiser is a noise; S O X O M is a
    /// fix. Only for the ident-shaped ones — an airport or a named leg the navigator generated
    /// ("HDG 071") is prose and stays prose.
    /// </summary>
    private static string Spell(string ident)
    {
        if (ident.Length == 0 || ident.Length > 5) return ident;
        foreach (char c in ident)
            if (!char.IsLetterOrDigit(c)) return ident;
        return string.Join(" ", ident.ToUpperInvariant().ToCharArray());
    }
}
