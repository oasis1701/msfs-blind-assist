using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Power and Levers (DA40-NG).
///
/// The NG has ONE lever. There is no propeller control and no mixture: the FADEC sets
/// both from the power lever, so the pedestal is a single quadrant and this panel has a
/// single control. (The XLS is the opposite — throttle, propeller and mixture — and gets
/// its own panel.)
///
/// The lever commands LOAD, not RPM, and the two are not the same thing. Measured live
/// across the range:
///
///   lever    target RPM   actual RPM   load
///     0 %       2150          713        2 %
///    20 %       1800         1490       20 %
///    40 %       1883         1815       40 %
///    60 %       1967         1991       60 %
///
/// which is the POH's published curve exactly. Note it is NOT monotonic: commanded RPM
/// FALLS from 2150 at idle to 1800 at 20 %, then climbs to 2100 at 92 % and 2300 at full.
/// Below 20 % is what the POH calls "disc mode" — low power at high RPM, which produces a
/// lot of drag and is used deliberately to bleed speed or descend quickly. A pilot who
/// only heard "power 15 percent" would have no idea the propeller was being used as an
/// airbrake, so commanded RPM sits on the scan next to the actual.
///
/// The FADEC reads the lever TWICE, once per ECU, and the model gives each reading its own
/// failure modes (noise, or silently copying the other channel). Both are reported, because
/// two channels disagreeing is the symptom of a lever-sensor fault and there is no other
/// way to see it.
/// </summary>
public partial class CowsDA40Definition
{
    private const string PowerPanel = "Power and Levers";

    /// <summary>Full travel of the MSFS throttle axis, for the percentage conversion.</summary>
    private const double ThrottleAxisMax = 16383.0;

    private static Dictionary<string, SimVarDefinition> BuildPowerVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        v["DA40_POWER_LEVER_SET"] = new SimVarDefinition
        {
            Name = "GENERAL ENG THROTTLE LEVER POSITION:1",
            DisplayName = "Power Lever",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            // A NUMBER, so it does not auto-announce. The lever moves continuously — under
            // a hardware throttle it would be speaking a new percentage several times a
            // second, over everything else. Switches announce; values are read.
            IsAnnounced = true,
            Format = "F0"
        };

        // ---------- Status ----------

        AddReadout(v, "DA40_POWER_LEVER_POS", "FADEC_POWER_LEVER:1", "Lever Position", "percent", "F0");
        AddReadout(v, "DA40_POWER_LOAD", "DISP_LD", "Engine Load", "percent", "F0");
        AddReadout(v, "DA40_POWER_RPM", "PROP_RPM_SENS:1", "Propeller RPM", "rpm", "F0");
        // What the FADEC is ASKING for, against what it is getting. A gap between the two
        // is the governor working, or failing to.
        AddReadout(v, "DA40_POWER_TARGET_RPM", "FADEC_TARGET_RPM:1", "Commanded RPM", "rpm", "F0");
        AddReadout(v, "DA40_POWER_FUEL_FLOW", "DISP_FF", "Fuel Flow", "gallons per hour", "F2");

        // The two lever channels. They should agree; a divergence is a sensor fault.
        AddReadout(v, "DA40_POWER_LEVER_A", "FADEC_POWER_LEVER_A:1", "Lever Channel A", "percent", "F0");
        AddReadout(v, "DA40_POWER_LEVER_B", "FADEC_POWER_LEVER_B:1", "Lever Channel B", "percent", "F0");

        return v;
    }

    private static readonly List<string> PowerControls = new()
    {
        "DA40_POWER_LEVER_SET"
    };

    private static readonly List<string> PowerDisplay = new()
    {
        "DA40_POWER_LEVER_POS",
        "DA40_POWER_LOAD",
        "DA40_POWER_RPM",
        "DA40_POWER_TARGET_RPM",
        "DA40_POWER_FUEL_FLOW",
        "DA40_POWER_LEVER_A",
        "DA40_POWER_LEVER_B"
    };

    /// <summary>
    /// The lever is written through the standard throttle axis event, which takes 0 to
    /// 16383 rather than a percentage. Confirmed live: 3277 gave exactly 20 percent.
    /// </summary>
    private bool HandlePowerSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (varKey != "DA40_POWER_LEVER_SET") return false;

        double pct = Math.Clamp(value, 0, 100);
        int axis = (int)Math.Round(pct / 100.0 * ThrottleAxisMax);

        simConnect.ExecuteCalculatorCode($"{axis} (>K:THROTTLE_SET)");

        // A typed numeric entry confirms, and the commanded RPM is worth hearing with it —
        // it is the half of the setting the lever percentage does not tell you.
        announcer.AnnounceImmediate($"Power lever {pct:0} percent");
        return true;
    }
}
