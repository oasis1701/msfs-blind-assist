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

        // ---------- The Repair and Refuel key binding: deliberately NOT a button ----------
        //
        // The POH's way out when the MFD menu cannot be reached is the simulator's Repair and
        // Refuel KEY, which the aeroplane catches in an input binding (Inputs.xml) to reset
        // the battery and the failures and push the PFD and MFD breakers in. Measured live
        // 2026-10-05: neither a calculator (>K:REPAIR_AND_REFUEL) nor a SimConnect
        // TransmitClientEvent reaches that binding — a raised FAILURES_SENS_VOLT stayed raised
        // through both — so a button would do nothing. The Reset panel's own Failures and
        // Battery buttons write the same L:vars the binding does, and always work.

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

            // ---------- Which ECU is flying the engine, and what it can sense ----------
            //
            // FADEC_RUNNING_A/B: the ECU actually controlling the engine (powered, and asked
            // for by the voter). Each ECU has its own crankshaft, camshaft and boost sensor,
            // failed separately (the FADEC and Sensors failure rows); FADEC_SENS_* is what
            // the RUNNING ECU gets, which is what the engine runs on. They were wrongly
            // offered as failure switches under their 1.1.5 names (FAILURES_CRANK_SENS and
            // friends) — derived values the model rewrites every frame, so a write did
            // nothing. ⚠️ With NO ECU running the model holds the crank signal LOST (Logic
            // 1326ff), which is true — nothing is reading the crankshaft — and is why the
            // running rows sit beside it.
            AddFlag(v, "DA40_ECU_RUNNING_A", "FADEC_RUNNING_A:1", "ECU A Running", "No", "Yes");
            AddFlag(v, "DA40_ECU_RUNNING_B", "FADEC_RUNNING_B:1", "ECU B Running", "No", "Yes");
            // Delivered (and silent - SilentCachedReadouts) so the load, RPM and fuel-flow
            // rows can say what the G1000 says when no ECU runs: nothing (see below).
            foreach (string k in new[] { "DA40_ECU_RUNNING_A", "DA40_ECU_RUNNING_B" })
            {
                v[k].UpdateFrequency = UpdateFrequency.Continuous;
                v[k].IsAnnounced = true;
            }
            AddFlag(v, "DA40_ECU_SENS_CRANK", "FADEC_SENS_CRANK:1",
                "Crankshaft Signal", "OK", "Lost");
            AddFlag(v, "DA40_ECU_SENS_CAM", "FADEC_SENS_CAM:1",
                "Camshaft Signal", "OK", "Lost");
            AddFlag(v, "DA40_ECU_SENS_BOOST", "FADEC_SENS_BOOST:1",
                "Boost Signal", "OK", "Lost");

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

    private bool _ecuARunning, _ecuBRunning, _ecuRunningKnown;

    /// <summary>The NG keys that read the ECU's own indications of load, RPM and fuel flow.</summary>
    private static readonly HashSet<string> EcuIndicationKeys = new(StringComparer.Ordinal)
    {
        "DA40_POWER_LOAD", "DA40_POWER_RPM", "DA40_POWER_FUEL_FLOW",
        "DA40_START_LOAD", "DA40_START_RPM", "DA40_FUEL_FLOW", "DA40_NG_MIN_LOAD",
        "DA40_ECU_PROP_SENSED"
    };

    private void NoteEcuRunning(string varKey, double value)
    {
        if (varKey == "DA40_ECU_RUNNING_A") { _ecuARunning = value >= 0.5; _ecuRunningKnown = true; }
        else if (varKey == "DA40_ECU_RUNNING_B") { _ecuBRunning = value >= 0.5; _ecuRunningKnown = true; }
    }

    /// <summary>
    /// ⚠️ WITH NO ECU RUNNING THE G1000 SHOWS LOAD, RPM AND FUEL FLOW AS DASHES, and MSFSBA
    /// said "Load 0 percent, green" beside them. Measured live on the NG at Colombo
    /// (2026-10-05): engine master off, both FADEC_RUNNING 0, the strip read "Load %: ———,
    /// RPM: ————, FFlow GPH: ---"; master on, ECU A running, the same gauges read 0, 0, 0.0.
    /// So with neither ECU running these rows say what the pilot would see: no reading.
    /// </summary>
    private bool TryGetEcuDeadIndication(string varKey, out string displayText)
    {
        displayText = "";
        if (!IsNG || !_ecuRunningKnown || _ecuARunning || _ecuBRunning) return false;
        if (!EcuIndicationKeys.Contains(varKey)) return false;
        displayText = "no reading";
        return true;
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
                "DA40_ECU_RUNNING_A", "DA40_ECU_RUNNING_B",
                "DA40_ECU_SENS_CRANK", "DA40_ECU_SENS_CAM", "DA40_ECU_SENS_BOOST"
            });
            AddRows(d, ElectricalPanel, new List<string> { "DA40_ELEC_BATT_ECU_TEMP" });
        }
    }
}
