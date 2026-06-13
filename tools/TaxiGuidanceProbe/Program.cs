using MSFSBlindAssist.Navigation;

// TaxiGuidanceProbe — asserts the steering look-ahead walk is continuous and
// the curve scan detects the KATL 2026-06-10 micro-segment curve. Run:
//   dotnet run --project tools/TaxiGuidanceProbe -p:Platform=x64
// Exit code 0 = all checks pass.

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
    if (!ok) failures++;
}

const double MPD = 111132.0; // metres per degree latitude (matches app helpers)

// Build a polyline from (bearing, length) legs starting at an origin.
(double[] lats, double[] lons) BuildPolyline(double lat0, double lon0, (double brgDeg, double lenM)[] legs)
{
    var lats = new double[legs.Length + 1];
    var lons = new double[legs.Length + 1];
    lats[0] = lat0; lons[0] = lon0;
    for (int i = 0; i < legs.Length; i++)
    {
        double rad = legs[i].brgDeg * Math.PI / 180.0;
        double cosLat = Math.Cos(lats[i] * Math.PI / 180.0);
        lats[i + 1] = lats[i] + legs[i].lenM * Math.Cos(rad) / MPD;
        lons[i + 1] = lons[i] + legs[i].lenM * Math.Sin(rad) / (MPD * cosLat);
    }
    return (lats, lons);
}

double DistM(double lat1, double lon1, double lat2, double lon2)
{
    double klat = MPD, klon = MPD * Math.Cos(lat1 * Math.PI / 180.0);
    return Math.Sqrt(Math.Pow((lat2 - lat1) * klat, 2) + Math.Pow((lon2 - lon1) * klon, 2));
}

// ---------------------------------------------------------------------------
// Case 1 — KATL 10:54 curve replica (segs 8–14): bearings 318→270 in small
// steps, 25 m legs, then a 200 m straight tail on the final bearing.
// ---------------------------------------------------------------------------
var curveLegs = new (double, double)[]
{
    (318, 25), (313, 25), (303, 25), (298, 25), (288, 25), (277, 25), (270, 25),
    (270, 200)
};
var (cLats, cLons) = BuildPolyline(33.6350, -84.4150, curveLegs);

// 1a. Cumulative turn over 100 m from the start ≈ sum of step deltas within
// the window. From node0 the first 100 m covers junctions at 25/50/75/100 m:
// deltas −5, −10, −5, −10 = −30°.
double cum = GuidanceGeometry.CumulativeTurnDeg(cLats, cLons, 0, cLats[0], cLons[0], 100.0, out bool discrete);
Check(Math.Abs(cum - (-30.0)) < 1.0, $"curve replica: cumulative turn ≈ −30° over 100 m (got {cum:F1})");
Check(!discrete, "curve replica: no single step ≥ 20° (curve, not a discrete turn)");

// 1b. Over 175 m (all junctions): −48° total.
cum = GuidanceGeometry.CumulativeTurnDeg(cLats, cLons, 0, cLats[0], cLons[0], 175.0, out discrete);
Check(Math.Abs(cum - (-48.0)) < 1.0, $"curve replica: cumulative turn ≈ −48° over 175 m (got {cum:F1})");

// 1b2. Mid-segment window accounting: aircraft 12 m ALONG leg 0, window 90 m.
// Junctions ahead at 13/38/63/88 m → −5 −10 −5 −10 = −30°. From node 0 the
// same window reaches only three junctions (−20°), so this pins the (1 − t)
// start-of-window projection.
{
    double rad0 = 318 * Math.PI / 180.0, cl0 = Math.Cos(cLats[0] * Math.PI / 180.0);
    double aLat12 = cLats[0] + 12.0 * Math.Cos(rad0) / MPD;
    double aLon12 = cLons[0] + 12.0 * Math.Sin(rad0) / (MPD * cl0);
    double cumMid = GuidanceGeometry.CumulativeTurnDeg(cLats, cLons, 0, aLat12, aLon12, 90.0, out bool discMid);
    Check(Math.Abs(cumMid - (-30.0)) < 1.0 && !discMid, $"curve replica: mid-segment window start (t≈0.48) → −30° over 90 m (got {cumMid:F1})");
}

