using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace VirtualPilot.Approach;

/// <summary>
/// What one approach measured, written as one results line: <c>VG|name|key=value|...</c>, numbers
/// in invariant culture, "NA" where a measure does not apply (no final segment flown, not a step
/// run). The keys and their meaning are listed in docs/virtual-pilot.md, "Approaches".
/// </summary>
sealed class ApproachMetrics
{
    private const double FtPerNm = 6076.12;
    private const double EstablishedCrossNm = 0.1, EstablishedTrackDeg = 5.0, EstablishedHoldS = 3.0;
    private const double OvershootMinStartCrossNm = 0.15;

    private static readonly Regex BankCallout = new(@"^(\d+ (left|right)|matched|On commanded path)$", RegexOptions.CultureInvariant);

    private readonly record struct Sample(double T, double Along, double Cross, double TrackErr, double PathErr, double Vs,
        double DesiredPitch, double DesiredBank, double Bank);

    // Gust measures: a knock with no bank callout within this long counts this long; recovery and
    // the cross-track are looked for within the window after a knock. The recovered band is 1°, twice
    // the pilot's 0.5° deadband: at 0.5° the pilot settles just inside the band's edge, the slowly
    // moving command pushes it out again before the 1 s hold completes, and the measure read the
    // deadband's limit cycle (11 s for a jet back level 2 s after a 2° knock) instead of the recovery.
    private const double GustCalloutCapS = 10.0, GustWindowS = 30.0, GustRecoveredBandDeg = 1.0, GustRecoveredHoldS = 1.0;

    private readonly ApproachScenario _sc;
    private readonly VisualGuidanceProfile _profile;
    private readonly List<Sample> _samples = new();
    private readonly Dictionary<string, string> _fields = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    public string EndReason { get; private set; } = "running";

    /// <summary>Where the run ended (nm back from the threshold, negative past it) and the datum's height there; for the self-test.</summary>
    public double EndAlongNm { get; private set; }
    public double EndDatumAglFt { get; private set; }

    public ApproachMetrics(ApproachScenario sc, VisualGuidanceProfile profile) { _sc = sc; _profile = profile; }

    /// <summary>One frame: the aircraft state and the command visual guidance produced from it.</summary>
    public void Record(double t, ApproachAircraft ac, VisualGuidanceManager vg)
    {
        var r = _sc.Runway;
        var (along, cross) = ApproachRunways.Locate(r, ac.Lat, ac.Lon);
        double trackErr = ((ac.TrackTrueDeg - r.Runway.Heading) % 360 + 540) % 360 - 180;
        double pathErr = ac.AltMslFt - ApproachRunways.PathMslFt(r, along, _profile);
        _samples.Add(new Sample(t, along, cross, trackErr, pathErr, ac.VsFpm, vg.DesiredPitchForHarness, vg.DesiredBankForHarness, ac.BankStdDeg));
    }

