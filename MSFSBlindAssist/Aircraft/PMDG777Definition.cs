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
                "Lights", "Signs", "Wipers", "Panel Lighting", "ADIRU"
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
                "Landing Gear", "Brakes", "GPWS", "Instruments"
            },
            ["Pedestal"] = new List<string>
            {
                "Control Stand", "Transponder/TCAS",
                "CDU", "Evacuation", "Warning", "Engine Fire", "Radio", "Calls"
            },
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_APU_Start"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_APU_Start",
                DisplayName = "APU Start",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Press twice to start the APU."
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
                Name = "ELEC_annunExtPowr_ON_0",
                DisplayName = "External Power Primary",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ELEC_ExtPwrSec"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_ON_1",
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
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["ELEC_IDGDisc_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_IDGDiscSw_1",
                DisplayName = "IDG Disconnect 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunAPU_GEN_OFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunAPU_GEN_OFF",
                DisplayName = "APU GEN OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunAPU_FAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunAPU_FAULT",
                DisplayName = "APU FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunBusTieISLN_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBusTieISLN_0",
                DisplayName = "Bus Tie 1 ISLN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunBusTieISLN_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBusTieISLN_1",
                DisplayName = "Bus Tie 2 ISLN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunExtPwrAVAIL_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_AVAIL_0",
                DisplayName = "Ext Power 1 AVAIL Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunExtPwrAVAIL_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunExtPowr_AVAIL_1",
                DisplayName = "Ext Power 2 AVAIL Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunGenOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunGenOFF_0",
                DisplayName = "Generator 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunGenOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunGenOFF_1",
                DisplayName = "Generator 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunBackupGenOFF_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBackupGenOFF_0",
                DisplayName = "Backup Generator 1 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunBackupGenOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunBackupGenOFF_1",
                DisplayName = "Backup Generator 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunIDGDiscDRIVE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIDGDiscDRIVE_0",
                DisplayName = "IDG 1 Disc Drive Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunIDGDiscDRIVE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIDGDiscDRIVE_1",
                DisplayName = "IDG 2 Disc Drive Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunCabUtilOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunCabUtilOFF",
                DisplayName = "Cabin Utility OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunIFEPassSeatsOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunIFEPassSeatsOFF",
                DisplayName = "IFE Pass Seats OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ELEC_annunTowingPowerON_BATT"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_annunTowingPowerON_BATT",
                DisplayName = "Towing Power ON BATT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunPrimEngPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryEngPumpFAULT_1",
                DisplayName = "Primary Engine Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunPrimElecPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryElecPumpFAULT_0",
                DisplayName = "Primary Electric Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunPrimElecPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunPrimaryElecPumpFAULT_1",
                DisplayName = "Primary Electric Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunDemandElecPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandElecPumpFAULT_0",
                DisplayName = "Demand Electric Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunDemandElecPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandElecPumpFAULT_1",
                DisplayName = "Demand Electric Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunDemandAirPumpFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandAirPumpFAULT_0",
                DisplayName = "Demand Air Pump 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunDemandAirPumpFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunDemandAirPumpFAULT_1",
                DisplayName = "Demand Air Pump 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunRATPress"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunRamAirTurbinePRESS",
                DisplayName = "RAM Air Turbine PRESS Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["HYD_annunRATUnlkd"] = new SimConnect.SimVarDefinition
            {
                Name = "HYD_annunRamAirTurbineUNLKD",
                DisplayName = "RAM Air Turbine UNLKD Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                DisplayName = "Fuel To Remain Pull",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["FUEL_FuelToRemainSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_FuelToRemain_Selector",
                DisplayName = "Fuel To Remain Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Decr", [1] = "Neutral", [2] = "Incr" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunAftXFEED_VALVE"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunAftXFEED_VALVE",
                DisplayName = "Aft XFEED VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Fwd_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Fwd_0",
                DisplayName = "LOW PRESS Fwd 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Fwd_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Fwd_1",
                DisplayName = "LOW PRESS Fwd 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Aft_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Aft_0",
                DisplayName = "LOW PRESS Aft 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Aft_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Aft_1",
                DisplayName = "LOW PRESS Aft 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Ctr_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Ctr_0",
                DisplayName = "LOW PRESS Center 1 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunLOWPRESS_Ctr_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunLOWPRESS_Ctr_1",
                DisplayName = "LOW PRESS Center 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunJettisonNozzleVALVE_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunJettisonNozleVALVE_0",
                DisplayName = "Jettison Nozzle 1 VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunJettisonNozzleVALVE_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunJettisonNozleVALVE_1",
                DisplayName = "Jettison Nozzle 2 VALVE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_annunArmFAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_annunArmFAULT",
                DisplayName = "Arm FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FUEL_AuxPump"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_PumpAux_Sw",
                DisplayName = "Aux Fuel Pump",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "Start", [1] = "Norm" }
            },
            ["ENG_StartSelector_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_Start_Selector_1",
                DisplayName = "Start Selector Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Start", [1] = "Norm" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ENG_annunALTN_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_annunALTN_1",
                DisplayName = "EEC ALTN Right Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ENG_annunAutostartOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_annunAutostartOFF",
                DisplayName = "Autostart OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                DisplayName = "Engine 1 Bleed OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunEngBleedOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunEngBleedAirOFF_1",
                DisplayName = "Engine 2 Bleed OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunAPUBleedOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunAPUBleedAirOFF",
                DisplayName = "APU Bleed OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunIsolationValveCLOSED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunIsolationValveCLOSED_0",
                DisplayName = "Isolation Valve L CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunIsolationValveCLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunIsolationValveCLOSED_1",
                DisplayName = "Isolation Valve R CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunCtrIsolationValveCLOSED"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunCtrIsolationValveCLOSED",
                DisplayName = "Center Isolation Valve CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cold", [35] = "Neutral", [60] = "Warm", [70] = "Manual" }
            },
            ["AIR_TempSelectorCabin"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_TempSelector_1",
                DisplayName = "Temp Selector Cabin",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cold", [35] = "Neutral", [60] = "Warm", [70] = "Manual" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunPackOFF_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunPackOFF_1",
                DisplayName = "Pack 2 OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunTrimAirFAULT_1"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunTrimAirFAULT_0",
                DisplayName = "Trim Air 1 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunTrimAirFAULT_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunTrimAirFAULT_1",
                DisplayName = "Trim Air 2 FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunEquipCoolingOVRD"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunEquipCoolingOVRD",
                DisplayName = "Equipment Cooling OVRD Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunAltnVentFAULT"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunAltnVentFAULT",
                DisplayName = "Alt Vent FAULT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_MainDeckFlow"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_MainDeckFlowSw_NORM",
                DisplayName = "Main Deck Flow",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "High", [1] = "Normal" }
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
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Decr", [1] = "Neutral", [2] = "Incr" }
            },
            ["AIR_LdgAltPulled"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_LdgAlt_Sw_Pulled",
                DisplayName = "Landing Altitude Pull",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["AIR_OutflowValve_Fwd"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_OutflowValve_Sw_AUTO_0",
                DisplayName = "Outflow Valve Fwd",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
            },
            ["AIR_OutflowValve_Aft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_OutflowValve_Sw_AUTO_1",
                DisplayName = "Outflow Valve Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Manual", [1] = "Auto" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["AIR_annunOutflowValveMAN_2"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_annunOutflowValve_MAN_1",
                DisplayName = "Outflow Valve 2 MAN Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_1",
                DisplayName = "Window Heat 2 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_3"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_2",
                DisplayName = "Window Heat 3 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["ICE_annunWindowHeatINOP_4"] = new SimConnect.SimVarDefinition
            {
                Name = "ICE_annunWindowHeatINOP_3",
                DisplayName = "Window Heat 4 INOP Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
            ["FIRE_CargoFireArmMainDeck"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_CargoFire_Sw_MainDeckArm",
                DisplayName = "Main Deck Cargo Fire Arm",
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
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["FIRE_CargoDepr"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_CargoDepr",
                DisplayName = "Cargo Depressurization",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["FIRE_FireOvhtTest"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_FireOvhtTest_Sw",
                DisplayName = "Fire Overheat Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
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
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            // ENGINE FIRE HANDLES (Pedestal)
            ["FIRE_EngineHandle_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandle_0",
                DisplayName = "Engine 1 Fire Handle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal", [1] = "Pulled", [2] = "Disch Left", [3] = "Disch Right"
                }
            },
            ["FIRE_EngineHandle_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandle_1",
                DisplayName = "Engine 2 Fire Handle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Normal", [1] = "Pulled", [2] = "Disch Left", [3] = "Disch Right"
                }
            },
            ["FIRE_EngineHandleUnlock_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleUnlock_Sw_0",
                DisplayName = "Engine 1 Handle Unlock",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["FIRE_EngineHandleUnlock_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleUnlock_Sw_1",
                DisplayName = "Engine 2 Handle Unlock",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["FIRE_annunENG_BTL_DISCH_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunENG_BTL_DISCH_0",
                DisplayName = "Engine 1 Bottle Discharged",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FIRE_annunENG_BTL_DISCH_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunENG_BTL_DISCH_1",
                DisplayName = "Engine 2 Bottle Discharged",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FIRE_EngineHandleIsUnlocked_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIsUnlocked_0",
                DisplayName = "Engine 1 Handle Unlocked",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["FIRE_EngineHandleIsUnlocked_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIsUnlocked_1",
                DisplayName = "Engine 2 Handle Unlocked",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunCargoFire_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoFire_1",
                DisplayName = "Cargo Fire 2 Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunCargoDISCH"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoDISCH",
                DisplayName = "Cargo DISCH Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunAPU_BTL_DISCH"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunAPU_BTL_DISCH",
                DisplayName = "APU Bottle DISCH Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunEngHandle_1"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIlluminated_0",
                DisplayName = "Engine 1 Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunEngHandle_2"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_EngineHandleIlluminated_1",
                DisplayName = "Engine 2 Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunAPUHandleIllum"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_APUHandleIlluminated",
                DisplayName = "APU Fire Handle Illuminated",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunMainDeckCargoFire"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunMainDeckCargoFire",
                DisplayName = "Main Deck Cargo Fire Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FIRE_annunCargoDEPR"] = new SimConnect.SimVarDefinition
            {
                Name = "FIRE_annunCargoDEPR",
                DisplayName = "Cargo DEPR Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
            ["LTS_CameraLights"] = new SimConnect.SimVarDefinition
            {
                Name = "LTS_Camera_LTS_Sw_ON",
                DisplayName = "Camera Lights",
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
            ["OXY_Suprnmry"] = new SimConnect.SimVarDefinition
            {
                Name = "OXY_Suprnmry_Sw_On",
                DisplayName = "Supernumerary Oxygen",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "Test", [1] = "Bright", [2] = "Dim" }
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
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Low", [2] = "High" }
            },
            ["AIR_CargoTempAft"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_CargoTemp_Selector_1",
                DisplayName = "Cargo Temp Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Low", [2] = "High" }
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
            // OVERHEAD — ADIRU
            // =================================================================
            ["ADIRU_Switch"] = new SimConnect.SimVarDefinition
            {
                Name = "ADIRU_Sw_On",
                DisplayName = "ADIRU Switch",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ADIRU_annunOFF"] = new SimConnect.SimVarDefinition
            {
                Name = "ADIRU_annunOFF",
                DisplayName = "ADIRU OFF Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["ADIRU_annunON_BAT"] = new SimConnect.SimVarDefinition
            {
                Name = "ADIRU_annunON_BAT",
                DisplayName = "ADIRU ON BAT Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
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
            ["FCTL_ThrustAsymComp"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_ThrustAsymComp_Sw_AUTO",
                DisplayName = "Thrust Asymmetry Comp",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Auto" }
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
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunWingHydVALVE_CLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunWingHydVALVE_CLOSED_1",
                DisplayName = "Wing Hyd Valve Right CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunWingHydVALVE_CLOSED_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunWingHydVALVE_CLOSED_2",
                DisplayName = "Wing Hyd Valve Center CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_L"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_0",
                DisplayName = "Tail Hyd Valve Left CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_1",
                DisplayName = "Tail Hyd Valve Right CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunTailHydVALVE_CLOSED_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunTailHydVALVE_CLOSED_2",
                DisplayName = "Tail Hyd Valve Center CLOSED Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["FCTL_annunPrimFltComputersDISC"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_annunPrimFltComputersDISC",
                DisplayName = "Primary Flight Computers DISC Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
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
            ["CVR_Test"] = new SimConnect.SimVarDefinition
            {
                Name = "CVR_Test",
                DisplayName = "CVR Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CVR_Erase"] = new SimConnect.SimVarDefinition
            {
                Name = "CVR_Erase",
                DisplayName = "CVR Erase",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["ELEC_GndTest"] = new SimConnect.SimVarDefinition
            {
                Name = "ELEC_GndTest",
                DisplayName = "Ground Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // OVERHEAD MAINTENANCE — EEC/APU MAINTENANCE
            // =================================================================
            ["ENG_EECTest_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECPower_Sw_TEST_0",
                DisplayName = "EEC Test Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["ENG_EECTest_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_EECPower_Sw_TEST_1",
                DisplayName = "EEC Test Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["APU_PowerTest"] = new SimConnect.SimVarDefinition
            {
                Name = "APU_Power_Sw_TEST",
                DisplayName = "APU Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },

            // =================================================================
            // GLARESHIELD — EFIS CAPTAIN (index 0)
            // =================================================================
            ["EFIS_MinsSelBARO_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsSelBARO_0",
                DisplayName = "Minimums Type (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Radio", [1] = "Baro" }
            },
            ["EFIS_BaroSelHPA_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroSelHPA_0",
                DisplayName = "Baro Unit (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "IN", [1] = "HPA" }
            },
            ["EFIS_VORADFSel1_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_VORADFSel1_0",
                DisplayName = "VOR/ADF 1 (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "VOR", [1] = "Off", [2] = "ADF" }
            },
            ["EFIS_VORADFSel2_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_VORADFSel2_0",
                DisplayName = "VOR/ADF 2 (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "VOR", [1] = "Off", [2] = "ADF" }
            },
            ["EFIS_ModeSel_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ModeSel_0",
                DisplayName = "Mode (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "APP", [1] = "VOR", [2] = "MAP", [3] = "PLAN" }
            },
            ["EFIS_RangeSel_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_RangeSel_0",
                DisplayName = "Range (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "10", [1] = "20", [2] = "40", [3] = "80", [4] = "160", [5] = "320", [6] = "640" }
            },
            ["EFIS_MinsKnob_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsKnob_0",
                DisplayName = "Mins Knob (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["EFIS_BaroKnob_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroKnob_0",
                DisplayName = "Baro Knob (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["EFIS_MinsRST_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsRST_Sw_Pushed_0",
                DisplayName = "Mins RST (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_BaroSTD_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroSTD_Sw_Pushed_0",
                DisplayName = "Baro STD (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_ModeCTR_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ModeCTR_Sw_Pushed_0",
                DisplayName = "Mode CTR (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_RangeTFC_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_RangeTFC_Sw_Pushed_0",
                DisplayName = "Range TFC (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_WXR_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_WXR_Sw_Pushed_0",
                DisplayName = "WXR (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_STA_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_STA_Sw_Pushed_0",
                DisplayName = "STA (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_WPT_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_WPT_Sw_Pushed_0",
                DisplayName = "WPT (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_ARPT_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ARPT_Sw_Pushed_0",
                DisplayName = "ARPT (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_DATA_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_DATA_Sw_Pushed_0",
                DisplayName = "DATA (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_POS_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_POS_Sw_Pushed_0",
                DisplayName = "POS (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_TERR_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_TERR_Sw_Pushed_0",
                DisplayName = "TERR (Capt)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_FPV_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_FPV_Capt",
                DisplayName = "FPV",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["EFIS_MTRS_Capt"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MTRS_Capt",
                DisplayName = "MTRS",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // GLARESHIELD — EFIS FIRST OFFICER (index 1)
            // =================================================================
            ["EFIS_MinsSelBARO_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsSelBARO_1",
                DisplayName = "Minimums Type (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Radio", [1] = "Baro" }
            },
            ["EFIS_BaroSelHPA_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroSelHPA_1",
                DisplayName = "Baro Unit (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "IN", [1] = "HPA" }
            },
            ["EFIS_VORADFSel1_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_VORADFSel1_1",
                DisplayName = "VOR/ADF 1 (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "VOR", [1] = "Off", [2] = "ADF" }
            },
            ["EFIS_VORADFSel2_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_VORADFSel2_1",
                DisplayName = "VOR/ADF 2 (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "VOR", [1] = "Off", [2] = "ADF" }
            },
            ["EFIS_ModeSel_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ModeSel_1",
                DisplayName = "Mode (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "APP", [1] = "VOR", [2] = "MAP", [3] = "PLAN" }
            },
            ["EFIS_RangeSel_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_RangeSel_1",
                DisplayName = "Range (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "10", [1] = "20", [2] = "40", [3] = "80", [4] = "160", [5] = "320", [6] = "640" }
            },
            ["EFIS_MinsKnob_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsKnob_1",
                DisplayName = "Mins Knob (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["EFIS_BaroKnob_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroKnob_1",
                DisplayName = "Baro Knob (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["EFIS_MinsRST_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MinsRST_Sw_Pushed_1",
                DisplayName = "Mins RST (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_BaroSTD_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_BaroSTD_Sw_Pushed_1",
                DisplayName = "Baro STD (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_ModeCTR_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ModeCTR_Sw_Pushed_1",
                DisplayName = "Mode CTR (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_RangeTFC_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_RangeTFC_Sw_Pushed_1",
                DisplayName = "Range TFC (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_WXR_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_WXR_Sw_Pushed_1",
                DisplayName = "WXR (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_STA_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_STA_Sw_Pushed_1",
                DisplayName = "STA (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_WPT_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_WPT_Sw_Pushed_1",
                DisplayName = "WPT (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_ARPT_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_ARPT_Sw_Pushed_1",
                DisplayName = "ARPT (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_DATA_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_DATA_Sw_Pushed_1",
                DisplayName = "DATA (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_POS_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_POS_Sw_Pushed_1",
                DisplayName = "POS (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_TERR_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_TERR_Sw_Pushed_1",
                DisplayName = "TERR (FO)",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["EFIS_FPV_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_FPV_FO",
                DisplayName = "FPV",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["EFIS_MTRS_FO"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_MTRS_FO",
                DisplayName = "MTRS",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // GLARESHIELD — MODE CONTROL PANEL (MCP) — Read-only displays
            // =================================================================
            ["MCP_IASMach"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_IASMach",
                DisplayName = "Speed",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },
            ["MCP_Heading"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_Heading",
                DisplayName = "Heading",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },
            ["MCP_Altitude"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_Altitude",
                DisplayName = "Altitude",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },
            ["MCP_VertSpeed"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VertSpeed",
                DisplayName = "Vertical Speed",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },

            // MCP — Boolean switches
            ["MCP_FD_L"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_FD_Sw_On_0",
                DisplayName = "Flight Director Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["MCP_FD_R"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_FD_Sw_On_1",
                DisplayName = "Flight Director Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["MCP_ATArm_L"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_ATArm_Sw_On_0",
                DisplayName = "AT Arm Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["MCP_ATArm_R"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_ATArm_Sw_On_1",
                DisplayName = "AT Arm Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["MCP_AltIncrSel"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_AltIncrSel",
                DisplayName = "Alt Increment",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "1000" }
            },
            ["MCP_DisengageBar"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_DisengageBar",
                DisplayName = "Disengage Bar",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Up", [1] = "Down" }
            },

            // MCP — Selectors
            ["MCP_BankLimitSel"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_BankLimitSel",
                DisplayName = "Bank Limit",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "5", [2] = "10", [3] = "15", [4] = "20", [5] = "25" }
            },
            ["MCP_HDGDialMode"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_HDGDial_Mode",
                DisplayName = "HDG/TRK Mode",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "HDG", [1] = "TRK" }
            },
            ["MCP_VSDialMode"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VSDial_Mode",
                DisplayName = "VS/FPA Mode",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "VS", [1] = "FPA" }
            },

            // MCP — Mode engage buttons (momentary)
            ["MCP_LNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_LNAV_Sw_Pushed",
                DisplayName = "LNAV",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_VNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VNAV_Sw_Pushed",
                DisplayName = "VNAV",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_FLCH"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_FLCH_Sw_Pushed",
                DisplayName = "FLCH",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_HDG_HOLD"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_HDG_HOLD_Sw_Pushed",
                DisplayName = "HDG HOLD",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_VS_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VS_FPA_Sw_Pushed",
                DisplayName = "VS/FPA",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_ALT_HOLD"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_ALT_HOLD_Sw_Pushed",
                DisplayName = "ALT HOLD",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_LOC"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_LOC_Sw_Pushed",
                DisplayName = "LOC",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_APP"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_APP_Sw_Pushed",
                DisplayName = "APP",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_AT"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_AT_Sw_Pushed",
                DisplayName = "A/T",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_CLB_CON"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_CLB_CON_Sw_Pushed",
                DisplayName = "CLB CON",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_AP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_AP_Sw_Pushed_0",
                DisplayName = "AP Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_AP_R"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_AP_Sw_Pushed_1",
                DisplayName = "AP Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_SpeedPush"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_Speeed_Sw_Pushed",
                DisplayName = "Speed Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_HeadingPush"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_Heading_Sw_Pushed",
                DisplayName = "Heading Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_AltitudePush"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_Altitude_Sw_Pushed",
                DisplayName = "Altitude Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_IAS_MACH_Toggle"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_IAS_MACH_Toggle_Sw_Pushed",
                DisplayName = "IAS/MACH Toggle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_HDG_TRK_Toggle"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_HDG_TRK_Toggle_Sw_Pushed",
                DisplayName = "HDG/TRK Toggle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_VS_FPA_Toggle"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VS_FPA_Toggle_Sw_Pushed",
                DisplayName = "VS/FPA Toggle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },

            ["MCP_CRS_L_Push"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_CRS_L_Push",
                DisplayName = "Course Left Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["MCP_CRS_R_Push"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_CRS_R_Push",
                DisplayName = "Course Right Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // MCP — Annunciators (monitored, not in panel controls)
            ["MCP_annunAP_L"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunAP_0",
                DisplayName = "AP Left Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunAP_R"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunAP_1",
                DisplayName = "AP Right Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunAT"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunAT",
                DisplayName = "A/T Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunLNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunLNAV",
                DisplayName = "LNAV Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunVNAV"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunVNAV",
                DisplayName = "VNAV Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunFLCH"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunFLCH",
                DisplayName = "FLCH Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunHDG_HOLD"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunHDG_HOLD",
                DisplayName = "HDG HOLD Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunVS_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunVS_FPA",
                DisplayName = "VS/FPA Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunALT_HOLD"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunALT_HOLD",
                DisplayName = "ALT HOLD Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunLOC"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunLOC",
                DisplayName = "LOC Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["MCP_annunAPP"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_annunAPP",
                DisplayName = "APP Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            // MCP — Display state variables (monitored, not in panel controls)
            ["MCP_IASBlank"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_IASBlank",
                DisplayName = "IAS Blank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["MCP_VertSpeedBlank"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_VertSpeedBlank",
                DisplayName = "Vertical Speed Blank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["MCP_FPA"] = new SimConnect.SimVarDefinition
            {
                Name = "MCP_FPA",
                DisplayName = "Flight Path Angle",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // =================================================================
            // GLARESHIELD — DISPLAY SELECT PANEL (DSP)
            // =================================================================
            ["DSP_L_INBD"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_L_INBD_Sw",
                DisplayName = "L INBD",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_R_INBD"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_R_INBD_Sw",
                DisplayName = "R INBD",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_LWR_CTR"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_LWR_CTR_Sw",
                DisplayName = "LWR CTR",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_ENG"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_ENG_Sw",
                DisplayName = "ENG",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_STAT"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_STAT_Sw",
                DisplayName = "STAT",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_ELEC"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_ELEC_Sw",
                DisplayName = "ELEC",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_HYD"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_HYD_Sw",
                DisplayName = "HYD",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_FUEL"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_FUEL_Sw",
                DisplayName = "FUEL",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_AIR"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_AIR_Sw",
                DisplayName = "AIR",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_DOOR"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_DOOR_Sw",
                DisplayName = "DOOR",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_GEAR"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_GEAR_Sw",
                DisplayName = "GEAR",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_FCTL"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_FCTL_Sw",
                DisplayName = "FCTL",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_CAM"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_CAM_Sw",
                DisplayName = "CAM",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_CHKL"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_CHKL_Sw",
                DisplayName = "CHKL",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_COMM"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_COMM_Sw",
                DisplayName = "COMM",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_NAV"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_NAV_Sw",
                DisplayName = "NAV",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["DSP_CANC_RCL"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_CANC_RCL_Sw",
                DisplayName = "CANC/RCL",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true,
                IsMomentary = true
            },
            // DSP — Annunciators (monitored, not in panel controls)
            ["DSP_annunL_INBD"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_annunL_INBD",
                DisplayName = "L INBD Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["DSP_annunR_INBD"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_annunR_INBD",
                DisplayName = "R INBD Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["DSP_annunLWR_CTR"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_annunLWR_CTR",
                DisplayName = "LWR CTR Annunciator",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // GLARESHIELD — WARNING
            // =================================================================
            ["WARN_Reset_L"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_Reset_Sw_Pushed_0",
                DisplayName = "Master Warning Reset Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["WARN_Reset_R"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_Reset_Sw_Pushed_1",
                DisplayName = "Master Warning Reset Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["WARN_annunMasterWarning_L"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_annunMASTER_WARNING_0",
                DisplayName = "Master Warning Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["WARN_annunMasterWarning_R"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_annunMASTER_WARNING_1",
                DisplayName = "Master Warning Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["WARN_annunMasterCaution_L"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_annunMASTER_CAUTION_0",
                DisplayName = "Master Caution Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["WARN_annunMasterCaution_R"] = new SimConnect.SimVarDefinition
            {
                Name = "WARN_annunMASTER_CAUTION_1",
                DisplayName = "Master Caution Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // FORWARD PANEL — LANDING GEAR
            // =================================================================
            ["GEAR_Lever"] = new SimConnect.SimVarDefinition
            {
                Name = "GEAR_Lever",
                DisplayName = "Gear Lever",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Up", [1] = "Down" }
            },
            ["GEAR_LockOvrd"] = new SimConnect.SimVarDefinition
            {
                Name = "GEAR_LockOvrd_Sw",
                DisplayName = "Gear Lock Override",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
            },
            ["GEAR_AltnGearDown"] = new SimConnect.SimVarDefinition
            {
                Name = "GEAR_AltnGear_Sw_DOWN",
                DisplayName = "Alternate Gear Down",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Down" }
            },

            // =================================================================
            // FORWARD PANEL — BRAKES
            // =================================================================
            ["BRAKES_AutobrakeSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "BRAKES_AutobrakeSelector",
                DisplayName = "Autobrake Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "RTO", [1] = "Off", [2] = "1", [3] = "2",
                    [4] = "3", [5] = "4", [6] = "Auto"
                }
            },
            ["BRAKES_ParkingBrake"] = new SimConnect.SimVarDefinition
            {
                Name = "BRAKES_ParkingBrakeLeverOn",
                DisplayName = "Parking Brake",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["BRAKES_annunBRAKE_SOURCE"] = new SimConnect.SimVarDefinition
            {
                Name = "BRAKES_annunBRAKE_SOURCE",
                DisplayName = "Brake Source Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // FORWARD PANEL — GPWS
            // =================================================================
            ["GPWS_TerrInhibit"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_TerrInhibitSw_OVRD",
                DisplayName = "GPWS Terrain Inhibit",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
            },
            ["GPWS_GearInhibit"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_GearInhibitSw_OVRD",
                DisplayName = "GPWS Gear Inhibit",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
            },
            ["GPWS_FlapInhibit"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_FlapInhibitSw_OVRD",
                DisplayName = "GPWS Flap Inhibit",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
            },
            ["GPWS_GSInhibit"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_GSInhibit_Sw",
                DisplayName = "GPWS G/S Inhibit",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Inhibit" }
            },
            ["GPWS_RunwayOvrd"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_RunwayOvrdSw_OVRD",
                DisplayName = "GPWS Runway Override",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Override" }
            },
            ["GPWS_annunGND_PROX_top"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_annunGND_PROX_top",
                DisplayName = "GND PROX Top Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["GPWS_annunGND_PROX_bottom"] = new SimConnect.SimVarDefinition
            {
                Name = "GPWS_annunGND_PROX_bottom",
                DisplayName = "GND PROX Bottom Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // FORWARD PANEL — INSTRUMENTS
            // =================================================================
            ["ISP_Nav_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_Nav_L_Sw_CDU",
                DisplayName = "Nav Source Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "FMC", [1] = "VOR" }
            },
            ["ISP_DsplCtrl_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_DsplCtrl_L_Sw_Altn",
                DisplayName = "Display Control Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["ISP_AirDataAtt_L"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_AirDataAtt_L_Sw_Altn",
                DisplayName = "Air Data Attitude Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["ISP_Nav_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_Nav_R_Sw_CDU",
                DisplayName = "Nav Source Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "FMC", [1] = "VOR" }
            },
            ["ISP_DsplCtrl_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_DsplCtrl_R_Sw_Altn",
                DisplayName = "Display Control Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["ISP_AirDataAtt_R"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_AirDataAtt_R_Sw_Altn",
                DisplayName = "Air Data Attitude Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["ISP_DsplCtrl_C"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_DsplCtrl_C_Sw_Altn",
                DisplayName = "Display Control Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["ISP_FMC_Selector"] = new SimConnect.SimVarDefinition
            {
                Name = "ISP_FMC_Selector",
                DisplayName = "FMC Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Left", [1] = "Auto", [2] = "Right" }
            },
            ["DSP_InbdDspl_L"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_InbdDspl_L_Selector",
                DisplayName = "Inboard Display Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "ND", [1] = "NAV", [2] = "MFD", [3] = "EICAS" }
            },
            ["DSP_InbdDspl_R"] = new SimConnect.SimVarDefinition
            {
                Name = "DSP_InbdDspl_R_Selector",
                DisplayName = "Inboard Display Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "EICAS", [1] = "MFD", [2] = "ND", [3] = "PFD" }
            },
            ["EFIS_HdgRef"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_HdgRef_Sw_Norm",
                DisplayName = "Heading Reference",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "True", [1] = "Normal" }
            },
            ["EFIS_annunHdgRefTRUE"] = new SimConnect.SimVarDefinition
            {
                Name = "EFIS_annunHdgRefTRUE",
                DisplayName = "Heading Ref TRUE Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            // ISFD momentary buttons
            ["ISFD_Baro"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_Baro_Sw_Pushed",
                DisplayName = "ISFD Baro",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["ISFD_RST"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_RST_Sw_Pushed",
                DisplayName = "ISFD RST",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["ISFD_Minus"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_Minus_Sw_Pushed",
                DisplayName = "ISFD Minus",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["ISFD_Plus"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_Plus_Sw_Pushed",
                DisplayName = "ISFD Plus",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["ISFD_APP"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_APP_Sw_Pushed",
                DisplayName = "ISFD APP",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["ISFD_HP_IN"] = new SimConnect.SimVarDefinition
            {
                Name = "ISFD_HP_IN_Sw_Pushed",
                DisplayName = "ISFD HP/IN",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },


            // =================================================================
            // PEDESTAL — CONTROL STAND
            // =================================================================
            ["FCTL_Speedbrake"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_Speedbrake_Lever",
                DisplayName = "Speed Brake",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["FCTL_Flaps"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_Flaps_Lever",
                DisplayName = "Flaps",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Up", [1] = "1", [2] = "5", [3] = "15",
                    [4] = "20", [5] = "25", [6] = "30"
                }
            },
            ["FCTL_AltnFlapsArm"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_AltnFlaps_Sw_ARM",
                DisplayName = "Alternate Flaps Arm",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Arm" }
            },
            ["FCTL_AltnFlapsControl"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_AltnFlaps_Control_Sw",
                DisplayName = "Alternate Flaps Control",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Retract", [1] = "Off", [2] = "Extend" }
            },
            ["FCTL_StabCutout_C"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_StabCutOutSw_C_NORMAL",
                DisplayName = "Stab Cutout Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutout", [1] = "Normal" }
            },
            ["FCTL_StabCutout_R"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_StabCutOutSw_R_NORMAL",
                DisplayName = "Stab Cutout Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutout", [1] = "Normal" }
            },
            ["FCTL_AltnPitch"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_AltnPitch_Lever",
                DisplayName = "Alternate Pitch Trim",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Nose Down", [1] = "Neutral", [2] = "Nose Up" }
            },
            ["ENG_FuelControl_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_FuelControl_Sw_RUN_0",
                DisplayName = "Fuel Control 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }
            },
            ["ENG_FuelControl_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_FuelControl_Sw_RUN_1",
                DisplayName = "Fuel Control 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Cutoff", [1] = "Run" }
            },

            ["ENG_TOGA_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_TOGA_1",
                DisplayName = "TOGA Switch 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["ENG_TOGA_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_TOGA_2",
                DisplayName = "TOGA Switch 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["ENG_ATDisengage_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_ATDisengage_1",
                DisplayName = "AT Disengage 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["ENG_ATDisengage_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_ATDisengage_2",
                DisplayName = "AT Disengage 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // FORWARD PANEL — Yoke / Standby Instruments
            // =================================================================
            ["YOKE_APDisc"] = new SimConnect.SimVarDefinition
            {
                Name = "YOKE_APDisc",
                DisplayName = "Yoke AP Disconnect",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["STBY_ASI_Push"] = new SimConnect.SimVarDefinition
            {
                Name = "STBY_ASI_Push",
                DisplayName = "Standby ASI Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["STBY_ALT_Push"] = new SimConnect.SimVarDefinition
            {
                Name = "STBY_ALT_Push",
                DisplayName = "Standby Altimeter Push",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // PEDESTAL — TRANSPONDER/TCAS
            // =================================================================
            ["XPDR_XpndrSelector"] = new SimConnect.SimVarDefinition
            {
                Name = "XPDR_XpndrSelector_R",
                DisplayName = "Transponder Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "L", [1] = "R" }
            },
            ["XPDR_AltSource"] = new SimConnect.SimVarDefinition
            {
                Name = "XPDR_AltSourceSel_ALTN",
                DisplayName = "Altitude Source",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Normal", [1] = "Altn" }
            },
            ["XPDR_ModeSel"] = new SimConnect.SimVarDefinition
            {
                Name = "XPDR_ModeSel",
                DisplayName = "Mode Selector",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Stby", [1] = "Alt Rptg Off", [2] = "Xpndr",
                    [3] = "TA Only", [4] = "TA/RA"
                }
            },
            ["XPDR_Ident"] = new SimConnect.SimVarDefinition
            {
                Name = "XPDR_Ident_Sw_Pushed",
                DisplayName = "IDENT",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["XPDR_Test"] = new SimConnect.SimVarDefinition
            {
                Name = "XPDR_Test",
                DisplayName = "TCAS Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["TRANSPONDER_CODE_SET"] = new SimConnect.SimVarDefinition
            {
                Name = "TRANSPONDER CODE:1",
                DisplayName = "Squawk Code",
                Type = SimConnect.SimVarType.SimVar,
                Units = "BCO16",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // =================================================================
            // PEDESTAL — COMMUNICATION
            // =================================================================
            ["COMM_RadioTransfer_1"] = new SimConnect.SimVarDefinition
            {
                Name = "COMM_RadioTransfer_Sw_Pushed_0",
                DisplayName = "Radio Transfer 1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["COMM_RadioTransfer_2"] = new SimConnect.SimVarDefinition
            {
                Name = "COMM_RadioTransfer_Sw_Pushed_1",
                DisplayName = "Radio Transfer 2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            ["COMM_RadioTransfer_3"] = new SimConnect.SimVarDefinition
            {
                Name = "COMM_RadioTransfer_Sw_Pushed_2",
                DisplayName = "Radio Transfer 3",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                RenderAsButton = true
            },
            // =================================================================
            // PEDESTAL — CDU
            // =================================================================
            ["CDU_OpenCDU"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_OpenCDU",
                DisplayName = "Open CDU",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                IsAnnounced = false,
                RenderAsButton = true
            },
            ["CDU_BrtKnob_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_BrtKnob_0",
                DisplayName = "CDU Brightness Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CDU_BrtKnob_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_BrtKnob_1",
                DisplayName = "CDU Brightness Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["CDU_BrtKnob_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_BrtKnob_2",
                DisplayName = "CDU Brightness Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            // CDU annunciators
            ["CDU_annunEXEC_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunEXEC_0",
                DisplayName = "CDU EXEC Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunEXEC_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunEXEC_1",
                DisplayName = "CDU EXEC Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunEXEC_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunEXEC_2",
                DisplayName = "CDU EXEC Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunDSPY_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunDSPY_0",
                DisplayName = "CDU DSPY Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunDSPY_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunDSPY_1",
                DisplayName = "CDU DSPY Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunDSPY_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunDSPY_2",
                DisplayName = "CDU DSPY Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunFAIL_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunFAIL_0",
                DisplayName = "CDU FAIL Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunFAIL_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunFAIL_1",
                DisplayName = "CDU FAIL Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunFAIL_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunFAIL_2",
                DisplayName = "CDU FAIL Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunMSG_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunMSG_0",
                DisplayName = "CDU MSG Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunMSG_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunMSG_1",
                DisplayName = "CDU MSG Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunMSG_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunMSG_2",
                DisplayName = "CDU MSG Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunOFST_L"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunOFST_0",
                DisplayName = "CDU OFST Left",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunOFST_C"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunOFST_1",
                DisplayName = "CDU OFST Center",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },
            ["CDU_annunOFST_R"] = new SimConnect.SimVarDefinition
            {
                Name = "CDU_annunOFST_2",
                DisplayName = "CDU OFST Right",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // PEDESTAL — EICAS EVENT RECORD
            // =================================================================
            ["EICAS_EventRcd"] = new SimConnect.SimVarDefinition
            {
                Name = "EICAS_EventRcd_Sw_Pushed",
                DisplayName = "EICAS Event Record",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },

            // =================================================================
            // OVERHEAD — AIR CONDITIONING RESET
            // =================================================================
            ["AIR_AirCondReset"] = new SimConnect.SimVarDefinition
            {
                Name = "AIR_AirCondReset_Sw_Pushed",
                DisplayName = "Air Cond Reset",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },

            // =================================================================
            // PEDESTAL — FLIGHT CONTROLS (TRIM)
            // =================================================================
            ["FCTL_AileronTrim"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_AileronTrim_Switches",
                DisplayName = "Aileron Trim",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Left Wing Down", [1] = "Neutral", [2] = "Right Wing Down"
                }
            },
            ["FCTL_RudderTrim"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_RudderTrim_Knob",
                DisplayName = "Rudder Trim",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string>
                {
                    [0] = "Nose Left", [1] = "Neutral", [2] = "Nose Right"
                }
            },

            // =================================================================
            // PEDESTAL — FLIGHT CONTROLS (RUDDER TRIM CANCEL)
            // =================================================================
            ["FCTL_RudderTrimCancel"] = new SimConnect.SimVarDefinition
            {
                Name = "FCTL_RudderTrimCancel_Sw_Pushed",
                DisplayName = "Rudder Trim Cancel",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },

            // =================================================================
            // PEDESTAL — EVACUATION
            // =================================================================
            ["EVAC_Command"] = new SimConnect.SimVarDefinition
            {
                Name = "EVAC_Command_Sw_ON",
                DisplayName = "Evacuation Command",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
            },
            ["EVAC_HornShutoff"] = new SimConnect.SimVarDefinition
            {
                Name = "EVAC_HornSutOff_Sw_Pulled",
                DisplayName = "Evacuation Horn Shutoff",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["EVAC_PressToTest"] = new SimConnect.SimVarDefinition
            {
                Name = "EVAC_PressToTest_Sw_Pressed",
                DisplayName = "Evacuation Press to Test",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true,
            },
            ["EVAC_annunLight"] = new SimConnect.SimVarDefinition
            {
                Name = "EVAC_LightIlluminated",
                DisplayName = "Evacuation Light",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                OnlyAnnounceValueDescriptionMatches = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" }
            },

            // =================================================================
            // PEDESTAL — CALL PANEL
            // =================================================================
            ["CALL_Ground"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_Ground",
                DisplayName = "Call Ground",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CALL_CrewRest"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_CrewRest",
                DisplayName = "Call Crew Rest",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CALL_Suprnmry"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_Suprnmry",
                DisplayName = "Call Supernumerary",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CALL_Cargo"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_Cargo",
                DisplayName = "Call Cargo",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CALL_CargoAudio"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_CargoAudio",
                DisplayName = "Call Cargo Audio",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },
            ["CALL_MainDeckAlert"] = new SimConnect.SimVarDefinition
            {
                Name = "CALL_MainDeckAlert",
                DisplayName = "Main Deck Alert",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Never,
                RenderAsButton = true,
                IsMomentary = true
            },

            // =================================================================
            // DOORS
            // =================================================================
            ["DOOR_Entry_1L"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_0",
                DisplayName = "Entry 1L",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_1R"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_1",
                DisplayName = "Entry 1R",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_2L"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_2",
                DisplayName = "Entry 2L",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_2R"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_3",
                DisplayName = "Entry 2R",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_3L"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_4",
                DisplayName = "Entry 3L",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_3R"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_5",
                DisplayName = "Entry 3R",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_4L"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_6",
                DisplayName = "Entry 4L",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_4R"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_7",
                DisplayName = "Entry 4R",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_5L"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_8",
                DisplayName = "Entry 5L",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Entry_5R"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_9",
                DisplayName = "Entry 5R",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Cargo_Fwd"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_10",
                DisplayName = "Cargo Forward",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Cargo_Aft"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_11",
                DisplayName = "Cargo Aft",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Cargo_Bulk"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_12",
                DisplayName = "Cargo Bulk",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Cargo_Main"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_13",
                DisplayName = "Cargo Main",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_Fwd_Access"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_14",
                DisplayName = "Forward Access",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_EE_Access"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_state_15",
                DisplayName = "E/E Access",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Open", [1] = "Closed", [2] = "Closed and Armed", [3] = "Closing", [4] = "Opening" }
            },
            ["DOOR_CockpitDoor"] = new SimConnect.SimVarDefinition
            {
                Name = "DOOR_CockpitDoorOpen",
                DisplayName = "Cockpit Door",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },

            // FMC DATA (background monitoring — no panel placement)
            ["FMC_V1"] = new SimConnect.SimVarDefinition
            {
                Name = "FMC_V1",
                DisplayName = "V1",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["FMC_VR"] = new SimConnect.SimVarDefinition
            {
                Name = "FMC_VR",
                DisplayName = "VR",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["FMC_V2"] = new SimConnect.SimVarDefinition
            {
                Name = "FMC_V2",
                DisplayName = "V2",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["FMC_CruiseAlt"] = new SimConnect.SimVarDefinition
            {
                Name = "FMC_CruiseAlt",
                DisplayName = "Cruise Altitude",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },

            // MONITORING ENHANCEMENTS (background — no panel placement)
            ["MON_APURunning"] = new SimConnect.SimVarDefinition
            {
                Name = "APURunning",
                DisplayName = "APU",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Shut Down", [1] = "Running" }
            },
            ["MON_IRS_Aligned"] = new SimConnect.SimVarDefinition
            {
                Name = "IRS_aligned",
                DisplayName = "IRS",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not Aligned", [1] = "Aligned" }
            },
            ["MON_ENG_StartValve_1"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_StartValve_0",
                DisplayName = "Engine 1 Start Valve",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["MON_ENG_StartValve_2"] = new SimConnect.SimVarDefinition
            {
                Name = "ENG_StartValve_1",
                DisplayName = "Engine 2 Start Valve",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
            },
            ["MON_WheelChocks"] = new SimConnect.SimVarDefinition
            {
                Name = "WheelChocksSet",
                DisplayName = "Wheel Chocks",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Removed", [1] = "Set" }
            },
            ["MON_GroundConn"] = new SimConnect.SimVarDefinition
            {
                Name = "GroundConnAvailable",
                DisplayName = "Ground Connections",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Not Available", [1] = "Available" }
            },

            // BRAKES — panel control

            // FUEL QUANTITY (continuous monitoring for hotkey reads — no auto announcements)
            ["MON_FUEL_QtyLeft"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_QtyLeft",
                DisplayName = "Fuel Left Tank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false
            },
            ["MON_FUEL_QtyCenter"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_QtyCenter",
                DisplayName = "Fuel Center Tank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false
            },
            ["MON_FUEL_QtyRight"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_QtyRight",
                DisplayName = "Fuel Right Tank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false
            },
            ["MON_FUEL_QtyAux"] = new SimConnect.SimVarDefinition
            {
                Name = "FUEL_QtyAux",
                DisplayName = "Fuel Aux Tank",
                Type = SimConnect.SimVarType.PMDGVar,
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = false
            },

            // =================================================================
            // RADIO — COM1/COM2 Frequencies (standard SimConnect, not PMDG SDK)
            // =================================================================
            ["COM1_ActiveFreq"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:1",
                DisplayName = "COM1 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },
            ["COM_STANDBY_FREQUENCY_SET:1"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:1",
                DisplayName = "COM1 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["COM1_RADIO_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM_STBY_RADIO_SWAP",
                DisplayName = "COM1 Swap",
                Type = SimConnect.SimVarType.Event,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Swap COM1 active and standby frequencies"
            },
            ["COM2_ActiveFreq"] = new SimConnect.SimVarDefinition
            {
                Name = "COM ACTIVE FREQUENCY:2",
                DisplayName = "COM2 Active",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true,
                PreventTextInput = true
            },
            ["COM_STANDBY_FREQUENCY_SET:2"] = new SimConnect.SimVarDefinition
            {
                Name = "COM STANDBY FREQUENCY:2",
                DisplayName = "COM2 Standby",
                Type = SimConnect.SimVarType.SimVar,
                Units = "MHz",
                UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
                IsAnnounced = true
            },
            ["COM2_RADIO_SWAP"] = new SimConnect.SimVarDefinition
            {
                Name = "COM2_RADIO_SWAP",
                DisplayName = "COM2 Swap",
                Type = SimConnect.SimVarType.Event,
                RenderAsButton = true,
                IsMomentary = true,
                HelpText = "Swap COM2 active and standby frequencies"
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
                "ELEC_Battery", "ELEC_APUGen", "ELEC_APU_Selector", "ELEC_APU_Start",
                "ELEC_BusTie_1", "ELEC_BusTie_2",
                "ELEC_ExtPwrPrim", "ELEC_ExtPwrSec",
                "ELEC_Gen_1", "ELEC_Gen_2",
                "ELEC_BackupGen_1", "ELEC_BackupGen_2",
                "ELEC_IDGDisc_1", "ELEC_IDGDisc_2",
                "ELEC_CabUtil", "ELEC_IFEPassSeats", "ELEC_StandbyPwr",
                "ELEC_GndTest"
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
                "FUEL_JettisonArm", "FUEL_FuelToRemainPulled",
                "FUEL_AuxPump"
            },

            // Overhead — Engines
            ["Engines"] = new List<string>
            {
                "ENG_EECMode_L", "ENG_EECMode_R",
                "ENG_StartSelector_L", "ENG_StartSelector_R",
                "ENG_Autostart",
                "ENG_FuelControl_1", "ENG_FuelControl_2"
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
                "AIR_AirCondReset",
                "AIR_MainDeckFlow"
            },

            // Overhead — Pressurization
            ["Pressurization"] = new List<string>
            {
                "AIR_OutflowValveFwd", "AIR_OutflowValveAft",
                "AIR_LdgAltPulled",
                "AIR_OutflowValve_Fwd", "AIR_OutflowValve_Aft"
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
                "FIRE_CargoFireArmFwd", "FIRE_CargoFireArmAft", "FIRE_CargoFireArmMainDeck",
                "FIRE_CargoFireDisch", "FIRE_CargoDepr", "FIRE_FireOvhtTest",
                "FIRE_APUHandle", "FIRE_APUHandleUnlock"
            },

            // Overhead — Lights
            ["Lights"] = new List<string>
            {
                "LTS_LandingLightL", "LTS_LandingLightR", "LTS_LandingLightNose",
                "LTS_RunwayTurnoffL", "LTS_RunwayTurnoffR",
                "LTS_Taxi", "LTS_Strobe", "LTS_Beacon",
                "LTS_NAV", "LTS_Logo", "LTS_Wing", "LTS_Storm",
                "LTS_CameraLights"
            },

            // Overhead — Signs
            ["Signs"] = new List<string>
            {
                "SIGNS_NoSmoking", "SIGNS_SeatBelts", "OXY_PassOxygen",
                "OXY_Suprnmry"
            },

            // Overhead — Wipers
            ["Wipers"] = new List<string>
            {
                "WIPERS_Left", "WIPERS_Right", "COMM_ServiceInterphone"
            },

            // Overhead — Panel Lighting
            ["Panel Lighting"] = new List<string>
            {
                "LTS_MasterBrightSw",
                "LTS_IndLightsTest",
                "LTS_EmerLights"
            },

            // Overhead — ADIRU
            ["ADIRU"] = new List<string>
            {
                "ADIRU_Switch"
            },

            // Overhead Maintenance — Flight Controls
            ["Flight Controls"] = new List<string>
            {
                "FCTL_WingHydValve_L", "FCTL_WingHydValve_R", "FCTL_WingHydValve_C",
                "FCTL_TailHydValve_L", "FCTL_TailHydValve_R", "FCTL_TailHydValve_C",
                "FCTL_PrimFltComputers",
                "FCTL_ThrustAsymComp"
            },

            // Overhead Maintenance — Backup Systems
            ["Backup Systems"] = new List<string>
            {
                "ICE_BackupWindowHeat_L", "ICE_BackupWindowHeat_R",
                "ELEC_TowingPower",
                "CVR_Test", "CVR_Erase"
            },

            // Overhead Maintenance — EEC/APU Maintenance
            ["EEC/APU Maintenance"] = new List<string>
            {
                "ENG_EECTest_L", "ENG_EECTest_R", "APU_PowerTest"
            },

            // Glareshield — EFIS Captain
            ["EFIS Captain"] = new List<string>
            {
                "EFIS_MinsSelBARO_Capt", "EFIS_BaroSelHPA_Capt",
                "EFIS_VORADFSel1_Capt", "EFIS_VORADFSel2_Capt",
                "EFIS_ModeSel_Capt", "EFIS_RangeSel_Capt",
                "EFIS_MinsRST_Capt", "EFIS_BaroSTD_Capt",
                "EFIS_ModeCTR_Capt", "EFIS_RangeTFC_Capt",
                "EFIS_WXR_Capt", "EFIS_STA_Capt", "EFIS_WPT_Capt",
                "EFIS_ARPT_Capt", "EFIS_DATA_Capt", "EFIS_POS_Capt", "EFIS_TERR_Capt",
                "EFIS_FPV_Capt", "EFIS_MTRS_Capt"
            },

            // Glareshield — EFIS First Officer
            ["EFIS First Officer"] = new List<string>
            {
                "EFIS_MinsSelBARO_FO", "EFIS_BaroSelHPA_FO",
                "EFIS_VORADFSel1_FO", "EFIS_VORADFSel2_FO",
                "EFIS_ModeSel_FO", "EFIS_RangeSel_FO",
                "EFIS_MinsRST_FO", "EFIS_BaroSTD_FO",
                "EFIS_ModeCTR_FO", "EFIS_RangeTFC_FO",
                "EFIS_WXR_FO", "EFIS_STA_FO", "EFIS_WPT_FO",
                "EFIS_ARPT_FO", "EFIS_DATA_FO", "EFIS_POS_FO", "EFIS_TERR_FO",
                "EFIS_FPV_FO", "EFIS_MTRS_FO"
            },

            // Glareshield — Mode Control Panel
            ["Mode Control Panel"] = new List<string>
            {
                "MCP_IASMach", "MCP_Heading", "MCP_Altitude", "MCP_VertSpeed",
                "MCP_FD_L", "MCP_FD_R",
                "MCP_ATArm_L", "MCP_ATArm_R",
                "MCP_AltIncrSel", "MCP_DisengageBar",
                "MCP_BankLimitSel", "MCP_HDGDialMode", "MCP_VSDialMode",
                "MCP_LNAV", "MCP_VNAV", "MCP_FLCH",
                "MCP_HDG_HOLD", "MCP_VS_FPA", "MCP_ALT_HOLD",
                "MCP_LOC", "MCP_APP", "MCP_AT", "MCP_CLB_CON",
                "MCP_AP_L", "MCP_AP_R",
                "MCP_SpeedPush", "MCP_HeadingPush", "MCP_AltitudePush",
                "MCP_IAS_MACH_Toggle", "MCP_HDG_TRK_Toggle", "MCP_VS_FPA_Toggle",
                "MCP_CRS_L_Push", "MCP_CRS_R_Push",
                "YOKE_APDisc"
            },

            // Glareshield — Display Select Panel
            ["Display Select Panel"] = new List<string>
            {
                "DSP_L_INBD", "DSP_R_INBD", "DSP_LWR_CTR",
                "DSP_ENG", "DSP_STAT", "DSP_ELEC", "DSP_HYD", "DSP_FUEL",
                "DSP_AIR", "DSP_DOOR", "DSP_GEAR", "DSP_FCTL",
                "DSP_CAM", "DSP_CHKL", "DSP_COMM", "DSP_NAV", "DSP_CANC_RCL"
            },

            // Forward Panel — Landing Gear
            ["Landing Gear"] = new List<string>
            {
                "GEAR_Lever", "GEAR_LockOvrd", "GEAR_AltnGearDown"
            },

            // Forward Panel — Brakes
            ["Brakes"] = new List<string>
            {
                "BRAKES_AutobrakeSelector", "BRAKES_ParkingBrake"
            },

            // Forward Panel — GPWS
            ["GPWS"] = new List<string>
            {
                "GPWS_TerrInhibit", "GPWS_GearInhibit", "GPWS_FlapInhibit",
                "GPWS_GSInhibit", "GPWS_RunwayOvrd"
            },

            // Forward Panel — Instruments
            ["Instruments"] = new List<string>
            {
                "ISP_Nav_L", "ISP_DsplCtrl_L", "ISP_AirDataAtt_L",
                "ISP_Nav_R", "ISP_DsplCtrl_R", "ISP_AirDataAtt_R",
                "ISP_DsplCtrl_C", "ISP_FMC_Selector",
                "DSP_InbdDspl_L", "DSP_InbdDspl_R",
                "EFIS_HdgRef",
                "ISFD_Baro", "ISFD_RST", "ISFD_Minus", "ISFD_Plus", "ISFD_APP", "ISFD_HP_IN",
                "STBY_ASI_Push", "STBY_ALT_Push"
            },

            // Pedestal — Control Stand
            ["Control Stand"] = new List<string>
            {
                "FCTL_Flaps",
                "FCTL_AltnFlapsArm", "FCTL_AltnFlapsControl",
                "FCTL_StabCutout_C", "FCTL_StabCutout_R",
                "FCTL_AltnPitch", "FCTL_AileronTrim", "FCTL_RudderTrim", "FCTL_RudderTrimCancel",
                "BRAKES_ParkingBrake",
                "ENG_TOGA_1", "ENG_TOGA_2", "ENG_ATDisengage_1", "ENG_ATDisengage_2"
            },

            // Pedestal — Transponder/TCAS
            ["Transponder/TCAS"] = new List<string>
            {
                "XPDR_XpndrSelector", "XPDR_AltSource",
                "XPDR_ModeSel", "XPDR_Ident", "XPDR_Test",
                "TRANSPONDER_CODE_SET"
            },

            // Pedestal — CDU
            ["CDU"] = new List<string>
            {
                "CDU_OpenCDU",
                "EICAS_EventRcd"
            },

            // Pedestal — Evacuation
            ["Evacuation"] = new List<string>
            {
                "EVAC_Command", "EVAC_HornShutoff", "EVAC_PressToTest"
            },

            // Pedestal — Warning (Master Warning/Caution already in Glareshield Warning section)
            ["Warning"] = new List<string>
            {
                "WARN_Reset_L", "WARN_Reset_R"
            },

            // Pedestal — Engine Fire
            ["Engine Fire"] = new List<string>
            {
                "FIRE_EngineHandleUnlock_1", "FIRE_EngineHandle_1",
                "FIRE_EngineHandleUnlock_2", "FIRE_EngineHandle_2"
            },

            // Pedestal — Radio
            ["Radio"] = new List<string>
            {
                "COM1_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
                "COM2_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP"
            },

            // Pedestal — Calls
            ["Calls"] = new List<string>
            {
                "CALL_Ground", "CALL_CrewRest", "CALL_Suprnmry",
                "CALL_Cargo", "CALL_CargoAudio", "CALL_MainDeckAlert"
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

    // Track last known radio/squawk values to suppress initial load announcement.
    // Value 0 means "not yet seen" — first update stores silently, subsequent updates announce.
    private double _lastComActiveFreq1;
    private double _lastComActiveFreq2;
    private double _lastComStandbyFreq1;
    private double _lastComStandbyFreq2;
    private double _lastSquawkCode;

    // =========================================================================
    // Variable → event name mapping (simple toggle and momentary controls)
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, string> _simpleEventMap =
        new Dictionary<string, string>
        {
            // --- Electrical ---
            ["ELEC_Battery"]        = "EVT_OH_ELEC_BATTERY_SWITCH",
            ["ELEC_APUGen"]         = "EVT_OH_ELEC_APU_GEN_SWITCH",
            ["ELEC_APU_Selector"]   = "EVT_OH_ELEC_APU_SEL_SWITCH",
            ["ELEC_APU_Start"]      = "EVT_OH_ELEC_APU_SEL_SWITCH",
            ["ELEC_BusTie_1"]       = "EVT_OH_ELEC_BUS_TIE1_SWITCH",
            ["ELEC_BusTie_2"]       = "EVT_OH_ELEC_BUS_TIE2_SWITCH",
            // NOTE: PMDG event names are swapped vs annunciator array indices.
            // SEC event (69639) controls annun index 0 (primary), PRIM event (69640) controls index 1 (secondary).
            ["ELEC_ExtPwrPrim"]     = "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH",
            ["ELEC_ExtPwrSec"]      = "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH",
            ["ELEC_Gen_1"]          = "EVT_OH_ELEC_GEN1_SWITCH",
            ["ELEC_Gen_2"]          = "EVT_OH_ELEC_GEN2_SWITCH",
            ["ELEC_BackupGen_1"]    = "EVT_OH_ELEC_BACKUP_GEN1_SWITCH",
            ["ELEC_BackupGen_2"]    = "EVT_OH_ELEC_BACKUP_GEN2_SWITCH",
            ["ELEC_CabUtil"]        = "EVT_OH_ELEC_CAB_UTIL",
            ["ELEC_IFEPassSeats"]   = "EVT_OH_ELEC_IFE",
            ["ELEC_StandbyPwr"]     = "EVT_OH_ELEC_STBY_PWR_SWITCH",
            ["ELEC_TowingPower"]    = "EVT_OH_ELEC_TOWING_PWR_SWITCH",
            ["ELEC_GndTest"]        = "EVT_OH_ELEC_GND_TEST_SWITCH",

            // --- ADIRU ---
            ["ADIRU_Switch"]        = "EVT_OH_ADIRU_SWITCH",

            // --- Hydraulic ---
            ["HYD_PrimEngPump_1"]   = "EVT_OH_HYD_ENG1",
            ["HYD_PrimEngPump_2"]   = "EVT_OH_HYD_ENG2",
            ["HYD_PrimElecPump_1"]  = "EVT_OH_HYD_ELEC1",
            ["HYD_PrimElecPump_2"]  = "EVT_OH_HYD_ELEC2",
            ["HYD_DemandElecPump_1"]= "EVT_OH_HYD_DEMAND_ELEC1",
            ["HYD_DemandElecPump_2"]= "EVT_OH_HYD_DEMAND_ELEC2",
            ["HYD_DemandAirPump_1"] = "EVT_OH_HYD_AIR1",
            ["HYD_DemandAirPump_2"] = "EVT_OH_HYD_AIR2",
            ["HYD_RAT"]             = "EVT_OH_HYD_RAM_AIR",

            // --- Hydraulic valve guards (guarded toggle handled separately) ---
            ["FCTL_WingHydValve_L"] = "EVT_OH_HYD_VLV_PWR_WING_L",
            ["FCTL_WingHydValve_R"] = "EVT_OH_HYD_VLV_PWR_WING_R",
            ["FCTL_WingHydValve_C"] = "EVT_OH_HYD_VLV_PWR_WING_C",
            ["FCTL_TailHydValve_L"] = "EVT_OH_HYD_VLV_PWR_TAIL_L",
            ["FCTL_TailHydValve_R"] = "EVT_OH_HYD_VLV_PWR_TAIL_R",
            ["FCTL_TailHydValve_C"] = "EVT_OH_HYD_VLV_PWR_TAIL_C",
            ["FCTL_PrimFltComputers"]= "EVT_OH_PRIM_FLT_COMPUTERS",
            ["FCTL_ThrustAsymComp"] = "EVT_OH_THRUST_ASYM_COMP",

            // --- Fuel ---
            ["FUEL_FwdPump_1"]          = "EVT_OH_FUEL_PUMP_1_FORWARD",
            ["FUEL_FwdPump_2"]          = "EVT_OH_FUEL_PUMP_2_FORWARD",
            ["FUEL_AftPump_1"]          = "EVT_OH_FUEL_PUMP_1_AFT",
            ["FUEL_AftPump_2"]          = "EVT_OH_FUEL_PUMP_2_AFT",
            ["FUEL_CtrPump_1"]          = "EVT_OH_FUEL_PUMP_L_CENTER",
            ["FUEL_CtrPump_2"]          = "EVT_OH_FUEL_PUMP_R_CENTER",
            ["FUEL_CrossfeedFwd"]       = "EVT_OH_FUEL_CROSSFEED_FORWARD",
            ["FUEL_CrossfeedAft"]       = "EVT_OH_FUEL_CROSSFEED_AFT",
            ["FUEL_JettisonNozzleL"]    = "EVT_OH_FUEL_JETTISON_NOZZLE_L",
            ["FUEL_JettisonNozzleR"]    = "EVT_OH_FUEL_JETTISON_NOZZLE_R",
            ["FUEL_JettisonArm"]        = "EVT_OH_FUEL_JETTISON_ARM",
            ["FUEL_FuelToRemainPulled"] = "EVT_OH_FUEL_TO_REMAIN_PULL",
            ["FUEL_FuelToRemainSelector"]= "EVT_OH_FUEL_TO_REMAIN_ROTATE",
            ["FUEL_AuxPump"]            = "EVT_OH_FUEL_PUMP_AUX",

            // --- Engines ---
            ["ENG_Autostart"]       = "EVT_OH_ENGINE_AUTOSTART",
            ["ENG_StartSelector_L"] = "EVT_OH_ENGINE_L_START",
            ["ENG_StartSelector_R"] = "EVT_OH_ENGINE_R_START",
            ["ENG_EECTest_L"]       = "EVT_OH_EEC_TEST_L_SWITCH",
            ["ENG_EECTest_R"]       = "EVT_OH_EEC_TEST_R_SWITCH",
            ["ENG_FuelControl_1"]   = "EVT_CONTROL_STAND_ENG1_START_LEVER",
            ["ENG_FuelControl_2"]   = "EVT_CONTROL_STAND_ENG2_START_LEVER",
            ["APU_PowerTest"]       = "EVT_OH_APU_TEST_SWITCH",
            ["CVR_Test"]            = "EVT_OH_CVR_TEST",
            ["CVR_Erase"]           = "EVT_OH_CVR_ERASE",

            // --- Bleed Air ---
            ["AIR_EngBleed_1"]          = "EVT_OH_BLEED_ENG_1_SWITCH",
            ["AIR_EngBleed_2"]          = "EVT_OH_BLEED_ENG_2_SWITCH",
            ["AIR_APUBleed"]            = "EVT_OH_BLEED_APU_SWITCH",
            ["AIR_IsolationValve_L"]    = "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_L",
            ["AIR_IsolationValve_R"]    = "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_R",
            ["AIR_CtrIsolationValve"]   = "EVT_OH_BLEED_ISOLATION_VALVE_SWITCH_C",

            // --- Air Conditioning ---
            ["AIR_AirCondReset"]        = "EVT_OH_AIRCOND_RESET_SWITCH",
            ["AIR_Pack_1"]              = "EVT_OH_AIRCOND_PACK_SWITCH_L",
            ["AIR_Pack_2"]              = "EVT_OH_AIRCOND_PACK_SWITCH_R",
            ["AIR_TrimAir_1"]           = "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_L",
            ["AIR_TrimAir_2"]           = "EVT_OH_AIRCOND_TRIM_AIR_SWITCH_R",
            ["AIR_RecircFanUpper"]      = "EVT_OH_AIRCOND_RECIRC_FAN_UPP_SWITCH",
            ["AIR_RecircFanLower"]      = "EVT_OH_AIRCOND_RECIRC_FAN_LWR_SWITCH",
            ["AIR_EquipCooling"]        = "EVT_OH_AIRCOND_EQUIP_COOLING_SWITCH",
            ["AIR_Gasper"]              = "EVT_OH_AIRCOND_GASPER_SWITCH",
            ["AIR_AltnVent"]            = "EVT_OH_AIRCOND_ALT_VENT_SWITCH",
            ["AIR_TempSelectorFlightDeck"] = "EVT_OH_AIRCOND_TEMP_SELECTOR_FLT_DECK",
            ["AIR_TempSelectorCabin"]   = "EVT_OH_AIRCOND_TEMP_SELECTOR_CABIN",
            ["AIR_CargoTempFwd"]        = "EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_AFT",
            ["AIR_CargoTempAft"]        = "EVT_OH_AIRCOND_TEMP_SELECTOR_CARGO_BULK",
            ["AIR_CargoTempMainDeckFwd"]= "EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_FWD",
            ["AIR_CargoTempMainDeckAft"]= "EVT_OH_AIRCOND_TEMP_SELECTOR_MAIN_CARGO_AFT",
            ["AIR_CargoTempLowerFwd"]   = "EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_FWD",
            ["AIR_CargoTempLowerAft"]   = "EVT_OH_AIRCOND_TEMP_SELECTOR_LWR_CARGO_AFT",
            ["AIR_MainDeckFlow"]        = "EVT_OH_AIRCOND_MAIN_DECK_FLOW_SWITCH",

            // --- Pressurization ---
            ["AIR_OutflowValveFwd"]     = "EVT_OH_PRESS_VALVE_SWITCH_MANUAL_1",
            ["AIR_OutflowValveAft"]     = "EVT_OH_PRESS_VALVE_SWITCH_MANUAL_2",
            ["AIR_LdgAltSelector"]      = "EVT_OH_PRESS_LAND_ALT_KNOB_ROTATE",
            ["AIR_LdgAltPulled"]        = "EVT_OH_PRESS_LAND_ALT_KNOB_PULL",

            // --- Anti-Ice ---
            ["ICE_WindowHeat_1"]        = "EVT_OH_ICE_WINDOW_HEAT_1",
            ["ICE_WindowHeat_2"]        = "EVT_OH_ICE_WINDOW_HEAT_2",
            ["ICE_WindowHeat_3"]        = "EVT_OH_ICE_WINDOW_HEAT_3",
            ["ICE_WindowHeat_4"]        = "EVT_OH_ICE_WINDOW_HEAT_4",
            ["ICE_WingAntiIce"]         = "EVT_OH_ICE_WING_ANTIICE",
            ["ICE_EngAntiIce_1"]        = "EVT_OH_ICE_ENGINE_ANTIICE_1",
            ["ICE_EngAntiIce_2"]        = "EVT_OH_ICE_ENGINE_ANTIICE_2",
            ["ICE_BackupWindowHeat_L"]  = "EVT_OH_ICE_BU_WINDOW_HEAT_L",
            ["ICE_BackupWindowHeat_R"]  = "EVT_OH_ICE_BU_WINDOW_HEAT_R",

            // --- Fire ---
            ["FIRE_CargoFireArmFwd"]    = "EVT_OH_FIRE_CARGO_ARM_FWD",
            ["FIRE_CargoFireArmAft"]    = "EVT_OH_FIRE_CARGO_ARM_AFT",
            ["FIRE_CargoFireArmMainDeck"]  = "EVT_OH_FIRE_CARGO_ARM_MAIN_DECK",
            ["FIRE_CargoFireDisch"]     = "EVT_OH_FIRE_CARGO_DISCH",
            ["FIRE_CargoDepr"]          = "EVT_OH_FIRE_CARGO_DISCH_DEPR",
            ["FIRE_FireOvhtTest"]       = "EVT_OH_FIRE_OVHT_TEST",
            ["FIRE_APUHandle"]          = "EVT_OH_FIRE_HANDLE_APU_TOP",
            ["FIRE_APUHandleUnlock"]    = "EVT_OH_FIRE_UNLOCK_SWITCH_APU",
            ["FIRE_EngineHandle_1"]     = "EVT_FIRE_HANDLE_ENGINE_1_TOP",
            ["FIRE_EngineHandle_2"]     = "EVT_FIRE_HANDLE_ENGINE_2_TOP",

            // --- Lights ---
            ["LTS_LandingLightL"]       = "EVT_OH_LIGHTS_LANDING_L",
            ["LTS_LandingLightR"]       = "EVT_OH_LIGHTS_LANDING_R",
            ["LTS_LandingLightNose"]    = "EVT_OH_LIGHTS_LANDING_NOSE",
            ["LTS_RunwayTurnoffL"]      = "EVT_OH_LIGHTS_L_TURNOFF",
            ["LTS_RunwayTurnoffR"]      = "EVT_OH_LIGHTS_R_TURNOFF",
            ["LTS_Taxi"]                = "EVT_OH_LIGHTS_TAXI",
            ["LTS_Strobe"]              = "EVT_OH_LIGHTS_STROBE",
            ["LTS_Beacon"]              = "EVT_OH_LIGHTS_BEACON",
            ["LTS_NAV"]                 = "EVT_OH_LIGHTS_NAV",
            ["LTS_Logo"]                = "EVT_OH_LIGHTS_LOGO",
            ["LTS_Wing"]                = "EVT_OH_LIGHTS_WING",
            ["LTS_Storm"]               = "EVT_OH_LIGHTS_STORM",
            ["LTS_CameraLights"]        = "EVT_OH_CAMERA_LTS_SWITCH",
            ["LTS_MasterBrightSw"]      = "EVT_OH_MASTER_BRIGHT_PUSH",
            ["LTS_MasterBrightKnob"]    = "EVT_OH_MASTER_BRIGHT_ROTATE",
            ["LTS_IndLightsTest"]       = "EVT_OH_LIGHTS_IND_LTS_SWITCH",
            ["LTS_DomeLight"]           = "EVT_OH_DOME_SWITCH",
            ["LTS_CircuitBreakerLight"] = "EVT_OH_CB_LIGHT_CONTROL",
            ["LTS_OverheadPanel"]       = "EVT_OH_PANEL_LIGHT_CONTROL",
            ["LTS_GlareshieldPanel"]    = "EVT_OH_GS_PANEL_LIGHT_CONTROL",
            ["LTS_GlareshieldFlood"]    = "EVT_OH_GS_FLOOD_LIGHT_CONTROL",
            ["LTS_EmerLights"]          = "EVT_OH_EMER_EXIT_LIGHT_SWITCH",

            // --- Signs ---
            ["SIGNS_NoSmoking"]         = "EVT_OH_NO_SMOKING_LIGHT_SWITCH",
            ["SIGNS_SeatBelts"]         = "EVT_OH_FASTEN_BELTS_LIGHT_SWITCH",
            ["OXY_PassOxygen"]          = "EVT_OH_OXY_PASS_SWITCH",
            ["OXY_Suprnmry"]            = "EVT_OH_OXY_SUPRNMRY_SWITCH",

            // --- Wipers / Comms ---
            ["WIPERS_Left"]             = "EVT_OH_WIPER_LEFT_SWITCH",
            ["WIPERS_Right"]            = "EVT_OH_WIPER_RIGHT_SWITCH",
            ["COMM_ServiceInterphone"]  = "EVT_OH_SERVICE_INTERPHONE_SWITCH",

            // --- EFIS Captain ---
            ["EFIS_MinsSelBARO_Capt"]   = "EVT_EFIS_CPT_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_Capt"]    = "EVT_EFIS_CPT_BARO_IN_HPA",
            ["EFIS_VORADFSel1_Capt"]    = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_Capt"]    = "EVT_EFIS_CPT_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_Capt"]       = "EVT_EFIS_CPT_MODE",
            ["EFIS_RangeSel_Capt"]      = "EVT_EFIS_CPT_RANGE",
            ["EFIS_MinsKnob_Capt"]      = "EVT_EFIS_CPT_MINIMUMS",
            ["EFIS_BaroKnob_Capt"]      = "EVT_EFIS_CPT_BARO",
            ["EFIS_MinsRST_Capt"]       = "EVT_EFIS_CPT_MINIMUMS_RST",
            ["EFIS_BaroSTD_Capt"]       = "EVT_EFIS_CPT_BARO_STD",
            ["EFIS_ModeCTR_Capt"]       = "EVT_EFIS_CPT_MODE_CTR",
            ["EFIS_RangeTFC_Capt"]      = "EVT_EFIS_CPT_RANGE_TFC",
            ["EFIS_WXR_Capt"]           = "EVT_EFIS_CPT_WXR",
            ["EFIS_STA_Capt"]           = "EVT_EFIS_CPT_STA",
            ["EFIS_WPT_Capt"]           = "EVT_EFIS_CPT_WPT",
            ["EFIS_ARPT_Capt"]          = "EVT_EFIS_CPT_ARPT",
            ["EFIS_DATA_Capt"]          = "EVT_EFIS_CPT_DATA",
            ["EFIS_POS_Capt"]           = "EVT_EFIS_CPT_POS",
            ["EFIS_TERR_Capt"]          = "EVT_EFIS_CPT_TERR",
            ["EFIS_FPV_Capt"]           = "EVT_EFIS_CPT_FPV",
            ["EFIS_MTRS_Capt"]          = "EVT_EFIS_CPT_MTRS",

            // --- EFIS First Officer ---
            ["EFIS_MinsSelBARO_FO"]     = "EVT_EFIS_FO_MINIMUMS_RADIO_BARO",
            ["EFIS_BaroSelHPA_FO"]      = "EVT_EFIS_FO_BARO_IN_HPA",
            ["EFIS_VORADFSel1_FO"]      = "EVT_EFIS_FO_VOR_ADF_SELECTOR_L",
            ["EFIS_VORADFSel2_FO"]      = "EVT_EFIS_FO_VOR_ADF_SELECTOR_R",
            ["EFIS_ModeSel_FO"]         = "EVT_EFIS_FO_MODE",
            ["EFIS_RangeSel_FO"]        = "EVT_EFIS_FO_RANGE",
            ["EFIS_MinsKnob_FO"]        = "EVT_EFIS_FO_MINIMUMS",
            ["EFIS_BaroKnob_FO"]        = "EVT_EFIS_FO_BARO",
            ["EFIS_MinsRST_FO"]         = "EVT_EFIS_FO_MINIMUMS_RST",
            ["EFIS_BaroSTD_FO"]         = "EVT_EFIS_FO_BARO_STD",
            ["EFIS_ModeCTR_FO"]         = "EVT_EFIS_FO_MODE_CTR",
            ["EFIS_RangeTFC_FO"]        = "EVT_EFIS_FO_RANGE_TFC",
            ["EFIS_WXR_FO"]             = "EVT_EFIS_FO_WXR",
            ["EFIS_STA_FO"]             = "EVT_EFIS_FO_STA",
            ["EFIS_WPT_FO"]             = "EVT_EFIS_FO_WPT",
            ["EFIS_ARPT_FO"]            = "EVT_EFIS_FO_ARPT",
            ["EFIS_DATA_FO"]            = "EVT_EFIS_FO_DATA",
            ["EFIS_POS_FO"]             = "EVT_EFIS_FO_POS",
            ["EFIS_TERR_FO"]            = "EVT_EFIS_FO_TERR",
            ["EFIS_FPV_FO"]             = "EVT_EFIS_FO_FPV",
            ["EFIS_MTRS_FO"]            = "EVT_EFIS_FO_MTRS",
            ["EFIS_HdgRef"]             = "EVT_EFIS_HDG_REF_SWITCH",

            // --- MCP switches (toggles) ---
            ["MCP_FD_L"]                = "EVT_MCP_FD_SWITCH_L",
            ["MCP_FD_R"]                = "EVT_MCP_FD_SWITCH_R",
            ["MCP_ATArm_L"]             = "EVT_MCP_AT_ARM_SWITCH_L",
            ["MCP_ATArm_R"]             = "EVT_MCP_AT_ARM_SWITCH_R",
            ["MCP_AltIncrSel"]          = "EVT_MCP_ALT_INCR_SELECTOR",
            ["MCP_DisengageBar"]        = "EVT_MCP_DISENGAGE_BAR",
            ["MCP_BankLimitSel"]        = "EVT_MCP_BANK_ANGLE_SELECTOR",
            ["MCP_HDGDialMode"]         = "EVT_MCP_HDG_TRK_SWITCH",
            ["MCP_VSDialMode"]          = "EVT_MCP_VS_SWITCH",

            // --- MCP momentary mode buttons ---
            ["MCP_LNAV"]                = "EVT_MCP_LNAV_SWITCH",
            ["MCP_VNAV"]                = "EVT_MCP_VNAV_SWITCH",
            ["MCP_FLCH"]                = "EVT_MCP_LVL_CHG_SWITCH",
            ["MCP_HDG_HOLD"]            = "EVT_MCP_HDG_HOLD_SWITCH",
            ["MCP_VS_FPA"]              = "EVT_MCP_VS_FPA_SWITCH",
            ["MCP_ALT_HOLD"]            = "EVT_MCP_ALT_HOLD_SWITCH",
            ["MCP_LOC"]                 = "EVT_MCP_LOC_SWITCH",
            ["MCP_APP"]                 = "EVT_MCP_APP_SWITCH",
            ["MCP_AT"]                  = "EVT_MCP_AT_SWITCH",
            ["MCP_CLB_CON"]             = "EVT_MCP_CLB_CON_SWITCH",
            ["MCP_AP_L"]                = "EVT_MCP_AP_L_SWITCH",
            ["MCP_AP_R"]                = "EVT_MCP_AP_R_SWITCH",
            ["MCP_SpeedPush"]           = "EVT_MCP_SPEED_PUSH_SWITCH",
            ["MCP_HeadingPush"]         = "EVT_MCP_HEADING_PUSH_SWITCH",
            ["MCP_AltitudePush"]        = "EVT_MCP_ALTITUDE_PUSH_SWITCH",
            ["MCP_IAS_MACH_Toggle"]     = "EVT_MCP_IAS_MACH_SWITCH",
            ["MCP_HDG_TRK_Toggle"]      = "EVT_MCP_HDG_TRK_SWITCH",
            ["MCP_VS_FPA_Toggle"]       = "EVT_MCP_VS_FPA_SWITCH",

            // --- MCP numeric selectors (wheel stepped) ---
            ["MCP_IASMach"]             = "EVT_MCP_SPEED_SELECTOR",
            ["MCP_Heading"]             = "EVT_MCP_HEADING_SELECTOR",
            ["MCP_Altitude"]            = "EVT_MCP_ALTITUDE_SELECTOR",
            ["MCP_VertSpeed"]           = "EVT_MCP_VS_SELECTOR",

            // --- Display Select Panel (momentary buttons) ---
            ["DSP_L_INBD"]              = "EVT_DSP_L_INBD_SWITCH",
            ["DSP_R_INBD"]              = "EVT_DSP_R_INBD_SWITCH",
            ["DSP_LWR_CTR"]             = "EVT_DSP_LWR_CTR_SWITCH",
            ["DSP_ENG"]                 = "EVT_DSP_ENG_SWITCH",
            ["DSP_STAT"]                = "EVT_DSP_STAT_SWITCH",
            ["DSP_ELEC"]                = "EVT_DSP_ELEC_SWITCH",
            ["DSP_HYD"]                 = "EVT_DSP_HYD_SWITCH",
            ["DSP_FUEL"]                = "EVT_DSP_FUEL_SWITCH",
            ["DSP_AIR"]                 = "EVT_DSP_AIR_SWITCH",
            ["DSP_DOOR"]                = "EVT_DSP_DOOR_SWITCH",
            ["DSP_GEAR"]                = "EVT_DSP_GEAR_SWITCH",
            ["DSP_FCTL"]                = "EVT_DSP_FCTL_SWITCH",
            ["DSP_CAM"]                 = "EVT_DSP_CAM_SWITCH",
            ["DSP_CHKL"]                = "EVT_DSP_CHKL_SWITCH",
            ["DSP_COMM"]                = "EVT_DSP_COMM_SWITCH",
            ["DSP_NAV"]                 = "EVT_DSP_NAV_SWITCH",
            ["DSP_CANC_RCL"]            = "EVT_DSP_CANC_RCL_SWITCH",
            ["DSP_InbdDspl_L"]          = "EVT_DSP_INDB_DSPL_L",
            ["DSP_InbdDspl_R"]          = "EVT_DSP_INDB_DSPL_R",

            // --- Landing Gear ---
            ["GEAR_Lever"]              = "EVT_GEAR_LEVER",
            ["GEAR_LockOvrd"]           = "EVT_GEAR_LEVER_UNLOCK",
            ["GEAR_AltnGearDown"]       = "EVT_GEAR_ALTN_GEAR_DOWN",

            // --- Brakes ---
            ["BRAKES_AutobrakeSelector"]= "EVT_ABS_AUTOBRAKE_SELECTOR",
            ["BRAKES_ParkingBrake"]     = "EVT_CONTROL_STAND_PARK_BRAKE_LEVER",

            // --- GPWS ---
            ["GPWS_TerrInhibit"]        = "EVT_GPWS_TERR_OVRD_SWITCH",
            ["GPWS_GearInhibit"]        = "EVT_GPWS_GEAR_OVRD_SWITCH",
            ["GPWS_FlapInhibit"]        = "EVT_GPWS_FLAP_OVRD_SWITCH",
            ["GPWS_GSInhibit"]          = "EVT_GPWS_GS_INHIBIT_SWITCH",
            ["GPWS_RunwayOvrd"]         = "EVT_GPWS_RWY_OVRD_SWITCH",

            // --- Instruments (ISP switches) ---
            ["ISP_FMC_Selector"]        = "EVT_FWD_FMC_SELECTOR",
            ["ISP_Nav_L"]               = "EVT_FWD_NAV_SOURCE_L",
            ["ISP_Nav_R"]               = "EVT_FWD_NAV_SOURCE_R",
            ["ISP_DsplCtrl_L"]          = "EVT_FWD_DSPL_CTRL_SOURCE_L",
            ["ISP_DsplCtrl_R"]          = "EVT_FWD_DSPL_CTRL_SOURCE_R",
            ["ISP_AirDataAtt_L"]        = "EVT_FWD_AIR_DATA_ATT_SOURCE_L",
            ["ISP_AirDataAtt_R"]        = "EVT_FWD_AIR_DATA_ATT_SOURCE_R",

            // --- ISFD momentary buttons ---
            ["ISFD_Baro"]               = "EVT_ISFD_BARO",
            ["ISFD_RST"]                = "EVT_ISFD_ATT_RST",
            ["ISFD_Minus"]              = "EVT_ISFD_MINUS",
            ["ISFD_Plus"]               = "EVT_ISFD_PLUS",
            ["ISFD_APP"]                = "EVT_ISFD_APP",
            ["ISFD_HP_IN"]              = "EVT_ISFD_HP_IN",

            // --- Control Stand (pedestal) ---
            ["FCTL_Speedbrake"]         = "EVT_CONTROL_STAND_SPEED_BRAKE_LEVER",
            ["FCTL_Flaps"]              = "EVT_CONTROL_STAND_FLAPS_LEVER",
            ["FCTL_AltnFlapsArm"]       = "EVT_ALTN_FLAPS_ARM",
            ["FCTL_AltnFlapsControl"]   = "EVT_ALTN_FLAPS_POS",
            ["FCTL_StabCutout_C"]       = "EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C",
            ["FCTL_StabCutout_R"]       = "EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R",
            ["FCTL_AltnPitch"]          = "EVT_CONTROL_STAND_ALT_PITCH_TRIM_LEVER",
            ["FCTL_AileronTrim"]        = "EVT_FCTL_AILERON_TRIM",
            ["FCTL_RudderTrim"]         = "EVT_FCTL_RUDDER_TRIM",
            ["FCTL_RudderTrimCancel"]   = "EVT_FCTL_RUDDER_TRIM_CANCEL",

            // --- CDU ---
            ["CDU_BrtKnob_L"]           = "EVT_CDU_L_BRITENESS",
            ["CDU_BrtKnob_R"]           = "EVT_CDU_R_BRITENESS",
            ["CDU_BrtKnob_C"]           = "EVT_CDU_C_BRITENESS",

            // --- XPDR/TCAS ---
            ["XPDR_XpndrSelector"]     = "EVT_TCAS_XPNDR",
            ["XPDR_AltSource"]          = "EVT_TCAS_ALTSOURCE",
            ["XPDR_ModeSel"]             = "EVT_TCAS_MODE",
            ["XPDR_Ident"]              = "EVT_TCAS_IDENT",
            ["XPDR_Test"]               = "EVT_TCAS_TEST",

            // --- Warning ---
            ["WARN_Reset_L"]            = "EVT_MASTER_WARNING_RESET_LEFT",
            ["WARN_Reset_R"]            = "EVT_MASTER_WARNING_RESET_RIGHT",

            // --- Pedestal misc ---
            ["EVAC_Command"]            = "EVT_PED_EVAC_SWITCH",
            ["EVAC_HornShutoff"]        = "EVT_PED_EVAC_HORN_SHUTOFF",
            ["EVAC_PressToTest"]        = "EVT_PED_EVAC_TEST_SWITCH",
            ["EICAS_EventRcd"]          = "EVT_PED_EICAS_EVENT_RCD",
            ["ISP_DsplCtrl_C"]          = "EVT_PED_DSPL_CTRL_SOURCE_C",

            // --- Call Panel ---
            ["CALL_Ground"]         = "EVT_PED_CALL_GND",
            ["CALL_CrewRest"]       = "EVT_PED_CALL_CREW_REST",
            ["CALL_Suprnmry"]       = "EVT_PED_CALL_SUPRNMRY",
            ["CALL_Cargo"]          = "EVT_PED_CALL_CARGO",
            ["CALL_CargoAudio"]     = "EVT_PED_CALL_CARGO_AUDIO",
            ["CALL_MainDeckAlert"]  = "EVT_PED_CALL_MAIN_DK_ALERT",

            // --- MCP Course Push ---
            ["MCP_CRS_L_Push"]  = "EVT_MCP_CRS_L_PUSH",
            ["MCP_CRS_R_Push"]  = "EVT_MCP_CRS_R_PUSH",

            // --- TOGA / AT Disengage ---
            ["ENG_TOGA_1"]        = "EVT_CONTROL_STAND_TOGA1_SWITCH",
            ["ENG_TOGA_2"]        = "EVT_CONTROL_STAND_TOGA2_SWITCH",
            ["ENG_ATDisengage_1"] = "EVT_CONTROL_STAND_AT1_DISENGAGE_SWITCH",
            ["ENG_ATDisengage_2"] = "EVT_CONTROL_STAND_AT2_DISENGAGE_SWITCH",

            // --- Outflow Valve Auto/Manual ---
            ["AIR_OutflowValve_Fwd"]  = "EVT_OH_PRESS_VALVE_SWITCH_1",
            ["AIR_OutflowValve_Aft"]  = "EVT_OH_PRESS_VALVE_SWITCH_2",

            // --- Yoke / Standby Instruments ---
            ["YOKE_APDisc"]           = "EVT_YOKE_AP_DISC_SWITCH",
            ["STBY_ASI_Push"]         = "EVT_STANDBY_ASI_KNOB_PUSH",
            ["STBY_ALT_Push"]         = "EVT_STANDBY_ALTIMETER_KNOB_PUSH",
        };

    // =========================================================================
    // Guarded switch table: varKey → (guardEvent, switchEvent)
    // =========================================================================
    private static readonly IReadOnlyDictionary<string, (string Guard, string Switch)> _guardedMap =
        new Dictionary<string, (string, string)>
        {
            ["ELEC_IDGDisc_1"]      = ("EVT_OH_ELEC_DISCONNECT1_GUARD", "EVT_OH_ELEC_DISCONNECT1_SWITCH"),
            ["ELEC_IDGDisc_2"]      = ("EVT_OH_ELEC_DISCONNECT2_GUARD", "EVT_OH_ELEC_DISCONNECT2_SWITCH"),
            ["ELEC_StandbyPwr"]     = ("EVT_OH_ELEC_STBY_PWR_GUARD",   "EVT_OH_ELEC_STBY_PWR_SWITCH"),
            ["ELEC_TowingPower"]    = ("EVT_OH_ELEC_TOWING_PWR_GUARD",  "EVT_OH_ELEC_TOWING_PWR_SWITCH"),
            ["ENG_EECMode_L"]       = ("EVT_OH_EEC_L_GUARD",            "EVT_OH_EEC_L_SWITCH"),
            ["ENG_EECMode_R"]       = ("EVT_OH_EEC_R_GUARD",            "EVT_OH_EEC_R_SWITCH"),
            ["ENG_EECTest_L"]       = ("EVT_OH_EEC_TEST_L_SWITCH_GUARD","EVT_OH_EEC_TEST_L_SWITCH"),
            ["ENG_EECTest_R"]       = ("EVT_OH_EEC_TEST_R_SWITCH_GUARD","EVT_OH_EEC_TEST_R_SWITCH"),
            ["APU_PowerTest"]       = ("EVT_OH_APU_TEST_SWITCH_GUARD",  "EVT_OH_APU_TEST_SWITCH"),
            ["HYD_RAT"]             = ("EVT_OH_HYD_RAM_AIR_COVER",      "EVT_OH_HYD_RAM_AIR"),
            ["FCTL_WingHydValve_L"] = ("EVT_OH_HYD_VLV_PWR_WING_L_GUARD","EVT_OH_HYD_VLV_PWR_WING_L"),
            ["FCTL_WingHydValve_R"] = ("EVT_OH_HYD_VLV_PWR_WING_R_GUARD","EVT_OH_HYD_VLV_PWR_WING_R"),
            ["FCTL_WingHydValve_C"] = ("EVT_OH_HYD_VLV_PWR_WING_C_GUARD","EVT_OH_HYD_VLV_PWR_WING_C"),
            ["FCTL_TailHydValve_L"] = ("EVT_OH_HYD_VLV_PWR_TAIL_L_GUARD","EVT_OH_HYD_VLV_PWR_TAIL_L"),
            ["FCTL_TailHydValve_R"] = ("EVT_OH_HYD_VLV_PWR_TAIL_R_GUARD","EVT_OH_HYD_VLV_PWR_TAIL_R"),
            ["FCTL_TailHydValve_C"] = ("EVT_OH_HYD_VLV_PWR_TAIL_C_GUARD","EVT_OH_HYD_VLV_PWR_TAIL_C"),
            ["FCTL_PrimFltComputers"]= ("EVT_OH_PRIM_FLT_COMPUTERS_GUARD","EVT_OH_PRIM_FLT_COMPUTERS"),
            ["FUEL_JettisonNozzleL"]= ("EVT_OH_FUEL_JETTISON_NOZZLE_L_GUARD","EVT_OH_FUEL_JETTISON_NOZZLE_L"),
            ["FUEL_JettisonNozzleR"]= ("EVT_OH_FUEL_JETTISON_NOZZLE_R_GUARD","EVT_OH_FUEL_JETTISON_NOZZLE_R"),
            ["FIRE_CargoFireDisch"]  = ("EVT_OH_FIRE_CARGO_DISCH_GUARD", "EVT_OH_FIRE_CARGO_DISCH"),
            ["FIRE_EngineHandleUnlock_1"] = ("EVT_FIRE_UNLOCK_SWITCH_ENGINE_1", "EVT_FIRE_HANDLE_ENGINE_1_TOP"),
            ["FIRE_EngineHandleUnlock_2"] = ("EVT_FIRE_UNLOCK_SWITCH_ENGINE_2", "EVT_FIRE_HANDLE_ENGINE_2_TOP"),
            ["ICE_BackupWindowHeat_L"]= ("EVT_OH_ICE_BU_WINDOW_HEAT_L_GUARD","EVT_OH_ICE_BU_WINDOW_HEAT_L"),
            ["ICE_BackupWindowHeat_R"]= ("EVT_OH_ICE_BU_WINDOW_HEAT_R_GUARD","EVT_OH_ICE_BU_WINDOW_HEAT_R"),
            ["OXY_PassOxygen"]       = ("EVT_OH_OXY_PASS_GUARD",         "EVT_OH_OXY_PASS_SWITCH"),
            ["OXY_Suprnmry"]         = ("EVT_OH_OXY_SUPRNMRY_GUARD",    "EVT_OH_OXY_SUPRNMRY_SWITCH"),
            ["ELEC_GndTest"]         = ("EVT_OH_ELEC_GND_TEST_GUARD",   "EVT_OH_ELEC_GND_TEST_SWITCH"),
            ["LTS_EmerLights"]       = ("EVT_OH_EMER_EXIT_LIGHT_GUARD",  "EVT_OH_EMER_EXIT_LIGHT_SWITCH"),
            ["GEAR_AltnGearDown"]    = ("EVT_GEAR_ALTN_GEAR_DOWN_GUARD", "EVT_GEAR_ALTN_GEAR_DOWN"),
            ["FCTL_AltnFlapsArm"]    = ("EVT_ALTN_FLAPS_ARM_GUARD",      "EVT_ALTN_FLAPS_ARM"),
            ["FCTL_StabCutout_C"]    = ("EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C_GUARD","EVT_CONTROL_STAND_STABCUTOUT_SWITCH_C"),
            ["FCTL_StabCutout_R"]    = ("EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R_GUARD","EVT_CONTROL_STAND_STABCUTOUT_SWITCH_R"),
            ["GPWS_TerrInhibit"]     = ("EVT_GPWS_TERR_OVRD_GUARD",     "EVT_GPWS_TERR_OVRD_SWITCH"),
            ["GPWS_GearInhibit"]     = ("EVT_GPWS_GEAR_OVRD_GUARD",     "EVT_GPWS_GEAR_OVRD_SWITCH"),
            ["GPWS_FlapInhibit"]     = ("EVT_GPWS_FLAP_OVRD_GUARD",     "EVT_GPWS_FLAP_OVRD_SWITCH"),
            ["GPWS_RunwayOvrd"]      = ("EVT_GPWS_RWY_OVRD_GUARD",      "EVT_GPWS_RWY_OVRD_SWITCH"),
            ["EFIS_HdgRef"]          = ("EVT_EFIS_HDG_REF_GUARD",       "EVT_EFIS_HDG_REF_SWITCH"),
            ["AIR_AltnVent"]         = ("EVT_OH_AIRCOND_ALT_VENT_GUARD", "EVT_OH_AIRCOND_ALT_VENT_SWITCH"),
            ["EVAC_Command"]         = ("EVT_PED_EVAC_SWITCH_GUARD",    "EVT_PED_EVAC_SWITCH"),
        };

    public override bool HandleUIVariableSet(
        string varKey, double value,
        SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] ENTRY varKey={varKey} value={value} RenderAsButton={varDef.RenderAsButton} IsMomentary={varDef.IsMomentary} ValueDescriptions.Count={varDef.ValueDescriptions.Count}");

        // ------------------------------------------------------------------
        // 0. Standard SimConnect events and COM frequency set
        //    Handled here to prevent MainForm's redundant announcements.
        //    ProcessSimVarUpdate announces when the SimVar actually changes.
        // ------------------------------------------------------------------
        if (varDef.Type == SimConnect.SimVarType.Event)
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: SIMCONNECT EVENT — calling SendEvent({varDef.Name})");
            simConnect.SendEvent(varDef.Name);
            return true;
        }

        // COM standby frequency set — validate, convert to Hz, send via SimConnect.
        // Return true to prevent MainForm's "Standby frequency set to xxx" announcement.
        if (varKey.StartsWith("COM_STANDBY_FREQUENCY_SET"))
        {
            if (value >= 118.0 && value <= 136.975)
            {
                uint frequencyHz = (uint)(value * 1000000);
                string setEvent = varKey.Contains(":2") ? "COM2_STBY_RADIO_SET_HZ" : "COM_STBY_RADIO_SET_HZ";
                simConnect.SendEvent(setEvent, frequencyHz);
            }
            else
            {
                announcer.AnnounceImmediate("Invalid frequency. Range: 118.000 to 136.975");
            }
            return true;
        }

        // ------------------------------------------------------------------
        // 1. Guarded switches — require guard open → toggle → guard close
        // ------------------------------------------------------------------
        if (_guardedMap.TryGetValue(varKey, out var guardPair))
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: GUARDED switch guard={guardPair.Guard} switch={guardPair.Switch}");
            if (EventIds.TryGetValue(guardPair.Guard, out int gId) &&
                EventIds.TryGetValue(guardPair.Switch, out int sId))
            {
                SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Sending guarded toggle: guardId={gId} switchId={sId}");
                _ = simConnect.SendPMDGGuardedToggle(
                    guardPair.Guard,  (uint)gId,
                    guardPair.Switch, (uint)sId);
                return true;
            }
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] GUARDED: event IDs not found for guard={guardPair.Guard} or switch={guardPair.Switch}");
        }

        // ------------------------------------------------------------------
        // 2. Look up the event name for this variable key
        // ------------------------------------------------------------------
        if (!_simpleEventMap.TryGetValue(varKey, out string? eventName))
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] NOT in _simpleEventMap — returning false");
            return false;
        }

        if (!EventIds.TryGetValue(eventName, out int evId))
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] eventName={eventName} NOT in EventIds — returning false");
            return false;
        }

        uint eventId = (uint)evId;
        SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] eventName={eventName} eventId={eventId} (0x{eventId:X})");

        // ------------------------------------------------------------------
        // 2b. APU Start — special: send position 2 to the APU selector event,
        //     but only when the selector is already at On (1)
        // ------------------------------------------------------------------
        if (varKey == "ELEC_APU_Start")
        {
            var dm = simConnect.PMDG777DataManager;
            int current = dm != null ? (int)dm.GetFieldValue("ELEC_APU_Selector") : 0;
            if (current == 1)
                simConnect.SendPMDGEvent(eventName, eventId, 2); // 2 = Start position
            return true;
        }

        // ------------------------------------------------------------------
        // 3. Momentary / button press — CDA parameter 1 = "pressed"
        //    (CDA parameter 0 means "not pressed" which is a no-op)
        // ------------------------------------------------------------------
        if (varDef.RenderAsButton || varDef.IsMomentary)
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: MOMENTARY/BUTTON — calling SendPMDGEvent({eventName}, {eventId}, 1)");
            simConnect.SendPMDGEvent(eventName, eventId, 1);
            return true;
        }

        // ------------------------------------------------------------------
        // 4. Switches with ValueDescriptions — send target position directly via CDA.
        //    Works for both two-position toggles and multi-position selectors.
        //    CDA sends {EventId, PositionValue} to set the switch to the exact
        //    target position — no stepping, no direction ambiguity.
        // ------------------------------------------------------------------
        if (varDef.ValueDescriptions.Count >= 2)
        {
            int target = (int)value;
            var dm = simConnect.PMDG777DataManager;
            if (dm != null)
            {
                int current = (int)dm.GetFieldValue(varDef.Name);
                SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: CDA DIRECT current={current} target={target}");
                if (current == target)
                {
                    SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] CDA DIRECT: already at target — skipping");
                    return true;
                }
            }
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] CDA DIRECT: sending position {target} for {eventName}");
            simConnect.SendPMDGEvent(eventName, eventId, target);
            return true;
        }

        // ------------------------------------------------------------------
        // 6. No ValueDescriptions — treat as a single toggle/press
        // ------------------------------------------------------------------
        SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: NO ValueDescriptions — calling SendPMDGEvent({eventName}, {eventId})");
        simConnect.SendPMDGEvent(eventName, eventId);
        return true;
    }

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        SimConnect.PMDG777Debug.Log($"[PMDG777Definition.ProcessSimVarUpdate] ENTRY varName={varName} value={value}");
        if (base.ProcessSimVarUpdate(varName, value, announcer))
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.ProcessSimVarUpdate] Handled by base class: {varName}");
            return true;
        }

        // Suppress raw value announcements for momentary button push states.
        // These _Sw_Pushed fields briefly go to 1 then back to 0 — the actual
        // mode state is announced via separate annunciator variables.
        var variables = GetVariables();
        if (variables.TryGetValue(varName, out var varDef) && varDef.RenderAsButton)
        {
            return true; // Suppress — screen reader announces the button press via UI
        }

        // Speed brake — custom formatting for lever position
        if (varName == "FCTL_Speedbrake")
        {
            int lever = (int)value;
            if (lever == 0)
                announcer.Announce("Speed brake down");
            else if (lever == 25)
                announcer.Announce("Speed brake armed");
            else
            {
                // 26-100 = deployed, map to approximate percentage
                int pct = (int)Math.Round((lever - 25.0) / 75.0 * 100);
                announcer.Announce($"Speed brake {pct} percent");
            }
            return true;
        }

        // MCP display value announcements
        if (varName == "MCP_IASMach")
        {
            if (value < 10)
                announcer.Announce($"Mach {value:F2}");
            else
                announcer.Announce($"Speed {(int)value} knots");
            return true;
        }

        if (varName == "MCP_Heading")
        {
            announcer.Announce($"Heading {(int)value}");
            return true;
        }

        if (varName == "MCP_Altitude")
        {
            announcer.Announce($"Altitude {(int)value}");
            return true;
        }

        if (varName == "MCP_VertSpeed")
        {
            announcer.Announce($"Vertical speed {(int)value}");
            return true;
        }

        if (varName == "MCP_IASBlank" && value > 0)
        {
            announcer.Announce("Speed blank");
            return true;
        }

        if (varName == "MCP_VertSpeedBlank" && value > 0)
        {
            announcer.Announce("Vertical speed blank");
            return true;
        }

        if (varName == "MCP_FPA")
        {
            float fpa = (float)value;
            announcer.Announce($"FPA {fpa:F1} degrees");
            return true;
        }

        // MCP mode annunciator announcements
        if (varName == "MCP_annunAP_L")
        {
            announcer.Announce(value > 0 ? "Autopilot left engaged" : "Autopilot left disengaged");
            return true;
        }

        if (varName == "MCP_annunAP_R")
        {
            announcer.Announce(value > 0 ? "Autopilot right engaged" : "Autopilot right disengaged");
            return true;
        }

        if (varName == "MCP_annunAT")
        {
            announcer.Announce(value > 0 ? "Autothrottle engaged" : "Autothrottle disengaged");
            return true;
        }

        if (varName == "MCP_annunLNAV")
        {
            announcer.Announce(value > 0 ? "LNAV engaged" : "LNAV disengaged");
            return true;
        }

        if (varName == "MCP_annunVNAV")
        {
            announcer.Announce(value > 0 ? "VNAV engaged" : "VNAV disengaged");
            return true;
        }

        if (varName == "MCP_annunFLCH")
        {
            announcer.Announce(value > 0 ? "FLCH engaged" : "FLCH disengaged");
            return true;
        }

        if (varName == "MCP_annunHDG_HOLD")
        {
            announcer.Announce(value > 0 ? "Heading hold engaged" : "Heading hold disengaged");
            return true;
        }

        if (varName == "MCP_annunVS_FPA")
        {
            announcer.Announce(value > 0 ? "VS/FPA engaged" : "VS/FPA disengaged");
            return true;
        }

        if (varName == "MCP_annunALT_HOLD")
        {
            announcer.Announce(value > 0 ? "Altitude hold engaged" : "Altitude hold disengaged");
            return true;
        }

        if (varName == "MCP_annunLOC")
        {
            announcer.Announce(value > 0 ? "Localizer engaged" : "Localizer disengaged");
            return true;
        }

        if (varName == "MCP_annunAPP")
        {
            announcer.Announce(value > 0 ? "Approach engaged" : "Approach disengaged");
            return true;
        }

        // Master warning and caution
        if (varName == "WARN_annunMasterWarning_L" || varName == "WARN_annunMasterWarning_R")
        {
            announcer.Announce(value > 0 ? "Master WARNING" : "Master WARNING cleared");
            return true;
        }

        if (varName == "WARN_annunMasterCaution_L" || varName == "WARN_annunMasterCaution_R")
        {
            announcer.Announce(value > 0 ? "Master CAUTION" : "Master CAUTION cleared");
            return true;
        }

        // FMC V-speeds — suppress when 0 (not yet set)
        if (varName == "FMC_V1")
        {
            if (value > 0)
                announcer.Announce($"V1 {(int)value} knots");
            return true;
        }
        if (varName == "FMC_VR")
        {
            if (value > 0)
                announcer.Announce($"VR {(int)value} knots");
            return true;
        }
        if (varName == "FMC_V2")
        {
            if (value > 0)
                announcer.Announce($"V2 {(int)value} knots");
            return true;
        }
        if (varName == "FMC_CruiseAlt")
        {
            if (value > 0)
                announcer.Announce($"Cruise altitude {(int)value} feet");
            return true;
        }


        // APU Selector — custom announcement to handle all positions including Start (2)
        if (varName == "ELEC_APU_Selector")
        {
            string state = (int)value switch
            {
                0 => "Off",
                1 => "On",
                2 => "Start",
                _ => ((int)value).ToString()
            };
            announcer.Announce($"APU Selector {state}");
            return true;
        }

        // APU Running state
        if (varName == "MON_APURunning")
        {
            announcer.Announce(value > 0 ? "APU running" : "APU shut down");
            return true;
        }

        // COM/squawk announcements — only announce when value actually changes
        if (varName == "COM1_ActiveFreq")
        {
            if (_lastComActiveFreq1 > 0 && Math.Abs(value - _lastComActiveFreq1) > 0.001)
                announcer.Announce($"COM1 active {value:F3}");
            _lastComActiveFreq1 = value;
            return true;
        }
        if (varName == "COM_STANDBY_FREQUENCY_SET:1")
        {
            if (_lastComStandbyFreq1 > 0 && Math.Abs(value - _lastComStandbyFreq1) > 0.001)
                announcer.Announce($"COM1 standby {value:F3}");
            _lastComStandbyFreq1 = value;
            return true;
        }
        if (varName == "COM2_ActiveFreq")
        {
            if (_lastComActiveFreq2 > 0 && Math.Abs(value - _lastComActiveFreq2) > 0.001)
                announcer.Announce($"COM2 active {value:F3}");
            _lastComActiveFreq2 = value;
            return true;
        }
        if (varName == "COM_STANDBY_FREQUENCY_SET:2")
        {
            if (_lastComStandbyFreq2 > 0 && Math.Abs(value - _lastComStandbyFreq2) > 0.001)
                announcer.Announce($"COM2 standby {value:F3}");
            _lastComStandbyFreq2 = value;
            return true;
        }
        if (varName == "TRANSPONDER_CODE_SET")
        {
            if (_lastSquawkCode > 0 && Math.Abs(value - _lastSquawkCode) > 0.5)
            {
                int bcd = (int)value;
                int d1 = (bcd >> 12) & 0xF;
                int d2 = (bcd >> 8) & 0xF;
                int d3 = (bcd >> 4) & 0xF;
                int d4 = bcd & 0xF;
                announcer.Announce($"Squawk {d1}{d2}{d3}{d4}");
            }
            _lastSquawkCode = value;
            return true;
        }

        SimConnect.PMDG777Debug.Log($"[PMDG777Definition.ProcessSimVarUpdate] No match — returning false for {varName}");
        return false;
    }

    public override bool HandleHotkeyAction(
        HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm,
        HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // ------------------------------------------------------------------
            // MCP Readouts
            // ------------------------------------------------------------------

            case HotkeyAction.ReadHeading:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int heading = (int)dm.GetFieldValue("MCP_Heading");
                announcer.AnnounceImmediate($"Heading {heading}");
                return true;
            }

            case HotkeyAction.ReadSpeed:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                float speed = (float)dm.GetFieldValue("MCP_IASMach");
                // If speed < 10, it's Mach; otherwise IAS in knots
                string speedText = speed < 10f
                    ? $"Mach {speed:0.000}"
                    : $"Speed {(int)speed} knots";
                announcer.AnnounceImmediate(speedText);
                return true;
            }

            case HotkeyAction.ReadAltitude:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int altitude = (int)dm.GetFieldValue("MCP_Altitude");
                announcer.AnnounceImmediate($"Altitude {altitude}");
                return true;
            }

            case HotkeyAction.ReadFCUVerticalSpeedFPA:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                // MCP_VSDial_Mode: 0 = VS, 1 = FPA
                int vsMode = (int)dm.GetFieldValue("MCP_VSDial_Mode");
                if (vsMode == 1)
                {
                    float fpa = (float)dm.GetFieldValue("MCP_FPA");
                    announcer.AnnounceImmediate($"FPA {fpa:+0.0;-0.0;0.0} degrees");
                }
                else
                {
                    int vs = (int)dm.GetFieldValue("MCP_VertSpeed");
                    announcer.AnnounceImmediate($"Vertical speed {vs} feet per minute");
                }
                return true;
            }

            // ------------------------------------------------------------------
            // Fuel Readout
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFuelQuantity:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int left   = (int)Math.Round(dm.GetFieldValue("FUEL_QtyLeft"));
                int center = (int)Math.Round(dm.GetFieldValue("FUEL_QtyCenter"));
                int right  = (int)Math.Round(dm.GetFieldValue("FUEL_QtyRight"));
                int aux    = (int)Math.Round(dm.GetFieldValue("FUEL_QtyAux"));
                int total  = left + center + right + aux;
                announcer.AnnounceImmediate(
                    $"Left {left}, Center {center}, Right {right}, Aux {aux}, Total {total} pounds");
                return true;
            }

            // ------------------------------------------------------------------
            // Flaps and Gear
            // ------------------------------------------------------------------

            case HotkeyAction.ReadFlaps:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int lever = (int)dm.GetFieldValue("FCTL_Flaps_Lever");
                string position = lever switch
                {
                    0 => "Up",
                    1 => "1",
                    2 => "5",
                    3 => "15",
                    4 => "20",
                    5 => "25",
                    6 => "30",
                    _ => lever.ToString()
                };
                announcer.AnnounceImmediate($"Flaps {position}");
                return true;
            }

            case HotkeyAction.ReadGear:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int gear = (int)dm.GetFieldValue("GEAR_Lever");
                // GEAR_Lever: 0 = up, 1 = down
                announcer.AnnounceImmediate(gear == 0 ? "Gear up" : "Gear down");
                return true;
            }

            // ------------------------------------------------------------------
            // Altimeter (EFIS baro)
            // ------------------------------------------------------------------

            case HotkeyAction.ReadAltimeter:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                // EFIS_BaroSTD_Sw_Pushed_0: true = STD selected
                double baroStd = dm.GetFieldValue("EFIS_BaroSTD_Sw_Pushed_0");
                if (baroStd > 0.5)
                {
                    announcer.AnnounceImmediate("Altimeter standard");
                    return true;
                }
                // EFIS_BaroSelHPA_0: 0 = IN, 1 = HPA
                double isHpa = dm.GetFieldValue("EFIS_BaroSelHPA_0");
                // EFIS_BaroKnob_0: raw knob value
                double knobRaw = dm.GetFieldValue("EFIS_BaroKnob_0");
                if (isHpa > 0.5)
                {
                    // HPA value stored directly
                    announcer.AnnounceImmediate($"Altimeter {(int)knobRaw} hectopascals");
                }
                else
                {
                    // Inches value stored as integer tenths (e.g. 2992 = 29.92)
                    double inHg = knobRaw / 100.0;
                    announcer.AnnounceImmediate($"Altimeter {inHg:0.00} inches");
                }
                return true;
            }

            case HotkeyAction.ReadDistanceToTOD:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                float dist = (float)dm.GetFieldValue("FMC_DistanceToTOD");
                if (dist < 0)
                    announcer.AnnounceImmediate("Top of descent not available");
                else if (dist < 0.1f)
                    announcer.AnnounceImmediate("Past top of descent");
                else
                    announcer.AnnounceImmediate($"{dist:F0} miles to top of descent");
                return true;
            }

            case HotkeyAction.ReadDistanceToDest:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                float dist = (float)dm.GetFieldValue("FMC_DistanceToDest");
                if (dist < 0)
                    announcer.AnnounceImmediate("Distance to destination not available");
                else
                    announcer.AnnounceImmediate($"{dist:F0} miles to destination");
                return true;
            }

            case HotkeyAction.ReadThrustLimitMode:
            {
                var dm = simConnect.PMDG777DataManager;
                if (dm == null) return false;
                int mode = (int)dm.GetFieldValue("FMC_ThrustLimitMode");
                string modeText = mode switch
                {
                    0 => "None",
                    1 => "TO", 2 => "TO 1", 3 => "TO 2",
                    4 => "D-TO", 5 => "D-TO 1", 6 => "D-TO 2",
                    7 => "CLB", 8 => "CLB 1", 9 => "CLB 2",
                    10 => "CRZ", 11 => "CON",
                    _ => $"Unknown ({mode})"
                };
                announcer.AnnounceImmediate($"Thrust limit {modeText}");
                return true;
            }

            // ------------------------------------------------------------------
            // MCP Direct-Set Input Dialogs
            // ------------------------------------------------------------------

            case HotkeyAction.FCUSetHeading:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGHeadingDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetSpeed:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGSpeedDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetAltitude:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGAltitudeDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetVS:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGVSDialog(simConnect, announcer, parentForm);
                return true;
            }

            case HotkeyAction.FCUSetBaro:
            {
                hotkeyManager.ExitInputHotkeyMode();
                ShowPMDGBaroDialog(simConnect, announcer, parentForm);
                return true;
            }

            // CDU handled by MainForm (Task 13)
            case HotkeyAction.ShowFenixMCDU:
                return false;


            default:
                return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
        }
    }

    // =========================================================================
    // FCU Request Override Methods
    // =========================================================================

    public override void RequestFCUHeading(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDG777DataManager;
        if (dm == null) return;
        int heading = (int)dm.GetFieldValue("MCP_Heading");
        announcer.AnnounceImmediate($"Heading {heading}");
    }

    public override void RequestFCUSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDG777DataManager;
        if (dm == null) return;
        float speed = (float)dm.GetFieldValue("MCP_IASMach");
        string speedText = speed < 10f
            ? $"Mach {speed:0.000}"
            : $"Speed {(int)speed} knots";
        announcer.AnnounceImmediate(speedText);
    }

    public override void RequestFCUAltitude(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDG777DataManager;
        if (dm == null) return;
        int altitude = (int)dm.GetFieldValue("MCP_Altitude");
        announcer.AnnounceImmediate($"Altitude {altitude}");
    }

    public override void RequestFCUVerticalSpeed(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var dm = simConnect.PMDG777DataManager;
        if (dm == null) return;
        int vsMode = (int)dm.GetFieldValue("MCP_VSDial_Mode");
        if (vsMode == 1)
        {
            float fpa = (float)dm.GetFieldValue("MCP_FPA");
            announcer.AnnounceImmediate($"FPA {fpa:+0.0;-0.0;0.0} degrees");
        }
        else
        {
            int vs = (int)dm.GetFieldValue("MCP_VertSpeed");
            announcer.AnnounceImmediate($"Vertical speed {vs} feet per minute");
        }
    }

    // =========================================================================
    // MCP Direct-Set Dialog Helpers
    // =========================================================================

    private void ShowPMDGHeadingDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dialog = new Forms.FCUInputForm(
            "MCP Heading", "heading", "0-359", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 359)
                    return (true, "");
                return (false, "Enter a value between 0 and 359");
            });

        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (int.TryParse(dialog.InputValue, out int hdg))
            {
                if (EventIds.TryGetValue("EVT_MCP_HDGTRK_SET", out int evId))
                    simConnect.SendPMDGEvent("EVT_MCP_HDGTRK_SET", (uint)evId, hdg);
                announcer.AnnounceImmediate($"Heading set to {hdg}");
            }
        }
    }

    private void ShowPMDGSpeedDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dialog = new Forms.FCUInputForm(
            "MCP Speed", "speed (knots or Mach, e.g. 280 or 0.84)", "100-399 or 0.00-0.99", announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    if (val >= 0.0 && val < 10.0) return (true, ""); // Mach
                    if (val >= 100 && val <= 399)  return (true, ""); // IAS knots
                }
                return (false, "Enter knots (100-399) or Mach (0.00-0.99)");
            });

        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double spd))
            {
                if (spd < 10.0)
                {
                    // Mach: send as Mach * 1000 (e.g., 0.84 → 840)
                    int machVal = (int)Math.Round(spd * 1000);
                    if (EventIds.TryGetValue("EVT_MCP_MACH_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_MACH_SET", (uint)evId, machVal);
                    announcer.AnnounceImmediate($"Mach set to {spd:0.000}");
                }
                else
                {
                    int iasVal = (int)spd;
                    if (EventIds.TryGetValue("EVT_MCP_IAS_SET", out int evId))
                        simConnect.SendPMDGEvent("EVT_MCP_IAS_SET", (uint)evId, iasVal);
                    announcer.AnnounceImmediate($"Speed set to {iasVal} knots");
                }
            }
        }
    }

    private void ShowPMDGAltitudeDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dialog = new Forms.FCUInputForm(
            "MCP Altitude", "altitude", "0-45000", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= 0 && val <= 45000)
                    return (true, "");
                return (false, "Enter a value between 0 and 45000");
            });

        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (int.TryParse(dialog.InputValue, out int alt))
            {
                if (EventIds.TryGetValue("EVT_MCP_ALT_SET", out int evId))
                    simConnect.SendPMDGEvent("EVT_MCP_ALT_SET", (uint)evId, alt);
                announcer.AnnounceImmediate($"Altitude set to {alt}");
            }
        }
    }

    private void ShowPMDGVSDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dialog = new Forms.FCUInputForm(
            "MCP Vertical Speed", "vertical speed (fpm)", "-9900 to 9900", announcer,
            input =>
            {
                if (int.TryParse(input, out int val) && val >= -9900 && val <= 9900)
                    return (true, "");
                return (false, "Enter a value between -9900 and 9900 fpm");
            });

        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (int.TryParse(dialog.InputValue, out int vs))
            {
                // Encode: value + 10000 (e.g. -1800 fpm → 8200)
                int encoded = vs + 10000;
                if (EventIds.TryGetValue("EVT_MCP_VS_SET", out int evId))
                    simConnect.SendPMDGEvent("EVT_MCP_VS_SET", (uint)evId, encoded);
                announcer.AnnounceImmediate($"Vertical speed set to {vs} feet per minute");
            }
        }
    }

    private void ShowPMDGBaroDialog(
        SimConnect.SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer,
        Form parentForm)
    {
        if (!simConnect.IsConnected)
        {
            announcer.AnnounceImmediate("Not connected to simulator.");
            return;
        }

        var dialog = new Forms.FCUInputForm(
            "Altimeter Setting", "baro (hPa or in Hg, e.g. 1013 or 29.92)", "940-1050 or 27.00-31.50", announcer,
            input =>
            {
                if (double.TryParse(input, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double val))
                {
                    if (val >= 940 && val <= 1050) return (true, "");  // hPa
                    if (val >= 27.0 && val <= 31.5) return (true, ""); // in Hg
                }
                return (false, "Enter hPa (940-1050) or in Hg (27.00-31.50)");
            });

        if (dialog.ShowDialog(parentForm) == DialogResult.OK && dialog.IsValidInput)
        {
            if (double.TryParse(dialog.InputValue, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double baro))
            {
                if (baro >= 940 && baro <= 1050)
                {
                    // HPA: send as raw integer hPa value
                    int hpaVal = (int)Math.Round(baro);
                    if (EventIds.TryGetValue("EVT_EFIS_CPT_BARO", out int evId))
                        simConnect.SendPMDGEvent("EVT_EFIS_CPT_BARO", (uint)evId, hpaVal);
                    announcer.AnnounceImmediate($"Altimeter set to {hpaVal} hectopascals");
                }
                else
                {
                    // Inches: send as integer hundredths (e.g. 29.92 → 2992)
                    int inchVal = (int)Math.Round(baro * 100);
                    if (EventIds.TryGetValue("EVT_EFIS_CPT_BARO", out int evId))
                        simConnect.SendPMDGEvent("EVT_EFIS_CPT_BARO", (uint)evId, inchVal);
                    announcer.AnnounceImmediate($"Altimeter set to {baro:0.00} inches");
                }
            }
        }
    }

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
            ["EVT_MCP_CRS_L_PUSH"]                 = 69853,
            ["EVT_MCP_CRS_R_PUSH"]                 = 69856,
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
            // GLARESHIELD — Master Warning Reset
            // -----------------------------------------------------------------
            ["EVT_MASTER_WARNING_RESET_LEFT"]       = 69809,
            ["EVT_MASTER_WARNING_RESET_RIGHT"]      = 69904,

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
            // PEDESTAL — Trim
            // -----------------------------------------------------------------
            ["EVT_FCTL_AILERON_TRIM"]             = 70359,
            ["EVT_FCTL_RUDDER_TRIM"]              = 70360,
            ["EVT_FCTL_RUDDER_TRIM_CANCEL"]       = 70361,

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
            // PEDESTAL — CDU Right
            // -----------------------------------------------------------------
            ["EVT_CDU_R_L1"]                       = 70033,
            ["EVT_CDU_R_L2"]                       = 70034,
            ["EVT_CDU_R_L3"]                       = 70035,
            ["EVT_CDU_R_L4"]                       = 70036,
            ["EVT_CDU_R_L5"]                       = 70037,
            ["EVT_CDU_R_L6"]                       = 70038,
            ["EVT_CDU_R_R1"]                       = 70039,
            ["EVT_CDU_R_R2"]                       = 70040,
            ["EVT_CDU_R_R3"]                       = 70041,
            ["EVT_CDU_R_R4"]                       = 70042,
            ["EVT_CDU_R_R5"]                       = 70043,
            ["EVT_CDU_R_R6"]                       = 70044,
            ["EVT_CDU_R_INIT_REF"]                 = 70045,
            ["EVT_CDU_R_RTE"]                      = 70046,
            ["EVT_CDU_R_DEP_ARR"]                  = 70047,
            ["EVT_CDU_R_ALTN"]                     = 70048,
            ["EVT_CDU_R_VNAV"]                     = 70049,
            ["EVT_CDU_R_FIX"]                      = 70050,
            ["EVT_CDU_R_LEGS"]                     = 70051,
            ["EVT_CDU_R_HOLD"]                     = 70052,
            ["EVT_CDU_R_PROG"]                     = 70053,
            ["EVT_CDU_R_EXEC"]                     = 70054,
            ["EVT_CDU_R_MENU"]                     = 70055,
            ["EVT_CDU_R_NAV_RAD"]                  = 70056,
            ["EVT_CDU_R_PREV_PAGE"]                = 70057,
            ["EVT_CDU_R_NEXT_PAGE"]                = 70058,
            ["EVT_CDU_R_1"]                        = 70059,
            ["EVT_CDU_R_2"]                        = 70060,
            ["EVT_CDU_R_3"]                        = 70061,
            ["EVT_CDU_R_4"]                        = 70062,
            ["EVT_CDU_R_5"]                        = 70063,
            ["EVT_CDU_R_6"]                        = 70064,
            ["EVT_CDU_R_7"]                        = 70065,
            ["EVT_CDU_R_8"]                        = 70066,
            ["EVT_CDU_R_9"]                        = 70067,
            ["EVT_CDU_R_DOT"]                      = 70068,
            ["EVT_CDU_R_0"]                        = 70069,
            ["EVT_CDU_R_PLUS_MINUS"]               = 70070,
            ["EVT_CDU_R_A"]                        = 70071,
            ["EVT_CDU_R_B"]                        = 70072,
            ["EVT_CDU_R_C"]                        = 70073,
            ["EVT_CDU_R_D"]                        = 70074,
            ["EVT_CDU_R_E"]                        = 70075,
            ["EVT_CDU_R_F"]                        = 70076,
            ["EVT_CDU_R_G"]                        = 70077,
            ["EVT_CDU_R_H"]                        = 70078,
            ["EVT_CDU_R_I"]                        = 70079,
            ["EVT_CDU_R_J"]                        = 70080,
            ["EVT_CDU_R_K"]                        = 70081,
            ["EVT_CDU_R_L"]                        = 70082,
            ["EVT_CDU_R_M"]                        = 70083,
            ["EVT_CDU_R_N"]                        = 70084,
            ["EVT_CDU_R_O"]                        = 70085,
            ["EVT_CDU_R_P"]                        = 70086,
            ["EVT_CDU_R_Q"]                        = 70087,
            ["EVT_CDU_R_R"]                        = 70088,
            ["EVT_CDU_R_S"]                        = 70089,
            ["EVT_CDU_R_T"]                        = 70090,
            ["EVT_CDU_R_U"]                        = 70091,
            ["EVT_CDU_R_V"]                        = 70092,
            ["EVT_CDU_R_W"]                        = 70093,
            ["EVT_CDU_R_X"]                        = 70094,
            ["EVT_CDU_R_Y"]                        = 70095,
            ["EVT_CDU_R_Z"]                        = 70096,
            ["EVT_CDU_R_SPACE"]                    = 70097,
            ["EVT_CDU_R_DEL"]                      = 70098,
            ["EVT_CDU_R_SLASH"]                    = 70099,
            ["EVT_CDU_R_CLR"]                      = 70100,
            ["EVT_CDU_R_FMCCOMM"]                  = 73833,

            // -----------------------------------------------------------------
            // PEDESTAL — CDU Center
            // -----------------------------------------------------------------
            ["EVT_CDU_C_L1"]                       = 70285,
            ["EVT_CDU_C_L2"]                       = 70286,
            ["EVT_CDU_C_L3"]                       = 70287,
            ["EVT_CDU_C_L4"]                       = 70288,
            ["EVT_CDU_C_L5"]                       = 70289,
            ["EVT_CDU_C_L6"]                       = 70290,
            ["EVT_CDU_C_R1"]                       = 70291,
            ["EVT_CDU_C_R2"]                       = 70292,
            ["EVT_CDU_C_R3"]                       = 70293,
            ["EVT_CDU_C_R4"]                       = 70294,
            ["EVT_CDU_C_R5"]                       = 70295,
            ["EVT_CDU_C_R6"]                       = 70296,
            ["EVT_CDU_C_INIT_REF"]                 = 70297,
            ["EVT_CDU_C_RTE"]                      = 70298,
            ["EVT_CDU_C_DEP_ARR"]                  = 70299,
            ["EVT_CDU_C_ALTN"]                     = 70300,
            ["EVT_CDU_C_VNAV"]                     = 70301,
            ["EVT_CDU_C_FIX"]                      = 70302,
            ["EVT_CDU_C_LEGS"]                     = 70303,
            ["EVT_CDU_C_HOLD"]                     = 70304,
            ["EVT_CDU_C_PROG"]                     = 70305,
            ["EVT_CDU_C_EXEC"]                     = 70306,
            ["EVT_CDU_C_MENU"]                     = 70307,
            ["EVT_CDU_C_NAV_RAD"]                  = 70308,
            ["EVT_CDU_C_PREV_PAGE"]                = 70309,
            ["EVT_CDU_C_NEXT_PAGE"]                = 70310,
            ["EVT_CDU_C_1"]                        = 70311,
            ["EVT_CDU_C_2"]                        = 70312,
            ["EVT_CDU_C_3"]                        = 70313,
            ["EVT_CDU_C_4"]                        = 70314,
            ["EVT_CDU_C_5"]                        = 70315,
            ["EVT_CDU_C_6"]                        = 70316,
            ["EVT_CDU_C_7"]                        = 70317,
            ["EVT_CDU_C_8"]                        = 70318,
            ["EVT_CDU_C_9"]                        = 70319,
            ["EVT_CDU_C_DOT"]                      = 70320,
            ["EVT_CDU_C_0"]                        = 70321,
            ["EVT_CDU_C_PLUS_MINUS"]               = 70322,
            ["EVT_CDU_C_A"]                        = 70323,
            ["EVT_CDU_C_B"]                        = 70324,
            ["EVT_CDU_C_C"]                        = 70325,
            ["EVT_CDU_C_D"]                        = 70326,
            ["EVT_CDU_C_E"]                        = 70327,
            ["EVT_CDU_C_F"]                        = 70328,
            ["EVT_CDU_C_G"]                        = 70329,
            ["EVT_CDU_C_H"]                        = 70330,
            ["EVT_CDU_C_I"]                        = 70331,
            ["EVT_CDU_C_J"]                        = 70332,
            ["EVT_CDU_C_K"]                        = 70333,
            ["EVT_CDU_C_L"]                        = 70334,
            ["EVT_CDU_C_M"]                        = 70335,
            ["EVT_CDU_C_N"]                        = 70336,
            ["EVT_CDU_C_O"]                        = 70337,
            ["EVT_CDU_C_P"]                        = 70338,
            ["EVT_CDU_C_Q"]                        = 70339,
            ["EVT_CDU_C_R"]                        = 70340,
            ["EVT_CDU_C_S"]                        = 70341,
            ["EVT_CDU_C_T"]                        = 70342,
            ["EVT_CDU_C_U"]                        = 70343,
            ["EVT_CDU_C_V"]                        = 70344,
            ["EVT_CDU_C_W"]                        = 70345,
            ["EVT_CDU_C_X"]                        = 70346,
            ["EVT_CDU_C_Y"]                        = 70347,
            ["EVT_CDU_C_Z"]                        = 70348,
            ["EVT_CDU_C_SPACE"]                    = 70349,
            ["EVT_CDU_C_DEL"]                      = 70350,
            ["EVT_CDU_C_SLASH"]                    = 70351,
            ["EVT_CDU_C_CLR"]                      = 70352,
            ["EVT_CDU_C_FMCCOMM"]                  = 76353,

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

            // -----------------------------------------------------------------
            // FORWARD PANEL — Source Selectors
            // -----------------------------------------------------------------
            ["EVT_FWD_NAV_SOURCE_L"]              = 69800,
            ["EVT_FWD_NAV_SOURCE_R"]              = 69908,
            ["EVT_FWD_DSPL_CTRL_SOURCE_L"]        = 69801,
            ["EVT_FWD_DSPL_CTRL_SOURCE_R"]        = 69909,
            ["EVT_FWD_AIR_DATA_ATT_SOURCE_L"]     = 69802,
            ["EVT_FWD_AIR_DATA_ATT_SOURCE_R"]     = 69910,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Chronometers
            // -----------------------------------------------------------------
            ["EVT_CHRONO_L_CHR"]                   = 69803,
            ["EVT_CHRONO_L_TIME_DATE_SELECT"]      = 69804,
            ["EVT_CHRONO_L_TIME_DATE_PUSH"]        = 71353,
            ["EVT_CHRONO_L_ET"]                    = 69805,
            ["EVT_CHRONO_L_SET"]                   = 69806,
            ["EVT_CHRONO_R_CHR"]                   = 69911,
            ["EVT_CHRONO_R_TIME_DATE_SELECT"]      = 69912,
            ["EVT_CHRONO_R_TIME_DATE_PUSH"]        = 72434,
            ["EVT_CHRONO_R_ET"]                    = 69913,
            ["EVT_CHRONO_R_SET"]                   = 69914,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Standby Instruments
            // -----------------------------------------------------------------
            ["EVT_STANDBY_ASI_KNOB"]              = 69940,
            ["EVT_STANDBY_ASI_KNOB_PUSH"]         = 72712,
            ["EVT_STANDBY_ALTIMETER_KNOB"]        = 69943,
            ["EVT_STANDBY_ALTIMETER_KNOB_PUSH"]   = 72742,

            // -----------------------------------------------------------------
            // FORWARD PANEL — Yoke
            // -----------------------------------------------------------------
            ["EVT_YOKE_AP_DISC_SWITCH"]           = 70716,

            // -----------------------------------------------------------------
            // PEDESTAL — TCAS
            // -----------------------------------------------------------------
            ["EVT_TCAS_XPNDR"]                    = 70383,
            ["EVT_TCAS_ALTSOURCE"]                 = 70375,
            ["EVT_TCAS_MODE"]                      = 70381,
            ["EVT_TCAS_IDENT"]                     = 70378,
            ["EVT_TCAS_TEST"]                      = 77123,

            // CDU Right and Center brightness (missing from initial registration)
            ["EVT_CDU_R_BRITENESS"]                = 70105,
            ["EVT_CDU_C_BRITENESS"]                = 70357,
        };
}
