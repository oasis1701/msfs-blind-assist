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
/// level instead of on the glide path's angle. <paramref name="Descent"/> scripts a pilot obeying ATC's
/// "descend and maintain" (<see cref="ApproachVerticalScript"/>), and the run starts on its descent.
/// <paramref name="Slowing"/> slows the aircraft from <paramref name="TasKt"/> through the approach
/// (<see cref="ApproachSlowing"/>); without it the speed is held to touchdown.
/// </summary>
sealed record ApproachScenario(string Name, ApproachRunway Runway, ApproachAircraftKind Kind, double AlongNm, double CrossNm,
    double HeadingRelDeg, double HeightOffsetFt, double WindFromRelDeg, double WindKt, int Fps, double ReactionDelayS,
    double? PitchStepAtS, double? TasKt, bool SensorNoise = false, double GustBankDeg = 0, bool BankBySpeech = false,
    double StartBankDeg = 0, IReadOnlyList<(double AtS, double ToRelHeadingDeg)>? Turns = null, bool StartLevel = false,
    ApproachVerticalScript? Descent = null, ApproachSlowing? Slowing = null);

/// <summary>The scenario matrix: 287 approaches (docs/virtual-pilot.md, "Approaches").</summary>
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
        AddVerticalFamilies(list);
        AddDatumFamily(list, kinds);
        return list;
    }

    // Where the path starts and how steep it is (2026-11-06): runway C's displaced threshold (3,471 ft,
    // KJFK 22R's) and runway D's PAPI at 4° with no ILS. On final on the path and 200 ft off it, and on a
    // 30° intercept, both aircraft, the 0.5 s pilot. Measured against the landing threshold and the
    // runway's own angle, so visual guidance aiming at the runway end, or flying 3°, shows as path error.
    private static void AddDatumFamily(List<ApproachScenario> list, ApproachAircraftKind[] kinds)
    {
        const double d = 0.5;
        var heights = new (string Name, double Ft)[] { ("h0", 0), ("h+200", 200), ("h-200", -200) };
        foreach (var r in ApproachRunways.Datum)
            foreach (var k in kinds)
            {
                foreach (var h in heights)
                    list.Add(Final($"DATUM-{r.Id}-{KindName(k)}-{h.Name}", r, k, 0, h.Ft, d));
                foreach (char side in new[] { 'L', 'R' })
                    list.Add(Intercept($"DATUM-{r.Id}-{KindName(k)}-INT-{side}30", r, k, side, 30, d));
            }
    }

    // The vertical path (visual guidance stage 5): finals that start high or low, joins after ATC's
    // "descend and maintain", and a vectoring start well above the path. Runway A, the 0.5 s pilot.
    private static void AddVerticalFamilies(List<ApproachScenario> list)
    {
        var a = ApproachRunways.A;
        const double d = 0.5;
        static double Sign(char side) => side == 'R' ? 1 : -1;
        var jet = ApproachAircraftKind.Jet;
        var light = ApproachAircraftKind.Light;

        // On the centreline and the course, at that distance and that far off the path, already
        // descending at the path's angle. "Too high" is due on a4 (jet) and a2 (light), "Too low" on
        // a3-h-400 (jet) and a1.5 (light), neither on the others.
        foreach (var (k, along, height) in new[]
                 {
                     (jet, 8.0, 600.0), (jet, 4.0, 600.0), (jet, 3.0, -400.0), (jet, 6.0, -250.0),
                     (light, 4.0, 300.0), (light, 2.0, 400.0), (light, 1.5, -150.0), (light, 3.0, -100.0),
                 })
            list.Add(new($"VERT-A-{KindName(k)}-a{Inv(along, "0.##")}-h{Inv(height, "+0;-0")}", a, k, along, 0, 0, height,
                0, 0, 30, d, null, null));

        // ATC's "descend and maintain" on the 30° intercept: the jet 1,000 ft above the path, the light
        // aircraft 500, descending to a level near the path at the join (DESC) or well above it (DESCHIGH).
        foreach (var k in new[] { jet, light })
            foreach (char side in new[] { 'L', 'R' })
            {
                bool isJet = k == jet;
                var (along, offset) = InterceptStart(k);
                double s = Sign(side);
                double above = isJet ? 1000 : 500, vs = isJet ? -1500 : -700;
                foreach (var (family, levelAt) in new[] { ("DESC", isJet ? 2500.0 : 1300.0), ("DESCHIGH", isJet ? 3500.0 : 1900.0) })
                    list.Add(new ApproachScenario($"{family}-A-{KindName(k)}-{side}", a, k, along, s * offset, -s * 30, above,
                        0, 0, 30, d, null, null) with { Descent = new ApproachVerticalScript(vs, levelAt) });
            }

        // The vectoring start flying parallel inbound, level, the jet 1,000 ft and the light aircraft 400 ft above the path.
        foreach (var k in new[] { jet, light })
            foreach (char side in new[] { 'L', 'R' })
            {
                bool isJet = k == jet;
                list.Add(new ApproachScenario($"VECHIGH-A-{KindName(k)}-{side}-par", a, k, isJet ? 10.0 : 5.0,
                    Sign(side) * (isJet ? 2.0 : 1.0), 0, isJet ? 1000 : 400, 0, 0, 30, d, null, null) with { StartLevel = true });
            }

        // The vectoring start far abeam ("cleared visual" from well beside final), flying parallel inbound,
        // level at the path's height for its distance along: the jet 9 nm out and 4 nm abeam, the light
        // aircraft 4.5 nm out and 2 nm abeam. Here the path at the along-course distance is far under the
        // planned descent (the stage-5 final review's finding 1); the VEC starts, 2 nm and 1 nm abeam, never
        // reach that geometry.
        foreach (var k in new[] { jet, light })
            foreach (char side in new[] { 'L', 'R' })
            {
                bool isJet = k == jet;
                list.Add(new ApproachScenario($"VECWIDE-A-{KindName(k)}-{side}-par", a, k, isJet ? 9.0 : 4.5,
                    Sign(side) * (isJet ? 4.0 : 2.0), 0, 0, 0, 0, 30, d, null, null) with { StartLevel = true });
            }
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

        // Far side at speed (the first sim flight, 2026-11-06, Fenix A320 off KATL 27R): climbed out past
        // the far end and level about 4,000 ft above the field, flying away from it, at an airliner's clean
        // speeds. Every other pattern start flies 140 kt; the capture law's turn rate outruns a 25° bank
        // above about 180 kt.
        foreach (char side in new[] { 'L', 'R' })
            foreach (double tas in new[] { 210.0, 250.0 })
                foreach (double dly in Delays)
                    list.Add(new ApproachScenario($"FARFAST-A-JET-{side}-t{Inv(tas, "0")}-{DelayName(dly)}", a, ApproachAircraftKind.Jet,
                        -12.5, Sign(side) * 0.5, 0, AboveField(4000, -12.5), 0, 0, 30, dly, null, tas) with { StartLevel = true });

        // Slowing down (the owner's A320 visual approaches, 2026-11-06): pressed at 230 or 210 kt, 190 for the
        // turns, 180 on final, 140 from 6 to 4 nm (ApproachSlowing). The far side as the first sim flight; a
        // "cleared visual" 12 nm out and 4 nm beside final, flying parallel; ATC's 30° intercept; a downwind
        // abeam the runway 3 nm out, 2,000 ft above the field. The owner: approaches usually start 18 to 14 nm
        // out, so the "cleared visual" and the intercept also start there. On ATC's intercept the pilot descends
        // as cleared ("descend and maintain"), since joining leaves the altitude to ATC.
        var slowStarts = new (string Name, double Along, double Cross, double Rel, double Height, double Tas, bool Level,
            ApproachVerticalScript? Descent)[]
        {
            ("FAR", -12.5, 0.5, 0, AboveField(4000, -12.5), 230, true, null),
            ("VEC", 12.0, 4.0, 0, 0, 230, true, null),
            ("VEC14", 14.0, 4.0, 0, 0, 230, true, null),
            ("VEC18", 18.0, 4.0, 0, 0, 230, true, null),
            ("INT", 12.0, 3.0, -30, 0, 210, false, new ApproachVerticalScript(-1500, 2500)),
            ("INT14", 14.0, 3.0, -30, 0, 230, false, new ApproachVerticalScript(-1500, 3000)),
            ("INT18", 18.0, 4.0, -30, 0, 230, false, new ApproachVerticalScript(-1500, 3500)),
            ("DW", DownwindAlongNm, 3.0, 180, AboveField(2000, DownwindAlongNm), 210, false, null),
        };
        foreach (var st in slowStarts)
            foreach (char side in new[] { 'L', 'R' })
                foreach (double dly in Delays)
                    list.Add(new ApproachScenario($"SLOW-{st.Name}-A-JET-{side}-t{Inv(st.Tas, "0")}-{DelayName(dly)}", a,
                        ApproachAircraftKind.Jet, st.Along, Sign(side) * st.Cross, Sign(side) * st.Rel, st.Height, 0, 0, 30, dly,
                        null, st.Tas) with { StartLevel = st.Level, Slowing = new ApproachSlowing(), Descent = st.Descent });

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
