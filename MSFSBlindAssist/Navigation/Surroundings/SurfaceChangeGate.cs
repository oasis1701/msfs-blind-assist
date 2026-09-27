namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// The surface under the wheels in the only terms worth speaking. Asphalt vs concrete vs tarmac is
/// not news; leaving the hard surface is.
/// </summary>
public enum SurfaceFamily
{
    /// <summary>`SURFACE_INFO_VALID` was false, or the enum is one this table does not name.</summary>
    Unknown,
    /// <summary>Anything a transport aircraft is meant to taxi on.</summary>
    Paved,
    /// <summary>Prepared but unpaved — gravel, dirt, sand, coral, shale, hard turf.</summary>
    Unpaved,
    Grass,
    Water,
    /// <summary>Snow or ice UNDER the wheels, i.e. the surface itself, not a contamination
    /// depth. <c>SURFACE_CONDITION</c> is the separate "wet/icy/snow" overlay on a surface that
    /// is still asphalt; this is the ground being made of it.</summary>
    SnowOrIce,
}

/// <summary>
/// Classifies the sim's <c>SURFACE_TYPE</c> enum. Measured live in MSFS 2024: 0 concrete, 1 grass,
/// 4 asphalt. The rest follows the SDK enum unconfirmed, so an unknown value is
/// <see cref="SurfaceFamily.Unknown"/>, which is never spoken.
/// </summary>
public static class SurfaceFamilies
{
    public static SurfaceFamily Of(int surfaceType) => surfaceType switch
    {
        0 or 4 or 15 or 16 or 17 or 18 or 19 or 20 or 23 => SurfaceFamily.Paved,
        //  concrete, asphalt, oil treated, steel mats, bituminus, brick, macadam, planks, tarmac
        7 or 12 or 13 or 14 or 21 or 22 => SurfaceFamily.Unpaved,
        //  hard turf, dirt, coral, gravel, sand, shale
        1 or 3 or 5 or 6 => SurfaceFamily.Grass,
        //  grass, grass bumpy, short grass, long grass
        2 => SurfaceFamily.Water,
        8 or 9 => SurfaceFamily.SnowOrIce,
        _ => SurfaceFamily.Unknown,
    };

    /// <summary>The word spoken for a family, or null for one that is never announced.</summary>
    public static string? Spoken(SurfaceFamily family) => family switch
    {
        SurfaceFamily.Paved => "pavement",
        SurfaceFamily.Unpaved => "an unpaved surface",
        SurfaceFamily.Grass => "grass",
        SurfaceFamily.Water => "water",
        SurfaceFamily.SnowOrIce => "snow or ice",
        _ => null,
    };
}

/// <summary>
/// "Off the pavement, on grass." — a blind pilot cannot see the taxiway edge. Pure: the caller
/// feeds samples, it returns the sentence or null.
///
/// <para>Confirmation is DISTANCE travelled on the new surface (time and sample counts both break
/// on a stop-start taxi), counted from the new surface's FIRST reading: the distance that arrives
/// with that reading was driven from where the old surface was last read. At the monitor's 2 s poll
/// a confirmation therefore always takes at least two readings.</para>
///
/// <para>Leaving the pavement is one bucket (any non-paved family continues it, and the confirming
/// reading names it), so mottled grass-and-gravel ground still confirms. Every other change is per
/// family, and a reading of the announced family resets what is pending. Only a change of family is
/// spoken.</para>
/// </summary>
public sealed class SurfaceChangeGate
{
    /// <summary>Distance on the new surface before it is believed: enough to cross a painted edge
    /// and a shoulder, so a wheel clipping the grass at a corner is not announced.</summary>
    public const double ConfirmMetres = 12.0;

    /// <summary>Below this a surface change is a reposition, slew or scenery load, not taxiing.</summary>
    public const double MinSpeedKts = 1.0;

    private SurfaceFamily _spoken = SurfaceFamily.Unknown;   // what the pilot was last told
    private SurfaceFamily _pending = SurfaceFamily.Unknown;  // what we are accumulating evidence for
    private double _pendingMetres;

    /// <summary>What the pilot has been told they are on. Unknown before the first callout.</summary>
    public SurfaceFamily AnnouncedFamily => _spoken;

    /// <summary>Forget everything (teleport, aircraft change, reconnect). The next surface is a
    /// silent baseline: a pilot who was placed somewhere has not driven off anything.</summary>
    public void Reset()
    {
        _spoken = SurfaceFamily.Unknown;
        _pending = SurfaceFamily.Unknown;
        _pendingMetres = 0;
    }

    /// <summary>
    /// One sample. <paramref name="metresSinceLast"/> is credited only when this reading continues
    /// evidence the previous reading opened; otherwise new evidence opens at zero. A non-finite
    /// distance counts as none and a non-finite speed as stopped. Returns the sentence, or null.
    /// </summary>
    public string? Evaluate(int surfaceType, bool surfaceInfoValid, bool onGround, double groundSpeedKts, double metresSinceLast)
    {
        if (!onGround || !surfaceInfoValid) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        var family = SurfaceFamilies.Of(surfaceType);

        // An unknown value is no evidence; never announced, never resets what the pilot was told.
        if (family == SurfaceFamily.Unknown) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        // The first family is the silent baseline: spawning on grass is not leaving a taxiway.
        if (_spoken == SurfaceFamily.Unknown) { _spoken = family; _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        if (family == _spoken) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        // Leaving the pavement: any non-paved reading continues the evidence. Otherwise only the
        // same family does.
        bool continues = _spoken == SurfaceFamily.Paved ? _pending != SurfaceFamily.Unknown : family == _pending;

        // New evidence starts at zero: the distance arriving with it was driven on the old surface.
        if (!continues) { _pending = family; _pendingMetres = 0; return null; }
        _pending = family;   // the latest reading names the sentence

        // Stopped (or NaN speed): hold the evidence, accumulate nothing.
        if (!(groundSpeedKts >= MinSpeedKts)) return null;

        // A non-finite distance is no distance (NaN would otherwise confirm on the spot).
        _pendingMetres += double.IsFinite(metresSinceLast) ? Math.Max(0, metresSinceLast) : 0;
        if (_pendingMetres < ConfirmMetres) return null;

        var from = _spoken;
        _spoken = family;
        _pending = SurfaceFamily.Unknown;
        _pendingMetres = 0;
        return Compose(from, family);
    }

    /// <summary>The sentence: leaving the pavement leads with "Off the pavement".</summary>
    internal static string Compose(SurfaceFamily from, SurfaceFamily to)
    {
        string? word = SurfaceFamilies.Spoken(to);
        if (word == null) return "";
        if (from == SurfaceFamily.Paved && to != SurfaceFamily.Paved)
            return $"Off the pavement, on {word}.";
        if (to == SurfaceFamily.Paved)
            return "Back on pavement.";
        return $"Now on {word}.";
    }
}
