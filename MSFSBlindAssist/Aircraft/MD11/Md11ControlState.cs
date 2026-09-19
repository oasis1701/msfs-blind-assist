namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// Composes the spoken state of an MD-11 control from its legend lamps, its latch and the
/// DC-power gate. Pure and order-sensitive (spec §3.3):
///   1. every LIT legend, in lamp order, joined with ", " — a lamp is the system's own answer;
///   2. else the proven latch position — valid even when the aircraft is unpowered;
///   3. else "unpowered" when the DC gate is off — every lamp reads 0 cold and dark, AND on the
///      battery alone, and calling that "normal" would lie about a dark panel (measured
///      2026-09-05; see <see cref="IsPowered"/> for why the gate is two vars, not one);
///   4. else the dark meaning ("On" for an OFF-legend button, "Not available" for EXT PWR).
/// Returns null when there is nothing to say, which MainForm renders as a bare label.
/// </summary>
public static class Md11ControlState
{
    /// <summary>Stock <c>ELECTRICAL MAIN BUS VOLTAGE</c> at or above this counts as a live bus (24 V measured on battery or external power; 0 V cold and dark).</summary>
    public const double PoweredVolts = 20.0;

    /// <summary>A lamp var above this is lit (they are 0/1 booleans; brightness is a separate var).</summary>
    public const double LitThreshold = 0.5;

    public const string Unpowered = "unpowered";

    /// <summary>
    /// Do the ANNUNCIATORS have power? Two parts, and the second is the one that matters
    /// (measured on a live MD-11F, 2026-09-05 — a deviation from the spec's volts-only §3.4).
    ///
    /// The bus voltage alone says only that SOMETHING is on. On the battery alone it already
    /// reads 24 V, but the DC busses that feed the systems annunciators are dead:
    /// <c>DC1_BUS_OFF_LT</c> and <c>AC1_OFF_LT</c> are LIT while
    /// <c>FUEL_TANK_1_PUMP_OFF_LT</c>, <c>HYD_EDP_1_L_OFF_LT</c> and <c>ELEC_GEN1_OFF_LT</c> all
    /// read 0 — not because those systems are on, but because nothing is lighting their lamps.
    /// A volts-only gate composes "Tank 1 Fuel Pumps: On" for a pump that is off. Plug in external
    /// power and the picture inverts: DC1 OFF goes dark and the pump lamp lights.
    ///
    /// No stock var separates the two states (<c>ELECTRICAL GENALT BUS VOLTAGE:1</c> reads 0 on
    /// external power too), so the gate uses the lamp the aircraft itself lights: powered means a
    /// live bus AND DC bus 1 not annunciated off. Cold: 0 V, unpowered. Battery only: DC1 OFF lit,
    /// unpowered. External, APU or generator power: DC1 OFF dark, powered. A DC1 value that has
    /// not been delivered yet counts as UNPOWERED — the honest answer before the first batch, and
    /// the same one the cold cockpit gives.
    /// </summary>
    public static bool IsPowered(double? volts, double? dc1BusOffLamp)
        => volts is >= PoweredVolts && dc1BusOffLamp is <= LitThreshold;

    public static string? Compose(Md11StateSpec? spec, Func<string, double?> read, bool powered)
    {
        if (spec == null) return null;

        // Lazily allocated: this runs on every stateful row of an open panel, once a second from
        // the display auto-refresh and again per delivery, and on a normal panel NOTHING is lit —
        // so the overwhelmingly common result was an empty list allocated and thrown away. Same
        // idiom TakeoffVSpeedCallouts uses for the same reason. `lit` stays null until the first
        // lamp is actually lit, and one lit lamp (the common non-empty case) never joins.
        List<string>? lit = null;
        string? onlyLit = null;
        foreach (var lamp in spec.Lamps)
        {
            if (read(lamp.Var) is not > LitThreshold || string.IsNullOrEmpty(lamp.Lit)) continue;
            if (onlyLit == null) { onlyLit = lamp.Lit; continue; }
            if (string.Equals(onlyLit, lamp.Lit, StringComparison.Ordinal)) continue;
            lit ??= new List<string>(spec.Lamps.Count) { onlyLit };
            if (!lit.Contains(lamp.Lit)) lit.Add(lamp.Lit);
        }
        if (lit != null) return string.Join(", ", lit);
        if (onlyLit != null) return onlyLit;

        if (spec.Latch != null && read(spec.Latch.Var) is { } position)
            return position > LitThreshold ? spec.Latch.On : spec.Latch.Off;

        if (!powered && (spec.Lamps.Count > 0 || !string.IsNullOrEmpty(spec.Dark))) return Unpowered;

        return string.IsNullOrEmpty(spec.Dark) ? null : spec.Dark;
    }
}
