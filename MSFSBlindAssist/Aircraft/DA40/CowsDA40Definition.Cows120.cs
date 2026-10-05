using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// What COWS DA40 1.2.0 (September 2026) ADDED that a pilot can read or work, kept together so
/// the next update can be diffed against one place. Each was found by diffing the installed
/// package against the previous one recovered from the Orbx backup, file by file, and read in
/// the model rather than inferred from the changelog.
///
/// THE RULE 1.2.0 CHANGED UNDER EVERY FAILURE: "Failures are now made inactive when failures
/// are set to off." Every failure read in the model is now multiplied by L:FAILURES_ON, which
/// the failure logic itself holds at 1 only while the MFD menu's Failures Mode is anything but
/// Off. So with Failures Mode Off, a failure row can be set, reads back as set, and does
/// NOTHING — which is exactly what a pilot injecting a failure to practise on would hit first,
/// and nothing on any panel said so. <c>DA40_SIM_FAILURES_ACTIVE</c> is on every failure panel's
/// scan for that reason. It is reported, never enforced: the menu is the pilot's.
///
/// THE INDICATION FAILURES MOVED. Up to 1.1.5 a failed indication was L:FAILURES_DISP_*; 1.2.0
/// renamed the injectable failure L:FAILURES_SENS_* and turned FAILURES_DISP_* into its
/// DERIVED copy, rewritten every frame from SENS while failures are active and forced to 0
/// while they are not. A row still writing DISP would be overwritten within a frame — the rows
/// in <see cref="BuildFailureVariables"/> write SENS.
/// </summary>
public partial class CowsDA40Definition
{
    private static Dictionary<string, SimVarDefinition> BuildCows120Variables(bool isNg)
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Failures: are they live at all ----------
        v["DA40_SIM_FAILURES_ACTIVE"] = new SimVarDefinition
        {
            Name = "FAILURES_ON",
            DisplayName = "Failures Active",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Yes" }
        };

        // ---------- Pitot heat: the element, not the switch ----------
        //
        // 1.2.0 modelled the probe heater as a PTC resistance (PITOT_RES climbs with the
        // probe's temperature), so the power it draws is the honest answer to "is it heating":
        // high on a cold probe, falling as it warms, zero when the switch, the breaker or the
        // essential bus is off. The switch and the CAS message both say what was COMMANDED.
        AddReadout(v, "DA40_PITOT_POWER", "PITOT_WATT", "Pitot Heater Power", "watts", "F0");

        // ---------- Battery surface charge ----------
        //
        // 1.2.0 redid surface charge as a voltage the battery carries on top of its resting
        // voltage after charging, which bleeds away under load. It replaces the old
        // ELEC_BATT_SURF accumulator outright (0 down to -2), which no longer exists.
        AddReadout(v, "DA40_ELEC_BATT_SURF", "ELEC_BATT_SURF_VOLT", "Battery Surface Charge",
            "volts", "F2");

        // ---------- The Repair and Refuel key binding ----------
        //
        // The POH's own way out when the MFD menu cannot be reached: the aeroplane binds the
        // simulator's REPAIR_AND_REFUEL event to reset the battery and the failures and push
        // the PFD and MFD breakers back in, then passes the event on, so the simulator refuels
        // and repairs as well.
        AddResetButton(v, "DA40_FAIL_REPAIR_REFUEL", "Repair and Refuel");

        // ---------- Force-feedback options, from the vendor's binding notes ----------
        AddOptionSwitch(v, "DA40_OPT_FFB_YOKE", "FFB_YOKE", "FFB Yoke", "Off", "On");
        AddOptionSwitch(v, "DA40_OPT_FFB_SERVO_DISABLE", "FFB_YOKE_SERVO_DISABLE",
            "FFB Yoke Servo Disable", "Off", "On");

