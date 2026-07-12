using System;
using System.Collections.Generic;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.FBWA320;

/// <summary>
/// Reads FlyByWire A32NX control state from the SimConnect cache for the First
/// Officer's checklist auto-detection. Mirrors <see cref="FBWA380.FbwA380StateEvaluator"/>
/// with the A320 two-engine key set (confirmed against FlyByWireA320Definition.GetVariables()).
/// </summary>
public sealed class FbwA320StateEvaluator : LVarStateEvaluator
{
    private static readonly string[] PollFields =
    {
        // Batteries (A320 has only 2 — no ESS/APU battery like the A380's 4).
        "A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", "A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO",
        // External power (single un-indexed PB on the A320, unlike the A380's 4).
        "A32NX_OVHD_ELEC_EXT_PWR_PB_IS_ON", "EXT_PWR_AVAILABLE",
        "A32NX_OVHD_ADIRS_IR_1_MODE_SELECTOR_KNOB", "A32NX_OVHD_ADIRS_IR_2_MODE_SELECTOR_KNOB",
        "A32NX_OVHD_ADIRS_IR_3_MODE_SELECTOR_KNOB",
        "PUSH_OVHD_OXYGEN_CREW",
        // Lights: NAV+LOGO are one combined switch on the A320 (A32NX_LIGHTS_NAV_LOGO);
        // strobe is LIGHTING_STROBE_0; beacon is the stock "LIGHT BEACON" simvar.
        "A32NX_LIGHTS_NAV_LOGO", "LIGHTING_STROBE_0", "LIGHT BEACON", "WING_LIGHTS_SET",
        "LANDING_LIGHTS_ON_THIRD_PARTY",
        "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION", "XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION",
        // Wing anti-ice reads back the same L:var the PB writes (never _SYSTEM_*, which is a
        // Rust per-frame OUTPUT). Engine anti-ice state = the stock ENG_ANTI_ICE:n readouts.
        "A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", "ENG_ANTI_ICE:1", "ENG_ANTI_ICE:2",
        "A32NX_OVHD_COND_PACK_1_PB_IS_ON", "A32NX_OVHD_COND_PACK_2_PB_IS_ON",
        "A32NX_KNOB_OVHD_AIRCOND_XBLEED_Position", "A32NX_KNOB_OVHD_AIRCOND_PACKFLOW_Position",
        "A32NX_OVHD_COND_HOT_AIR_PB_IS_ON", "A32NX_OVHD_PRESS_MODE_SEL_PB_IS_AUTO",
        // Task 7 additions: checklist Auto items read these state-readback fields, which
        // differ from (or are additional to) the write keys the flow uses.
        "LIGHT WING", "CABIN SEATBELTS ALERT SWITCH",
        "A32NX_SWITCH_TCAS_TRAFFIC_POSITION", "A32NX_SWITCH_TCAS_POSITION", "A32NX_TRANSPONDER_MODE",
        "LIGHT TAXI:2", "LIGHTING_LANDING_1", "LIGHTING_LANDING_2",
        "A32NX_EFIS_L_LS_BUTTON_IS_ON", "A32NX_EFIS_R_LS_BUTTON_IS_ON",
        "A32NX_PARK_BRAKE_LEVER_POS", "A32NX_SPOILERS_ARMED",
        "GEAR_HANDLE_POSITION", "A32NX_FLAPS_HANDLE_INDEX", "ENGINE_MODE_SELECTOR",
        // Engine masters: dict keys (cache is keyed by GetVariables() key, not the underlying
        // SimVar Name "FUELSYSTEM VALVE SWITCH:n").
        "ENGINE_1_MASTER", "ENGINE_2_MASTER", "A32NX_ENGINE_STATE:1", "A32NX_ENGINE_STATE:2",
        "XMLVAR_A320_WeatherRadar_Sys", "XMLVAR_A320_WeatherRadar_Mode", "A32NX_SWITCH_RADAR_PWS_POSITION",
        "FUEL_PUMP_L1", "FUEL_PUMP_L2", "FUEL_PUMP_R1", "FUEL_PUMP_R2", "FUEL_PUMP_C1", "FUEL_PUMP_C2",
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
        "A32NX_OVHD_APU_START_PB_IS_ON", "A32NX_OVHD_APU_START_PB_IS_AVAILABLE",
        "A32NX_COCKPIT_DOOR_LOCKED", "A32NX_AUTOBRAKES_ARMED_MODE",
        "A32NX_EFIS_L_ND_MODE",
        "A32NX_EFIS_L_ND_RANGE",
        "A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE",
        "A32NX_SWITCH_ATC_ALT",
        "A32NX_FMGC_1_FD_ENGAGED", "A32NX_FMGC_2_FD_ENGAGED",
        "A32NX_OVHD_INTLT_ANN", "A32NX_OVHD_INTLT_DOME", "A32NX_STBY_COMPASS_LIGHT_TOGGLE",
        // Auto-flap schedule inputs (speed tape + flaps handle). A32NX_SPEEDS_LANDING_CONF3
        // was removed (Task 12 audit) — confirmed absent from FlyByWireA320Definition.
        "A32NX_SPEEDS_GD", "A32NX_SPEEDS_S", "A32NX_SPEEDS_F", "A32NX_SPEEDS_VFEN",
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        if (field == "FO_ENGINES_OFF")
        {
            double e1 = GetValue("A32NX_ENGINE_STATE:1"), e2 = GetValue("A32NX_ENGINE_STATE:2");
            // Cold cache = indeterminate, not "engines running": propagate NaN so the
            // ChecklistManager neither ticks nor reverts until the states have been read.
            if (double.IsNaN(e1) || double.IsNaN(e2))
            {
                value = double.NaN;
                return true;
            }
            value = (e1 < 0.5 && e2 < 0.5) ? 1 : 0;
            return true;
        }
        value = double.NaN;
        return false;
    }
}
