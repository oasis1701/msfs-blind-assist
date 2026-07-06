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
        "WING_ANTI_ICE_OVHD", "ENG1_ANTI_ICE", "ENG2_ANTI_ICE", "ENG3_ANTI_ICE", "ENG4_ANTI_ICE",
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
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        if (field == "FO_ENGINES_OFF")
        {
            bool allOff =
                GetValue("ENG_VALVE_SWITCH:1") < 0.5 && GetValue("ENG_VALVE_SWITCH:2") < 0.5 &&
                GetValue("ENG_VALVE_SWITCH:3") < 0.5 && GetValue("ENG_VALVE_SWITCH:4") < 0.5;
            value = allOff ? 1 : 0;
            return true;
        }
        value = double.NaN;
        return false;
    }
}
