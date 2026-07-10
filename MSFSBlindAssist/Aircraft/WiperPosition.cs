namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Live wiper OFF/SLOW/FAST decode shared by the A32NX (electrical circuits 77 Capt /
/// 80 F/O) and A380 (141 / 143) definitions. The position is a TWO-var state: the
/// circuit switch bool + the circuit power setting. The power setting PERSISTS at its
/// default (100%) while the switch is off, so a cold-start read of switch=off +
/// power=100 must classify OFF, not FAST — the switch always wins. Power is tolerated
/// as ratio (0.75/1.0) or percent (75/100) so a unit surprise can't collapse the two
/// speeds.
/// </summary>
public static class WiperPosition
{
    /// <summary>0 Off / 1 Slow / 2 Fast. Null halves default safe: no switch read yet
    /// = Off; no power read yet = Fast-if-on (100% is the circuit default).</summary>
    public static int FromCircuit(double? circuitSwitchOn, double? powerSetting)
    {
        if (!(circuitSwitchOn > 0.5)) return 0;
        double p = powerSetting ?? 100.0;
        if (p <= 1.5) p *= 100.0;
        return p < 87.5 ? 1 : 2;
    }

    /// <summary>Spoken/display text for a <see cref="FromCircuit"/> state.</summary>
    public static string Text(int state) => state <= 0 ? "Off" : state == 1 ? "Slow" : "Fast";
}