// 1c. WalkTarget continuity while marching the aircraft along the polyline.
// Advance segIdx the way the manager does (capture at 25 m to the segment's
// end node — here that means as soon as we pass each node, since legs are 25 m).
// Assert no frame-to-frame target jump exceeds 8 m for a 2 m aircraft step.
{
    double maxJump = 0;
    (double lat, double lon)? prevTgt = null;
    int seg = 0;
    for (double s = 0; s <= 350; s += 2.0)
    {
        // aircraft position s metres along the polyline
        double remaining = s; int i = 0; double aLat = cLats[0], aLon = cLons[0];
        while (i < curveLegs.Length && remaining > curveLegs[i].Item2)
        { remaining -= curveLegs[i].Item2; i++; }
        if (i < curveLegs.Length)
        {
            double rad = curveLegs[i].Item1 * Math.PI / 180.0;
            double cosLat = Math.Cos(cLats[i] * Math.PI / 180.0);
            aLat = cLats[i] + remaining * Math.Cos(rad) / MPD;
            aLon = cLons[i] + remaining * Math.Sin(rad) / (MPD * cosLat);
        }
        else { aLat = cLats[^1]; aLon = cLons[^1]; }

        // manager-style segment advance: capture when within 25 m of seg end
        while (seg < curveLegs.Length - 1 && DistM(aLat, aLon, cLats[seg + 1], cLons[seg + 1]) < 25.0)
            seg++;

        var tgt = GuidanceGeometry.WalkTarget(cLats, cLons, seg, aLat, aLon, 52.0);
        if (prevTgt is { } p)
            maxJump = Math.Max(maxJump, DistM(p.lat, p.lon, tgt.lat, tgt.lon));
        prevTgt = tgt;

        // target is never behind the aircraft's direction of travel (±100°)
        if (s > 1 && seg < curveLegs.Length)
        {
            double travelBrg = curveLegs[Math.Min(seg, curveLegs.Length - 1)].Item1;
            double tgtBrg = Math.Atan2(
                (tgt.lon - aLon) * MPD * Math.Cos(aLat * Math.PI / 180.0),
                (tgt.lat - aLat) * MPD) * 180.0 / Math.PI;
            double diff = ((tgtBrg - travelBrg + 540) % 360) - 180;
            Check(Math.Abs(diff) <= 100.0 || DistM(aLat, aLon, tgt.lat, tgt.lon) < 1.0,
                  $"target never behind travel direction at s={s:F0} (diff {diff:F0}°)");
            if (Math.Abs(diff) > 100.0) break; // don't spam failures
        }
    }
    Check(maxJump < 8.0, $"curve replica: max frame-to-frame target jump < 8 m (got {maxJump:F1} m; old code jumped 70+ m)");
}

// ---------------------------------------------------------------------------
// Case 2 — discrete 90° turn: 100 m north then 100 m east.
// ---------------------------------------------------------------------------
var (dLats, dLons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (0, 100), (90, 100) });
cum = GuidanceGeometry.CumulativeTurnDeg(dLats, dLons, 0, dLats[0], dLons[0], 150.0, out discrete);
Check(discrete, "discrete 90°: flagged as discrete step (owned by turn announcements)");
Check(Math.Abs(cum - 90.0) < 1.0, $"discrete 90°: cumulative = +90° (got {cum:F1})");

