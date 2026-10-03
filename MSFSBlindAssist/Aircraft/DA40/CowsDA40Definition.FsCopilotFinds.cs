using System.Collections.Generic;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// ⚠️ NINE READOUTS THE AEROPLANE HAS AND MSFSBA WAS NOT READING, found by diffing FS
/// Copilot's own COWS_DA40NG.yaml against every variable this definition binds.
///
/// FS Copilot syncs a shared cockpit, so its definition is a THIRD-PARTY INVENTORY of what
/// this airframe exposes, written by someone who had to enumerate the state that matters or
/// the two aircraft would drift apart. That makes it a coverage test of a kind the
/// interaction-surface audit structurally cannot be: that audit enumerates CLICKABLE
/// components, and none of these nine is a control - they are all things the aeroplane
/// computes and shows.
///
/// ⚠️ Everything here was READ LIVE before it was defined, because a variable named in a
/// third-party file is a claim, not evidence - the same standard the stock-event names were
/// held to. Values measured on the ground, engine off, avionics master off:
///     COWS_MINIMUMS_ALTITUDE     0          (not set)
///     FUEL_TOTALISER_REM        37.31 gal
///     ELEC_BATT_ECU_CAPACITY   145.95
///     PITOT_TEMP                29.94
///     FUEL_TEMP_C:1             29.74
///     AFCS_POWER                 0          (avionics master off - see below)
///
/// ⚠️ AFCS_POWER READING 0 IS NOT A FAULT AND IS ITSELF A CONFIRMATION. The avionics master
/// was deliberately off, and this aeroplane's systems.cfg puts the AFCS on bus.2 (BUS_AVN).
/// It agreeing with the bus wiring is why it is trustworthy as an autopilot-availability
/// readout: it answers "is the autopilot powered at all", which the GFC 700 panel could not
/// previously say.
///
/// ⚠️ UNITS ARE DECLARED "number" AND SPOKEN THROUGH THE OVERRIDE. Every one of these is an
/// L:var, and an L:var registered with a converting unit makes SimConnect convert from its
/// own base unit and return garbage - this aeroplane's own rule. The word a pilot hears
/// comes from TryGetFsCopilotDisplayOverride.
///
/// ⚠️ ELEC_BATT_ECU_CAPACITY carries NO unit in the readout, deliberately. It measured
/// 145.95, so it is not a percentage, and nothing in the package names its unit - inventing
/// "amp hours" would be a guess presented as fact. A bare number a pilot can watch fall is
/// honest; a wrong unit is not.
/// </summary>
public partial class CowsDA40Definition
{
    private static void AddFsCopilotReadout(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ExcludeFromMonitorManager = true,
            Format = "F1"
        };
    }

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotFindVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // The G1000's minimums. A blind pilot flying an approach has no other way to read
        // back the DA or MDA they set on the PFD, and 0 genuinely means "not set" - which
        // is a different answer from "I cannot tell you".
        AddFsCopilotReadout(v, "DA40_G1000_MINIMUMS", "COWS_MINIMUMS_ALTITUDE",
            "Minimums Altitude");

        // The MFD fuel calculator. ⚠️ NOT the tanks - it is a TOTALISER the pilot sets and
        // which counts down by fuel USED, so it can disagree with the tank gauges by design
        // (measured: tanks 37.2 gal against a calculator showing 32.2 remaining). Both
        // numbers are true and they answer different questions.
        AddFsCopilotReadout(v, "DA40_FUEL_TOTALISER_REM", "FUEL_TOTALISER_REM",
            "Fuel Calculator Remaining");
        AddFsCopilotReadout(v, "DA40_FUEL_TOTALISER_USED", "FUEL_TOTALISER_USE",
            "Fuel Calculator Used");

        // ⚠️ NO FUEL TEMPERATURES HERE, AND THEY WERE REMOVED RATHER THAN NEVER ADDED.
        // FUEL_TEMP_C:1/:2 are the physics behind DISP_FT:1/:2, which this definition
        // already reads AND which carry an indication failure it already binds
        // (FAILURES_DISP_FUEL_T:1/:2). Measured: the failure drove DISP_FT:1 from 36.73 to
        // -63.26 while FUEL_TEMP_C:1 sat at 36.81, so the pair would have read a healthy
        // fuel temperature off a dead gauge - the exact thing the oil and coolant readouts
        // were retracted for, missed on the first pass because diesel waxing made them look
        // like safety information rather than a duplicate.
        //
        // ⚠️ AND THE FAILURE SIGNATURE IS NOT ALWAYS ZERO. Oil temperature and volts both
        // went to 0; fuel temperature goes OFF-SCALE NEGATIVE. Never test for a failed
        // indication by comparing against 0.

        // ⚠️ THE ANSWER TO "IS THE PITOT HEAT ACTUALLY WORKING". The switch position and the
        // CAS message both say what was COMMANDED; this is the only thing that says the
        // element is warming. It sits near ambient when off.
        AddFsCopilotReadout(v, "DA40_PITOT_TEMP", "PITOT_TEMP",
            "Pitot Temperature");

        // The ECU's own backup battery - what keeps the FADEC alive if the main bus dies.
        // Unit deliberately unnamed; see the class comment.
        AddFsCopilotReadout(v, "DA40_ELEC_BATT_ECU_CAPACITY", "ELEC_BATT_ECU_CAPACITY",
            "ECU Battery Capacity");
        // ⚠️ IT RUNS 0 DOWN TO -2, NOT 0 TO 100, and calling it a "charge" made a rested
        // battery read "0.0" as though it were flat. The model clamps it (`-2 max 0 min`),
        // drives it NEGATIVE while the battery is loaded and recovers it toward 0 when the
        // load comes off, then feeds it into the charge factor as -SURF/6. So zero is the
        // RESTED state and -2 is the worst case - the opposite reading to the obvious one.
        AddFsCopilotReadout(v, "DA40_ELEC_BATT_SURF", "ELEC_BATT_SURF",
            "Battery Surface Depletion");

        // Is the autopilot POWERED - a different question from whether it is engaged, and
        // one the GFC 700 panel could not answer at all. It lives on the avionics bus.
        AddFsCopilotReadout(v, "DA40_AP_POWERED", "AFCS_POWER",
            "Autopilot Powered");

        return v;
    }

    /// <summary>
    /// The unit a pilot HEARS. See the class comment for why it cannot come from Units.
    /// </summary>
    private bool TryGetFsCopilotDisplayOverride(string varKey, double value, out string text)
    {
        // The ECU test button RAMPS - about 0.67 while held, never exactly 1 - so the
        // reading is whether it is down, not the ramp's current value.
        if (varKey == "DA40_ECU_TEST_HELD")
        {
            text = value > 0.25 ? "Held" : "Not held";
            return true;
        }

        switch (varKey)
        {
            case "DA40_G1000_MINIMUMS":
                text = value <= 0 ? "Not set" : $"{value:0} feet";
                return true;

            case "DA40_FUEL_TOTALISER_REM":
            case "DA40_FUEL_TOTALISER_USED":
                text = $"{value:0.0} gallons";
                return true;

            case "DA40_PITOT_TEMP":
                text = $"{value:0} degrees C";
                return true;

            case "DA40_AP_POWERED":
                // A bool dressed as a number by the model. Named as a state, not a figure.
                text = value > 0.5 ? "Powered" : "Not powered";
                return true;
        }

        text = "";
        return false;
    }

    /// <summary>Where each find is read. Appended to the panels they belong to.</summary>
    private static readonly List<string> FsCopilotFuelRows = new()
    {
        "DA40_FUEL_TOTALISER_REM", "DA40_FUEL_TOTALISER_USED"
    };

    private static readonly List<string> FsCopilotElectricalRows = new()
    {
        "DA40_ELEC_BATT_ECU_CAPACITY", "DA40_ELEC_BATT_SURF"
    };

    private static readonly List<string> FsCopilotIcePitotRows = new() { "DA40_PITOT_TEMP" };

    private static readonly List<string> FsCopilotAutopilotRows = new()
    {
        "DA40_AP_POWERED", "DA40_G1000_MINIMUMS"
    };

    // ==================================================================================
    // SECOND PASS: everything else the YAML named that the aeroplane really has.
    //
    // WARNING: THE YAML IS A LEAD SOURCE, NOT AN ORACLE - PROVEN, not assumed. Checking
    // every candidate against the PACKAGE ITSELF before defining it found ELEVEN names FS
    // Copilot lists that the aircraft does not contain at all: AFCS_FAIL_AIL/ELE/TRIM (the
    // real ones are FAILURES_AFCS_*, which this definition already had), the whole
    // FAILURES_SENS_* family, and LIGHTING_PANEL_1 / LIGHTING_GLARESHIELD_1 (the real
    // brightness inputs are LIGHT POTENTIOMETER, already bound). Almost certainly leftovers
    // from an older build of the aeroplane.
    //
    // Binding those would have produced eleven readouts sitting at 0 forever, quietly
    // reporting "no failure" about a system nothing is watching - worse than not having
    // them, because a pilot would scan them and be reassured. The package scan is what
    // separates a claim from evidence, and it costs one pass over 1.2 MB of XML.
    // ==================================================================================

    private static void AddFind(Dictionary<string, SimVarDefinition> v, string key, string name,
        SimVarType type, string display, string units = "number",
        string format = "F1", Dictionary<double, string>? states = null, bool announce = false)
    {
        var d = new SimVarDefinition
        {
            Name = name,
            DisplayName = display,
            Type = type,
            Units = units,
            UpdateFrequency = announce ? UpdateFrequency.Continuous : UpdateFrequency.OnRequest,
            IsAnnounced = announce,
            Format = format
        };
        if (states != null) d.ValueDescriptions = states;
        else { d.RenderAsReadOnlyStatus = true; d.ExcludeFromMonitorManager = true; }
        v[key] = d;
    }

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotSecondPassVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        var yesNo = new Dictionary<double, string> { [0] = "No", [1] = "Yes" };

        // ---------- THE TWO THAT MUST INTERRUPT ----------
        //
        // WARNING: NEITHER WAS BOUND AT ALL. An engine fire and an engine failure are the
        // two states on this aeroplane a pilot must not have to go looking for, and MSFSBA
        // could not see either. The fire is even referenced in the fuel-valve
        // documentation - "turning the valve OFF really does clear ENG ON FIRE:1" - so it
        // was known to exist and still never read.
        AddFind(v, "DA40_ENG_FIRE", "ENG ON FIRE:1", SimVarType.SimVar, "Engine Fire",
            "bool", "F0", new Dictionary<double, string> { [0] = "No", [1] = "FIRE" }, true);

        AddFind(v, "DA40_ENG_FAILED", "GENERAL ENG FAILED:1", SimVarType.SimVar,
            "Engine Failed",
            "bool", "F0", yesNo, true);

        // ---------- ENGINE THERMAL, AT FULL RESOLUTION ----------
        //
        // WARNING: WC_TEMP_BLOCK IS THE ONE WITH A LIMIT ATTACHED. The model damages the
        // block directly above 140 C, with no failure involved and regardless of the damage
        // switch - so it is the temperature that can wreck the engine while every gauge a
        // pilot can read stays green. Measured 84.99 C on the ground.
        AddFind(v, "DA40_ENG_BLOCK_TEMP", "WC_TEMP_BLOCK:1", SimVarType.LVar,
            "Block Temperature");
        AddFind(v, "DA40_ENG_RAD_TEMP", "WC_TEMP_RAD:1", SimVarType.LVar,
            "Radiator Temperature");
        AddFind(v, "DA40_ENG_THERMOSTAT", "WC_THERMOSTAT:1", SimVarType.LVar,
            "Thermostat");

        // The coolant, both halves: how much is left and how fast it is going.
        AddFind(v, "DA40_ENG_COOLANT_LEVEL", "RECIP ENG COOLANT RESERVOIR PERCENT:1",
            SimVarType.SimVar, "Coolant Reservoir",
            "percent", "F0");
        AddFind(v, "DA40_ENG_COOLANT_LEAK_RATE", "ENG_COOLANT_LEAK:1", SimVarType.LVar,
            "Coolant Leak");

        // ---------- DAMAGE THE HEALTH FIGURES DO NOT COVER ----------
        //
        // WARNING: These are SEPARATE accumulators, not components of HEALTH_*. Turbo
        // friction and fuel oscillation each track their own wear, so an engine can be
        // accumulating damage that every health percentage still reports as fine.
        AddFind(v, "DA40_DAMAGE_TURBO_FRICTION", "DAMAGE_TURBO_FRIC:1", SimVarType.LVar,
            "Turbo Friction Damage");
        AddFind(v, "DA40_DAMAGE_FUEL_OSCILLATION", "DAMAGE_FUEL_OSCI:1", SimVarType.LVar,
            "Fuel Oscillation Damage");

        // ---------- THE ECUs, BEYOND PASS/FAIL ----------
        AddFind(v, "DA40_ECU_A_STARTING", "FADEC_START_ECU_A:1", SimVarType.LVar,
            "ECU A Starting", "number", "F0", yesNo);
        AddFind(v, "DA40_ECU_B_STARTING", "FADEC_START_ECU_B:1", SimVarType.LVar,
            "ECU B Starting", "number", "F0", yesNo);
        AddFind(v, "DA40_ECU_A_FAIL_TIME", "FADEC_ECU_FAIL_TIME_A:1", SimVarType.LVar,
            "ECU A Fault Timer");
        AddFind(v, "DA40_ECU_B_FAIL_TIME", "FADEC_ECU_FAIL_TIME_B:1", SimVarType.LVar,
            "ECU B Fault Timer");

        // ---------- WHAT THE AUTOPILOT SERVOS ARE PULLING AGAINST ----------
        //
        // The one honest answer to "is the autopilot fighting me". A servo working hard is
        // how an out-of-trim aeroplane shows itself before the autopilot gives up.
        AddFind(v, "DA40_AP_FORCE_AILERON", "AFCS_FORCE_AIL", SimVarType.LVar,
            "Aileron Servo Force");
        AddFind(v, "DA40_AP_FORCE_ELEVATOR", "AFCS_FORCE_ELE", SimVarType.LVar,
            "Elevator Servo Force");

        // ---------- TO/GA, WHICH THE BUTTON SAID COULD NOT BE READ BACK ----------
        //
        // WARNING: CORRECTION TO THIS DEFINITION'S OWN COMMENT. The TO/GA button was
        // documented as carrying "no resting state and no variable to read back", so the
        // aeroplane was said to answer only through the flight director's pitch command.
        // WT_TOGA_ACTIVE is exactly that read-back, and it was in the package all along.
        AddFind(v, "DA40_AP_TOGA_ACTIVE", "WT_TOGA_ACTIVE", SimVarType.LVar,
            "Go Around Mode", "number", "F0",
            new Dictionary<double, string> { [0] = "Off", [1] = "Active" }, true);

        // ---------- THE START, WHILE IT IS HAPPENING ----------
        AddFind(v, "DA40_START_AUTOSTART_STEP", "AUTOSTART_STEP", SimVarType.LVar,
            "Auto Start Step",
            "number", "F0");
        AddFind(v, "DA40_START_INPUT", "INPUT_START", SimVarType.LVar,
            "Start Input", "percent", "F0");

        // ---------- FAILURES THAT HAD NO ROW ----------
        AddFind(v, "DA40_FAIL_FUEL_LEFT", "FAILURES_FUEL_L", SimVarType.LVar,
            "Left Fuel Failure", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_FUEL_RIGHT", "FAILURES_FUEL_R", SimVarType.LVar,
            "Right Fuel Failure", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_WASTEGATE_A", "FAILURES_WASTEGATE_A:1", SimVarType.LVar,
            "Wastegate A Failure", "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_WASTEGATE_B", "FAILURES_WASTEGATE_B:1", SimVarType.LVar,
            "Wastegate B Failure", "number", "F0", yesNo, true);

        return v;
    }

    /// <summary>Second-pass rows, by the panel each belongs to.</summary>
    private static readonly List<string> FsCopilotEngineRows = new()
    {
        "DA40_ENG_FIRE", "DA40_ENG_FAILED",
        // No gauge exists for these three, so there is no indication to read around - the
        // same judgement already made for engine damage and health.
        "DA40_ENG_BLOCK_TEMP", "DA40_ENG_RAD_TEMP", "DA40_ENG_THERMOSTAT",
        "DA40_ENG_COOLANT_LEVEL", "DA40_ENG_COOLANT_LEAK_RATE"
    };

    private static readonly List<string> FsCopilotEcuRows = new()
    {
        "DA40_ECU_A_STARTING", "DA40_ECU_B_STARTING",
        "DA40_ECU_A_FAIL_TIME", "DA40_ECU_B_FAIL_TIME"
    };

    private static readonly List<string> FsCopilotApRows2 = new()
    {
        "DA40_AP_FORCE_AILERON", "DA40_AP_FORCE_ELEVATOR", "DA40_AP_TOGA_ACTIVE"
    };

    private static readonly List<string> FsCopilotStartRows = new()
    {
        "DA40_START_AUTOSTART_STEP", "DA40_START_INPUT"
    };

    private static readonly List<string> FsCopilotDamageRows = new()
    {
        "DA40_DAMAGE_TURBO_FRICTION", "DA40_DAMAGE_FUEL_OSCILLATION"
    };

    private static readonly List<string> FsCopilotFuelFailureRows = new()
    {
        "DA40_FAIL_FUEL_LEFT", "DA40_FAIL_FUEL_RIGHT"
    };

    private static readonly List<string> FsCopilotEngineFailureRows = new()
    {
        "DA40_FAIL_WASTEGATE_A", "DA40_FAIL_WASTEGATE_B"
    };

    // ==================================================================================
    // THIRD PASS - and the correction that decided what belongs in it.
    //
    // WARNING: RETRACTED - "EVERY ENGINE TEMPERATURE READS A DISP_ VARIABLE" WAS REPORTED
    // AS A DEFECT AND IS NOT ONE. THE DISP_ BINDINGS ARE CORRECT.
    //
    // The claim was that DISP_ is the quantised drawn value and the physics behind it is
    // the honest reading. COWS's own Documentation/Failures.txt settles it the other way,
    // and a live injection proved it:
    //
    //     --Engine Indications--
    //     L:FAILURES_DISP_OT,   Loss of oil temperature indication
    //     L:FAILURES_DISP_VOLT, Loss of VOLTS indication      ... and ten more
    //
    // The INDICATION failures are named for the DISP_ variables, because DISP_ IS THE
    // INDICATION. Measured live:
    //     FAILURES_DISP_OT   = 1  ->  DISP_OT    87.59 -> 0, WC_TEMP_OIL_SENS still 86.70
    //     FAILURES_DISP_VOLT = 1  ->  DISP_VOLTS 28.14 -> 0
    //
    // So reading WC_TEMP_OIL or WC_TEMP_OIL_SENS on a panel would show a blind pilot a
    // perfect oil temperature off a DEAD GAUGE - defeating an entire failure class COWS
    // deliberately modelled, and handing them something the sighted pilot cannot have. The
    // six gauge-backed readouts added in the first version of this pass were removed again.
    //
    // THE LINE THAT CAME OUT OF IT, and it is the one to apply to the XLS and the DA42:
    //   - a quantity WITH an indication is read from the INDICATION (DISP_), so that when
    //     the indication fails the blind pilot loses it exactly as the sighted pilot does;
    //   - a quantity with NO indication at all (block temperature, radiator temperature,
    //     the thermostat, damage and health) may be exposed from the model, because there
    //     is no indication to defeat - which is the same judgement already made for the
    //     engine damage and health figures.
    //
    // WARNING: DISP_ ALSO LAGS, and that is the gauge's own needle dynamics rather than a
    // fault: measured 87.59 drawn against 86.70 computed on a COOLING engine, the drawn
    // value trailing high. A sighted pilot reads the lagging needle too.
    //
    // WARNING: THE RPM RULE SURVIVES, but only by luck of the airframe. "Take RPM from
    // PROP_RPM_SENS:1, never DISP_PROP_RPM" bypasses the indication - and FAILURES_DISP_RPM
    // does NOT EXIST on the NG (it is in the XLS-flavoured failure document along with MAP,
    // CHT and EGT). On the XLS that same rule WOULD read straight through a failed RPM
    // indication, so it has to be re-decided there rather than inherited.
    // ==================================================================================

    private static Dictionary<string, SimVarDefinition> BuildFsCopilotThirdPassVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();
        var yesNo = new Dictionary<double, string> { [0] = "No", [1] = "Yes" };

        // ---------- THE MEASURED TEMPERATURES, BESIDE THE DRAWN ONES ----------

        // The SENSORS, which are a third thing again: what the instrument is being TOLD,
        // so a failed sensor shows up as a sensor reading that disagrees with the physical
        // temperature beside it.

        // ---------- THE RAW DAMAGE ACCUMULATORS ----------
        //
        // WARNING: NOT THE SAME SCALE AS EACH OTHER. The model computes
        // HEALTH_BLOCK = 1 - DAMAGE_BLOCK/800 but HEALTH_OIL = (100 - DAMAGE_OIL)/100, so a
        // completely destroyed block publishes 0.875 health. The health percentages are
        // rescaled for the pilot; these are what the model actually counts.
        AddFind(v, "DA40_DAMAGE_BLOCK_RAW", "DAMAGE_BLOCK:1", SimVarType.LVar,
            "Block Damage");
        AddFind(v, "DA40_DAMAGE_OIL_RAW", "DAMAGE_OIL:1", SimVarType.LVar,
            "Oil Damage");
        AddFind(v, "DA40_DAMAGE_TURBO_RAW", "DAMAGE_TURBO:1", SimVarType.LVar,
            "Turbocharger Damage");
        AddFind(v, "DA40_DAMAGE_FUEL_RAW", "DAMAGE_FUEL:1", SimVarType.LVar,
            "Fuel System Damage");
        AddFind(v, "DA40_DAMAGE_FUEL_PUMP_1", "DAMAGE_FUEL:11", SimVarType.LVar,
            "Fuel Pump 1 Damage");
        AddFind(v, "DA40_DAMAGE_FUEL_PUMP_2", "DAMAGE_FUEL:12", SimVarType.LVar,
            "Fuel Pump 2 Damage");

        // ---------- FAILURES WITH NO ROW ----------
        AddFind(v, "DA40_FAIL_PROP_COMBINED", "FAILURES_PROP:1", SimVarType.LVar,
            "Propeller Failure",
            "number", "F0", yesNo, true);
        AddFind(v, "DA40_FAIL_TURBO_STOCK", "RECIP ENG TURBOCHARGER FAILED:1",
            SimVarType.SimVar, "Turbocharger Failed",
            "bool", "F0", yesNo, true);

        // ---------- WHAT THE PILOT IS HOLDING ----------
        AddFind(v, "DA40_TRIM_AXIS_INPUT", "INPUT_TRIM_AXIS", SimVarType.LVar,
            "Trim Axis Input");
        // ⚠️ "ECU_TEST:1_IsDown" DOES NOT EXIST AND THE ROW COULD ONLY EVER SAY "No". It is
        // FS Copilot's spelling, taken from its YAML, and grepping the whole installed
        // package - binary files included - finds ECU_TEST, ECU_TEST1 and ECU_TEST:1 but no
        // _IsDown of any kind. A missing L:var reads as ZERO rather than failing, which is
        // exactly how a dead binding survives: the row rendered, said "No", and a pilot
        // holding the button down would have watched it go on saying "No".
        //
        // Same family as the sixteen FS Copilot names already recorded as absent here
        // (ATT_CAGE_IsDown among them); this one was bound where those were not.
        //
        // The real variable is ECU_TEST:1, which this project's own ECU file already
        // describes: the model declares it ASOBO_GT_Push_Button_Held and RAMPS it, so it
        // reads about 0.67 while held and never exactly 1. Hence a THRESHOLD rather than a
        // yes/no on the raw value.
        AddFind(v, "DA40_ECU_TEST_HELD", "ECU_TEST:1", SimVarType.LVar,
            "ECU Test Button Held",
            "number", "F2");

        // ---------- THE STOCK SWITCH MIRRORS ----------
        //
        // Bound as the STOCK variables the sim itself keeps, beside the model's own inputs.
        // They are what an external tool sees, and a disagreement between the two is exactly
        // the kind of fault that is otherwise invisible.
        // ⚠️ A SWITCH IS OFF OR ON, NOT "No". These three read "Pitot Heat Switch: No",
        // "Fuel Pump Switch: No", "Engine Master Switch: No" - an answer to a question
        // nobody asked, where every other switch on this aeroplane says Off or On. The
        // yes/no wording belongs to the FAULT flags above it ("Trim Runaway: No"), which
        // genuinely are questions.
        var offOn = new Dictionary<double, string> { [0] = "Off", [1] = "On" };

        AddFind(v, "DA40_PITOT_HEAT_STOCK", "PITOT HEAT SWITCH:1", SimVarType.SimVar,
            "Pitot Heat Switch", "bool", "F0", offOn);
        AddFind(v, "DA40_ENGINE_MASTER_STOCK", "RECIP ENG ENGINE MASTER SWITCH:1",
            SimVarType.SimVar, "Engine Master Switch",
            "bool", "F0", offOn);
        AddFind(v, "DA40_FUEL_PUMP_STOCK", "GENERAL ENG FUEL PUMP SWITCH EX1:1",
            SimVarType.SimVar, "Fuel Pump Switch",
            "bool", "F0", offOn);

        return v;
    }

    // ⚠️ NO RESET BUTTONS HERE. Four were added and removed the same hour: this definition
    // ALREADY has all six the MFD's Reset Menu offers (DA40_FAIL_RESET, _DAMAGE, _BATT,
    // _ECU, _WIRE, _ALL), and they were invisible to the YAML diff because a BUTTON's Name
    // is its own KEY - the L:var it writes lives in the setter, not in the definition. So
    // "this L:var is not bound as a Name" does NOT mean the aeroplane cannot already do it,
    // and every candidate must also be searched for in the SOURCE before being called
    // missing. Four duplicate reset buttons is what that oversight produced.

    private static readonly List<string> FsCopilotEngineRows2 = new()
    {
        // ⚠️ The six gauge-backed temperature readouts that were here are GONE - see the
        // retraction above. What is left are the two stock SWITCH mirrors, which are
        // switch positions rather than instrument readings and so defeat no indication.
        "DA40_ENGINE_MASTER_STOCK", "DA40_FUEL_PUMP_STOCK"
    };

    private static readonly List<string> FsCopilotDamageRows2 = new()
    {
        "DA40_DAMAGE_BLOCK_RAW", "DA40_DAMAGE_OIL_RAW", "DA40_DAMAGE_TURBO_RAW",
        "DA40_DAMAGE_FUEL_RAW", "DA40_DAMAGE_FUEL_PUMP_1", "DA40_DAMAGE_FUEL_PUMP_2"
    };


    private static readonly List<string> FsCopilotTrimRows = new() { "DA40_TRIM_AXIS_INPUT" };
    private static readonly List<string> FsCopilotEcuRows2 = new() { "DA40_ECU_TEST_HELD" };
    private static readonly List<string> FsCopilotIcePitotRows2 = new() { "DA40_PITOT_HEAT_STOCK" };
    private static readonly List<string> FsCopilotEngineFailureRows2 = new()
    {
        "DA40_FAIL_PROP_COMBINED", "DA40_FAIL_TURBO_STOCK"
    };
}
