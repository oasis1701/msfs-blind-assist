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
                DisplayName = "ECAM EMER CANC Button On",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["I_ECAM_EMER_CANCEL"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ECAM_EMER_CANCEL",
                DisplayName = "ECAM EMER CANC Button Off",
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
            ["N_ELEC_VOLT_BAT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "N_ELEC_VOLT_BAT_1",
                DisplayName = "OVHD BAT 1 Volt Display",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
            },
            ["N_ELEC_VOLT_BAT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "N_ELEC_VOLT_BAT_2",
                DisplayName = "OVHD BAT 2 Volt Display",
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

            // ========== LIGHTS (6 variables) ==========
            ["A_OH_LIGHTING_OVD"] = new SimConnect.SimVarDefinition
            {
                Name = "A_OH_LIGHTING_OVD",
                DisplayName = "OVHD INTEG LIGHT KNOB POSITION",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
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
            ["I_OH_NAV_ADIRS_ON_BAT"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADIRS_ON_BAT",
                DisplayName = "ADIRS On Bat",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },

            // ========== RADIO (119 variables) ==========
            ["I_OH_NAV_ADR3_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR3_U",
                DisplayName = "ADIRS ADR 3 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR3_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR3_L",
                DisplayName = "ADIRS ADR 3 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR2_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR2_U",
                DisplayName = "ADIRS ADR 2 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR2_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR2_L",
                DisplayName = "ADIRS ADR 2 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR1_U"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR1_U",
                DisplayName = "ADIRS ADR 1 Fault",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
            ["I_OH_NAV_ADR1_L"] = new SimConnect.SimVarDefinition
            {
                Name = "I_OH_NAV_ADR1_L",
                DisplayName = "ADIRS ADR 1 Available",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> {[0] = "Off", [1] = "On"}
            },
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
            ["I_ASP_HF_1_SEND"] = new SimConnect.SimVarDefinition
            {
                Name = "I_ASP_HF_1_SEND",
                DisplayName = "ACP1 HF 2 Send Fault",
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
            ["S_OH_SIGNS_SMOKING"] = new SimConnect.SimVarDefinition
            {
                Name = "S_OH_SIGNS_SMOKING",
                DisplayName = "OVHD NO SMOKING SIGN ON",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
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
                "Electrical"
                // Additional panels will be added here as features are implemented
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

                // Bus Controls (2 controls)
                "S_OH_ELEC_BUS_TIE",
                "S_OH_ELEC_AC_ESS_FEED",

                // IDG Buttons (2 controls)
                "S_OH_ELEC_IDG1",
                "S_OH_ELEC_IDG2",

                // Galley Buttons (2 controls)
                "S_OH_ELEC_GALY",
                "S_OH_ELEC_COMMERCIAL"
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
    /// - Buttons: Use ExecuteCalculatorCode (RPN operations via MobiFlight)
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer)
    {
        // ========== BATTERY SWITCHES (Combo Boxes - use SetLVar) ==========
        if (varKey == "S_OH_ELEC_BAT1")
        {
            simConnect.SetLVar("S_OH_ELEC_BAT1", value);
            announcer.Announce($"Battery 1 {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_BAT2")
        {
            simConnect.SetLVar("S_OH_ELEC_BAT2", value);
            announcer.Announce($"Battery 2 {(value == 1 ? "On" : "Off")}");
            return true;
        }

        // ========== ELECTRICAL PANEL CONTROLS ==========
        // These work like batteries - combo boxes with Off (0) / On (1) states
        // External Power is the only button (uses ExecuteButtonTransition)

        if (varKey == "S_OH_ELEC_GEN1")
        {
            simConnect.SetLVar("S_OH_ELEC_GEN1_LINE", value);
            announcer.Announce($"Generator 1 {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_GEN2")
        {
            simConnect.SetLVar("S_OH_ELEC_GEN2", value);
            announcer.Announce($"Generator 2 {(value == 1 ? "On" : "Off")}");
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
            announcer.Announce($"APU Generator {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_BUS_TIE")
        {
            simConnect.SetLVar("S_OH_ELEC_BUSTIE", value);
            announcer.Announce($"Bus Tie {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_AC_ESS_FEED")
        {
            simConnect.SetLVar("S_OH_ELEC_AC_ESS_FEED", value);
            announcer.Announce($"AC Essential Feed {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_IDG1")
        {
            simConnect.SetLVar("S_OH_ELEC_IDG1", value);
            announcer.Announce($"IDG 1 {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_IDG2")
        {
            simConnect.SetLVar("S_OH_ELEC_IDG2", value);
            announcer.Announce($"IDG 2 {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_GALY")
        {
            simConnect.SetLVar("S_OH_ELEC_GALY", value);
            announcer.Announce($"Galley {(value == 1 ? "On" : "Off")}");
            return true;
        }

        if (varKey == "S_OH_ELEC_COMMERCIAL")
        {
            simConnect.SetLVar("S_OH_ELEC_COMMERCIAL", value);
            announcer.Announce($"Commercial {(value == 1 ? "On" : "Off")}");
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
        // Reset to 0
        simConnect.SetLVar(varName, 0);

        // Set up timer to transition to 1 after delay
        var transitionTimer = new System.Windows.Forms.Timer();
        transitionTimer.Interval = 500;
        transitionTimer.Tick += (sender, e) =>
        {
            transitionTimer.Stop();
            transitionTimer.Dispose();

            try
            {
                if (simConnect != null && simConnect.IsConnected)
                {
                    simConnect.SetLVar(varName, 1);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FenixA320] Error in {displayName} transition: {ex.Message}");
            }
        };
        transitionTimer.Start();

        announcer.Announce($"{displayName} pressed");
    }
}
