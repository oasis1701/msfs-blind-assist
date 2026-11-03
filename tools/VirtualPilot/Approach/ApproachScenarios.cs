using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace VirtualPilot.Approach;

/// <summary>
/// One simulated approach. Headings and wind directions are relative to the runway's true course;
/// cross is + right of the extended centreline facing the runway; the height offset is from the
/// 3° path's height at the start (<see cref="ApproachRunways.PathMslFt"/>).
/// </summary>
sealed record ApproachScenario(string Name, ApproachRunway Runway, ApproachAircraftKind Kind, double AlongNm, double CrossNm,
    double HeadingRelDeg, double HeightOffsetFt, double WindFromRelDeg, double WindKt, int Fps, double ReactionDelayS,
    double? PitchStepAtS, double? TasKt);

/// <summary>The scenario matrix: 147 approaches in seven families (docs/virtual-pilot.md, "Approaches").</summary>
static class ApproachScenarios
{
    private static readonly double[] Delays = { 0.5, 1.0 };
    private static IReadOnlyList<ApproachScenario>? _all;

    public static IReadOnlyList<ApproachScenario> All() => _all ??= Build();

    private static string Inv(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    private static string KindName(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? "JET" : "LIGHT";
    private static string DelayName(double d) => "d" + Inv(d, "0.0");

    // A height above the field, as an offset from the path height at the threshold (the glidepath bias).
    private static double AboveField(double ft) => ft - new VisualGuidanceProfile().GlideslopeAltitudeBiasFt;

    private static double FinalAlong(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? 8.0 : 4.0;
    private static (double Along, double Side) InterceptStart(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? (12.0, 3.0) : (6.0, 1.5);

    private static ApproachScenario Final(string name, ApproachRunway r, ApproachAircraftKind k, double cross, double height,
        double delay, int fps = 30, double windFromRel = 0, double windKt = 0, double? tas = null)
        => new(name, r, k, FinalAlong(k), cross, 0, height, windFromRel, windKt, fps, delay, null, tas);

    // Right of final (side +) heads course − angle to cut toward the line; left heads course + angle.
    private static ApproachScenario Intercept(string name, ApproachRunway r, ApproachAircraftKind k, char side, double angle,
        double delay, double windFromRel = 0, double windKt = 0, double? tas = null)
    {
        var (along, offset) = InterceptStart(k);
        double sign = side == 'R' ? 1 : -1;
        return new(name, r, k, along, sign * offset, -sign * angle, 0, windFromRel, windKt, 30, delay, null, tas);
    }

    private static IReadOnlyList<ApproachScenario> Build()
    {
        var list = new List<ApproachScenario>();
        var kinds = new[] { ApproachAircraftKind.Jet, ApproachAircraftKind.Light };
        var crosses = new (string Name, double Nm)[] { ("x0", 0), ("x0.2R", 0.2), ("x0.2L", -0.2) };
        var heights = new (string Name, double Ft)[] { ("h0", 0), ("h+200", 200), ("h-200", -200) };

        // Final: on the course, offset sideways and vertically.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (var x in crosses)
                    foreach (var h in heights)
                        foreach (double d in Delays)
                            list.Add(Final($"FINAL-{r.Id}-{KindName(k)}-{x.Name}-{h.Name}-{DelayName(d)}", r, k, x.Nm, h.Ft, d));

        // Intercept: on the path's height, cutting toward the line at 30, 45 and 90°.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                    foreach (double angle in new[] { 30.0, 45.0, 90.0 })
                        foreach (double d in Delays)
                            list.Add(Intercept($"INT-{r.Id}-{KindName(k)}-{side}{Inv(angle, "0")}-{DelayName(d)}", r, k, side, angle, d));

        // Downwind: abeam the threshold, flying the reciprocal, above the field.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                    foreach (double d in Delays)
                    {
                        bool jet = k == ApproachAircraftKind.Jet;
                        double offset = (jet ? 3.0 : 1.5) * (side == 'R' ? 1 : -1);
                        list.Add(new($"DW-{r.Id}-{KindName(k)}-{side}-{DelayName(d)}", r, k, 0, offset, 180,
                            AboveField(jet ? 2000 : 1000), 0, 0, 30, d, null, null));
                    }

        var a = ApproachRunways.A;
        var jetK = ApproachAircraftKind.Jet;

        // Wind: a 15 kt crosswind on final, and a 15 kt tailwind on a 90° base.
        list.Add(Final("WIND-XW-A-JET-d0.5", a, jetK, 0, 0, 0.5, windFromRel: 90, windKt: 15));
        list.Add(Intercept("WIND-TW-A-JET-d0.5", a, jetK, 'R', 90, 0.5, windFromRel: 90, windKt: 15));

        // Frame rate: the same final at 20, 30 and 60 frames a second.
        foreach (int fps in new[] { 20, 30, 60 })
            list.Add(Final($"FPS-A-JET-f{fps}", a, jetK, 0, 0, 0.5, fps));

        // Step: settled on the path at 6 nm, the pilot holds 1° below their pitch from 40 s.
        foreach (var k in kinds)
            list.Add(new($"STEP-A-{KindName(k)}", a, k, 6.0, 0, 0, 0, 0, 0, 30, 0.5, 40.0, null));

        // Speed: a fast jet and a fast light aircraft, on a 45° intercept and offset on final.
        foreach (var (k, tas, label) in new[] { (jetK, 160.0, "JET160"), (ApproachAircraftKind.Light, 90.0, "LIGHT90") })
        {
            list.Add(Intercept($"SPEED-A-{label}-R45-d0.5", a, k, 'R', 45, 0.5, tas: tas));
            list.Add(Final($"SPEED-A-{label}-x0.2R-h+200-d0.5", a, k, 0.2, 200, 0.5, tas: tas));
        }
        return list;
    }
}