// WalkTarget wraps past the junction once within look-ahead: aircraft 30 m
// before the junction with 52 m look-ahead → target 22 m down the east leg.
{
    double aLat = dLats[0] + 70.0 / MPD;  // 70 m up the north leg
    var tgt = GuidanceGeometry.WalkTarget(dLats, dLons, 0, aLat, dLons[0], 52.0);
    double eastM = (tgt.lon - dLons[1]) * MPD * Math.Cos(dLats[1] * Math.PI / 180.0);
    double northOfJunction = (tgt.lat - dLats[1]) * MPD;
    Check(Math.Abs(eastM - 22.0) < 1.5 && Math.Abs(northOfJunction) < 1.5,
          $"discrete 90°: target wraps 22 m past junction along next leg (got east={eastM:F1}, dNorth={northOfJunction:F1})");
}

// ---------------------------------------------------------------------------
// Case 3 — straight 500 m: target stays ~lookAhead ahead, no curve detected.
// ---------------------------------------------------------------------------
var (sLats, sLons) = BuildPolyline(33.0, -84.0, new (double, double)[] { (45, 500) });
cum = GuidanceGeometry.CumulativeTurnDeg(sLats, sLons, 0, sLats[0], sLons[0], 100.0, out discrete);
Check(Math.Abs(cum) < 0.5 && !discrete, "straight: no cumulative turn, no discrete step");
{
    var tgt = GuidanceGeometry.WalkTarget(sLats, sLons, 0, sLats[0], sLons[0], 80.0);
    double d = DistM(sLats[0], sLons[0], tgt.lat, tgt.lon);
    Check(Math.Abs(d - 80.0) < 1.0, $"straight: target is 80 m ahead (got {d:F1})");
}
// Remaining route shorter than look-ahead → final node.
{
    var tgt = GuidanceGeometry.WalkTarget(sLats, sLons, 0, sLats[1], sLons[1], 80.0);
    Check(DistM(tgt.lat, tgt.lon, sLats[1], sLons[1]) < 0.5, "straight: clamps to final node at route end");
}

// ---------------------------------------------------------------------------
// Case 4 — zero-length segment robustness: duplicate node in the polyline.
// EAST-going on purpose: a degenerate segment's phantom bearing is
// atan2(0,0) = 0° (north). On an east-going route that phantom would inject
// ±90° deltas and flip hasDiscreteStep — so these checks genuinely pin the
// degenerate-segment guards (a north-going polyline passed even without them).
// ---------------------------------------------------------------------------
double dLon100 = 100.0 / (MPD * Math.Cos(33.0 * Math.PI / 180.0));   // 100 m of longitude at 33°N
var zLats = new[] { 33.0, 33.0, 33.0, 33.0 };
var zLons = new[] { -84.0, -84.0 + dLon100, -84.0 + dLon100, -84.0 + 2 * dLon100 };  // 100 m, 0 m, 100 m east
{
    var tgt = GuidanceGeometry.WalkTarget(zLats, zLons, 0, zLats[0], zLons[0], 150.0);
    double d = DistM(zLats[0], zLons[0], tgt.lat, tgt.lon);
    Check(Math.Abs(d - 150.0) < 1.0, $"zero-length seg: walk passes through degenerate segment (got {d:F1} m)");
}
cum = GuidanceGeometry.CumulativeTurnDeg(zLats, zLons, 0, zLats[0], zLons[0], 200.0, out discrete);
Check(Math.Abs(cum) < 0.5 && !discrete, "zero-length seg: no phantom turn from degenerate bearing (east-going pin)");
// Degenerate CURRENT segment: segIdx points at the zero-length joint (seg 1).
// Pins the `t = 1.0` ternary in WalkTarget (without it: 0/0 = NaN) and the
// b0 reference-bearing scan in CumulativeTurnDeg.
{
    var tgt = GuidanceGeometry.WalkTarget(zLats, zLons, 1, zLats[1], zLons[1], 50.0);
    double d = DistM(zLats[1], zLons[1], tgt.lat, tgt.lon);
    Check(Math.Abs(d - 50.0) < 1.0, $"degenerate CURRENT segment: target 50 m up the next leg, no NaN (got {d:F1} m)");
}
cum = GuidanceGeometry.CumulativeTurnDeg(zLats, zLons, 1, zLats[1], zLons[1], 200.0, out discrete);
Check(Math.Abs(cum) < 0.5 && !discrete, "degenerate CURRENT segment: b0 scan skips joint, no phantom turn");

