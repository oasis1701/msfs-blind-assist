using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Aircraft definition scaffold for the PMDG 777.
/// Panel structure and event ID dictionary are defined here.
/// Variables and panel controls will be populated in subsequent tasks.
/// </summary>
public class PMDG777Definition : BaseAircraftDefinition
{
    public override string AircraftName => "PMDG 777";
    public override string AircraftCode => "PMDG_777";

    // PMDG 777 MCP uses increment/decrement selectors for speed/heading/altitude/VS
    public override FCUControlType GetAltitudeControlType() => FCUControlType.SetValue;
    public override FCUControlType GetHeadingControlType() => FCUControlType.SetValue;
    public override FCUControlType GetSpeedControlType() => FCUControlType.SetValue;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.SetValue;

    // =========================================================================
    // Panel Structure
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        return new Dictionary<string, List<string>>
        {
            ["Overhead"] = new List<string>
            {
                "Electrical", "Hydraulic", "Fuel", "Engines", "Bleed Air",
                "Air Conditioning", "Pressurization", "Anti-Ice", "Fire",
                "Lights", "Signs", "Wipers", "Panel Lighting", "Cargo Temperature"
            },
            ["Overhead Maintenance"] = new List<string>
            {
                "Flight Controls", "Backup Systems", "EEC/APU Maintenance"
            },
            ["Glareshield"] = new List<string>
            {
                "EFIS Captain", "EFIS First Officer", "Mode Control Panel", "Display Select Panel"
            },
            ["Forward Panel"] = new List<string>
            {
                "Landing Gear", "Brakes", "GPWS", "Instruments", "Chronometers"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Transponder/TCAS", "Weather Radar",
                "Communication", "CDU", "Warning"
            }
        };
    }

    // =========================================================================
    // Variables — scaffold (populated in Tasks 6-8)
    // =========================================================================

    public override Dictionary<string, SimConnect.SimVarDefinition> GetVariables()
    {
        var variables = GetBaseVariables();
        var pmdgVars = GetPMDGVariables();
        foreach (var kvp in pmdgVars)
            variables[kvp.Key] = kvp.Value;
        return variables;
    }

    private Dictionary<string, SimConnect.SimVarDefinition> GetPMDGVariables()
    {
        return new Dictionary<string, SimConnect.SimVarDefinition>
        {
            // =================================================================
            // ELECTRICAL
            // =================================================================
            ["ELEC_Battery"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_Battery_Sw_ON",
                DisplayName = "Battery",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_APUGen"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_APUGen_Sw_ON",
                DisplayName = "APU Generator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_APU_Selector"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_APU_Selector",
                DisplayName = "APU Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On", [2] = "Start" }
            },
            ["ELEC_BusTie_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_BusTie_Sw_AUTO_0",
                DisplayName = "Bus Tie 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Isln", [1] = "Auto" }
            },
            ["ELEC_BusTie_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_BusTie_Sw_AUTO_1",
                DisplayName = "Bus Tie 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Isln", [1] = "Auto" }
            },
            ["ELEC_ExtPwrPrim"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_ExtPwrSw_0",
                DisplayName = "External Power Primary",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_ExtPwrSec"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_ExtPwrSw_1",
                DisplayName = "External Power Secondary",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_Gen_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_Gen_Sw_ON_0",
                DisplayName = "Generator 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_Gen_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_Gen_Sw_ON_1",
                DisplayName = "Generator 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_BackupGen_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_BackupGen_Sw_ON_0",
                DisplayName = "Backup Generator 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_BackupGen_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_BackupGen_Sw_ON_1",
                DisplayName = "Backup Generator 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_IDGDisc_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_IDGDiscSw_0",
                DisplayName = "IDG Disconnect 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Disconnect" }
            },
            ["ELEC_IDGDisc_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_IDGDiscSw_1",
                DisplayName = "IDG Disconnect 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Disconnect" }
            },
            ["ELEC_CabUtil"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_CabUtilSw",
                DisplayName = "Cabin Utility",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_IFEPassSeats"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_IFEPassSeatsSw",
                DisplayName = "IFE Passenger Seats",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_StandbyPwr"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_StandbyPowerSw",
                DisplayName = "Standby Power",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "Bat" }
            },
            // Electrical annunciators
            ["ELEC_annunBattery_OFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBattery_OFF",
                DisplayName = "Battery OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunAPU_GEN_OFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunAPU_GEN_OFF",
                DisplayName = "APU GEN OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunAPU_FAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunAPU_FAULT",
                DisplayName = "APU FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunBusTieISLN_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBusTieISLN_0",
                DisplayName = "Bus Tie 1 ISLN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunBusTieISLN_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBusTieISLN_1",
                DisplayName = "Bus Tie 2 ISLN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunExtPwrON_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_ON_0",
                DisplayName = "Ext Power 1 ON Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunExtPwrON_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_ON_1",
                DisplayName = "Ext Power 2 ON Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunExtPwrAVAIL_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_AVAIL_0",
                DisplayName = "Ext Power 1 AVAIL Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunExtPwrAVAIL_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_AVAIL_1",
                DisplayName = "Ext Power 2 AVAIL Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunGenOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunGenOFF_0",
                DisplayName = "Generator 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunGenOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunGenOFF_1",
                DisplayName = "Generator 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunBackupGenOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBackupGenOFF_0",
                DisplayName = "Backup Generator 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunBackupGenOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBackupGenOFF_1",
                DisplayName = "Backup Generator 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunIDGDiscDRIVE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIDGDiscDRIVE_0",
                DisplayName = "IDG 1 Disc Drive Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunIDGDiscDRIVE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIDGDiscDRIVE_1",
                DisplayName = "IDG 2 Disc Drive Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunCabUtilOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunCabUtilOFF",
                DisplayName = "Cabin Utility OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunIFEPassSeatsOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIFEPassSeatsOFF",
                DisplayName = "IFE Pass Seats OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ELEC_annunTowingPowerON_BATT"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunTowingPowerON_BATT",
                DisplayName = "Towing Power ON BATT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // HYDRAULIC
            // =================================================================
            ["HYD_PrimEngPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_PrimaryEngPump_Sw_ON_0",
                DisplayName = "Primary Engine Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HYD_PrimEngPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_PrimaryEngPump_Sw_ON_1",
                DisplayName = "Primary Engine Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HYD_PrimElecPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_PrimaryElecPump_Sw_ON_0",
                DisplayName = "Primary Electric Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HYD_PrimElecPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_PrimaryElecPump_Sw_ON_1",
                DisplayName = "Primary Electric Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["HYD_DemandElecPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_DemandElecPump_Selector_0",
                DisplayName = "Demand Electric Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["HYD_DemandElecPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_DemandElecPump_Selector_1",
                DisplayName = "Demand Electric Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["HYD_DemandAirPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_DemandAirPump_Selector_0",
                DisplayName = "Demand Air Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["HYD_DemandAirPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_DemandAirPump_Selector_1",
                DisplayName = "Demand Air Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["HYD_RAT"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_RamAirTurbineSw",
                DisplayName = "RAM Air Turbine",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Deploy" }
            },
            // Hydraulic annunciators
            ["HYD_annunPrimEngPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryEngPumpFAULT_0",
                DisplayName = "Primary Engine Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunPrimEngPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryEngPumpFAULT_1",
                DisplayName = "Primary Engine Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunPrimElecPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryElecPumpFAULT_0",
                DisplayName = "Primary Electric Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunPrimElecPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryElecPumpFAULT_1",
                DisplayName = "Primary Electric Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunDemandElecPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandElecPumpFAULT_0",
                DisplayName = "Demand Electric Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunDemandElecPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandElecPumpFAULT_1",
                DisplayName = "Demand Electric Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunDemandAirPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandAirPumpFAULT_0",
                DisplayName = "Demand Air Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunDemandAirPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandAirPumpFAULT_1",
                DisplayName = "Demand Air Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunRATPress"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunRamAirTurbinePRESS",
                DisplayName = "RAM Air Turbine PRESS Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["HYD_annunRATUnlkd"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunRamAirTurbineUNLKD",
                DisplayName = "RAM Air Turbine UNLKD Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // FUEL
            // =================================================================
            ["FUEL_FwdPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpFwd_Sw_0",
                DisplayName = "Forward Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_FwdPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpFwd_Sw_1",
                DisplayName = "Forward Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_AftPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpAft_Sw_0",
                DisplayName = "Aft Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_AftPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpAft_Sw_1",
                DisplayName = "Aft Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_CtrPump_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpCtr_Sw_0",
                DisplayName = "Center Pump 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_CtrPump_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpCtr_Sw_1",
                DisplayName = "Center Pump 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_CrossfeedFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_CrossFeedFwd_Sw",
                DisplayName = "Crossfeed Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["FUEL_CrossfeedAft"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_CrossFeedAft_Sw",
                DisplayName = "Crossfeed Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["FUEL_JettisonNozzleL"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_JettisonNozle_Sw_0",
                DisplayName = "Jettison Nozzle Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_JettisonNozzleR"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_JettisonNozle_Sw_1",
                DisplayName = "Jettison Nozzle Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FUEL_JettisonArm"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_JettisonArm_Sw",
                DisplayName = "Jettison Arm",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["FUEL_FuelToRemainPulled"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_FuelToRemain_Sw_Pulled",
                DisplayName = "Fuel To Remain Pulled",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
            },
            ["FUEL_FuelToRemainSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_FuelToRemain_Selector",
                DisplayName = "Fuel To Remain Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Decr", [1] = "Off", [2] = "Incr" }
            },
            // Fuel annunciators
            ["FUEL_annunFwdXFEED_VALVE"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunFwdXFEED_VALVE",
                DisplayName = "Fwd XFEED VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunAftXFEED_VALVE"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunAftXFEED_VALVE",
                DisplayName = "Aft XFEED VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Fwd_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Fwd_0",
                DisplayName = "LOW PRESS Fwd 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Fwd_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Fwd_1",
                DisplayName = "LOW PRESS Fwd 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Aft_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Aft_0",
                DisplayName = "LOW PRESS Aft 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Aft_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Aft_1",
                DisplayName = "LOW PRESS Aft 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Ctr_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Ctr_0",
                DisplayName = "LOW PRESS Center 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Ctr_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Ctr_1",
                DisplayName = "LOW PRESS Center 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunJettisonNozzleVALVE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunJettisonNozleVALVE_0",
                DisplayName = "Jettison Nozzle 1 VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunJettisonNozzleVALVE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunJettisonNozleVALVE_1",
                DisplayName = "Jettison Nozzle 2 VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FUEL_annunArmFAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunArmFAULT",
                DisplayName = "Arm FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // ENGINES
            // =================================================================
            ["ENG_EECMode_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECMode_Sw_NORM_0",
                DisplayName = "EEC Mode Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Altn", [1] = "Norm" }
            },
            ["ENG_EECMode_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECMode_Sw_NORM_1",
                DisplayName = "EEC Mode Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Altn", [1] = "Norm" }
            },
            ["ENG_StartSelector_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_Start_Selector_0",
                DisplayName = "Start Selector Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Norm", [1] = "Start" }
            },
            ["ENG_StartSelector_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_Start_Selector_1",
                DisplayName = "Start Selector Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Norm", [1] = "Start" }
            },
            ["ENG_Autostart"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_Autostart_Sw_ON",
                DisplayName = "Autostart",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            // Engine annunciators
            ["ENG_annunALTN_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_annunALTN_0",
                DisplayName = "EEC ALTN Left Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ENG_annunALTN_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_annunALTN_1",
                DisplayName = "EEC ALTN Right Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ENG_annunAutostartOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_annunAutostartOFF",
                DisplayName = "Autostart OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // BLEED AIR
            // =================================================================
            ["AIR_EngBleed_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_EngBleedAir_Sw_AUTO_0",
                DisplayName = "Engine Bleed 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },
            ["AIR_EngBleed_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_EngBleedAir_Sw_AUTO_1",
                DisplayName = "Engine Bleed 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },
            ["AIR_APUBleed"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_APUBleedAir_Sw_AUTO",
                DisplayName = "APU Bleed",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },
            ["AIR_IsolationValve_L"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_IsolationValve_Sw_0",
                DisplayName = "Isolation Valve Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Auto" }
            },
            ["AIR_IsolationValve_R"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_IsolationValve_Sw_1",
                DisplayName = "Isolation Valve Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Auto" }
            },
            ["AIR_CtrIsolationValve"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CtrIsolationValve_Sw",
                DisplayName = "Center Isolation Valve",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Auto" }
            },
            // Bleed air annunciators
            ["AIR_annunEngBleedOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunEngBleedAirOFF_0",
                DisplayName = "Engine Bleed 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunEngBleedOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunEngBleedAirOFF_1",
                DisplayName = "Engine Bleed 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunAPUBleedOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunAPUBleedAirOFF",
                DisplayName = "APU Bleed OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunIsolationValveCLOSED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunIsolationValveCLOSED_0",
                DisplayName = "Isolation Valve Left CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunIsolationValveCLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunIsolationValveCLOSED_1",
                DisplayName = "Isolation Valve Right CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunCtrIsolationValveCLOSED"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunCtrIsolationValveCLOSED",
                DisplayName = "Center Isolation Valve CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // AIR CONDITIONING
            // =================================================================
            ["AIR_Pack_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_Pack_Sw_AUTO_0",
                DisplayName = "Pack 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },
            ["AIR_Pack_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_Pack_Sw_AUTO_1",
                DisplayName = "Pack 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
            },
            ["AIR_TrimAir_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_TrimAir_Sw_On_0",
                DisplayName = "Trim Air 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_TrimAir_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_TrimAir_Sw_On_1",
                DisplayName = "Trim Air 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_RecircFanUpper"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_RecircFan_Sw_On_0",
                DisplayName = "Recirc Fan Upper",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_RecircFanLower"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_RecircFan_Sw_On_1",
                DisplayName = "Recirc Fan Lower",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_EquipCooling"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_EquipCooling_Sw_AUTO",
                DisplayName = "Equipment Cooling",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Ovrd", [1] = "Auto" }
            },
            ["AIR_Gasper"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_Gasper_Sw_On",
                DisplayName = "Gasper",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_AltnVent"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_AltnVentSw_ON",
                DisplayName = "Alt Ventilation",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["AIR_TempSelectorFlightDeck"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_TempSelector_0",
                DisplayName = "Temp Selector Flight Deck",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_TempSelectorCabin"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_TempSelector_1",
                DisplayName = "Temp Selector Cabin",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            // Air conditioning annunciators
            ["AIR_annunPackOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunPackOFF_0",
                DisplayName = "Pack 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunPackOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunPackOFF_1",
                DisplayName = "Pack 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunTrimAirFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunTrimAirFAULT_0",
                DisplayName = "Trim Air 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunTrimAirFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunTrimAirFAULT_1",
                DisplayName = "Trim Air 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunEquipCoolingOVRD"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunEquipCoolingOVRD",
                DisplayName = "Equipment Cooling OVRD Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunAltnVentFAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunAltnVentFAULT",
                DisplayName = "Alt Vent FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // PRESSURIZATION
            // =================================================================
            ["AIR_OutflowValveFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_OutflowValveManual_Selector_0",
                DisplayName = "Outflow Valve Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Auto", [2] = "Close" }
            },
            ["AIR_OutflowValveAft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_OutflowValveManual_Selector_1",
                DisplayName = "Outflow Valve Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Auto", [2] = "Close" }
            },
            ["AIR_LdgAltSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_LdgAlt_Selector",
                DisplayName = "Landing Altitude Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_LdgAltPulled"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_LdgAlt_Sw_Pulled",
                DisplayName = "Landing Altitude Pulled",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled" }
            },
            // Pressurization annunciators
            ["AIR_annunOutflowValveMAN_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunOutflowValve_MAN_0",
                DisplayName = "Outflow Valve 1 MAN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["AIR_annunOutflowValveMAN_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunOutflowValve_MAN_1",
                DisplayName = "Outflow Valve 2 MAN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // ANTI-ICE
            // =================================================================
            ["ICE_WindowHeat_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeat_Sw_ON_0",
                DisplayName = "Window Heat 1 (Left Side)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ICE_WindowHeat_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeat_Sw_ON_1",
                DisplayName = "Window Heat 2 (Left Forward)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ICE_WindowHeat_3"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeat_Sw_ON_2",
                DisplayName = "Window Heat 3 (Right Forward)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ICE_WindowHeat_4"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeat_Sw_ON_3",
                DisplayName = "Window Heat 4 (Right Side)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ICE_WingAntiIce"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WingAntiIceSw",
                DisplayName = "Wing Anti-Ice",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["ICE_EngAntiIce_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_EngAntiIceSw_0",
                DisplayName = "Engine Anti-Ice 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["ICE_EngAntiIce_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_EngAntiIceSw_1",
                DisplayName = "Engine Anti-Ice 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            // Anti-ice annunciators
            ["ICE_annunWindowHeatINOP_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_0",
                DisplayName = "Window Heat 1 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_1",
                DisplayName = "Window Heat 2 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_3"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_2",
                DisplayName = "Window Heat 3 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_4"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_3",
                DisplayName = "Window Heat 4 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // FIRE
            // =================================================================
            ["FIRE_CargoFireArmFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_CargoFire_Sw_Arm_0",
                DisplayName = "Cargo Fire Arm Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["FIRE_CargoFireArmAft"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_CargoFire_Sw_Arm_1",
                DisplayName = "Cargo Fire Arm Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["FIRE_CargoFireDisch"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_CargoFireDisch_Sw",
                DisplayName = "Cargo Fire Discharge",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Discharge" }
            },
            ["FIRE_FireOvhtTest"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_FireOvhtTest_Sw",
                DisplayName = "Fire Overheat Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Test" }
            },
            ["FIRE_APUHandle"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_APUHandle",
                DisplayName = "APU Fire Handle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Pulled", [2] = "Left", [3] = "Right" }
            },
            ["FIRE_APUHandleUnlock"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_APUHandleUnlock_Sw",
                DisplayName = "APU Handle Unlock",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Unlock" }
            },
            // Fire annunciators
            ["FIRE_annunCargoFire_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoFire_0",
                DisplayName = "Cargo Fire 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunCargoFire_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoFire_1",
                DisplayName = "Cargo Fire 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunCargoDISCH"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoDISCH",
                DisplayName = "Cargo DISCH Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunAPU_BTL_DISCH"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunAPU_BTL_DISCH",
                DisplayName = "APU Bottle DISCH Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunEngHandle_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIlluminated_0",
                DisplayName = "Engine 1 Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunEngHandle_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIlluminated_1",
                DisplayName = "Engine 2 Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunAPUHandleIllum"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_APUHandleIlluminated",
                DisplayName = "APU Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunMainDeckCargoFire"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunMainDeckCargoFire",
                DisplayName = "Main Deck Cargo Fire Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FIRE_annunCargoDEPR"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoDEPR",
                DisplayName = "Cargo DEPR Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // LIGHTS
            // =================================================================
            ["LTS_LandingLightL"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_LandingLights_Sw_ON_0",
                DisplayName = "Landing Light Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_LandingLightR"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_LandingLights_Sw_ON_1",
                DisplayName = "Landing Light Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_LandingLightNose"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_LandingLights_Sw_ON_2",
                DisplayName = "Landing Light Nose",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_RunwayTurnoffL"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_RunwayTurnoff_Sw_ON_0",
                DisplayName = "Runway Turnoff Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_RunwayTurnoffR"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_RunwayTurnoff_Sw_ON_1",
                DisplayName = "Runway Turnoff Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Taxi"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Taxi_Sw_ON",
                DisplayName = "Taxi",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Strobe"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Strobe_Sw_ON",
                DisplayName = "Strobe",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Beacon"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Beacon_Sw_ON",
                DisplayName = "Beacon",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_NAV_Sw_ON",
                DisplayName = "Nav",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Logo"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Logo_Sw_ON",
                DisplayName = "Logo",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Wing"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Wing_Sw_ON",
                DisplayName = "Wing",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_Storm"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Storm_Sw_ON",
                DisplayName = "Storm Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // =================================================================
            // SIGNS
            // =================================================================
            ["SIGNS_NoSmoking"] = new SimConnect.SimVarDefinition
            {
                Name = "SIGNS_NoSmokingSelector",
                DisplayName = "No Smoking",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["SIGNS_SeatBelts"] = new SimConnect.SimVarDefinition
            {
                Name = "SIGNS_SeatBeltsSelector",
                DisplayName = "Seat Belts",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto", [2] = "On" }
            },
            ["OXY_PassOxygen"] = new SimConnect.SimVarDefinition
            {
                Name = "OXY_PassOxygen_Sw_On",
                DisplayName = "Passenger Oxygen",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "On" }
            },
            // Signs annunciators
            ["OXY_annunPassOxygenON"] = new SimConnect.SimVarDefinition
            {
                Name = "OXY_annunPassOxygenON",
                DisplayName = "Passenger Oxygen ON Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // WIPERS
            // =================================================================
            ["WIPERS_Left"] = new SimConnect.SimVarDefinition
            {
                Name = "WIPERS_Selector_0",
                DisplayName = "Wiper Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Int", [2] = "Low", [3] = "High" }
            },
            ["WIPERS_Right"] = new SimConnect.SimVarDefinition
            {
                Name = "WIPERS_Selector_1",
                DisplayName = "Wiper Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Int", [2] = "Low", [3] = "High" }
            },
            ["COMM_ServiceInterphone"] = new SimConnect.SimVarDefinition
            {
                Name = "COMM_ServiceInterphoneSw",
                DisplayName = "Service Interphone",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },

            // =================================================================
            // PANEL LIGHTING
            // =================================================================
            ["LTS_MasterBrightSw"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_MasterBright_Sw_ON",
                DisplayName = "Master Bright Switch",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["LTS_MasterBrightKnob"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_MasterBrigntKnob",
                DisplayName = "Master Bright Knob",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_IndLightsTest"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_IndLightsTestSw",
                DisplayName = "Indicator Lights Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Bright", [2] = "Dim" }
            },
            ["LTS_DomeLight"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_DomeLightKnob",
                DisplayName = "Dome Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_CircuitBreakerLight"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_CircuitBreakerKnob",
                DisplayName = "Circuit Breaker Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_OverheadPanel"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_OvereadPanelKnob",
                DisplayName = "Overhead Panel",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_GlareshieldPanel"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_GlareshieldPNLlKnob",
                DisplayName = "Glareshield Panel",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_GlareshieldFlood"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_GlareshieldFLOODKnob",
                DisplayName = "Glareshield Flood",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [25] = "Dim", [50] = "Medium", [75] = "Bright", [100] = "Full" }
            },
            ["LTS_EmerLights"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_EmerLightsSelector",
                DisplayName = "Emergency Lights",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Armed", [2] = "On" }
            },

            // =================================================================
            // CARGO TEMPERATURE
            // =================================================================
            ["AIR_CargoTempFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_Selector_0",
                DisplayName = "Cargo Temp Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_CargoTempAft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_Selector_1",
                DisplayName = "Cargo Temp Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_CargoTempMainDeckFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_MainDeckFwd_Sel",
                DisplayName = "Cargo Temp Main Deck Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_CargoTempMainDeckAft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_MainDeckAft_Sel",
                DisplayName = "Cargo Temp Main Deck Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_CargoTempLowerFwd"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_LowerFwd_Sel",
                DisplayName = "Cargo Temp Lower Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["AIR_CargoTempLowerAft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_LowerAft_Sel",
                DisplayName = "Cargo Temp Lower Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // =================================================================
            // OVERHEAD MAINTENANCE — FLIGHT CONTROLS
            // =================================================================
            ["FCTL_WingHydValve_L"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_WingHydValve_Sw_SHUT_OFF_0",
                DisplayName = "Wing Hydraulic Valve Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_WingHydValve_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_WingHydValve_Sw_SHUT_OFF_1",
                DisplayName = "Wing Hydraulic Valve Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_WingHydValve_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_WingHydValve_Sw_SHUT_OFF_2",
                DisplayName = "Wing Hydraulic Valve Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_TailHydValve_L"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_TailHydValve_Sw_SHUT_OFF_0",
                DisplayName = "Tail Hydraulic Valve Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_TailHydValve_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_TailHydValve_Sw_SHUT_OFF_1",
                DisplayName = "Tail Hydraulic Valve Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_TailHydValve_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_TailHydValve_Sw_SHUT_OFF_2",
                DisplayName = "Tail Hydraulic Valve Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Shut Off" }
            },
            ["FCTL_PrimFltComputers"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_PrimFltComputersSw_AUTO",
                DisplayName = "Primary Flight Computers",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Disc", [1] = "Auto" }
            },
            // Flight controls annunciators
            ["FCTL_annunWingHydVALVE_CLOSED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunWingHydVALVE_CLOSED_0",
                DisplayName = "Wing Hyd Valve Left CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunWingHydVALVE_CLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunWingHydVALVE_CLOSED_1",
                DisplayName = "Wing Hyd Valve Right CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunWingHydVALVE_CLOSED_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunWingHydVALVE_CLOSED_2",
                DisplayName = "Wing Hyd Valve Center CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_0",
                DisplayName = "Tail Hyd Valve Left CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_1",
                DisplayName = "Tail Hyd Valve Right CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_2",
                DisplayName = "Tail Hyd Valve Center CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },
            ["FCTL_annunPrimFltComputersDISC"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunPrimFltComputersDISC",
                DisplayName = "Primary Flight Computers DISC Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [1] = "on" }
            },

            // =================================================================
            // OVERHEAD MAINTENANCE — BACKUP SYSTEMS
            // =================================================================
            ["ICE_BackupWindowHeat_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeatBackUp_Sw_OFF_0",
                DisplayName = "Backup Window Heat Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Off" }
            },
            ["ICE_BackupWindowHeat_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_WindowHeatBackUp_Sw_OFF_1",
                DisplayName = "Backup Window Heat Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Off" }
            },
            ["ELEC_TowingPower"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_TowingPower_Sw_BATT",
                DisplayName = "Towing Power",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Batt" }
            },

            // =================================================================
            // OVERHEAD MAINTENANCE — EEC/APU MAINTENANCE
            // =================================================================
            ["ENG_EECTest_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECPower_Sw_TEST_0",
                DisplayName = "EEC Test Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Test" }
            },
            ["ENG_EECTest_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECPower_Sw_TEST_1",
                DisplayName = "EEC Test Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Test" }
            },
            ["APU_PowerTest"] = new SimConnect.SimVarDefinition
            {
                Name = "APU_Power_Sw_TEST",
                DisplayName = "APU Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Test" }
            },
        };
    }

    // =========================================================================
    // Panel Controls — scaffold (populated in Tasks 6-8)
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // Overhead — Electrical
            ["Electrical"] = new List<string>
            {
                "ELEC_Battery", "ELEC_APUGen", "ELEC_APU_Selector",
                "ELEC_BusTie_1", "ELEC_BusTie_2",
                "ELEC_ExtPwrPrim", "ELEC_ExtPwrSec",
                "ELEC_Gen_1", "ELEC_Gen_2",
                "ELEC_BackupGen_1", "ELEC_BackupGen_2",
                "ELEC_IDGDisc_1", "ELEC_IDGDisc_2",
                "ELEC_CabUtil", "ELEC_IFEPassSeats", "ELEC_StandbyPwr"
            },

            // Overhead — Hydraulic
            ["Hydraulic"] = new List<string>
            {
                "HYD_PrimEngPump_1", "HYD_PrimEngPump_2",
                "HYD_PrimElecPump_1", "HYD_PrimElecPump_2",
                "HYD_DemandElecPump_1", "HYD_DemandElecPump_2",
                "HYD_DemandAirPump_1", "HYD_DemandAirPump_2",
                "HYD_RAT"
            },

            // Overhead — Fuel
            ["Fuel"] = new List<string>
            {
                "FUEL_FwdPump_1", "FUEL_FwdPump_2",
                "FUEL_AftPump_1", "FUEL_AftPump_2",
                "FUEL_CtrPump_1", "FUEL_CtrPump_2",
                "FUEL_CrossfeedFwd", "FUEL_CrossfeedAft",
                "FUEL_JettisonNozzleL", "FUEL_JettisonNozzleR",
                "FUEL_JettisonArm", "FUEL_FuelToRemainPulled", "FUEL_FuelToRemainSelector"
            },

            // Overhead — Engines
            ["Engines"] = new List<string>
            {
                "ENG_EECMode_L", "ENG_EECMode_R",
                "ENG_StartSelector_L", "ENG_StartSelector_R",
                "ENG_Autostart"
            },

            // Overhead — Bleed Air
            ["Bleed Air"] = new List<string>
            {
                "AIR_EngBleed_1", "AIR_EngBleed_2", "AIR_APUBleed",
                "AIR_IsolationValve_L", "AIR_IsolationValve_R", "AIR_CtrIsolationValve"
            },

            // Overhead — Air Conditioning
            ["Air Conditioning"] = new List<string>
            {
                "AIR_Pack_1", "AIR_Pack_2",
                "AIR_TrimAir_1", "AIR_TrimAir_2",
                "AIR_RecircFanUpper", "AIR_RecircFanLower",
                "AIR_EquipCooling", "AIR_Gasper", "AIR_AltnVent",
                "AIR_TempSelectorFlightDeck", "AIR_TempSelectorCabin"
            },

            // Overhead — Pressurization
            ["Pressurization"] = new List<string>
            {
                "AIR_OutflowValveFwd", "AIR_OutflowValveAft",
                "AIR_LdgAltSelector", "AIR_LdgAltPulled"
            },

            // Overhead — Anti-Ice
            ["Anti-Ice"] = new List<string>
            {
                "ICE_WindowHeat_1", "ICE_WindowHeat_2", "ICE_WindowHeat_3", "ICE_WindowHeat_4",
                "ICE_WingAntiIce", "ICE_EngAntiIce_1", "ICE_EngAntiIce_2"
            },

            // Overhead — Fire
            ["Fire"] = new List<string>
            {
                "FIRE_CargoFireArmFwd", "FIRE_CargoFireArmAft",
                "FIRE_CargoFireDisch", "FIRE_FireOvhtTest",
                "FIRE_APUHandle", "FIRE_APUHandleUnlock"
            },

            // Overhead — Lights
            ["Lights"] = new List<string>
            {
                "LTS_LandingLightL", "LTS_LandingLightR", "LTS_LandingLightNose",
                "LTS_RunwayTurnoffL", "LTS_RunwayTurnoffR",
                "LTS_Taxi", "LTS_Strobe", "LTS_Beacon",
                "LTS_NAV", "LTS_Logo", "LTS_Wing", "LTS_Storm"
            },

            // Overhead — Signs
            ["Signs"] = new List<string>
            {
                "SIGNS_NoSmoking", "SIGNS_SeatBelts", "OXY_PassOxygen"
            },

            // Overhead — Wipers
            ["Wipers"] = new List<string>
            {
                "WIPERS_Left", "WIPERS_Right", "COMM_ServiceInterphone"
            },

            // Overhead — Panel Lighting
            ["Panel Lighting"] = new List<string>
            {
                "LTS_MasterBrightSw", "LTS_MasterBrightKnob",
                "LTS_IndLightsTest", "LTS_DomeLight",
                "LTS_CircuitBreakerLight", "LTS_OverheadPanel",
                "LTS_GlareshieldPanel", "LTS_GlareshieldFlood",
                "LTS_EmerLights"
            },

            // Overhead — Cargo Temperature
            ["Cargo Temperature"] = new List<string>
            {
                "AIR_CargoTempFwd", "AIR_CargoTempAft",
                "AIR_CargoTempMainDeckFwd", "AIR_CargoTempMainDeckAft",
                "AIR_CargoTempLowerFwd", "AIR_CargoTempLowerAft"
            },

            // Overhead Maintenance — Flight Controls
            ["Flight Controls"] = new List<string>
            {
                "FCTL_WingHydValve_L", "FCTL_WingHydValve_R", "FCTL_WingHydValve_C",
                "FCTL_TailHydValve_L", "FCTL_TailHydValve_R", "FCTL_TailHydValve_C",
                "FCTL_PrimFltComputers"
            },

            // Overhead Maintenance — Backup Systems
            ["Backup Systems"] = new List<string>
            {
                "ICE_BackupWindowHeat_L", "ICE_BackupWindowHeat_R",
                "ELEC_TowingPower"
            },

            // Overhead Maintenance — EEC/APU Maintenance
            ["EEC/APU Maintenance"] = new List<string>
            {
                "ENG_EECTest_L", "ENG_EECTest_R", "APU_PowerTest"
            },
        };
    }

    // =========================================================================
    // Optional overrides — stubs
    // =========================================================================

    public override Dictionary<string, List<string>> GetPanelDisplayVariables() => new();
    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =========================================================================
    // Event handling overrides — scaffold (populated in Tasks 9-11)
    // =========================================================================

    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer) => false;

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        return base.ProcessSimVarUpdate(varName, value, announcer);
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager) => false;

    // =========================================================================
    // Static PMDG 777 Event ID Dictionary
    // Source: pmdg_777.json event catalog (event_base = 69632)
    // =========================================================================

    /// <summary>
    /// Maps PMDG 777 event names to their numeric event IDs.
    /// Used when sending events via the PMDG SDK control area.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, int> EventIds =
        new Dictionary<string, int>
        {
            // -----------------------------------------------------------------
            // OVERHEAD — Electrical
            // -----------------------------------------------------------------
            ["EVT_OH_ELEC_BATTERY_SWITCH"]        = 69633,
            ["EVT_OH_ELEC_APU_GEN_SWITCH"]        = 69634,
            ["EVT_OH_ELEC_APU_SEL_SWITCH"]        = 69635,
            ["EVT_OH_ELEC_BUS_TIE1_SWITCH"]       = 69637,
            ["EVT_OH_ELEC_BUS_TIE2_SWITCH"]       = 69638,
            ["EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"]   = 69640,
            ["EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"]    = 69639,
            ["EVT_OH_ELEC_GEN1_SWITCH"]            = 69641,
            ["EVT_OH_ELEC_GEN2_SWITCH"]            = 69642,
            ["EVT_OH_ELEC_BACKUP_GEN1_SWITCH"]     = 69643,
            ["EVT_OH_ELEC_BACKUP_GEN2_SWITCH"]     = 69644,
            ["EVT_OH_ELEC_DISCONNECT1_SWITCH"]     = 69645,
            ["EVT_OH_ELEC_DISCONNECT1_GUARD"]      = 69646,
            ["EVT_OH_ELEC_DISCONNECT2_SWITCH"]     = 69647,
            ["EVT_OH_ELEC_DISCONNECT2_GUARD"]      = 69648,
            ["EVT_OH_ELEC_IFE"]                    = 69649,
            ["EVT_OH_ELEC_CAB_UTIL"]               = 69650,
            ["EVT_OH_ELEC_STBY_PWR_SWITCH"]        = 69713,
            ["EVT_OH_ELEC_STBY_PWR_GUARD"]         = 69714,
            ["EVT_OH_ELEC_GND_TEST_SWITCH"]        = 69784,
            ["EVT_OH_ELEC_GND_TEST_GUARD"]         = 69785,
            ["EVT_OH_ELEC_TOWING_PWR_SWITCH"]      = 69782,
            ["EVT_OH_ELEC_TOWING_PWR_GUARD"]       = 69783,

            // -----------------------------------------------------------------
            // OVERHEAD — Hydraulic
            // -----------------------------------------------------------------
            ["EVT_OH_HYD_DEMAND_ELEC1"]            = 69667,
            ["EVT_OH_HYD_AIR1"]                    = 69668,
            ["EVT_OH_HYD_AIR2"]                    = 69669,
            ["EVT_OH_HYD_DEMAND_ELEC2"]            = 69670,
            ["EVT_OH_HYD_ENG1"]                    = 69671,
            ["EVT_OH_HYD_ELEC1"]                   = 69672,
            ["EVT_OH_HYD_ELEC2"]                   = 69673,
            ["EVT_OH_HYD_ENG2"]                    = 69674,
            ["EVT_OH_HYD_RAM_AIR"]                 = 69675,
            ["EVT_OH_HYD_RAM_AIR_COVER"]           = 69676,
            ["EVT_OH_HYD_VLV_PWR_WING_L"]          = 69692,
            ["EVT_OH_HYD_VLV_PWR_WING_L_GUARD"]    = 69693,
            ["EVT_OH_HYD_VLV_PWR_WING_C"]          = 69695,
            ["EVT_OH_HYD_VLV_PWR_WING_C_GUARD"]    = 69696,
            ["EVT_OH_HYD_VLV_PWR_WING_R"]          = 69698,
            ["EVT_OH_HYD_VLV_PWR_WING_R_GUARD"]    = 69699,
            ["EVT_OH_HYD_VLV_PWR_TAIL_L"]          = 69701,
            ["EVT_OH_HYD_VLV_PWR_TAIL_L_GUARD"]    = 69702,
            ["EVT_OH_HYD_VLV_PWR_TAIL_C"]          = 69703,
            ["EVT_OH_HYD_VLV_PWR_TAIL_C_GUARD"]    = 69704,
            ["EVT_OH_HYD_VLV_PWR_TAIL_R"]          = 69706,
            ["EVT_OH_HYD_VLV_PWR_TAIL_R_GUARD"]    = 69707,

            // -----------------------------------------------------------------
            // OVERHEAD — Fuel
            // -----------------------------------------------------------------
            ["EVT_OH_FUEL_JETTISON_NOZZLE_L"]      = 69729,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_L_GUARD"] = 69730,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_R"]      = 69731,
            ["EVT_OH_FUEL_JETTISON_NOZZLE_R_GUARD"] = 69732,
            ["EVT_OH_FUEL_TO_REMAIN_ROTATE"]       = 69733,
            ["EVT_OH_FUEL_TO_REMAIN_PULL"]         = 70643,
            ["EVT_OH_FUEL_JETTISON_ARM"]           = 69734,
            ["EVT_OH_FUEL_PUMP_1_FORWARD"]         = 69735,
            ["EVT_OH_FUEL_PUMP_2_FORWARD"]         = 69736,
            ["EVT_OH_FUEL_PUMP_1_AFT"]             = 69737,
            ["EVT_OH_FUEL_PUMP_2_AFT"]             = 69738,
            ["EVT_OH_FUEL_CROSSFEED_FORWARD"]      = 69739,
            ["EVT_OH_FUEL_CROSSFEED_AFT"]          = 69740,
            ["EVT_OH_FUEL_PUMP_L_CENTER"]          = 69741,
            ["EVT_OH_FUEL_PUMP_R_CENTER"]          = 69742,
            ["EVT_OH_FUEL_PUMP_AUX"]               = 70669,

            // -----------------------------------------------------------------
            // OVERHEAD — Engines
            // -----------------------------------------------------------------
            ["EVT_OH_EEC_L_SWITCH"]                = 69722,
            ["EVT_OH_EEC_L_GUARD"]                 = 69723,
            ["EVT_OH_EEC_R_SWITCH"]                = 69724,
            ["EVT_OH_EEC_R_GUARD"]                 = 69725,
            ["EVT_OH_ENGINE_L_START"]              = 69726,
            ["EVT_OH_ENGINE_R_START"]              = 69727,
            ["EVT_OH_ENGINE_AUTOSTART"]            = 69728,
            ["EVT_OH_EEC_TEST_L_SWITCH"]           = 69793,
            ["EVT_OH_EEC_TEST_L_SWITCH_GUARD"]     = 69794,
            ["EVT_OH_EEC_TEST_R_SWITCH"]           = 69795,
            ["EVT_OH_EEC_TEST_R_SWITCH_GUARD"]     = 69796,

            // -----------------------------------------------------------------
            // OVERHEAD — Bleed Air
            // -----------------------------------------------------------------
            ["EVT_OH_BLEED_ENG_1_SWITCH"]          = 69761,
            ["EVT_OH_BLEED_ENG_2_SWITCH"]          = 69762,
            ["EVT_OH_BLEED_APU_SWITCH"]            = 69763,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_L"] = 69764,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_C"] = 69765,
            ["EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_R"] = 69766,

            // -----------------------------------------------------------------
            // OVERHEAD — Air Conditioning
            // -----------------------------------------------------------------
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_FLT_DECK"]      = 69771,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CABIN"]         = 69772,
            ["EVT_OH_AIRCOND_RESET_SWITCH"]                = 69773,
            ["EVT_OH_AIRCOND_RECIRC_FAN_UPP_SWITCH"]       = 69774,
            ["EVT_OH_AIRCOND_RECIRC_FAN_LWR_SWITCH"]       = 69775,
            ["EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH"]        = 69776,
            ["EVT_OH_AIRCOND_GASPER_SWITCH"]               = 69777,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_AFT"]     = 69780,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_BULK"]    = 69781,
            ["EVT_OH_AIRCOND_PACK_SWITCH_L"]               = 69767,
            ["EVT_OH_AIRCOND_PACK_SWITCH_R"]               = 69768,
            ["EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L"]           = 69769,
            ["EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R"]           = 69770,
            ["EVT_OH_AIRCOND_RECIRC_FANS_SWITCH"]          = 70684,
            ["EVT_OH_AIRCOND_MAIN_DECK_FLOW_SWITCH"]       = 70685,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_FWD"] = 70682,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_AFT"] = 70683,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_FWD"] = 70686,
            ["EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_AFT"] = 70687,
            ["EVT_OH_AIRCOND_ALT_VENT_SWITCH"]             = 70689,
            ["EVT_OH_AIRCOND_ALT_VENT_GUARD"]              = 70743,

            // -----------------------------------------------------------------
            // OVERHEAD — Pressurization
            // -----------------------------------------------------------------
            ["EVT_OH_PRESS_VALVE_SWITCH_MANUAL_1"] = 69756,
            ["EVT_OH_PRESS_VALVE_SWITCH_MANUAL_2"] = 69757,
            ["EVT_OH_PRESS_LAND_ALT_KNOB_ROTATE"]  = 69758,
            ["EVT_OH_PRESS_LAND_ALT_KNOB_PULL"]    = 70893,
            ["EVT_OH_PRESS_VALVE_SWITCH_1"]        = 69759,
            ["EVT_OH_PRESS_VALVE_SWITCH_2"]        = 69760,
            ["EVT_OH_PRESS_VALVE_SWITCHES_MANUAL"] = 70883,

            // -----------------------------------------------------------------
            // OVERHEAD — Anti-Ice
            // -----------------------------------------------------------------
            ["EVT_OH_ICE_WING_ANTIICE"]            = 69743,
            ["EVT_OH_ICE_ENGINE_ANTIICE_1"]        = 69744,
            ["EVT_OH_ICE_ENGINE_ANTIICE_2"]        = 69745,
            ["EVT_OH_ICE_WINDOW_HEAT_1"]           = 69677,
            ["EVT_OH_ICE_WINDOW_HEAT_2"]           = 69678,
            ["EVT_OH_ICE_WINDOW_HEAT_3"]           = 69679,
            ["EVT_OH_ICE_WINDOW_HEAT_4"]           = 69680,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_L"]        = 69709,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_L_GUARD"]  = 69710,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_R"]        = 69711,
            ["EVT_OH_ICE_BU_WINDOW_HEAT_R_GUARD"]  = 69712,

            // -----------------------------------------------------------------
            // OVERHEAD — Fire
            // -----------------------------------------------------------------
            ["EVT_OH_FIRE_CARGO_ARM_FWD"]          = 69717,
            ["EVT_OH_FIRE_CARGO_ARM_AFT"]          = 69718,
            ["EVT_OH_FIRE_CARGO_ARM_MAIN_DECK"]    = 70706,
            ["EVT_OH_FIRE_CARGO_DISCH"]            = 69719,
            ["EVT_OH_FIRE_CARGO_DISCH_GUARD"]      = 69720,
            ["EVT_OH_FIRE_CARGO_DISCH_DEPR"]       = 70707,
            ["EVT_OH_FIRE_OVHT_TEST"]              = 69721,
            ["EVT_OH_FIRE_HANDLE_APU_TOP"]         = 69716,
            ["EVT_OH_FIRE_HANDLE_APU_BOTTOM"]      = 78033,
            ["EVT_OH_FIRE_UNLOCK_SWITCH_APU"]      = 78034,
            ["EVT_FIRE_HANDLE_ENGINE_1_TOP"]       = 70283,
            ["EVT_FIRE_HANDLE_ENGINE_1_BOTTOM"]    = 76143,
            ["EVT_FIRE_UNLOCK_SWITCH_ENGINE_1"]    = 76144,
            ["EVT_FIRE_HANDLE_ENGINE_2_TOP"]       = 70284,
            ["EVT_FIRE_HANDLE_ENGINE_2_BOTTOM"]    = 76153,
            ["EVT_FIRE_UNLOCK_SWITCH_ENGINE_2"]    = 76154,

            // -----------------------------------------------------------------
            // OVERHEAD — Lights
            // -----------------------------------------------------------------
            ["EVT_OH_LIGHTS_NAV"]                  = 69747,
            ["EVT_OH_LIGHTS_BEACON"]               = 69746,
            ["EVT_OH_LIGHTS_STROBE"]               = 69754,
            ["EVT_OH_LIGHTS_LOGO"]                 = 69748,
            ["EVT_OH_LIGHTS_WING"]                 = 69749,
            ["EVT_OH_LIGHTS_IND_LTS_SWITCH"]       = 69750,
            ["EVT_OH_LIGHTS_L_TURNOFF"]            = 69751,
            ["EVT_OH_LIGHTS_R_TURNOFF"]            = 69752,
            ["EVT_OH_LIGHTS_LR_TURNOFF"]           = 70833,
            ["EVT_OH_LIGHTS_TAXI"]                 = 69753,
            ["EVT_OH_LIGHTS_LANDING_L"]            = 69654,
            ["EVT_OH_LIGHTS_LANDING_R"]            = 69656,
            ["EVT_OH_LIGHTS_LANDING_NOSE"]         = 69655,
            ["EVT_OH_LIGHTS_LANDING_LNR"]          = 71973,
            ["EVT_OH_LIGHTS_STORM"]                = 69659,
            ["EVT_OH_CAMERA_LTS_SWITCH"]           = 69651,

            // -----------------------------------------------------------------
            // OVERHEAD — Signs
            // -----------------------------------------------------------------
            ["EVT_OH_FASTEN_BELTS_LIGHT_SWITCH"]   = 69662,
            ["EVT_OH_NO_SMOKING_LIGHT_SWITCH"]     = 69661,
            ["EVT_OH_EMER_EXIT_LIGHT_SWITCH"]      = 69681,
            ["EVT_OH_EMER_EXIT_LIGHT_GUARD"]       = 69682,

            // -----------------------------------------------------------------
            // OVERHEAD — Wipers
            // -----------------------------------------------------------------
            ["EVT_OH_WIPER_LEFT_SWITCH"]           = 69652,
            ["EVT_OH_WIPER_RIGHT_SWITCH"]          = 69755,

            // -----------------------------------------------------------------
            // OVERHEAD — Panel Lighting
            // -----------------------------------------------------------------
            ["EVT_OH_PANEL_LIGHT_CONTROL"]         = 69657,
            ["EVT_OH_DOME_SWITCH"]                 = 69658,
            ["EVT_OH_MASTER_BRIGHT_ROTATE"]        = 69660,
            ["EVT_OH_MASTER_BRIGHT_PUSH"]          = 72433,
            ["EVT_OH_GS_PANEL_LIGHT_CONTROL"]      = 69653,
            ["EVT_OH_GS_FLOOD_LIGHT_CONTROL"]      = 71733,
            ["EVT_OH_CB_LIGHT_CONTROL"]            = 72133,

            // -----------------------------------------------------------------
            // OVERHEAD — Miscellaneous (maintenance-area panels)
            // -----------------------------------------------------------------
            ["EVT_OH_ADIRU_SWITCH"]                = 69691,
            ["EVT_OH_THRUST_ASYM_COMP"]            = 69686,
            ["EVT_OH_PRIM_FLT_COMPUTERS"]          = 69687,
            ["EVT_OH_PRIM_FLT_COMPUTERS_GUARD"]    = 69688,
            ["EVT_OH_SERVICE_INTERPHONE_SWITCH"]   = 69683,
            ["EVT_OH_OXY_PASS_SWITCH"]             = 69684,
            ["EVT_OH_OXY_PASS_GUARD"]              = 69685,
            ["EVT_OH_OXY_SUPRNMRY_SWITCH"]         = 70708,
            ["EVT_OH_OXY_SUPRNMRY_GUARD"]          = 70709,
            ["EVT_OH_APU_TEST_SWITCH"]             = 69791,
            ["EVT_OH_APU_TEST_SWITCH_GUARD"]       = 69792,
            ["EVT_OH_CVR_TEST"]                    = 69788,
            ["EVT_OH_CVR_ERASE"]                   = 69789,

            // -----------------------------------------------------------------
            // GLARESHIELD — MCP (Mode Control Panel)
            // -----------------------------------------------------------------
            ["EVT_MCP_FD_SWITCH_L"]                = 69834,
            ["EVT_MCP_FD_SWITCH_R"]                = 69862,
            ["EVT_MCP_AT_ARM_SWITCH_L"]            = 69836,
            ["EVT_MCP_AT_ARM_SWITCH_R"]            = 69837,
            ["EVT_MCP_AT_SWITCH"]                  = 69839,
            ["EVT_MCP_CLB_CON_SWITCH"]             = 69838,
            ["EVT_MCP_LNAV_SWITCH"]                = 69843,
            ["EVT_MCP_VNAV_SWITCH"]                = 69844,
            ["EVT_MCP_LVL_CHG_SWITCH"]             = 69845,
            ["EVT_MCP_HDG_HOLD_SWITCH"]            = 69851,
            ["EVT_MCP_VS_FPA_SWITCH"]              = 69852,
            ["EVT_MCP_ALT_HOLD_SWITCH"]            = 69858,
            ["EVT_MCP_LOC_SWITCH"]                 = 69859,
            ["EVT_MCP_APP_SWITCH"]                 = 69860,
            ["EVT_MCP_AP_L_SWITCH"]                = 69835,
            ["EVT_MCP_AP_R_SWITCH"]                = 69861,
            ["EVT_MCP_IAS_SET"]                    = 84134,
            ["EVT_MCP_MACH_SET"]                   = 84135,
            ["EVT_MCP_HDGTRK_SET"]                 = 84136,
            ["EVT_MCP_ALT_SET"]                    = 84137,
            ["EVT_MCP_VS_SET"]                     = 84138,
            ["EVT_MCP_FPA_SET"]                    = 84139,
            ["EVT_MCP_SPEED_PUSH_SWITCH"]          = 71732,
            ["EVT_MCP_HEADING_PUSH_SWITCH"]        = 69850,
            ["EVT_MCP_ALTITUDE_PUSH_SWITCH"]       = 71883,
            ["EVT_MCP_VS_SWITCH"]                  = 69855,
            ["EVT_MCP_IAS_MACH_SWITCH"]            = 69840,
            ["EVT_MCP_HDG_TRK_SWITCH"]             = 69848,
            ["EVT_MCP_BANK_ANGLE_SELECTOR"]        = 71813,
            ["EVT_MCP_ALT_INCR_SELECTOR"]          = 69857,
            ["EVT_MCP_DISENGAGE_BAR"]              = 69846,
            ["EVT_MCP_SPEED_SELECTOR"]             = 69842,
            ["EVT_MCP_HEADING_SELECTOR"]           = 71812,
            ["EVT_MCP_ALTITUDE_SELECTOR"]          = 71882,
            ["EVT_MCP_VS_SELECTOR"]                = 69854,
            ["EVT_MCP_TOGA_SCREW_L"]               = 74633,
            ["EVT_MCP_TOGA_SCREW_R"]               = 74634,

            // -----------------------------------------------------------------
            // GLARESHIELD — EFIS Captain
            // -----------------------------------------------------------------
            ["EVT_EFIS_CPT_MINIMUMS_RADIO_BARO"]   = 69813,
            ["EVT_EFIS_CPT_MINIMUMS"]              = 69814,
            ["EVT_EFIS_CPT_MINIMUMS_RST"]          = 69815,
            ["EVT_EFIS_CPT_VOR_ADF_SELECTOR_L"]    = 69816,
            ["EVT_EFIS_CPT_MODE"]                  = 69817,
            ["EVT_EFIS_CPT_MODE_CTR"]              = 69818,
            ["EVT_EFIS_CPT_RANGE"]                 = 69819,
            ["EVT_EFIS_CPT_RANGE_TFC"]             = 69820,
            ["EVT_EFIS_CPT_VOR_ADF_SELECTOR_R"]    = 69821,
            ["EVT_EFIS_CPT_BARO_IN_HPA"]           = 69822,
            ["EVT_EFIS_CPT_BARO"]                  = 69823,
            ["EVT_EFIS_CPT_BARO_STD"]              = 69824,
            ["EVT_EFIS_CPT_FPV"]                   = 69825,
            ["EVT_EFIS_CPT_MTRS"]                  = 69826,
            ["EVT_EFIS_CPT_WXR"]                   = 69827,
            ["EVT_EFIS_CPT_STA"]                   = 69828,
            ["EVT_EFIS_CPT_WPT"]                   = 69829,
            ["EVT_EFIS_CPT_ARPT"]                  = 69830,
            ["EVT_EFIS_CPT_DATA"]                  = 69831,
            ["EVT_EFIS_CPT_POS"]                   = 69832,
            ["EVT_EFIS_CPT_TERR"]                  = 69833,

            // -----------------------------------------------------------------
            // GLARESHIELD — EFIS First Officer
            // -----------------------------------------------------------------
            ["EVT_EFIS_FO_MINIMUMS_RADIO_BARO"]    = 69880,
            ["EVT_EFIS_FO_MINIMUMS"]               = 69881,
            ["EVT_EFIS_FO_MINIMUMS_RST"]           = 69882,
            ["EVT_EFIS_FO_VOR_ADF_SELECTOR_L"]     = 69883,
            ["EVT_EFIS_FO_MODE"]                   = 69884,
            ["EVT_EFIS_FO_MODE_CTR"]               = 69885,
            ["EVT_EFIS_FO_RANGE"]                  = 69886,
            ["EVT_EFIS_FO_RANGE_TFC"]              = 69887,
            ["EVT_EFIS_FO_VOR_ADF_SELECTOR_R"]     = 69888,
            ["EVT_EFIS_FO_BARO_IN_HPA"]            = 69889,
            ["EVT_EFIS_FO_BARO"]                   = 69890,
            ["EVT_EFIS_FO_BARO_STD"]               = 69891,
            ["EVT_EFIS_FO_FPV"]                    = 69892,
            ["EVT_EFIS_FO_MTRS"]                   = 69893,
            ["EVT_EFIS_FO_WXR"]                    = 69894,
            ["EVT_EFIS_FO_STA"]                    = 69895,
            ["EVT_EFIS_FO_WPT"]                    = 69896,
            ["EVT_EFIS_FO_ARPT"]                   = 69897,
            ["EVT_EFIS_FO_POS"]                    = 69899,
            ["EVT_EFIS_FO_TERR"]                   = 69900,
            ["EVT_EFIS_FO_DATA"]                   = 72293,
            ["EVT_EFIS_HDG_REF_SWITCH"]            = 69945,
            ["EVT_EFIS_HDG_REF_GUARD"]             = 69946,

            // -----------------------------------------------------------------
            // GLARESHIELD — Display Select Panel (DSP)
            // -----------------------------------------------------------------
            ["EVT_DSP_L_INBD_SWITCH"]              = 69863,
            ["EVT_DSP_R_INBD_SWITCH"]              = 69864,
            ["EVT_DSP_LWR_CTR_SWITCH"]             = 69865,
            ["EVT_DSP_ENG_SWITCH"]                 = 69866,
            ["EVT_DSP_STAT_SWITCH"]                = 69867,
            ["EVT_DSP_ELEC_SWITCH"]                = 69868,
            ["EVT_DSP_HYD_SWITCH"]                 = 69869,
            ["EVT_DSP_FUEL_SWITCH"]                = 69870,
            ["EVT_DSP_AIR_SWITCH"]                 = 69871,
            ["EVT_DSP_DOOR_SWITCH"]                = 69872,
            ["EVT_DSP_GEAR_SWITCH"]                = 69873,
            ["EVT_DSP_FCTL_SWITCH"]                = 69874,
            ["EVT_DSP_CAM_SWITCH"]                 = 69875,
            ["EVT_DSP_CHKL_SWITCH"]                = 69876,
            ["EVT_DSP_COMM_SWITCH"]                = 69877,
            ["EVT_DSP_NAV_SWITCH"]                 = 69878,
            ["EVT_DSP_CANC_RCL_SWITCH"]            = 69879,
            ["EVT_DSP_INDB_DSPL_L"]                = 69947,
            ["EVT_DSP_INDB_DSPL_R"]                = 69922,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Landing Gear
            // -----------------------------------------------------------------
            ["EVT_GEAR_LEVER"]                     = 69927,
            ["EVT_GEAR_LEVER_UNLOCK"]              = 69928,
            ["EVT_GEAR_ALTN_GEAR_DOWN"]            = 69925,
            ["EVT_GEAR_ALTN_GEAR_DOWN_GUARD"]      = 69926,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Brakes / Autobrake
            // -----------------------------------------------------------------
            ["EVT_ABS_AUTOBRAKE_SELECTOR"]         = 69924,

            // -----------------------------------------------------------------
            // FORWARD PANEL — GPWS
            // -----------------------------------------------------------------
            ["EVT_GPWS_TERR_OVRD_SWITCH"]          = 69929,
            ["EVT_GPWS_TERR_OVRD_GUARD"]           = 69930,
            ["EVT_GPWS_GEAR_OVRD_SWITCH"]          = 69931,
            ["EVT_GPWS_GEAR_OVRD_GUARD"]           = 69932,
            ["EVT_GPWS_FLAP_OVRD_SWITCH"]          = 69933,
            ["EVT_GPWS_FLAP_OVRD_GUARD"]           = 69934,
            ["EVT_GPWS_GS_INHIBIT_SWITCH"]         = 69935,
            ["EVT_GPWS_RWY_OVRD_SWITCH"]           = 70741,
            ["EVT_GPWS_RWY_OVRD_GUARD"]            = 70742,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Instruments (ISFD)
            // -----------------------------------------------------------------
            ["EVT_ISFD_APP"]                       = 70442,
            ["EVT_ISFD_HP_IN"]                     = 70443,
            ["EVT_ISFD_PLUS"]                      = 70444,
            ["EVT_ISFD_MINUS"]                     = 70445,
            ["EVT_ISFD_ATT_RST"]                   = 70446,
            ["EVT_ISFD_BARO"]                      = 70447,
            ["EVT_ISFD_BARO_PUSH"]                 = 70448,

            // -----------------------------------------------------------------
            // PEDESTAL — Control Stand
            // -----------------------------------------------------------------
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER"]        = 70130,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_DOWN"]   = 74613,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM"]    = 74614,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_UP"]     = 74615,
            ["EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_50"]     = 74616,
            ["EVT_CONTROL_STAND_REV_THRUST1_LEVER"]        = 70131,
            ["EVT_CONTROL_STAND_TOGA1_SWITCH"]             = 70132,
            ["EVT_CONTROL_STAND_FWD_THRUST1_LEVER"]        = 70133,
            ["EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH"]     = 70134,
            ["EVT_CONTROL_STAND_REV_THRUST2_LEVER"]        = 70135,
            ["EVT_CONTROL_STAND_TOGA2_SWITCH"]             = 70136,
            ["EVT_CONTROL_STAND_FWD_THRUST2_LEVER"]        = 70137,
            ["EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH"]     = 70138,
            ["EVT_CONTROL_STAND_FLAPS_LEVER"]              = 70139,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_0"]            = 74703,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_1"]            = 74704,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_5"]            = 74705,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_15"]           = 74706,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_20"]           = 74707,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_25"]           = 74708,
            ["EVT_CONTROL_STAND_FLAPS_LEVER_30"]           = 74709,
            ["EVT_CONTROL_STAND_ALT_PITCH_TRIM_LEVER"]     = 70128,
            ["EVT_CONTROL_STAND_PARK_BRAKE_LEVER"]         = 70147,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C"]      = 70149,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C_GUARD"] = 70148,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R"]      = 70151,
            ["EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R_GUARD"] = 70150,
            ["EVT_CONTROL_STAND_ENG1_START_LEVER"]         = 70152,
            ["EVT_CONTROL_STAND_ENG2_START_LEVER"]         = 70153,

            // -----------------------------------------------------------------
            // PEDESTAL — Alternate Flaps
            // -----------------------------------------------------------------
            ["EVT_ALTN_FLAPS_ARM"]                 = 70142,
            ["EVT_ALTN_FLAPS_ARM_GUARD"]           = 70143,
            ["EVT_ALTN_FLAPS_POS"]                 = 70144,

            // -----------------------------------------------------------------
            // PEDESTAL — CDU Left
            // -----------------------------------------------------------------
            ["EVT_CDU_L_L1"]                       = 69960,
            ["EVT_CDU_L_L2"]                       = 69961,
            ["EVT_CDU_L_L3"]                       = 69962,
            ["EVT_CDU_L_L4"]                       = 69963,
            ["EVT_CDU_L_L5"]                       = 69964,
            ["EVT_CDU_L_L6"]                       = 69965,
            ["EVT_CDU_L_R1"]                       = 69966,
            ["EVT_CDU_L_R2"]                       = 69967,
            ["EVT_CDU_L_R3"]                       = 69968,
            ["EVT_CDU_L_R4"]                       = 69969,
            ["EVT_CDU_L_R5"]                       = 69970,
            ["EVT_CDU_L_R6"]                       = 69971,
            ["EVT_CDU_L_INIT_REF"]                 = 69972,
            ["EVT_CDU_L_RTE"]                      = 69973,
            ["EVT_CDU_L_DEP_ARR"]                  = 69974,
            ["EVT_CDU_L_ALTN"]                     = 69975,
            ["EVT_CDU_L_VNAV"]                     = 69976,
            ["EVT_CDU_L_FIX"]                      = 69977,
            ["EVT_CDU_L_LEGS"]                     = 69978,
            ["EVT_CDU_L_HOLD"]                     = 69979,
            ["EVT_CDU_L_PROG"]                     = 69980,
            ["EVT_CDU_L_EXEC"]                     = 69981,
            ["EVT_CDU_L_MENU"]                     = 69982,
            ["EVT_CDU_L_NAV_RAD"]                  = 69983,
            ["EVT_CDU_L_PREV_PAGE"]                = 69984,
            ["EVT_CDU_L_NEXT_PAGE"]                = 69985,
            ["EVT_CDU_L_1"]                        = 69986,
            ["EVT_CDU_L_2"]                        = 69987,
            ["EVT_CDU_L_3"]                        = 69988,
            ["EVT_CDU_L_4"]                        = 69989,
            ["EVT_CDU_L_5"]                        = 69990,
            ["EVT_CDU_L_6"]                        = 69991,
            ["EVT_CDU_L_7"]                        = 69992,
            ["EVT_CDU_L_8"]                        = 69993,
            ["EVT_CDU_L_9"]                        = 69994,
            ["EVT_CDU_L_DOT"]                      = 69995,
            ["EVT_CDU_L_0"]                        = 69996,
            ["EVT_CDU_L_PLUS_MINUS"]               = 69997,
            ["EVT_CDU_L_A"]                        = 69998,
            ["EVT_CDU_L_B"]                        = 69999,
            ["EVT_CDU_L_C"]                        = 70000,
            ["EVT_CDU_L_D"]                        = 70001,
            ["EVT_CDU_L_E"]                        = 70002,
            ["EVT_CDU_L_F"]                        = 70003,
            ["EVT_CDU_L_G"]                        = 70004,
            ["EVT_CDU_L_H"]                        = 70005,
            ["EVT_CDU_L_I"]                        = 70006,
            ["EVT_CDU_L_J"]                        = 70007,
            ["EVT_CDU_L_K"]                        = 70008,
            ["EVT_CDU_L_L"]                        = 70009,
            ["EVT_CDU_L_M"]                        = 70010,
            ["EVT_CDU_L_N"]                        = 70011,
            ["EVT_CDU_L_O"]                        = 70012,
            ["EVT_CDU_L_P"]                        = 70013,
            ["EVT_CDU_L_Q"]                        = 70014,
            ["EVT_CDU_L_R"]                        = 70015,
            ["EVT_CDU_L_S"]                        = 70016,
            ["EVT_CDU_L_T"]                        = 70017,
            ["EVT_CDU_L_U"]                        = 70018,
            ["EVT_CDU_L_V"]                        = 70019,
            ["EVT_CDU_L_W"]                        = 70020,
            ["EVT_CDU_L_X"]                        = 70021,
            ["EVT_CDU_L_Y"]                        = 70022,
            ["EVT_CDU_L_Z"]                        = 70023,
            ["EVT_CDU_L_SPACE"]                    = 70024,
            ["EVT_CDU_L_DEL"]                      = 70025,
            ["EVT_CDU_L_SLASH"]                    = 70026,
            ["EVT_CDU_L_CLR"]                      = 70027,
            ["EVT_CDU_L_BRITENESS"]                = 70032,
            ["EVT_CDU_L_FMCCOMM"]                  = 73103,

            // -----------------------------------------------------------------
            // PEDESTAL — Misc
            // -----------------------------------------------------------------
            ["EVT_PED_DSPL_CTRL_SOURCE_C"]         = 70110,
            ["EVT_PED_EICAS_EVENT_RCD"]            = 70111,
            ["EVT_PED_UPPER_BRIGHT_CONTROL"]       = 70112,
            ["EVT_PED_LOWER_BRIGHT_CONTROL"]       = 70113,
            ["EVT_PED_LOWER_TERR_BRIGHT_CONTROL"]  = 74443,
            ["EVT_PED_L_CCD_SIDE"]                 = 70114,
            ["EVT_PED_L_CCD_INBD"]                 = 70115,
            ["EVT_PED_L_CCD_LWR"]                  = 70116,
            ["EVT_PED_R_CCD_SIDE"]                 = 70123,
            ["EVT_PED_R_CCD_INBD"]                 = 70122,
            ["EVT_PED_R_CCD_LWR"]                  = 70121,
            ["EVT_PED_OBS_AUDIO_SELECTOR"]         = 70280,
            ["EVT_PED_FLOOR_LIGHTS"]               = 70367,
            ["EVT_PED_PANEL_LIGHT_CONTROL"]        = 70368,
            ["EVT_PED_FLOOD_LIGHT_CONTROL"]        = 70369,
            ["EVT_PED_EVAC_SWITCH"]                = 70371,
            ["EVT_PED_EVAC_SWITCH_GUARD"]          = 70372,
            ["EVT_PED_EVAC_HORN_SHUTOFF"]          = 70373,
            ["EVT_PED_EVAC_TEST_SWITCH"]           = 70374,
            ["EVT_PED_CALL_GND"]                   = 70710,
            ["EVT_PED_CALL_CREW_REST"]             = 70711,
            ["EVT_PED_CALL_SUPRNMRY"]              = 70712,
            ["EVT_PED_CALL_CARGO"]                 = 70713,
            ["EVT_PED_CALL_CARGO_AUDIO"]           = 70714,
            ["EVT_PED_CALL_MAIN_DK_ALERT"]         = 70715,

            // -----------------------------------------------------------------
            // FORWARD PANEL — FMC Selector
            // -----------------------------------------------------------------
            ["EVT_FWD_FMC_SELECTOR"]               = 69923,
        };
}
