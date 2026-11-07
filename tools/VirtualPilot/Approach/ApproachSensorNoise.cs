namespace VirtualPilot.Approach;

/// <summary>
/// Sensor noise on what visual guidance reads, for the NOISE scenarios: independent Gaussian noise
/// on the vertical speed, the angle of attack and the ground track, every frame. The three spreads
/// are ASSUMED values for a stress case, not measured from the sim. Every other field passes
/// through unchanged, and the aircraft, the metrics and the trace keep the true values.
/// </summary>
sealed class ApproachSensorNoise
{
    public const int Seed = 20261103;
    public const double VsSigmaFpm = 30.0, AlphaSigmaDeg = 0.2, TrackSigmaDeg = 0.2;

    private readonly Random _random;

    public ApproachSensorNoise(int seed) { _random = new Random(seed); }

    public VgSimFrame Apply(in VgSimFrame f)
    {
        double track = (f.GroundTrackMagDeg + TrackSigmaDeg * Gaussian()) % 360;
        if (track < 0) track += 360;
        return f with
        {
            VsFpm = f.VsFpm + VsSigmaFpm * Gaussian(),
            AlphaRad = f.AlphaRad + AlphaSigmaDeg * Gaussian() * Math.PI / 180,
            GroundTrackMagDeg = track,
        };
    }

    // Box–Muller: one standard normal from two uniforms. 1 − u keeps the logarithm off zero.
    private double Gaussian()
    {
        double u1 = 1.0 - _random.NextDouble(), u2 = _random.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
