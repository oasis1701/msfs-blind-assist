using System;
using System.Collections.Generic;
using System.Linq;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// The PMDG 777 power-up state the Cockpit Prep flow checks before it starts: the five Preflight
/// lines the Electrical Power Up flow already set — "Battery switch: ON" (PF_BATTERY), "ADIRU
/// switch: ON" (PF_ADIRU), "Demand pump selectors: OFF" (PF_DEMAND_PUMPS_OFF), "Navigation light:
/// ON" (PF_NAV_LIGHTS) and "Landing gear lever: DOWN" (PF_GEAR_DOWN). Cockpit Prep had no step
/// behind them, so a finished run latched them over whatever the switches read (FO-7); its
/// read-only check (CP_POWER_UP_CHECK) waits on <see cref="Field"/> instead. The evaluator
/// publishes it as a synthetic field. Pure logic, the seam <see cref="Pmdg777GearConfirmation"/>
/// uses; Pmdg777PowerUpCheckTests pins that <see cref="Conditions"/> are exactly the five lines'
/// own fields and conditions.
/// </summary>
public static class Pmdg777PowerUpCheck
{
    /// <summary>Synthetic state field: 1 = all five lines hold, 0 = one does not, NaN = an input
    /// has not been read yet.</summary>
    public const string Field = "FO_POWER_UP_OK";

    /// <summary>Each input field with the condition its Preflight line applies to it (the demand
    /// pumps line reads four selectors).</summary>
    public static IReadOnlyList<(string Input, Func<double, bool> Holds)> Conditions { get; } =
        new (string, Func<double, bool>)[]
        {
            ("ELEC_Battery_Sw_ON", v => v > 0.5),              // PF_BATTERY
            ("ADIRU_Sw_On", v => v > 0.5),                     // PF_ADIRU
            ("HYD_DemandElecPump_Selector_0", v => v < 0.5),   // PF_DEMAND_PUMPS_OFF
            ("HYD_DemandElecPump_Selector_1", v => v < 0.5),
            ("HYD_DemandAirPump_Selector_0", v => v < 0.5),
            ("HYD_DemandAirPump_Selector_1", v => v < 0.5),
            ("LTS_NAV_Sw_ON", v => v > 0.5),                   // PF_NAV_LIGHTS
            ("GEAR_Lever", v => Math.Abs(v - 1) < 0.1),        // PF_GEAR_DOWN
        };

    /// <summary>1 when every condition holds, 0 when any does not, NaN when any input is unread —
    /// an unknown reading keeps the check waiting, because NaN fails its condition. No checklist
    /// line reads this field; only the flow's check and the evaluator's IsPowerUpConfirmed do.</summary>
    public static double Value(Func<string, double> read)
    {
        var readings = Conditions.Select(c => (c.Holds, Reading: read(c.Input))).ToList();
        if (readings.Any(r => double.IsNaN(r.Reading))) return double.NaN;
        return readings.All(r => r.Holds(r.Reading)) ? 1 : 0;
    }
}
