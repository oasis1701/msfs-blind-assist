using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Ice and Pitot.
///
/// Three controls, each named in the AFM. Pitot Heat is legend item 3 and the
/// Alternate static valve is item 15; Alternate Air is not on the legend but appears
/// throughout the procedures as an OPEN/CLOSED item — before take-off "check CLOSED",
/// and OPEN in the icing and engine-trouble drills — so it is a first-class control,
/// not an afterthought.
///
/// All three were written and read back live, each with a downstream effect rather than
/// a self-echo:
///   Pitot Heat      K:PITOT_HEAT_ON/OFF   ->  A:PITOT HEAT 0/1, L:STATE_PITOT follows
///   Alternate Air   L:ENGINE_ALTERNATE_AIR ->  L:ENG_ALT_AIR_FACTOR moved 1.00 to 0.98
///   Alternate Static K:TOGGLE_ALTERNATE_STATIC -> A:ALTERNATE STATIC SOURCE OPEN 0/1
///
/// Pitot heat has a CAS consequence worth knowing about: with it OFF the G1000 shows
/// PITOT HT OFF, and with it ON while the aeroplane is stationary on the ground the
/// airframe raises PITOT FAIL. Both are modelled behaviour, not artefacts — the CAS
/// panel reports them verbatim.
///
/// The ice/wing inspection light is NOT here. It is a light, it lives on the Lighting
/// panel, and no control is duplicated across panels.
///
/// Two things deliberately NOT shown:
///   - L:ABS_AMBIENT_TEMPERATURE. "ABS" is absolute — it is KELVIN, and reported 303 on a
///     30 degC day. An L:var's Units string is only a label, so OAT is read from the
///     AMBIENT TEMPERATURE SimVar, which is already in celsius.
///   - L:STBY_DIFFERENTIAL_PRESSURE. It reads about 4 at rest and the model only uses it
///     internally, as a threshold against 200 in the glow-plug logic. It is not a cockpit
///     indication and its units are undocumented, so putting a bare "4" on the scan would
///     be noise dressed up as instrumentation.
/// </summary>
public partial class CowsDA40Definition
{
    private const string IcePitotPanel = "Ice and Pitot";

    private static Dictionary<string, SimVarDefinition> BuildIcePitotVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        v["DA40_ICE_PITOT_HEAT"] = new SimVarDefinition
        {
            Name = "PITOT HEAT",
            DisplayName = "Pitot Heat",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        v["DA40_ICE_ALTERNATE_AIR"] = new SimVarDefinition
        {
            Name = "ENGINE_ALTERNATE_AIR",
            DisplayName = "Engine Alternate Air",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        v["DA40_ICE_ALTERNATE_STATIC"] = new SimVarDefinition
        {
            Name = "ALTERNATE STATIC SOURCE OPEN",
            DisplayName = "Alternate Static Valve",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        };

        // ---------- Status ----------

        AddFlag(v, "DA40_ICE_PITOT_STATE", "STATE_PITOT", "Pitot Heat State", "Off", "On");
        AddFlag(v, "DA40_ICE_ALT_AIR_STATE", "STATE_ALTERNATE_AIR", "Alternate Air State", "Closed", "Open");

        // ⚠️ THE INDUCTION FILTER BLOCKS WITH ICE, AND NOTHING SAID SO. This is the whole
        // reason the alternate air control exists, and a blind pilot had the control and no
        // way to know they needed it. The model's own condition, read out of its logic:
        //
        //   ice accreting AND relative wind >= 60 kt AND precipitation > 5 mm/h
        //   AND ALTERNATE AIR CLOSED   ->  restriction builds, capped at 100
        //
        // and it clears itself only once the outside air is at or above zero, when the ice
        // melts off. So it is not a failure a pilot can reset - it is a consequence of
        // flying in precipitation near freezing with the filter unprotected, and the fix is
        // to open alternate air BEFORE it happens.
        //
        // ⚠️ FILTER_RESRTICTION is spelt that way in the aeroplane. It is their variable
        // and their typo; renaming it here would simply read nothing.
        v["DA40_ICE_FILTER"] = new SimVarDefinition
        {
            Name = "FILTER_RESRTICTION",
            DisplayName = "Induction Filter Restriction",
            Type = SimVarType.LVar,
            Units = "percent",
            Format = "F0",
            UpdateFrequency = UpdateFrequency.Continuous,
            // Announced only to reach the batch; the graded announcer speaks the onset.
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true
        };
        // Moves off 1.00 when alternate air opens — the induction restriction, so the
        // pilot can see the door is actually doing something.
        AddReadout(v, "DA40_ICE_ALT_AIR_FACTOR", "ENG_ALT_AIR_FACTOR", "Induction Air Factor", "", "F2");
        // Read from the SimVar, NOT from L:ABS_AMBIENT_TEMPERATURE. "ABS" means absolute:
        // that L:var is in KELVIN and rendered 303 for a 30 degC day. A units string on an
        // L:var is only a label — MSFSBA prints the raw number — so the conversion has to
        // come from the source, and the SimVar already gives celsius.
        v["DA40_ICE_OAT"] = new SimVarDefinition
        {
            Name = "AMBIENT TEMPERATURE",
            DisplayName = "Outside Air Temperature",
            Type = SimVarType.SimVar,
            Units = "celsius",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    private static readonly List<string> IcePitotControls = new()
    {
        "DA40_ICE_PITOT_HEAT",
        "DA40_ICE_ALTERNATE_AIR",
        "DA40_ICE_ALTERNATE_STATIC"
    };

    private static readonly List<string> IcePitotDisplay = new()
    {
        // Found by diffing FS Copilot's COWS_DA40NG.yaml against this definition.
        "DA40_ICE_PITOT_STATE",
        "DA40_ICE_ALT_AIR_STATE",
        "DA40_ICE_FILTER",
        "DA40_ICE_ALT_AIR_FACTOR",
        "DA40_ICE_OAT"
    };

    /// <summary>
    /// Ice and pitot writes. Pitot heat and the static valve have discrete ON/OFF and
    /// TOGGLE events respectively; alternate air is a plain latching L:var.
    /// </summary>
    private bool HandleIcePitotSet(string varKey, double value, SimConnectManager simConnect)
    {
        bool on = value >= 0.5;

        switch (varKey)
        {
            case "DA40_ICE_PITOT_HEAT":
                simConnect.ExecuteCalculatorCode(on ? "(>K:PITOT_HEAT_ON)" : "(>K:PITOT_HEAT_OFF)");
                return true;

            case "DA40_ICE_ALTERNATE_AIR":
                simConnect.SetLVar("ENGINE_ALTERNATE_AIR", on ? 1 : 0);
                return true;

            case "DA40_ICE_ALTERNATE_STATIC":
                // Only a toggle exists, so compare first and the combo stays idempotent.
                simConnect.ExecuteCalculatorCode(
                    $"(A:ALTERNATE STATIC SOURCE OPEN, Bool) {(on ? 0 : 1)} == " +
                    "if{ (>K:TOGGLE_ALTERNATE_STATIC) }");
                return true;
        }

        return false;
    }
}
