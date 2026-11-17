using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Volanta;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// A headless visual guidance manager on runway 09 (true 090, no variation, landing threshold 47.0 N
/// 8.0 E, elevation 500 ft, 3°) with its own clock and a <see cref="SpeechCapture"/>. The aircraft does
/// not move between updates; only what a test changes with <see cref="Place"/> changes. With a
/// displaced threshold the runway's end (the start of its pavement) lies that far before the landing
/// threshold, which stays at 47.0 N 8.0 E, so <see cref="Place"/>'s distances are always from it.
/// </summary>
internal sealed class VisualGuidanceTestRig
{
    private const double ThrLat = 47.0, ThrLon = 8.0, Elev = 500.0, Course = 90.0;
    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly VisualGuidanceManager _vg;
    private readonly double _pathDeg;
    private DateTime _clock = Start;
    private double _seconds;

    /// <param name="record">Receives the flight recording's lines (VP-2); by default they go nowhere.</param>
    /// <param name="thresholdOffsetFt">The runway's displaced threshold (<see cref="Runway.ThresholdOffset"/>).</param>
    /// <param name="glideslopeAngleDeg">The ILS glideslope's angle the navdata gives (0: no ILS).</param>
    /// <param name="papiAngleDeg">The PAPI's angle the navdata gives (0: none).</param>
    /// <param name="pathDeg">The glide path <see cref="Place"/> puts the aircraft on, by default 3°.</param>
    public VisualGuidanceTestRig(Action<string>? record = null, double thresholdOffsetFt = 0.0,
        double glideslopeAngleDeg = 3.0, double papiAngleDeg = 0.0, double pathDeg = 3.0)
    {
        _pathDeg = pathDeg;
        double startLat = ThrLat, startLon = ThrLon;
        if (thresholdOffsetFt > 0)
        {
            var pavementStart = VolantaTrafficCalculator.Project(ThrLat, ThrLon, Course + 180, thresholdOffsetFt / 6076.12);
            (startLat, startLon) = (pavementStart.Latitude, pavementStart.Longitude);
        }
        var (endLat, endLon) = VolantaTrafficCalculator.Project(ThrLat, ThrLon, Course, 2.0);
        var runway = new Runway
        {
            AirportICAO = "ZZVG", RunwayID = "09", Heading = Course, HeadingMag = Course,
            StartLat = startLat, StartLon = startLon, EndLat = endLat, EndLon = endLon,
            Length = 2.0 * 6076.12 + thresholdOffsetFt, ThresholdOffset = thresholdOffsetFt,
            ThresholdElevation = Elev, GlideslopeAngleDeg = glideslopeAngleDeg, PapiAngleDeg = papiAngleDeg,
        };
        var airport = new Airport { ICAO = "ZZVG", Name = "Test", Latitude = ThrLat, Longitude = ThrLon, Altitude = Elev, MagVar = 0.0 };
        _vg = new VisualGuidanceManager(Speech)
        {
            HeadlessForHarness = true, ClockForHarness = () => _clock, FlightRecordForHarness = record ?? (_ => { }),
        };
        _vg.Toggle();
        _vg.Initialize(runway, airport, HandFlyWaveType.Triangle, 0.0, HandFlyWaveType.Sine, 0.0, false, new VisualGuidanceProfile());
    }

    /// <summary>Everything the manager spoke, from the activation sentence on.</summary>
    public SpeechCapture Speech { get; } = new();

    public double Bank => _vg.DesiredBankForHarness;
    public double Pitch => _vg.DesiredPitchForHarness;

    /// <summary>The guidance phase's name (Deciding, Joining, Vectoring, Final, Flare, Touchdown).</summary>
    public string Phase => _vg.PhaseForHarness;

    /// <summary>The decision visual guidance made (OnFinal, Joining, Vectoring, Pattern), "None" before it.</summary>
    public string Decision => _vg.DecisionForHarness;

    /// <summary>What F would say now (<see cref="VisualGuidanceManager.GetStatusReadout"/>).</summary>
    public string Readout => _vg.GetStatusReadout();

