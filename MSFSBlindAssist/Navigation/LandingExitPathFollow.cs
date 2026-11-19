namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Is the aircraft turning onto the chosen exit's own path?" — the question the rollout's
/// missed-exit detectors must ask before declaring a miss.
///
/// Both detectors (the in-rollout one in <c>UpdateLandingRollout</c> and the post-handoff
/// monitor in <c>UpdatePosition</c>) judge a miss from the RUNWAY: far enough past the
/// turn point, heading still within 15° of the runway, and (post-handoff) within 30 ft of
/// the centreline. That cannot tell "rolled straight past" from "turning onto an exit whose
/// first stretch leaves the runway at a shallow angle". YPPH 21 → C9 (live 2026-09-18):
/// C9 leaves the centreline at 14° and is still only 6 m (20 ft) left of it 100 ft past the
/// junction, so an aircraft flying C9 — 12° into the turn, tone centred — was told
/// "Missed C9. Retargeting C11" 95 ft past the junction and guided away from the exit it
/// was on.
///
/// A miss is held off only when ALL of these are true:
///   • the aircraft is within <see cref="ToleranceMetres"/> of the exit path (junction →
///     the handoff destination);
///   • the path where the aircraft is DIVERGES from the runway (≥ <see cref="MinTurnDeg"/>) —
///     a stretch drawn along the centreline says nothing about intent;
///   • the aircraft itself is turned ≥ <see cref="MinTurnDeg"/> off the runway, to the SAME
///     side, and within <see cref="MaxHeadingMismatchDeg"/> of that stretch's direction.
/// An aircraft rolling straight on keeps runway heading, so its miss is called exactly
/// where it always was — the heading gate is what makes this change additive only. (A
/// position-only hold-off was measured and rejected: at 500 airports it delayed 6,350
/// straight-roll miss calls because many sceneries draw exits ALONG the centreline for
/// 60-75 m, and 1,390 of those delays made the retarget skip the next exit.)
/// </summary>
public static class LandingExitPathFollow
{
    /// <summary>Cross-track allowance around the exit path. The YPPH aircraft flew C9 4-6 m off the navdata line.</summary>
    public const double ToleranceMetres = 10.0;

    /// <summary>Minimum turn off the runway heading for the path stretch the aircraft is on.</summary>
    public const double MinTurnDeg = 5.0;

    /// <summary>
    /// Minimum turn off the runway heading for the AIRCRAFT. Lower than the path's: on a shallow
    /// exit a pilot following the tone is only a few degrees into the turn when the check runs
    /// (KDFW 18R E7, an 8° exit, flown at 4° — VirtualPilot 2026-09-18). A straight roll holds
    /// runway heading, so 3° still separates the two.
    /// </summary>
    public const double MinAircraftTurnDeg = 3.0;

    /// <summary>Largest difference between the aircraft heading and the path stretch it is on.</summary>
    public const double MaxHeadingMismatchDeg = 20.0;

    /// <summary>
    /// How far past the miss margin the path may still hold a miss off. A committed
    /// aircraft has left the runway (lateral / heading handoff) long before this; the cap
    /// only exists so the hold-off can never suppress a miss indefinitely.
    /// Callers pass the margin INCLUDING the exit's on-centreline run
    /// (<see cref="OnAxisRunMetres"/>, itself capped): measured from the junction, a long stub
    /// used the whole window up before the exit even began to curve, so a pilot following the
    /// curve was told "Missed" — KPHX 07L E12 (on the centreline for 161 m, called at 206 m
    /// with the aircraft 8° into the turn) and KSTL 30R E2 (VirtualPilot, 2026-09-23).
    /// </summary>
    public const double SuppressWindowFeet = 400.0;

    private const double MetresPerDegLat = 111132.0;

