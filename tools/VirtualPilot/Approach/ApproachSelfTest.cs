using System.Globalization;

namespace VirtualPilot.Approach;

/// <summary>
/// Checks of the approach harness itself (`approach --selftest`): the aircraft model's turn,
/// path and wind arithmetic, the SimConnect sign conventions it feeds visual guidance, and the
/// runway geometry. Each prints PASS or FAIL; the return value is the failure count.
/// </summary>
static class ApproachSelfTest
{
    public static int Run()
    {
        var checks = new List<(string Name, Func<string?> Check)>
        {
            ("turn-right", () => TurnCheck(+25, +36.4)),
            ("turn-left", () => TurnCheck(-25, -36.4)),
            ("path-3deg", PathCheck),
            ("wind-drift", WindDriftCheck),
            ("simframe-signs", SimFrameSignsCheck),
            ("wrap-north", WrapNorthCheck),
            ("locate", LocateCheck),
            ("great-circle", GreatCircleCheck),
            ("delay", DelayCheck),
            ("roll-rate", RollRateCheck),
            ("deadband", DeadbandCheck),
            ("saturated-uses-callout", SaturatedUsesCalloutCheck),
            ("saturated-no-callout", SaturatedNoCalloutCheck),
            ("speech-clock", SpeechClockCheck),
            ("fps-all-complete", FpsAllCompleteCheck),
            ("results-invariant", ResultsInvariantCheck),
            ("matrix-size", MatrixSizeCheck),
            ("step-feedback-measured", StepFeedbackMeasuredCheck),
            ("touchdown-at-wheels", TouchdownAtWheelsCheck),
            ("far-end-ends-run", FarEndEndsRunCheck),
            ("downwind-mirror", DownwindMirrorCheck),
            ("landing-classified", LandingClassifiedCheck),
            ("rate-rms", RateRmsCheck),
            ("noise-feed-only", NoiseFeedOnlyCheck),
            ("noise-deterministic", NoiseDeterministicCheck),
            ("touchdown-sink", TouchdownSinkCheck),
        };

        int failures = 0;
        foreach (var (name, check) in checks)
        {
            string? detail;
            try { detail = check(); }
            catch (Exception ex) { detail = ex.GetType().Name + ": " + ex.Message; }
            if (detail == null) Console.WriteLine($"PASS {name}");
            else { Console.WriteLine($"FAIL {name}: {detail}"); failures++; }
        }
        Console.WriteLine(failures == 0 ? $"{checks.Count} checks passed" : $"{failures} of {checks.Count} checks failed");
        return failures;
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static double HeadingChange(double from, double to) => ((to - from) % 360 + 540) % 360 - 180;

    // g·tan 25° / 72.02 m/s = 3.638°/s, so 10 s of 25° bank turns the jet 36.4°; right bank turns right.
    private static string? TurnCheck(double bankStd, double expectedChange)
    {
        var rwy = ApproachRunways.B;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 10, 0);
        var spec = ApproachAircraftSpecs.For(ApproachAircraftKind.Jet);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 3000, 268, 0, 0);
        for (int i = 0; i < 100; i++) ac.Step(0.1, bankStd, spec.AlphaTrimDeg);
        double change = HeadingChange(268, ac.HeadingTrueDeg);
        return Math.Abs(change - expectedChange) <= 0.5 ? null : $"heading changed {F(change)}°, expected {F(expectedChange)}° ±0.5";
    }

    // Pitch at alpha-trim − 3 settles the flight path at −3°: 140 kt × 101.27 × sin 3° = 742 fpm down.
    private static string? PathCheck()
    {
        var rwy = ApproachRunways.B;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 10, 0);
        var spec = ApproachAircraftSpecs.For(ApproachAircraftKind.Jet);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 3000, 268, 0, 0);
        for (int i = 0; i < 150; i++) ac.Step(0.1, 0, spec.AlphaTrimDeg - 3);
        return Math.Abs(ac.VsFpm - -742) <= 10 ? null : $"VS {F(ac.VsFpm)} fpm, expected -742 ±10";
    }

    // 140 kt east plus 15 kt north (wind from 180): drift atan(15/140) = 6.1° left, track 083.9.
    private static string? WindDriftCheck()
    {
        var rwy = ApproachRunways.B;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 10, 0);
        var spec = ApproachAircraftSpecs.For(ApproachAircraftKind.Jet);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 3000, 90, 180, 15);
        ac.Step(0.1, 0, spec.AlphaTrimDeg);
        return Math.Abs(ac.TrackTrueDeg - 83.9) <= 0.3 ? null : $"track {F(ac.TrackTrueDeg)}°, expected 083.9 ±0.3";
    }

    // SimConnect conventions: bank is left-positive, pitch nose-down positive, both in radians.
    private static string? SimFrameSignsCheck()
    {
        var rwy = ApproachRunways.B;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 10, 0);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 3000, 268, 0, 0);
        ac.Step(0.01, 10, 5);
        var f = ac.ToSimFrame();
        var problems = new List<string>();
        if (Math.Abs(f.BankRad - -0.1745) > 0.0005) problems.Add($"BankRad {F(f.BankRad)}, expected -0.1745");
        if (Math.Abs(f.PitchRad - -0.0873) > 0.0005) problems.Add($"PitchRad {F(f.PitchRad)}, expected -0.0873");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Review Focus 1: runway A's course is true 004 with 9° E variation, so magnetic 355.
    private static string? WrapNorthCheck()
    {
        var rwy = ApproachRunways.A;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 10, 0);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 4500, 4, 0, 0);
        var f = ac.ToSimFrame();
        var problems = new List<string>();
        if (Math.Abs(HeadingChange(355, f.HeadingMagDeg)) > 0.01) problems.Add($"HeadingMagDeg {F(f.HeadingMagDeg)}, expected 355");
        if (Math.Abs(HeadingChange(355, f.GroundTrackMagDeg)) > 0.01) problems.Add($"GroundTrackMagDeg {F(f.GroundTrackMagDeg)}, expected 355");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    private static string? LocateCheck()
    {
        var problems = new List<string>();
        foreach (var rwy in ApproachRunways.All)
        {
            foreach (double cross in new[] { 0.5, -0.5 })
            {
                var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 8, cross);
                var (along, x) = ApproachRunways.Locate(rwy, lat, lon);
                if (Math.Abs(along - 8) > 0.01 || Math.Abs(x - cross) > 0.01)
                    problems.Add($"runway {rwy.Id} (8, {F(cross)}) located at ({F(along)}, {F(x)})");
            }
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Wings level is a great circle, so an aircraft started on the centreline along its local
    // direction stays on it. Runway B's course is east-west at 40° N, where a constant true heading
    // (a rhumb line) would drift about 40 ft off in 7.5 nm.
    private static string? GreatCircleCheck()
    {
        var rwy = ApproachRunways.B;
        var (lat, lon) = ApproachRunways.PointOnApproach(rwy, 8, 0);
        double localCourse = MSFSBlindAssist.Navigation.NavigationCalculator.CalculateBearing(lat, lon, rwy.Runway.StartLat, rwy.Runway.StartLon);
        var spec = ApproachAircraftSpecs.For(ApproachAircraftKind.Jet);
        var ac = new ApproachAircraft(ApproachAircraftKind.Jet, rwy, lat, lon, 3000, localCourse, 0, 0);
        int steps = (int)Math.Round(7.5 / 140.0 * 3600 * 30);
        for (int i = 0; i < steps; i++) ac.Step(1.0 / 30, 0, spec.AlphaTrimDeg);
        var (along, cross) = ApproachRunways.Locate(rwy, ac.Lat, ac.Lon);
        return Math.Abs(cross) < 0.0005 ? null : $"at {F(along)} nm out the aircraft is {F(cross * 6076.12)} ft off the centreline, expected under 3";
    }

    // ── The tone-following pilot ──────────────────────────────────────────────────────────

    private const double PilotDt = 1.0 / 30;
    private static readonly IReadOnlyList<(double T, string Text, bool Immediate)> NoSpeech = Array.Empty<(double, string, bool)>();

    private static TonePilot JetPilot(double delayS, double bank, double pitch)
        => new(ApproachAircraftKind.Jet, new MSFSBlindAssist.Aircraft.VisualGuidanceProfile(), delayS, bank, pitch);

    // Flies the pilot from t0 to tEnd against fixed commands; returns the attitude at each requested time.
    private static Dictionary<double, (double Bank, double Pitch)> FlyPilot(TonePilot pilot, double t0, double tEnd,
        double desiredBank, double desiredPitch, IReadOnlyList<(double T, string Text, bool Immediate)> speech, params double[] sampleAt)
    {
        var samples = new Dictionary<double, (double, double)>();
        int steps = (int)Math.Round((tEnd - t0) / PilotDt);
        (double Bank, double Pitch) att = default;
        for (int i = 0; i <= steps; i++)
        {
            double t = t0 + i * PilotDt;
            att = pilot.Step(t, PilotDt, desiredBank, desiredPitch, speech);
            foreach (double s in sampleAt)
                if (!samples.ContainsKey(s) && t >= s - 1e-9) samples[s] = att;
        }
        return samples;
    }

    // 1.0 s reaction delay: a pitch command at t = 0 is not acted on before t = 1.
    private static string? DelayCheck()
    {
        var s = FlyPilot(JetPilot(1.0, 0, 0), 0, 3, 0, 3, NoSpeech, 0.9, 3.0);
        var problems = new List<string>();
        if (Math.Abs(s[0.9].Pitch) > 0.01) problems.Add($"pitch at 0.9 s {F(s[0.9].Pitch)}, expected 0");
        if (s[3.0].Pitch <= 1) problems.Add($"pitch at 3 s {F(s[3.0].Pitch)}, expected > 1");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // The jet pilot rolls at no more than 5°/s.
    private static string? RollRateCheck()
    {
        var s = FlyPilot(JetPilot(0, 0, 0), 0, 0.5, 4, 0, NoSpeech, 0.5);
        return s[0.5].Bank <= 2.5 ? null : $"bank after 0.5 s {F(s[0.5].Bank)}, expected ≤ 2.5";
    }

    // Under half a degree of bank and a tenth of a degree of pitch, the pilot hears no difference.
    private static string? DeadbandCheck()
    {
        var s = FlyPilot(JetPilot(0, 0, 0), 0, 3, 0.4, 0.08, NoSpeech, 3.0);
        var problems = new List<string>();
        if (s[3.0].Bank != 0) problems.Add($"bank {F(s[3.0].Bank)}, expected 0");
        if (s[3.0].Pitch != 0) problems.Add($"pitch {F(s[3.0].Pitch)}, expected 0");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Both tones pinned right (bank 20, desired 30): only the callout says how far; "3 right" takes
    // the pilot to 23, and once the line is over 2 s old the two pinned tones sound alike, so they hold.
    private static string? SaturatedUsesCalloutCheck()
    {
        const double t0 = 10.0;
        var speech = new List<(double T, string Text, bool Immediate)> { (t0 - 0.5, "3 right", true) };
        var s = FlyPilot(JetPilot(0, 20, 0), t0, t0 + 5, 30, 0, speech, t0 + 2.5, t0 + 5);
        var problems = new List<string>();
        foreach (double at in new[] { t0 + 2.5, t0 + 5 })
            if (Math.Abs(s[at].Bank - 23) > 0.5) problems.Add($"bank at {F(at - t0)} s {F(s[at].Bank)}, expected 23 ±0.5");
        if (Math.Abs(s[t0 + 5].Bank - s[t0 + 2.5].Bank) > 0.05) problems.Add("the bank kept moving after the callout aged out");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Desired 30 with no callout: the pilot rolls until the current tone pins too, at 5°, and stays.
    private static string? SaturatedNoCalloutCheck()
    {
        var s = FlyPilot(JetPilot(0, 0, 0), 0, 5, 30, 0, NoSpeech, 3.0, 5.0);
        var problems = new List<string>();
        foreach (double at in new[] { 3.0, 5.0 })
            if (Math.Abs(s[at].Bank - 5) > 0.5) problems.Add($"bank at {F(at)} s {F(s[at].Bank)}, expected 5 ±0.5");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Review Focus 4: every captured line carries simulated time.
    private static string? SpeechClockCheck()
    {
        double clock = 12.5;
        var speech = new ApproachSpeech(() => clock);
        speech.AnnounceImmediate("matched");
        if (speech.Lines.Count != 1) return $"{speech.Lines.Count} lines captured, expected 1";
        var line = speech.Lines[0];
        return line.T == 12.5 && line.Text == "matched" && line.Immediate ? null : $"captured ({F(line.T)}, {line.Text}, {line.Immediate})";
    }

    // ── Scenarios and results ─────────────────────────────────────────────────────────────

    private static ApproachScenario Scenario(string name)
        => ApproachScenarios.All().Single(s => s.Name == name);

    // Review Focus 2: the same scenario at 20, 30 and 60 frames a second runs to an end and writes
    // a results line, whatever today's guidance does with the frame rate.
    private static string? FpsAllCompleteCheck()
    {
        var problems = new List<string>();
        foreach (string name in new[] { "FPS-A-JET-f20", "FPS-A-JET-f30", "FPS-A-JET-f60" })
        {
            string line = ApproachRunner.RunOne(Scenario(name), trace: null).ToResultsLine(name);
            if (!line.StartsWith("VG|" + name + "|", StringComparison.Ordinal) || !line.Contains("|endReason=", StringComparison.Ordinal))
                problems.Add($"{name}: {line}");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // Review Focus 5: a Swedish or German PC still writes '.' decimals, or the compare script misreads them.
    private static string? ResultsInvariantCheck()
    {
        var saved = CultureInfo.CurrentCulture;
        string line;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            line = ApproachRunner.RunOne(Scenario("FINAL-A-JET-x0-h0-d0.5"), trace: null).ToResultsLine("FINAL-A-JET-x0-h0-d0.5");
        }
        finally { CultureInfo.CurrentCulture = saved; }

        if (line.Contains(',')) return "results line contains ','";
        var problems = new List<string>();
        foreach (string field in line.Split('|').Skip(2))
        {
            int eq = field.IndexOf('=');
            if (eq < 0) { problems.Add($"field without '=': {field}"); continue; }
            string key = field[..eq], value = field[(eq + 1)..];
            if (key is "endReason" or "established" || value == "NA") continue;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                problems.Add($"{key}={value} does not parse");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    private static string? MatrixSizeCheck()
    {
        var all = ApproachScenarios.All();
        int unique = all.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count();
        return all.Count == 151 && unique == 151 ? null : $"{all.Count} scenarios, {unique} unique names; expected 151";
    }

    // The rate measure does not depend on the sample rate for a smooth series: a ramp of 2 per
    // second reads 2.0 at 20, 30 and 60 Hz. A series flipping 0, 1, 0, 1 at 30 Hz reads 30 per second.
    private static string? RateRmsCheck()
    {
        var problems = new List<string>();
        foreach (int hz in new[] { 20, 30, 60 })
        {
            var ramp = Enumerable.Range(0, 3 * hz + 1).Select(i => (T: (double)i / hz, V: 2.0 * i / hz)).ToList();
            double rate = ApproachMetrics.RateRms(ramp);
            if (!(Math.Abs(rate - 2.0) <= 1e-9)) problems.Add($"ramp at {hz} Hz reads {F(rate)}, expected 2.0");
        }
        var flip = Enumerable.Range(0, 90).Select(i => (T: i / 30.0, V: (double)(i % 2))).ToList();
        double flipRate = ApproachMetrics.RateRms(flip);
        if (!(Math.Abs(flipRate - 30.0) <= 1e-9)) problems.Add($"0,1,0,1 at 30 Hz reads {F(flipRate)}, expected 30");
        if (ApproachMetrics.RateRms(new[] { (0.0, 5.0) }) != 0) problems.Add("one point reads non-zero");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // The sensor noise reaches only the three values it is meant for, with the stated spread and no
    // bias. The frame's track sits next to north so the wrap to 0–360 is exercised.
    private static string? NoiseFeedOnlyCheck()
    {
        var f = new VgSimFrame(Lat: 47.1, Lon: 8.2, AltMslFt: 2500, HeadingMagDeg: 359.0, GroundTrackMagDeg: 359.95,
            GsKt: 140, VsFpm: -742, AglFt: 2000, PitchRad: -0.01, BankRad: 0.02, AlphaRad: 0.05);
        var noise = new ApproachSensorNoise(ApproachSensorNoise.Seed);
        const int n = 20000;
        var vs = new double[n]; var alpha = new double[n]; var track = new double[n];
        var problems = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var g = noise.Apply(f);
            if (g.Lat != f.Lat || g.Lon != f.Lon || g.AltMslFt != f.AltMslFt || g.HeadingMagDeg != f.HeadingMagDeg
                || g.GsKt != f.GsKt || g.AglFt != f.AglFt || g.PitchRad != f.PitchRad || g.BankRad != f.BankRad)
            {
                problems.Add($"sample {i} changed a field the noise must leave alone: {g}");
                break;
            }
            if (g.GroundTrackMagDeg < 0 || g.GroundTrackMagDeg >= 360) { problems.Add($"track {F(g.GroundTrackMagDeg)} outside 0–360"); break; }
            vs[i] = g.VsFpm - f.VsFpm;
            alpha[i] = (g.AlphaRad - f.AlphaRad) * 180 / Math.PI;
            track[i] = HeadingChange(f.GroundTrackMagDeg, g.GroundTrackMagDeg);
        }
        void Spread(string name, double[] d, double sigma)
        {
            double mean = d.Average();
            double sd = Math.Sqrt(d.Sum(x => (x - mean) * (x - mean)) / (d.Length - 1));
            if (Math.Abs(sd - sigma) > 0.05 * sigma) problems.Add($"{name} spread {F(sd)}, expected {F(sigma)} ±5 %");
            if (Math.Abs(mean) > 0.05 * sigma) problems.Add($"{name} mean {F(mean)}, expected 0 ±{F(0.05 * sigma)}");
        }
        if (problems.Count == 0)
        {
            Spread("VS", vs, ApproachSensorNoise.VsSigmaFpm);
            Spread("alpha", alpha, ApproachSensorNoise.AlphaSigmaDeg);
            Spread("track", track, ApproachSensorNoise.TrackSigmaDeg);
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // A landing writes its sink rate at wheel contact, in feet per minute down (positive); a run
    // that never lands writes NA.
    private static string? TouchdownSinkCheck()
    {
        string Field(string line, string key) => line.Split('|').FirstOrDefault(f => f.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "missing";
        var problems = new List<string>();
        const string landing = "FINAL-A-JET-x0-h0-d0.5", step = "STEP-A-JET";
        string l = ApproachRunner.RunOne(Scenario(landing), trace: null).ToResultsLine(landing);
        string v = Field(l, "touchdownVsFpm");
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double sink) || sink <= 0 || sink > 2000)
            problems.Add($"{landing} touchdownVsFpm={v}, expected a sink between 0 and 2000 fpm");
        string s = Field(ApproachRunner.RunOne(Scenario(step), trace: null).ToResultsLine(step), "touchdownVsFpm");
        if (s != "NA") problems.Add($"{step} touchdownVsFpm={s}, expected NA");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // A noisy run is as repeatable as any other: the noise is seeded per run.
    private static string? NoiseDeterministicCheck()
    {
        const string name = "NOISE-A-JET-f30";
        string a = ApproachRunner.RunOne(Scenario(name), trace: null).ToResultsLine(name);
        string b = ApproachRunner.RunOne(Scenario(name), trace: null).ToResultsLine(name);
        return a == b ? null : $"two runs differ:\n  {a}\n  {b}";
    }

    // The run ends when the WHEELS reach the ground: visual guidance puts the main gear the profile's
    // FlareAltitudeBiasFt below the datum, so the datum is then at that height, within one frame.
    private static string? TouchdownAtWheelsCheck()
    {
        const string name = "FINAL-A-JET-x0-h0-d0.5";
        var m = ApproachRunner.RunOne(Scenario(name), trace: null);
        double bias = new MSFSBlindAssist.Aircraft.VisualGuidanceProfile().FlareAltitudeBiasFt;
        bool atWheels = m.EndDatumAglFt <= bias && m.EndDatumAglFt > bias - 1.0;
        return atWheels ? null : $"{name} ended ({m.EndReason}) with the datum at {F(m.EndDatumAglFt)} ft, expected just under {F(bias)} (wheel contact)";
    }

    // A run still airborne past the runway's far end ends there, rather than flying on to the timeout.
    private static string? FarEndEndsRunCheck()
    {
        const string name = "FINAL-A-LIGHT-x0-h0-d0.5";
        var sc = Scenario(name);
        var m = ApproachRunner.RunOne(sc, trace: null);
        double farEndNm = -sc.Runway.Runway.Length / 6076.12;
        return m.EndAlongNm >= farEndNm - 0.01 ? null : $"{name} ended ({m.EndReason}) {F(-m.EndAlongNm)} nm past the threshold, beyond the far end at {F(-farEndNm)} nm";
    }

    // A downwind from the left and one from the right are mirror images: the same ending, about the
    // same time, on their own sides. The start must not sit where a rounding decides the phase.
    private static string? DownwindMirrorCheck()
    {
        var problems = new List<string>();
        foreach (string rwy in new[] { "A", "B" })
        {
            string left = $"DW-{rwy}-JET-L-d0.5", right = $"DW-{rwy}-JET-R-d0.5";
            string l = ApproachRunner.RunOne(Scenario(left), trace: null).ToResultsLine(left);
            string r = ApproachRunner.RunOne(Scenario(right), trace: null).ToResultsLine(right);
            string Field(string line, string key) => line.Split('|').FirstOrDefault(f => f.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? "NA";
            double Num(string line, string key) => Field(line, key) is var v && v != "NA" ? double.Parse(v, CultureInfo.InvariantCulture) : double.NaN;
            // Passing the runway's far end in the flare and touching down within 100 ft of it are the
            // same ending: which line the last frame crosses first is a coincidence of that frame.
            double farEndFt = -ApproachRunways.All.First(x => x.Id == rwy).Runway.Length;
            string Ending(string line) => Field(line, "endReason") == "pastRunwayEnd" || Math.Abs(Num(line, "touchdownAlongFt") - farEndFt) <= 100
                ? "at the far end" : Field(line, "endReason");
            if (Ending(l) != Ending(r))
                problems.Add($"runway {rwy}: {Ending(l)} on the left, {Ending(r)} on the right");
            if (Math.Abs(Num(l, "durationS") - Num(r, "durationS")) > 10)
                problems.Add($"runway {rwy}: {F(Num(l, "durationS"))} s on the left, {F(Num(r, "durationS"))} s on the right");
            double xl = Num(l, "touchdownXteFt"), xr = Num(r, "touchdownXteFt");
            if (!double.IsNaN(xl) && !double.IsNaN(xr) && !(Math.Abs(xl) < 30 && Math.Abs(xr) < 30) && Math.Sign(xl) == Math.Sign(xr))
                problems.Add($"runway {rwy}: both landed on the same side ({F(xl)} ft, {F(xr)} ft)");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // "touchdown" means on the runway's pavement (2 nm by 150 ft); everything else names where it landed.
    private static string? LandingClassifiedCheck()
    {
        var r = ApproachRunways.A;
        const double Ft = 1.0 / 6076.12;
        var cases = new (double Along, double Cross, string Expected)[]
        {
            (0.1, 0, "landedShort"), (-0.3, 0, "touchdown"), (-0.3, 50 * Ft, "touchdown"),
            (-0.3, -100 * Ft, "offRunway"), (-2.1, 0, "landedLong"),
        };
        var problems = cases
            .Where(c => ApproachRunner.LandingEndReason(r, c.Along, c.Cross) != c.Expected)
            .Select(c => $"({F(c.Along)} nm, {F(c.Cross / Ft)} ft) is {ApproachRunner.LandingEndReason(r, c.Along, c.Cross)}, expected {c.Expected}")
            .ToList();
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // A step run records its sample at step + 10 s before it ends, so both self-feedback measures exist.
    private static string? StepFeedbackMeasuredCheck()
    {
        var problems = new List<string>();
        foreach (string name in new[] { "STEP-A-JET", "STEP-A-LIGHT" })
        {
            string line = ApproachRunner.RunOne(Scenario(name), trace: null).ToResultsLine(name);
            foreach (string key in new[] { "selfFeedback3s", "selfFeedback10s" })
                if (!line.Contains("|" + key + "=", StringComparison.Ordinal) || line.Contains("|" + key + "=NA", StringComparison.Ordinal))
                    problems.Add($"{name} has no {key}");
        }
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }
}
