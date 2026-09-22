using MSFSBlindAssist.Navigation.Surroundings;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Services;

/// <summary>What one ground sample meant to the two callouts that share it.</summary>
/// <param name="Usable">False when the position was not a finite number: nothing on this tick may
/// act on it, and it was not kept as the position the next distance is measured from.</param>
/// <param name="First">There was no previous position to measure from — the first ground sample
/// of a session, after <see cref="SurroundingsSampleTracker.Reset"/>, or after a pause in sampling
/// (both switches off). It is recorded, never judged, and the passing half acts from the NEXT one:
/// this one may predate a liftoff or be as old as the pause.</param>
/// <param name="Jumped">Further from the previous position than taxiing can account for
/// (<see cref="AirportSurroundingsMonitor.IsPositionJump"/>): the aircraft was PUT here. The
/// surface baseline is already dropped; the caller drops its passing tracks.</param>
/// <param name="SurfaceCallout">The surface sentence to speak (queued), or null.</param>
internal readonly record struct SurroundingsSample(bool Usable, bool First, bool Jumped, string? SurfaceCallout)
{
    /// <summary>The answer for a sample whose position could not be read.</summary>
    public static readonly SurroundingsSample Unusable = new(false, false, false, null);
}

/// <summary>
/// The per-sample half of <see cref="AirportSurroundingsMonitor"/> that needs no sim, no timer and
/// no announcer: the last position, the teleport test, the unreadable-sample guard, the two callout
/// switches and what turning one back ON forgets, and the <see cref="SurfaceChangeGate"/> they feed.
/// PURE — the monitor hands it one sample per poll and speaks what comes back — so its rules are
/// pinned at the cadence production runs at (<see cref="AirportSurroundingsMonitor.PollMs"/>),
/// which the gate's own tests, fed a distance at a time, cannot see.
///
/// <para>The monitor takes NO sample while both switches are off, so the last position is only
/// current while at least one is on. Turning a switch on therefore forgets it exactly when the
/// other was off too — a pause in sampling — and never otherwise: forgetting it while the other
/// switch kept sampling would only throw a poll of evidence away.</para>
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
    /// Sets the passing callouts' switch and returns TRUE only when this call turned it on from
    /// off: the caller's passing tracks then hold approaches nobody watched while it was off, and
    /// read against the next sample one could arm a pass that was never seen, so the caller
    /// rebaselines them. Assigning the value it already has — the settings dialog assigns every
    /// switch on every OK — is not an edge and returns false. Never touches the surface gate: the
    /// convenience callout being switched on must not cost the safety one the evidence of an
    /// excursion the pilot is driving right now.
    /// </summary>
    public bool SetPassingEnabled(bool on)
    {
        bool turnedOn = on && !_passingEnabled;
        if (turnedOn && !_surfaceEnabled) _last = null;   // sampling resumes after a pause
        _passingEnabled = on;
        return turnedOn;
    }

    /// <summary>
    /// Sets the surface callout's switch and returns TRUE only when this call turned it on from
    /// off. Turning it on drops the surface baseline: what the gate last believed was read before
    /// the switch went off and anything driven since was never watched, so the next judged
    /// surface is a silent baseline — the first surface of a session is one for the same reason.
    /// Assigning the value it already has changes nothing, so a settings OK in the middle of an
    /// excursion keeps its evidence.
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

    /// <summary>A reconnect, an aircraft or database switch, a turnaround liftoff, an airborne
    /// episode: forget the last position and the surface baseline. The switches are the pilot's
    /// settings and survive.</summary>
    public void Reset()
    {
        _surface.Reset();
        _last = null;
    }

    /// <summary>
    /// One ground sample, in the <c>AIRCRAFT_POSITION</c> struct's own terms (its surface fields
    /// are doubles). Returns what the sample meant; see <see cref="SurroundingsSample"/>.
    /// </summary>
    public SurroundingsSample Sample(double lat, double lon, double surfaceType, double surfaceInfoValid, double groundSpeedKts)
    {
        // An unreadable position is not a sample. Kept as the last position it would poison the
        // next one — no distance can be measured from a NaN, and the NaN distance it yields once
        // confirmed a surface on the spot — so it is skipped outright and the last READABLE
        // position stays the reference.
        if (!double.IsFinite(lat) || !double.IsFinite(lon)) return SurroundingsSample.Unusable;

        bool first = _last == null;
        bool jumped = false;
        double metres = 0;
        if (_last is { } last)
        {
            if (AirportSurroundingsMonitor.IsPositionJump(last.Lat, last.Lon, lat, lon))
            {
                // An aircraft PUT on the grass has not driven off anything, and the metres between
                // the two positions were never taxied: this sample becomes the silent baseline.
                jumped = true;
                _surface.Reset();
            }
            else
            {
                metres = TaxiGeo.HaversineMeters(last.Lat, last.Lon, lat, lon);
            }
        }
        _last = (lat, lon);

        // The first sample after a reset, a liftoff or a pause is RECORDED, never judged: it can
        // predate the landing (the airborne branch requests no position, and the position mirrors
        // carry the last surface forward), and a paved departure made the baseline would announce
        // a grass-strip landing as leaving the pavement.
        string? call = null;
        if (_surfaceEnabled && !first)
            call = _surface.Evaluate(SurfaceTypeOf(surfaceType), IsValid(surfaceInfoValid),
                                     onGround: true, groundSpeedKts, metres);
        return new SurroundingsSample(Usable: true, First: first, Jumped: jumped, SurfaceCallout: call);
    }

    // A surface type that is not a finite number is no family at all: -1 is outside the enum, so
    // SurfaceFamilies reads it as Unknown and the gate stays silent. Never (int)NaN, which .NET 9
    // and later saturate to 0 — CONCRETE — so a NaN would tell a pilot on the grass they were back
    // on pavement (measured on the 10.0 runtime).
    private static int SurfaceTypeOf(double raw) => double.IsFinite(raw) ? (int)Math.Round(raw) : -1;

    // SURFACE INFO VALID is a bool the sim delivers as 0 or 1. NaN != 0 is TRUE, so a bare "!= 0"
    // would call an unreadable flag valid.
    private static bool IsValid(double raw) => double.IsFinite(raw) && raw != 0;
}
