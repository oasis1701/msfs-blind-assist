namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// What the pavement under the wheels IS, in the only terms worth speaking to a pilot. The sim's
/// own <c>SURFACE_TYPE</c> enum separates asphalt from concrete from tarmac from macadam, and none
/// of those differences is news — a pilot cannot act on any of them and announcing them would make
/// the callout noise. What a pilot CAN act on is leaving the hard surface.
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
/// Classifies the sim's <c>SURFACE_TYPE</c> enum into the families above.
///
/// <para>MEASURED live 2026-09-22 in MSFS 2024, three values confirmed against known ground:
/// <c>0</c> concrete (LOWI GA apron), <c>1</c> grass (LOWI's 08L/26R grass strip, which navdata
/// records with <c>surface='G'</c>), <c>4</c> asphalt (EHAM stands and taxiways, LOWI taxiway
/// Alpha). The rest of the table is the published SDK enum and is NOT independently confirmed
/// here — which is why <see cref="Of"/> answers <see cref="SurfaceFamily.Unknown"/> for a value it
/// does not know rather than guessing a family, and why <see cref="SurfaceChangeGate"/> never
/// speaks a transition into or out of Unknown.</para>
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
/// "You have left the pavement." — the one surroundings callout with a safety case behind it
/// rather than a convenience one. A blind pilot cannot see where the taxiway edge is; the tester
/// this was designed with could not perform the grass half of its own acceptance test for exactly
/// that reason, which is the argument for the feature in one sentence.
///
/// <para>PURE: no clock, no sim access. The caller feeds it samples and it answers with the
/// sentence to speak, or null. Distance-binned, never time-binned — a taxiing aircraft stops and
/// starts constantly (a DA40 steers on differential braking), so a "has held for N seconds" rule
/// fires on a stationary aircraft and a "for N samples" rule scales with frame rate. Metres
/// travelled is the only stable axis.</para>
///
/// <para>The metres are those travelled BETWEEN two readings of the new surface, never the ones
/// before its first reading: those were driven from where the OLD surface was last read. The only
/// caller polls every 2 s (<c>AirportSurroundingsMonitor.PollMs</c>) — 10-15 m at taxi speed, a
/// whole <see cref="ConfirmMetres"/> — so crediting them let one reading confirm on its own, and a
/// wheel clipping the grass at a corner was announced (PR #230 review, SC-2). Confirming therefore
/// takes at least two readings of the new surface, whatever the speed; the tests pin this at that
/// cadence.</para>
///
/// <para>What counts as "the new surface" depends on the change. LEAVING THE PAVEMENT is one
/// surface whatever it is made of: any non-paved family, from the first such reading, so a drift
/// onto mottled ground whose readings alternate grass and gravel is one excursion, and the
/// CONFIRMING reading names it (PR #230 review, A3-6 — kept per family, each change reset the
/// evidence and that drift was never announced). EVERY OTHER change is per family: back onto the
/// pavement, or one non-paved family to another, needs readings of that family, and a reading of
/// the family the pilot was last told about resets whatever is pending — so flapping between
/// grass and gravel off the pavement stays silent.</para>
///
/// <para>Only a change of FAMILY is spoken. Asphalt to concrete happens at the edge of most
/// aprons — measured four times on one 2.35 km LOWI taxi — and carries nothing a pilot can act
/// on.</para>
/// </summary>
public sealed class SurfaceChangeGate
{
    /// <summary>
    /// How far the aircraft must travel ON the new surface before it is believed — measured from
    /// the FIRST reading of it, never before (see <see cref="Evaluate"/>). Sized to cross a
    /// painted edge and a shoulder rather than to tune a callout: below it, a wheel clipping the
    /// grass at a tight corner and coming straight back is not a departure from the taxiway, and
    /// announcing it teaches the pilot to ignore the one that matters. Distance, not time, so a
    /// stationary aircraft never accumulates confirmation.
    /// </summary>
    public const double ConfirmMetres = 12.0;

    /// <summary>
    /// Below this the aircraft is not taxiing anywhere and a surface change is a reposition, a
    /// slew or the scenery loading under it — none of which is a pilot leaving the pavement.
    /// </summary>
    public const double MinSpeedKts = 1.0;

