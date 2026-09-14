using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Simulation → the failure panels, plus Engine Damage and Reset.
///
/// COWS ships a Failures.txt, and it is NOT the whole story for this airframe: it is
/// largely the LYCOMING's list — magnetos, mixture and propeller cables, manifold
/// pressure, CHT and EGT — none of which an AE300 diesel has. The NG's own failures are in
/// the L:var table and not in that document at all: the FADEC's crank, cam and boost
/// sensors, each duplicated PER ECU; the two power-lever channels; the wastegate and
/// turbocharger; the glow plugs; the coolant loop. So the panels are built from the
/// aircraft's variables and the document is used for the WORDING, which is the right way
/// round.
///
/// THREE SHAPES OF FAILURE, and the document is explicit about all three:
///   - a flag, set to 1;
///   - a mode, where each number is a DIFFERENT failure of the same part;
///   - a factor from 0 to 1, "0.2 = 20% output reduced".
/// The factors are offered as a PERCENTAGE and divided by 100 on the way out, because
/// "0.35" is not how a pilot thinks about a 35 % blocked injector.
///
/// THE RESET VARIABLE IN THE VENDOR DOCUMENT DOES NOT EXIST. Failures.txt says
/// "L:FAILURES_RESET = 1 can be set to reset all failures". Nothing reads it. The model
/// writes L:RESET_FAILURES — the same two words the other way round — and verified live,
/// setting FAILURES_RESET left a raised failure raised while RESET_FAILURES cleared it.
/// There is also L:RESET_DAMAGE, and L:RESET_ALL which does both.
///
/// The aeroplane has its OWN emergency reset, and it is a real cockpit action rather than
/// a menu: engine master OFF with the ECU TEST button held for eight ticks clears the
/// failures, resets the battery and pushes the PFD and MFD breakers back in.
///
/// Every failure announces. A failure appearing without being asked for is the single most
/// important background change this aeroplane can produce.
/// </summary>
public partial class CowsDA40Definition
{
    private const string SimEnginePanel = "Engine Failures";
    private const string SimFadecPanel = "FADEC and Sensors";
    private const string SimFuelPanel = "Fuel Failures";
    private const string SimElecPanel = "Electrical Failures";
    private const string SimIndicationPanel = "Indication Failures";
    private const string SimSystemsPanel = "Flight System Failures";
    private const string SimLightsPanel = "Light Failures";
    private const string SimBrakesPanel = "Brake Failures";
    private const string SimDamagePanel = "Engine Damage";
    private const string SimResetPanel = "Reset";

