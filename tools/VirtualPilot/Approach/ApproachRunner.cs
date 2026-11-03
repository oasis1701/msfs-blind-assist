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
    private const double MaxRunS = 900.0, StepRunAfterS = 10.0, TouchdownWithinNm = 0.3, PitchStepDeg = 1.0;
    private static readonly DateTime ClockStart = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Run(Options opt)
    {
        if (opt.SelfTest) return ApproachSelfTest.Run();

        if (opt.Trace != null)
        {
            var sc = ApproachScenarios.All().FirstOrDefault(s => s.Name == opt.Trace)
                  ?? ApproachScenarios.All().FirstOrDefault(s => s.Name.Contains(opt.Trace, StringComparison.OrdinalIgnoreCase));
            if (sc == null) { Console.WriteLine($"no approach scenario matches {opt.Trace}"); return 2; }
            var m = RunOne(sc, Console.WriteLine);
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
                var m = RunOne(sc, trace: null);
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

    /// <summary>Flies one scenario to its end. With <paramref name="trace"/>, prints a frame every 0.5 simulated seconds.</summary>
    public static ApproachMetrics RunOne(ApproachScenario sc, Action<string>? trace)
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
            vg.Toggle();
            vg.Initialize(rwy.Runway, rwy.Airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, profile);

            var (lat, lon) = ApproachRunways.PointOnApproach(rwy, sc.AlongNm, sc.CrossNm);
            double alt = ApproachRunways.PathMslFt(rwy, sc.AlongNm, profile) + sc.HeightOffsetFt;
            double course = rwy.Runway.Heading;
            // A start on the course is already flying final, so it starts descending at the glide
            // path's angle; every other start (intercept, downwind) is level.
            double startGamma = sc.HeadingRelDeg == 0 ? -rwy.Runway.GlideslopeAngleDeg : 0.0;
            var ac = new ApproachAircraft(sc.Kind, rwy, lat, lon, alt, course + sc.HeadingRelDeg,
                course + sc.WindFromRelDeg, sc.WindKt, sc.TasKt, startGamma);
            var pilot = new TonePilot(sc.Kind, profile, sc.ReactionDelayS, 0.0, ac.PitchDeg);
            var metrics = new ApproachMetrics(sc, profile);

            double dt = 1.0 / sc.Fps;
            bool stepped = false;
            int printedHalfSeconds = -1, spokenPrinted = 0;
            string endReason;
            for (long frame = 0; ; frame++)
            {
                t = frame * dt;
                VgFeed.Feed(vg, ac.ToSimFrame());
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
                var (bank, pitch) = pilot.Step(t, dt, vg.DesiredBankForHarness, vg.DesiredPitchForHarness, speech.Lines);
                ac.Step(dt, bank, pitch);
                t = (frame + 1) * dt;

                if (ac.AglFt <= 0)
                {
                    var (along, _) = ApproachRunways.Locate(rwy, ac.Lat, ac.Lon);
                    endReason = along <= TouchdownWithinNm ? "touchdown" : "landedShort";
                    break;
                }
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