    private SurfaceFamily _spoken = SurfaceFamily.Unknown;   // what the pilot was last told
    private SurfaceFamily _pending = SurfaceFamily.Unknown;  // what we are accumulating evidence for
    private double _pendingMetres;

    /// <summary>What the pilot has been told they are on. Unknown before the first callout.</summary>
    public SurfaceFamily AnnouncedFamily => _spoken;

    /// <summary>Forget everything — a teleport, an aircraft change, a reconnect, a new flight.
    /// The next surface becomes the silent baseline rather than a callout, exactly as it does on
    /// the first sample of a session: a pilot who has just been placed somewhere has not DRIVEN
    /// off anything.</summary>
    public void Reset()
    {
        _spoken = SurfaceFamily.Unknown;
        _pending = SurfaceFamily.Unknown;
        _pendingMetres = 0;
    }

    /// <summary>
    /// One sample. <paramref name="metresSinceLast"/> is the ground distance covered since the
    /// previous call, and it is credited only when THIS reading CONTINUES evidence the previous
    /// reading already opened: when the pavement is being left, any non-paved reading continues it
    /// (and the latest reading names the sentence); for every other change, only a reading of the
    /// same family. A reading that does not continue it opens new evidence at zero, whatever
    /// distance arrives with it, because that distance was driven from where the old surface was
    /// last read. A distance that is not a finite number counts as none, and a ground speed that is
    /// not one counts as stopped — neither can ever confirm. Returns the sentence to speak, or null.
    /// </summary>
    public string? Evaluate(int surfaceType, bool surfaceInfoValid, bool onGround, double groundSpeedKts, double metresSinceLast)
    {
        if (!onGround || !surfaceInfoValid) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        var family = SurfaceFamilies.Of(surfaceType);

        // A value this table cannot name is not evidence of anything. Hold the last announced
        // family and wait — never announce "unknown", and never let an unknown sample reset the
        // pilot's understanding of what they are on.
        if (family == SurfaceFamily.Unknown) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        // FIRST family of the session is the baseline: the pilot is told what they are on only
        // when they DRIVE onto something different. Spawning on grass is not leaving a taxiway.
        if (_spoken == SurfaceFamily.Unknown) { _spoken = family; _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        if (family == _spoken) { _pending = SurfaceFamily.Unknown; _pendingMetres = 0; return null; }

        // Does this reading CONTINUE the evidence already open? Leaving the pavement is ONE bucket:
        // while the pilot was last told "pavement", any non-paved reading continues it, so mottled
        // ground whose readings alternate grass and gravel still adds up (PR #230 review, A3-6). A
        // pending family can then only be non-paved — a Paved reading equals _spoken and reset it
        // above. Every other change is per family: back onto the pavement, or one non-paved family
        // to another, needs readings of THAT family.
        bool continues = _spoken == SurfaceFamily.Paved ? _pending != SurfaceFamily.Unknown : family == _pending;

        // A reading that does not continue it OPENS new evidence at ZERO, moving or not. The
        // distance that arrives with it was driven from where the OLD surface was last read — how
        // much of it lay on the new one is unknowable — and at the monitor's 2 s poll it is a whole
        // ConfirmMetres: credited, one reading of the grass at a corner confirmed on its own.
        if (!continues) { _pending = family; _pendingMetres = 0; return null; }
        _pending = family;   // the latest reading names the sentence

        // Stopped — or a speed that is not a number, which fails every comparison and so is tested
        // the way that fails CLOSED: hold the evidence, accumulate nothing.
        if (!(groundSpeedKts >= MinSpeedKts)) return null;

        // Math.Max(0, NaN) is NaN and NaN is never below ConfirmMetres: a distance that is not a
        // finite number once confirmed on the spot. It is no distance at all.
        _pendingMetres += double.IsFinite(metresSinceLast) ? Math.Max(0, metresSinceLast) : 0;
        if (_pendingMetres < ConfirmMetres) return null;

        var from = _spoken;
        _spoken = family;
        _pending = SurfaceFamily.Unknown;
        _pendingMetres = 0;
        return Compose(from, family);
    }

    /// <summary>
    /// The sentence. Leaving the pavement leads with the fact that it has been LEFT, because that
    /// is the actionable half and a pilot hearing only the destination surface has to infer it.
    /// Returning to pavement is the reassurance and says so plainly.
    /// </summary>
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