    private static Dictionary<string, SimVarDefinition> BuildFailureVariables(bool isNg)
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Engine Failures ----------
        if (isNg)
        {
            AddFailureModes(v, "DA40_FAIL_BYPASS", "FAILURES_BYPASS:1", "Oil Bypass Valve",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Stuck closed", [2] = "Stuck open", [3] = "Stuck as is" });
            AddFailureModes(v, "DA40_FAIL_THERM_COOL", "FAILURES_THERMOSTAT:1", "Coolant Thermostat",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Stuck closed", [2] = "Stuck open", [3] = "Stuck as is" });
            AddFailureFlag(v, "DA40_FAIL_WATER_PUMP", "FAILURES_WATER_PUMP:1", "Water Pump");
            AddFailureFactor(v, "DA40_FAIL_COOLANT_LEAK_SET", "FAILURES_COOLANT_LEAK:1", "Coolant Leak");
            AddFailureFlag(v, "DA40_FAIL_OIL_P_SENSOR", "FAILURES_OIL_P_SENSOR:1", "Oil Pressure Sensor");
            AddFailureFlag(v, "DA40_FAIL_OIL_T_SENSOR", "FAILURES_OIL_TEMP_SENSOR:1", "Oil Temperature Sensor");
            AddFailureFactor(v, "DA40_FAIL_TURBO_SET", "FAILURES_TURBO:1", "Turbocharger");
            AddFailureFlag(v, "DA40_FAIL_WASTEGATE", "FAILURES_WASTEGATE:1", "Wastegate");
        }

        // ---------- FADEC and Sensors ----------
        if (isNg)
        {
            AddFailureFlag(v, "DA40_FAIL_CRANK_SENS", "FAILURES_CRANK_SENS:1", "Crankshaft Sensor");
            AddFailureFlag(v, "DA40_FAIL_CRANK_A", "FAILURES_CRANK_SENSOR_A:1", "Crankshaft Sensor, ECU A");
            AddFailureFlag(v, "DA40_FAIL_CRANK_B", "FAILURES_CRANK_SENSOR_B:1", "Crankshaft Sensor, ECU B");
            AddFailureFlag(v, "DA40_FAIL_CAM_SENS", "FAILURES_CAM_SENS:1", "Camshaft Sensor");
            AddFailureFlag(v, "DA40_FAIL_CAM_A", "FAILURES_CAM_SENSOR_A:1", "Camshaft Sensor, ECU A");
            AddFailureFlag(v, "DA40_FAIL_CAM_B", "FAILURES_CAM_SENSOR_B:1", "Camshaft Sensor, ECU B");
            AddFailureFlag(v, "DA40_FAIL_BOOST_SENS", "FAILURES_BOOST_SENS:1", "Boost Sensor");
            AddFailureFlag(v, "DA40_FAIL_BOOST_A", "FAILURES_BOOST_SENSOR_A:1", "Boost Sensor, ECU A");
            AddFailureFlag(v, "DA40_FAIL_BOOST_B", "FAILURES_BOOST_SENSOR_B:1", "Boost Sensor, ECU B");
            AddFailureModes(v, "DA40_FAIL_LEVER_A", "FAILURES_POWER_LEVER_A:1", "Power Lever, ECU A",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Noisy reading", [2] = "Copies the other channel" });
            AddFailureModes(v, "DA40_FAIL_LEVER_B", "FAILURES_POWER_LEVER_B:1", "Power Lever, ECU B",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Noisy reading", [2] = "Copies the other channel" });
            AddFailureFlag(v, "DA40_FAIL_PROP_A", "FAILURES_PROP_A:1", "Propeller Control, ECU A");
            AddFailureFlag(v, "DA40_FAIL_PROP_B", "FAILURES_PROP_B:1", "Propeller Control, ECU B");
            AddFailureFlag(v, "DA40_FAIL_GLOW", "FAILURES_GLOW", "Glow Plugs");
            AddFailureFactor(v, "DA40_FAIL_BOOST_LEAK_SET", "FAILURES_BOOST_LEAK:1", "Boost Leak");
        }

        // ---------- Fuel Failures ----------
        if (isNg)
        {
            AddFailureFlag(v, "DA40_FAIL_FUEL_P_SENSOR", "FAILURES_FUEL_P_SENSOR:1", "Fuel Pressure Sensor");
        }

        // ---------- Electrical Failures ----------
        AddFailureFlag(v, "DA40_FAIL_ALT", "FAILURES_ALT", "Alternator");

        // ---------- Indication Failures ----------
        AddFailureFlag(v, "DA40_FAIL_DISP_OP", "FAILURES_DISP_OP", "Oil Pressure");
        AddFailureFlag(v, "DA40_FAIL_DISP_OT", "FAILURES_DISP_OT", "Oil Temperature");
        AddFailureFlag(v, "DA40_FAIL_DISP_AMPS", "FAILURES_DISP_AMPS", "Ammeter");
        AddFailureFlag(v, "DA40_FAIL_DISP_VOLT", "FAILURES_DISP_VOLT", "Voltmeter");
        AddFailureFlag(v, "DA40_FAIL_DISP_FUEL_1", "FAILURES_DISP_FUEL:1", "Main Tank Quantity");
        AddFailureFlag(v, "DA40_FAIL_DISP_FUEL_2", "FAILURES_DISP_FUEL:2", "Auxiliary Tank Quantity");
        AddFailureFlag(v, "DA40_FAIL_DISP_FUEL_T1", "FAILURES_DISP_FUEL_T:1", "Main Tank Temperature");
        AddFailureFlag(v, "DA40_FAIL_DISP_FUEL_T2", "FAILURES_DISP_FUEL_T:2", "Auxiliary Tank Temperature");
        AddFailureFlag(v, "DA40_FAIL_DISP_GT", "FAILURES_DISP_GT", "Gearbox Temperature");
        AddFailureFlag(v, "DA40_FAIL_DISP_WT", "FAILURES_DISP_WT", "Coolant Temperature");

        // ---------- Flight System Failures ----------
        AddFailureFlag(v, "DA40_FAIL_AFCS_ELE", "FAILURES_AFCS_ELE", "Elevator Servo");
        AddFailureFlag(v, "DA40_FAIL_AFCS_AIL", "FAILURES_AFCS_AIL", "Aileron Servo");
        AddFailureFlag(v, "DA40_FAIL_AFCS_TRIM", "FAILURES_AFCS_TRIM", "Trim Servo");
        AddFailureModes(v, "DA40_FAIL_AFCS_TRIM_RUN", "FAILURES_AFCS_TRIM_RUN", "Trim Runaway",
            new Dictionary<double, string> { [-1] = "Runs nose down", [0] = "Normal", [1] = "Runs nose up" });
        AddFailureFlag(v, "DA40_FAIL_STALL_HORN", "FAILURES_STALL_HORN", "Stall Warning");
        AddFailureModes(v, "DA40_FAIL_STBY_AIRSPEED", "FAILURES_STBY_AIRSPEED", "Standby Airspeed",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Pitot line failure", [2] = "Pitot line leak", [3] = "Pitot line blockage" });
        AddFailureFlag(v, "DA40_FAIL_STBY_STATIC", "FAILURES_STBY_STATIC", "Standby Static Line");

        // ---------- Light Failures ----------
        AddFailureFlag(v, "DA40_FAIL_L_FLAP_1", "FAILURES_LIGHT_FLAP:1", "Flap UP Light");
        AddFailureFlag(v, "DA40_FAIL_L_FLAP_2", "FAILURES_LIGHT_FLAP:2", "Flap T/O Light");
        AddFailureFlag(v, "DA40_FAIL_L_FLAP_3", "FAILURES_LIGHT_FLAP:3", "Flap LDG Light");
        AddFailureModes(v, "DA40_FAIL_L_DIMMER", "FAILURES_LIGHT_DIMMER", "Instrument Lights",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Stuck full bright", [2] = "Failed off" });
        AddFailureModes(v, "DA40_FAIL_L_FLOOD", "FAILURES_LIGHT_FLOOD", "Flood Lights",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Failed", [2] = "Jittering" });
        AddFailureModes(v, "DA40_FAIL_L_LDG", "FAILURES_LIGHT_LDG", "Landing Light",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_TAX", "FAILURES_LIGHT_TAX", "Taxi Light",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_POS", "FAILURES_LIGHT_POS", "Position Lights",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_ACL", "FAILURES_LIGHT_ACL", "Anti-Collision Lights",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_CAB_1", "FAILURES_LIGHT_CAB:1", "Cabin Light Right",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_CAB_2", "FAILURES_LIGHT_CAB:2", "Cabin Light Left",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });
        AddFailureModes(v, "DA40_FAIL_L_CAB_3", "FAILURES_LIGHT_CAB:3", "Cabin Light Baggage",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Burnt out", [2] = "Breaker" });

        // ---------- Brake Failures ----------
        AddFailureModes(v, "DA40_FAIL_BRAKE_L", "FAILURES_BRAKE:1", "Left Brake",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Failed", [2] = "Loss of effectiveness", [3] = "Jammed" });
        AddFailureModes(v, "DA40_FAIL_BRAKE_R", "FAILURES_BRAKE:2", "Right Brake",
            new Dictionary<double, string> { [0] = "Normal", [1] = "Failed", [2] = "Loss of effectiveness", [3] = "Jammed" });

        // ---------- Engine Damage ----------
        if (isNg)
        {
            AddFailureFlag(v, "DA40_FAIL_OIL_PUMP", "FAILURES_OIL:1", "Oil Pump");
            AddFailureFlag(v, "DA40_FAIL_BLOCK", "FAILURES_BLOCK:1", "Engine Block");
        }

        // ⚠️ THREE OF THESE CARRY A ":1" AND IT IS LOAD-BEARING. The oil pump, the engine
        // block and the bypass are read by the aeroplane as FAILURES_OIL:1,
        // FAILURES_BLOCK:1 and FAILURES_BYPASS:1; MSFSBA wrote the UNINDEXED name, which
        // is a different variable that nothing reads. Measured before the fix: arming the
        // oil pump failure with the engine running left oil pressure at 2.41 bar and then
        // 2.40 eight seconds later - it did nothing at all, which is exactly how it was
        // reported. A write-stick test cannot catch this; only reading what the MODEL
        // reads can.

        // ---------- Breaker trips ----------
        // Thirty of them, all read by the aeroplane, none previously offered.
        AddBreakerTrip(v, "DA40_FAIL_CBT_ADC", "FAILURES_CB_ADC", "ADC");
        AddBreakerTrip(v, "DA40_FAIL_CBT_AFCS", "FAILURES_CB_AFCS", "Autopilot Computer");
        AddBreakerTrip(v, "DA40_FAIL_CBT_AHRS", "FAILURES_CB_AHRS", "AHRS");
        AddBreakerTrip(v, "DA40_FAIL_CBT_ALT", "FAILURES_CB_ALT", "Alternator");
        AddBreakerTrip(v, "DA40_FAIL_CBT_AP", "FAILURES_CB_AP", "Autopilot");
        AddBreakerTrip(v, "DA40_FAIL_CBT_AUD", "FAILURES_CB_AUD", "Audio Panel");
        AddBreakerTrip(v, "DA40_FAIL_CBT_AV_FAN", "FAILURES_CB_AV_FAN", "Avionics Fan");
        AddBreakerTrip(v, "DA40_FAIL_CBT_CDU_FAN", "FAILURES_CB_CDU_FAN", "CDU Fan");
        AddBreakerTrip(v, "DA40_FAIL_CBT_COM1", "FAILURES_CB_COM1", "COM 1");
        AddBreakerTrip(v, "DA40_FAIL_CBT_COM2", "FAILURES_CB_COM2", "COM 2");
        AddBreakerTrip(v, "DA40_FAIL_CBT_ECA", "FAILURES_CB_ECA", "ECU A");
        AddBreakerTrip(v, "DA40_FAIL_CBT_ECB", "FAILURES_CB_ECB", "ECU B");
        AddBreakerTrip(v, "DA40_FAIL_CBT_ENGINST", "FAILURES_CB_ENGINST", "Engine Instruments");
        AddBreakerTrip(v, "DA40_FAIL_CBT_ESS_TIE", "FAILURES_CB_ESS_TIE", "Essential Bus Tie");
        AddBreakerTrip(v, "DA40_FAIL_CBT_FLAP", "FAILURES_CB_FLAP", "Flap");
        AddBreakerTrip(v, "DA40_FAIL_CBT_FLAPS", "FAILURES_CB_FLAPS", "Flaps");
        AddBreakerTrip(v, "DA40_FAIL_CBT_FPA", "FAILURES_CB_FPA", "Fuel Pump A");
        AddBreakerTrip(v, "DA40_FAIL_CBT_FPB", "FAILURES_CB_FPB", "Fuel Pump B");
        AddBreakerTrip(v, "DA40_FAIL_CBT_HORIZON", "FAILURES_CB_HORIZON", "Standby Horizon");
        AddBreakerTrip(v, "DA40_FAIL_CBT_MAIN_TIE", "FAILURES_CB_MAIN_TIE", "Main Bus Tie");
        AddBreakerTrip(v, "DA40_FAIL_CBT_MAST", "FAILURES_CB_MAST", "Master Control");
        AddBreakerTrip(v, "DA40_FAIL_CBT_MFD", "FAILURES_CB_MFD", "MFD");
        AddBreakerTrip(v, "DA40_FAIL_CBT_NAV1", "FAILURES_CB_NAV1", "NAV 1");
        AddBreakerTrip(v, "DA40_FAIL_CBT_NAV2", "FAILURES_CB_NAV2", "NAV 2");
        AddBreakerTrip(v, "DA40_FAIL_CBT_PFD", "FAILURES_CB_PFD", "PFD");
        AddBreakerTrip(v, "DA40_FAIL_CBT_PITOT", "FAILURES_CB_PITOT", "Pitot Heat");
        AddBreakerTrip(v, "DA40_FAIL_CBT_START", "FAILURES_CB_START", "Starter");
        AddBreakerTrip(v, "DA40_FAIL_CBT_TAS", "FAILURES_CB_TAS", "Traffic");
        AddBreakerTrip(v, "DA40_FAIL_CBT_XFR", "FAILURES_CB_XFR", "Transfer Pump");
        AddBreakerTrip(v, "DA40_FAIL_CBT_XPDR", "FAILURES_CB_XPDR", "Transponder");

        // ---------- Reset ----------

        // THE VENDOR DOCUMENT IS WRONG HERE. Failures.txt says
        // "L:FAILURES_RESET = 1 can be set to reset all failures"; nothing in the model
        // reads that variable. The model's own reset writes L:RESET_FAILURES - the same
        // two words the other way round - and setting FAILURES_RESET was verified live to
        // leave a raised failure raised, while RESET_FAILURES cleared it.
        // The MFD's own Reset Menu offers SIX resets, not three. MSFSBA shipped the three
        // that clear FAILURES and stopped there, which left a blind pilot unable to reach
        // the three that clear STATE - and state is what actually strands the aeroplane.
        // Found live at VCBI: a saved state restored FLAT batteries (all three capacities
        // at 0 against factory 230/140/20), so the ECU could never power and nothing would
        // crank, and no amount of reloading helped because the state was reloaded with it.
        // "Reset: ECU" then cleared an ECU A FAIL that the AFM's own clearing procedure -
        // run twice, correctly - could not. The names come from the MFD plugin's own menu.
        //
        // ⚠️ AND THE MENU IS NOT THE SAME ON BOTH AIRFRAMES. The COWS POH lists it per
        // variant (Section I, "Reset menu"), and a scan of the two installed packages
        // agrees exactly: RESET_ECU and RESET_WIRE appear in the NG's XML 4 and 3 times and
        // in the XLS's NOT AT ALL, while RESET_FLOOD and RESET_PLUGS appear in the XLS's 3
        // and 2 times and in the NG's not at all. MSFSBA gave BOTH variants the NG set, so
        // on the XLS two buttons wrote L:vars that do not exist - a press that announces
        // "ECUs reset" and does nothing - and the two the XLS really has were unreachable.
        //
        // Losing those two is the expensive half. The XLS is the variant that can be
        // FLOODED - its own start procedure is written around not flooding it - and it is
        // the one that FOULS PLUGS, which MSFSBA reports per plug on the Mixture panel with
        // no way to clear. Both are the aircraft's own documented way out.
        AddResetButton(v, "DA40_FAIL_RESET", "Clear Failures");
        AddResetButton(v, "DA40_FAIL_RESET_DAMAGE", "Clear Engine Damage");
        AddResetButton(v, "DA40_FAIL_RESET_BATT", "Reset Batteries");
        AddResetButton(v, "DA40_FAIL_RESET_ECU", "Reset ECUs");
        AddResetButton(v, "DA40_FAIL_RESET_WIRE", "Reset Fuel Valve Safety Wire");
        AddResetButton(v, "DA40_FAIL_RESET_FLOOD", "Clear Flooded Engine");
        AddResetButton(v, "DA40_FAIL_RESET_PLUGS", "Clear Spark Plug Fouling");
        AddResetButton(v, "DA40_FAIL_RESET_ALL", "Clear Failures and Damage");

        return v;
    }

    private static void AddResetButton(Dictionary<string, SimVarDefinition> v, string key,
        string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = key,
            DisplayName = label,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };
    }


    /// <summary>
    /// A failure that POPS A BREAKER, which is a different thing from the breaker being
    /// pulled and deserves its own name for it.
    ///
    /// The aeroplane models thirty of these and MSFSBA offered none, which is a large part
    /// of the gap between the 79 failures it exposed and the "130+ random failures" COWS
    /// advertises. They genuinely work - the starter one is read right beside the write
    /// that trips CB_STR - and they are the mechanism behind a breaker popping in flight
    /// rather than a pilot pulling it.
    ///
    /// No " Failure" suffix here: "Starter Breaker Trip" already says what it is, and
    /// "Starter Breaker Trip Failure" says it twice.
    /// </summary>
    private static void AddBreakerTrip(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label + " Breaker Trip",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Normal",
                [1] = "WILL TRIP"
            }
        };
    }

    /// <summary>A failure that is simply present or not.</summary>
    private static void AddFailureFlag(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label + " Failure",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Normal",
                [1] = "FAILED"
            }
        };
    }

    /// <summary>
    /// A failure where each number is a DIFFERENT failure of the same part, so the options
    /// are named rather than numbered - "stuck open" and "stuck closed" are not degrees of
    /// one thing.
    /// </summary>
    private static void AddFailureModes(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label, Dictionary<double, string> modes)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label + " Failure",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = modes
        };
    }

    /// <summary>
    /// A failure with a severity. The airframe wants 0 to 1; this is entered as a
    /// PERCENTAGE and divided on the way out, because "0.35" is not how anyone thinks
    /// about a 35 percent blocked injector.
    /// </summary>
    private static void AddFailureFactor(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string label)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = label + " Failure",
            Type = SimVarType.LVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            Format = "F0",
            Scale = 100.0
        };
    }

