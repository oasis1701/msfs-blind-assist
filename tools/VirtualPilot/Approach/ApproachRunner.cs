using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils;

namespace VirtualPilot.Approach;

/// <summary>
/// The `approach` mode: flies simulated approaches through the real VisualGuidanceManager on two
/// synthetic runways, so it needs no navdata database. See docs/virtual-pilot.md, "Approaches".
/// </summary>
static class ApproachRunner
{
    private const double MaxRunS = 900.0, StepRunAfterS = 10.0, PitchStepDeg = 1.0, FtPerNm = 6076.12;
    private static readonly DateTime ClockStart = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>When a gust scenario's gusts start, in simulated seconds: left, right, left.</summary>
    public static readonly double[] GustAtS = { 40, 80, 120 };
    private static readonly int[] GustSigns = { -1, +1, -1 };

    /// <summary>Each gust's knock is spread evenly over this many seconds of frames.</summary>
    public const double GustRampS = 0.3;

    /// <summary>The first frame of each gust (<see cref="GustAtS"/> rounded to a frame) and its time, as the frame loop computes it.</summary>
    public static IEnumerable<(long Frame, double T, int Sign)> GustStarts(ApproachScenario sc)
    {
        double dt = 1.0 / sc.Fps;
        for (int k = 0; k < GustAtS.Length; k++)
        {
            long frame = (long)Math.Round(GustAtS[k] * sc.Fps);
            yield return (frame, frame * dt, GustSigns[k]);
        }
    }