// ---------------------------------------------------------------------------
// Case 5 — hairpin + stationary aircraft (KIAH 15:23:40 replica). The via-FE
// loop route put a 172° out-and-back joint under a nearly-stationary aircraft;
// the old code's 50 m branch boundary flipped the target 102 m in ONE frame
// (smoothed error −23° → +41°: instant hard-left → hard-right pan). The walk
// must give a STATIONARY aircraft a STATIONARY target (rotation is not an
// input), and stay continuous while creeping through the apex.
// Polyline: 100 m east, then a 172° reversal: 102 m back west-ish (bearing 263°).
// ---------------------------------------------------------------------------
var (hLats, hLons) = BuildPolyline(29.995, -95.354, new (double, double)[] { (95, 100), (267, 102) });
{
    // Aircraft parked 50.05 m before the hairpin apex on the first leg —
    // exactly where the old 50 m boundary sat. Target must be identical
    // across repeated calls (stationary) regardless of any "rotation".
    double rad = 95 * Math.PI / 180.0, cosLat = Math.Cos(hLats[0] * Math.PI / 180.0);
    double aLat = hLats[0] + 49.95 * Math.Cos(rad) / MPD;
    double aLon = hLons[0] + 49.95 * Math.Sin(rad) / (MPD * cosLat);
    var t1 = GuidanceGeometry.WalkTarget(hLats, hLons, 0, aLat, aLon, 52.0);
    var t2 = GuidanceGeometry.WalkTarget(hLats, hLons, 0, aLat, aLon, 52.0);
    Check(t1 == t2, "hairpin: repeated identical calls give identical target (no hidden state/time dependence)");

    // Creep through the apex region in 0.05 m steps (slower than the KIAH
    // creep) on segment 0; assert the target never jumps more than 1 m per
    // step — the old code jumped 102 m crossing the 50 m boundary here.
    (double lat, double lon)? prev = null;
    double maxJump = 0;
    for (double s = 49.0; s <= 51.0; s += 0.05)
    {
        double pLat = hLats[0] + s * Math.Cos(rad) / MPD;
        double pLon = hLons[0] + s * Math.Sin(rad) / (MPD * cosLat);
        var tgt = GuidanceGeometry.WalkTarget(hLats, hLons, 0, pLat, pLon, 52.0);
        if (prev is { } p) maxJump = Math.Max(maxJump, DistM(p.lat, p.lon, tgt.lat, tgt.lon));
        prev = tgt;
    }
    Check(maxJump < 1.0, $"hairpin: creeping 0.05 m steps near old 50 m boundary -> target moves < 1 m/step (got {maxJump:F2} m; old code jumped 102 m)");
}

// ---------------------------------------------------------------------------
// Case 6 — rollout anticipation math.
// ---------------------------------------------------------------------------
// Turning right at 8°/s toward a target 12° right: projected error is
// 12 − 8×1.5 = 0 → tone centred while still 12° short = "unwind now".
Check(Math.Abs(GuidanceGeometry.ProjectHeadingError(12.0, 8.0, 1.5, 30.0)) < 0.01,
      "rate-lead: centres 12° early at 8°/s");
// Not turning: projection is a no-op.
Check(Math.Abs(GuidanceGeometry.ProjectHeadingError(12.0, 0.0, 1.5, 30.0) - 12.0) < 0.01,
      "rate-lead: no-op when not yawing");
// Noise clamp: a 100°/s spike contributes at most ±30°.
Check(Math.Abs(GuidanceGeometry.ProjectHeadingError(0.0, 100.0, 1.5, 30.0) - (-30.0)) < 0.01,
      "rate-lead: clamped against rate spikes");

Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
Environment.Exit(failures == 0 ? 0 : 1);
