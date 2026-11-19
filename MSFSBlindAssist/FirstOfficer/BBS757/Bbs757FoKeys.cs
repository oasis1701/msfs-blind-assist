using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// The 757 variables the First Officer reads and writes, by the definition's own keys: a map
/// control is keyed by its node (bbs757_control_map.json), a status row by its row key. Every
/// name here is one the definition registers (pinned by Bbs757FoChecklistStructureTests).
/// </summary>
public static class Bbs757FoKeys
{
    // ---- electrical / APU ----
    public const string Battery = "B757_OVHD_BATTERY_BUTTON";
    public const string StandbyPower = "B757_OVHD_STBY_POWER_SWITCH";      // 0 Off, 1 Auto, 2 BAT
    public const string LeftBusTie = "B757_OVHD_L_BUS_TIE_BUTTON";
    public const string RightBusTie = "B757_OVHD_R_BUS_TIE_BUTTON";
    public const string LeftGenCont = "B757_OVHD_L_GEN_CONT_BUTTON";
    public const string RightGenCont = "B757_OVHD_R_GEN_CONT_BUTTON";
    public const string ApuGen = "B757_OVHD_APU_GEN_BUTTON";
    public const string LeftUtilBus = "B757_OVHD_L_UTIL_BUS_BUTTON";
    public const string RightUtilBus = "B757_OVHD_R_UTIL_BUS_BUTTON";
    public const string ExtPwrButton = "B757_OVHD_EXT_PWR_BUTTON";            // momentary
    public const string ExtPwrOn = Bbs757SharedKeys.ExtPwrOn;                 // ON light
    public const string ExtPwrAvail = Bbs757SharedKeys.ExtPwrAvail;           // AVAIL light
    public const string ApuSelector = "B757_OVHD_APU_SWITCH";                 // 0 Off, 1 On, 2 Start
    public const string ApuState = "B757_APU_STATE";                          // 0 Off, 1 Starting, 2 Running, 3 Shutting down

    // ---- IRS ----
    public static readonly string[] IrsSwitches = { "B757_IRS_L_SWITCH", "B757_IRS_C_SWITCH", "B757_IRS_R_SWITCH" };
    public static readonly string[] IrsAlignLights = { "BBS757_IRS_L_ALIGN", "BBS757_IRS_C_ALIGN", "BBS757_IRS_R_ALIGN" };
    public const double IrsOff = 0, IrsAlign = 1, IrsNav = 2;

    // ---- engine controls ----
    public const string LeftYawDamper = "B757_OVHD_L_YAW_DAMPER_BUTTON";
    public const string RightYawDamper = "B757_OVHD_R_YAW_DAMPER_BUTTON";
    public const string LeftEec = "B757_OVHD_L_ELEC-ENG-CONT_BUTTON";         // Rolls-Royce only (guarded)
    public const string RightEec = "B757_OVHD_R_ELEC-ENG-CONT_BUTTON";
    // The engine limiter switches. The manual lists them as Rolls-Royce only ("ENGINE LIMITERS (RR ONLY) ... ON",
    // S39 #31 / E87), but the Pratt & Whitney cockpit has the same two switches (control-verification E3: the node
    // is in the PW interior model, the map labels them without "(RR)", and BlueBird itself loads them ON on both
    // variants). So the First Officer sets them ON on the PW too: it only restores the state BlueBird loads.
    public const string LeftLimiter = "B757_OVHD_L_ENG_LIMITER_BUTTON";
    public const string RightLimiter = "B757_OVHD_R_ENG_LIMITER_BUTTON";
    public const string Ignition = "B757_OVHD_ENG_START_SWITCH";              // 0 "1", 1 "2", 2 BOTH
    public const string LeftStart = "B757_OVHD_L_ENG_START_SWITCH";           // 1 GND, 3 AUTO, 4 OFF, 5 CONT, 6 FLT
    public const string RightStart = "B757_OVHD_R_ENG_START_SWITCH";
    public const double StartGnd = 1, StartAuto = 3;
    public const string LeftStartValve = "BBS757_L_START_VALVE";
    public const string RightStartValve = "BBS757_R_START_VALVE";

    /// <summary>The engines panel's EGT rows (stock exhaust gas temperature, degrees C): the Rolls-Royce cold and hot start rules.</summary>
    public const string LeftEgt = "BBS757_ENG1_EGT";
    public const string RightEgt = "BBS757_ENG2_EGT";

    /// <summary>BlueBird's own autothrottle-armed flag (L:BBS_AUTO_THROTTLE, ruling R40): the A/T's real state, not the switch.</summary>
    public const string AutothrottleArmedState = Bbs757AutoflightAnnouncements.AtArmedKey;

