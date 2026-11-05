using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace VirtualPilot.Approach;

/// <summary>
/// One simulated approach. Headings and wind directions are relative to the runway's true course;
/// cross is + right of the extended centreline facing the runway; the height offset is from the
/// 3° path's height at the start (<see cref="ApproachRunways.PathMslFt"/>). With
/// <paramref name="SensorNoise"/>, visual guidance reads its inputs through <see cref="ApproachSensorNoise"/>.
/// With <paramref name="GustBankDeg"/> above 0, gusts knock the aircraft that many degrees left,
/// right and left (<see cref="ApproachRunner.GustAtS"/>); with <paramref name="BankBySpeech"/> the
/// pilot steers the bank by the callouts alone (<see cref="TonePilot"/>). <paramref name="StartBankDeg"/>
/// (right positive) starts the aircraft banked; <paramref name="Turns"/> script a pilot obeying ATC
/// (<see cref="ApproachScriptedTurns"/>); <paramref name="StartLevel"/> starts a course-parallel start
/// level instead of on the glide path's angle.
/// </summary>
sealed record ApproachScenario(string Name, ApproachRunway Runway, ApproachAircraftKind Kind, double AlongNm, double CrossNm,
    double HeadingRelDeg, double HeightOffsetFt, double WindFromRelDeg, double WindKt, int Fps, double ReactionDelayS,
    double? PitchStepAtS, double? TasKt, bool SensorNoise = false, double GustBankDeg = 0, bool BankBySpeech = false,
    double StartBankDeg = 0, IReadOnlyList<(double AtS, double ToRelHeadingDeg)>? Turns = null, bool StartLevel = false);

/// <summary>The scenario matrix: 203 approaches in fifteen families (docs/virtual-pilot.md, "Approaches").</summary>
static class ApproachScenarios
{
    private static readonly double[] Delays = { 0.5, 1.0 };
    private const double DownwindAlongNm = -1.0;   // abeam the middle of the 2 nm runway
    private static IReadOnlyList<ApproachScenario>? _all;

    public static IReadOnlyList<ApproachScenario> All() => _all ??= Build();

