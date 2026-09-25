using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// Glareshield → Annunciator Panel: every lamp derived from the same variables the cockpit
/// lamps read (Lj35AnnunciatorLogic), announced when it lights or clears, each with its own
/// Ctrl+M row. The inputs are cached silently; the lamps themselves are pseudo-variables.
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string AnnunciatorPanel = "Annunciator Panel";

    private readonly Dictionary<string, bool> _lampState = new(StringComparer.Ordinal);
    private readonly Lj35MasterCaution _masterCaution = new();
    private bool _annunciatorTestRunning;

    private static Dictionary<string, SimVarDefinition> BuildAnnunciatorVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddButton(v, "LJ35_ANN_TEST", "GENERIC_L35A_SWITCH_ANUNNCIATOR_TEST_1", "Annunciator Test", "Lights every lamp while held.");
        AddSwitch(v, "LJ35_ALERTER_DAY_NIGHT", "LEAR_ALERTER_DAY", "Alerter Brightness", "Night", "Day");
        AddFlag(v, "LJ35_ANN_TEST_RUNNING", "L35A_TEST", "Annunciator Test", "Idle", "Running");
        Cache(v, "LJ35_ANN_TEST_RUNNING");

        // Inputs the lamps need that no other panel caches.
        AddSimReadout(v, "LJ35_FUEL_WING_L_GAL", "FUEL TANK LEFT MAIN QUANTITY", "Left Wing Fuel", "gallons", "F0");
        AddSimReadout(v, "LJ35_FUEL_WING_R_GAL", "FUEL TANK RIGHT MAIN QUANTITY", "Right Wing Fuel", "gallons", "F0");
        AddSimReadout(v, "LJ35_FUEL_PRESS_L", "FUELSYSTEM LINE FUEL PRESSURE:11", "Left Fuel Pressure", "psi", "F1");
        AddSimReadout(v, "LJ35_FUEL_PRESS_R", "FUELSYSTEM LINE FUEL PRESSURE:26", "Right Fuel Pressure", "psi", "F1");
        AddFlag(v, "LJ35_INV_PRI_BUS", "BUS CONNECTION ON:8", "Primary Inverter Bus", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_INV_SEC_BUS", "BUS CONNECTION ON:9", "Secondary Inverter Bus", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_INV_PRI_BREAKER", "BUS BREAKER PULLED:8", "Primary Inverter Breaker", "In", "Pulled", simvar: true);
        AddFlag(v, "LJ35_INV_SEC_BREAKER", "BUS BREAKER PULLED:9", "Secondary Inverter Breaker", "In", "Pulled", simvar: true);
        AddSimReadout(v, "LJ35_HYD_PSI_2", "HYDRAULIC PRESSURE:2", "Hydraulic Pressure 2", "psi", "F0");
        AddSimReadout(v, "LJ35_HYD_PSI_3", "HYDRAULIC PRESSURE:3", "Hydraulic Pressure 3", "psi", "F0");
        AddFlag(v, "LJ35_STALL_CIRCUIT_L", "CIRCUIT ON:5", "Left Stall Circuit", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_STALL_CIRCUIT_R", "CIRCUIT ON:8", "Right Stall Circuit", "Off", "On", simvar: true);
        AddFlag(v, "LJ35_ON_GROUND", "SIM ON GROUND", "On Ground", "Airborne", "On ground", simvar: true);
        AddFlag(v, "LJ35_MSW_PILOT", "GENERIC_LEAR_STEER_ON_PILOT_1", "Pilot MSW", "Released", "Pressed");
        AddFlag(v, "LJ35_MSW_COPILOT", "GENERIC_LEAR_STEER_ON_COPILOT_1", "Copilot MSW", "Released", "Pressed");
        AddFlag(v, "LJ35_XFLOW_VALVE", "FUELSYSTEM VALVE OPEN:4", "Crossflow Valve", "Closed", "Open", simvar: true);
        AddFlag(v, "LJ35_GPWS_ACTIVE", "GPWS SYSTEM ACTIVE", "GPWS", "Off", "Active", simvar: true);
        AddSimReadout(v, "LJ35_AGL", "PLANE ALT ABOVE GROUND", "Height Above Ground", "feet", "F0");
        AddSimReadout(v, "LJ35_VS", "VERTICAL SPEED", "Vertical Speed", "feet per minute", "F0");
        AddFlag(v, "LJ35_LOW_HEIGHT", "WARNING LOW HEIGHT", "Below Decision Height", "No", "Yes", simvar: true);
        AddFlag(v, "LJ35_RADALT_CIRCUIT", "CIRCUIT ON:169", "Radio Altimeter Circuit", "Off", "On", simvar: true);
        AddSimReadout(v, "LJ35_GEAR_CENTER", "GEAR CENTER POSITION", "Nose Gear", "percent", "F0");
        AddFlag(v, "LJ35_ENG_SYNC_SW", "GENERIC_LEAR_SW_SYNC_1", "Engine Sync Switch", "Off", "On");
        // For the top-of-descent estimate (Shift+D): the altitude the descent starts from, and
        // the GNS VNAV target the Working Title unit publishes only while its VNAV is armed.
        AddSimReadout(v, "LJ35_ALT_MSL", "INDICATED ALTITUDE", "Indicated Altitude", "feet", "F0");
        AddSimReadout(v, "LJ35_GPS_TARGET_ALT", "GPS TARGET ALTITUDE", "GNS VNAV Target Altitude", "feet", "F0");
        // The characteristic-speed keys (Shift+1..6) read the gross weight through the Tablet
        // panel's LJ35_TOTAL_WEIGHT — ⚠️ never a second key on "TOTAL WEIGHT": the continuous
        // batch sorts by SimVar NAME and a duplicate shifts every later variable's slot.
        foreach (var k in new[]
                 {
                     "LJ35_FUEL_WING_L_GAL", "LJ35_FUEL_WING_R_GAL", "LJ35_FUEL_PRESS_L", "LJ35_FUEL_PRESS_R",
                     "LJ35_INV_PRI_BUS", "LJ35_INV_SEC_BUS", "LJ35_INV_PRI_BREAKER", "LJ35_INV_SEC_BREAKER",
                     "LJ35_HYD_PSI_2", "LJ35_HYD_PSI_3", "LJ35_STALL_CIRCUIT_L", "LJ35_STALL_CIRCUIT_R",
                     "LJ35_ON_GROUND", "LJ35_MSW_PILOT", "LJ35_MSW_COPILOT", "LJ35_XFLOW_VALVE", "LJ35_GPWS_ACTIVE",
                     "LJ35_AGL", "LJ35_VS", "LJ35_LOW_HEIGHT", "LJ35_RADALT_CIRCUIT", "LJ35_GEAR_CENTER", "LJ35_ENG_SYNC_SW",
                     "LJ35_ALT_MSL", "LJ35_GPS_TARGET_ALT"
                 })
            Cache(v, k);

        // The lamps: Continuous + announced pseudo-variables so each earns a Ctrl+M row. Their
        // ProcessSimVarUpdate branch returns true, so the generic announcer never speaks the 0.
        foreach (var lamp in Lj35AnnunciatorLogic.Lamps)
        {
            v[lamp.Key] = new SimVarDefinition
            {
                Name = lamp.Key,
                DisplayName = lamp.Name + " Lamp",
                Type = SimVarType.LVar,
                Units = "number",
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsReadOnlyStatus = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = lamp.OffText, [1] = lamp.OnText }
            };
        }
        v["LJ35_ANN_MASTER_CAUTION"] = new SimVarDefinition
        {
            Name = "LJ35_ANN_MASTER_CAUTION",
            DisplayName = "Master Caution",
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Out", [1] = "Lit" }
        };
        return v;
    }

    private static readonly List<string> AnnunciatorControls = new() { "LJ35_ANN_TEST", "LJ35_ALERTER_DAY_NIGHT" };

    private static List<string> AnnunciatorDisplay
    {
        get
        {
            var d = new List<string> { "LJ35_ANN_MASTER_CAUTION", "LJ35_ANN_TEST_RUNNING" };
            d.AddRange(Lj35AnnunciatorLogic.Lamps.Select(l => l.Key));
            return d;
        }
    }

    private static bool HandleAnnunciatorSet(string varKey, double value, SimConnectManager sc)
    {
        switch (varKey)
        {
            case "LJ35_ANN_TEST": Pulse(sc, "GENERIC_L35A_SWITCH_ANUNNCIATOR_TEST_1", 2000); return true;
            case "LJ35_ALERTER_DAY_NIGHT": sc.SetLVar("GENERIC_LEAR_ALERTER_DAY", value); return true;
        }
        return false;
    }

    /// <summary>Called for every cached input delivery; evaluates the lamps that read that key.</summary>
    private void EvaluateLamps(string changedKey, ScreenReaderAnnouncer announcer)
    {
        if (changedKey == "LJ35_ANN_TEST_RUNNING")
        {
            _annunciatorTestRunning = Live(changedKey) > 0.5;
            return;
        }
        if (_annunciatorTestRunning) return;

        double Read(string k) => Live(k);
        bool muted(string key) => Settings.SettingsManager.Current.LJ35DisabledMonitorVariablesSet.Contains(key);

        foreach (var lamp in Lj35AnnunciatorLogic.Lamps)
        {
            if (Array.IndexOf(lamp.Inputs, changedKey) < 0) continue;
            // Every input must have arrived, or a half-baked read announces a lamp for a 0.
            bool ready = true;
            foreach (var i in lamp.Inputs) if (!Has(i)) { ready = false; break; }
            if (!ready) continue;

            bool lit = lamp.Lit(Read);
            if (_lampState.TryGetValue(lamp.Key, out var was))
            {
                if (was == lit) continue;
                _lampState[lamp.Key] = lit;
                if (!muted(lamp.Key)) announcer.Announce(lit ? lamp.OnText : lamp.OffText);
            }
            else
            {
                _lampState[lamp.Key] = lit; // baseline silently
            }
        }

        foreach (var (_, inputs, _) in Lj35MasterCaution.Causes)
        {
            if (Array.IndexOf(inputs, changedKey) < 0) continue;
            bool ready = true;
            foreach (var i in inputs) if (!Has(i)) { ready = false; break; }
            if (!ready) continue;
            var fresh = _masterCaution.Evaluate(Read);
            if (fresh.Count > 0 && _lampState.ContainsKey("LJ35_ANN_MASTER_CAUTION_BASELINED") && !muted("LJ35_ANN_MASTER_CAUTION"))
                announcer.Announce("Master caution: " + string.Join(", ", fresh));
            _lampState["LJ35_ANN_MASTER_CAUTION_BASELINED"] = true;
            break;
        }
    }

    /// <summary>Panel text for a derived lamp row; the sim value is meaningless for these.</summary>
    private bool TryGetLampDisplay(string varKey, out string text)
    {
        text = string.Empty;
        if (varKey == "LJ35_ANN_MASTER_CAUTION")
        {
            text = _masterCaution.IsLit ? "Lit" : "Out";
            return true;
        }
        var lamp = Lj35AnnunciatorLogic.Find(varKey);
        if (lamp == null) return false;
        text = _lampState.TryGetValue(varKey, out var lit) ? (lit ? lamp.OnText : lamp.OffText) : "Not yet read";
        return true;
    }
}
