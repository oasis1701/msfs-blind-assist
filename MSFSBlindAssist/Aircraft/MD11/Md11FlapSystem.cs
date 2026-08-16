using System.Globalization;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The MD-11's combined flap/slat handle and its Dial-A-Flap thumbwheel.
///
/// THE HANDLE. Unlike an Airbus's flap lever or a 737's flap gate, the MD-11 has ONE handle that
/// commands flaps and leading-edge slats together. Clean to fully extended, the detents are:
///
///   FLAP UP / SLAT RET   flaps and slats stowed — the clean-flight position
///   0 / EXT              slats out, flaps still at zero: extra low-speed margin, minimal drag
///   DIAL-A-FLAP          the variable take-off detent; the ANGLE comes from the thumbwheel,
///                        not from the handle position (see below)
///   28                   principally the go-around setting
///   35                   a normal landing flap setting — less drag than 50
///   50                   maximum landing flaps: more drag, lower speed, not required every landing
///
/// There is a physical gate at 28 so the handle cannot slip straight between the take-off range
/// and the landing range. That is why 28 is a detent in its own right rather than a waypoint on
/// the way up: a go-around from 35 or 50 retracts to 28 FIRST.
///
/// So from clean, successive "extend one step" commands give: 0/EXT → DIAL-A-FLAP → 28 → 35 → 50.
///
/// THE THUMBWHEEL. Dial-A-Flap is what makes the take-off detent variable: the thumbwheel selects
/// any take-off flap angle from 10° to 25°, and the handle, placed in the DIAL-A-FLAP detent,
/// extends the flaps to whatever the wheel says. Handle position and selected angle are therefore
/// two independent facts, and a blind pilot needs both — "Dial-A-Flap" alone doesn't say whether
/// you're taking off on 10 or on 25. Every read-out here reports the angle alongside the detent.
///
/// WHY THE DETENTS ARE CURATED RATHER THAN PARSED. TFDi's tooltip encodes five positions as an
/// RPN <c>%{case}</c> map and the Dial-A-Flap detent as a RANGE test
/// (<c>38 65 (L:MD11_FLAP_RNG) rng</c>) that sits OUTSIDE the case block. A generic parser sees
/// the cases and misses the range, so the lever reads as five positions instead of six — with the
/// take-off detent, the one the thumbwheel exists to serve, missing entirely. The generator pins
/// all six in its CURATED table; this class consumes them.
/// </summary>
public sealed class Md11FlapSystem
{
    /// <summary>The flap handle's node id — also its MSFSBA variable key.</summary>
    public const string LeverKey = "MD11_FLAP_LATCH";

    /// <summary>The Dial-A-Flap thumbwheel's node id — also its MSFSBA variable key.</summary>
    public const string DialKey = "MD11_DIALAFLAP_WHEEL_RNG";

    /// <summary>Set by TFDi while the flaps are in transit; drives the "flaps moving" read-out.</summary>
    public const string FlapsMovingVar = "MD11_FLAPS_MOVING";

    private readonly Md11Control? _lever;
    private readonly Md11Control? _dial;

    public Md11FlapSystem(Md11ControlMap map)
    {
        _lever = map.Controls.FirstOrDefault(c => c.NodeId == LeverKey);
        _dial = map.Controls.FirstOrDefault(c => c.NodeId == DialKey);
    }

    public Md11Control? Lever => _lever;
    public Md11Control? Dial => _dial;

    /// <summary>The handle's six detents, clean → fully extended.</summary>
    public IReadOnlyList<Md11Detent> Detents => _lever?.Detents ?? new List<Md11Detent>();

    /// <summary>The thumbwheel's raw↔degrees transform.</summary>
    public Md11DialAFlapSpec DialSpec => _dial?.DialAFlap ?? new Md11DialAFlapSpec();

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The detent the handle is currently in, or null if it is between detents (in transit).
    /// Uses the curated range test, so the Dial-A-Flap band (FLAP_RNG 38–65) resolves correctly
    /// instead of collapsing onto the neighbouring point detents.
    /// </summary>
    public Md11Detent? DetentFor(double flapRng)
        => Detents.FirstOrDefault(d => d.Matches(flapRng));

    /// <summary>
    /// The selected take-off flap angle in degrees, from the thumbwheel's raw value.
    /// TFDi's own tooltip formula: degrees = 10 + raw / 6.6667.
    /// </summary>
    public double DegreesFor(double dialRaw) => DialSpec.ToDegrees(dialRaw);

