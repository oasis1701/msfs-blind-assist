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
        return all.Count == 147 && unique == 147 ? null : $"{all.Count} scenarios, {unique} unique names; expected 147";
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
