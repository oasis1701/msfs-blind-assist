using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Lighting Switches.
///
/// The AFM's abbreviation list names the switch bank exactly: LANDING, TAXI/MAP,
/// POSITION, STROBE (= anti-collision), INST. LT and FLOOD. Legend item 10 is
/// "Rotary buttons for instrument lighting and flood light" and item 11 "Light
/// switches", so those two are BRIGHTNESS KNOBS, not on/off toggles — which is how
/// they are exposed here.
///
/// The knob wiring is read straight out of the model's Logic.xml rather than guessed:
///
///   L:STATE_LIGHT_INS  &lt;-&gt;  A:LIGHT POTENTIOMETER:3   (+ K:PANEL_LIGHTS_SET)
///   L:STATE_LIGHT_FLD  &lt;-&gt;  A:LIGHT POTENTIOMETER:5   (+ K:GLARESHIELD_LIGHTS_SET)
///
/// Both STATE_ vars are percentages, not booleans — INST. LT read 100 with the panel
/// lit. Toggling GLARESHIELD_LIGHTS_SET alone did NOT move STATE_LIGHT_FLD, which is
/// what sent me to the XML: the potentiometer is the real control and the bool is only
/// a companion.
///
/// The three cabin lights are individually switched overhead (right, left, baggage) as
/// A:LIGHT CABIN:1/2/3, and that is exactly how they appear here. COWS also puts a
/// convenience clickspot on the standby airspeed needle that toggles all three at once,
/// but that is a MOUSE SHORTCUT, not a control on the aeroplane's panel — so it is not
/// offered here. The three switches already do everything it does.
///
/// The pilot's flood light is wired directly to the main battery and works with the
/// electric master off (POH), which is why the Emergency-procedures checklist reaches
/// for it after an electrical failure.
///
/// COMPLETENESS, checked against the model rather than assumed:
///   - Every light switch here is TWO-POSITION. The only three-position switches in the
///     cockpit are the ECU voter, the ignition key and the fuel selector — none of them
///     lights.
///   - There is NO beacon. The AFM abbreviation list defines STROBE as the
///     anti-collision light, and the word "beacon" does not occur in the model at all.
///   - L:STATE_LIGHT_ICE mirrors A:LIGHT WING, the wing/ice inspection light. The model
///     provides NO cockpit switch for it, so it is reported on the scan and not offered
///     as a control — inventing a switch the aeroplane does not have would be as wrong
///     as omitting one it does.
/// </summary>
public partial class CowsDA40Definition
{
    private const string LightingPanel = "Lighting Switches";

    private static Dictionary<string, SimVarDefinition> BuildLightingVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddLightSwitch(v, "DA40_LIGHT_LANDING", "LIGHT LANDING", "Landing Light");
        // TAXI, and only taxi. The label used to say "Taxi and Map Light", which the
        // aeroplane does not support: systems.cfg puts every Type 6 emitter in the left
        // wing (TAXI_N, TAXI_R, TAXI_F, TAXI_B) and there is no cabin map light on the
        // circuit. The AFM's placard reads TAXI.
        AddLightSwitch(v, "DA40_LIGHT_TAXI", "LIGHT TAXI", "Taxi Light");
        AddLightSwitch(v, "DA40_LIGHT_POSITION", "LIGHT NAV", "Position Lights");
        AddLightSwitch(v, "DA40_LIGHT_STROBE", "LIGHT STROBE", "Strobe Lights");

        // Brightness knobs, 0-100 %.
        AddBrightness(v, "DA40_LIGHT_INSTRUMENT_SET", "LIGHT POTENTIOMETER:3", "Instrument Lights");
        AddBrightness(v, "DA40_LIGHT_FLOOD_SET", "LIGHT POTENTIOMETER:5", "Flood Light");

        // Overhead cabin lights — individually switched, as on the aeroplane.
        AddLightSwitch(v, "DA40_LIGHT_CABIN_RIGHT", "LIGHT CABIN:1", "Cabin Light Right");
        AddLightSwitch(v, "DA40_LIGHT_CABIN_LEFT", "LIGHT CABIN:2", "Cabin Light Left");
        AddLightSwitch(v, "DA40_LIGHT_CABIN_BAGGAGE", "LIGHT CABIN:3", "Cabin Light Baggage");

        // ---------- Status ----------

        AddReadout(v, "DA40_LIGHT_INS_LEVEL", "STATE_LIGHT_INS", "Instrument Brightness", "percent", "F0");
        AddReadout(v, "DA40_LIGHT_FLD_LEVEL", "STATE_LIGHT_FLD", "Flood Brightness", "percent", "F0");
        AddFlag(v, "DA40_LIGHT_ICE_STATE", "STATE_LIGHT_ICE", "Ice Inspection Light", "Off", "On");
        AddFlag(v, "DA40_LIGHT_LGN_STATE", "STATE_LIGHT_LGN", "Landing Light State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_TXI_STATE", "STATE_LIGHT_TXI", "Taxi Light State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_POS_STATE", "STATE_LIGHT_POS", "Position Light State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_STB_STATE", "STATE_LIGHT_STB", "Strobe Light State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_CBN1_STATE", "STATE_LIGHT_CBN1", "Cabin Right State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_CBN2_STATE", "STATE_LIGHT_CBN2", "Cabin Left State", "Off", "On");
        AddFlag(v, "DA40_LIGHT_CBN3_STATE", "STATE_LIGHT_CBN3", "Cabin Baggage State", "Off", "On");