    /// <summary>
    /// The engine variant as BlueBird publishes it, <c>L:B757_ENG_VARIANT</c>: 1 Pratt &amp; Whitney (measured on the
    /// PW, 2026-10-27), 0 Rolls-Royce (sound.xml). Registered by the First Officer. The aircraft title decides first
    /// (<see cref="Bbs757FoStateEvaluator.TitleVariant"/>); without one only a 1 is believed (PW), because an
    /// unwritten L:var also reads 0, so 0 is unknown, never Rolls-Royce.
    /// </summary>
    public const string EngineVariant = "BBS757_FO_ENG_VARIANT";

    // Fuel control switches: PW 0 CUTOFF / 2 RUN; RR 0 RUN / 1 RICH / 2 CUTOFF.
    public const string LeftFuelPw = "BBS_fuelcut_PW1";
    public const string RightFuelPw = "BBS_fuelcut_PW2";
    public const string LeftFuelRr = "BBS_fuelcut_RR1";
    public const string RightFuelRr = "BBS_fuelcut_RR2";
    public const double PwCutoff = 0, PwRun = 2, RrRun = 0, RrRich = 1, RrCutoff = 2;

    // ---- hydraulics ----
    public const string LeftEngHyd = "B757_OVHD_L_ENG_HYD_PUMPS_BUTTON";
    public const string RightEngHyd = "B757_OVHD_R_ENG_HYD_PUMPS_BUTTON";
    public const string LeftElecHyd = "B757_OVHD_L_ENG_ELEC_PUMPS_BUTTON";
    public const string C1ElecHyd = "B757_OVHD_C_ENG1_HYD_PUMPS_BUTTON";
    public const string C2ElecHyd = "B757_OVHD_C_ENG2_HYD_PUMPS_BUTTON";
    public const string RightElecHyd = "B757_OVHD_R_ENG_ELEC_PUMPS_BUTTON";

    // ---- fuel ----
    public const string LeftFwdPump = "B757_OVHD_L_PUMPS_FWD_BUTTON";
    public const string LeftAftPump = "B757_OVHD_L_PUMPS_AFT_BUTTON";
    public const string RightFwdPump = "B757_OVHD_R_PUMPS_FWD_BUTTON";
    public const string RightAftPump = "B757_OVHD_R_PUMPS_AFT_BUTTON";
    public const string CenterLeftPump = "B757_OVHD_C_PUMPS_L_BUTTON";
    public const string CenterRightPump = "B757_OVHD_C_PUMPS_R_BUTTON";
    public const string XfeedFwd = "B757_OVHD_XFEED_PUMPS_FWD_BUTTON";
    public const string XfeedAft = "B757_OVHD_XFEED_PUMPS_AFT_BUTTON";
    public const string CenterFuelGallons = Bbs757Loadsheet.CenterFuelKey;

    // ---- anti-ice, windows, lights, signs ----
    public const string WingAntiIce = "B757_OVHD_ANTI_ICE_WING_BUTTON";
    public const string LeftEngAntiIce = "B757_OVHD_ANTI_ICE_L_ENG_BUTTON";
    public const string RightEngAntiIce = "B757_OVHD_ANTI_ICE_R_ENG_BUTTON";
    public static readonly string[] WindowHeat =
    {
        "B757_OVHD_L_SIDE_WINDOW_HEAT_BUTTON", "B757_OVHD_L_FWD_WINDOW_HEAT_BUTTON",
        "B757_OVHD_R_FWD_WINDOW_HEAT_BUTTON", "B757_OVHD_R_SIDE_WINDOW_HEAT_BUTTON",
    };
    // The node names and labels of the exterior lights do not match (plan 02's curation): these are
    // keyed by node, named by what the control drives (its state var).
    public const string BeaconLight = "B757_OVHD_LOGO";             // state B757_OVHD_BEACON_LIGHT, "Anti-col Red Lights"
    public const string PositionLights = "B757_OVHD_BEACON";        // state B757_OVHD_NAV_LIGHTS
    public const string StrobeLights = "B757_OVHD_NAV";             // state B757_OVHD_STROBE, "Anti-col White Lights"
    public const string TaxiLight = "B757_OVHD_btn_lights_taxi";    // state B757_OVHD_NOSE_LANDING_LIGHT
    public const string LeftLanding = "B757_OVHD_SW_landing_L";
    public const string RightLanding = "B757_OVHD_SW_landing_R";
    public const string LeftTurnoff = "B757_OVHD_RWY_TURN_L";
    public const string RightTurnoff = "B757_OVHD_RWY_TURN_R";
    public const string WingLights = "B757_OVHD_HI_INT";
    public const string LogoLights = "B757_OVHD_LOGO_BUTTON";
    public const string SeatBelts = "B757_OVHD_SEATBELTS_SWITCH";   // 0 OFF, 1 AUTO, 2 ON
    public const string NoSmoking = "B757_OVHD_NO_SMOKING_SWITCH";
    public const double SignOff = 0, SignAuto = 1, SignOn = 2;
    public const string EmergencyLights = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK";   // 0 OFF, 1 ARMED, 2 ON (guarded)