    private static readonly List<string> SimEngineControls = new()
    {
        "DA40_FAIL_BYPASS",
        "DA40_FAIL_THERM_COOL",
        "DA40_FAIL_WATER_PUMP",
        "DA40_FAIL_COOLANT_LEAK_SET",
        "DA40_FAIL_OIL_P_SENSOR",
        "DA40_FAIL_OIL_T_SENSOR",
        "DA40_FAIL_TURBO_SET",
        "DA40_FAIL_WASTEGATE",
    };

    private static readonly List<string> SimFadecControls = new()
    {
        "DA40_FAIL_CRANK_SENS",
        "DA40_FAIL_CRANK_A",
        "DA40_FAIL_CRANK_B",
        "DA40_FAIL_CAM_SENS",
        "DA40_FAIL_CAM_A",
        "DA40_FAIL_CAM_B",
        "DA40_FAIL_BOOST_SENS",
        "DA40_FAIL_BOOST_A",
        "DA40_FAIL_BOOST_B",
        "DA40_FAIL_LEVER_A",
        "DA40_FAIL_LEVER_B",
        "DA40_FAIL_PROP_A",
        "DA40_FAIL_PROP_B",
        "DA40_FAIL_GLOW",
        "DA40_FAIL_BOOST_LEAK_SET"
    };