        if (isNg)
        {
            // ---------- The ECU shutdown / saving procedure ----------
            //
            // 1.2.0 gave each ECU a power state of its own. Engine master ON: the ECU comes up
            // after about a second. Engine master OFF: the ECU STAYS powered about five more
            // seconds while it saves, and if its supply (the electric master, or its breaker)
            // goes before that, it latches an ECU fault for the next flight. So "On" with the
            // engine master off is an ECU still saving — a state no lamp shows.
            AddFlag(v, "DA40_ECU_POWER_A", "FADEC_MASTER_A:1", "ECU A Power", "Off", "On");
            AddFlag(v, "DA40_ECU_POWER_B", "FADEC_MASTER_B:1", "ECU B Power", "Off", "On");

            // ---------- Which sensor the ACTIVE ECU is using ----------
            //
            // Each ECU has its own crankshaft, camshaft and boost sensor, failed separately
            // (the FADEC and Sensors failure rows). Which one the engine is running on is the
            // ACTIVE ECU's, published here: these are what the FADEC actually acts on, and
            // they were wrongly offered as failure switches under their 1.1.5 names
            // (FAILURES_CRANK_SENS and friends) — derived values the model rewrites every
            // frame, so a write did nothing. 1.2.0 renamed them FADEC_SENS_*.
            AddFlag(v, "DA40_ECU_SENS_CRANK", "FADEC_SENS_CRANK:1",
                "Active ECU Crankshaft Sensor", "Normal", "Failed");
            AddFlag(v, "DA40_ECU_SENS_CAM", "FADEC_SENS_CAM:1",
                "Active ECU Camshaft Sensor", "Normal", "Failed");
            AddFlag(v, "DA40_ECU_SENS_BOOST", "FADEC_SENS_BOOST:1",
                "Active ECU Boost Sensor", "Normal", "Failed");

            // ---------- Induction air, since 1.2.0 redid alternate air ----------
            //
            // Alternate air on the NG now draws WARM air from behind the radiator: with it
            // open the induction air is half radiator temperature and half outside air, which
            // is the whole point of opening it in icing. The old ENG_ALT_AIR_FACTOR (a
            // restriction that moved 1.00 to 0.98) no longer exists.
            AddReadout(v, "DA40_ICE_INDUCTION_TEMP", "ENG_INT_AIR_TEMP:1",
                "Induction Air Temperature", "celsius", "F0");

            // ---------- The ECU backup battery's temperature ----------
            AddReadout(v, "DA40_ELEC_BATT_ECU_TEMP", "ELEC_BATT_ECU_TEMP",
                "ECU Battery Temperature", "celsius", "F0");
        }

        return v;
    }

    /// <summary>The 1.2.0 rows, by panel, appended after every panel exists.</summary>
    private void AddCows120Rows(Dictionary<string, List<string>> d)
    {
        foreach (string panel in FailurePanels(IsNG).Keys)
            AddRows(d, panel, new List<string> { "DA40_SIM_FAILURES_ACTIVE" });

        AddRows(d, IcePitotPanel, new List<string> { "DA40_PITOT_POWER" });
        if (!IsNG) AddRows(d, IcePitotPanel, FsCopilotIcePitotRows);

        if (IsNG)
        {
            AddRows(d, EcuPanel, new List<string>
            {
                "DA40_ECU_POWER_A", "DA40_ECU_POWER_B",
                "DA40_ECU_SENS_CRANK", "DA40_ECU_SENS_CAM", "DA40_ECU_SENS_BOOST"
            });
            AddRows(d, ElectricalPanel, new List<string> { "DA40_ELEC_BATT_ECU_TEMP" });
        }
    }

    private bool HandleCows120Set(string varKey, ScreenReaderAnnouncer announcer,
        SimConnectManager simConnect)
    {
        if (varKey != "DA40_FAIL_REPAIR_REFUEL") return false;

        // The aeroplane's own binding catches the event and passes it on to the simulator.
        simConnect.ExecuteCalculatorCodeUnique("(>K:REPAIR_AND_REFUEL)");
        return true;
    }
}
