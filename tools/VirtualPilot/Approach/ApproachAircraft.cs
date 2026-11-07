using MSFSBlindAssist.Services.Volanta;

namespace VirtualPilot.Approach;

enum ApproachAircraftKind { Jet, Light }

static class ApproachAircraftSpecs
{
    /// <summary>
    /// TAS, the angle of attack it trims at (pitch − flight path), the flight-path lag, and the
    /// pilot's roll and pitch rates. The jet is an airliner on approach speed with autothrust; the
    /// light aircraft a C172 or DA40 on final.
    /// </summary>
    public static (double TasKt, double AlphaTrimDeg, double TauGammaS, double RollRateDegS, double PitchRateDegS) For(ApproachAircraftKind kind)
        => kind == ApproachAircraftKind.Jet ? (140, 5.0, 2.5, 5, 2) : (65, 4.0, 1.5, 10, 4);
}

/// <summary>
/// One frame of what MainForm feeds visual guidance, in SimConnect's own conventions: pitch is
/// nose-down positive and bank left-positive, both in radians, and headings are magnetic degrees.
/// </summary>
readonly record struct VgSimFrame(double Lat, double Lon, double AltMslFt, double HeadingMagDeg, double GroundTrackMagDeg,
    double GsKt, double VsFpm, double AglFt, double PitchRad, double BankRad, double AlphaRad);

/// <summary>
/// A point-mass aircraft. Bank sets the turn rate (g·tan φ / V); pitch sets the flight path
/// through a first-order lag, at a fixed trim angle of attack; speed is held (autothrust); the
/// wind is constant; the terrain is flat at the runway's elevation. No ground effect.
/// </summary>
sealed class ApproachAircraft
{
    private const double G = 9.80665, MsPerKt = 1852.0 / 3600.0, FpmPerKt = 101.269;

    private readonly ApproachRunway _rwy;
    private readonly double _alphaTrimDeg, _tauGammaS;
    private double _tasKt;
    private readonly double _windToTrueDeg, _windKt;

    public double Lat { get; private set; }
    public double Lon { get; private set; }
    public double AltMslFt { get; private set; }
    public double HeadingTrueDeg { get; private set; }
    public double TrackTrueDeg { get; private set; }
    public double GsKt { get; private set; }
    public double VsFpm { get; private set; }
    public double BankStdDeg { get; private set; }
    public double PitchDeg { get; private set; }
    public double GammaDeg { get; private set; }
    public double AlphaDeg { get; private set; }
    public double AglFt => AltMslFt - _rwy.Runway.ThresholdElevation;
    public double TasKt => _tasKt;

    /// <summary>The pilot slowing down (<see cref="ApproachSlowing"/>); the next <see cref="Step"/> flies at it.</summary>
    public void SetTas(double tasKt) => _tasKt = tasKt;

    /// <param name="tasKt">Overrides the kind's TAS (the speed scenarios).</param>
    /// <param name="gammaDeg">The flight path it starts on, already settled (0 = level).</param>
    /// <param name="bankStdDeg">The bank it starts with, right wing down positive (a press in a turn).</param>
    public ApproachAircraft(ApproachAircraftKind kind, ApproachRunway rwy, double lat, double lon, double altMslFt,
        double headingTrueDeg, double windFromTrueDeg, double windKt, double? tasKt = null, double gammaDeg = 0.0,
        double bankStdDeg = 0.0)
    {
        BankStdDeg = bankStdDeg;
        var spec = ApproachAircraftSpecs.For(kind);
        _rwy = rwy;
        _tasKt = tasKt ?? spec.TasKt;
        _alphaTrimDeg = spec.AlphaTrimDeg;
        _tauGammaS = spec.TauGammaS;
        _windToTrueDeg = ApproachRunways.Norm(windFromTrueDeg + 180);
        _windKt = windKt;
        Lat = lat; Lon = lon; AltMslFt = altMslFt;
        HeadingTrueDeg = ApproachRunways.Norm(headingTrueDeg);
        GammaDeg = gammaDeg;
        PitchDeg = _alphaTrimDeg + gammaDeg;
        AlphaDeg = _alphaTrimDeg;
        UpdateVelocity();
    }

    /// <summary>Flies <paramref name="dt"/> seconds at the attitude given; the pilot model owns how fast the attitude changes.</summary>
    public void Step(double dt, double bankStdDeg, double pitchDeg)
    {
        BankStdDeg = bankStdDeg;
        PitchDeg = pitchDeg;

        double turnRateRad = G * Math.Tan(BankStdDeg * Math.PI / 180) / (_tasKt * MsPerKt);
        HeadingTrueDeg = ApproachRunways.Norm(HeadingTrueDeg + turnRateRad * 180 / Math.PI * dt);

        GammaDeg += ((PitchDeg - _alphaTrimDeg) - GammaDeg) / _tauGammaS * dt;
        AlphaDeg = PitchDeg - GammaDeg;

        UpdateVelocity();
        AltMslFt += VsFpm / 60.0 * dt;
        double lat0 = Lat, lon0 = Lon;
        (Lat, Lon) = VolantaTrafficCalculator.Project(Lat, Lon, TrackTrueDeg, GsKt * dt / 3600.0);

        // Wings level is a great circle, whose true bearing turns with the convergence of the
        // meridians (Δλ·sin φ). Holding a constant true heading instead flies a rhumb line, about
        // 40 ft off an east-west centreline at 40° N in 7.5 nm (the great-circle self-test).
        double dLon = ((Lon - lon0) % 360 + 540) % 360 - 180;
        HeadingTrueDeg = ApproachRunways.Norm(HeadingTrueDeg + dLon * Math.Sin((lat0 + Lat) / 2 * Math.PI / 180));
        UpdateVelocity();
    }

    // The air vector is the TAS along the true heading; the wind adds to it.
    private void UpdateVelocity()
    {
        double h = HeadingTrueDeg * Math.PI / 180, w = _windToTrueDeg * Math.PI / 180;
        double east = _tasKt * Math.Sin(h) + _windKt * Math.Sin(w);
        double north = _tasKt * Math.Cos(h) + _windKt * Math.Cos(w);
        GsKt = Math.Sqrt(east * east + north * north);
        TrackTrueDeg = ApproachRunways.Norm(Math.Atan2(east, north) * 180 / Math.PI);
        VsFpm = _tasKt * Math.Sin(GammaDeg * Math.PI / 180) * FpmPerKt;
    }

    public VgSimFrame ToSimFrame()
    {
        double magVar = _rwy.Airport.MagVar;
        return new VgSimFrame(Lat, Lon, AltMslFt,
            ApproachRunways.Norm(HeadingTrueDeg - magVar), ApproachRunways.Norm(TrackTrueDeg - magVar),
            GsKt, VsFpm, AglFt,
            PitchRad: -PitchDeg * Math.PI / 180, BankRad: -BankStdDeg * Math.PI / 180, AlphaRad: AlphaDeg * Math.PI / 180);
    }
}