    /// <summary>The vertical cue's mode (Follow, Hold, Plan, Path, Flare).</summary>
    public string VerticalMode => _vg.VerticalModeForHarness;

    /// <summary>The target descent angle the vertical cue flies, degrees, + down.</summary>
    public double TargetDescent => _vg.TargetDescentForHarness;

    /// <summary>
    /// alongNm back from the threshold, crossNm right of the centreline, heading course + headingRelDeg
    /// and the ground track course + trackRelDeg (by default the heading: no wind), at gsKt; bankDeg right
    /// wing down positive (fed to the manager as SimConnect's left-positive bank); pitchDeg nose up.
    /// </summary>
    public void Place(double alongNm, double crossNm, double headingRelDeg, double vsFpm = -742, double altOffsetFt = 0,
        double aoaDeg = 5.0, double bankDeg = 0.0, double gsKt = 140, double pitchDeg = 2.0, double? trackRelDeg = null)
    {
        static double Norm(double d) => (d % 360 + 360) % 360;
        // Past the threshold (alongNm negative) is ahead along the course: Project takes no negative distance.
        var back = alongNm >= 0
            ? VolantaTrafficCalculator.Project(ThrLat, ThrLon, Norm(Course + 180), alongNm)
            : VolantaTrafficCalculator.Project(ThrLat, ThrLon, Course, -alongNm);
        var pos = crossNm >= 0
            ? VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(Course + 90), crossNm)
            : VolantaTrafficCalculator.Project(back.Latitude, back.Longitude, Norm(Course - 90), -crossNm);
        double alt = Elev + alongNm * 6076.12 * Math.Tan(_pathDeg * Math.PI / 180) + new VisualGuidanceProfile().GlideslopeAltitudeBiasFt + altOffsetFt;
        double heading = Norm(Course + headingRelDeg);

        _vg.UpdateLatitude(pos.Latitude); _vg.UpdateLongitude(pos.Longitude); _vg.UpdateAltitudeMSL(alt);
        _vg.UpdateHeading(heading); _vg.UpdateGroundSpeed(gsKt); _vg.UpdateVerticalSpeed(vsFpm);
        _vg.UpdateGroundTrack(Norm(Course + (trackRelDeg ?? headingRelDeg)));
        _vg.UpdatePitch(VisualGuidanceManager.PitchDegreesFromSimConnect(-pitchDeg * Math.PI / 180));
        _vg.UpdateBank(VisualGuidanceManager.BankDegreesFromSimConnect(-bankDeg * Math.PI / 180));
        _vg.UpdateAoA(VisualGuidanceManager.AoaDegreesFromSimConnect(aoaDeg * Math.PI / 180));
        _vg.UpdateAGL(alt - Elev);
    }

    /// <summary>Switches visual guidance off, as Ctrl+V or a landing does.</summary>
    public void Stop() => _vg.Stop();

    /// <summary>Sets the clock to <paramref name="seconds"/> after the start and runs one update.</summary>
    public void UpdateAt(double seconds)
    {
        _seconds = seconds;
        _clock = Start.AddTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond));
        _vg.ProcessUpdate();
    }

    /// <summary>
    /// Carries on from the current clock with one update every 1/hz s until <paramref name="seconds"/>
    /// have passed. <paramref name="beforeEach"/>, given the update's time in seconds, can move the aircraft first;
    /// <paramref name="afterEach"/> sees the result of each update.
    /// </summary>
    public void FlyFor(double seconds, int hz, Action<double>? beforeEach = null, Action<double>? afterEach = null)
    {
        double from = _seconds;
        int steps = (int)Math.Round(seconds * hz);
        for (int i = 1; i <= steps; i++)
        {
            double t = from + (double)i / hz;
            beforeEach?.Invoke(t);
            UpdateAt(t);
            afterEach?.Invoke(t);
        }
    }

    /// <summary>The time of the last update, seconds after the start.</summary>
    public double Now => _seconds;
}
