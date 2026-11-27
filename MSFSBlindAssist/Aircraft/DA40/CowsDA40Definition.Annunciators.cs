using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Instrument Panel → Annunciators.
///
/// The PHYSICAL annunciator lights, which on the G1000 variant of the DA40-NG are just
/// four: the three flap position lights and the essential-bus light. Everything else the
/// AFM calls an annunciation — ENG TEMP, OIL PRES, GLOW ON, STARTER, ECU A/B FAIL and the
/// rest — is drawn on the G1000's CAS window, not on a lamp, and belongs with the CAS
/// readout rather than here. (The AFM also documents a White Wire annunciator panel
/// variant with real lamps for all of those; COWS models the G1000 one.)
///
/// This panel is entirely READ-ONLY. An annunciator is an output; there is nothing to set.
///
/// Each lamp is a genuine circuit, not a repeat of the switch that drives it. The flap
/// lights are lit from the actual measured flap travel AND gated on their own circuit
/// breakers, so the model's emissive code reads:
///
///     TRAILING EDGE FLAPS LEFT PERCENT less-equal 40
///       AND NOT FAILURES_LIGHT_FLAP:1
///       AND CB_FLP in
///       AND CB_INT in     ->  FLAP_LIGHT:1
///
/// which means a flap light can be dark because the flaps are elsewhere, because the bulb
/// has failed, or because a breaker is out — three different things a sighted pilot
/// distinguishes by looking at the panel and the breakers together. Reporting the lamp
/// state as its own reading, separate from the flap selector position, is what makes that
/// distinction available at all.
/// </summary>
public partial class CowsDA40Definition
{
    private const string AnnunciatorsPanel = "Annunciators";

    private static Dictionary<string, SimVarDefinition> BuildAnnunciatorVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddFlag(v, "DA40_ANN_FLAP_UP", "FLAP_LIGHT:1", "Flap Light UP", "Off", "Lit");
        AddFlag(v, "DA40_ANN_FLAP_TO", "FLAP_LIGHT:2", "Flap Light T/O", "Off", "Lit");
        AddFlag(v, "DA40_ANN_FLAP_LDG", "FLAP_LIGHT:3", "Flap Light LDG", "Off", "Lit");

        // The essential-bus lamp is lit from ELEC_BUS_BATT_VOLT against the master, so it
        // is a real indication of bus health rather than a copy of the ESS BUS switch.
        AddReadout(v, "DA40_ANN_ESS_BUS_VOLTS", "ELEC_BUS_BATT_VOLT", "Essential Bus Lamp Supply", "volts", "F1");

        // The breakers that can extinguish a flap light while everything else is healthy.
        AddFlag(v, "DA40_ANN_CB_FLAP", "STATE_CB_FLP", DA40BreakerPlacards.For("CB_FLP") + " Breaker", "In", "Out");
        AddFlag(v, "DA40_ANN_CB_INT", "STATE_CB_INT", DA40BreakerPlacards.For("CB_INT") + " Breaker", "In", "Out");

        // The flap position the lights are reporting on, so a dark panel can be read as
        // "flaps are in transit" rather than "a lamp has failed".
        v["DA40_ANN_FLAP_TRAVEL"] = new SimVarDefinition
        {
            Name = "TRAILING EDGE FLAPS LEFT PERCENT",
            DisplayName = "Flap Travel",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F0"
        };

        return v;
    }

    /// <summary>
    /// Nothing to set — every annunciator is an output. The panel still needs its (empty)
    /// controls entry, which the structure loop provides, or MainForm would render nothing
    /// at all and the status display would never appear.
    /// </summary>
    private static readonly List<string> AnnunciatorsDisplay = new()
    {
        "DA40_ANN_FLAP_UP",
        "DA40_ANN_FLAP_TO",
        "DA40_ANN_FLAP_LDG",
        "DA40_ANN_FLAP_TRAVEL",
        "DA40_ANN_ESS_BUS_VOLTS",
        "DA40_ANN_CB_FLAP",
        "DA40_ANN_CB_INT"
    };
}
