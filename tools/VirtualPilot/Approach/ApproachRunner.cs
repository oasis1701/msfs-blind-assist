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

    /// <summary>
    /// Flies one scenario to its end. With <paramref name="trace"/>, prints a frame every 0.5 simulated
    /// seconds; with <paramref name="record"/>, receives the flight recording's lines (VP-2).
    /// </summary>
    public static ApproachMetrics RunOne(ApproachScenario sc, Action<string>? trace, Action<string>? record = null)
    {
        var rwy = sc.Runway;
        var profile = new VisualGuidanceProfile();
        double t = 0;
        var savedClock = SimClock.Override;
        SimClock.Override = () => ClockStart.AddSeconds(t);
        try
        {
            var speech = new ApproachSpeech(() => t);
            var vg = new VisualGuidanceManager(speech) { HeadlessForHarness = true };
            FlightRecordHook?.SetValue(vg, record ?? NoRecord);
            vg.Toggle();
            vg.Initialize(rwy.Runway, rwy.Airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, profile);

            var (lat, lon) = ApproachRunways.PointOnApproach(rwy, sc.AlongNm, sc.CrossNm);
            double alt = ApproachRunways.PathMslFt(rwy, sc.AlongNm, profile) + sc.HeightOffsetFt;
            double course = rwy.Runway.Heading;
            // A start on the course is already flying final, so it starts descending at the glide
            // path's angle; every other start (intercept, downwind) is level.
            double startGamma = sc.HeadingRelDeg == 0 ? -rwy.Runway.GlideslopeAngleDeg : 0.0;
            // Headings are relative to the centreline's direction where the aircraft starts; the
            // wind is relative to the threshold's course.
            var ac = new ApproachAircraft(sc.Kind, rwy, lat, lon, alt, ApproachRunways.LocalCourse(rwy, lat, lon) + sc.HeadingRelDeg,
                course + sc.WindFromRelDeg, sc.WindKt, sc.TasKt, startGamma);
            var pilot = new TonePilot(sc.Kind, profile, sc.ReactionDelayS, 0.0, ac.PitchDeg, sc.BankBySpeech);
            var gusts = sc.GustBankDeg > 0 ? GustStarts(sc).ToList() : new List<(long Frame, double T, int Sign)>();
            int rampFrames = Math.Max(1, (int)Math.Round(GustRampS * sc.Fps));
            var metrics = new ApproachMetrics(sc, profile);
            // Noise reaches only what visual guidance reads; the aircraft, metrics and trace stay true.
            var noise = sc.SensorNoise ? new ApproachSensorNoise(ApproachSensorNoise.Seed) : null;

            double dt = 1.0 / sc.Fps;
            bool stepped = false;
            int printedHalfSeconds = -1, spokenPrinted = 0;
            string endReason;
            for (long frame = 0; ; frame++)
            {
                t = frame * dt;
                var frameIn = ac.ToSimFrame();
                VgFeed.Feed(vg, noise != null ? noise.Apply(frameIn) : frameIn);
                metrics.Record(t, ac, vg);   // the state and the command it produced, same frame
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
                var (bank, pitch) = pilot.Step(t, dt, vg.DesiredBankForHarness, vg.DesiredPitchForHarness, speech.Lines);
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
                if (alongNow < -rwy.Runway.Length / FtPerNm) { endReason = "pastRunwayEnd"; break; }
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
        if (alongNm < -r.Runway.Length / FtPerNm) return "landedLong";
        if (Math.Abs(crossNm * FtPerNm) > r.Runway.Width / 2) return "offRunway";
        return "touchdown";
    }

    private static string TraceLine(double t, ApproachScenario sc, ApproachAircraft ac, VisualGuidanceManager vg, VisualGuidanceProfile profile)
    {
        var (along, cross) = ApproachRunways.Locate(sc.Runway, ac.Lat, ac.Lon);
        double pathErr = ac.AltMslFt - ApproachRunways.PathMslFt(sc.Runway, along, profile);
        string N(double v, string f) => v.ToString(f, Inv);
        return $"t={N(t, "0.0"),6} {vg.PhaseForHarness,-18} along={N(along, "0.00"),6}nm cross={N(cross * 6076.12, "0"),6}ft " +
               $"dPath={N(pathErr, "0"),5}ft vs={N(ac.VsFpm, "0"),5} " +
               $"bank {N(vg.DesiredBankForHarness, "0.0"),5}/{N(ac.BankStdDeg, "0.0"),5} " +
               $"pitch {N(vg.DesiredPitchForHarness, "0.00"),6}/{N(ac.PitchDeg, "0.00"),6}";
    }
}