    /// <summary>
    /// The FWD cabin call button (momentary): the hi-lo chime. Any of the panel's cabin call buttons rings it (owner,
    /// 2026-11-08); BlueBird's descent procedure illustrates FWD (p. 55).
    /// </summary>
    public const string CabinCall = "B757_OVHD_FWD_CABIN_CALL_BUTTON";
    public const string Wipers = "B757_OVHD_WIPER_SWITCH";

    // ---- air conditioning / pressurisation ----
    public const string LeftPackSelector = "B757_OVHD_SEL_L_PACK_SWITCH";   // 0 OFF, 1 AUTO, 2-4 STBY
    public const string RightPackSelector = "B757_OVHD_SEL_R_PACK_SWITCH";
    public const double PackOff = 0, PackAuto = 1;
    public const string Isolation = "B757_OVHD_ISOLATION_BUTTON";
    public const string LeftEngBleed = "B757_OVHD_LENG_BUTTON";
    public const string RightEngBleed = "B757_OVHD_RENG_BUTTON";
    public const string ApuBleed = "B757_OVHD_APU_BUTTON";
    public const string TrimAir = "B757_OVHD_TRIM_AIR_BUTTON";
    public const string LeftRecirc = "B757_OVHD_L_RECIRC_BUTTON";
    public const string RightRecirc = "B757_OVHD_R_RECIRC_BUTTON";
    public const string PressMode = "B757_OVHD_MODE_SELECT_SWITCH";         // 0 AUTO 1, 1 AUTO 2, 2 MAN

    // ---- MCP / main panel ----
    public const string CaptainFd = "B757_MCP_SW_FD_Cap";
    public const string FirstOfficerFd = "B757_MCP_SW_FD_FO";
    // The MCP LNAV and VNAV buttons (momentary) and their lights (Bbs757McpLights' streamed rows: lit armed or engaged).
    public const string LnavButton = "B757_MCP_LNAV_BUTTON";
    public const string VnavButton = "B757_MCP_VNAV_BUTTON";
    public const string LnavLight = "BBS757_MCP_LIGHT_LNAV";
    public const string VnavLight = "BBS757_MCP_LIGHT_VNAV";
    public const string AutothrottleArm = "B757_MCP_SW_AP_throttle";        // 0 ARM (On), 10 Off
    public const double AutothrottleArmed = 0, AutothrottleOff = 10;
    public const string BankLimit = "B757_MCP_AP_HDG_BNKLIM";
    public const string Autobrake = "B757_MAIN_knob_gears_altn_autobrake";  // 0 RTO, 1 OFF, 2 DISARM, 3-6 "1"-"4", 7 MAX AUTO
    public const double AutobrakeRto = 0, AutobrakeOff = 1;
    public static readonly string[] Heaters =
    {
        "B757_MAIN_L_FOOT_HEATERS_SWITCH", "B757_MAIN_L_SHOULDER_HEATERS_SWITCH",
        "B757_MAIN_R_FOOT_HEATERS_SWITCH", "B757_MAIN_R_SHOULDER_HEATERS_SWITCH",
    };
    public const double HeaterOff = 2;
    public static readonly string[] TerrainAndWeather =
    {
        "B757_ADI_1_TERR_BUTTON", "B757_ADI_1_WXR_BUTTON", "B757_ADI_2_TERR_BUTTON", "B757_ADI_2_WXR_BUTTON",
    };
    public const string GearLever = "B757_MAIN_Landing_Gear_Y";              // state B757_GEAR_HANDLE_POS: 0 UP, 0.5 OFF, 1 DOWN
    public const string ParkingBrake = "BBS757_PEDESTAL_Park_Brake_Lever";   // state B757_FC_PARK_BRAKE (B757-1)
    public const string TcasMode = "B757_PED_TCAS_MODE_KNOB";                // 0 STBY, 1 ALT RPTG OFF, 2 XPNDR, 3 TA ONLY, 4 TA/RA
    public const double TcasStandby = 0, TcasTaRa = 4;

    // ---- streamed rows (plans 01, 06) ----
    public const string FlapsHandle = Bbs757Readouts.FlapsHandleKey;         // FLAPS HANDLE INDEX: 0 UP .. 6 "30"
    public const string Speedbrake = Bbs757Readouts.SpeedbrakeKey;           // L:B757_FC_SPOILER_LEVER
    public static readonly string[] GearLegs = { Bbs757Readouts.GearNoseKey, Bbs757Readouts.GearLeftKey, Bbs757Readouts.GearRightKey };
    public static readonly string[] Doors =
    {
        "BBS757_DOOR_L1", "BBS757_DOOR_L2", "BBS757_DOOR_L3", "BBS757_DOOR_R1", "BBS757_DOOR_R2", "BBS757_DOOR_R3",
    };
    public static readonly string[] CargoDoors = { "BBS757_DOOR_CARGO_FWD", "BBS757_DOOR_CARGO_AFT" };
}