    /// <summary>Ends the run and computes every measure.</summary>
    public void Finish(string endReason, double t, ApproachAircraft ac, IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        EndReason = endReason;
        Put("endReason", endReason);
        Put("durationS", t, "0.0");

        var r = _sc.Runway;
        var (along, cross) = ApproachRunways.Locate(r, ac.Lat, ac.Lon);
        EndAlongNm = along;
        EndDatumAglFt = ac.AglFt;
        bool landed = endReason is "touchdown" or "landedShort" or "landedLong" or "offRunway";
        Put("touchdownAlongFt", landed ? along * FtPerNm : null, "0");
        Put("touchdownXteFt", landed ? cross * FtPerNm : null, "0");
        Put("touchdownVsFpm", landed ? -ac.VsFpm : null, "0");   // sink at wheel contact, fpm down

        double? establishedAt = EstablishedAt();
        Put("established", establishedAt.HasValue ? "True" : "False");
        Put("timeToEstablishS", establishedAt, "0.0");
        Put("overshootFt", Overshoot(establishedAt), "0");

        bool jet = _sc.Kind == ApproachAircraftKind.Jet;
        double segFrom = jet ? 5.0 : 3.0, segTo = jet ? 0.5 : 0.3;
        var seg = establishedAt is double e
            ? _samples.Where(s => s.T >= e && s.Along <= segFrom && s.Along >= segTo).ToList()
            : new List<Sample>();
        bool hasSeg = seg.Count > 0;
        Put("finalXteRmsFt", hasSeg ? Rms(seg.Select(s => s.Cross * FtPerNm)) : null, "0.0");
        Put("finalXteMaxFt", hasSeg ? seg.Max(s => Math.Abs(s.Cross)) * FtPerNm : null, "0.0");
        Put("pathRmsFt", hasSeg ? Rms(seg.Select(s => s.PathErr)) : null, "0.0");
        Put("pathMaxHighFt", hasSeg ? Math.Max(0, seg.Max(s => s.PathErr)) : null, "0.0");
        Put("pathMaxLowFt", hasSeg ? Math.Max(0, -seg.Min(s => s.PathErr)) : null, "0.0");
        Put("pitchJitterDeg", seg.Count > 1 ? Rms(Diffs(seg.Select(s => s.DesiredPitch))) : null, "0.0000");
        Put("bankJitterDeg", seg.Count > 1 ? Rms(Diffs(seg.Select(s => s.DesiredBank))) : null, "0.0000");
        Put("pitchRateRmsDegS", hasSeg ? RateRms(seg.Select(s => (s.T, s.DesiredPitch)).ToList()) : null, "0.0000");
        Put("bankRateRmsDegS", hasSeg ? RateRms(seg.Select(s => (s.T, s.DesiredBank)).ToList()) : null, "0.0000");

        Put("maxDescentFpm", _samples.Count > 0 ? Math.Max(0, _samples.Max(s => -s.Vs)) : 0, "0");

        double minutes = Math.Max(t, 1e-9) / 60.0;
        int bank = speech.Count(l => BankCallout.IsMatch(l.Text));
        int centreline = speech.Count(l => l.Text.EndsWith("of center", StringComparison.Ordinal) || l.Text == "on runway center");
        Put("bankCalloutsPerMin", bank / minutes, "0.00");
        Put("centrelineCalloutsPerMin", centreline / minutes, "0.00");
        Put("otherCallouts", speech.Count - bank - centreline, "0");
        Put("cutOffCallouts", CutOff(speech), "0");

        // Gust runs only: the knocks that started before the run ended.
        var knocks = _sc.GustBankDeg > 0
            ? ApproachRunner.GustStarts(_sc).Where(g => g.T < t).Select(g => g.T).ToList()
            : new List<double>();
        bool gusty = knocks.Count > 0;
        Put("gustCalloutDelayS", gusty ? knocks.Average(k => GustCalloutDelay(k, speech)) : null, "0.00");
        Put("gustRecoveryS", gusty ? knocks.Average(GustRecovery) : null, "0.00");
        Put("gustXteMaxFt", gusty ? knocks.Max(k => _samples.Where(s => s.T >= k && s.T <= k + GustWindowS).Select(s => Math.Abs(s.Cross)).DefaultIfEmpty(0).Max()) * FtPerNm : null, "0.0");

        if (_sc.PitchStepAtS is double step)
        {
            Put("selfFeedback3s", SelfFeedback(step, 3.0), "0.000");
            Put("selfFeedback10s", SelfFeedback(step, 10.0), "0.000");
        }
    }

    public string ToResultsLine(string scenarioName)
    {
        var sb = new StringBuilder("VG|").Append(scenarioName);
        foreach (string key in _order) sb.Append('|').Append(key).Append('=').Append(_fields[key]);
        return sb.ToString();
    }

    // The moment the aircraft has held within 0.1 nm of the line and 5° of the course for 3 s:
    // when "Established on final" would be spoken.
    private double? EstablishedAt()
    {
        double? holdStart = null;
        foreach (var s in _samples)
        {
            bool inside = Math.Abs(s.Cross) <= EstablishedCrossNm && Math.Abs(s.TrackErr) <= EstablishedTrackDeg;
            if (!inside) { holdStart = null; continue; }
            holdStart ??= s.T;
            if (s.T - holdStart.Value >= EstablishedHoldS) return s.T;
        }
        return null;
    }