    /// <summary>
    /// Shortest distance, in metres, from a point to a polyline (equirectangular — the path
    /// is a few hundred metres long), with the true bearing of the nearest stretch
    /// (NaN for a single-point path). Empty path → <see cref="double.PositiveInfinity"/>.
    /// </summary>
    public static double DistanceToPathMeters(
        IReadOnlyList<(double Lat, double Lon)> path, double lat, double lon, out double nearestBearingTrue)
    {
        nearestBearingTrue = double.NaN;
        if (path == null || path.Count == 0) return double.PositiveInfinity;
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        (double x, double y) P((double Lat, double Lon) p) =>
            ((p.Lon - lon) * MetresPerDegLat * cosLat, (p.Lat - lat) * MetresPerDegLat);

        if (path.Count == 1)
        {
            var (x, y) = P(path[0]);
            return Math.Sqrt(x * x + y * y);
        }

        double best = double.PositiveInfinity;
        for (int i = 0; i < path.Count - 1; i++)
        {
            var (ax, ay) = P(path[i]);
            var (bx, by) = P(path[i + 1]);
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            if (len2 < 1e-6) continue;   // zero-length stretch has no direction
            double t = Math.Clamp(-(ax * dx + ay * dy) / len2, 0.0, 1.0);
            double cx = ax + t * dx, cy = ay + t * dy;
            double d = Math.Sqrt(cx * cx + cy * cy);
            if (d < best)
            {
                best = d;
                nearestBearingTrue = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
            }
        }
        return best;
    }

    public static double DistanceToPathMeters(
        IReadOnlyList<(double Lat, double Lon)> path, double lat, double lon)
        => DistanceToPathMeters(path, lat, lon, out _);

    /// <summary>
    /// True when a miss the runway-referenced tests would declare must be held off because
    /// the aircraft is turning onto the exit's own path. <paramref name="path"/> null or
    /// shorter than two points → false (the detectors behave exactly as before).
    /// </summary>
    public static bool HoldsOffMiss(
        IReadOnlyList<(double Lat, double Lon)>? path,
        double lat, double lon, double headingTrue, double runwayHeadingTrue,
        double signedAlongPastFt, double overshootMarginFt)
    {
        if (path == null || path.Count < 2) return false;
        if (signedAlongPastFt > overshootMarginFt + SuppressWindowFeet) return false;
        if (DistanceToPathMeters(path, lat, lon, out double stretchBrg) > ToleranceMetres) return false;
        if (double.IsNaN(stretchBrg)) return false;

        double pathTurn = Norm(stretchBrg - runwayHeadingTrue);
        double aircraftTurn = Norm(headingTrue - runwayHeadingTrue);
        if (Math.Abs(pathTurn) < MinTurnDeg || Math.Abs(aircraftTurn) < MinAircraftTurnDeg) return false;
        if (Math.Sign(pathTurn) != Math.Sign(aircraftTurn)) return false;
        return Math.Abs(Norm(headingTrue - stretchBrg)) <= MaxHeadingMismatchDeg;
    }

    /// <summary>
    /// How far past the margin "still on the exit's path" may hold a miss off — twice
    /// <see cref="SuppressWindowFeet"/>, so a path hugging the runway can never hold one off
    /// indefinitely.
    /// </summary>
    public const double OnPathWindowFeet = 2 * SuppressWindowFeet;

    /// <summary>
    /// True while the aircraft is still ON the chosen exit's own path (within
    /// <see cref="ToleranceMetres"/>) — the pilot following the tone is, by definition, and a
    /// "Missed" there pulls them off the exit they are correctly on. Owner ruling 2026-09-23:
    /// following the tone must never earn a false "Missed". 54 of the 68 false misses
    /// VirtualPilot still found fired BEFORE the exit's path had moved 10 m from the centreline,
    /// the aircraft 0-3 m from that path (KDTW 27L T5, 09R T4, KSTL 30R E2 at 2-3°): there,
    /// "following the exit" and "rolled straight past" are the same position and only the
    /// path moving away can tell them apart — so the miss waits for it. A straight roll past is
    /// called as soon as the path is more than <see cref="OnPathToleranceMetres"/> from it;
    /// bounded by <see cref="OnPathWindowFeet"/>.
    /// <para>This is position-only, which the class summary records was once rejected (delayed
    /// straight-roll calls). The owner re-weighed it: a false "Missed" pulls a tone-following pilot
    /// off a correct exit, a later call only affects a pilot already ignoring the tone. Measured
    /// (VirtualPilot, 100 airports): false misses 68 → 22, plus 15 knock-on failures fixed (runs
    /// off the end, non-arrivals, off-pavement); straight-roll calls later — STRAIGHT_MISS_LATE
    /// 14 → 101, median 224 m, worst 342 m past the junction — with every miss still called and no
    /// straight-roll scenario newly failing. The harness does not record which exit a retarget
    /// chose, so a later call skipping the next exit is not measured.</para>
    /// </summary>
    public static bool StillOnExitPath(IReadOnlyList<(double Lat, double Lon)>? path,
        double lat, double lon, double signedAlongPastFt, double overshootMarginFt)
    {
        if (path == null || path.Count < 2) return false;
        if (signedAlongPastFt > overshootMarginFt + OnPathWindowFeet) return false;
        return DistanceToPathMeters(path, lat, lon) <= OnPathToleranceMetres;
    }