    /// <summary>
    /// What the screen reader says for the handle's current position.
    ///
    /// The Dial-A-Flap detent always carries its angle ("Dial-A-Flap, 15 degrees"): the detent
    /// name alone is not actionable — it tells the pilot the handle is in the take-off detent but
    /// not what take-off flap setting they are about to rotate on.
    /// </summary>
    public string DescribePosition(double flapRng, double dialRaw)
    {
        var detent = DetentFor(flapRng);
        if (detent == null) return "Flaps in transit";

        if (!detent.Dial) return detent.Name;

        var deg = DegreesFor(dialRaw);
        return $"{detent.Name}, {deg.ToString("0", CultureInfo.InvariantCulture)} degrees";
    }

    /// <summary>The ValueDescriptions for the handle combo: raw FLAP_RNG value → detent name.</summary>
    public Dictionary<double, string> LeverValueDescriptions()
    {
        var d = new Dictionary<double, string>();
        foreach (var det in Detents) d[det.Value] = det.Name;
        return d;
    }

    // ---------------------------------------------------------------------------------
    // Dial-A-Flap combo
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// The selectable take-off angles: 10° through 25°, one degree at a time.
    ///
    /// Whole degrees because that is how the setting is briefed and how the real thumbwheel is
    /// read ("dial a flap 15"); the underlying var is continuous, but offering fractions would be
    /// a combo box with hundreds of meaningless entries for a blind pilot to arrow through.
    /// </summary>
    public IEnumerable<int> SelectableDegrees()
    {
        for (var d = (int)Math.Round(DialSpec.MinDeg); d <= (int)Math.Round(DialSpec.MaxDeg); d++)
            yield return d;
    }

    /// <summary>
    /// ValueDescriptions for the Dial-A-Flap combo, keyed by RAW thumbwheel units (the var's own
    /// unit) so the combo's selected value maps straight back onto what the aircraft reports.
    /// </summary>
    public Dictionary<double, string> DialValueDescriptions()
    {
        var d = new Dictionary<double, string>();
        foreach (var deg in SelectableDegrees())
            d[Math.Round(DialSpec.ToRaw(deg), 4)] = $"{deg} degrees";
        return d;
    }

    /// <summary>
    /// Half a degree in raw units — the convergence tolerance for a thumbwheel walk. Anything
    /// inside this rounds to the requested whole degree.
    /// </summary>
    public double DialToleranceRaw => DialSpec.UnitsPerDeg / 2.0;

    // ---------------------------------------------------------------------------------
    // Actuation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Moves the handle to the detent whose representative value is <paramref name="targetRng"/>.
    /// Closed-loop: the handle only exposes relative wheel events, and their direction is not
    /// documented, so the walker calibrates against the aircraft rather than guessing.
    /// </summary>
    public Task<bool> SetLeverAsync(double targetRng, SimConnectManager sim, Md11EventBus bus)
        => _lever == null
            ? Task.FromResult(false)
            : Md11SelectorWalker.WalkAsync(_lever, targetRng, LeverKey, sim, bus);

    /// <summary>
    /// Sets the thumbwheel to a whole take-off flap angle (10–25°).
    /// Analog walk: one probe click measures both step size and direction, then the rest is
    /// arithmetic — see <see cref="Md11SelectorWalker.WalkAnalogAsync"/>.
    /// </summary>
    public Task<bool> SetDialDegreesAsync(int degrees, SimConnectManager sim, Md11EventBus bus)
    {
        if (_dial == null) return Task.FromResult(false);
        var clamped = Math.Clamp(degrees, (int)Math.Round(DialSpec.MinDeg), (int)Math.Round(DialSpec.MaxDeg));
        return Md11SelectorWalker.WalkAnalogAsync(
            _dial, DialSpec.ToRaw(clamped), DialKey, sim, bus, DialToleranceRaw);
    }

    /// <summary>
    /// Sets the thumbwheel from a RAW units target (what the combo's value carries).
    ///
    /// NOT a CEVENT walk. The wheel spans ~90 raw units and each WHEEL_UP/DOWN CEVENT moves it one
    /// unit, so reaching a target needs dozens of writes to CEVENT — a rate-limited shared slot
    /// TFDi says "do not overuse". Bursting it makes the wheel jam at an end stop (proven live).
    /// So write the wheel's own backing L:var (the OVERRIDE_ANIM_CODE source) directly through the
    /// calc path in ONE shot; the animation follows it. Clamped to the wheel's 0–100 travel.
    /// </summary>
    public Task<bool> SetDialRawAsync(double raw, SimConnectManager sim, Md11EventBus bus,
        System.Threading.CancellationToken ct = default)
    {
        if (_dial == null) return Task.FromResult(false);
        bus.WriteExternal(DialKey, Math.Clamp(raw, 0, 100));
        return Task.FromResult(true);
    }
}