    private static readonly List<string> SimFuelControls = new()
    {
        "DA40_FAIL_FUEL_P_SENSOR",
    };

    private static readonly List<string> SimElecControls = new()
    {
        "DA40_FAIL_ALT",
    };

    private static readonly List<string> SimIndicationControls = new()
    {
        "DA40_FAIL_DISP_OP",
        "DA40_FAIL_DISP_OT",
        "DA40_FAIL_DISP_AMPS",
        "DA40_FAIL_DISP_VOLT",
        "DA40_FAIL_DISP_FUEL_1",
        "DA40_FAIL_DISP_FUEL_2",
        "DA40_FAIL_DISP_FUEL_T1",
        "DA40_FAIL_DISP_FUEL_T2",
        "DA40_FAIL_DISP_GT",
        "DA40_FAIL_DISP_WT"
    };

    private static readonly List<string> SimSystemsControls = new()
    {
        "DA40_FAIL_AFCS_ELE",
        "DA40_FAIL_AFCS_AIL",
        "DA40_FAIL_AFCS_TRIM",
        "DA40_FAIL_AFCS_TRIM_RUN",
        "DA40_FAIL_STALL_HORN",
        "DA40_FAIL_STBY_AIRSPEED",
        "DA40_FAIL_STBY_STATIC"
    };

