using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.FBWA380;

/// <summary>
/// Reads FlyByWire A380 control state from the SimConnect cache for the First
/// Officer's checklist auto-detection. Mirrors <see cref="Fenix.FenixStateEvaluator"/>.
/// </summary>
public sealed class FbwA380StateEvaluator : LVarStateEvaluator
{
    private static readonly string[] PollFields =
    {
        "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
        "A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON",
        "A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", "A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON",
        "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
        "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
        "PUSH_OVHD_OXYGEN_CREW",
        "LIGHT_NAV", "LIGHT_LOGO", "LIGHT_BEACON", "LIGHT_STROBE", "LIGHT_WING",
        "LIGHT_LANDING", "LIGHT_TAXI_OVHD",
        "SEATBELT_SIGN",
        "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_Position", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_Position",
        // Engine anti-ice state = the stock ENG_ANTI_ICE:n readouts. The ENGn_ANTI_ICE
        // write keys are Act() combos with NO backing L:var — polling them reads junk.
        "WING_ANTI_ICE_OVHD", "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2", "ENG_ANTI_ICE:3", "ENG_ANTI_ICE:4",
        "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
        "A32NX_KNOB_OVHD_AIRCOND_XBLEED_POSITION", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_POSITION",
        "A32NX_OVHD_COND_HOT_AIR_1_PB_IS_ON", "A32NX_OVHD_COND_HOT_AIR_2_PB_IS_ON",
        "ANTISKID_BRAKES_ACTIVE", "A32NX_PARK_BRAKE_LEVER_POS", "A32NX_SPOILERS_ARMED",
        "A32NX_GEAR_HANDLE_POSITION", "ENGINE_MODE_SELECTOR",
        "ENG_VALVE_SWITCH:1", "ENG_VALVE_SWITCH:2", "ENG_VALVE_SWITCH:3", "ENG_VALVE_SWITCH:4",
        "XMLVAR_A320_WeatherRadar_Sys", "A32NX_SWITCH_RADAR_PWS_Position",
        "FUELPUMP_FEEDTK1_MAIN", "FUELPUMP_FEEDTK1_STBY", "FUELPUMP_FEEDTK2_MAIN", "FUELPUMP_FEEDTK2_STBY",
        "FUELPUMP_FEEDTK3_MAIN", "FUELPUMP_FEEDTK3_STBY", "FUELPUMP_FEEDTK4_MAIN", "FUELPUMP_FEEDTK4_STBY",
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
        "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_AUTOBRAKES_SELECTED_MODE",
        "A32NX_EFIS_L_ND_MODE", "A32NX_EFIS_R_ND_MODE",
        "A32NX_EFIS_L_ND_RANGE", "A32NX_EFIS_R_ND_RANGE",
        "XMLVAR_Baro_Selector_HPA_1", "XMLVAR_Baro_Selector_HPA_2",
        "FD_1_CTL", "FD_2_CTL",
        "ELEC_APU_GEN:1", "ELEC_APU_GEN:2",
        // Auto-flap schedule inputs (speed tape + landing config + flaps handle).
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F", "A32NX_SPEEDS_VFEN",
        "A32NX_SPEEDS_LANDING_CONF3", "A32NX_FLAPS_HANDLE_INDEX",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        if (field == "FO_ENGINES_OFF")
        {
            double v1 = GetValue("ENG_VALVE_SWITCH:1"), v2 = GetValue("ENG_VALVE_SWITCH:2"),
                   v3 = GetValue("ENG_VALVE_SWITCH:3"), v4 = GetValue("ENG_VALVE_SWITCH:4");
            // Cold cache = indeterminate, not "engines running": propagate NaN so the
            // ChecklistManager neither ticks nor reverts until the valves have been read.
            if (double.IsNaN(v1) || double.IsNaN(v2) || double.IsNaN(v3) || double.IsNaN(v4))
            {
                value = double.NaN;
                return true;
            }
            value = (v1 < 0.5 && v2 < 0.5 && v3 < 0.5 && v4 < 0.5) ? 1 : 0;
            return true;
        }
        value = double.NaN;
        return false;
    }
}
