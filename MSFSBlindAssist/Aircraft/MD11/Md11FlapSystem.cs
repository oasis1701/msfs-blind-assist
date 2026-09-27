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

    /// <summary>The thumbwheel's spoken name, for a set that could not be delivered.</summary>
    public const string DialName = "Dial-A-Flap";

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
    ///
    /// <paramref name="dialRaw"/> is null while the thumbwheel has not been sampled, and then the
    /// detent says so ("Dial-A-Flap, angle not yet read") — never an angle: raw 0 is a real 10°,
    /// so the 0 the callers used to substitute announced a take-off flap nobody had set.
    /// </summary>
    public string DescribePosition(double flapRng, double? dialRaw)
    {
        var detent = DetentFor(flapRng);
        if (detent == null) return "Flaps in transit";

        if (!detent.Dial) return detent.Name;
        if (dialRaw is not double raw) return $"{detent.Name}, angle not yet read";

        var deg = DegreesFor(raw);
        return $"{detent.Name}, {deg.ToString("0", CultureInfo.InvariantCulture)} degrees";
    }

    /// <summary>
    /// The spoken flap read-out's decision for a newly composed <paramref name="text"/>, given the
    /// text last recorded (<paramref name="lastRecorded"/>, empty before the first) — BASELINE-FIRST,
    /// like the spoiler read-out: a repeat is silent; the first COMPLETE text is recorded silently
    /// (connecting, or switching to the MD-11 in flight, must not announce where the handle already
    /// sits); every later change is spoken and recorded.
    ///
    /// <paramref name="complete"/> is false while the text still lacks a var it needs — the
    /// Dial-A-Flap detent before the thumbwheel's first sample. Such a text is neither spoken nor
    /// recorded while there is no baseline: the lever's sample can land before the wheel's, and a
    /// baseline of "angle not yet read" would make the wheel's first sample speak the angle on
    /// connect. Once a baseline exists it is ordinary news.
    /// </summary>
    public static (bool Speak, bool Record) ReadoutDecision(string lastRecorded, string text, bool complete)
    {
        if (string.Equals(text, lastRecorded, StringComparison.Ordinal)) return (false, false);
        if (lastRecorded.Length == 0) return (false, complete);
        return (true, true);
    }

    /// <summary>The ValueDescriptions for the handle combo: raw FLAP_RNG value → detent name.</summary>
    public Dictionary<double, string> LeverValueDescriptions()
    {
        var d = new Dictionary<double, string>();
        foreach (var det in Detents) d[det.Value] = det.Name;
        return d;
    }

    /// <summary>
    /// The <see cref="LeverValueDescriptions"/> KEY that describes a raw FLAP_RNG value — the
    /// classifier behind the handle's combo (<c>SimVarDefinition.ValueToDescriptionKey</c>).
    ///
    /// The detents are not all points: Dial-A-Flap is a BAND (38–65, the handle sitting wherever
    /// the thumbwheel puts it — TFDi's ReadyToFly state parks it at 46.91), so an exact-key lookup
    /// missed it on every real take-off and the combo opened with NO selection, where the first
    /// Down-arrow selects row 0 and commits it: "Flap Up / Slat Retracted", a walk that RETRACTS
    /// the flaps. Same classification the read-out uses (<see cref="DetentFor"/>), so the combo and
    /// the spoken position can never disagree. A value in NO detent is the handle in transit; it
    /// stays unclassified, which leaves the combo showing nothing rather than a position the
    /// handle is not in.
    /// </summary>
    public double LeverDetentKey(double flapRng) => DetentFor(flapRng)?.Value ?? flapRng;

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
    /// The listed whole degree nearest a raw thumbwheel value, clamped to the wheel's 10–25° span.
    ///
    /// Rounds half AWAY from zero — the rule <c>ToString("0")</c> applies in
    /// <see cref="DescribePosition"/> and in the panel display row — so a combo seeded from this
    /// can never show one degree while the read-out speaks another.
    /// </summary>
    public int NearestSelectableDegrees(double raw)
    {
        var deg = (int)Math.Round(DegreesFor(raw), MidpointRounding.AwayFromZero);
        return Math.Clamp(deg, (int)Math.Round(DialSpec.MinDeg), (int)Math.Round(DialSpec.MaxDeg));
    }

    /// <summary>
    /// The <see cref="DialValueDescriptions"/> KEY nearest a raw thumbwheel value — what a combo
    /// built from that dictionary must select to show the wheel's current setting, and the
    /// classifier behind it (<c>SimVarDefinition.ValueToDescriptionKey</c>).
    ///
    /// The var is CONTINUOUS (raw 33.0 for TFDi's shipped 14.95°) while the keys are whole degrees,
    /// so an exact-key lookup misses on every real value and a combo seeded that way opens with NO
    /// selection — where the first Down-arrow selects row 0 and writes 10° to the take-off wheel.
    /// Same rounding expression as the keys, so the match is exact.
    /// </summary>
    public double NearestDialChoice(double raw)
        => Math.Round(DialSpec.ToRaw(NearestSelectableDegrees(raw)), 4);

    /// <summary>
    /// What a Dial-A-Flap set says when the wheel settles AWAY from the pick: null when it landed
    /// EXACTLY (a landed set is silent — the screen reader already read the combo's pick, the rule
    /// every set on this aircraft follows), else "Dial-A-Flap 17 degrees, could not reach 20", so a
    /// wheel that stopped short is never mistaken for one that took the pick. Both are whole
    /// degrees; the caller rounds the achieved angle with <see cref="NearestSelectableDegrees"/>,
    /// the display row's rule, so the sentence and the row can never name different angles.
    ///
    /// EXACT, not "within a degree". The set is ONE direct write of the wheel's own backing var
    /// (<see cref="SetDialRawAsync"/>) and it round-trips: the combo writes
    /// <c>Math.Round(ToRaw(deg), 4)</c>, <see cref="DegreesFor"/> returns that whole degree to
    /// within 3×10⁻⁵, and <see cref="NearestSelectableDegrees"/> would need a 0.5° (3.33 raw-unit)
    /// excursion to round elsewhere — so a set that landed gives <c>gotDeg == wantDeg</c>. The ±1°
    /// band was the CEVENT walk's tolerance, and left behind it silenced a real 1° miss: the combo
    /// would show 25 while the wheel sat on 24 and the display row read 24, which is exactly the
    /// "a silently-failed selection looks identical to a successful one" case this sentence exists
    /// to prevent.
    /// </summary>
    public static string? DialSetShortfall(int wantDeg, int gotDeg)
        => gotDeg == wantDeg
            ? null
            : $"Dial-A-Flap {gotDeg.ToString(CultureInfo.InvariantCulture)} degrees, could not reach {wantDeg.ToString(CultureInfo.InvariantCulture)}";

    // ---------------------------------------------------------------------------------
    // Actuation
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Sets the thumbwheel from a RAW units target (what the combo's value carries).
    ///
    /// NOT a CEVENT walk. The wheel spans ~90 raw units and each WHEEL_UP/DOWN CEVENT moves it one
    /// unit, so reaching a target needs dozens of writes to CEVENT — a rate-limited shared slot
    /// TFDi says "do not overuse". Bursting it makes the wheel jam at an end stop (proven live).
    /// So write the wheel's own backing L:var (the OVERRIDE_ANIM_CODE source) directly through the
    /// calc path in ONE shot; the animation follows it. Clamped to the wheel's 0–100 travel.
    /// </summary>
    public Task<bool> SetDialRawAsync(double raw, SimConnectManager sim, Md11EventBus bus)
    {
        if (_dial == null) return Task.FromResult(false);
        bus.WriteExternal(DialKey, Math.Clamp(raw, 0, 100));
        return Task.FromResult(true);
    }
}