    private static string Inv(double v, string format) => v.ToString(format, CultureInfo.InvariantCulture);
    private static string KindName(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? "JET" : "LIGHT";
    private static string DelayName(double d) => "d" + Inv(d, "0.0");

    // A height above the field, as the scenario's offset from the path height at the start
    // (ApproachRunways.PathMslFt, which is below the field past the threshold).
    private static double AboveField(double ft, double alongNm)
        => ft - (alongNm * 6076.12 * Math.Tan(3.0 * Math.PI / 180) + new VisualGuidanceProfile().GlideslopeAltitudeBiasFt);

    private static double FinalAlong(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? 8.0 : 4.0;
    private static (double Along, double Side) InterceptStart(ApproachAircraftKind k) => k == ApproachAircraftKind.Jet ? (12.0, 3.0) : (6.0, 1.5);

    private static ApproachScenario Final(string name, ApproachRunway r, ApproachAircraftKind k, double cross, double height,
        double delay, int fps = 30, double windFromRel = 0, double windKt = 0, double? tas = null, bool noise = false)
        => new(name, r, k, FinalAlong(k), cross, 0, height, windFromRel, windKt, fps, delay, null, tas, noise);

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

        // Downwind: abeam mid-runway, flying the reciprocal, above the field. Not abeam the
        // threshold itself: there visual guidance's behind-the-runway test (bearing within 90° of
        // the course) is decided by rounding, so one side started Extending and the other not.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                    foreach (double d in Delays)
                    {
                        bool jet = k == ApproachAircraftKind.Jet;
                        double offset = (jet ? 3.0 : 1.5) * (side == 'R' ? 1 : -1);
                        list.Add(new($"DW-{r.Id}-{KindName(k)}-{side}-{DelayName(d)}", r, k, DownwindAlongNm, offset, 180,
                            AboveField(jet ? 2000 : 1000, DownwindAlongNm), 0, 0, 30, d, null, null));
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

        // Noise: the offset final with assumed sensor noise on what visual guidance reads, at 30
        // and 60 frames a second (ApproachSensorNoise).
        foreach (var k in kinds)
            foreach (int fps in new[] { 30, 60 })
                list.Add(Final($"NOISE-A-{KindName(k)}-f{fps}", a, k, 0.2, 200, 0.5, fps, noise: true));

        // Gust: on the centreline and the path, knocked 2° and 4° left, right, left on final, with
        // the tone-following pilot and with one who steers the bank by the callouts alone.
        foreach (var k in kinds)
            foreach (double g in new[] { 2.0, 4.0 })
                foreach (bool bySpeech in new[] { false, true })
                    list.Add(Final($"GUST-A-{KindName(k)}-g{Inv(g, "0")}-{(bySpeech ? "speech" : "tone")}", a, k, 0, 0, 0.5)
                        with { GustBankDeg = g, BankBySpeech = bySpeech });

        AddJoiningFamilies(list, kinds);
        return list;
    }

    // Joining final (visual guidance stage 4): positions BA must vector from, the far side of the
    // field, a cut that crosses final too close in, a press in a turn, an ATC heading that stops
    // crossing final, and a shallow ATC cut. Each side: + right of the centreline facing the runway.
    private static void AddJoiningFamilies(List<ApproachScenario> list, ApproachAircraftKind[] kinds)
    {
        var a = ApproachRunways.A;
        const double d = 0.5;
        static double Sign(char side) => side == 'R' ? 1 : -1;
        ApproachScenario At(string name, ApproachRunway r, ApproachAircraftKind k, double along, double cross, double rel, double height)
            => new(name, r, k, along, cross, rel, height, 0, 0, 30, d, null, null);

        // Vectoring: level at the path's height beside a long final, flying parallel inbound or outbound.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                    foreach (var (dir, rel) in new[] { ("par", 0.0), ("away", 180.0) })
                    {
                        bool jet = k == ApproachAircraftKind.Jet;
                        list.Add(At($"VEC-{r.Id}-{KindName(k)}-{side}-{dir}-{DelayName(d)}", r, k, jet ? 10.0 : 5.0,
                            Sign(side) * (jet ? 2.0 : 1.0), rel, 0) with { StartLevel = true });
                    }

        // Far side: past the runway's far end, beside it, flying back toward the field, above it.
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                {
                    bool jet = k == ApproachAircraftKind.Jet;
                    double along = jet ? -4.0 : -3.0;
                    list.Add(At($"FAR-{r.Id}-{KindName(k)}-{side}-{DelayName(d)}", r, k, along, Sign(side) * (jet ? 1.0 : 0.5), 180,
                        AboveField(jet ? 2000 : 1000, along)));
                }

        foreach (var k in kinds)
            foreach (char side in new[] { 'L', 'R' })
            {
                bool jet = k == ApproachAircraftKind.Jet;
                double s = Sign(side);
                var (intAlong, intSide) = InterceptStart(k);

                // Too close: cutting toward the line at 45°, crossing it inside the minimum final (jet 5.5 nm, light 0.8).
                list.Add(At($"CLOSE-A-{KindName(k)}-{side}-{DelayName(d)}", a, k, jet ? 7.5 : 1.8, s * (jet ? 2.0 : 1.0), -s * 45, 0));

                // Turning: the intercept start, parallel and banked 20° toward the line, turning to a 30° cut.
                list.Add(At($"TURN-A-{KindName(k)}-{side}-{DelayName(d)}", a, k, intAlong, s * intSide, 0, 0)
                    with { StartLevel = true, StartBankDeg = -s * 20, Turns = new[] { (0.0, -s * 30) } });

                // Heading change: on the 30° intercept, the pilot turns parallel at 20 s and to a 45° cut at 60 s.
                list.Add(At($"NOCROSS-A-{KindName(k)}-{side}-{DelayName(d)}", a, k, intAlong, s * intSide, -s * 30, 0)
                    with { Turns = new[] { (20.0, 0.0), (60.0, -s * 45) } });
            }

        // Shallow: a 15° cut from far out, crossing final at about 8.8 nm (jet) and 4.4 nm (light).
        foreach (var r in ApproachRunways.All)
            foreach (var k in kinds)
                foreach (char side in new[] { 'L', 'R' })
                {
                    bool jet = k == ApproachAircraftKind.Jet;
                    list.Add(At($"SHALLOW-{r.Id}-{KindName(k)}-{side}-{DelayName(d)}", r, k, jet ? 20.0 : 10.0,
                        Sign(side) * (jet ? 3.0 : 1.5), -Sign(side) * 15, 0));
                }
    }
}
