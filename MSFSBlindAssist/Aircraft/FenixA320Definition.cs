using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition for Fenix A320 CEO.
/// Fenix uses increment/decrement controls for FCU instead of direct value input.
/// </summary>
public class FenixA320Definition : BaseAircraftDefinition
{
    public override string AircraftName => "Fenix A320 CEO";
    public override string AircraftCode => "FENIX_A320CEO";

    // Fenix FCU uses increment/decrement buttons, not direct value input like FlyByWire
    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // ========== ADIRS (13 variables) ==========
            ["I_OH_NAV_IR3_SWITCH_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR3_SWITCH_L",
                DisplayName = "ADIRS IR 3 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_IR3_SWITCH_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR3_SWITCH_U",
                DisplayName = "ADIRS IR 3 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_IR2_SWITCH_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR2_SWITCH_L",
                DisplayName = "ADIRS IR 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_IR2_SWITCH_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR2_SWITCH_U",
                DisplayName = "ADIRS IR 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_IR1_SWITCH_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR1_SWITCH_L",
                DisplayName = "ADIRS IR 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_IR1_SWITCH_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_IR1_SWITCH_U",
                DisplayName = "ADIRS IR 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR1_L",
                DisplayName = "OH ADIRS ADR1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR1_U",
                DisplayName = "OH ADIRS ADR1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR2_L",
                DisplayName = "OH ADIRS ADR2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR2_U",
                DisplayName = "OH ADIRS ADR2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR3_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR3_L",
                DisplayName = "OH ADIRS ADR3 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR3_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR3_U",
                DisplayName = "OH ADIRS ADR3 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADIRS_ON_BAT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADIRS_ON_BAT",
                DisplayName = "OH ADIRS ON BAT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== AIR CONDITIONING AND PRESSURIZATION (22 variables) ==========
            ["I_OH_PNEUMATIC_EXTRACT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_EXTRACT_U",
                DisplayName = "Ventilation Extract Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_EXTRACT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_EXTRACT_L",
                DisplayName = "Ventilation Extract Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_CAB_FANS_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_CAB_FANS_U",
                DisplayName = "Ventilation Cabin Fans Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_CAB_FANS_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_CAB_FANS_L",
                DisplayName = "Ventilation Cabin Fans Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_BLOWER_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_BLOWER_U",
                DisplayName = "Ventilation Blower Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_BLOWER_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_BLOWER_L",
                DisplayName = "Ventilation Blower Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PRESS_MODE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PRESS_MODE_U",
                DisplayName = "Pressurization Mode Select Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PRESS_MODE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PRESS_MODE_L",
                DisplayName = "Pressurization Mode Select Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_DITCHING_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_DITCHING_L",
                DisplayName = "Pressurization Ditching",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_RAM_AIR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_RAM_AIR_L",
                DisplayName = "Pneumatic Ram Air",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PACK_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PACK_2_U",
                DisplayName = "Pneumatic Pack 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PACK_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PACK_2_L",
                DisplayName = "Pneumatic Pack 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PACK_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PACK_1_U",
                DisplayName = "Pneumatic Pack 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_PACK_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_PACK_1_L",
                DisplayName = "Pneumatic Pack 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_HOT_AIR_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_HOT_AIR_U",
                DisplayName = "Pneumatic Hot Air Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_HOT_AIR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_HOT_AIR_L",
                DisplayName = "Pneumatic Hot Air Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG2_BLEED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG2_BLEED_U",
                DisplayName = "Pneumatic Engine Bleed 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG2_BLEED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG2_BLEED_L",
                DisplayName = "Pneumatic Engine Bleed 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG1_BLEED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG1_BLEED_U",
                DisplayName = "Pneumatic Engine Bleed 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG1_BLEED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG1_BLEED_L",
                DisplayName = "Pneumatic Engine Bleed 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_APU_BLEED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_APU_BLEED_U",
                DisplayName = "Pneumatic APU Bleed Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_APU_BLEED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_APU_BLEED_L",
                DisplayName = "Pneumatic APU Bleed Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== ANTI-ICE (8 variables) ==========
            ["I_OH_PNEUMATIC_WING_ANTI_ICE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_WING_ANTI_ICE_U",
                DisplayName = "Icing Wing Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_WING_ANTI_ICE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_WING_ANTI_ICE_L",
                DisplayName = "Icing Wing Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PROBE_HEAT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PROBE_HEAT_U",
                DisplayName = "Icing Probe Heat Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PROBE_HEAT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PROBE_HEAT_L",
                DisplayName = "Icing Probe Heat Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG2_ANTI_ICE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG2_ANTI_ICE_U",
                DisplayName = "Icing Engine 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG2_ANTI_ICE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG2_ANTI_ICE_L",
                DisplayName = "Icing Engine 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG1_ANTI_ICE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG1_ANTI_ICE_U",
                DisplayName = "Icing Engine 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_ENG1_ANTI_ICE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_ENG1_ANTI_ICE_L",
                DisplayName = "Icing Engine 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== AUTOPILOT (21 variables) ==========
            ["I_FCU_EXPED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EXPED",
                DisplayName = "FCU EXPED Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_ATHR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_ATHR",
                DisplayName = "FCU ATHR Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_APPR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_APPR",
                DisplayName = "FCU APPR Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_AP2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_AP2",
                DisplayName = "FCU AP2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_AP1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_AP1",
                DisplayName = "FCU AP1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_LOC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_LOC",
                DisplayName = "FCU LOC Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_SPEED_MANAGED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_SPEED_MANAGED",
                DisplayName = "FCU SPEED Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Selected", [1] = "Managed"}
            },
            ["I_FCU_HEADING_MANAGED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_HEADING_MANAGED",
                DisplayName = "FCU HEADING Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Selected", [1] = "Managed"}
            },
            ["I_FCU_ALTITUDE_MANAGED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_ALTITUDE_MANAGED",
                DisplayName = "FCU ALTITUDE Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Selected", [1] = "Managed"}
            },
            ["I_FCU_TRACK_FPA_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_TRACK_FPA_MODE",
                DisplayName = "FCU HDG TRK MODE SELECT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "HDG/VS", [1] = "TRK/FPA"}
            },
            ["I_FCU_MACH_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_MACH_MODE",
                DisplayName = "FCU SPD MACH MODE SELECT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Speed", [1] = "Mach"}
            },
            ["N_FCU_SPEED"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_SPEED",
                DisplayName = "FCU SPEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_HEADING"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_HEADING",
                DisplayName = "FCU HEADING",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_ALTITUDE"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_ALTITUDE",
                DisplayName = "FCU ALTITUDE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_VS"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_VS",
                DisplayName = "FCU VERTICAL SPEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_FCU_SPEED_DASHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_SPEED_DASHED",
                DisplayName = "FCU SPEED Dashed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["B_FCU_HEADING_DASHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_HEADING_DASHED",
                DisplayName = "FCU HEADING Dashed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["B_FCU_VERTICALSPEED_DASHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_VERTICALSPEED_DASHED",
                DisplayName = "FCU VERTICAL SPEED Dashed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["B_FCU_POWER"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_POWER",
                DisplayName = "FCU POWER",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_FCU_SPEED_MACH"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_SPEED_MACH",
                DisplayName = "FCU SPEED MACH",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Speed", [1] = "Mach"}
            },
            ["B_FCU_TRACK_FPA_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FCU_TRACK_FPA_MODE",
                DisplayName = "FCU TRACK FPA MODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "HDG/VS", [1] = "TRK/FPA"}
            },

            // ========== AVIONICS (9 variables) ==========
            ["I_XPDR_FAIL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_XPDR_FAIL",
                DisplayName = "Transponder ATC FAIL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_TERRAIN_ON_ND_FO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_TERRAIN_ON_ND_FO_L",
                DisplayName = "MainPanel Terrain On ND FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_TERRAIN_ON_ND_CAPT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_TERRAIN_ON_ND_CAPT_L",
                DisplayName = "MainPanel Terrain On ND Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_ATC_MSG_FO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_ATC_MSG_FO_L",
                DisplayName = "Glareshield MSG FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_ATC_MSG_FO_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_ATC_MSG_FO_U",
                DisplayName = "Glareshield ATC FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_ATC_MSG_CAPT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_ATC_MSG_CAPT_L",
                DisplayName = "Glareshield MSG Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_ATC_MSG_CAPT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_ATC_MSG_CAPT_U",
                DisplayName = "Glareshield ATC Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOLAND_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOLAND_FO",
                DisplayName = "Glareshield Autoland FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOLAND_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOLAND_CAPT",
                DisplayName = "Glareshield Autoland Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== CONTROLS (21 variables) ==========
            ["I_FC_SIDESTICK_PRIORITY_FO_ARROW"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FC_SIDESTICK_PRIORITY_FO_ARROW",
                DisplayName = "Glareshield Sidestick Priority FO Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FC_SIDESTICK_PRIORITY_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FC_SIDESTICK_PRIORITY_FO",
                DisplayName = "Glareshield Sidestick Priority FO Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FC_SIDESTICK_PRIORITY_CAPT_ARROW"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FC_SIDESTICK_PRIORITY_CAPT_ARROW",
                DisplayName = "Glareshield Sidestick Priority Captain Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FC_SIDESTICK_PRIORITY_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FC_SIDESTICK_PRIORITY_CAPT",
                DisplayName = "Glareshield Sidestick Priority Captain Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_3_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_3_U",
                DisplayName = "FlightControl SEC 3 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_3_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_3_L",
                DisplayName = "FlightControl SEC 3 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_2_U",
                DisplayName = "FlightControl SEC 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_2_L",
                DisplayName = "FlightControl SEC 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_1_U",
                DisplayName = "FlightControl SEC 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_SEC_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_SEC_1_L",
                DisplayName = "FlightControl SEC 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_FAC_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_FAC_2_U",
                DisplayName = "FlightControl FAC 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_FAC_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_FAC_2_L",
                DisplayName = "FlightControl FAC 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_FAC_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_FAC_1_U",
                DisplayName = "FlightControl FAC 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_FAC_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_FAC_1_L",
                DisplayName = "FlightControl FAC 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_ELAC_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_ELAC_2_U",
                DisplayName = "FlightControl ELAC 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_ELAC_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_ELAC_2_L",
                DisplayName = "FlightControl ELAC 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_ELAC_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_ELAC_1_U",
                DisplayName = "FlightControl ELAC 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FLT_CTL_ELAC_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FLT_CTL_ELAC_1_L",
                DisplayName = "FlightControl ELAC 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["N_FC_RUDDER_TRIM_DECIMAL"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FC_RUDDER_TRIM_DECIMAL",
                DisplayName = "RUDDER TRIM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "number",
            },
            ["B_FC_RUDDER_TRIM_DASHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_FC_RUDDER_TRIM_DASHED",
                DisplayName = "RUDDER TRIM Dashed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            // ========== ECAM (16 variables) ==========
            ["I_ECAM_WHEEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_WHEEL",
                DisplayName = "ECAM WHEEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_STATUS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_STATUS",
                DisplayName = "ECAM STS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_CAB_PRESS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_CAB_PRESS",
                DisplayName = "ECAM PRESS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_HYD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_HYD",
                DisplayName = "ECAM HYD",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_FUEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_FUEL",
                DisplayName = "ECAM FUEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_FCTL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_FCTL",
                DisplayName = "ECAM FCTL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_ENGINE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_ENGINE",
                DisplayName = "ECAM ENG",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_ELEC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_ELEC",
                DisplayName = "ECAM ELEC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_DOOR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_DOOR",
                DisplayName = "ECAM DOOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_COND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_COND",
                DisplayName = "ECAM COND",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_CLR_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_CLR_RIGHT",
                DisplayName = "ECAM CLR Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_CLR_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_CLR_LEFT",
                DisplayName = "ECAM CLR Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_BLEED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_BLEED",
                DisplayName = "ECAM BLEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_APU"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_APU",
                DisplayName = "ECAM APU",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ECAM_EMER_CANCEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_EMER_CANCEL",
                DisplayName = "ECAM EMER CANC Button",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== EFB (4 variables) ==========
            ["S_EFB_VISIBLE_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_EFB_VISIBLE_CAPT",
                DisplayName = "Captain EFB Visibility",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["S_EFB_VISIBLE_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_EFB_VISIBLE_FO",
                DisplayName = "FO EFB Visibility",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["S_EFB_CHARGING_CABLE_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_EFB_CHARGING_CABLE_FO",
                DisplayName = "FO EFB Cable Visibility",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["S_EFB_CHARGING_CABLE_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_EFB_CHARGING_CABLE_CAPT",
                DisplayName = "Captain EFB Cable Visibility",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== EFIS (28 variables) ==========
            ["I_FCU_EFIS2_WPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_WPT",
                DisplayName = "EFIS 2 WPT Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_VORD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_VORD",
                DisplayName = "EFIS 2 VORD Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_NDB"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_NDB",
                DisplayName = "EFIS 2 NDB Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_LS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_LS",
                DisplayName = "EFIS 2 LS Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_FD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_FD",
                DisplayName = "EFIS 2 FD Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_CSTR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_CSTR",
                DisplayName = "EFIS 2 CSTR Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS2_ARPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_ARPT",
                DisplayName = "EFIS 2 ARPT Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_WPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_WPT",
                DisplayName = "EFIS 1 WPT Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_VORD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_VORD",
                DisplayName = "EFIS 1 VORD Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_NDB"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_NDB",
                DisplayName = "EFIS 1 NDB Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_LS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_LS",
                DisplayName = "EFIS 1 LS Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_FD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_FD",
                DisplayName = "EFIS 1 FD Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_CSTR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_CSTR",
                DisplayName = "EFIS 1 CSTR Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_FCU_EFIS1_ARPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_ARPT",
                DisplayName = "EFIS 1 ARPT Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_FCU_EFIS1_BARO_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FCU_EFIS1_BARO_MODE",
                DisplayName = "EFIS 1 BARO MODE STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "QNH", [1] = "STD"}
            },
            ["S_FCU_EFIS2_BARO_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FCU_EFIS2_BARO_MODE",
                DisplayName = "EFIS 2 BARO MODE STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "QNH", [1] = "STD"}
            },
            ["I_FCU_EFIS1_QNH"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS1_QNH",
                DisplayName = "EFIS1 BARO STD Status",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "QNH", [1] = "STD"}
            },
            ["I_FCU_EFIS2_QNH"] = new SimConnect.SimVarDefinition
            {
                Name = "I_FCU_EFIS2_QNH",
                DisplayName = "EFIS2 BARO STD Status",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "QNH", [1] = "STD"}
            },
            ["N_FCU_EFIS1_BARO_INCH"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_EFIS1_BARO_INCH",
                DisplayName = "EFIS1 BARO INHG Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_EFIS1_BARO_HPA"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_EFIS1_BARO_HPA",
                DisplayName = "EFIS1 BARO HPA Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_EFIS2_BARO_HPA"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_EFIS2_BARO_HPA",
                DisplayName = "EFIS2 BARO HPA Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FCU_EFIS2_BARO_INCH"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FCU_EFIS2_BARO_INCH",
                DisplayName = "EFIS2 BARO INHG Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            // Note: KOHLSMAN SimVars removed - Fenix uses N_FCU_EFIS1/2_BARO_HPA/INCH instead

            // ========== ELECTRICAL (48 variables) ==========
            ["I_OH_ELEC_IDG2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_IDG2_U",
                DisplayName = "Electrical IDG 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_IDG1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_IDG1_U",
                DisplayName = "Electrical IDG 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN2_U",
                DisplayName = "Electrical Generator 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN2_L",
                DisplayName = "Electrical Generator 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN1_U",
                DisplayName = "Electrical Generator 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN1_L",
                DisplayName = "Electrical Generator 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GALY_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GALY_U",
                DisplayName = "Electrical Galley Cabin Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GALY_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GALY_L",
                DisplayName = "Electrical Galley Cabin Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_EXT_PWR_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_EXT_PWR_U",
                DisplayName = "Electrical External Power Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_EXT_PWR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_EXT_PWR_L",
                DisplayName = "Electrical External Power Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_COMMERCIAL_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_COMMERCIAL_U",
                DisplayName = "Electrical Commercial Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_COMMERCIAL_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_COMMERCIAL_L",
                DisplayName = "Electrical Commercial Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_BUSTIE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_BUSTIE_L",
                DisplayName = "Electrical Bus Tie",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_BAT2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_BAT2_U",
                DisplayName = "Electrical Battery 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_BAT2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_BAT2_L",
                DisplayName = "Electrical Battery 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_BAT1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_BAT1_U",
                DisplayName = "Electrical Battery 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_BAT1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_BAT1_L",
                DisplayName = "Electrical Battery 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_GENERATOR_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_GENERATOR_U",
                DisplayName = "Electrical APU Generator Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_GENERATOR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_GENERATOR_L",
                DisplayName = "Electrical APU Generator Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_AC_ESS_FEED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_AC_ESS_FEED_U",
                DisplayName = "Electrical AC Essential Feed Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_AC_ESS_FEED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_AC_ESS_FEED_L",
                DisplayName = "Electrical AC Essential Feed Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Switch Position Variables (combo boxes - read/write state)
            ["S_OH_ELEC_BAT1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_BAT1",
                DisplayName = "Battery 1 Switch",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_BAT2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_BAT2",
                DisplayName = "Battery 2 Switch",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Button Controls (write-only - execute RPN operations)
            ["S_OH_ELEC_GEN1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_GEN1_LINE",  // Note: Uses GEN1_LINE internally
                DisplayName = "Generator 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_GEN2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_GEN2",
                DisplayName = "Generator 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_EXT_PWR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_EXT_PWR",
                DisplayName = "External Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ELEC_APU_GEN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_APU_GENERATOR",
                DisplayName = "APU Generator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_BUS_TIE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_BUSTIE",
                DisplayName = "Bus Tie",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_AC_ESS_FEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_AC_ESS_FEED",
                DisplayName = "AC ESS Feed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_IDG1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_IDG1",
                DisplayName = "IDG 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_IDG2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_IDG2",
                DisplayName = "IDG 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_GALY"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_GALY",
                DisplayName = "Galley",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_COMMERCIAL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_COMMERCIAL",
                DisplayName = "Commercial",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_APU_MASTER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_APU_MASTER",
                DisplayName = "APU Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_APU_START"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_APU_START",
                DisplayName = "APU Start",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_GEN1_LINE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_GEN1_LINE",
                DisplayName = "Emergency Gen 1 Line",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_EMER_GEN_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_EMER_GEN_TEST",
                DisplayName = "Emergency Gen Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_EMER_GEN_MAN_ON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_EMER_GEN_MAN_ON",
                DisplayName = "Emergency Gen Manual On",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELEC_EMER_GEN_MAN_ON_Cover"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_EMER_GEN_MAN_ON_Cover",
                DisplayName = "Emergency Gen Manual On Cover",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Closed", [1] = "Open"}
            },
            ["S_OH_ELEC_EMER_GEN_TEST_Cover"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELEC_EMER_GEN_TEST_Cover",
                DisplayName = "Emergency Gen Test Cover",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Closed", [1] = "Open"}
            },

            // ========== ADIRS (11 variables) ==========
            // Note: Numeric keypad (0-9, CLR, ENT) will be added later

            // IR Mode Knobs
            ["S_OH_NAV_IR1_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR1_MODE",
                DisplayName = "IR 1 Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Nav", [2] = "Att"}
            },
            ["S_OH_NAV_IR2_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR2_MODE",
                DisplayName = "IR 2 Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Nav", [2] = "Att"}
            },
            ["S_OH_NAV_IR3_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR3_MODE",
                DisplayName = "IR 3 Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Nav", [2] = "Att"}
            },

            // ADR Buttons
            ["S_OH_NAV_ADR1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_ADR1",
                DisplayName = "ADR 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_NAV_ADR2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_ADR2",
                DisplayName = "ADR 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_NAV_ADR3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_ADR3",
                DisplayName = "ADR 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // IR Push Buttons
            ["S_OH_NAV_IR1_SWITCH"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR1_SWITCH",
                DisplayName = "IR 1 Push",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_NAV_IR2_SWITCH"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR2_SWITCH",
                DisplayName = "IR 2 Push",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_NAV_IR3_SWITCH"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_IR3_SWITCH",
                DisplayName = "IR 3 Push",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Display Selectors
            ["S_OH_NAV_DATA_DISP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_DATA_DISP",
                DisplayName = "Data Display",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Wind", [1] = "PPOS", [2] = "HDG", [3] = "STS", [4] = "TK/GS", [5] = "TEST"}
            },
            ["S_OH_NAV_SYS_DISP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_NAV_SYS_DISP",
                DisplayName = "System Display",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "IR 1", [1] = "IR 2", [2] = "IR 3", [3] = "ADR 1", [4] = "ADR 2", [5] = "ADR 3"}
            },

            // Keypad Buttons
            ["S_OH_ADIRS_KEY_0"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_0",
                DisplayName = "Key 0",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_1",
                DisplayName = "Key 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_2",
                DisplayName = "Key 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_3",
                DisplayName = "Key 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_4"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_4",
                DisplayName = "Key 4",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_5"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_5",
                DisplayName = "Key 5",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_6"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_6",
                DisplayName = "Key 6",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_7"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_7",
                DisplayName = "Key 7",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_8"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_8",
                DisplayName = "Key 8",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_9"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_9",
                DisplayName = "Key 9",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_CLR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_CLR",
                DisplayName = "Key Clear",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_OH_ADIRS_KEY_ENT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ADIRS_KEY_ENT",
                DisplayName = "Key Enter",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // ========== RADIO MANAGEMENT PANEL (RMP) (42 variables) ==========

            // RMP1 Power Switch
            ["S_PED_RMP1_POWER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_POWER",
                DisplayName = "RMP1 Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // RMP1 Mode Selection Buttons (Momentary)
            ["S_PED_RMP1_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_VHF1",
                DisplayName = "RMP1 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_VHF2",
                DisplayName = "RMP1 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_VHF3",
                DisplayName = "RMP1 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_HF1",
                DisplayName = "RMP1 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_HF2",
                DisplayName = "RMP1 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_NAV",
                DisplayName = "RMP1 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_VOR",
                DisplayName = "RMP1 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_ILS",
                DisplayName = "RMP1 ILS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_MLS",
                DisplayName = "RMP1 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_ADF",
                DisplayName = "RMP1 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_BFO",
                DisplayName = "RMP1 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_AM",
                DisplayName = "RMP1 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // RMP2 Power Switch
            ["S_PED_RMP2_POWER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_POWER",
                DisplayName = "RMP2 Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // RMP2 Mode Selection Buttons (Momentary)
            ["S_PED_RMP2_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_VHF1",
                DisplayName = "RMP2 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_VHF2",
                DisplayName = "RMP2 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_VHF3",
                DisplayName = "RMP2 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_HF1",
                DisplayName = "RMP2 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_HF2",
                DisplayName = "RMP2 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_NAV",
                DisplayName = "RMP2 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_VOR",
                DisplayName = "RMP2 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_ILS",
                DisplayName = "RMP2 ILS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_MLS",
                DisplayName = "RMP2 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_ADF",
                DisplayName = "RMP2 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_BFO",
                DisplayName = "RMP2 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_AM",
                DisplayName = "RMP2 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // RMP3 Power Switch
            ["S_PED_RMP3_POWER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_POWER",
                DisplayName = "RMP3 Power",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // RMP3 Mode Selection Buttons (Momentary)
            ["S_PED_RMP3_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_VHF1",
                DisplayName = "RMP3 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_VHF2",
                DisplayName = "RMP3 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_VHF3",
                DisplayName = "RMP3 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_HF1",
                DisplayName = "RMP3 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_HF2",
                DisplayName = "RMP3 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_NAV",
                DisplayName = "RMP3 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_VOR",
                DisplayName = "RMP3 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_ILS",
                DisplayName = "RMP3 ILS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_MLS",
                DisplayName = "RMP3 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_ADF",
                DisplayName = "RMP3 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_BFO",
                DisplayName = "RMP3 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_AM",
                DisplayName = "RMP3 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // RMP1 Frequency Buttons and Transfer
            ["E_PED_RMP1_INNER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP1_INNER_INC",
                DisplayName = "RMP1 Inner Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP1_INNER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP1_INNER_DEC",
                DisplayName = "RMP1 Inner Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP1_OUTER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP1_OUTER_INC",
                DisplayName = "RMP1 Outer Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP1_OUTER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP1_OUTER_DEC",
                DisplayName = "RMP1 Outer Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP1_XFER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP1_XFER",
                DisplayName = "RMP1 Transfer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // RMP2 Frequency Buttons and Transfer
            ["E_PED_RMP2_INNER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP2_INNER_INC",
                DisplayName = "RMP2 Inner Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP2_INNER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP2_INNER_DEC",
                DisplayName = "RMP2 Inner Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP2_OUTER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP2_OUTER_INC",
                DisplayName = "RMP2 Outer Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP2_OUTER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP2_OUTER_DEC",
                DisplayName = "RMP2 Outer Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP2_XFER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP2_XFER",
                DisplayName = "RMP2 Transfer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // RMP3 Frequency Buttons and Transfer
            ["E_PED_RMP3_INNER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP3_INNER_INC",
                DisplayName = "RMP3 Inner Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP3_INNER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP3_INNER_DEC",
                DisplayName = "RMP3 Inner Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP3_OUTER_INC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP3_OUTER_INC",
                DisplayName = "RMP3 Outer Inc",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["E_PED_RMP3_OUTER_DEC"] = new SimConnect.SimVarDefinition
            {
                Name = "E_PED_RMP3_OUTER_DEC",
                DisplayName = "RMP3 Outer Dec",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_PED_RMP3_XFER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_RMP3_XFER",
                DisplayName = "RMP3 Transfer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // ========== AUDIO CONTROL PANEL (ACP) (14 variables) ==========

            // Volume Controls (13 knobs)
            ["A_ASP_VHF_1_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_VHF_1_VOLUME",
                DisplayName = "ACP VHF 1 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_VHF_2_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_VHF_2_VOLUME",
                DisplayName = "ACP VHF 2 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_VHF_3_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_VHF_3_VOLUME",
                DisplayName = "ACP VHF 3 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_HF_1_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_HF_1_VOLUME",
                DisplayName = "ACP HF 1 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_HF_2_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_HF_2_VOLUME",
                DisplayName = "ACP HF 2 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_CAB_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_CAB_VOLUME",
                DisplayName = "ACP CAB Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_PA_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_PA_VOLUME",
                DisplayName = "ACP PA Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_INT_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_INT_VOLUME",
                DisplayName = "ACP INT Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_ILS_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_ILS_VOLUME",
                DisplayName = "ACP ILS Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_MLS_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_MLS_VOLUME",
                DisplayName = "ACP MLS Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_ADF_1_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_ADF_1_VOLUME",
                DisplayName = "ACP ADF 1 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_ADF_2_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_ADF_2_VOLUME",
                DisplayName = "ACP ADF 2 Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },
            ["A_ASP_MARKER_VOLUME"] = new SimConnect.SimVarDefinition
            {
                Name = "A_ASP_MARKER_VOLUME",
                DisplayName = "ACP MARKER Volume",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%",
                    [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%",
                    [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%",
                    [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%",
                    [1.00] = "100%"
                }
            },

            // INTRAD Switch
            ["S_ASP_INTRAD"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ASP_INTRAD",
                DisplayName = "ACP INTRAD Switch",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "INT", [1] = "OFF", [2] = "RAD"}
            },

            // ========== AIR CONDITIONING AND PRESSURIZATION (18 variables) ==========

            // Bleed Buttons
            ["S_OH_PNEUMATIC_APU_BLEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_APU_BLEED",
                DisplayName = "APU Bleed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_ENG1_BLEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_ENG1_BLEED",
                DisplayName = "Engine 1 Bleed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_ENG2_BLEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_ENG2_BLEED",
                DisplayName = "Engine 2 Bleed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Pack Buttons
            ["S_OH_PNEUMATIC_PACK_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_PACK_1",
                DisplayName = "Pack 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_PACK_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_PACK_2",
                DisplayName = "Pack 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Air Buttons
            ["S_OH_PNEUMATIC_HOT_AIR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_HOT_AIR",
                DisplayName = "Hot Air",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_RAM_AIR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_RAM_AIR",
                DisplayName = "Ram Air",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Pressurization
            ["S_OH_PNEUMATIC_DITCHING"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_DITCHING",
                DisplayName = "Ditching",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_PRESS_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_PRESS_MODE",
                DisplayName = "Pressurization Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Select", [1] = "Auto"}
            },
            ["A_OH_PNEUMATIC_LDG_ELEV"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_PNEUMATIC_LDG_ELEV",
                DisplayName = "Landing Elevation",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[-3.00] = "-3.00", [-2.75] = "-2.75", [-2.50] = "-2.50", [-2.25] = "-2.25", [-2.00] = "-2.00", [-1.75] = "-1.75", [-1.50] = "-1.50", [-1.25] = "-1.25", [-1.00] = "-1.00", [-0.75] = "-0.75", [-0.50] = "-0.50", [-0.25] = "-0.25", [0.00] = "0.00", [0.25] = "0.25", [0.50] = "0.50", [0.75] = "0.75", [1.00] = "1.00", [1.25] = "1.25", [1.50] = "1.50", [1.75] = "1.75", [2.00] = "2.00", [2.25] = "2.25", [2.50] = "2.50", [2.75] = "2.75", [3.00] = "3.00", [3.25] = "3.25", [3.50] = "3.50", [3.75] = "3.75", [4.00] = "4.00", [4.25] = "4.25", [4.50] = "4.50", [4.75] = "4.75", [5.00] = "5.00", [5.25] = "5.25", [5.50] = "5.50", [5.75] = "5.75", [6.00] = "6.00", [6.25] = "6.25", [6.50] = "6.50", [6.75] = "6.75", [7.00] = "7.00", [7.25] = "7.25", [7.50] = "7.50", [7.75] = "7.75", [8.00] = "8.00", [8.25] = "8.25", [8.50] = "8.50", [8.75] = "8.75", [9.00] = "9.00", [9.25] = "9.25", [9.50] = "9.50", [9.75] = "9.75", [10.00] = "10.00", [10.25] = "10.25", [10.50] = "10.50", [10.75] = "10.75", [11.00] = "11.00", [11.25] = "11.25", [11.50] = "11.50", [11.75] = "11.75", [12.00] = "12.00", [12.25] = "12.25", [12.50] = "12.50", [12.75] = "12.75", [13.00] = "13.00", [13.25] = "13.25", [13.50] = "13.50", [13.75] = "13.75", [14.00] = "14.00"}
            },
            ["S_OH_PNEUMATIC_PRESS_MAN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_PRESS_MAN",
                DisplayName = "Manual Vertical Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Up", [1] = "Mid", [2] = "Down"}
            },

            // Ventilation Buttons
            ["S_OH_PNEUMATIC_BLOWER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_BLOWER",
                DisplayName = "Blower",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_EXTRACT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_EXTRACT",
                DisplayName = "Extract",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_CAB_FANS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_CAB_FANS",
                DisplayName = "Cabin Fans",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Selectors
            ["S_OH_PNEUMATIC_XBLEED_SELECTOR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_XBLEED_SELECTOR",
                DisplayName = "Cross Bleed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Shut", [1] = "Auto", [2] = "Open"}
            },
            ["S_OH_PNEUMATIC_PACK_FLOW"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_PACK_FLOW",
                DisplayName = "Pack Flow",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Low", [1] = "Normal", [2] = "High"}
            },

            // Temperature Controls
            ["A_OH_PNEUMATIC_COCKPIT_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_PNEUMATIC_COCKPIT_TEMP",
                DisplayName = "Cockpit Temperature",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.0", [0.1] = "0.1", [0.2] = "0.2", [0.3] = "0.3", [0.4] = "0.4", [0.5] = "0.5", [0.6] = "0.6", [0.7] = "0.7", [0.8] = "0.8", [0.9] = "0.9", [1.0] = "1.0"}
            },
            ["A_OH_PNEUMATIC_FWD_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_PNEUMATIC_FWD_TEMP",
                DisplayName = "Forward Cabin Temperature",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.0", [0.1] = "0.1", [0.2] = "0.2", [0.3] = "0.3", [0.4] = "0.4", [0.5] = "0.5", [0.6] = "0.6", [0.7] = "0.7", [0.8] = "0.8", [0.9] = "0.9", [1.0] = "1.0"}
            },
            ["A_OH_PNEUMATIC_AFT_TEMP"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_PNEUMATIC_AFT_TEMP",
                DisplayName = "Aft Cabin Temperature",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.0", [0.1] = "0.1", [0.2] = "0.2", [0.3] = "0.3", [0.4] = "0.4", [0.5] = "0.5", [0.6] = "0.6", [0.7] = "0.7", [0.8] = "0.8", [0.9] = "0.9", [1.0] = "1.0"}
            },

            // Cargo Controls
            ["S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO",
                DisplayName = "Aft Cargo Hot Air",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE",
                DisplayName = "Cargo Aft Isolation Valve",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== FIRE PANEL ==========
            // Main Fire Push Buttons
            ["S_OH_FIRE_ENG1_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG1_BUTTON",
                DisplayName = "Engine 1 Fire Push Button",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Pressed"}
            },
            ["S_OH_FIRE_ENG2_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG2_BUTTON",
                DisplayName = "Engine 2 Fire Push Button",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Pressed"}
            },
            ["S_OH_FIRE_APU_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_APU_BUTTON",
                DisplayName = "APU Fire Push Button",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Pressed"}
            },

            // Fire Test Buttons
            ["S_OH_FIRE_ENG1_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG1_TEST",
                DisplayName = "Engine 1 Fire Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Test"}
            },
            ["S_OH_FIRE_ENG2_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG2_TEST",
                DisplayName = "Engine 2 Fire Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Test"}
            },
            ["S_OH_FIRE_APU_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_APU_TEST",
                DisplayName = "APU Fire Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Test"}
            },

            // Agent Discharge Buttons
            ["S_OH_FIRE_ENG1_AGENT1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG1_AGENT1",
                DisplayName = "Engine 1 Agent 1 Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Discharge"}
            },
            ["S_OH_FIRE_ENG1_AGENT2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG1_AGENT2",
                DisplayName = "Engine 1 Agent 2 Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Discharge"}
            },
            ["S_OH_FIRE_ENG2_AGENT1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG2_AGENT1",
                DisplayName = "Engine 2 Agent 1 Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Discharge"}
            },
            ["S_OH_FIRE_ENG2_AGENT2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_ENG2_AGENT2",
                DisplayName = "Engine 2 Agent 2 Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Discharge"}
            },
            ["S_OH_FIRE_APU_AGENT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FIRE_APU_AGENT",
                DisplayName = "APU Agent Discharge",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Normal", [1] = "Discharge"}
            },

            // ========== HYDRAULIC PANEL ==========
            // Engine Pumps
            ["S_OH_HYD_ENG_1_PUMP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_ENG_1_PUMP",
                DisplayName = "Engine 1 Pump (Green)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_ENG_2_PUMP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_ENG_2_PUMP",
                DisplayName = "Engine 2 Pump (Yellow)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Electric Pumps
            ["S_OH_HYD_BLUE_ELEC_PUMP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_BLUE_ELEC_PUMP",
                DisplayName = "Blue Electric Pump",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_YELLOW_ELEC_PUMP"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_YELLOW_ELEC_PUMP",
                DisplayName = "Yellow Electric Pump",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // PTU and RAT
            ["S_OH_HYD_PTU"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_PTU",
                DisplayName = "Power Transfer Unit (PTU)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_RAT_MAN_ON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_RAT_MAN_ON",
                DisplayName = "RAT Manual On",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Low Mechanical Valves
            ["S_OH_HYD_LMV_YELLOW"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_LMV_YELLOW",
                DisplayName = "LMV Yellow",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_LMV_GREEN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_LMV_GREEN",
                DisplayName = "LMV Green",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_LMV_BLUE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_LMV_BLUE",
                DisplayName = "LMV Blue",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_HYD_BLUE_PUMP_OVERRIDE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_HYD_BLUE_PUMP_OVERRIDE",
                DisplayName = "Blue Pump Override",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== FUEL PANEL ==========
            // Left Wing Tank Pumps
            ["S_OH_FUEL_LEFT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_LEFT_1",
                DisplayName = "Left Tank Pump 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_FUEL_LEFT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_LEFT_2",
                DisplayName = "Left Tank Pump 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Center Tank Pumps
            ["S_OH_FUEL_CENTER_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_CENTER_1",
                DisplayName = "Center Tank Pump 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_FUEL_CENTER_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_CENTER_2",
                DisplayName = "Center Tank Pump 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Right Wing Tank Pumps
            ["S_OH_FUEL_RIGHT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_RIGHT_1",
                DisplayName = "Right Tank Pump 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_FUEL_RIGHT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_RIGHT_2",
                DisplayName = "Right Tank Pump 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Crossfeed and Mode
            ["S_OH_FUEL_XFEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_XFEED",
                DisplayName = "Crossfeed Valve",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Closed", [1] = "Open"}
            },
            ["S_OH_FUEL_MODE_SEL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FUEL_MODE_SEL",
                DisplayName = "Mode Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== ANTI-ICE PANEL ==========
            // Engine Anti-Ice
            ["S_OH_PNEUMATIC_ENG1_ANTI_ICE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_ENG1_ANTI_ICE",
                DisplayName = "Engine 1 Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_PNEUMATIC_ENG2_ANTI_ICE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_ENG2_ANTI_ICE",
                DisplayName = "Engine 2 Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Wing Anti-Ice
            ["S_OH_PNEUMATIC_WING_ANTI_ICE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PNEUMATIC_WING_ANTI_ICE",
                DisplayName = "Wing Anti-Ice",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Probe Heat
            ["S_OH_PROBE_HEAT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_PROBE_HEAT",
                DisplayName = "Probe Heat",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== MAIN INSTRUMENT PANEL ==========
            // Auto Brakes - 3 momentary push buttons
            ["S_MIP_AUTOBRAKE_LO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_AUTOBRAKE_LO",
                DisplayName = "Autobrake Low",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_AUTOBRAKE_MED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_AUTOBRAKE_MED",
                DisplayName = "Autobrake Medium",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_AUTOBRAKE_MAX"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_AUTOBRAKE_MAX",
                DisplayName = "Autobrake Max",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Landing Gear - Lever control
            ["S_MIP_GEAR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_GEAR",
                DisplayName = "Landing Gear",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Up", [1] = "Down"}
            },

            // Brake Fan
            ["S_MIP_BRAKE_FAN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_BRAKE_FAN",
                DisplayName = "Brake Fan",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Anti-Skid
            ["S_FC_MIP_ANTI_SKID"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_MIP_ANTI_SKID",
                DisplayName = "Anti-Skid",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Console Floor Lights
            ["S_MIP_LIGHT_CONSOLEFLOOR_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_LIGHT_CONSOLEFLOOR_CAPT",
                DisplayName = "Console Floor Light Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Dim", [2] = "Bright"}
            },
            ["S_MIP_LIGHT_CONSOLEFLOOR_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_LIGHT_CONSOLEFLOOR_FO",
                DisplayName = "Console Floor Light First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Dim", [2] = "Bright"}
            },

            // ISIS (Standby Instrument) - 6 momentary buttons
            ["S_MIP_ISFD_BUGS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_BUGS",
                DisplayName = "ISIS Bugs",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ISFD_LS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_LS",
                DisplayName = "ISIS Localizer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ISFD_PLUS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_PLUS",
                DisplayName = "ISIS Plus",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ISFD_MINUS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_MINUS",
                DisplayName = "ISIS Minus",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ISFD_RST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_RST",
                DisplayName = "ISIS Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ISFD_BARO_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ISFD_BARO_BUTTON",
                DisplayName = "ISIS Barometric Pressure",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // GPWS/Terrain Buttons
            ["S_MIP_GPWS_VISUAL_ALERT_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_GPWS_VISUAL_ALERT_CAPT",
                DisplayName = "GPWS GS Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_GPWS_VISUAL_ALERT_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_GPWS_VISUAL_ALERT_FO",
                DisplayName = "GPWS GS First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_GPWS_TERRAIN_ON_ND_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_GPWS_TERRAIN_ON_ND_CAPT",
                DisplayName = "Terrain on ND Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_GPWS_TERRAIN_ON_ND_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_GPWS_TERRAIN_ON_ND_FO",
                DisplayName = "Terrain on ND First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Warning/Message Buttons
            ["S_MIP_MASTER_WARNING_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_MASTER_WARNING_CAPT",
                DisplayName = "Master Warning Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_MASTER_WARNING_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_MASTER_WARNING_FO",
                DisplayName = "Master Warning First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_MASTER_CAUTION_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_MASTER_CAUTION_CAPT",
                DisplayName = "Master Caution Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_MASTER_CAUTION_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_MASTER_CAUTION_FO",
                DisplayName = "Master Caution First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ATC_MSG_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ATC_MSG_CAPT",
                DisplayName = "ATC Message Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_ATC_MSG_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_ATC_MSG_FO",
                DisplayName = "ATC Message First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_CHRONO_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_CHRONO_CAPT",
                DisplayName = "Chronometer Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_CHRONO_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_CHRONO_FO",
                DisplayName = "Chronometer First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Autoland Buttons
            ["S_MIP_AUTOLAND_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_AUTOLAND_CAPT",
                DisplayName = "Autoland Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_MIP_AUTOLAND_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_AUTOLAND_FO",
                DisplayName = "Autoland First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Main Instrument Panel Lights
            ["A_MIP_LIGHTING_MAP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LIGHTING_MAP_L",
                DisplayName = "Map Light Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "Off",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },
            ["A_MIP_LIGHTING_MAP_R"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LIGHTING_MAP_R",
                DisplayName = "Map Light Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "Off",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },
            ["A_MIP_LIGHTING_FLOOD_MAIN"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LIGHTING_FLOOD_MAIN",
                DisplayName = "Main Panel Flood Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "Off",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },
            ["A_MIP_LIGHTING_FLOOD_PEDESTAL"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LIGHTING_FLOOD_PEDESTAL",
                DisplayName = "Pedestal Flood Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "Off",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },

            // Audio - Loudspeaker Volume Controls
            ["A_MIP_LOUDSPEAKER_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LOUDSPEAKER_CAPT",
                DisplayName = "Loudspeaker Volume Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "0%",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },
            ["A_MIP_LOUDSPEAKER_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LOUDSPEAKER_FO",
                DisplayName = "Loudspeaker Volume First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "0%",
                    [0.1] = "10%",
                    [0.2] = "20%",
                    [0.3] = "30%",
                    [0.4] = "40%",
                    [0.5] = "50%",
                    [0.6] = "60%",
                    [0.7] = "70%",
                    [0.8] = "80%",
                    [0.9] = "90%",
                    [1.0] = "100%"
                }
            },

            // ========== OXYGEN PANEL ==========
            ["S_OH_OXYGEN_CREW_OXYGEN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_OXYGEN_CREW_OXYGEN",
                DisplayName = "Crew Oxygen",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_OXYGEN_HIGH_ALT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_OXYGEN_HIGH_ALT",
                DisplayName = "High Altitude Landing",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_OXYGEN_MASK_MAN_ON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_OXYGEN_MASK_MAN_ON",
                DisplayName = "Mask Manual On",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_OXYGEN_TMR_RESET"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_OXYGEN_TMR_RESET",
                DisplayName = "Oxygen Timer Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== EVACUATION PANEL ==========
            ["S_OH_EVAC_CAPT_PURSER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EVAC_CAPT_PURSER",
                DisplayName = "Evac Capt/Purser Switch",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_EVAC_COMMAND"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EVAC_COMMAND",
                DisplayName = "Evac Command",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_EVAC_HORN_SHUTOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EVAC_HORN_SHUTOFF",
                DisplayName = "Evac Horn Shutoff",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== CALLS PANEL ==========
            ["S_OH_CALLS_MECH"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_MECH",
                DisplayName = "Calls Mechanic",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CALLS_ALL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_ALL",
                DisplayName = "Calls All",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CALLS_FWD"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_FWD",
                DisplayName = "Calls Forward",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CALLS_AFT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_AFT",
                DisplayName = "Calls Aft",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CALLS_EMER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_EMER",
                DisplayName = "Calls Emergency",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CALLS_EMER_Cover"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CALLS_EMER_Cover",
                DisplayName = "Calls Emergency Cover",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Closed", [1] = "Open"}
            },

            // ========== WIPERS PANEL ==========
            ["S_MISC_WIPER_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MISC_WIPER_CAPT",
                DisplayName = "Captain Wiper",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Slow", [2] = "Fast"}
            },
            ["S_MISC_WIPER_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MISC_WIPER_FO",
                DisplayName = "First Officer Wiper",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Slow", [2] = "Fast"}
            },
            ["S_MISC_WIPER_REPELLENT_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MISC_WIPER_REPELLENT_CAPT",
                DisplayName = "Captain Rain Repellent",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_MISC_WIPER_REPELLENT_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MISC_WIPER_REPELLENT_FO",
                DisplayName = "First Officer Rain Repellent",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== CARGO SMOKE PANEL ==========
            ["S_OH_CARGO_SMOKE_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CARGO_SMOKE_TEST",
                DisplayName = "Cargo Smoke Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CARGO_DISC_1_OLD_LAYOUT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CARGO_DISC_1_OLD_LAYOUT",
                DisplayName = "Cargo Discharge 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_CARGO_DISC_2_OLD_LAYOUT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_CARGO_DISC_2_OLD_LAYOUT",
                DisplayName = "Cargo Discharge 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== GPWS PANEL ==========
            ["S_OH_GPWS_TERR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_GPWS_TERR",
                DisplayName = "GPWS Terrain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_GPWS_SYS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_GPWS_SYS",
                DisplayName = "GPWS System",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_GPWS_LDG_FLAP3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_GPWS_LDG_FLAP3",
                DisplayName = "GPWS Landing Flap 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_GPWS_GS_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_GPWS_GS_MODE",
                DisplayName = "GPWS Glideslope Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_GPWS_FLAP_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_GPWS_FLAP_MODE",
                DisplayName = "GPWS Flap Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== ENGINE PANEL ==========
            ["S_OH_ENG_MANSTART_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_MANSTART_1",
                DisplayName = "Engine 1 Manual Start",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ENG_MANSTART_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_MANSTART_2",
                DisplayName = "Engine 2 Manual Start",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ENG_N1_MODE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_N1_MODE_1",
                DisplayName = "Engine 1 N1 Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ENG_N1_MODE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_N1_MODE_2",
                DisplayName = "Engine 2 N1 Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== MAINTENANCE PANEL ==========
            ["S_OH_AFT_FADEC_GND_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_AFT_FADEC_GND_1",
                DisplayName = "FADEC Ground 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_AFT_FADEC_GND_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_AFT_FADEC_GND_2",
                DisplayName = "FADEC Ground 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELT",
                DisplayName = "Emergency Locator Transmitter",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ELT_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ELT_TEST",
                DisplayName = "ELT Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_APU_AUTOEXTING_RESET"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_APU_AUTOEXTING_RESET",
                DisplayName = "APU Auto Extinguishing Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_APU_AUTOEXTING_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_APU_AUTOEXTING_TEST",
                DisplayName = "APU Auto Extinguishing Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_SVCE_INT_OVRD"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_SVCE_INT_OVRD",
                DisplayName = "Service Interphone Override",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_LIGHTING_AVIONICS_COMPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_LIGHTING_AVIONICS_COMPT",
                DisplayName = "Avionics Compartment Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== PEDESTAL - ENGINES PANEL (3 variables) ==========
            ["S_ENG_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ENG_MODE",
                DisplayName = "Engine Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Crank", [1] = "Norm", [2] = "Ign/Start"}
            },
            ["S_ENG_MASTER_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ENG_MASTER_1",
                DisplayName = "Engine 1 Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_ENG_MASTER_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ENG_MASTER_2",
                DisplayName = "Engine 2 Master",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== PEDESTAL - WEATHER RADAR PANEL (7 variables) ==========
            // PWS Switch (Combo box)
            ["S_WR_PRED_WS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_WR_PRED_WS",
                DisplayName = "PWS (Predictive Wind Shear)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Auto"}
            },

            // System Switch (Combo box with 3 positions)
            ["S_WR_SYS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_WR_SYS",
                DisplayName = "System",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "1", [1] = "Off", [2] = "2"}
            },

            // GCS Switch (Combo box)
            ["S_WR_GCS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_WR_GCS",
                DisplayName = "GCS (Ground Clutter Suppression)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Multiscan Switch (Combo box)
            ["S_WR_MULTISCAN"] = new SimConnect.SimVarDefinition
            {
                Name = "S_WR_MULTISCAN",
                DisplayName = "Multiscan",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // Tilt Knob (Combo box with 31 positions: -15 to +15)
            ["A_WR_TILT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_WR_TILT",
                DisplayName = "Tilt",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [-15] = "-15°", [-14] = "-14°", [-13] = "-13°", [-12] = "-12°", [-11] = "-11°",
                    [-10] = "-10°", [-9] = "-9°", [-8] = "-8°", [-7] = "-7°", [-6] = "-6°",
                    [-5] = "-5°", [-4] = "-4°", [-3] = "-3°", [-2] = "-2°", [-1] = "-1°",
                    [0] = "0°", [1] = "+1°", [2] = "+2°", [3] = "+3°", [4] = "+4°",
                    [5] = "+5°", [6] = "+6°", [7] = "+7°", [8] = "+8°", [9] = "+9°",
                    [10] = "+10°", [11] = "+11°", [12] = "+12°", [13] = "+13°", [14] = "+14°", [15] = "+15°"
                }
            },

            // Gain Knob (Combo box with 10 positions: -5 to +4)
            ["A_WR_GAIN"] = new SimConnect.SimVarDefinition
            {
                Name = "A_WR_GAIN",
                DisplayName = "Gain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [-5] = "-5", [-4] = "-4", [-3] = "-3", [-2] = "-2", [-1] = "-1",
                    [0] = "0", [1] = "+1", [2] = "+2", [3] = "+3", [4] = "+4"
                }
            },

            // Image Selector Knob (Combo box with 11 positions: 0.0 to 1.0)
            ["S_WR_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_WR_MODE",
                DisplayName = "Image Selector",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0.0] = "0", [0.1] = "1", [0.2] = "2", [0.3] = "3", [0.4] = "4",
                    [0.5] = "5", [0.6] = "6", [0.7] = "7", [0.8] = "8", [0.9] = "9", [1.0] = "10"
                }
            },

            // ========== PEDESTAL - ECAM PANEL (20 variables) ==========
            // Brightness Knobs (2 step-based combo boxes)
            ["A_DISPLAY_BRIGHTNESS_ECAM_U"] = new SimConnect.SimVarDefinition
            {
                Name = "A_DISPLAY_BRIGHTNESS_ECAM_U",
                DisplayName = "Upper ECAM Brightness",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%", [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%", [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%", [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%", [1.00] = "100%"}
            },
            ["A_DISPLAY_BRIGHTNESS_ECAM_L"] = new SimConnect.SimVarDefinition
            {
                Name = "A_DISPLAY_BRIGHTNESS_ECAM_L",
                DisplayName = "Lower ECAM Brightness",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.00] = "0%", [0.05] = "5%", [0.10] = "10%", [0.15] = "15%", [0.20] = "20%", [0.25] = "25%", [0.30] = "30%", [0.35] = "35%", [0.40] = "40%", [0.45] = "45%", [0.50] = "50%", [0.55] = "55%", [0.60] = "60%", [0.65] = "65%", [0.70] = "70%", [0.75] = "75%", [0.80] = "80%", [0.85] = "85%", [0.90] = "90%", [0.95] = "95%", [1.00] = "100%"}
            },

            // ECAM System Page Buttons (18 buttons)
            ["S_ECAM_ENGINE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_ENGINE",
                DisplayName = "ECAM ENG",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_BLEED"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_BLEED",
                DisplayName = "ECAM BLEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_CAB_PRESS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_CAB_PRESS",
                DisplayName = "ECAM PRESS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_ELEC"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_ELEC",
                DisplayName = "ECAM ELEC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_HYD"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_HYD",
                DisplayName = "ECAM HYD",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_FUEL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_FUEL",
                DisplayName = "ECAM FUEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_APU"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_APU",
                DisplayName = "ECAM APU",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_COND"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_COND",
                DisplayName = "ECAM COND",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_DOOR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_DOOR",
                DisplayName = "ECAM DOOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_WHEEL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_WHEEL",
                DisplayName = "ECAM WHEEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_FCTL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_FCTL",
                DisplayName = "ECAM F/CTL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_ALL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_ALL",
                DisplayName = "ECAM ALL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_STATUS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_STATUS",
                DisplayName = "ECAM STS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_CLR_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_CLR_LEFT",
                DisplayName = "ECAM CLR Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_CLR_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_CLR_RIGHT",
                DisplayName = "ECAM CLR Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_RCL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_RCL",
                DisplayName = "ECAM RCL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_TO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_TO",
                DisplayName = "ECAM TO CONFIG",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_ECAM_EMER_CANCEL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_ECAM_EMER_CANCEL",
                DisplayName = "ECAM EMER CANC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // ========== PEDESTAL - FLIGHT CONTROLS PANEL (5 variables) ==========
            ["S_MIP_PARKING_BRAKE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_PARKING_BRAKE",
                DisplayName = "Parking Brake",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["A_FC_SPEEDBRAKE"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_SPEEDBRAKE",
                DisplayName = "Speedbrake/Spoilers",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Armed", [1] = "Disarmed/Stowed", [2] = "Half Extended", [3] = "Fully Extended"}
            },
            ["S_FC_RUDDER_TRIM_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_RUDDER_TRIM",
                DisplayName = "Rudder Trim Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_FC_RUDDER_TRIM_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_RUDDER_TRIM",
                DisplayName = "Rudder Trim Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_FC_RUDDER_TRIM_RESET"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_RUDDER_TRIM_RESET",
                DisplayName = "Rudder Trim Reset",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["A_FC_ELEVATOR_TRIM"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_ELEVATOR_TRIM",
                DisplayName = "Elevator Trim",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [-4000] = "Full Nose Down (-4000)",
                    [-3000] = "-3000",
                    [-2000] = "-2000",
                    [-1000] = "-1000",
                    [0] = "Neutral (0)",
                    [1000] = "+1000",
                    [2000] = "+2000",
                    [3000] = "+3000",
                    [4000] = "+4000",
                    [5000] = "+5000",
                    [6000] = "+6000",
                    [7000] = "+7000",
                    [8000] = "+8000",
                    [9000] = "+9000",
                    [10000] = "+10000",
                    [11000] = "+11000",
                    [12000] = "+12000",
                    [13000] = "+13000",
                    [13500] = "Full Nose Up (+13500)"
                }
            },
            ["S_FC_FLAPS_LEVER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_FLAPS",
                DisplayName = "Flaps Lever",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "0 (Retracted)", [1] = "1 (Config 1)", [2] = "2 (Config 2)", [3] = "3 (Config 3)", [4] = "4 (Full)"}
            },
            ["A_FC_THROTTLE_LEFT_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_THROTTLE_LEFT_INPUT",
                DisplayName = "Left Thrust Lever",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[1] = "Reverse", [2] = "Idle", [3] = "CLB", [4] = "FLX/MCT", [5] = "TOGA"}
            },
            ["A_FC_THROTTLE_RIGHT_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_THROTTLE_RIGHT_INPUT",
                DisplayName = "Right Thrust Lever",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[1] = "Reverse", [2] = "Idle", [3] = "CLB", [4] = "FLX/MCT", [5] = "TOGA"}
            },
            ["A_FC_THROTTLE_BOTH_INPUT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_THROTTLE_BOTH_INPUT",
                DisplayName = "Both Thrust Levers",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[1] = "Reverse", [2] = "Idle", [3] = "CLB", [4] = "FLX/MCT", [5] = "TOGA"}
            },
            ["S_FC_THR_INST_DISCONNECT1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_THR_INST_DISCONNECT1",
                DisplayName = "A/THR Disconnect Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_FC_THR_INST_DISCONNECT2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_THR_INST_DISCONNECT2",
                DisplayName = "A/THR Disconnect Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_FC_CAPT_INST_DISCONNECT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_CAPT_INST_DISCONNECT",
                DisplayName = "AP Disconnect Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },
            ["S_FC_FO_INST_DISCONNECT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_FO_INST_DISCONNECT",
                DisplayName = "AP Disconnect First Officer",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // ========== PEDESTAL - ATC TCAS PANEL (9 variables) ==========

            // Transponder Mode Knob
            ["S_XPDR_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_XPDR_MODE",
                DisplayName = "Transponder Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "STBY", [1] = "TA", [2] = "TA/RA"}
            },

            // Transponder Operation Knob
            ["S_XPDR_OPERATION"] = new SimConnect.SimVarDefinition
            {
                Name = "S_XPDR_OPERATION",
                DisplayName = "Transponder Operation",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "STBY", [1] = "AUTO", [2] = "ON"}
            },

            // ATC Switch
            ["S_XPDR_ATC"] = new SimConnect.SimVarDefinition
            {
                Name = "S_XPDR_ATC",
                DisplayName = "ATC Switch",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "ATC 1", [1] = "ATC 2"}
            },

            // Altitude Reporting Switch
            ["S_XPDR_ALTREPORTING"] = new SimConnect.SimVarDefinition
            {
                Name = "S_XPDR_ALTREPORTING",
                DisplayName = "Altitude Reporting",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // TCAS Traffic/Range Knob
            ["S_TCAS_RANGE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_TCAS_RANGE",
                DisplayName = "TCAS Traffic",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "THRT", [1] = "ALL", [2] = "ABV", [3] = "BLW"}
            },

            // IDENT Button
            ["S_XPDR_IDENT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_XPDR_IDENT",
                DisplayName = "IDENT",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // CLR Button
            ["S_PED_ATC_CLR"] = new SimConnect.SimVarDefinition
            {
                Name = "S_PED_ATC_CLR",
                DisplayName = "CLR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // Transponder Code Set (Standard MSFS Event)
            ["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "XPNDR_SET",
                DisplayName = "SQUAWK",
                Type = SimConnect.SimVarType.Event,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
            },

            // ========== SIGNS PANEL ==========
            // Seat Belt Signs
            ["S_OH_SIGNS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_SIGNS",
                DisplayName = "Seat Belt Signs",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // No Smoking Signs
            ["S_OH_SIGNS_SMOKING"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_SIGNS_SMOKING",
                DisplayName = "No Smoking Signs",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Auto", [2] = "On"}
            },

            // Emergency Exit Lights
            ["S_OH_INT_LT_EMER"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_INT_LT_EMER",
                DisplayName = "Emergency Exit Lights",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Arm", [2] = "On"}
            },

            // ========== EXTERNAL LIGHTS PANEL ==========
            // NAV & LOGO
            ["S_OH_EXT_LT_NAV_LOGO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_NAV_LOGO",
                DisplayName = "NAV & LOGO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "NAV", [2] = "LOGO"}
            },

            // STROBE
            ["S_OH_EXT_LT_STROBE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_STROBE",
                DisplayName = "Strobe",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Auto", [2] = "On"}
            },

            // BEACON
            ["S_OH_EXT_LT_BEACON"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_BEACON",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // WING
            ["S_OH_EXT_LT_WING"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_WING",
                DisplayName = "Wing",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // LANDING LEFT
            ["S_OH_EXT_LT_LANDING_L"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_LANDING_L",
                DisplayName = "Landing Left",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Retract", [1] = "Off", [2] = "On"}
            },

            // LANDING RIGHT
            ["S_OH_EXT_LT_LANDING_R"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_LANDING_R",
                DisplayName = "Landing Right",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Retract", [1] = "Off", [2] = "On"}
            },

            // LANDING BOTH
            ["S_OH_EXT_LT_LANDING_BOTH"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_LANDING_BOTH",
                DisplayName = "Landing Both",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Retract", [1] = "Off", [2] = "On"}
            },

            // RWY TURN OFF
            ["S_OH_EXT_LT_RWY_TURNOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_RWY_TURNOFF",
                DisplayName = "Runway Turn Off",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // NOSE
            ["S_OH_EXT_LT_NOSE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_EXT_LT_NOSE",
                DisplayName = "Nose",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Taxi", [2] = "TO"}
            },

            // ========== INTERIOR LIGHTS PANEL ==========
            // DOME
            ["S_OH_INT_LT_DOME"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_INT_LT_DOME",
                DisplayName = "Dome",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Dim", [2] = "Bright"}
            },

            // ANNUNCIATOR
            ["S_OH_IN_LT_ANN_LT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_IN_LT_ANN_LT",
                DisplayName = "Annunciator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Dim", [1] = "Bright", [2] = "Test"}
            },

            // ICE STANDBY
            ["S_OH_IN_LT_ICE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_IN_LT_ICE",
                DisplayName = "Ice Standby",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // CAPTAIN READING (0.1 steps)
            ["A_OH_LIGHTING_READING_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_LIGHTING_READING_CAPT",
                DisplayName = "Captain Reading",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.0", [0.1] = "0.1", [0.2] = "0.2", [0.3] = "0.3", [0.4] = "0.4", [0.5] = "0.5", [0.6] = "0.6", [0.7] = "0.7", [0.8] = "0.8", [0.9] = "0.9", [1.0] = "1.0"}
            },

            // FO READING (0.1 steps)
            ["A_OH_LIGHTING_READING_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_LIGHTING_READING_FO",
                DisplayName = "FO Reading",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.0", [0.1] = "0.1", [0.2] = "0.2", [0.3] = "0.3", [0.4] = "0.4", [0.5] = "0.5", [0.6] = "0.6", [0.7] = "0.7", [0.8] = "0.8", [0.9] = "0.9", [1.0] = "1.0"}
            },

            // OVERHEAD INTEGRAL (0.05 steps)
            ["A_OH_LIGHTING_OVD"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_LIGHTING_OVD",
                DisplayName = "Overhead Integral",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0.0] = "0.00", [0.05] = "0.05", [0.1] = "0.10", [0.15] = "0.15", [0.2] = "0.20", [0.25] = "0.25", [0.3] = "0.30", [0.35] = "0.35", [0.4] = "0.40", [0.45] = "0.45", [0.5] = "0.50", [0.55] = "0.55", [0.6] = "0.60", [0.65] = "0.65", [0.7] = "0.70", [0.75] = "0.75", [0.8] = "0.80", [0.85] = "0.85", [0.9] = "0.90", [0.95] = "0.95", [1.0] = "1.00"}
            },

            // ========== FLIGHT CONTROLS PANEL ==========
            // ELAC 1
            ["S_OH_FLT_CTL_ELAC_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_ELAC_1",
                DisplayName = "ELAC 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ELAC 2
            ["S_OH_FLT_CTL_ELAC_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_ELAC_2",
                DisplayName = "ELAC 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // SEC 1
            ["S_OH_FLT_CTL_SEC_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_SEC_1",
                DisplayName = "SEC 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // SEC 2
            ["S_OH_FLT_CTL_SEC_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_SEC_2",
                DisplayName = "SEC 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // SEC 3
            ["S_OH_FLT_CTL_SEC_3"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_SEC_3",
                DisplayName = "SEC 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // FAC 1
            ["S_OH_FLT_CTL_FAC_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_FAC_1",
                DisplayName = "FAC 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // FAC 2
            ["S_OH_FLT_CTL_FAC_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_FLT_CTL_FAC_2",
                DisplayName = "FAC 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== VOICE RECORDER PANEL ==========
            // GND CTL
            ["S_OH_RCRD_GND_CTL"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_RCRD_GND_CTL",
                DisplayName = "GND CTL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                RenderAsButton = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Press"}
            },

            // CVR ERASE
            ["S_OH_RCRD_ERASE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_RCRD_ERASE",
                DisplayName = "CVR Erase",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // CVR TEST
            ["S_OH_RCRD_TEST"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_RCRD_TEST",
                DisplayName = "CVR Test",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== COCKPIT DOOR PANEL ==========
            // VIDEO
            ["S_OH_COCKPIT_DOOR_VIDEO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_COCKPIT_DOOR_VIDEO",
                DisplayName = "VIDEO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            ["N_ELEC_VOLT_BAT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "N_ELEC_VOLT_BAT_1",
                DisplayName = "Battery 1 Voltage Display",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_ELEC_VOLT_BAT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "N_ELEC_VOLT_BAT_2",
                DisplayName = "Battery 2 Voltage Display",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_AC_STAT_INV"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_AC_STAT_INV",
                DisplayName = "ELEC BUS POWER AC STAT INV STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_AC_ESS"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_AC_ESS",
                DisplayName = "ELEC BUS POWER AC ESS STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_AC_1"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_AC_1",
                DisplayName = "ELEC BUS POWER AC 1 STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_AC_2"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_AC_2",
                DisplayName = "ELEC BUS POWER AC 2 STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_AC_ESS_SHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_AC_ESS_SHED",
                DisplayName = "ELEC BUS POWER AC ESS SHED STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_ESS"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_ESS",
                DisplayName = "ELEC BUS POWER DC ESS STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_SERIVICE"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_SERIVICE",
                DisplayName = "ELEC BUS POWER DC SERVICE STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_BAT"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_BAT",
                DisplayName = "ELEC BUS POWER DC BAT STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_ESS_SHED"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_ESS_SHED",
                DisplayName = "ELEC BUS POWER DC ESS SHED STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_1"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_1",
                DisplayName = "ELEC BUS POWER DC 1 STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["B_ELEC_BUS_POWER_DC_2"] = new SimConnect.SimVarDefinition
            {
                Name = "B_ELEC_BUS_POWER_DC_2",
                DisplayName = "ELEC BUS POWER DC 2 STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== ENGINES (8 variables) ==========
            ["S_OH_ENG_N1_MODE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_N1_MODE_2",
                DisplayName = "Engine N1 Mode 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_OH_ENG_N1_MODE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_ENG_N1_MODE_1",
                DisplayName = "Engine N1 Mode 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ENG_MANSTART_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ENG_MANSTART_2_L",
                DisplayName = "Engine Manual Start 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ENG_MANSTART_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ENG_MANSTART_1_L",
                DisplayName = "Engine Manual Start 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_START_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_START_U",
                DisplayName = "APU Start Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_START_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_START_L",
                DisplayName = "APU Start Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_MASTER_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_MASTER_U",
                DisplayName = "APU Master Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_APU_MASTER_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_APU_MASTER_L",
                DisplayName = "APU Master Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== FLIGHT CONTROLS (3 variables) ==========
            ["S_FC_FLAPS"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_FLAPS",
                DisplayName = "FC FLAPS LEVER POSITION INDEX",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            // ========== FLIGHT INSTRUMENTATION (20 variables) ==========
            ["FNX2PLD_speedV1"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_speedV1",
                DisplayName = "FNX320+FENIXQUARTZ TO-SPEEDS V1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "knots",
            },
            ["FNX2PLD_speedVR"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_speedVR",
                DisplayName = "FNX320+FENIXQUARTZ TO-SPEEDS VR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "knots",
            },
            ["FNX2PLD_speedV2"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_speedV2",
                DisplayName = "FNX320+FENIXQUARTZ TO-SPEEDS V2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "knots",
            },
            ["FNX2PLD_fcuSpd"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuSpd",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT TARGET AIRSPEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "knots",
            },
            ["FNX2PLD_fcuHdg"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuHdg",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT TARGET HEADING",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "degrees",
            },
            ["FNX2PLD_fcuAlt"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuAlt",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT TARGET ALTITUDE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "feet",
            },
            ["FNX2PLD_fcuVs"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuVs",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT TARGET VERTICAL SPEED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                Units = "feet per minute",
            },
            ["FNX2PLD_isVsActive"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_isVsActive",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT VERTICAL SPEED ACTIVE LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["FNX2PLD_fcuSpdDashed"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuSpdDashed",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT SPEED Dashed LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["FNX2PLD_fcuHdgDashed"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuHdgDashed",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT HDG Dashed LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["FNX2PLD_fcuVsDashed"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_fcuVsDashed",
                DisplayName = "FNX320+FENIXQUARTZ AUTOPILOT AUTOPILOT VS Dashed LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Displayed", [1] = "Dashed"}
            },
            ["FNX2PLD_bat1"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_bat1",
                DisplayName = "FNX320+FENIXQUARTZ BAT 1 VOLTAGE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_bat2"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_bat2",
                DisplayName = "FNX320+FENIXQUARTZ BAT 2 VOLTAGE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_xpdr"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_xpdr",
                DisplayName = "FNX320+FENIXQUARTZ XPDR CODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_clockChr"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_clockChr",
                DisplayName = "FNX320+FENIXQUARTZ CLOCK CHR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_clockEt"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_clockEt",
                DisplayName = "FNX320+FENIXQUARTZ CLOCK ET",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== FUEL (16 variables) ==========
            ["I_OH_FUEL_RIGHT_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_RIGHT_2_U",
                DisplayName = "Fuel Wing Tank Pump Right 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_RIGHT_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_RIGHT_2_L",
                DisplayName = "Fuel Wing Tank Pump Right 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_RIGHT_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_RIGHT_1_U",
                DisplayName = "Fuel Wing Tank Pump Right 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_RIGHT_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_RIGHT_1_L",
                DisplayName = "Fuel Wing Tank Pump Right 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_LEFT_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_LEFT_2_U",
                DisplayName = "Fuel Wing Tank Pump Left 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_LEFT_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_LEFT_2_L",
                DisplayName = "Fuel Wing Tank Pump Left 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_LEFT_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_LEFT_1_U",
                DisplayName = "Fuel Wing Tank Pump Left 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_LEFT_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_LEFT_1_L",
                DisplayName = "Fuel Wing Tank Pump Left 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_MODE_SEL_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_MODE_SEL_U",
                DisplayName = "Fuel Mode Select Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_MODE_SEL_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_MODE_SEL_L",
                DisplayName = "Fuel Mode Select Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_XFEED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_XFEED_U",
                DisplayName = "Fuel Crossfeed Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_XFEED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_XFEED_L",
                DisplayName = "Fuel Crossfeed Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_CENTER_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_CENTER_2_U",
                DisplayName = "Fuel Center Tank Pump 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_CENTER_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_CENTER_2_L",
                DisplayName = "Fuel Center Tank Pump 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_CENTER_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_CENTER_1_U",
                DisplayName = "Fuel Center Tank Pump 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FUEL_CENTER_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FUEL_CENTER_1_L",
                DisplayName = "Fuel Center Tank Pump 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== GEAR (17 variables) ==========
            ["I_MIP_GEAR_RED"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_RED",
                DisplayName = "MainPanel Landing Gear Arrow",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_3_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_3_U",
                DisplayName = "MainPanel Landing Gear 3 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_3_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_3_L",
                DisplayName = "MainPanel Landing Gear 3 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_2_U",
                DisplayName = "MainPanel Landing Gear 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_2_L",
                DisplayName = "MainPanel Landing Gear 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_1_U",
                DisplayName = "MainPanel Landing Gear 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GEAR_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GEAR_1_L",
                DisplayName = "MainPanel Landing Gear 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_BRAKE_FAN_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_BRAKE_FAN_U",
                DisplayName = "MainPanel Brake Fan Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_BRAKE_FAN_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_BRAKE_FAN_L",
                DisplayName = "MainPanel Brake Fan Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_MED_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_MED_U",
                DisplayName = "MainPanel Autobrake Medium Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_MED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_MED_L",
                DisplayName = "MainPanel Autobrake Medium Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_MAX_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_MAX_U",
                DisplayName = "MainPanel Autobrake Max Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_MAX_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_MAX_L",
                DisplayName = "MainPanel Autobrake Max Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_LO_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_LO_U",
                DisplayName = "MainPanel Autobrake Low Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_AUTOBRAKE_LO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_AUTOBRAKE_LO_L",
                DisplayName = "MainPanel Autobrake Low Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_MIP_PARKING_BRAKE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_MIP_PARKING_BRAKE",
                DisplayName = "PARKING BRAKE STATE LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_FC_CAPT_TILLER_PEDAL_DISCONNECT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_FC_CAPT_TILLER_PEDAL_DISCONNECT",
                DisplayName = "Led PedalDisc Capt",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== HYDRAULIC (13 variables) ==========
            ["I_OH_HYD_PTU_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_PTU_U",
                DisplayName = "Hydraulic PTU Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_PTU_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_PTU_L",
                DisplayName = "Hydraulic PTU Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_ENG_2_PUMP_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_ENG_2_PUMP_U",
                DisplayName = "Hydraulic Engine 2 Pump Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_ENG_2_PUMP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_ENG_2_PUMP_L",
                DisplayName = "Hydraulic Engine 2 Pump Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_ENG_1_PUMP_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_ENG_1_PUMP_U",
                DisplayName = "Hydraulic Engine 1 Pump Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_ENG_1_PUMP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_ENG_1_PUMP_L",
                DisplayName = "Hydraulic Engine 1 Pump Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_YELLOW_ELEC_PUMP_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_YELLOW_ELEC_PUMP_U",
                DisplayName = "Hydraulic Electrical Pump Yellow Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_YELLOW_ELEC_PUMP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_YELLOW_ELEC_PUMP_L",
                DisplayName = "Hydraulic Electrical Pump Yellow Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_BLUE_ELEC_PUMP_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_BLUE_ELEC_PUMP_U",
                DisplayName = "Hydraulic Electrical Pump Blue Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_BLUE_ELEC_PUMP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_BLUE_ELEC_PUMP_L",
                DisplayName = "Hydraulic Electrical Pump Blue Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["N_HYD_PRESSURE_BRAKE_LEFT"] = new SimConnect.SimVarDefinition
            {
                Name = "N_HYD_PRESSURE_BRAKE_LEFT",
                DisplayName = "BRAKE HYD PRESSURE LEFT NEEDLE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_HYD_PRESSURE_BRAKE_RIGHT"] = new SimConnect.SimVarDefinition
            {
                Name = "N_HYD_PRESSURE_BRAKE_RIGHT",
                DisplayName = "BRAKE HYD PRESSURE RIGHT NEEDLE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_HYD_PRESSURE_BRAKE_ACCU"] = new SimConnect.SimVarDefinition
            {
                Name = "N_HYD_PRESSURE_BRAKE_ACCU",
                DisplayName = "BRAKE HYD ACCU PRESSURE NEEDLE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== ISIS (1 variables) ==========
            ["FNX2PLD_isisBaro"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_isisBaro",
                DisplayName = "FNX320+FENIXQUARTZ ISIS BARO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== LIGHTS (5 variables) ==========
            ["A_PED_LIGHTING_PEDESTAL"] = new SimConnect.SimVarDefinition
            {
                Name = "A_PED_LIGHTING_PEDESTAL",
                DisplayName = "FNX32 INTEG Light Knob Position",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["A_FCU_LIGHTING"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FCU_LIGHTING",
                DisplayName = "FCU INTEG BRIGHTNESS VALUE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["A_FCU_LIGHTING_TEXT"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FCU_LIGHTING_TEXT",
                DisplayName = "FCU BRIGHTNESS VALUE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["A_MIP_LIGHTING_FLOOD_MAIN"] = new SimConnect.SimVarDefinition
            {
                Name = "A_MIP_LIGHTING_FLOOD_MAIN",
                DisplayName = "LIGHTING FLOOD Main Pot Position",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["N_PED_LIGHTING_PEDESTAL"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_LIGHTING_PEDESTAL",
                DisplayName = "FNX32 Pedestal Back Lighting Value",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== LIGHTS (INTERIOR) (1 variables) ==========

            // ========== MCDU (20 variables) ==========
            ["I_CDU2_RDY"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_RDY",
                DisplayName = "MCDU RDY FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_RDY"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_RDY",
                DisplayName = "MCDU RDY Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_MCDU_MENU"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_MCDU_MENU",
                DisplayName = "MCDU MENU FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_MCDU_MENU"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_MCDU_MENU",
                DisplayName = "MCDU MENU Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_IND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_IND",
                DisplayName = "MCDU IND FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_IND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_IND",
                DisplayName = "MCDU IND Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_FM2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_FM2",
                DisplayName = "MCDU FM2 FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_FM2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_FM2",
                DisplayName = "MCDU FM2 Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_FM1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_FM1",
                DisplayName = "MCDU FM1 FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_FM1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_FM1",
                DisplayName = "MCDU FM1 Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_FM"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_FM",
                DisplayName = "MCDU FM FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_FM"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_FM",
                DisplayName = "MCDU FM Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_FAIL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_FAIL",
                DisplayName = "MCDU FAIL FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_FAIL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_FAIL",
                DisplayName = "MCDU FAIL Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU2_DASH"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU2_DASH",
                DisplayName = "MCDU Dash FO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_CDU1_DASH"] = new SimConnect.SimVarDefinition
            {
                Name = "I_CDU1_DASH",
                DisplayName = "MCDU Dash Captain",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["N_MISC_PERF_TO_V1"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MISC_PERF_TO_V1",
                DisplayName = "MCDU V1 Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MISC_PERF_TO_V2"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MISC_PERF_TO_V2",
                DisplayName = "MCDU V2 Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MISC_PERF_TO_VR"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MISC_PERF_TO_VR",
                DisplayName = "MCDU VR Speed",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MISC_PERF_TO_FLEX"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MISC_PERF_TO_FLEX",
                DisplayName = "MCDU Flex Temp",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },

            // ========== MISCELLANEOUS (31 variables) ==========
            ["I_OH_CARGO_SMOKE_FWD_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CARGO_SMOKE_FWD_U",
                DisplayName = "CargoSmoke Forward",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CARGO_SMOKE_AFT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CARGO_SMOKE_AFT_U",
                DisplayName = "CargoSmoke Aft",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_HOT_AIR_AFT_CARGO_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_HOT_AIR_AFT_CARGO_U",
                DisplayName = "CargoHeat Hot Air Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_HOT_AIR_AFT_CARGO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_HOT_AIR_AFT_CARGO_L",
                DisplayName = "CargoHeat Hot Air Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE_U",
                DisplayName = "CargoHeat Aft Isolation Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE_L",
                DisplayName = "CargoHeat Aft Isolation Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_RCRD_GND_CTL_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_RCRD_GND_CTL_L",
                DisplayName = "Recorder Ground Control",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_SVCE_INT_OVRD"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_SVCE_INT_OVRD",
                DisplayName = "OverheadMisc Svce Int",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_OXYGEN_TMR_RESET_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_OXYGEN_TMR_RESET_U",
                DisplayName = "OverheadMisc Oxygen TMR Reset Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_OXYGEN_TMR_RESET_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_OXYGEN_TMR_RESET_L",
                DisplayName = "OverheadMisc Oxygen TMR Reset Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_LMV_YELLOW_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_LMV_YELLOW_L",
                DisplayName = "OverheadMisc LMV Yellow",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_LMV_GREEN_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_LMV_GREEN_L",
                DisplayName = "OverheadMisc LMV Green",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_LMV_BLUE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_LMV_BLUE_L",
                DisplayName = "OverheadMisc LMV Blue",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_AFT_FADEC_GND_2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_AFT_FADEC_GND_2_L",
                DisplayName = "OverheadMisc Engine FADEC Ground 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_AFT_FADEC_GND_1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_AFT_FADEC_GND_1_L",
                DisplayName = "OverheadMisc Engine FADEC Ground 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_DOOR_VIDEO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_DOOR_VIDEO",
                DisplayName = "OverheadMisc Cockpit Door Video",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_HYD_BLUE_PUMP_OVERRIDE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_HYD_BLUE_PUMP_OVERRIDE_L",
                DisplayName = "OverheadMisc Blue Pump Override",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_LIGHTING_AVIONICS_COMPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_LIGHTING_AVIONICS_COMPT",
                DisplayName = "OverheadMisc Avionics Compt Light",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_APU_AUTOEXTING_TEST_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_APU_AUTOEXTING_TEST_U",
                DisplayName = "OverheadMisc APU Auto Exiting Test Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_APU_AUTOEXTING_TEST_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_APU_AUTOEXTING_TEST_L",
                DisplayName = "OverheadMisc APU Auto Exiting Test Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_DOOR_CTL_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_DOOR_CTL_U",
                DisplayName = "OverheadMisc Cockpit Door CTL Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_DOOR_CTL_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_DOOR_CTL_L",
                DisplayName = "OverheadMisc Cockpit Door CTL Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_TOILET"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_TOILET",
                DisplayName = "OverheadMisc Toilet",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["S_TRAY_TABLE_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "S_TRAY_TABLE_CAPT",
                DisplayName = "MISC CAPT TRAY TABLE STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["S_TRAY_TABLE_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "S_TRAY_TABLE_FO",
                DisplayName = "MISC FO TRAY TABLE STATUS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MIP_CLOCK_CHRONO"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MIP_CLOCK_CHRONO",
                DisplayName = "CLOCK CHRONO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MIP_CLOCK_ELAPSED"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MIP_CLOCK_ELAPSED",
                DisplayName = "CLOCK ELAPSED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MIP_CLOCK_MODE"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MIP_CLOCK_MODE",
                DisplayName = "CLOCK UTC MODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["I_PED_COCKPIT_DOOR_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_COCKPIT_DOOR_U",
                DisplayName = "PedestalMisc Cockpit Door Open (V2)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_COCKPIT_DOOR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_COCKPIT_DOOR_L",
                DisplayName = "PedestalMisc Cockpit Door Fault (V2)",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== NAVIGATION (3 variables) ==========
            ["I_OH_NAV_ADIRS_QUEUE_CLR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADIRS_QUEUE_CLR",
                DisplayName = "ADIRS Key Clear Dot",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["I_OH_NAV_ADIRS_QUEUE_ENT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADIRS_QUEUE_ENT",
                DisplayName = "ADIRS Key Enter Dot",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            // ========== RADIO (119 variables) ==========
            ["I_ASP3_VOICE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VOICE",
                DisplayName = "ACP3 VOICE Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_3_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_3_SEND",
                DisplayName = "ACP3 VHF 3 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_3_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_3_CALL",
                DisplayName = "ACP3 VHF 3 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_2_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_2_SEND",
                DisplayName = "ACP3 VHF 2 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_2_CALL",
                DisplayName = "ACP3 VHF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_1_SEND",
                DisplayName = "ACP3 VHF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_VHF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_VHF_1_CALL",
                DisplayName = "ACP3 VHF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_INT_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_INT_SEND",
                DisplayName = "ACP3 INT Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_INT_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_INT_CALL",
                DisplayName = "ACP3 INT Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_HF_2_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_HF_2_SEND",
                DisplayName = "ACP3 HF 2 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_HF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_HF_2_CALL",
                DisplayName = "ACP3 HF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_HF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_HF_1_SEND",
                DisplayName = "ACP3 HF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_HF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_HF_1_CALL",
                DisplayName = "ACP3 HF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_CAB_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_CAB_SEND",
                DisplayName = "ACP3 CAB Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_CAB_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_CAB_CALL",
                DisplayName = "ACP3 CAB Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VOICE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VOICE",
                DisplayName = "ACP2 VOICE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_3_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_3_SEND",
                DisplayName = "ACP2 VHF 3 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_3_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_3_CALL",
                DisplayName = "ACP2 VHF 3 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_2_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_2_SEND",
                DisplayName = "ACP2 VHF 2 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_2_CALL",
                DisplayName = "ACP2 VHF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_1_SEND",
                DisplayName = "ACP2 VHF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_VHF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_VHF_1_CALL",
                DisplayName = "ACP2 VHF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_PA_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_PA_SEND",
                DisplayName = "ACP2 PA SEND",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_INT_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_INT_SEND",
                DisplayName = "ACP2 INT Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_INT_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_INT_CALL",
                DisplayName = "ACP2 INT Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_HF_2_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_HF_2_SEND",
                DisplayName = "ACP2 HF 2 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_HF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_HF_2_CALL",
                DisplayName = "ACP2 HF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_HF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_HF_1_SEND",
                DisplayName = "ACP2 HF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_HF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_HF_1_CALL",
                DisplayName = "ACP2 HF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_CAB_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_CAB_SEND",
                DisplayName = "ACP2 CAB Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP2_CAB_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP2_CAB_CALL",
                DisplayName = "ACP2 CAB Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VOICE"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VOICE",
                DisplayName = "ACP1 VOICE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_3_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_3_SEND",
                DisplayName = "ACP1 VHF 3 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_3_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_3_CALL",
                DisplayName = "ACP1 VHF 3 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_2_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_2_SEND",
                DisplayName = "ACP1 VHF 2 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_2_CALL",
                DisplayName = "ACP1 VHF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_1_SEND",
                DisplayName = "ACP1 VHF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_1_CALL",
                DisplayName = "ACP1 VHF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_PA_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_PA_SEND",
                DisplayName = "ACP1 PA SEND",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_INT_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_INT_SEND",
                DisplayName = "ACP1 INT Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_INT_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_INT_CALL",
                DisplayName = "ACP1 INT Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_HF_2_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_2_CALL",
                DisplayName = "ACP1 HF 2 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_HF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_1_SEND",
                DisplayName = "ACP1 HF 1 Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_HF_1_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_1_CALL",
                DisplayName = "ACP1 HF 1 Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_CAB_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_CAB_SEND",
                DisplayName = "ACP1 CAB Send Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_VOR",
                DisplayName = "RMP3 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_VHF3",
                DisplayName = "RMP3 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_VHF2",
                DisplayName = "RMP3 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_VHF1",
                DisplayName = "RMP3 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_SEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_SEL",
                DisplayName = "RMP3 SEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_NAV",
                DisplayName = "RMP3 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_ILS",
                DisplayName = "RMP3 LS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_HF2",
                DisplayName = "RMP3 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_HF1",
                DisplayName = "RMP3 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_MLS",
                DisplayName = "RMP3 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_BFO",
                DisplayName = "RMP3 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_AM",
                DisplayName = "RMP3 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP3_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP3_ADF",
                DisplayName = "RMP3 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_VOR",
                DisplayName = "RMP2 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_VHF3",
                DisplayName = "RMP2 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_VHF2",
                DisplayName = "RMP2 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_VHF1",
                DisplayName = "RMP2 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_SEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_SEL",
                DisplayName = "RMP2 SEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_NAV",
                DisplayName = "RMP2 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_ILS",
                DisplayName = "RMP2 LS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_HF2",
                DisplayName = "RMP2 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_HF1",
                DisplayName = "RMP2 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_MLS",
                DisplayName = "RMP2 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_BFO",
                DisplayName = "RMP2 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_AM",
                DisplayName = "RMP2 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP2_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP2_ADF",
                DisplayName = "RMP2 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_VOR"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_VOR",
                DisplayName = "RMP1 VOR",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_VHF3"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_VHF3",
                DisplayName = "RMP1 VHF 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_VHF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_VHF2",
                DisplayName = "RMP1 VHF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_VHF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_VHF1",
                DisplayName = "RMP1 VHF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_SEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_SEL",
                DisplayName = "RMP1 SEL",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_NAV",
                DisplayName = "RMP1 NAV",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_ILS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_ILS",
                DisplayName = "RMP1 LS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_HF2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_HF2",
                DisplayName = "RMP1 HF 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_HF1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_HF1",
                DisplayName = "RMP1 HF 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_MLS"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_MLS",
                DisplayName = "RMP1 GLS",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_BFO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_BFO",
                DisplayName = "RMP1 BFO",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_AM"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_AM",
                DisplayName = "RMP1 AM",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_PED_RMP1_ADF"] = new SimConnect.SimVarDefinition
            {
                Name = "I_PED_RMP1_ADF",
                DisplayName = "RMP1 ADF",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP3_PA_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP3_PA_SEND",
                DisplayName = "ACP3 PA SEND",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_CAB_CALL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_CAB_CALL",
                DisplayName = "ACP1 CAB Send Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["FNX2PLD_com1Active"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_com1Active",
                DisplayName = "FNX320+FENIXQUARTZ COM 1 Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_com1Standby"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_com1Standby",
                DisplayName = "FNX320+FENIXQUARTZ COM 1 Standby",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["FNX2PLD_com2Active"] = new SimConnect.SimVarDefinition
            {
                Name = "FNX2PLD_com2Active",
                DisplayName = "FNX320+FENIXQUARTZ COM 2 Active",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP1_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP1_ACTIVE",
                DisplayName = "RMP1 ACTIVE FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP1_STDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP1_STDBY",
                DisplayName = "RMP1 STANDBY FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP2_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP2_ACTIVE",
                DisplayName = "RMP2 ACTIVE FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP2_STDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP2_STDBY",
                DisplayName = "RMP2 STANDBY FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP3_ACTIVE"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP3_ACTIVE",
                DisplayName = "RMP3 ACTIVE FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_RMP3_STDBY"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_RMP3_STDBY",
                DisplayName = "RMP3 STANDBY FREQ",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FREQ_STANDBY_XPDR_SELECTED"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FREQ_STANDBY_XPDR_SELECTED",
                DisplayName = "PED XPDR STANDBY CODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_FREQ_XPDR_SELECTED"] = new SimConnect.SimVarDefinition
            {
                Name = "N_FREQ_XPDR_SELECTED",
                DisplayName = "PED XPDR SELECTED CODE",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_PED_XPDR_CHAR_DISPLAYED"] = new SimConnect.SimVarDefinition
            {
                Name = "N_PED_XPDR_CHAR_DISPLAYED",
                DisplayName = "PED XPDR CHARED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["I_ASP_VHF_1_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_1_REC",
                DisplayName = "ACP1 VHF1 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_2_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_2_REC",
                DisplayName = "ACP1 VHF2 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_VHF_3_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_VHF_3_REC",
                DisplayName = "ACP1 VHF3 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_HF_1_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_1_REC",
                DisplayName = "ACP1 HF1 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_HF_2_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_2_REC",
                DisplayName = "ACP1 HF2 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_INT_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_INT_REC",
                DisplayName = "ACP1 INT Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_CAB_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_CAB_REC",
                DisplayName = "ACP1 CAB Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_PA_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_PA_REC",
                DisplayName = "ACP1 PA Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_NAV_1_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_NAV_1_REC",
                DisplayName = "ACP1 VOR1 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_NAV_2_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_NAV_2_REC",
                DisplayName = "ACP1 VOR2 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_MARKER_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_MARKER_REC",
                DisplayName = "ACP1 MKR Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_ILS_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_ILS_REC",
                DisplayName = "ACP1 ILS Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_ADF_1_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_ADF_1_REC",
                DisplayName = "ACP1 ADF1 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ASP_ADF_2_REC"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_ADF_2_REC",
                DisplayName = "ACP1 ADF2 Receive",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== SAFETY (20 variables) ==========
            ["I_OH_OXYGEN_PASSENGER_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_OXYGEN_PASSENGER_U",
                DisplayName = "Oxygen Passenger",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_OXYGEN_CREW_OXYGEN_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_OXYGEN_CREW_OXYGEN_L",
                DisplayName = "Oxygen Crew",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_OXYGEN_HIGH_ALT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_OXYGEN_HIGH_ALT_L",
                DisplayName = "Oxygen High Alt Landing",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CARGO_SMOKE_DISCHARGE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CARGO_SMOKE_DISCHARGE_2",
                DisplayName = "CargoSmoke Discharge Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CARGO_SMOKE_DISCHARGE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CARGO_SMOKE_DISCHARGE_1",
                DisplayName = "CargoSmoke Discharge Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CARGO_SMOKE_DISCHARGE_AGENT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CARGO_SMOKE_DISCHARGE_AGENT_2",
                DisplayName = "CargoSmoke Discharge Agent",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG2_AGENT2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG2_AGENT2_U",
                DisplayName = "Fire Engine 2 Agent 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG2_AGENT2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG2_AGENT2_L",
                DisplayName = "Fire Engine 2 Agent 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG2_AGENT1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG2_AGENT1_U",
                DisplayName = "Fire Engine 2 Agent 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG2_AGENT1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG2_AGENT1_L",
                DisplayName = "Fire Engine 2 Agent 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG1_AGENT2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG1_AGENT2_U",
                DisplayName = "Fire Engine 1 Agent 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG1_AGENT2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG1_AGENT2_L",
                DisplayName = "Fire Engine 1 Agent 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG1_AGENT1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG1_AGENT1_U",
                DisplayName = "Fire Engine 1 Agent 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG1_AGENT1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG1_AGENT1_L",
                DisplayName = "Fire Engine 1 Agent 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_APU_AGENT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_APU_AGENT_U",
                DisplayName = "Fire APU Agent Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_APU_AGENT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_APU_AGENT_L",
                DisplayName = "Fire APU Agent Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG1_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG1_BUTTON",
                DisplayName = "OH Fire Eng1 Button LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_ENG2_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_ENG2_BUTTON",
                DisplayName = "OH Fire Eng2 Button LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_FIRE_APU_BUTTON"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_FIRE_APU_BUTTON",
                DisplayName = "OH Fire APU Button LED",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["B_INT_CFSBLT"] = new SimConnect.SimVarDefinition
            {
                Name = "B_INT_CFSBLT",
                DisplayName = "OVHD SEAT BELT SIGN ON",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["S_OH_SIGNS_SMOKING_STATE"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_SIGNS_SMOKING",
                DisplayName = "No Smoking Sign State",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "Auto", [2] = "On"}
            },

            // ========== WARNING (31 variables) ==========
            ["I_OH_EVAC_COMMAND_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_EVAC_COMMAND_U",
                DisplayName = "Evacuation Command Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_EVAC_COMMAND_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_EVAC_COMMAND_L",
                DisplayName = "Evacuation Command Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_EMERG_GEN_FAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_EMERG_GEN_FAULT",
                DisplayName = "EmergencyElectrical RAT Emergency Generator",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN1_LINE_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN1_LINE_U",
                DisplayName = "EmergencyElectrical Generator 1 Line Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_ELEC_GEN1_LINE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_ELEC_GEN1_LINE_L",
                DisplayName = "EmergencyElectrical Generator 1 Line Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CALLS_EMER_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CALLS_EMER_U",
                DisplayName = "Call Emergency Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_CALLS_EMER_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_CALLS_EMER_L",
                DisplayName = "Call Emergency Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ENG_FIRE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ENG_FIRE_2",
                DisplayName = "Throttle Engine Fire 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ENG_FIRE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ENG_FIRE_1",
                DisplayName = "Throttle Engine Fire 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ENG_FAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ENG_FAULT_2",
                DisplayName = "Throttle Engine Fault 2",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_ENG_FAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ENG_FAULT_1",
                DisplayName = "Throttle Engine Fault 1",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_INT_LT_EMER_OFF"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_INT_LT_EMER_OFF",
                DisplayName = "Sign Emergency Exit",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_VISUAL_ALERT_FO_u"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_VISUAL_ALERT_FO_u",
                DisplayName = "MainPanel GPWS GS FO Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_VISUAL_ALERT_FO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_VISUAL_ALERT_FO_L",
                DisplayName = "MainPanel GPWS GS FO Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_VISUAL_ALERT_CAPT_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_VISUAL_ALERT_CAPT_U",
                DisplayName = "MainPanel GPWS GS Captain Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_GPWS_VISUAL_ALERT_CAPT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_GPWS_VISUAL_ALERT_CAPT_L",
                DisplayName = "MainPanel GPWS GS Captain Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_TERR_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_TERR_U",
                DisplayName = "GPWS Terrain Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_TERR_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_TERR_L",
                DisplayName = "GPWS Terrain Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_SYS_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_SYS_U",
                DisplayName = "GPWS System Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_SYS_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_SYS_L",
                DisplayName = "GPWS System Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_LDG_FLAP3_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_LDG_FLAP3_L",
                DisplayName = "GPWS Landing Flap 3",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_GS_MODE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_GS_MODE_L",
                DisplayName = "GPWS GS Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_GPWS_FLAP_MODE_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_GPWS_FLAP_MODE_L",
                DisplayName = "GPWS Flap Mode",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_WARNING_FO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_WARNING_FO_L",
                DisplayName = "Glareshield Master Warning FO Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_WARNING_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_WARNING_FO",
                DisplayName = "Glareshield Master Warning FO Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_WARNING_CAPT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_WARNING_CAPT_L",
                DisplayName = "Glareshield Master Warning Captain Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_WARNING_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_WARNING_CAPT",
                DisplayName = "Glareshield Master Warning Captain Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_CAUTION_FO_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_CAUTION_FO_L",
                DisplayName = "Glareshield Master Caution FO Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_CAUTION_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_CAUTION_FO",
                DisplayName = "Glareshield Master Caution FO Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_CAUTION_CAPT_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_CAUTION_CAPT_L",
                DisplayName = "Glareshield Master Caution Captain Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_MIP_MASTER_CAUTION_CAPT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_MIP_MASTER_CAUTION_CAPT",
                DisplayName = "Glareshield Master Caution Captain Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // TAKEOFF ASSIST VARIABLES (dynamically monitored when takeoff assist is active)
            ["PLANE_PITCH_DEGREES"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE PITCH DEGREES",
                DisplayName = "Aircraft Pitch",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when takeoff assist is active
                IsAnnounced = false, // Handled by TakeoffAssistManager
                Units = "radians" // Note: Despite name, returns radians!
            },
            ["PLANE_HEADING_DEGREES_MAGNETIC"] = new SimConnect.SimVarDefinition
            {
                Name = "PLANE HEADING DEGREES MAGNETIC",
                DisplayName = "Magnetic Heading",
                Type = SimConnect.SimVarType.SimVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest, // Registered at startup, monitored when takeoff assist is active
                IsAnnounced = false, // Handled by TakeoffAssistManager
                Units = "radians" // Note: Despite name, returns radians!
            },

            // Unused Variables - Available for future use
            // Comment out variables here when they're not needed for active monitoring
            // Uncomment and move back up when needed

            /*
            ["A_FC_ELEVATOR_TRIM"] = new SimConnect.SimVarDefinition
            {
                Name = "A_FC_ELEVATOR_TRIM",
                DisplayName = "ELEVATOR TRIM POSITION",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["A320_FC_L_FLAPS"] = new SimConnect.SimVarDefinition
            {
                Name = "A320_FC_L_FLAPS",
                DisplayName = "FC FLAPS EFFECTIVE LEFT POSITION",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["A320_FC_R_FLAPS"] = new SimConnect.SimVarDefinition
            {
                Name = "A320_FC_R_FLAPS",
                DisplayName = "FC FLAPS EFFECTIVE RIGHT POSITION",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_MIP_CLOCK_UTC"] = new SimConnect.SimVarDefinition
            {
                Name = "N_MIP_CLOCK_UTC",
                DisplayName = "CLOCK UTC",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            */
        };
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical",
                "ADIRS",
                "Air Conditioning and Pressurization",
                "Fire",
                "Hydraulic",
                "Fuel",
                "Anti-Ice",
                "Signs",
                "External Lights",
                "Interior Lights",
                "Flight Controls",
                "Voice Recorder",
                "Cockpit Door",
                "Oxygen",
                "Evacuation",
                "Calls",
                "Wipers",
                "Cargo Smoke",
                "GPWS",
                "Engine",
                "Maintenance"
            },

            ["Pedestal"] = new List<string>
            {
                "Engines",
                "Weather Radar",
                "ECAM",
                "Flight Controls",
                "ATC TCAS",
                "Radio Management Panel (RMP)",
                "Audio Control Panel (ACP)"
            },

            ["Main Instrument Panel"] = new List<string>
            {
                "Auto Brakes",
                "Landing Gear",
                "Console Floor Lights",
                "ISIS",
                "GPWS/Terrain",
                "Warnings/Messages",
                "Autoland",
                "Main Instrument Lights",
                "Audio"
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            ["Electrical"] = new List<string>
            {
                // Battery Switches (Combo boxes - 2 controls)
                "S_OH_ELEC_BAT1",
                "S_OH_ELEC_BAT2",

                // Generator Buttons (4 controls)
                "S_OH_ELEC_GEN1",
                "S_OH_ELEC_GEN2",
                "S_OH_ELEC_APU_GEN",
                "S_OH_ELEC_EXT_PWR",

                // APU Controls (2 controls)
                "S_OH_ELEC_APU_MASTER",
                "S_OH_ELEC_APU_START",

                // Bus Controls (2 controls)
                "S_OH_ELEC_BUS_TIE",
                "S_OH_ELEC_AC_ESS_FEED",

                // IDG Buttons (2 controls)
                "S_OH_ELEC_IDG1",
                "S_OH_ELEC_IDG2",

                // Galley Buttons (2 controls)
                "S_OH_ELEC_GALY",
                "S_OH_ELEC_COMMERCIAL",

                // Emergency Electrical (5 controls)
                "S_OH_ELEC_GEN1_LINE",
                "S_OH_ELEC_EMER_GEN_TEST",
                "S_OH_ELEC_EMER_GEN_MAN_ON",
                "S_OH_ELEC_EMER_GEN_MAN_ON_Cover",
                "S_OH_ELEC_EMER_GEN_TEST_Cover"
            },

            ["ADIRS"] = new List<string>
            {
                // IR Mode Knobs (3 controls)
                "S_OH_NAV_IR1_MODE",
                "S_OH_NAV_IR2_MODE",
                "S_OH_NAV_IR3_MODE",

                // ADR Buttons (3 controls)
                "S_OH_NAV_ADR1",
                "S_OH_NAV_ADR2",
                "S_OH_NAV_ADR3",

                // IR Push Buttons (3 controls)
                "S_OH_NAV_IR1_SWITCH",
                "S_OH_NAV_IR2_SWITCH",
                "S_OH_NAV_IR3_SWITCH",

                // Display Selectors (2 controls)
                "S_OH_NAV_DATA_DISP",
                "S_OH_NAV_SYS_DISP",

                // Keypad Buttons (12 controls)
                "S_OH_ADIRS_KEY_0",
                "S_OH_ADIRS_KEY_1",
                "S_OH_ADIRS_KEY_2",
                "S_OH_ADIRS_KEY_3",
                "S_OH_ADIRS_KEY_4",
                "S_OH_ADIRS_KEY_5",
                "S_OH_ADIRS_KEY_6",
                "S_OH_ADIRS_KEY_7",
                "S_OH_ADIRS_KEY_8",
                "S_OH_ADIRS_KEY_9",
                "S_OH_ADIRS_KEY_CLR",
                "S_OH_ADIRS_KEY_ENT"
            },

            ["Air Conditioning and Pressurization"] = new List<string>
            {
                // Bleed Buttons (3 controls)
                "S_OH_PNEUMATIC_APU_BLEED",
                "S_OH_PNEUMATIC_ENG1_BLEED",
                "S_OH_PNEUMATIC_ENG2_BLEED",

                // Pack Buttons (2 controls)
                "S_OH_PNEUMATIC_PACK_1",
                "S_OH_PNEUMATIC_PACK_2",

                // Air Buttons (2 controls)
                "S_OH_PNEUMATIC_HOT_AIR",
                "S_OH_PNEUMATIC_RAM_AIR",

                // Pressurization (4 controls)
                "S_OH_PNEUMATIC_DITCHING",
                "S_OH_PNEUMATIC_PRESS_MODE",
                "A_OH_PNEUMATIC_LDG_ELEV",
                "S_OH_PNEUMATIC_PRESS_MAN",

                // Ventilation Buttons (3 controls)
                "S_OH_PNEUMATIC_BLOWER",
                "S_OH_PNEUMATIC_EXTRACT",
                "S_OH_PNEUMATIC_CAB_FANS",

                // Selectors (2 controls)
                "S_OH_PNEUMATIC_XBLEED_SELECTOR",
                "S_OH_PNEUMATIC_PACK_FLOW",

                // Temperature Controls (3 controls)
                "A_OH_PNEUMATIC_COCKPIT_TEMP",
                "A_OH_PNEUMATIC_FWD_TEMP",
                "A_OH_PNEUMATIC_AFT_TEMP",

                // Cargo Controls (2 controls)
                "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO",
                "S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE"
            },

            ["Fire"] = new List<string>
            {
                // Main Fire Push Buttons (3 controls)
                "S_OH_FIRE_ENG1_BUTTON",
                "S_OH_FIRE_ENG2_BUTTON",
                "S_OH_FIRE_APU_BUTTON",

                // Test Buttons (3 controls)
                "S_OH_FIRE_ENG1_TEST",
                "S_OH_FIRE_ENG2_TEST",
                "S_OH_FIRE_APU_TEST",

                // Agent Discharge Buttons (5 controls)
                "S_OH_FIRE_ENG1_AGENT1",
                "S_OH_FIRE_ENG1_AGENT2",
                "S_OH_FIRE_ENG2_AGENT1",
                "S_OH_FIRE_ENG2_AGENT2",
                "S_OH_FIRE_APU_AGENT"
            },

            ["Hydraulic"] = new List<string>
            {
                // Engine Pumps (2 controls)
                "S_OH_HYD_ENG_1_PUMP",
                "S_OH_HYD_ENG_2_PUMP",

                // Electric Pumps (2 controls)
                "S_OH_HYD_BLUE_ELEC_PUMP",
                "S_OH_HYD_YELLOW_ELEC_PUMP",

                // PTU and RAT (2 controls)
                "S_OH_HYD_PTU",
                "S_OH_HYD_RAT_MAN_ON",

                // Low Mechanical Valves (4 controls)
                "S_OH_HYD_LMV_YELLOW",
                "S_OH_HYD_LMV_GREEN",
                "S_OH_HYD_LMV_BLUE",
                "S_OH_HYD_BLUE_PUMP_OVERRIDE"
            },

            ["Fuel"] = new List<string>
            {
                // Left Wing Tank Pumps (2 controls)
                "S_OH_FUEL_LEFT_1",
                "S_OH_FUEL_LEFT_2",

                // Center Tank Pumps (2 controls)
                "S_OH_FUEL_CENTER_1",
                "S_OH_FUEL_CENTER_2",

                // Right Wing Tank Pumps (2 controls)
                "S_OH_FUEL_RIGHT_1",
                "S_OH_FUEL_RIGHT_2",

                // Crossfeed and Mode (2 controls)
                "S_OH_FUEL_XFEED",
                "S_OH_FUEL_MODE_SEL"
            },

            ["Anti-Ice"] = new List<string>
            {
                // Engine Anti-Ice (2 controls)
                "S_OH_PNEUMATIC_ENG1_ANTI_ICE",
                "S_OH_PNEUMATIC_ENG2_ANTI_ICE",

                // Wing Anti-Ice (1 control)
                "S_OH_PNEUMATIC_WING_ANTI_ICE",

                // Probe Heat (1 control)
                "S_OH_PROBE_HEAT"
            },

            ["External Lights"] = new List<string>
            {
                // External Lights (9 controls)
                "S_OH_EXT_LT_NAV_LOGO",
                "S_OH_EXT_LT_STROBE",
                "S_OH_EXT_LT_BEACON",
                "S_OH_EXT_LT_WING",
                "S_OH_EXT_LT_LANDING_L",
                "S_OH_EXT_LT_LANDING_R",
                "S_OH_EXT_LT_RWY_TURNOFF",
                "S_OH_EXT_LT_NOSE",
                "S_OH_EXT_LT_LANDING_BOTH"
            },

            ["Interior Lights"] = new List<string>
            {
                // Interior Lights (6 controls)
                "S_OH_INT_LT_DOME",
                "S_OH_IN_LT_ANN_LT",
                "S_OH_IN_LT_ICE",
                "A_OH_LIGHTING_READING_CAPT",
                "A_OH_LIGHTING_READING_FO",
                "A_OH_LIGHTING_OVD"
            },

            ["Flight Controls"] = new List<string>
            {
                // Flight Control Computers (7 controls)
                "S_OH_FLT_CTL_ELAC_1",
                "S_OH_FLT_CTL_ELAC_2",
                "S_OH_FLT_CTL_SEC_1",
                "S_OH_FLT_CTL_SEC_2",
                "S_OH_FLT_CTL_SEC_3",
                "S_OH_FLT_CTL_FAC_1",
                "S_OH_FLT_CTL_FAC_2"
            },

            ["Voice Recorder"] = new List<string>
            {
                // Voice Recorder Controls (3 controls)
                "S_OH_RCRD_GND_CTL",
                "S_OH_RCRD_ERASE",
                "S_OH_RCRD_TEST"
            },

            ["Cockpit Door"] = new List<string>
            {
                // Cockpit Door Controls (1 control)
                "S_OH_COCKPIT_DOOR_VIDEO"
            },

            ["Signs"] = new List<string>
            {
                // Seat Belt Signs (1 control)
                "S_OH_SIGNS",

                // No Smoking Signs (1 control)
                "S_OH_SIGNS_SMOKING",

                // Emergency Exit Lights (1 control)
                "S_OH_INT_LT_EMER"
            },

            ["Oxygen"] = new List<string>
            {
                // Oxygen Controls (4 controls)
                "S_OH_OXYGEN_CREW_OXYGEN",
                "S_OH_OXYGEN_HIGH_ALT",
                "S_OH_OXYGEN_MASK_MAN_ON",
                "S_OH_OXYGEN_TMR_RESET"
            },

            ["Evacuation"] = new List<string>
            {
                // Evacuation Controls (3 controls)
                "S_OH_EVAC_CAPT_PURSER",
                "S_OH_EVAC_COMMAND",
                "S_OH_EVAC_HORN_SHUTOFF"
            },

            ["Calls"] = new List<string>
            {
                // Call Controls (6 controls)
                "S_OH_CALLS_MECH",
                "S_OH_CALLS_ALL",
                "S_OH_CALLS_FWD",
                "S_OH_CALLS_AFT",
                "S_OH_CALLS_EMER",
                "S_OH_CALLS_EMER_Cover"
            },

            ["Wipers"] = new List<string>
            {
                // Wiper Controls (4 controls)
                "S_MISC_WIPER_CAPT",
                "S_MISC_WIPER_FO",
                "S_MISC_WIPER_REPELLENT_CAPT",
                "S_MISC_WIPER_REPELLENT_FO"
            },

            ["Cargo Smoke"] = new List<string>
            {
                // Cargo Smoke Controls (3 controls)
                "S_OH_CARGO_SMOKE_TEST",
                "S_OH_CARGO_DISC_1_OLD_LAYOUT",
                "S_OH_CARGO_DISC_2_OLD_LAYOUT"
            },

            ["GPWS"] = new List<string>
            {
                // GPWS Controls (5 controls)
                "S_OH_GPWS_TERR",
                "S_OH_GPWS_SYS",
                "S_OH_GPWS_LDG_FLAP3",
                "S_OH_GPWS_GS_MODE",
                "S_OH_GPWS_FLAP_MODE"
            },

            ["Engine"] = new List<string>
            {
                // Engine Controls (4 controls)
                "S_OH_ENG_MANSTART_1",
                "S_OH_ENG_MANSTART_2",
                "S_OH_ENG_N1_MODE_1",
                "S_OH_ENG_N1_MODE_2"
            },

            ["Maintenance"] = new List<string>
            {
                // Maintenance Controls (8 controls)
                "S_OH_AFT_FADEC_GND_1",
                "S_OH_AFT_FADEC_GND_2",
                "S_OH_ELT",
                "S_OH_ELT_TEST",
                "S_OH_APU_AUTOEXTING_RESET",
                "S_OH_APU_AUTOEXTING_TEST",
                "S_OH_SVCE_INT_OVRD",
                "S_OH_LIGHTING_AVIONICS_COMPT"
            },

            // ========== PEDESTAL PANELS ==========
            ["Engines"] = new List<string>
            {
                // Engine Controls (3 controls)
                "S_ENG_MODE",
                "S_ENG_MASTER_1",
                "S_ENG_MASTER_2"
            },

            ["Weather Radar"] = new List<string>
            {
                // Weather Radar Controls (7 controls)
                "S_WR_PRED_WS",        // PWS Switch (Combo box)
                "S_WR_SYS",            // System Switch (Combo box)
                "S_WR_GCS",            // GCS Button
                "S_WR_MULTISCAN",      // Multiscan Button
                "A_WR_TILT",           // Tilt Knob
                "A_WR_GAIN",           // Gain Knob
                "S_WR_MODE"            // Image Selector
            },

            ["ECAM"] = new List<string>
            {
                // Brightness Knobs (2 step-based combo boxes)
                "A_DISPLAY_BRIGHTNESS_ECAM_U",
                "A_DISPLAY_BRIGHTNESS_ECAM_L",

                // ECAM System Page Buttons (18 buttons)
                "S_ECAM_ENGINE",
                "S_ECAM_BLEED",
                "S_ECAM_CAB_PRESS",
                "S_ECAM_ELEC",
                "S_ECAM_HYD",
                "S_ECAM_FUEL",
                "S_ECAM_APU",
                "S_ECAM_COND",
                "S_ECAM_DOOR",
                "S_ECAM_WHEEL",
                "S_ECAM_FCTL",
                "S_ECAM_ALL",
                "S_ECAM_STATUS",
                "S_ECAM_CLR_LEFT",
                "S_ECAM_CLR_RIGHT",
                "S_ECAM_RCL",
                "S_ECAM_TO",
                "S_ECAM_EMER_CANCEL"
            },

            ["Flight Controls"] = new List<string>
            {
                "S_MIP_PARKING_BRAKE",
                "A_FC_SPEEDBRAKE",
                "S_FC_RUDDER_TRIM_LEFT",
                "S_FC_RUDDER_TRIM_RIGHT",
                "S_FC_RUDDER_TRIM_RESET",
                "A_FC_ELEVATOR_TRIM",
                "S_FC_FLAPS_LEVER",
                "A_FC_THROTTLE_LEFT_INPUT",
                "A_FC_THROTTLE_RIGHT_INPUT",
                "A_FC_THROTTLE_BOTH_INPUT",
                "S_FC_THR_INST_DISCONNECT1",
                "S_FC_THR_INST_DISCONNECT2",
                "S_FC_CAPT_INST_DISCONNECT",
                "S_FC_FO_INST_DISCONNECT"
            },

            ["ATC TCAS"] = new List<string>
            {
                // Transponder Controls (8 controls)
                "S_XPDR_MODE",             // Mode Knob (STBY/TA/TA-RA)
                "S_XPDR_OPERATION",        // Operation Knob (STBY/AUTO/ON)
                "S_XPDR_ATC",              // ATC Switch (ATC 1/ATC 2)
                "S_XPDR_ALTREPORTING",     // Altitude Reporting (Off/On)
                "S_TCAS_RANGE",            // TCAS Traffic (THRT/ALL/ABV/BLW)
                "S_XPDR_IDENT",            // IDENT Button
                "S_PED_ATC_CLR",           // CLR Button
                "TRANSPONDER_CODE_SET"     // Set Transponder Code (replaces keypad 0-7)
            },

            ["Radio Management Panel (RMP)"] = new List<string>
            {
                // RMP1 Controls (19 controls)
                "S_PED_RMP1_POWER",
                "S_PED_RMP1_VHF1",
                "S_PED_RMP1_VHF2",
                "S_PED_RMP1_VHF3",
                "S_PED_RMP1_HF1",
                "S_PED_RMP1_HF2",
                "S_PED_RMP1_NAV",
                "S_PED_RMP1_VOR",
                "S_PED_RMP1_ILS",
                "S_PED_RMP1_MLS",
                "S_PED_RMP1_ADF",
                "S_PED_RMP1_BFO",
                "S_PED_RMP1_AM",
                "E_PED_RMP1_INNER_DEC",
                "E_PED_RMP1_INNER_INC",
                "E_PED_RMP1_OUTER_DEC",
                "E_PED_RMP1_OUTER_INC",
                "S_PED_RMP1_XFER",

                // RMP2 Controls (19 controls)
                "S_PED_RMP2_POWER",
                "S_PED_RMP2_VHF1",
                "S_PED_RMP2_VHF2",
                "S_PED_RMP2_VHF3",
                "S_PED_RMP2_HF1",
                "S_PED_RMP2_HF2",
                "S_PED_RMP2_NAV",
                "S_PED_RMP2_VOR",
                "S_PED_RMP2_ILS",
                "S_PED_RMP2_MLS",
                "S_PED_RMP2_ADF",
                "S_PED_RMP2_BFO",
                "S_PED_RMP2_AM",
                "E_PED_RMP2_INNER_DEC",
                "E_PED_RMP2_INNER_INC",
                "E_PED_RMP2_OUTER_DEC",
                "E_PED_RMP2_OUTER_INC",
                "S_PED_RMP2_XFER",

                // RMP3 Controls (19 controls)
                "S_PED_RMP3_POWER",
                "S_PED_RMP3_VHF1",
                "S_PED_RMP3_VHF2",
                "S_PED_RMP3_VHF3",
                "S_PED_RMP3_HF1",
                "S_PED_RMP3_HF2",
                "S_PED_RMP3_NAV",
                "S_PED_RMP3_VOR",
                "S_PED_RMP3_ILS",
                "S_PED_RMP3_MLS",
                "S_PED_RMP3_ADF",
                "S_PED_RMP3_BFO",
                "S_PED_RMP3_AM",
                "E_PED_RMP3_INNER_DEC",
                "E_PED_RMP3_INNER_INC",
                "E_PED_RMP3_OUTER_DEC",
                "E_PED_RMP3_OUTER_INC",
                "S_PED_RMP3_XFER"
            },

            ["Audio Control Panel (ACP)"] = new List<string>
            {
                // Volume Controls (13 knobs)
                "A_ASP_VHF_1_VOLUME",
                "A_ASP_VHF_2_VOLUME",
                "A_ASP_VHF_3_VOLUME",
                "A_ASP_HF_1_VOLUME",
                "A_ASP_HF_2_VOLUME",
                "A_ASP_CAB_VOLUME",
                "A_ASP_PA_VOLUME",
                "A_ASP_INT_VOLUME",
                "A_ASP_ILS_VOLUME",
                "A_ASP_MLS_VOLUME",
                "A_ASP_ADF_1_VOLUME",
                "A_ASP_ADF_2_VOLUME",
                "A_ASP_MARKER_VOLUME",

                // INTRAD Switch
                "S_ASP_INTRAD"
            },

            // ========== MAIN INSTRUMENT PANEL ==========
            ["Auto Brakes"] = new List<string>
            {
                "S_MIP_AUTOBRAKE_LO",
                "S_MIP_AUTOBRAKE_MED",
                "S_MIP_AUTOBRAKE_MAX",
                "S_MIP_BRAKE_FAN"
            },

            ["Landing Gear"] = new List<string>
            {
                "S_MIP_GEAR",
                "S_FC_MIP_ANTI_SKID"
            },

            ["Console Floor Lights"] = new List<string>
            {
                "S_MIP_LIGHT_CONSOLEFLOOR_CAPT",
                "S_MIP_LIGHT_CONSOLEFLOOR_FO"
            },

            ["ISIS"] = new List<string>
            {
                "S_MIP_ISFD_BUGS",
                "S_MIP_ISFD_LS",
                "S_MIP_ISFD_PLUS",
                "S_MIP_ISFD_MINUS",
                "S_MIP_ISFD_RST",
                "S_MIP_ISFD_BARO_BUTTON"
            },

            ["GPWS/Terrain"] = new List<string>
            {
                "S_MIP_GPWS_VISUAL_ALERT_CAPT",
                "S_MIP_GPWS_VISUAL_ALERT_FO",
                "S_MIP_GPWS_TERRAIN_ON_ND_CAPT",
                "S_MIP_GPWS_TERRAIN_ON_ND_FO"
            },

            ["Warnings/Messages"] = new List<string>
            {
                "S_MIP_MASTER_WARNING_CAPT",
                "S_MIP_MASTER_WARNING_FO",
                "S_MIP_MASTER_CAUTION_CAPT",
                "S_MIP_MASTER_CAUTION_FO",
                "S_MIP_ATC_MSG_CAPT",
                "S_MIP_ATC_MSG_FO",
                "S_MIP_CHRONO_CAPT",
                "S_MIP_CHRONO_FO"
            },

            ["Autoland"] = new List<string>
            {
                "S_MIP_AUTOLAND_CAPT",
                "S_MIP_AUTOLAND_FO"
            },

            ["Main Instrument Lights"] = new List<string>
            {
                "A_MIP_LIGHTING_MAP_L",
                "A_MIP_LIGHTING_MAP_R",
                "A_MIP_LIGHTING_FLOOD_MAIN",
                "A_MIP_LIGHTING_FLOOD_PEDESTAL"
            },

            ["Audio"] = new List<string>
            {
                "A_MIP_LOUDSPEAKER_CAPT",
                "A_MIP_LOUDSPEAKER_FO"
            }
        };
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        return new Dictionary<string, List<string>>
        {
            // Display-only variables can be added here as needed
        };
    }

    public override Dictionary<string, string> GetButtonStateMapping()
    {
        return new Dictionary<string, string>
        {
            // Button-to-state mappings will be added here
        };
    }

    /// <summary>
    /// Handle UI variable setting for Fenix A320 electrical panel controls.
    /// - Batteries: Use SetLVar (direct SimConnect)
    /// - Buttons: Use ExecuteButtonTransition (0→1 transition via SetLVar)
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        try
        {
            // ========== BATTERY SWITCHES (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_ELEC_BAT1")
            {
                simConnect.SetLVar("S_OH_ELEC_BAT1", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_BAT2")
            {
                simConnect.SetLVar("S_OH_ELEC_BAT2", value);
                return true;
            }

            // ========== ELECTRICAL PANEL CONTROLS ==========
            // These work like batteries - combo boxes with Off (0) / On (1) states
            // External Power is the only button (uses ExecuteButtonTransition)

            if (varKey == "S_OH_ELEC_GEN1")
            {
                simConnect.SetLVar("S_OH_ELEC_GEN1_LINE", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_GEN2")
            {
                simConnect.SetLVar("S_OH_ELEC_GEN2", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_EXT_PWR" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ELEC_EXT_PWR", "External Power", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ELEC_APU_GEN")
            {
                simConnect.SetLVar("S_OH_ELEC_APU_GENERATOR", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_BUS_TIE")
            {
                simConnect.SetLVar("S_OH_ELEC_BUSTIE", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_AC_ESS_FEED")
            {
                simConnect.SetLVar("S_OH_ELEC_AC_ESS_FEED", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_IDG1")
            {
                simConnect.SetLVar("S_OH_ELEC_IDG1", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_IDG2")
            {
                simConnect.SetLVar("S_OH_ELEC_IDG2", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_GALY")
            {
                simConnect.SetLVar("S_OH_ELEC_GALY", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_COMMERCIAL")
            {
                simConnect.SetLVar("S_OH_ELEC_COMMERCIAL", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_APU_MASTER")
            {
                simConnect.SetLVar("S_OH_ELEC_APU_MASTER", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_APU_START")
            {
                simConnect.SetLVar("S_OH_ELEC_APU_START", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_GEN1_LINE")
            {
                simConnect.SetLVar("S_OH_ELEC_GEN1_LINE", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_EMER_GEN_TEST")
            {
                simConnect.SetLVar("S_OH_ELEC_EMER_GEN_TEST", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_EMER_GEN_MAN_ON")
            {
                simConnect.SetLVar("S_OH_ELEC_EMER_GEN_MAN_ON", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_EMER_GEN_MAN_ON_Cover")
            {
                simConnect.SetLVar("S_OH_ELEC_EMER_GEN_MAN_ON_Cover", value);
                return true;
            }

            if (varKey == "S_OH_ELEC_EMER_GEN_TEST_Cover")
            {
                simConnect.SetLVar("S_OH_ELEC_EMER_GEN_TEST_Cover", value);
                return true;
            }

            // ========== ADIRS PANEL CONTROLS ==========

            // IR Mode Knobs
            if (varKey == "S_OH_NAV_IR1_MODE")
            {
                simConnect.SetLVar("S_OH_NAV_IR1_MODE", value);
                return true;
            }

            if (varKey == "S_OH_NAV_IR2_MODE")
            {
                simConnect.SetLVar("S_OH_NAV_IR2_MODE", value);
                return true;
            }

            if (varKey == "S_OH_NAV_IR3_MODE")
            {
                simConnect.SetLVar("S_OH_NAV_IR3_MODE", value);
                return true;
            }

            // ADR Buttons
            if (varKey == "S_OH_NAV_ADR1" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_ADR1", "ADR 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_NAV_ADR2" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_ADR2", "ADR 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_NAV_ADR3" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_ADR3", "ADR 3", simConnect, announcer);
                return true;
            }

            // IR Push Buttons
            if (varKey == "S_OH_NAV_IR1_SWITCH" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_IR1_SWITCH", "IR 1 Push", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_NAV_IR2_SWITCH" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_IR2_SWITCH", "IR 2 Push", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_NAV_IR3_SWITCH" && value == 1)
            {
                ExecuteButtonTransition("S_OH_NAV_IR3_SWITCH", "IR 3 Push", simConnect, announcer);
                return true;
            }

            // Display Selectors
            if (varKey == "S_OH_NAV_DATA_DISP")
            {
                simConnect.SetLVar("S_OH_NAV_DATA_DISP", value);
                return true;
            }

            if (varKey == "S_OH_NAV_SYS_DISP")
            {
                simConnect.SetLVar("S_OH_NAV_SYS_DISP", value);
                return true;
            }

            // Keypad Buttons
            if (varKey == "S_OH_ADIRS_KEY_0" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_0", "Key 0", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_1" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_1", "Key 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_2" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_2", "Key 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_3" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_3", "Key 3", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_4" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_4", "Key 4", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_5" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_5", "Key 5", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_6" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_6", "Key 6", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_7" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_7", "Key 7", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_8" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_8", "Key 8", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_9" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_9", "Key 9", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_CLR" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_CLR", "Key Clear", simConnect, announcer);
                return true;
            }

            if (varKey == "S_OH_ADIRS_KEY_ENT" && value == 1)
            {
                ExecuteButtonTransition("S_OH_ADIRS_KEY_ENT", "Key Enter", simConnect, announcer);
                return true;
            }

            // ========== AIR CONDITIONING AND PRESSURIZATION PANEL CONTROLS ==========

            // Bleed Buttons
            if (varKey == "S_OH_PNEUMATIC_APU_BLEED")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_APU_BLEED", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_ENG1_BLEED")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_ENG1_BLEED", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_ENG2_BLEED")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_ENG2_BLEED", value);
                return true;
            }

            // Pack Buttons
            if (varKey == "S_OH_PNEUMATIC_PACK_1")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_PACK_1", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_PACK_2")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_PACK_2", value);
                return true;
            }

            // Air Buttons
            if (varKey == "S_OH_PNEUMATIC_HOT_AIR")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_HOT_AIR", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_RAM_AIR")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_RAM_AIR", value);
                return true;
            }

            // Pressurization
            if (varKey == "S_OH_PNEUMATIC_DITCHING")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_DITCHING", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_PRESS_MODE")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_PRESS_MODE", value);
                return true;
            }

            if (varKey == "A_OH_PNEUMATIC_LDG_ELEV")
            {
                simConnect.SetLVar("A_OH_PNEUMATIC_LDG_ELEV", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_PRESS_MAN")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_PRESS_MAN", value);
                return true;
            }

            // Ventilation Buttons
            if (varKey == "S_OH_PNEUMATIC_BLOWER")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_BLOWER", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_EXTRACT")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_EXTRACT", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_CAB_FANS")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_CAB_FANS", value);
                return true;
            }

            // Selectors
            if (varKey == "S_OH_PNEUMATIC_XBLEED_SELECTOR")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_XBLEED_SELECTOR", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_PACK_FLOW")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_PACK_FLOW", value);
                return true;
            }

            // Temperature Controls
            if (varKey == "A_OH_PNEUMATIC_COCKPIT_TEMP")
            {
                simConnect.SetLVar("A_OH_PNEUMATIC_COCKPIT_TEMP", value);
                return true;
            }

            if (varKey == "A_OH_PNEUMATIC_FWD_TEMP")
            {
                simConnect.SetLVar("A_OH_PNEUMATIC_FWD_TEMP", value);
                return true;
            }

            if (varKey == "A_OH_PNEUMATIC_AFT_TEMP")
            {
                simConnect.SetLVar("A_OH_PNEUMATIC_AFT_TEMP", value);
                return true;
            }

            // Cargo Controls
            if (varKey == "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE", value);
                return true;
            }

            // ========== FIRE PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // Main Fire Push Buttons
            if (varKey == "S_OH_FIRE_ENG1_BUTTON")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG1_BUTTON", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_ENG2_BUTTON")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG2_BUTTON", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_APU_BUTTON")
            {
                simConnect.SetLVar("S_OH_FIRE_APU_BUTTON", value);
                return true;
            }

            // Fire Test Buttons
            if (varKey == "S_OH_FIRE_ENG1_TEST")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG1_TEST", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_ENG2_TEST")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG2_TEST", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_APU_TEST")
            {
                simConnect.SetLVar("S_OH_FIRE_APU_TEST", value);
                return true;
            }

            // Agent Discharge Buttons
            if (varKey == "S_OH_FIRE_ENG1_AGENT1")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG1_AGENT1", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_ENG1_AGENT2")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG1_AGENT2", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_ENG2_AGENT1")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG2_AGENT1", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_ENG2_AGENT2")
            {
                simConnect.SetLVar("S_OH_FIRE_ENG2_AGENT2", value);
                return true;
            }

            if (varKey == "S_OH_FIRE_APU_AGENT")
            {
                simConnect.SetLVar("S_OH_FIRE_APU_AGENT", value);
                return true;
            }

            // ========== HYDRAULIC PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // Engine Pumps
            if (varKey == "S_OH_HYD_ENG_1_PUMP")
            {
                simConnect.SetLVar("S_OH_HYD_ENG_1_PUMP", value);
                return true;
            }

            if (varKey == "S_OH_HYD_ENG_2_PUMP")
            {
                simConnect.SetLVar("S_OH_HYD_ENG_2_PUMP", value);
                return true;
            }

            // Electric Pumps
            if (varKey == "S_OH_HYD_BLUE_ELEC_PUMP")
            {
                simConnect.SetLVar("S_OH_HYD_BLUE_ELEC_PUMP", value);
                return true;
            }

            if (varKey == "S_OH_HYD_YELLOW_ELEC_PUMP" && value == 1)
            {
                ExecuteButtonTransition("S_OH_HYD_YELLOW_ELEC_PUMP", "Yellow Electric Pump", simConnect, announcer);
                return true;
            }

            // PTU and RAT
            if (varKey == "S_OH_HYD_PTU")
            {
                simConnect.SetLVar("S_OH_HYD_PTU", value);
                return true;
            }

            if (varKey == "S_OH_HYD_RAT_MAN_ON")
            {
                simConnect.SetLVar("S_OH_HYD_RAT_MAN_ON", value);
                return true;
            }

            // Low Mechanical Valves
            if (varKey == "S_OH_HYD_LMV_YELLOW")
            {
                simConnect.SetLVar("S_OH_HYD_LMV_YELLOW", value);
                return true;
            }

            if (varKey == "S_OH_HYD_LMV_GREEN")
            {
                simConnect.SetLVar("S_OH_HYD_LMV_GREEN", value);
                return true;
            }

            if (varKey == "S_OH_HYD_LMV_BLUE")
            {
                simConnect.SetLVar("S_OH_HYD_LMV_BLUE", value);
                return true;
            }

            if (varKey == "S_OH_HYD_BLUE_PUMP_OVERRIDE")
            {
                simConnect.SetLVar("S_OH_HYD_BLUE_PUMP_OVERRIDE", value);
                return true;
            }

            // ========== FUEL PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // Left Wing Tank Pumps
            if (varKey == "S_OH_FUEL_LEFT_1")
            {
                simConnect.SetLVar("S_OH_FUEL_LEFT_1", value);
                return true;
            }

            if (varKey == "S_OH_FUEL_LEFT_2")
            {
                simConnect.SetLVar("S_OH_FUEL_LEFT_2", value);
                return true;
            }

            // Center Tank Pumps
            if (varKey == "S_OH_FUEL_CENTER_1")
            {
                simConnect.SetLVar("S_OH_FUEL_CENTER_1", value);
                return true;
            }

            if (varKey == "S_OH_FUEL_CENTER_2")
            {
                simConnect.SetLVar("S_OH_FUEL_CENTER_2", value);
                return true;
            }

            // Right Wing Tank Pumps
            if (varKey == "S_OH_FUEL_RIGHT_1")
            {
                simConnect.SetLVar("S_OH_FUEL_RIGHT_1", value);
                return true;
            }

            if (varKey == "S_OH_FUEL_RIGHT_2")
            {
                simConnect.SetLVar("S_OH_FUEL_RIGHT_2", value);
                return true;
            }

            // Crossfeed and Mode
            if (varKey == "S_OH_FUEL_XFEED")
            {
                simConnect.SetLVar("S_OH_FUEL_XFEED", value);
                return true;
            }

            if (varKey == "S_OH_FUEL_MODE_SEL")
            {
                simConnect.SetLVar("S_OH_FUEL_MODE_SEL", value);
                return true;
            }

            // ========== ANTI-ICE PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // Engine Anti-Ice
            if (varKey == "S_OH_PNEUMATIC_ENG1_ANTI_ICE")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_ENG1_ANTI_ICE", value);
                return true;
            }

            if (varKey == "S_OH_PNEUMATIC_ENG2_ANTI_ICE")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_ENG2_ANTI_ICE", value);
                return true;
            }

            // Wing Anti-Ice
            if (varKey == "S_OH_PNEUMATIC_WING_ANTI_ICE")
            {
                simConnect.SetLVar("S_OH_PNEUMATIC_WING_ANTI_ICE", value);
                return true;
            }

            // Probe Heat
            if (varKey == "S_OH_PROBE_HEAT")
            {
                simConnect.SetLVar("S_OH_PROBE_HEAT", value);
                return true;
            }

            // ========== MAIN INSTRUMENT PANEL CONTROLS ==========
            // Auto Brakes - 3 momentary push buttons (use ExecuteButtonTransition)
            if (varKey == "S_MIP_AUTOBRAKE_LO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_AUTOBRAKE_LO", "Autobrake Low", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_AUTOBRAKE_MED" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_AUTOBRAKE_MED", "Autobrake Medium", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_AUTOBRAKE_MAX" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_AUTOBRAKE_MAX", "Autobrake Max", simConnect, announcer);
                return true;
            }

            // Landing Gear - Lever control (combo box, use SetLVar)
            if (varKey == "S_MIP_GEAR")
            {
                simConnect.SetLVar("S_MIP_GEAR", value);
                return true;
            }

            // Brake Fan - Combo box control
            if (varKey == "S_MIP_BRAKE_FAN")
            {
                simConnect.SetLVar("S_MIP_BRAKE_FAN", value);
                return true;
            }

            // Anti-Skid - Combo box control
            if (varKey == "S_FC_MIP_ANTI_SKID")
            {
                simConnect.SetLVar("S_FC_MIP_ANTI_SKID", value);
                return true;
            }

            // Console Floor Lights - Combo box controls
            if (varKey == "S_MIP_LIGHT_CONSOLEFLOOR_CAPT")
            {
                simConnect.SetLVar("S_MIP_LIGHT_CONSOLEFLOOR_CAPT", value);
                return true;
            }

            if (varKey == "S_MIP_LIGHT_CONSOLEFLOOR_FO")
            {
                simConnect.SetLVar("S_MIP_LIGHT_CONSOLEFLOOR_FO", value);
                return true;
            }

            // ISIS (Standby Instrument) - 6 momentary push buttons
            if (varKey == "S_MIP_ISFD_BUGS" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_BUGS", "ISIS Bugs", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ISFD_LS" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_LS", "ISIS Localizer", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ISFD_PLUS" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_PLUS", "ISIS Plus", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ISFD_MINUS" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_MINUS", "ISIS Minus", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ISFD_RST" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_RST", "ISIS Reset", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ISFD_BARO_BUTTON" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ISFD_BARO_BUTTON", "ISIS Barometric Pressure", simConnect, announcer);
                return true;
            }

            // ========== GPWS/TERRAIN PANEL CONTROLS ==========
            if (varKey == "S_MIP_GPWS_VISUAL_ALERT_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_GPWS_VISUAL_ALERT_CAPT", "GPWS GS Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_GPWS_VISUAL_ALERT_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_GPWS_VISUAL_ALERT_FO", "GPWS GS First Officer", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_GPWS_TERRAIN_ON_ND_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_GPWS_TERRAIN_ON_ND_CAPT", "Terrain on ND Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_GPWS_TERRAIN_ON_ND_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_GPWS_TERRAIN_ON_ND_FO", "Terrain on ND First Officer", simConnect, announcer);
                return true;
            }

            // ========== WARNINGS/MESSAGES PANEL CONTROLS ==========
            if (varKey == "S_MIP_MASTER_WARNING_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_MASTER_WARNING_CAPT", "Master Warning Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_MASTER_WARNING_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_MASTER_WARNING_FO", "Master Warning First Officer", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_MASTER_CAUTION_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_MASTER_CAUTION_CAPT", "Master Caution Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_MASTER_CAUTION_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_MASTER_CAUTION_FO", "Master Caution First Officer", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ATC_MSG_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ATC_MSG_CAPT", "ATC Message Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_ATC_MSG_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_ATC_MSG_FO", "ATC Message First Officer", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_CHRONO_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_CHRONO_CAPT", "Chronometer Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_CHRONO_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_CHRONO_FO", "Chronometer First Officer", simConnect, announcer);
                return true;
            }

            // ========== AUTOLAND PANEL CONTROLS ==========
            if (varKey == "S_MIP_AUTOLAND_CAPT" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_AUTOLAND_CAPT", "Autoland Captain", simConnect, announcer);
                return true;
            }

            if (varKey == "S_MIP_AUTOLAND_FO" && value == 1)
            {
                ExecuteButtonTransition("S_MIP_AUTOLAND_FO", "Autoland First Officer", simConnect, announcer);
                return true;
            }

            // ========== MAIN INSTRUMENT LIGHTS PANEL CONTROLS ==========
            if (varKey == "A_MIP_LIGHTING_MAP_L")
            {
                simConnect.SetLVar("A_MIP_LIGHTING_MAP_L", value);
                return true;
            }

            if (varKey == "A_MIP_LIGHTING_MAP_R")
            {
                simConnect.SetLVar("A_MIP_LIGHTING_MAP_R", value);
                return true;
            }

            if (varKey == "A_MIP_LIGHTING_FLOOD_MAIN")
            {
                simConnect.SetLVar("A_MIP_LIGHTING_FLOOD_MAIN", value);
                return true;
            }

            if (varKey == "A_MIP_LIGHTING_FLOOD_PEDESTAL")
            {
                simConnect.SetLVar("A_MIP_LIGHTING_FLOOD_PEDESTAL", value);
                return true;
            }

            // ========== AUDIO PANEL CONTROLS ==========
            if (varKey == "A_MIP_LOUDSPEAKER_CAPT")
            {
                simConnect.SetLVar("A_MIP_LOUDSPEAKER_CAPT", value);
                return true;
            }

            if (varKey == "A_MIP_LOUDSPEAKER_FO")
            {
                simConnect.SetLVar("A_MIP_LOUDSPEAKER_FO", value);
                return true;
            }

            // ========== EXTERNAL LIGHTS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // NAV & LOGO
            if (varKey == "S_OH_EXT_LT_NAV_LOGO")
            {
                simConnect.SetLVar("S_OH_EXT_LT_NAV_LOGO", value);
                return true;
            }

            // STROBE
            if (varKey == "S_OH_EXT_LT_STROBE")
            {
                simConnect.SetLVar("S_OH_EXT_LT_STROBE", value);
                return true;
            }

            // BEACON
            if (varKey == "S_OH_EXT_LT_BEACON")
            {
                simConnect.SetLVar("S_OH_EXT_LT_BEACON", value);
                return true;
            }

            // WING
            if (varKey == "S_OH_EXT_LT_WING")
            {
                simConnect.SetLVar("S_OH_EXT_LT_WING", value);
                return true;
            }

            // LANDING LEFT
            if (varKey == "S_OH_EXT_LT_LANDING_L")
            {
                simConnect.SetLVar("S_OH_EXT_LT_LANDING_L", value);
                return true;
            }

            // LANDING RIGHT
            if (varKey == "S_OH_EXT_LT_LANDING_R")
            {
                simConnect.SetLVar("S_OH_EXT_LT_LANDING_R", value);
                return true;
            }

            // LANDING BOTH
            if (varKey == "S_OH_EXT_LT_LANDING_BOTH")
            {
                simConnect.SetLVar("S_OH_EXT_LT_LANDING_BOTH", value);
                return true;
            }

            // RWY TURN OFF
            if (varKey == "S_OH_EXT_LT_RWY_TURNOFF")
            {
                simConnect.SetLVar("S_OH_EXT_LT_RWY_TURNOFF", value);
                return true;
            }

            // NOSE
            if (varKey == "S_OH_EXT_LT_NOSE")
            {
                simConnect.SetLVar("S_OH_EXT_LT_NOSE", value);
                return true;
            }

            // ========== INTERIOR LIGHTS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // DOME
            if (varKey == "S_OH_INT_LT_DOME")
            {
                simConnect.SetLVar("S_OH_INT_LT_DOME", value);
                return true;
            }

            // ANNUNCIATOR
            if (varKey == "S_OH_IN_LT_ANN_LT")
            {
                simConnect.SetLVar("S_OH_IN_LT_ANN_LT", value);
                return true;
            }

            // ICE STANDBY
            if (varKey == "S_OH_IN_LT_ICE")
            {
                simConnect.SetLVar("S_OH_IN_LT_ICE", value);
                return true;
            }

            // CAPTAIN READING
            if (varKey == "A_OH_LIGHTING_READING_CAPT")
            {
                simConnect.SetLVar("A_OH_LIGHTING_READING_CAPT", value);
                return true;
            }

            // FO READING
            if (varKey == "A_OH_LIGHTING_READING_FO")
            {
                simConnect.SetLVar("A_OH_LIGHTING_READING_FO", value);
                return true;
            }

            // OVERHEAD INTEGRAL
            if (varKey == "A_OH_LIGHTING_OVD")
            {
                simConnect.SetLVar("A_OH_LIGHTING_OVD", value);
                return true;
            }

            // ========== FLIGHT CONTROLS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // ELAC 1
            if (varKey == "S_OH_FLT_CTL_ELAC_1")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_ELAC_1", value);
                return true;
            }

            // ELAC 2
            if (varKey == "S_OH_FLT_CTL_ELAC_2")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_ELAC_2", value);
                return true;
            }

            // SEC 1
            if (varKey == "S_OH_FLT_CTL_SEC_1")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_SEC_1", value);
                return true;
            }

            // SEC 2
            if (varKey == "S_OH_FLT_CTL_SEC_2")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_SEC_2", value);
                return true;
            }

            // SEC 3
            if (varKey == "S_OH_FLT_CTL_SEC_3")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_SEC_3", value);
                return true;
            }

            // FAC 1
            if (varKey == "S_OH_FLT_CTL_FAC_1")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_FAC_1", value);
                return true;
            }

            // FAC 2
            if (varKey == "S_OH_FLT_CTL_FAC_2")
            {
                simConnect.SetLVar("S_OH_FLT_CTL_FAC_2", value);
                return true;
            }

            // ========== VOICE RECORDER PANEL CONTROLS ==========
            // GND CTL (Button - uses ExecuteButtonTransition)
            if (varKey == "S_OH_RCRD_GND_CTL" && value == 1)
            {
                ExecuteButtonTransition("S_OH_RCRD_GND_CTL", "GND CTL", simConnect, announcer);
                return true;
            }

            // CVR ERASE
            if (varKey == "S_OH_RCRD_ERASE")
            {
                simConnect.SetLVar("S_OH_RCRD_ERASE", value);
                return true;
            }

            // CVR TEST
            if (varKey == "S_OH_RCRD_TEST")
            {
                simConnect.SetLVar("S_OH_RCRD_TEST", value);
                return true;
            }

            // ========== COCKPIT DOOR PANEL CONTROLS ==========
            // VIDEO
            if (varKey == "S_OH_COCKPIT_DOOR_VIDEO")
            {
                simConnect.SetLVar("S_OH_COCKPIT_DOOR_VIDEO", value);
                return true;
            }

            // ========== SIGNS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            // Seat Belt Signs
            if (varKey == "S_OH_SIGNS")
            {
                simConnect.SetLVar("S_OH_SIGNS", value);
                return true;
            }

            // No Smoking Signs
            if (varKey == "S_OH_SIGNS_SMOKING")
            {
                simConnect.SetLVar("S_OH_SIGNS_SMOKING", value);
                return true;
            }

            // Emergency Exit Lights
            if (varKey == "S_OH_INT_LT_EMER")
            {
                simConnect.SetLVar("S_OH_INT_LT_EMER", value);
                return true;
            }

            // ========== OXYGEN PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_OXYGEN_CREW_OXYGEN")
            {
                simConnect.SetLVar("S_OH_OXYGEN_CREW_OXYGEN", value);
                return true;
            }

            if (varKey == "S_OH_OXYGEN_HIGH_ALT")
            {
                simConnect.SetLVar("S_OH_OXYGEN_HIGH_ALT", value);
                return true;
            }

            if (varKey == "S_OH_OXYGEN_MASK_MAN_ON")
            {
                simConnect.SetLVar("S_OH_OXYGEN_MASK_MAN_ON", value);
                return true;
            }

            if (varKey == "S_OH_OXYGEN_TMR_RESET")
            {
                simConnect.SetLVar("S_OH_OXYGEN_TMR_RESET", value);
                return true;
            }

            // ========== EVACUATION PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_EVAC_CAPT_PURSER")
            {
                simConnect.SetLVar("S_OH_EVAC_CAPT_PURSER", value);
                return true;
            }

            if (varKey == "S_OH_EVAC_COMMAND")
            {
                simConnect.SetLVar("S_OH_EVAC_COMMAND", value);
                return true;
            }

            if (varKey == "S_OH_EVAC_HORN_SHUTOFF")
            {
                simConnect.SetLVar("S_OH_EVAC_HORN_SHUTOFF", value);
                return true;
            }

            // ========== CALLS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_CALLS_MECH")
            {
                simConnect.SetLVar("S_OH_CALLS_MECH", value);
                return true;
            }

            if (varKey == "S_OH_CALLS_ALL")
            {
                simConnect.SetLVar("S_OH_CALLS_ALL", value);
                return true;
            }

            if (varKey == "S_OH_CALLS_FWD")
            {
                simConnect.SetLVar("S_OH_CALLS_FWD", value);
                return true;
            }

            if (varKey == "S_OH_CALLS_AFT")
            {
                simConnect.SetLVar("S_OH_CALLS_AFT", value);
                return true;
            }

            if (varKey == "S_OH_CALLS_EMER")
            {
                simConnect.SetLVar("S_OH_CALLS_EMER", value);
                return true;
            }

            if (varKey == "S_OH_CALLS_EMER_Cover")
            {
                simConnect.SetLVar("S_OH_CALLS_EMER_Cover", value);
                return true;
            }

            // ========== WIPERS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_MISC_WIPER_CAPT")
            {
                simConnect.SetLVar("S_MISC_WIPER_CAPT", value);
                return true;
            }

            if (varKey == "S_MISC_WIPER_FO")
            {
                simConnect.SetLVar("S_MISC_WIPER_FO", value);
                return true;
            }

            if (varKey == "S_MISC_WIPER_REPELLENT_CAPT")
            {
                simConnect.SetLVar("S_MISC_WIPER_REPELLENT_CAPT", value);
                return true;
            }

            if (varKey == "S_MISC_WIPER_REPELLENT_FO")
            {
                simConnect.SetLVar("S_MISC_WIPER_REPELLENT_FO", value);
                return true;
            }

            // ========== CARGO SMOKE PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_CARGO_SMOKE_TEST")
            {
                simConnect.SetLVar("S_OH_CARGO_SMOKE_TEST", value);
                return true;
            }

            if (varKey == "S_OH_CARGO_DISC_1_OLD_LAYOUT")
            {
                simConnect.SetLVar("S_OH_CARGO_DISC_1_OLD_LAYOUT", value);
                return true;
            }

            if (varKey == "S_OH_CARGO_DISC_2_OLD_LAYOUT")
            {
                simConnect.SetLVar("S_OH_CARGO_DISC_2_OLD_LAYOUT", value);
                return true;
            }

            // ========== GPWS PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_GPWS_TERR")
            {
                simConnect.SetLVar("S_OH_GPWS_TERR", value);
                return true;
            }

            if (varKey == "S_OH_GPWS_SYS")
            {
                simConnect.SetLVar("S_OH_GPWS_SYS", value);
                return true;
            }

            if (varKey == "S_OH_GPWS_LDG_FLAP3")
            {
                simConnect.SetLVar("S_OH_GPWS_LDG_FLAP3", value);
                return true;
            }

            if (varKey == "S_OH_GPWS_GS_MODE")
            {
                simConnect.SetLVar("S_OH_GPWS_GS_MODE", value);
                return true;
            }

            if (varKey == "S_OH_GPWS_FLAP_MODE")
            {
                simConnect.SetLVar("S_OH_GPWS_FLAP_MODE", value);
                return true;
            }

            // ========== ENGINE PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_ENG_MANSTART_1")
            {
                simConnect.SetLVar("S_OH_ENG_MANSTART_1", value);
                return true;
            }

            if (varKey == "S_OH_ENG_MANSTART_2")
            {
                simConnect.SetLVar("S_OH_ENG_MANSTART_2", value);
                return true;
            }

            if (varKey == "S_OH_ENG_N1_MODE_1")
            {
                simConnect.SetLVar("S_OH_ENG_N1_MODE_1", value);
                return true;
            }

            if (varKey == "S_OH_ENG_N1_MODE_2")
            {
                simConnect.SetLVar("S_OH_ENG_N1_MODE_2", value);
                return true;
            }

            // ========== MAINTENANCE PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_OH_AFT_FADEC_GND_1")
            {
                simConnect.SetLVar("S_OH_AFT_FADEC_GND_1", value);
                return true;
            }

            if (varKey == "S_OH_AFT_FADEC_GND_2")
            {
                simConnect.SetLVar("S_OH_AFT_FADEC_GND_2", value);
                return true;
            }

            if (varKey == "S_OH_ELT")
            {
                simConnect.SetLVar("S_OH_ELT", value);
                return true;
            }

            if (varKey == "S_OH_ELT_TEST")
            {
                simConnect.SetLVar("S_OH_ELT_TEST", value);
                return true;
            }

            if (varKey == "S_OH_APU_AUTOEXTING_RESET")
            {
                simConnect.SetLVar("S_OH_APU_AUTOEXTING_RESET", value);
                return true;
            }

            if (varKey == "S_OH_APU_AUTOEXTING_TEST")
            {
                simConnect.SetLVar("S_OH_APU_AUTOEXTING_TEST", value);
                return true;
            }

            if (varKey == "S_OH_SVCE_INT_OVRD")
            {
                simConnect.SetLVar("S_OH_SVCE_INT_OVRD", value);
                return true;
            }

            if (varKey == "S_OH_LIGHTING_AVIONICS_COMPT")
            {
                simConnect.SetLVar("S_OH_LIGHTING_AVIONICS_COMPT", value);
                return true;
            }

            // ========== PEDESTAL - ENGINES PANEL CONTROLS (Combo Boxes - use SetLVar) ==========
            if (varKey == "S_ENG_MODE")
            {
                simConnect.SetLVar("S_ENG_MODE", value);
                return true;
            }

            if (varKey == "S_ENG_MASTER_1")
            {
                simConnect.SetLVar("S_ENG_MASTER_1", value);
                return true;
            }

            if (varKey == "S_ENG_MASTER_2")
            {
                simConnect.SetLVar("S_ENG_MASTER_2", value);
                return true;
            }

            // ========== PEDESTAL - WEATHER RADAR PANEL CONTROLS ==========
            // PWS Switch (Combo Box - use SetLVar)
            if (varKey == "S_WR_PRED_WS")
            {
                simConnect.SetLVar("S_WR_PRED_WS", value);
                return true;
            }

            // System Switch (Combo Box - use SetLVar)
            if (varKey == "S_WR_SYS")
            {
                simConnect.SetLVar("S_WR_SYS", value);
                return true;
            }

            // GCS Switch (Combo Box - use SetLVar)
            if (varKey == "S_WR_GCS")
            {
                simConnect.SetLVar("S_WR_GCS", value);
                return true;
            }

            // Multiscan Switch (Combo Box - use SetLVar)
            if (varKey == "S_WR_MULTISCAN")
            {
                simConnect.SetLVar("S_WR_MULTISCAN", value);
                return true;
            }

            // Tilt Knob (Combo Box - use SetLVar)
            if (varKey == "A_WR_TILT")
            {
                simConnect.SetLVar("A_WR_TILT", value);
                return true;
            }

            // Gain Knob (Combo Box - use SetLVar)
            if (varKey == "A_WR_GAIN")
            {
                simConnect.SetLVar("A_WR_GAIN", value);
                return true;
            }

            // Image Selector (Combo Box - use SetLVar)
            if (varKey == "S_WR_MODE")
            {
                simConnect.SetLVar("S_WR_MODE", value);
                return true;
            }

            // ========== PEDESTAL - ECAM PANEL CONTROLS ==========
            // Brightness Knobs (Combo Boxes - use SetLVar)
            if (varKey == "A_DISPLAY_BRIGHTNESS_ECAM_U")
            {
                simConnect.SetLVar("A_DISPLAY_BRIGHTNESS_ECAM_U", value);
                return true;
            }

            if (varKey == "A_DISPLAY_BRIGHTNESS_ECAM_L")
            {
                simConnect.SetLVar("A_DISPLAY_BRIGHTNESS_ECAM_L", value);
                return true;
            }

            // ECAM System Page Buttons (use ExecuteButtonTransition)
            if (varKey == "S_ECAM_ENGINE" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_ENGINE", "ECAM ENG", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_BLEED" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_BLEED", "ECAM BLEED", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_CAB_PRESS" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_CAB_PRESS", "ECAM PRESS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_ELEC" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_ELEC", "ECAM ELEC", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_HYD" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_HYD", "ECAM HYD", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_FUEL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_FUEL", "ECAM FUEL", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_APU" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_APU", "ECAM APU", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_COND" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_COND", "ECAM COND", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_DOOR" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_DOOR", "ECAM DOOR", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_WHEEL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_WHEEL", "ECAM WHEEL", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_FCTL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_FCTL", "ECAM F/CTL", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_ALL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_ALL", "ECAM ALL", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_STATUS" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_STATUS", "ECAM STS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_CLR_LEFT" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_CLR_LEFT", "ECAM CLR Left", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_CLR_RIGHT" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_CLR_RIGHT", "ECAM CLR Right", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_RCL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_RCL", "ECAM RCL", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_TO" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_TO", "ECAM TO CONFIG", simConnect, announcer);
                return true;
            }

            if (varKey == "S_ECAM_EMER_CANCEL" && value == 1)
            {
                ExecuteButtonTransition("S_ECAM_EMER_CANCEL", "ECAM EMER CANC", simConnect, announcer);
                return true;
            }

            // ========== PEDESTAL - ATC TCAS PANEL CONTROLS ==========

            // Transponder Mode Knob (Combo Box - use SetLVar)
            if (varKey == "S_XPDR_MODE")
            {
                simConnect.SetLVar("S_XPDR_MODE", value);
                return true;
            }

            // Transponder Operation Knob (Combo Box - use SetLVar)
            if (varKey == "S_XPDR_OPERATION")
            {
                simConnect.SetLVar("S_XPDR_OPERATION", value);
                return true;
            }

            // ATC Switch (Combo Box - use SetLVar)
            if (varKey == "S_XPDR_ATC")
            {
                simConnect.SetLVar("S_XPDR_ATC", value);
                return true;
            }

            // Altitude Reporting (Combo Box - use SetLVar)
            if (varKey == "S_XPDR_ALTREPORTING")
            {
                simConnect.SetLVar("S_XPDR_ALTREPORTING", value);
                return true;
            }

            // TCAS Traffic/Range Knob (Combo Box - use SetLVar)
            if (varKey == "S_TCAS_RANGE")
            {
                simConnect.SetLVar("S_TCAS_RANGE", value);
                return true;
            }

            // IDENT Button (use ExecuteButtonTransition)
            if (varKey == "S_XPDR_IDENT" && value == 1)
            {
                ExecuteButtonTransition("S_XPDR_IDENT", "IDENT", simConnect, announcer);
                return true;
            }

            // CLR Button (use ExecuteButtonTransition)
            if (varKey == "S_PED_ATC_CLR" && value == 1)
            {
                ExecuteButtonTransition("S_PED_ATC_CLR", "CLR", simConnect, announcer);
                return true;
            }

            // Transponder Code Set (Standard MSFS Event - handled by MainForm)
            // Uses XPNDR_SET event with user-entered 4-digit code
            // MainForm will display text box and set button automatically

            // ========== RADIO MANAGEMENT PANEL (RMP) ==========

            // RMP1 Power Switch (Combo Box - use SetLVar)
            if (varKey == "S_PED_RMP1_POWER")
            {
                simConnect.SetLVar("S_PED_RMP1_POWER", value);
                return true;
            }

            // RMP1 Mode Buttons (Momentary - use ExecuteButtonTransition)
            if (varKey == "S_PED_RMP1_VHF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_VHF1", "RMP1 VHF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_VHF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_VHF2", "RMP1 VHF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_VHF3" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_VHF3", "RMP1 VHF 3", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_HF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_HF1", "RMP1 HF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_HF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_HF2", "RMP1 HF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_NAV" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_NAV", "RMP1 NAV", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_VOR" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_VOR", "RMP1 VOR", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_ILS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_ILS", "RMP1 ILS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_MLS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_MLS", "RMP1 GLS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_ADF" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_ADF", "RMP1 ADF", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_BFO" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_BFO", "RMP1 BFO", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP1_AM" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_AM", "RMP1 AM", simConnect, announcer);
                return true;
            }

            // RMP1 Frequency Buttons (Inc/Dec - use counter approach)
            // These variables are counters: incrementing increases frequency, decrementing decreases it
            if (varKey == "E_PED_RMP1_INNER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP1_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP1_INNER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP1_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP1_OUTER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP1_OUTER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP1_OUTER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP1_OUTER", simConnect);
                return true;
            }

            // RMP1 Transfer Button (Momentary - use ExecuteButtonTransition)
            if (varKey == "S_PED_RMP1_XFER" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP1_XFER", "RMP1 Transfer", simConnect, announcer);
                return true;
            }

            // RMP2 Power Switch
            if (varKey == "S_PED_RMP2_POWER")
            {
                simConnect.SetLVar("S_PED_RMP2_POWER", value);
                return true;
            }

            // RMP2 Mode Buttons
            if (varKey == "S_PED_RMP2_VHF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_VHF1", "RMP2 VHF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_VHF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_VHF2", "RMP2 VHF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_VHF3" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_VHF3", "RMP2 VHF 3", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_HF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_HF1", "RMP2 HF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_HF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_HF2", "RMP2 HF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_NAV" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_NAV", "RMP2 NAV", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_VOR" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_VOR", "RMP2 VOR", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_ILS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_ILS", "RMP2 ILS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_MLS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_MLS", "RMP2 GLS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_ADF" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_ADF", "RMP2 ADF", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_BFO" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_BFO", "RMP2 BFO", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP2_AM" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_AM", "RMP2 AM", simConnect, announcer);
                return true;
            }

            // RMP2 Frequency Buttons (Inc/Dec - use counter approach)
            // These variables are counters: incrementing increases frequency, decrementing decreases it
            if (varKey == "E_PED_RMP2_INNER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP2_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP2_INNER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP2_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP2_OUTER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP2_OUTER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP2_OUTER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP2_OUTER", simConnect);
                return true;
            }

            // RMP2 Transfer Button (Momentary - use ExecuteButtonTransition)
            if (varKey == "S_PED_RMP2_XFER" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP2_XFER", "RMP2 Transfer", simConnect, announcer);
                return true;
            }

            // RMP3 Power Switch
            if (varKey == "S_PED_RMP3_POWER")
            {
                simConnect.SetLVar("S_PED_RMP3_POWER", value);
                return true;
            }

            // RMP3 Mode Buttons
            if (varKey == "S_PED_RMP3_VHF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_VHF1", "RMP3 VHF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_VHF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_VHF2", "RMP3 VHF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_VHF3" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_VHF3", "RMP3 VHF 3", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_HF1" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_HF1", "RMP3 HF 1", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_HF2" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_HF2", "RMP3 HF 2", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_NAV" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_NAV", "RMP3 NAV", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_VOR" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_VOR", "RMP3 VOR", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_ILS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_ILS", "RMP3 ILS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_MLS" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_MLS", "RMP3 GLS", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_ADF" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_ADF", "RMP3 ADF", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_BFO" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_BFO", "RMP3 BFO", simConnect, announcer);
                return true;
            }

            if (varKey == "S_PED_RMP3_AM" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_AM", "RMP3 AM", simConnect, announcer);
                return true;
            }

            // RMP3 Frequency Buttons (Inc/Dec - use counter approach)
            // These variables are counters: incrementing increases frequency, decrementing decreases it
            if (varKey == "E_PED_RMP3_INNER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP3_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP3_INNER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP3_INNER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP3_OUTER_INC" && value == 1)
            {
                IncrementCounter("E_PED_RMP3_OUTER", simConnect);
                return true;
            }

            if (varKey == "E_PED_RMP3_OUTER_DEC" && value == 1)
            {
                DecrementCounter("E_PED_RMP3_OUTER", simConnect);
                return true;
            }

            // RMP3 Transfer Button (Momentary - use ExecuteButtonTransition)
            if (varKey == "S_PED_RMP3_XFER" && value == 1)
            {
                ExecuteButtonTransition("S_PED_RMP3_XFER", "RMP3 Transfer", simConnect, announcer);
                return true;
            }

            // ========== AUDIO CONTROL PANEL (ACP) ==========

            // Volume Controls (Combo Boxes - use SetLVar)
            if (varKey == "A_ASP_VHF_1_VOLUME")
            {
                simConnect.SetLVar("A_ASP_VHF_1_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_VHF_2_VOLUME")
            {
                simConnect.SetLVar("A_ASP_VHF_2_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_VHF_3_VOLUME")
            {
                simConnect.SetLVar("A_ASP_VHF_3_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_HF_1_VOLUME")
            {
                simConnect.SetLVar("A_ASP_HF_1_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_HF_2_VOLUME")
            {
                simConnect.SetLVar("A_ASP_HF_2_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_CAB_VOLUME")
            {
                simConnect.SetLVar("A_ASP_CAB_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_PA_VOLUME")
            {
                simConnect.SetLVar("A_ASP_PA_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_INT_VOLUME")
            {
                simConnect.SetLVar("A_ASP_INT_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_ILS_VOLUME")
            {
                simConnect.SetLVar("A_ASP_ILS_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_MLS_VOLUME")
            {
                simConnect.SetLVar("A_ASP_MLS_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_ADF_1_VOLUME")
            {
                simConnect.SetLVar("A_ASP_ADF_1_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_ADF_2_VOLUME")
            {
                simConnect.SetLVar("A_ASP_ADF_2_VOLUME", value);
                return true;
            }

            if (varKey == "A_ASP_MARKER_VOLUME")
            {
                simConnect.SetLVar("A_ASP_MARKER_VOLUME", value);
                return true;
            }

            // INTRAD Switch (Combo Box - use SetLVar)
            if (varKey == "S_ASP_INTRAD")
            {
                simConnect.SetLVar("S_ASP_INTRAD", value);
                return true;
            }

            // ========== FLIGHT CONTROLS PANEL (Pedestal) ==========
            // Parking Brake - Simple SetLVar
            if (varKey == "S_MIP_PARKING_BRAKE")
            {
                simConnect.SetLVar("S_MIP_PARKING_BRAKE", value);
                return true;
            }

            // Speedbrake/Spoilers - Simple SetLVar
            if (varKey == "A_FC_SPEEDBRAKE")
            {
                simConnect.SetLVar("A_FC_SPEEDBRAKE", value);
                return true;
            }

            // Rudder Trim Left - Momentary button (0 then back to 1)
            if (varKey == "S_FC_RUDDER_TRIM_LEFT" && value == 1)
            {
                ExecuteRudderTrimTransition(0, "Rudder Trim Left", simConnect, announcer);
                return true;
            }

            // Rudder Trim Right - Momentary button (2 then back to 1)
            if (varKey == "S_FC_RUDDER_TRIM_RIGHT" && value == 1)
            {
                ExecuteRudderTrimTransition(2, "Rudder Trim Right", simConnect, announcer);
                return true;
            }

            // Rudder Trim Reset - Press button (1 then 0)
            if (varKey == "S_FC_RUDDER_TRIM_RESET" && value == 1)
            {
                ExecuteButtonTransition("S_FC_RUDDER_TRIM_RESET", "Rudder Trim Reset", simConnect, announcer);
                return true;
            }

            // Elevator Trim - Simple SetLVar
            if (varKey == "A_FC_ELEVATOR_TRIM")
            {
                simConnect.SetLVar("A_FC_ELEVATOR_TRIM", value);
                return true;
            }

            // Flaps Lever - Simple SetLVar
            if (varKey == "S_FC_FLAPS_LEVER")
            {
                simConnect.SetLVar("S_FC_FLAPS", value);
                return true;
            }

            // Left Thrust Lever - Simple SetLVar
            if (varKey == "A_FC_THROTTLE_LEFT_INPUT")
            {
                simConnect.SetLVar("A_FC_THROTTLE_LEFT_INPUT", value);
                return true;
            }

            // Right Thrust Lever - Simple SetLVar
            if (varKey == "A_FC_THROTTLE_RIGHT_INPUT")
            {
                simConnect.SetLVar("A_FC_THROTTLE_RIGHT_INPUT", value);
                return true;
            }

            // Both Thrust Levers - Special handling (sets both left and right)
            if (varKey == "A_FC_THROTTLE_BOTH_INPUT")
            {
                simConnect.SetLVar("A_FC_THROTTLE_LEFT_INPUT", value);
                simConnect.SetLVar("A_FC_THROTTLE_RIGHT_INPUT", value);
                return true;
            }

            // Autothrottle Disconnect Left - Button
            if (varKey == "S_FC_THR_INST_DISCONNECT1" && value == 1)
            {
                ExecuteButtonTransition("S_FC_THR_INST_DISCONNECT1", "A/THR Disconnect Left", simConnect, announcer);
                return true;
            }

            // Autothrottle Disconnect Right - Button
            if (varKey == "S_FC_THR_INST_DISCONNECT2" && value == 1)
            {
                ExecuteButtonTransition("S_FC_THR_INST_DISCONNECT2", "A/THR Disconnect Right", simConnect, announcer);
                return true;
            }

            // Autopilot Disconnect Captain - Button
            if (varKey == "S_FC_CAPT_INST_DISCONNECT" && value == 1)
            {
                ExecuteButtonTransition("S_FC_CAPT_INST_DISCONNECT", "AP Disconnect Captain", simConnect, announcer);
                return true;
            }

            // Autopilot Disconnect First Officer - Button
            if (varKey == "S_FC_FO_INST_DISCONNECT" && value == 1)
            {
                ExecuteButtonTransition("S_FC_FO_INST_DISCONNECT", "AP Disconnect F/O", simConnect, announcer);
                return true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] Error setting {varKey} to {value}: {ex.Message}");
            announcer.Announce($"Error setting {varDef.DisplayName}");
            // Return true to indicate we handled it (even though it failed)
            // This prevents the generic handler from also failing
            return true;
        }

        // Not handled - use default behavior
        return false;
    }

    /// <summary>
    /// Helper method to execute Fenix button transition (0→1 pattern).
    /// Fenix buttons are transition-activated: they trigger when the variable goes from 0 to 1.
    /// This method sets the variable to 0, waits, then sets it to 1 to create the transition.
    /// </summary>
    private void ExecuteButtonTransition(string varName, string displayName,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] ExecuteButtonTransition START: {displayName} ({varName})");

            // Reset to 0
            if (simConnect != null && simConnect.IsConnected)
            {
                System.Diagnostics.Debug.WriteLine($"[FenixA320] Setting {varName} = 0 (Release)");
                simConnect.SetLVar(varName, 0);
            }

            // Set up timer to transition to 1 after delay
            var transitionTimer = new System.Windows.Forms.Timer();
            transitionTimer.Interval = 200;
            transitionTimer.Tick += (sender, e) =>
            {
                transitionTimer.Stop();
                transitionTimer.Dispose();

                try
                {
                    if (simConnect != null && simConnect.IsConnected)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FenixA320] Setting {varName} = 1 (Press)");
                        simConnect.SetLVar(varName, 1);
                        System.Diagnostics.Debug.WriteLine($"[FenixA320] ExecuteButtonTransition COMPLETE: {displayName}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FenixA320] Error in {displayName} transition (second phase): {ex.Message}");
                }
            };
            transitionTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] Error in {displayName} transition (first phase): {ex.Message}");
            announcer.Announce($"Error pressing {displayName}");
        }
    }

    // Counter tracking for RMP frequency knobs
    private Dictionary<string, int> rmpCounters = new Dictionary<string, int>();

    /// <summary>
    /// Increments a counter variable for RMP frequency controls.
    /// These variables act as counters - incrementing the value increases frequency.
    /// </summary>
    private void IncrementCounter(string varName, SimConnect.SimConnectManager simConnect)
    {
        try
        {
            // Get or initialize counter for this variable
            if (!rmpCounters.ContainsKey(varName))
            {
                rmpCounters[varName] = 0;
            }

            // Increment counter
            rmpCounters[varName]++;
            int newValue = rmpCounters[varName];

            System.Diagnostics.Debug.WriteLine($"[FenixA320] IncrementCounter: {varName} -> {newValue}");

            // Set the LVar to the new counter value
            if (simConnect != null && simConnect.IsConnected)
            {
                simConnect.SetLVar(varName, newValue);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] Error incrementing counter {varName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrements a counter variable for RMP frequency controls.
    /// These variables act as counters - decrementing the value decreases frequency.
    /// </summary>
    private void DecrementCounter(string varName, SimConnect.SimConnectManager simConnect)
    {
        try
        {
            // Get or initialize counter for this variable
            if (!rmpCounters.ContainsKey(varName))
            {
                rmpCounters[varName] = 0;
            }

            // Decrement counter
            rmpCounters[varName]--;
            int newValue = rmpCounters[varName];

            System.Diagnostics.Debug.WriteLine($"[FenixA320] DecrementCounter: {varName} -> {newValue}");

            // Set the LVar to the new counter value
            if (simConnect != null && simConnect.IsConnected)
            {
                simConnect.SetLVar(varName, newValue);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] Error decrementing counter {varName}: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper method for rudder trim momentary buttons.
    /// Sends the trim direction value, waits, then returns to center (1).
    /// </summary>
    /// <param name="trimValue">The trim direction: 0 = left, 2 = right</param>
    private void ExecuteRudderTrimTransition(int trimValue, string displayName,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        try
        {
            // Set trim direction (0 = left, 2 = right)
            if (simConnect != null && simConnect.IsConnected)
            {
                simConnect.SetLVar("S_FC_RUDDER_TRIM", trimValue);
            }

            // Set up timer to return to center (1) after delay
            var transitionTimer = new System.Windows.Forms.Timer();
            transitionTimer.Interval = 200;
            transitionTimer.Tick += (sender, e) =>
            {
                transitionTimer.Stop();
                transitionTimer.Dispose();

                try
                {
                    // Return to center position
                    if (simConnect != null && simConnect.IsConnected)
                    {
                        simConnect.SetLVar("S_FC_RUDDER_TRIM", 1);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FenixA320] Error in {displayName} transition (second phase): {ex.Message}");
                }
            };
            transitionTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FenixA320] Error in {displayName} transition (first phase): {ex.Message}");
            announcer.Announce($"Error executing {displayName}");
        }
    }
}
