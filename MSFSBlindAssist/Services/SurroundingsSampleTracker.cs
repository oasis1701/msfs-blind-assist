using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services;

/// <summary>What one ground sample meant to the two callouts that share it.</summary>
/// <param name="Usable">False when the position was not finite; nothing may act on it and it is
/// not kept as the reference.</param>
/// <param name="First">No previous position to measure from (session start, reset, or a pause in
/// sampling): recorded, never judged.</param>
/// <param name="Jumped">Too far from the previous position to have been taxied: the aircraft was put
/// here. The surface baseline is already dropped; the caller drops its passing tracks.</param>
/// <param name="SurfaceCallout">The surface sentence to speak (queued), or null.</param>
internal readonly record struct SurroundingsSample(bool Usable, bool First, bool Jumped, string? SurfaceCallout)
{
    /// <summary>The answer for a sample whose position could not be read.</summary>
    public static readonly SurroundingsSample Unusable = new(false, false, false, null);
}

/// <summary>
/// The pure per-sample half of <see cref="AirportSurroundingsMonitor"/>: last position, teleport
/// test, unreadable-sample guard, the two callout switches, and the <see cref="SurfaceChangeGate"/>
/// they feed. Pinned at the monitor's sparsest cadence (<see cref="AirportSurroundingsMonitor.PollMs"/>);
/// the surface rule is distance-based, so denser samples only make it more exact.
/// <para>No sample is taken while both switches are off, so turning one on forgets the last position
/// exactly when the other was off too.</para>
/// </summary>
internal sealed class SurroundingsSampleTracker
{
    private readonly SurfaceChangeGate _surface = new();
    private (double Lat, double Lon)? _last;
    private bool _passingEnabled;
    private bool _surfaceEnabled;

    /// <summary>The passing callouts' switch — <see cref="AirportSurroundingsMonitor.Enabled"/>.</summary>
    public bool PassingEnabled => _passingEnabled;

    /// <summary>The surface callout's switch — <see cref="AirportSurroundingsMonitor.SurfaceCalloutsEnabled"/>.</summary>
    public bool SurfaceEnabled => _surfaceEnabled;

    /// <summary>What the pilot has been told they are on; Unknown before the baseline.</summary>
    public SurfaceFamily AnnouncedSurface => _surface.AnnouncedFamily;

    /// <summary>
    /// Sets the passing switch; true only when this call turned it on, so the caller rebaselines its
    /// tracks (approaches recorded before were never watched). Re-assigning the same value is not an
    /// edge. Never touches the surface gate, so an excursion in progress keeps its evidence.
    /// </summary>
    public bool SetPassingEnabled(bool on)
    {
        bool turnedOn = on && !_passingEnabled;
        if (turnedOn && !_surfaceEnabled) _last = null;   // sampling resumes after a pause
        _passingEnabled = on;
        return turnedOn;
    }

    /// <summary>
    /// Sets the surface switch; true only when this call turned it on. Turning it on drops the surface
    /// baseline, so the next surface is a silent baseline. Re-assigning the same value changes nothing.
    /// </summary>
    public bool SetSurfaceEnabled(bool on)
    {
        bool turnedOn = on && !_surfaceEnabled;
        if (turnedOn)
        {
            _surface.Reset();
            if (!_passingEnabled) _last = null;           // sampling resumes after a pause
        }
        _surfaceEnabled = on;
        return turnedOn;
    }

    /// <summary>Forgets the last position and the surface baseline (reconnect, aircraft or database
    /// switch, turnaround liftoff, airborne episode). The switches survive.</summary>
    public void Reset()
    {
        _surface.Reset();
        _last = null;
    }

    /// <summary>One ground sample, in the <c>AIRCRAFT_POSITION</c> struct's own terms.</summary>
    public SurroundingsSample Sample(double lat, double lon, double surfaceType, double surfaceInfoValid, double groundSpeedKts)
    {
        // A NaN kept as the reference once confirmed a surface on the spot; skip it outright.
        if (!double.IsFinite(lat) || !double.IsFinite(lon)) return SurroundingsSample.Unusable;

        bool first = _last == null;
        bool jumped = false;
        double metres = 0;
        if (_last is { } last)
        {
            if (AirportSurroundingsMonitor.IsPositionJump(last.Lat, last.Lon, lat, lon))
            {
                // Put on the grass is not driving off the pavement: this sample is the new baseline.
                jumped = true;
                _surface.Reset();
            }
            else
            {
                metres = TaxiGeo.HaversineMeters(last.Lat, last.Lon, lat, lon);
            }
        }
        _last = (lat, lon);

        // A first sample has no distance to judge by; the surface baseline comes from the next.
        string? call = null;
        if (_surfaceEnabled && !first)
            call = _surface.Evaluate(SurfaceTypeOf(surfaceType), IsValid(surfaceInfoValid),
                                     onGround: true, groundSpeedKts, metres);
        return new SurroundingsSample(Usable: true, First: first, Jumped: jumped, SurfaceCallout: call);
    }

    // A non-finite type is -1, i.e. Unknown. Never (int)NaN: .NET 9+ saturates it to 0, CONCRETE,
    // which would tell a pilot on the grass they were back on pavement.
    private static int SurfaceTypeOf(double raw) => double.IsFinite(raw) ? (int)Math.Round(raw) : -1;

    // NaN != 0 is true, so a bare "!= 0" would call an unreadable flag valid.
    private static bool IsValid(double raw) => double.IsFinite(raw) && raw != 0;
}