    // The farthest past the line, after first crossing it, before established. Only for starts
    // more than 0.15 nm off the line; 0 if the aircraft never crosses.
    private double? Overshoot(double? establishedAt)
    {
        if (_samples.Count == 0 || Math.Abs(_samples[0].Cross) <= OvershootMinStartCrossNm) return null;
        int startSide = Math.Sign(_samples[0].Cross);
        bool crossed = false;
        double worst = 0;
        foreach (var s in _samples)
        {
            if (establishedAt is double e && s.T >= e) break;
            if (Math.Sign(s.Cross) == -startSide) crossed = true;
            if (crossed && Math.Sign(s.Cross) == -startSide) worst = Math.Max(worst, Math.Abs(s.Cross));
        }
        return worst * FtPerNm;
    }

    // How far the command moved per degree the pilot pitched, measured against the mean command over
    // the 2 s before the step: the pilot holds 1° nose-down from the step, so a command that follows
    // the pilot down reads positive, and one that moves the other way, asking them back up, reads
    // negative. Today's vertical-speed law predicts about −1.2 for the jet at 140 kt.
    private double? SelfFeedback(double step, double after)
    {
        var before = _samples.Where(s => s.T >= step - 2.0 && s.T < step).ToList();
        var at = _samples.FirstOrDefault(s => s.T >= step + after);
        if (before.Count == 0 || at.T < step + after) return null;
        return (at.DesiredPitch - before.Average(s => s.DesiredPitch)) / -1.0;
    }

    // From a knock's start to the first bank callout after it (the frame the knock starts on was fed
    // to visual guidance before the knock, so a line at that instant is not a reply to it).
    private static double GustCalloutDelay(double knock, IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        foreach (var line in speech)
            if (line.T > knock + 1e-9 && BankCallout.IsMatch(line.Text))
                return Math.Min(line.T - knock, GustCalloutCapS);
        return GustCalloutCapS;
    }

    // From a knock's start to the start of the first second in which the aircraft's bank stayed
    // within 0.5° of the commanded bank; the window's length if it never did.
    private double GustRecovery(double knock)
    {
        double? holdStart = null;
        foreach (var s in _samples)
        {
            if (s.T < knock) continue;
            if (s.T > knock + GustWindowS) break;
            if (Math.Abs(s.Bank - s.DesiredBank) > GustRecoveredBandDeg) { holdStart = null; continue; }
            holdStart ??= s.T;
            if (s.T - holdStart.Value >= GustRecoveredHoldS - 1e-9) return holdStart.Value - knock;
        }
        return GustWindowS;
    }

    // A line counts as cut off when an interrupting line follows before its estimated speaking time
    // (words / 2.5 + 0.2 s). A measure only: the app never sizes anything by estimate (VG-1).
    private static int CutOff(IReadOnlyList<(double T, string Text, bool Immediate)> speech)
    {
        int count = 0;
        for (int i = 0; i < speech.Count; i++)
        {
            int words = speech[i].Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            double end = speech[i].T + words / 2.5 + 0.2;
            for (int j = i + 1; j < speech.Count && speech[j].T < end; j++)
                if (speech[j].Immediate) { count++; break; }
        }
        return count;
    }

    /// <summary>
    /// The RMS of a series' rate of change, (v[i] − v[i−1]) / (t[i] − t[i−1]), in units per second;
    /// 0 for fewer than two points. Unlike a per-sample difference, a smooth series reads the same
    /// at any sample rate.
    /// </summary>
    public static double RateRms(IReadOnlyList<(double T, double V)> series)
    {
        if (series.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < series.Count; i++)
        {
            double rate = (series[i].V - series[i - 1].V) / (series[i].T - series[i - 1].T);
            sum += rate * rate;
        }
        return Math.Sqrt(sum / (series.Count - 1));
    }

    private static double Rms(IEnumerable<double> values)
    {
        double sum = 0; int n = 0;
        foreach (double v in values) { sum += v * v; n++; }
        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }

    private static IEnumerable<double> Diffs(IEnumerable<double> values)
    {
        double? prev = null;
        foreach (double v in values)
        {
            if (prev is double p) yield return v - p;
            prev = v;
        }
    }

    private void Put(string key, string value)
    {
        if (!_fields.ContainsKey(key)) _order.Add(key);
        _fields[key] = value;
    }

    private void Put(string key, double? value, string format)
        => Put(key, value is double v && double.IsFinite(v) ? v.ToString(format, CultureInfo.InvariantCulture) : "NA");
}