    private static readonly List<string> SimLightsControls = new()
    {
        "DA40_FAIL_L_FLAP_1",
        "DA40_FAIL_L_FLAP_2",
        "DA40_FAIL_L_FLAP_3",
        "DA40_FAIL_L_DIMMER",
        "DA40_FAIL_L_FLOOD",
        "DA40_FAIL_L_LDG",
        "DA40_FAIL_L_TAX",
        "DA40_FAIL_L_POS",
        "DA40_FAIL_L_ACL",
        "DA40_FAIL_L_CAB_1",
        "DA40_FAIL_L_CAB_2",
        "DA40_FAIL_L_CAB_3"
    };

    private static readonly List<string> SimBrakesControls = new()
    {
        "DA40_FAIL_BRAKE_L",
        "DA40_FAIL_BRAKE_R"
    };

    private static readonly List<string> SimDamageControls = new()
    {
        "DA40_FAIL_OIL_PUMP",
        "DA40_FAIL_BLOCK"
    };

    private static readonly List<string> BreakerTripControls = new()
    {
        "DA40_FAIL_CBT_ADC",
        "DA40_FAIL_CBT_AFCS",
        "DA40_FAIL_CBT_AHRS",
        "DA40_FAIL_CBT_ALT",
        "DA40_FAIL_CBT_AP",
        "DA40_FAIL_CBT_AUD",
        "DA40_FAIL_CBT_AV_FAN",
        "DA40_FAIL_CBT_CDU_FAN",
        "DA40_FAIL_CBT_COM1",
        "DA40_FAIL_CBT_COM2",
        "DA40_FAIL_CBT_ECA",
        "DA40_FAIL_CBT_ECB",
        "DA40_FAIL_CBT_ENGINST",
        "DA40_FAIL_CBT_ESS_TIE",
        "DA40_FAIL_CBT_FLAP",
        "DA40_FAIL_CBT_FLAPS",
        "DA40_FAIL_CBT_FPA",
        "DA40_FAIL_CBT_FPB",
        "DA40_FAIL_CBT_HORIZON",
        "DA40_FAIL_CBT_MAIN_TIE",
        "DA40_FAIL_CBT_MAST",
        "DA40_FAIL_CBT_MFD",
        "DA40_FAIL_CBT_NAV1",
        "DA40_FAIL_CBT_NAV2",
        "DA40_FAIL_CBT_PFD",
        "DA40_FAIL_CBT_PITOT",
        "DA40_FAIL_CBT_START",
        "DA40_FAIL_CBT_TAS",
        "DA40_FAIL_CBT_XFR",
        "DA40_FAIL_CBT_XPDR",
    };