    public static int Run(Options opt)
    {
        if (opt.SelfTest) return ApproachSelfTest.Run();
        if (opt.CaptureGain != null && CaptureGainHook == null)
        {
            Console.WriteLine("--capture-gain needs an app with VisualGuidanceManager.CaptureGainForHarness (visual guidance stage 4)");
            return 2;
        }
        CaptureGain = opt.CaptureGain;
        if (opt.Vertical != null)
        {
            if (VerticalTuningHook == null)
            {
                Console.WriteLine("--vertical needs an app with VisualGuidanceManager.VerticalTuningForHarness (visual guidance stage 5)");
                return 2;
            }
            VerticalTuning = BuildVerticalTuning(opt.Vertical, out string? error);
            if (VerticalTuning == null) { Console.WriteLine(error); return 2; }
        }
        else VerticalTuning = null;
        if (opt.Record != null && opt.Trace == null && opt.Scenario == null)
        {
            Console.WriteLine("--record needs --trace or --scenario: one recording per run, not the whole matrix");
            return 2;
        }
        // The flight recording (VP-2), as the app writes it to visual_guidance_flight.log.
        using var recording = opt.Record != null ? new StreamWriter(opt.Record) : null;
        Action<string>? record = recording != null ? recording.WriteLine : null;

        if (opt.Trace != null)
        {
            var sc = ApproachScenarios.All().FirstOrDefault(s => s.Name == opt.Trace)
                  ?? ApproachScenarios.All().FirstOrDefault(s => s.Name.Contains(opt.Trace, StringComparison.OrdinalIgnoreCase));
            if (sc == null) { Console.WriteLine($"no approach scenario matches {opt.Trace}"); return 2; }
            var m = RunOne(sc, Console.WriteLine, record);
            Console.WriteLine(m.ToResultsLine(sc.Name));
            return 0;
        }

        var scenarios = ApproachScenarios.All()
            .Where(s => opt.Scenario == null || s.Name.Contains(opt.Scenario, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var started = DateTime.Now;
        var endReasons = new SortedDictionary<string, int>(StringComparer.Ordinal);
        using (var results = new StreamWriter(Path.ChangeExtension(opt.Out, ".results.txt")))
        {
            int n = 0;
            foreach (var sc in scenarios)
            {
                var m = RunOne(sc, trace: null, record);
                results.WriteLine(m.ToResultsLine(sc.Name));
                endReasons[m.EndReason] = endReasons.TryGetValue(m.EndReason, out int c) ? c + 1 : 1;
                if (++n % 25 == 0 || n == scenarios.Count)
                    Console.WriteLine($"  {n}/{scenarios.Count}  ({(DateTime.Now - started).TotalSeconds.ToString("0", Inv)}s)");
            }
        }
        using (var outFile = new StreamWriter(opt.Out))
        {
            outFile.WriteLine($"# VirtualPilot approach — {scenarios.Count} scenarios — {DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)}");
            outFile.WriteLine($"# scenarios={scenarios.Count}");
            foreach (var kv in endReasons) outFile.WriteLine($"# endReason {kv.Key,-12} {kv.Value}");
        }
        Console.WriteLine($"approaches={scenarios.Count}  " + string.Join("  ", endReasons.Select(kv => $"{kv.Key}={kv.Value}")));
        return 0;
    }

    // Set by reflection, so this harness still builds against an app from before the recording
    // existed: the baseline procedure flies today's harness against the stage-1 app
    // (docs/virtual-pilot.md). The self-test's record-hook check fails if the hook is renamed.
    internal static readonly System.Reflection.PropertyInfo? FlightRecordHook = typeof(VisualGuidanceManager)
        .GetProperty("FlightRecordForHarness", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static readonly Action<string> NoRecord = _ => { };

    // Stage 4's hooks and profile fields, by reflection for the same reason: null against an older app,
    // where the profile setting is skipped and --capture-gain refuses to run. (Stage 4's sweep also
    // had --roll-in, the profile's RollInTimeS; 0 s was picked and the field removed.)
    private const System.Reflection.BindingFlags Hidden = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    internal static readonly System.Reflection.PropertyInfo? CaptureGainHook = typeof(VisualGuidanceManager).GetProperty("CaptureGainForHarness", Hidden);
    internal static readonly System.Reflection.PropertyInfo? DecisionHook = typeof(VisualGuidanceManager).GetProperty("DecisionForHarness", Hidden);
    private static readonly System.Reflection.PropertyInfo? MinimumFinalProperty = typeof(VisualGuidanceProfile).GetProperty("MinimumFinalNm");

    // Stage 5's hooks and profile field, by reflection for the same reason: null against an older app,
    // where the vertical measures read NA and the light runs keep the profile's gate.
    internal static readonly System.Reflection.PropertyInfo? VerticalModeHook = typeof(VisualGuidanceManager).GetProperty("VerticalModeForHarness", Hidden);
    internal static readonly System.Reflection.PropertyInfo? TargetDescentHook = typeof(VisualGuidanceManager).GetProperty("TargetDescentForHarness", Hidden);
    private static readonly System.Reflection.PropertyInfo? StableGateProperty = typeof(VisualGuidanceProfile).GetProperty("StableGateFt");
    internal static readonly System.Reflection.PropertyInfo? VerticalTuningHook = typeof(VisualGuidanceManager).GetProperty("VerticalTuningForHarness", Hidden);

    /// <summary>--vertical's tuning (a <c>VisualGuidanceVerticalTuning</c>, by reflection), for the whole run; null leaves the app's default.</summary>
    public static object? VerticalTuning { get; set; }

    /// <summary>
    /// The app's vertical tuning record with the named init properties set ("CorrectionTimeConstantS=27,
    /// TrimTimeConstantS=4"), the others at their defaults; null with <paramref name="error"/> for an
    /// unknown name or a value that is not a number.
    /// </summary>
    public static object? BuildVerticalTuning(string spec, out string? error)
    {
        error = null;
        var type = VerticalTuningHook?.PropertyType;
        if (type == null) { error = "the app has no vertical tuning"; return null; }
        object tuning = Activator.CreateInstance(type, nonPublic: true)!;
        var settable = type.GetProperties().Where(p => p.PropertyType == typeof(double) && p.CanWrite).ToDictionary(p => p.Name, StringComparer.Ordinal);
        foreach (string pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            string name = eq > 0 ? pair[..eq].Trim() : pair;
            if (!settable.TryGetValue(name, out var prop))
            {
                error = $"--vertical: no tuning number {name}; there are {string.Join(", ", settable.Keys)}";
                return null;
            }
            if (eq < 0 || !double.TryParse(pair[(eq + 1)..], NumberStyles.Float, Inv, out double value))
            {
                error = $"--vertical: {pair} has no number";
                return null;
            }
            prop.SetValue(tuning, value);
        }
        return tuning;
    }

    /// <summary>The light runs' minimum final: the circuit-sized final the spec gives light aircraft.</summary>
    public const double LightMinimumFinalNm = 1.0;

    /// <summary>The light runs' stable gate (ft above the threshold): the light aircraft's gate the owner chose on 2026-11-06.</summary>
    public const double LightStableGateFt = 300.0;

    /// <summary>The airliner gate, where the profile has no <c>StableGateFt</c> (before stage 5).</summary>
    public const double DefaultStableGateFt = 1000.0;

    /// <summary>The light runs' flare height (wheel height, ft): a light aircraft rounds out at about 5 to 15 ft (visual guidance stage 6).</summary>
    public const double LightFlareWheelFt = 10.0;

    /// <summary>--capture-gain, for the whole run; null leaves the app's default.</summary>
    public static double? CaptureGain { get; set; }

    /// <summary>The profile a scenario flies with: the A320 default, with the light runs' minimum final, stable gate and flare height.</summary>
    public static VisualGuidanceProfile ProfileFor(ApproachScenario sc)
    {
        // FlareTriggerWheelHeightFt is as old as the profile (stage 1 has it), so it is set directly;
        // the newer fields go by reflection.
        var profile = sc.Kind == ApproachAircraftKind.Light
            ? new VisualGuidanceProfile { FlareTriggerWheelHeightFt = LightFlareWheelFt }
            : new VisualGuidanceProfile();
        if (sc.Kind == ApproachAircraftKind.Light)
        {
            MinimumFinalProperty?.SetValue(profile, LightMinimumFinalNm);
            StableGateProperty?.SetValue(profile, LightStableGateFt);
        }
        return profile;
    }

    /// <summary>The decision visual guidance has made (OnFinal, Joining, Vectoring, Pattern), "None" before it, null against an app without one.</summary>
    public static string? DecisionOf(VisualGuidanceManager vg) => DecisionHook?.GetValue(vg) as string;

    /// <summary>The vertical mode (Follow, Hold, Plan, Path, Flare), null against an app without one.</summary>
    public static string? VerticalModeOf(VisualGuidanceManager vg) => VerticalModeHook?.GetValue(vg) as string;

    /// <summary>The target descent angle the vertical cue flies (degrees, + down), null against an app without one.</summary>
    public static double? TargetDescentOf(VisualGuidanceManager vg) => TargetDescentHook?.GetValue(vg) is double d ? d : null;

    /// <summary>The profile's stable gate, ft above the threshold; the airliner gate against an app without the field.</summary>
    public static double StableGateOf(VisualGuidanceProfile profile)
        => StableGateProperty?.GetValue(profile) is double g ? g : DefaultStableGateFt;

    /// <summary>
    /// The flight path a scenario starts on, degrees (+ climbing): a scripted descent's own; the glide
    /// path's angle for a start on the course; level for every other start (an intercept, a downwind,
    /// a vectoring start flying parallel).
    /// </summary>
    public static double StartGammaDeg(ApproachScenario sc)
    {
        if (sc.Descent is ApproachVerticalScript d)
        {
            double tas = sc.TasKt ?? ApproachAircraftSpecs.For(sc.Kind).TasKt;
            return Math.Asin(Math.Clamp(d.VsFpm / (tas * 101.269), -1, 1)) * 180 / Math.PI;
        }
        return sc.HeadingRelDeg == 0 && !sc.StartLevel ? -sc.Runway.PathDeg : 0.0;
    }

    /// <summary>
    /// Flies one scenario to its end. With <paramref name="trace"/>, prints a frame every 0.5 simulated
    /// seconds; with <paramref name="record"/>, receives the flight recording's lines (VP-2); with
    /// <paramref name="onFeed"/>, every frame fed to visual guidance, as fed.
    /// </summary>
    public static ApproachMetrics RunOne(ApproachScenario sc, Action<string>? trace, Action<string>? record = null,
        Action<VgSimFrame>? onFeed = null)
    {
        var rwy = sc.Runway;
        var profile = ProfileFor(sc);
        double t = 0;
        var savedClock = SimClock.Override;
        SimClock.Override = () => ClockStart.AddSeconds(t);
        try
        {
            var speech = new ApproachSpeech(() => t);
            var vg = new VisualGuidanceManager(speech) { HeadlessForHarness = true };
            FlightRecordHook?.SetValue(vg, record ?? NoRecord);
            if (CaptureGain is double gain) CaptureGainHook?.SetValue(vg, gain);
            if (VerticalTuning != null) VerticalTuningHook?.SetValue(vg, VerticalTuning);
            vg.Toggle();
            vg.Initialize(rwy.Runway, rwy.Airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, profile);

            var (lat, lon) = ApproachRunways.PointOnApproach(rwy, sc.AlongNm, sc.CrossNm);
            double alt = ApproachRunways.PathMslFt(rwy, sc.AlongNm, profile) + sc.HeightOffsetFt;
            double course = rwy.Runway.Heading;
            double startGamma = StartGammaDeg(sc);
            // Headings are relative to the centreline's direction where the aircraft starts; the
            // wind is relative to the threshold's course.
            var ac = new ApproachAircraft(sc.Kind, rwy, lat, lon, alt, ApproachRunways.LocalCourse(rwy, lat, lon) + sc.HeadingRelDeg,
                course + sc.WindFromRelDeg, sc.WindKt, sc.TasKt, startGamma, sc.StartBankDeg);
            var pilot = new TonePilot(sc.Kind, profile, sc.ReactionDelayS, sc.StartBankDeg, ac.PitchDeg, sc.BankBySpeech);
            var turns = sc.Turns != null ? new ApproachScriptedTurns(sc.Turns) : null;
            var descent = sc.Descent?.Start();
            var spec = ApproachAircraftSpecs.For(sc.Kind);
            double tasKt = sc.TasKt ?? spec.TasKt;
            var gusts = sc.GustBankDeg > 0 ? GustStarts(sc).ToList() : new List<(long Frame, double T, int Sign)>();
            int rampFrames = Math.Max(1, (int)Math.Round(GustRampS * sc.Fps));
            var metrics = new ApproachMetrics(sc, profile);
            // Noise reaches only what visual guidance reads; the aircraft, metrics and trace stay true.
            var noise = sc.SensorNoise ? new ApproachSensorNoise(ApproachSensorNoise.Seed) : null;

            double dt = 1.0 / sc.Fps;
            bool stepped = false;
            // A run ends past the runway's far end only once the aircraft has been over or before the
            // runway: a far-side start (the FAR family) begins past the far end, flying back toward it.
            double farEndNm = ApproachRunways.FarEndNm(rwy);
            bool reachedField = sc.AlongNm >= farEndNm;
            int printedHalfSeconds = -1, spokenPrinted = 0;
            string endReason;
            for (long frame = 0; ; frame++)
            {
                t = frame * dt;
                var frameIn = ac.ToSimFrame();
                var fed = noise != null ? noise.Apply(frameIn) : frameIn;
                onFeed?.Invoke(fed);
                VgFeed.Feed(vg, fed);
                // A scripted turn (a pilot obeying ATC) replaces the bank the pilot flies this frame.
                double? scripted = turns?.BankFor(t,
                    ((ac.HeadingTrueDeg - ApproachRunways.LocalCourse(rwy, ac.Lat, ac.Lon)) % 360 + 540) % 360 - 180);
                // A scripted descent (a pilot obeying ATC's "descend and maintain") replaces the pitch.
                double? scriptedPitch = descent?.PitchFor(t, ac.AglFt, ac.VsFpm, spec.AlphaTrimDeg, ac.TasKt, spec.TauGammaS);
                metrics.Record(t, ac, vg, scripted != null, scriptedPitch != null);   // the state and the command it produced, same frame
                // Checked after recording, so the step's 10 s sample exists for selfFeedback10s.
                if (sc.PitchStepAtS is double stepEnd && t >= stepEnd + StepRunAfterS) { endReason = "stepDone"; break; }

                if (trace != null && (int)Math.Floor(t / 0.5) > printedHalfSeconds)
                {
                    printedHalfSeconds = (int)Math.Floor(t / 0.5);
                    trace(TraceLine(t, sc, ac, vg, profile));
                    for (; spokenPrinted < speech.Lines.Count; spokenPrinted++)
                    {
                        var line = speech.Lines[spokenPrinted];
                        trace($"      say {line.T.ToString("0.00", Inv)}s{(line.Immediate ? " !" : "  ")} {line.Text}");
                    }
                }

                if (sc.PitchStepAtS is double stepAt && !stepped && t >= stepAt)
                {
                    pilot.FreezePitch(ac.PitchDeg - PitchStepDeg);
                    stepped = true;
                }
                // A gust: its share of the knock this frame, before the pilot steps.
                foreach (var g in gusts)
                {
                    if (frame < g.Frame || frame >= g.Frame + rampFrames) continue;
                    if (frame == g.Frame) trace?.Invoke($"      gust {(g.Sign * sc.GustBankDeg).ToString("+0;-0", Inv)}° over {GustRampS.ToString("0.0", Inv)} s");
                    pilot.Knock(g.Sign * sc.GustBankDeg / rampFrames);
                }
                var (bank, pitch) = pilot.Step(t, dt, vg.DesiredBankForHarness, vg.DesiredPitchForHarness, speech.Lines, scripted, scriptedPitch);
                // A pilot slowing down through the approach (ApproachSlowing): this step's speed.
                if (sc.Slowing is ApproachSlowing slowing)
                {
                    var (alongS, crossS) = ApproachRunways.Locate(rwy, ac.Lat, ac.Lon);
                    double relTrack = ((ac.TrackTrueDeg - ApproachRunways.LocalCourse(rwy, ac.Lat, ac.Lon)) % 360 + 540) % 360 - 180;
                    ac.SetTas(slowing.Step(ac.TasKt, slowing.TargetKt(alongS, crossS, relTrack), dt));
                }
                ac.Step(dt, bank, pitch);
                t = (frame + 1) * dt;

                // Visual guidance puts the main gear the profile's FlareAltitudeBiasFt below the
                // datum, so contact is when the wheels, not the datum, reach the ground.
                var (alongNow, crossNow) = ApproachRunways.Locate(rwy, ac.Lat, ac.Lon);
                if (ac.AglFt - profile.FlareAltitudeBiasFt <= 0)
                {
                    endReason = LandingEndReason(rwy, alongNow, crossNow);
                    break;
                }
                if (alongNow < farEndNm && reachedField) { endReason = "pastRunwayEnd"; break; }
                if (alongNow >= farEndNm) reachedField = true;
                if (!vg.IsActive) { endReason = "vgOff"; break; }
                if (t > MaxRunS) { endReason = "timeout"; break; }
            }

            metrics.Finish(endReason, t, ac, speech.Lines);
            if (trace != null)
            {
                for (; spokenPrinted < speech.Lines.Count; spokenPrinted++)
                {
                    var line = speech.Lines[spokenPrinted];
                    trace($"      say {line.T.ToString("0.00", Inv)}s{(line.Immediate ? " !" : "  ")} {line.Text}");
                }
                trace(TraceLine(t, sc, ac, vg, profile) + $"  END {endReason}");
            }
            return metrics;
        }
        finally
        {
            SimClock.Override = savedClock;
        }
    }

    /// <summary>
    /// Where a landing put the wheels: "touchdown" on the runway's pavement, else "landedShort"
    /// (before the threshold), "landedLong" (past the far end) or "offRunway" (beside it).
    /// </summary>
    public static string LandingEndReason(ApproachRunway r, double alongNm, double crossNm)
    {
        if (alongNm > 0) return "landedShort";
        if (alongNm < ApproachRunways.FarEndNm(r)) return "landedLong";
        if (Math.Abs(crossNm * FtPerNm) > r.Runway.Width / 2) return "offRunway";
        return "touchdown";
    }

    private static string TraceLine(double t, ApproachScenario sc, ApproachAircraft ac, VisualGuidanceManager vg, VisualGuidanceProfile profile)
    {
        var (along, cross) = ApproachRunways.Locate(sc.Runway, ac.Lat, ac.Lon);
        double pathErr = ac.AltMslFt - ApproachRunways.PathMslFt(sc.Runway, along, profile);
        string N(double v, string f) => v.ToString(f, Inv);
        string phase = DecisionOf(vg) is string d && d != "None" ? $"{vg.PhaseForHarness}/{d}" : vg.PhaseForHarness;
        string vertical = VerticalModeOf(vg) is string mode
            ? $" {mode,-6} tgt {(TargetDescentOf(vg) is double tg ? N(tg, "0.00") : "NA"),5}" : "";
        return $"t={N(t, "0.0"),6} {phase,-18} along={N(along, "0.00"),6}nm cross={N(cross * 6076.12, "0"),6}ft " +
               $"dPath={N(pathErr, "0"),5}ft vs={N(ac.VsFpm, "0"),5} " +
               $"bank {N(vg.DesiredBankForHarness, "0.0"),5}/{N(ac.BankStdDeg, "0.0"),5} " +
               $"pitch {N(vg.DesiredPitchForHarness, "0.00"),6}/{N(ac.PitchDeg, "0.00"),6}" + vertical
               + (sc.Slowing != null ? $" tas {N(ac.TasKt, "0")}" : "");
    }
}