        return v;
    }

    private static void AddLightSwitch(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "bool",
            // Continuous + announced: a light thrown from the cockpit or from hardware
            // must speak. MSFSBA's own combo sets are covered by the global _uiSetEcho
            // wrap, so setting it from the panel does not double-announce.
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };
    }

    private static void AddBrightness(Dictionary<string, SimVarDefinition> v, string key,
        string simvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = simvar,
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsSlider = true,
            SliderMin = 0,
            SliderMax = 100
        };
    }

    private static readonly List<string> LightingControls = new()
    {
        "DA40_LIGHT_LANDING",
        "DA40_LIGHT_TAXI",
        "DA40_LIGHT_POSITION",
        "DA40_LIGHT_STROBE",
        "DA40_LIGHT_INSTRUMENT_SET",
        "DA40_LIGHT_FLOOD_SET",
        "DA40_LIGHT_CABIN_RIGHT",
        "DA40_LIGHT_CABIN_LEFT",
        "DA40_LIGHT_CABIN_BAGGAGE"
    };

    private static readonly List<string> LightingDisplay = new()
    {
        "DA40_LIGHT_LGN_STATE",
        "DA40_LIGHT_TXI_STATE",
        "DA40_LIGHT_POS_STATE",
        "DA40_LIGHT_STB_STATE",
        "DA40_LIGHT_INS_LEVEL",
        "DA40_LIGHT_FLD_LEVEL",
        "DA40_LIGHT_CBN1_STATE",
        "DA40_LIGHT_CBN2_STATE",
        "DA40_LIGHT_CBN3_STATE",
        "DA40_LIGHT_ICE_STATE"
    };

    /// <summary>
    /// Lighting writes. The four exterior lights only expose TOGGLE events, so each is a
    /// conditional toggle — picking the value a light is already at must not flip it off.
    /// The two knobs go through their potentiometer SET events, which take a percentage.
    /// </summary>
    private bool HandleLightingSet(string varKey, double value, SimConnectManager simConnect)
    {
        int on = value >= 0.5 ? 1 : 0;

        switch (varKey)
        {
            case "DA40_LIGHT_LANDING":
                simConnect.ExecuteCalculatorCode(
                    $"(A:LIGHT LANDING, Bool) {1 - on} == if{{ (>K:LANDING_LIGHTS_TOGGLE) }}");
                return true;

            case "DA40_LIGHT_TAXI":
                simConnect.ExecuteCalculatorCode(
                    $"(A:LIGHT TAXI, Bool) {1 - on} == if{{ (>K:TOGGLE_TAXI_LIGHTS) }}");
                return true;

            case "DA40_LIGHT_POSITION":
                simConnect.ExecuteCalculatorCode(
                    $"(A:LIGHT NAV, Bool) {1 - on} == if{{ (>K:TOGGLE_NAV_LIGHTS) }}");
                return true;

            case "DA40_LIGHT_STROBE":
                simConnect.ExecuteCalculatorCode(
                    $"(A:LIGHT STROBE, Bool) {1 - on} == if{{ (>K:STROBES_TOGGLE) }}");
                return true;

            // Percentage knobs. PANEL_LIGHTS_SET / GLARESHIELD_LIGHTS_SET are written
            // alongside so the boolean companion the model also reads stays consistent.
            case "DA40_LIGHT_INSTRUMENT_SET":
                simConnect.ExecuteCalculatorCode(
                    $"{value:0} (>K:LIGHT_POTENTIOMETER_3_SET) " +
                    $"{(value > 0 ? 1 : 0)} (>K:PANEL_LIGHTS_SET)");
                return true;

            case "DA40_LIGHT_FLOOD_SET":
                simConnect.ExecuteCalculatorCode(
                    $"{value:0} (>K:LIGHT_POTENTIOMETER_5_SET) " +
                    $"{(value > 0 ? 1 : 0)} (>K:GLARESHIELD_LIGHTS_SET)");
                return true;

            // Indexed cabin lights: the event takes index then value.
            case "DA40_LIGHT_CABIN_RIGHT":
                simConnect.ExecuteCalculatorCode($"1 {on} (>K:2:CABIN_LIGHTS_SET)");
                return true;

            case "DA40_LIGHT_CABIN_LEFT":
                simConnect.ExecuteCalculatorCode($"2 {on} (>K:2:CABIN_LIGHTS_SET)");
                return true;

            case "DA40_LIGHT_CABIN_BAGGAGE":
                simConnect.ExecuteCalculatorCode($"3 {on} (>K:2:CABIN_LIGHTS_SET)");
                return true;

            case "DA40_LIGHT_CABIN_ALL":
                simConnect.ExecuteCalculatorCode("(>K:TOGGLE_CABIN_LIGHTS)");
                return true;
        }

        return false;
    }
}