    /// <summary>The three both airframes have, in the MFD menu's own order.</summary>
    private static readonly List<string> SharedResetControls = new()
    {
        "DA40_FAIL_RESET_DAMAGE",
        "DA40_FAIL_RESET",
        "DA40_FAIL_RESET_BATT"
    };

    /// <summary>The NG's two: the ECUs, and the fuel-valve safety wire it alone has.</summary>
    private static readonly List<string> NgResetControls = new()
    {
        "DA40_FAIL_RESET_ECU",
        "DA40_FAIL_RESET_WIRE"
    };

    /// <summary>
    /// The XLS's two. Flooding empties the engine of fuel and cools the lines; plugs clears
    /// the fouling. Neither exists on the NG - a diesel cannot be flooded and has no plugs.
    /// </summary>
    private static readonly List<string> XlsResetControls = new()
    {
        "DA40_FAIL_RESET_FLOOD",
        "DA40_FAIL_RESET_PLUGS"
    };

    private static List<string> ResetControlsFor(bool isNg)
    {
        var l = new List<string>(SharedResetControls);
        l.AddRange(isNg ? NgResetControls : XlsResetControls);
        l.Add("DA40_FAIL_RESET_ALL");
        return l;
    }

    /// <summary>Every failure panel, for the wiring. NG-only panels are filtered at build.</summary>
    private Dictionary<string, List<string>> FailurePanels(bool isNg)
    {
        var d = new Dictionary<string, List<string>>();

        // ⚠️ THE XLS GETS ITS OWN THREE, NOT NONE. These used to be NG-only outright, so the
        // XLS had no engine, fuel or damage failures at all - see CowsDA40Definition.XlsFailures.
        // Only the FADEC panel is genuinely NG-only; a Lycoming has no ECU.
        d[SimEnginePanel] = isNg
            ? new List<string>(SimEngineControls)
            : XlsEngineFailureControls();
        if (isNg) d[SimFadecPanel] = new List<string>(SimFadecControls);
        d[SimFuelPanel] = isNg
            ? new List<string>(SimFuelControls)
            : XlsFuelFailureControls();
        d[SimElecPanel] = new List<string>(SimElecControls);
        d[SimIndicationPanel] = new List<string>(SimIndicationControls);
        d[SimSystemsPanel] = new List<string>(SimSystemsControls);
        d[SimLightsPanel] = new List<string>(SimLightsControls);
        d[SimBrakesPanel] = new List<string>(SimBrakesControls);
        // ⚠️ THE XLS HAS NO SETTABLE DAMAGE CONTROL AT ALL - its damage is an accumulator,
        // not a switch - so the panel is display-only and its entry here must still EXIST
        // and be empty, or MainForm's panel build early-returns and the panel renders
        // nothing. The readings themselves are XlsDamageDisplay(), added as display rows.
        d[SimDamagePanel] = isNg
            ? new List<string>(SimDamageControls)
            : new List<string>();

        var trips = new List<string>(BreakerTripControls);
        if (!isNg) trips.AddRange(XlsBreakerTripControls());
        d["Breaker Trips"] = trips;
        d[SimResetPanel] = ResetControlsFor(isNg);
        return d;
    }

