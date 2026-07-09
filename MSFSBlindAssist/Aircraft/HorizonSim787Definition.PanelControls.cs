using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class HorizonSim787Definition
{
    // =========================================================================
    // Panel Controls
    // =========================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        return new Dictionary<string, List<string>>
        {
            // --- Overhead ---
            ["Electrical"] = new List<string>
            {
                "HS787_BatSwitch1",
                "HS787_BatSwitch2",
                "HS787_ExtPwr1",
                "HS787_ExtPwr2",
                "HS787_APU_Knob",
                "HS787_ApuGen1",
                "HS787_ApuGen2",
                "HS787_Gen1",
                "HS787_Gen2",
                "HS787_AvionicsMaster",
                "HS787_StandbyPower",
                "HS787_EmerLights",
                "HS787_UtilityCabin",
                "HS787_UtilityIfe"
            },
            ["IRS"] = new List<string>
            {
                // Knobs are user controls. The aligned/position indicators are
                // read-only — moved to GetPanelDisplayVariables so they render
                // as a status field, not an editable combo.
                "HS787_IRS_Knob1",
                "HS787_IRS_Knob2",
                "HS787_AirDataSrc1",
                "HS787_AirDataSrc2"
            },
            ["Hydraulics"] = new List<string>
            {
                "HS787_HydEngL",
                "HS787_HydEngR",
                "HS787_HydDemandLeft",
                "HS787_HydDemandRight",
                "HS787_HydC1",
                "HS787_HydC2"
            },
            ["Fuel"] = new List<string>
            {
                "HS787_FuelPump_LFwd",
                "HS787_FuelPump_RFwd",
                "HS787_FuelPump_LAft",
                "HS787_FuelPump_RAft",
                "HS787_FuelPump_CtrL",
                "HS787_FuelPump_CtrR",
                "HS787_FuelPump_APU",
                "HS787_FuelXfeed",
                "HS787_FuelBalance",
                "HS787_FuelBalanceActive",
                "HS787_FuelBalanceFault"
            },
            ["Bleed Air"] = new List<string>
            {
                "HS787_BleedEng1",
                "HS787_BleedEng2",
                "HS787_BleedAPU",
                "HS787_BleedIso"
            },
            ["Fire Protection"] = new List<string>
            {
                "HS787_FireTest",
                "HS787_EngFireHandle1",
                "HS787_EngFireHandle2",
                "HS787_APUFireHandle"
            },
            ["Cargo Fire"] = new List<string>
            {
                "HS787_CargoFireFwd",
                "HS787_CargoFireAft",
                "HS787_CargoFireDisch"
            },
            ["Air Conditioning"] = new List<string>
            {
                "HS787_PackL",
                "HS787_PackR",
                "HS787_PacksAuto",
                "HS787_TrimAirL",
                "HS787_TrimAirR",
                "HS787_RecircUpper",
                "HS787_RecircLower"
            },
            ["Pressurization"] = new List<string>
            {
                "HS787_PressManAltOn"
            },
            ["Cooling"] = new List<string>
            {
                "HS787_CoolingAft",
                "HS787_EquipFwd",
                "HS787_EmerLightsCover"
            },
            ["Anti-Ice"] = new List<string>
            {
                "HS787_AntiIceEng1",
                "HS787_AntiIceEng2",
                "HS787_AntiIceWing",
                "HS787_PitotHeat",
                "HS787_WshldDeice1",
                "HS787_WshldDeice2",
                "HS787_WshldDeice3",
                "HS787_WshldDeice4"
            },
            ["Signs"] = new List<string>
            {
                "HS787_Seatbelts",
                "HS787_NoSmoking"
            },
            ["Flight Controls"] = new List<string>
            {
                "HS787_AltnFlapsArmed",
                "HS787_AltnFlapsSelector",
                "HS787_FlightComputerAuto"
            },
            ["Engines"] = new List<string>
            {
                "HS787_EngineAutoStart",
                "HS787_EngineStart1",
                "HS787_EngineStart2",
                "HS787_FuelControl1",
                "HS787_FuelControl2"
            },

            // --- Glareshield ---
            ["EFIS"] = new List<string>
            {
                "HS787_BaroSelector",
                "HS787_BaroSTD",
                "HS787_MinsMode",
                "HS787_FPVMode"
            },
            ["MCP"] = new List<string>
            {
                "HS787_APMaster",
                "HS787_FlightDirector",
                "HS787_ATStatus",
                "HS787_YawDamper",
                "HS787_FPAMode",
                "HS787_TRKMode",
                "HS787_VNAV",
                "HS787_LOC",
                "HS787_APP"
            },
            ["HUD"] = new List<string>
            {
                "HS787_HudDown1",
                "HS787_HudDown2",
                "HS787_HudAutoBrt1",
                "HS787_HudAutoBrt2",
                "HS787_HudSymbology1",
                "HS787_HudSymbology2",
                "HS787_HudDeclutterInhibit1",
                "HS787_HudDeclutterInhibit2"
            },
            // FMC Status: all members are status indicators written by the FMS, not
            // user-toggleable. Rendered as a read-only display via GetPanelDisplayVariables
            // (which exposes them at the bottom of the panel as a status field).
            // Approach Course / Flight Timer Value / Checklist Phase are numeric reads.
            ["FMC Status"] = new List<string>(),
            // Annunciators / Fire: status indicators rendered as a read-only display
            // (see GetPanelDisplayVariables). Empty control list keeps the panel
            // section visible in the section nav.
            ["Annunciators"] = new List<string>(),
            ["Fire"] = new List<string>(),
            // Flight Data sub-panels: pure read-only nav/engine/timer readouts whose values
            // come from GetPanelDisplayVariables. They were listed in GetPanelStructure +
            // GetPanelDisplayVariables but NOT here, so MainForm's panel-build hit its
            // "not in GetPanelControls -> return" guard and rendered them completely EMPTY.
            // The empty control list is what makes a display-only panel render (FMC Status pattern).
            ["VNAV"] = new List<string>(),
            ["LNAV and Progress"] = new List<string>(),
            ["Glidepath"] = new List<string>(),
            ["Engine Data"] = new List<string>(),
            ["Flight Control Inputs"] = new List<string>(),
            ["Timers"] = new List<string>(),
            ["Other Data"] = new List<string>(),
            // Warnings: momentary reset buttons for the Master Caution / Master Warning.
            // Each button's label is suffixed with the current state (On / Off) so the
            // user can tell at a glance whether there is anything to acknowledge.
            ["Warnings"] = new List<string>
            {
                "HS787_MasterCautionReset",
                "HS787_MasterWarningReset"
            },

            // --- Pedestal ---
            // PMDG layout: per radio, [Active display button], [Standby textbox], [Swap button].
            ["Radio"] = new List<string>
            {
                "COM1_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:1", "COM1_RADIO_SWAP",
                "COM2_ActiveFreq", "COM_STANDBY_FREQUENCY_SET:2", "COM2_RADIO_SWAP"
            },
            ["Transponder"] = new List<string>
            {
                "HS787_TransponderMode",
                "TRANSPONDER_CODE_SET",
                "HS787_XpndrIdent"
            },
            ["Landing"] = new List<string>
            {
                "HS787_ParkBrake",
                "HS787_Autobrake",
                "HS787_AntiSkid",
                "HS787_FlapsHandle",
                "HS787_GearHandle"
            },
            ["Lighting"] = new List<string>
            {
                "HS787_LightMaster",
                "HS787_LightBeacon",
                "HS787_LightStrobe",
                "HS787_LightNav",
                "HS787_LightLanding",
                "HS787_LightTaxi",
                "HS787_LightLogo",
                "HS787_LightWing"
                // Interior light bus switches (Panel, Instrument, Glareshield, Pedestal,
                // Cabin, Dome) removed from the panel because their write events on HS787
                // aren't reliable via the standard MSFS K-events; the SimVars exist and
                // auto-announce on external change (still wired in continuous monitoring).
            },
            ["Options"] = new List<string>
            {
                "HS787_SATCOM",
                "HS787_VBar",
                "HS787_EfbPower"
            },

            // --- Ground Services ---
            ["Doors"] = new List<string>
            {
                "HS787_Door_1L",
                "HS787_Door_1R",
                "HS787_Door_2L",
                "HS787_Door_2R",
                "HS787_Door_3L",
                "HS787_Door_3R",
                "HS787_Door_4L",
                "HS787_Door_4R",
                "HS787_Door_FwdCargo",
                "HS787_Door_AftCargo",
                "HS787_FdDoorPower"
            },
            ["Services"] = new List<string>
            {
                "HS787_RefuelDoor",
                "HS787_GPUPipe"
            }
        };
    }
}