    /// <summary>
    /// Cross-track allowance for <see cref="StillOnExitPath"/>. Wider than
    /// <see cref="ToleranceMetres"/>: a pilot turning onto the exit lags the path by a few metres,
    /// and 17 of the false misses left at 10 m fired with the aircraft exactly at 10 m.
    /// </summary>
    public const double OnPathToleranceMetres = 15.0;

    /// <summary>Largest lateral offset (m) a point may have and still count as ON the centreline.</summary>
    public const double OnAxisLateralMetres = 6.0;
    /// <summary>Cap on the run: nothing drawn as an exit runs further down the runway than this.</summary>
    public const double OnAxisMaxRunMetres = 200.0;
    /// <summary>How far back down the runway a path may come before it counts as a hairpin.</summary>
    public const double OnAxisHairpinMetres = 15.0;
    /// <summary>
    /// A hairpin is turning back while still NEAR the runway (KPHL 35 E4). A path that loops
    /// back once well out on the apron (KSTL 30L V, YBBN 01L T1, CYYC 29 C — 50-80 m out) is an
    /// ordinary exit whose stub still needs the allowance; zeroing it produced a false "Missed"
    /// 31 m past the junction (VirtualPilot 2026-09-18).
    /// </summary>
    public const double OnAxisHairpinLateralMetres = 30.0;

    /// <summary>
    /// How far (metres) the exit's own path runs ALONG the runway centreline, forward of the
    /// junction, before it leaves it — the stretch on which "rolled straight past" and
    /// "following the exit" are the same position. Many sceneries draw the exit taxiway down
    /// the centreline first (KORD 27L M 80 m, ZSPD 35R B7 99 m, EGLL 27L N7 40 m after a 3 m
    /// jog); both missed-exit detectors wait this much longer (less any TurnPointOffsetFeet
    /// already applied). VirtualPilot measured ~1,000 false "missed" calls in 4,070 simulated
    /// landings at the 100 busiest airports before this; ~190 after.
    /// Returns 0 — the detectors unchanged — for a path that doubles back on the centreline,
    /// leaves it by turning BACK down the runway (KPHL 35 E4, a 160° hairpin), or comes back
    /// onto it after leaving (KSDF 29 F): those are not stubs a pilot rolls along.
    /// </summary>
    public static double OnAxisRunMetres(IReadOnlyList<(double Lat, double Lon)> path,
        double junctionLat, double junctionLon,
        double runwayStartLat, double runwayStartLon, double runwayHeadingTrue)
    {
        if (path == null || path.Count < 2) return 0.0;
        double h = runwayHeadingTrue * Math.PI / 180.0;
        (double along, double lateral) Frame(double lat, double lon)
        {
            double latMid = (lat + runwayStartLat) * 0.5 * Math.PI / 180.0;
            double dN = (lat - runwayStartLat) * MetresPerDegLat;
            double dE = (lon - runwayStartLon) * MetresPerDegLat * Math.Cos(latMid);
            return (dE * Math.Sin(h) + dN * Math.Cos(h), dE * Math.Cos(h) - dN * Math.Sin(h));
        }
        double j = Frame(junctionLat, junctionLon).along;
        double last = j;
        int i = 0;
        for (; i < path.Count; i++)
        {
            var (a, l) = Frame(path[i].Lat, path[i].Lon);
            if (Math.Abs(l) > OnAxisLateralMetres) break;
            if (a < last - 1.0) return 0.0;
            last = Math.Max(last, a);
        }
        for (; i < path.Count; i++)
        {
            var (a, l) = Frame(path[i].Lat, path[i].Lon);
            if (a < last - OnAxisHairpinMetres && Math.Abs(l) <= OnAxisHairpinLateralMetres) return 0.0;
            if (Math.Abs(l) <= OnAxisLateralMetres) return 0.0;
        }
        return Math.Min(last - j, OnAxisMaxRunMetres);
    }

    private static double Norm(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        if (deg < -180.0) deg += 360.0;
        return deg;
    }
}