    /// <summary>The factor failures, whose written value is a hundredth of what is typed.</summary>
    private static readonly HashSet<string> FailureFactorKeys = new()
    {
        "DA40_FAIL_COOLANT_LEAK_SET",
        "DA40_FAIL_TURBO_SET",
        "DA40_FAIL_BOOST_LEAK_SET",
        "DA40_FAIL_INJ_4"
    };

    private bool HandleFailureSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_FAIL_RESET":
                simConnect.SetLVar("RESET_FAILURES", 1);
                announcer.AnnounceImmediate("Failures cleared");
                return true;

            case "DA40_FAIL_RESET_DAMAGE":
                simConnect.SetLVar("RESET_DAMAGE", 1);
                announcer.AnnounceImmediate("Engine damage cleared");
                return true;

            case "DA40_FAIL_RESET_BATT":
                simConnect.SetLVar("RESET_BATT", 1);
                announcer.AnnounceImmediate("Batteries reset to full charge");
                return true;

            case "DA40_FAIL_RESET_ECU":
                simConnect.SetLVar("RESET_ECU", 1);
                announcer.AnnounceImmediate("ECUs reset");
                return true;

            case "DA40_FAIL_RESET_WIRE":
                simConnect.SetLVar("RESET_WIRE", 1);
                announcer.AnnounceImmediate("Fuel valve safety wire restored");
                return true;

            case "DA40_FAIL_RESET_FLOOD":
                simConnect.SetLVar("RESET_FLOOD", 1);
                announcer.AnnounceImmediate("Engine cleared of fuel, lines cooled");
                return true;

            case "DA40_FAIL_RESET_PLUGS":
                simConnect.SetLVar("RESET_PLUGS", 1);
                announcer.AnnounceImmediate("Spark plug fouling cleared");
                return true;

            case "DA40_FAIL_RESET_ALL":
                simConnect.SetLVar("RESET_ALL", 1);
                announcer.AnnounceImmediate("Failures and damage cleared");
                return true;
        }

        if (!varKey.StartsWith("DA40_FAIL_") || !GetVariables().TryGetValue(varKey, out var def))
        {
            return false;
        }

        if (FailureFactorKeys.Contains(varKey))
        {
            double pct = Math.Clamp(value, 0, 100);
            simConnect.SetLVar(def.Name, pct / 100.0);
            announcer.AnnounceImmediate($"{def.DisplayName} {pct:0} percent");
            return true;
        }

        simConnect.SetLVar(def.Name, value);
        return true;
    }
}
