using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.MD11.Md11FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.MD11;

using Item = Models.ChecklistItem<Md11FoActionExecutor, Md11FoStateEvaluator>;
using Group = Models.ChecklistGroup<Md11FoActionExecutor, Md11FoStateEvaluator>;
using CheckFn = System.Func<Md11FoActionExecutor, Md11FoStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The TFDi MD-11 First Officer checklists. Two layers, as on every profile: an ACTION group per
/// flow (ticking fires the item's switch through the executor, and it auto-detects from state), and
/// TFDi's own published normal checklist as action-free READBACK (*_CL) groups, every item and
/// response verbatim ("Item: Response"; an indented TFDi sub-check reads "Parent: Sub — Response").
/// A readback item the app can read auto-ticks; the rest (visual checks, FMS entries, test
/// results only a sighted crew sees) are manual ticks.
/// </summary>
public static class Md11FoChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        PowerUp(), CockpitEntryCL(),
        Preflight(), PreflightCL(),
        BeforeStart(), BeforeStartCL(),
        EngineStart(), EngineStartCL(),
        AfterStart(), AfterStartCL(),
        BeforeTakeoff(), BeforeTakeoffCL(),
        AfterTakeoff(), AfterTakeoffCL(),
        Passing10000CL(), TransitionAltitudeCL(), CruiseCL(), TransitionLevelCL(),
        Descent(), DescentCL(),
        BeforeLanding(), BeforeLandingCL(),
        AfterLanding(), AfterLandingCL(),
        Parking(), ParkingCL(),
        Shutdown(), ShutdownCL(),
    };

    private const string Batt = "MD11_OVHD_ELEC_BATT_BT";
    private const string EmerPwr = "MD11_OVHD_ELEC_EMER_PWR_KB";
    private const string ElecSel = "MD11_OVHD_ELEC_SYSTEM_SEL_BT";
    private const string FuelSel = "MD11_OVHD_FUEL_SYSTEM_SEL_BT";
    private const string HydSel = "MD11_OVHD_HYD_SYSTEM_SEL_BT";
    private const string AirSel = "MD11_OVHD_PNEU_SYSTEM_SEL_BT";
    private const string CabinSel = "MD11_OVHD_PNEU_CABIN_SYSTEM_SEL_BT";
    private const string ApuBleed = "MD11_OVHD_PNEU_APU_BLEED_BT";
    private const string EmerLts = "MD11_OVHD_LTS_EMER_SW";
    private const string NoSmoke = "MD11_OVHD_LTS_NO_SMOKE_SW";
    private const string SeatBelts = X.SeatBelts;
    private const string Evac = "MD11_AOVHD_EVAC_SW";
    private const string Autobrake = "MD11_CTR_AUTOBRAKE_SW";
    private const string Nose = "MD11_OVHD_LTS_NOSE_SW";
    private const string XpdrMode = "MD11_PED_XPNDR_MODE_KB";
    private const string AltRptg = "MD11_PED_XPNDR_ALT_RPTG_KB";
    private const string Park = "MD11_THR_PARK_LVR";
    private const string ApuState = "MD11_APU_STATE";

    private static bool Is(double v, double p) => Md11FoSwitching.AtPosition(v, p);
    private static bool On(double v) => v > 0.5;
    private static bool Off(double v) => v < 0.5;
    private static bool ApuStartingOrRunning(double v) => v > 0.5 && v < 2.5;

    private static CheckFn Set(string key, int target) => (e, _) => e.Set(key, target);

    private static CheckFn SetAll(params (string Key, int Target)[] writes) => async (e, _) =>
    {
        foreach (var (k, t) in writes) await e.Set(k, t);
    };

    /// <summary>
    /// A SLOW condition: hold the tick's action open until the item's own state is true, so the
    /// manual-tick revert grace never judges it early (CLAUDE.md — the Fenix APU lesson).
    /// </summary>
    private static CheckFn SetThenWait(string key, int target, string field, Func<double, bool> ok, int timeoutSec)
        => async (e, s) =>
        {
            await e.Set(key, target);
            for (int i = 0; i < timeoutSec && !ok(s.GetValue(field)); i++)
                await Task.Delay(1000);
        };

    // ================================================================== action groups

    private static Group PowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Items = new()
        {
            Auto("PU_FUEL_OFF", "POWER_UP", "Fuel switches: OFF", "FO_FUEL_SWITCHES_OFF", On,
                SetAll(("MD11_THR_L_FUEL_SW", 0), ("MD11_THR_C_FUEL_SW", 0), ("MD11_THR_R_FUEL_SW", 0))),
            Auto("PU_PARK", "POWER_UP", "Parking brake: SET", Park, On, Set(Park, 1)),
            Auto("PU_BATT", "POWER_UP", "Battery: ON", Batt, On, Set(Batt, 1)),
            Auto("PU_EXT_PWR", "POWER_UP", "External power: ON", "FO_EXT_POWER_ON", On, Set(X.ExtPower, 1)),
            Auto("PU_WXR_OFF", "POWER_UP", "Weather radar: OFF", "FO_WXR_OFF", On, Set(X.WeatherRadarOff, 1)),
            Auto("PU_EMER_PWR", "POWER_UP", "Emergency power: ARMED", EmerPwr, v => Is(v, 1), Set(EmerPwr, 1)),
            Auto("PU_IRS", "POWER_UP", "IRS 1, 2 and auxiliary: NAV", "FO_IRS_NAV", On,
                SetAll(("MD11_OVHD_IRS_1_KB", 1), ("MD11_OVHD_IRS_2_KB", 1), ("MD11_OVHD_IRS_3_KB", 1))),
            Auto("PU_NAV", "POWER_UP", "Navigation lights: ON", "FO_NAV_ON", On, Set("MD11_OVHD_LTS_NAV_BT", 1)),
            Reminder("PU_IRS_INIT", "POWER_UP", "Initialize the IRS on the MCDU F-PLN INIT page"),
        }
    };

    private static Group Preflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items = new()
        {
            ActionManual("PF_FIRE_TEST", "PREFLIGHT", "Engine and APU fire test", Set("MD11_AOVHD_FIRETEST_BT", 1)),
            ActionManual("PF_MW_RESET", "PREFLIGHT", "Master warning: reset", Set("MD11_GSL_MST_WRN_BT", 1)),
            ActionManual("PF_FUEL_USED", "PREFLIGHT", "Fuel used: reset", Set("MD11_OVHD_FUELUSEDRESET_BT", 1)),
            ActionManual("PF_ANNUN_TEST", "PREFLIGHT", "Annunciator light test", Set(X.AnnunciatorTest, 1)),
            ActionManual("PF_CARGO_FIRE_TEST", "PREFLIGHT", "Cargo fire manual test", Set("MD11_AOVHD_CRGSMK_TEST_BT", 1)),
            ActionManual("PF_CVR_TEST", "PREFLIGHT", "Cockpit voice recorder test", Set("MD11_OVHD_CVR_TEST_BT", 1)),
            Auto("PF_HYD_AUTO", "PREFLIGHT", "Hydraulic system: AUTO", HydSel, Off, Set(HydSel, 0)),
            ActionManual("PF_HYD_TEST", "PREFLIGHT", "Hydraulic test", Set(X.HydraulicTest, 1)),
            Auto("PF_ELEC_AUTO", "PREFLIGHT", "Electrical system: AUTO", ElecSel, Off, Set(ElecSel, 0)),
            Auto("PF_EMER_PWR", "PREFLIGHT", "Emergency power: ARMED", EmerPwr, v => Is(v, 1), Set(EmerPwr, 1)),
            Auto("PF_AIR_AUTO", "PREFLIGHT", "Air system: AUTO", AirSel, Off, Set(AirSel, 0)),
            Auto("PF_ECON", "PREFLIGHT", "Economy: ON", "FO_ECON_ON", On, Set("MD11_OVHD_PNEU_ECON_BT", 1)),
            Auto("PF_FUEL_AUTO", "PREFLIGHT", "Fuel system: AUTO", FuelSel, Off, Set(FuelSel, 0)),
            Auto("PF_CABIN_AUTO", "PREFLIGHT", "Cabin pressure: AUTO", CabinSel, Off, Set(CabinSel, 0)),
            ActionManual("PF_FUEL_QTY_TEST", "PREFLIGHT", "Fuel quantity test", Set("MD11_OVHD_FUEL_QTY_TEST_BT", 1)),
            ActionManual("PF_EMER_LT_TEST", "PREFLIGHT", "Emergency lights test", Set("MD11_OVHD_LTS_EMER_TEST_BT", 1)),
            Auto("PF_EMER_LTS", "PREFLIGHT", "Emergency lights: ARMED", EmerLts, v => Is(v, 1), Set(EmerLts, 1)),
            Auto("PF_NO_SMOKE", "PREFLIGHT", "No smoking signs: ON", NoSmoke, v => Is(v, 2), Set(NoSmoke, 2)),
            Auto("PF_SEAT_BELTS", "PREFLIGHT", "Seat belt signs: OFF", SeatBelts, v => Is(v, 0), Set(SeatBelts, 0)),
            Auto("PF_EVAC", "PREFLIGHT", "Evacuation switch: ARMED", Evac, v => Is(v, 1), Set(Evac, 1)),
            ActionManual("PF_GPWS_TEST", "PREFLIGHT", "GPWS test, and back to normal", Set(X.GpwsTest, 1)),
            Auto("PF_WINDSHIELD", "PREFLIGHT", "Windshield anti-ice: ON, normal, defog on", "FO_WINDSHIELD_ON", On,
                SetAll(("MD11_OVHD_WNDSHLD_AICE_L_BT", 1), ("MD11_OVHD_WNDSHLD_AICE_R_BT", 1),
                       ("MD11_OVHD_WNDSHLD_AICE_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", 0))),
            Auto("PF_AUTOBRAKE", "PREFLIGHT", "Autobrake: T.O.", Autobrake, v => Is(v, 0), Set(Autobrake, 0)),
            Auto("PF_DAF", "PREFLIGHT", "Dial-A-Flap: takeoff setting", "FO_DAF_TAKEOFF", On,
                async (e, s) => { if (s.TakeoffDialAFlap is int d) await e.Set(X.DialAFlap, d); }),
            ActionManual("PF_WXR_TEST", "PREFLIGHT", "Weather radar test, then off", Set(X.WeatherRadarTest, 1)),
            ActionManual("PF_OXY_CAPT", "PREFLIGHT", "Captain oxygen test", Set("MD11_LSIDE_OXY_TEST_BT", 1)),
            ActionManual("PF_OXY_FO", "PREFLIGHT", "First officer oxygen test", Set("MD11_RSIDE_OXY_TEST_BT", 1)),
            ActionManual("PF_ISFD_TEST", "PREFLIGHT", "Standby display test", Set("MD11_MIP_ISFD_TEST_BT", 1)),
            ActionManual("PF_TCAS_TEST", "PREFLIGHT", "TCAS test", Set("MD11_PED_XPNDR_TEST_BT", 1)),
            ActionManual("PF_CARGO_DOOR_TEST", "PREFLIGHT", "Cargo door test", Set("MD11_OVHD_CRG_DOOR_TEST_BT", 1)),
            Reminder("PF_CPT_ALTIMETERS", "PREFLIGHT", "Set the altimeters to local QNH"),
            Reminder("PF_CPT_THROTTLES", "PREFLIGHT", "Check throttle travel and the aural warnings, then close the throttles"),
            Reminder("PF_CPT_RADIOS", "PREFLIGHT", "Set the radio panels and the transponder code"),
            Reminder("PF_CPT_TRIMS", "PREFLIGHT", "Check rudder and aileron trim at zero"),
            Reminder("PF_CPT_DISPLAYS", "PREFLIGHT", "Check the displays for faults, and clear the system display cue lights"),
        }
    };

    private static Group BeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Auto("BS_APU", "BEFORE_START", "APU: start", ApuState, ApuStartingOrRunning, Set(X.ApuStart, 1)),
            Auto("BS_SEAT_BELTS", "BEFORE_START", "Seat belt signs: ON", SeatBelts, v => Is(v, 2), Set(SeatBelts, 2)),
            Auto("BS_EXT_OFF", "BEFORE_START", "External power: OFF", "FO_EXT_POWER_ON", Off, Set(X.ExtPower, 0)),
            Auto("BS_AUX_PUMP", "BEFORE_START", "Auxiliary hydraulic pump 1: ON", "FO_AUX_PUMP_1_ON", On,
                Set("MD11_OVHD_HYD_AUX_PUMP_1_BT", 1)),
            Auto("BS_IGNITION", "BEFORE_START", "Engine ignition: A or B", "FO_IGNITION_SELECTED", On, Set(X.Ignition, 1)),
            Auto("BS_APU_BLEED", "BEFORE_START", "APU bleed: ON", ApuBleed, On, Set(ApuBleed, 1)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON", "FO_BEACON_ON", On, Set("MD11_OVHD_LTS_BCN_BT", 1)),
            Reminder("BS_FMS", "BEFORE_START", "Confirm the FMS: weights, wind, runway slope, temperature and V-speeds"),
            Reminder("BS_EIS", "BEFORE_START", "Set the EIS bugs"),
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    private static Group EngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            // The START switch is held electrically and pops in at 45-52 % N2 — the physics of the
            // Boeing start selectors — so, like them, it latches on the engine RUNNING (the one
            // sanctioned StayComplete), never on the switch position, which un-ticks at cutout.
            AutoLatch("ES_E3_START", "ENGINE_START", "Engine 3 start switch: PULL", "FO_ENG3_N2",
                v => v >= Md11FoStateEvaluator.EngineRunningN2, Set(X.EngineStart3, 1)),
            Auto("ES_E3_FUEL", "ENGINE_START", "Engine 3 fuel switch: ON", "MD11_THR_R_FUEL_SW", On, Set("MD11_THR_R_FUEL_SW", 1)),
            AutoLatch("ES_E1_START", "ENGINE_START", "Engine 1 start switch: PULL", "FO_ENG1_N2",
                v => v >= Md11FoStateEvaluator.EngineRunningN2, Set(X.EngineStart1, 1)),
            Auto("ES_E1_FUEL", "ENGINE_START", "Engine 1 fuel switch: ON", "MD11_THR_L_FUEL_SW", On, Set("MD11_THR_L_FUEL_SW", 1)),
            AutoLatch("ES_E2_START", "ENGINE_START", "Engine 2 start switch: PULL", "FO_ENG2_N2",
                v => v >= Md11FoStateEvaluator.EngineRunningN2, Set(X.EngineStart2, 1)),
            Auto("ES_E2_FUEL", "ENGINE_START", "Engine 2 fuel switch: ON", "MD11_THR_C_FUEL_SW", On, Set("MD11_THR_C_FUEL_SW", 1)),
            Reminder("ES_ANTI_ICE", "ENGINE_START", "Engine anti-ice as required"),
        }
    };

    private static Group AfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_APU_BLEED", "AFTER_START", "APU bleed: OFF", ApuBleed, Off, Set(ApuBleed, 0)),
            Auto("AS_FLAPS", "AFTER_START", "Flap handle: Dial-A-Flap detent", "FO_FLAPS_DAF", On, Set(X.FlapHandle, 2)),
            Auto("AS_SPOILERS", "AFTER_START", "Spoilers: ARM", "FO_SPOILERS_ARMED", On, Set(X.Spoilers, 1)),
            Auto("AS_AUTOBRAKE", "AFTER_START", "Autobrake: T.O.", Autobrake, v => Is(v, 0), Set(Autobrake, 0)),
            ActionManual("AS_CONFIG_PAGE", "AFTER_START", "System display: config page", Set("MD11_PED_SD_CONFIG_BT", 1)),
            Auto("AS_TAXI_LIGHT", "AFTER_START", "Nose light: TAXI", Nose, v => Is(v, 1), Set(Nose, 1)),
            Reminder("AS_FLT_CTRL", "AFTER_START", "Check the flight controls"),
            Reminder("AS_STAB_TRIM", "AFTER_START", "Set the stabilizer trim from the takeoff data"),
        }
    };

    private static Group BeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            Auto("BT_LANDING_LIGHTS", "BEFORE_TAKEOFF", "Landing lights: ON", "FO_LANDING_LIGHTS_ON", On, Set(X.LandingLights, 2)),
            Auto("BT_STROBES", "BEFORE_TAKEOFF", "Strobe lights: ON", "FO_STROBES_ON", On, Set("MD11_OVHD_LTS_HI_INT_BT", 1)),
            Auto("BT_XPDR_MODE", "BEFORE_TAKEOFF", "Transponder: TA/RA", XpdrMode, v => Is(v, 3), Set(XpdrMode, 3)),
            Auto("BT_ALT_RPTG", "BEFORE_TAKEOFF", "Altitude reporting: ON", AltRptg, On, Set(AltRptg, 1)),
            Auto("BT_SPOILERS", "BEFORE_TAKEOFF", "Spoilers: ARM", "FO_SPOILERS_ARMED", On, Set(X.Spoilers, 1)),
            Auto("BT_AUTOBRAKE", "BEFORE_TAKEOFF", "Autobrake: T.O.", Autobrake, v => Is(v, 0), Set(Autobrake, 0)),
            Reminder("BT_EIS", "BEFORE_TAKEOFF", "Verify the EIS bugs, the runway, the stabilizer trim green band and the takeoff data"),
            Reminder("BT_MODES", "BEFORE_TAKEOFF", "Arm NAV and PROF, and press AUTO FLIGHT on the runway"),
        }
    };

    private static Group AfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("AT_GEAR", "AFTER_TAKEOFF", "Gear: UP", "FO_GEAR_UP", On, Set(X.Gear, 0)),
            Auto("AT_SPOILERS", "AFTER_TAKEOFF", "Spoilers: DISARM", "FO_SPOILERS_DOWN", On, Set(X.Spoilers, 0)),
            Auto("AT_AUTOBRAKE", "AFTER_TAKEOFF", "Autobrake: OFF", Autobrake, v => Is(v, 1), Set(Autobrake, 1)),
            Reminder("AT_FLAPS", "AFTER_TAKEOFF", "Flaps and slats up and retracted on schedule"),
            Reminder("AT_EAD", "AFTER_TAKEOFF", "Check the engine and alert display for alerts"),
        }
    };

    private static Group Descent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            Auto("DS_LANDING_LIGHTS", "DESCENT", "Landing lights: ON", "FO_LANDING_LIGHTS_ON", On, Set(X.LandingLights, 2)),
            Reminder("DS_QNH", "DESCENT", "Set the altimeters to local QNH"),
            Reminder("DS_AUTOBRAKE", "DESCENT", "Set the landing autobrake on the center instrument panel"),
            Reminder("DS_STERILE", "DESCENT", "Sterile cockpit chime"),
        }
    };

    private static Group BeforeLanding() => new()
    {
        Id = "BEFORE_LANDING", Name = "Before Landing",
        Items = new()
        {
            Auto("BL_GEAR", "BEFORE_LANDING", "Gear: DOWN", "FO_GEAR_DOWN", On, Set(X.Gear, 1)),
            Auto("BL_SPOILERS", "BEFORE_LANDING", "Spoilers: ARM", "FO_SPOILERS_ARMED", On, Set(X.Spoilers, 1)),
            Reminder("BL_FLAPS", "BEFORE_LANDING", "Flaps and slats as required"),
            Reminder("BL_MISSED", "BEFORE_LANDING", "Set the missed approach altitude"),
        }
    };

    private static Group AfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPOILERS", "AFTER_LANDING", "Spoilers: DOWN", "FO_SPOILERS_DOWN", On, Set(X.Spoilers, 0)),
            Auto("AL_FLAPS", "AFTER_LANDING", "Flap handle: UP", "FO_FLAPS_UP", On, Set(X.FlapHandle, 0)),
            Auto("AL_LANDING_LIGHTS", "AFTER_LANDING", "Landing lights: retracted", "FO_LANDING_LIGHTS_RETRACTED", On, Set(X.LandingLights, 0)),
            Auto("AL_STROBES", "AFTER_LANDING", "Strobe lights: OFF", "FO_STROBES_ON", Off, Set("MD11_OVHD_LTS_HI_INT_BT", 0)),
            Auto("AL_WXR", "AFTER_LANDING", "Weather radar: OFF", "FO_WXR_OFF", On, Set(X.WeatherRadarOff, 1)),
            Auto("AL_APU", "AFTER_LANDING", "APU: start", ApuState, ApuStartingOrRunning, Set(X.ApuStart, 1)),
            Reminder("AL_REVERSERS", "AFTER_LANDING", "Confirm the reversers are stowed"),
            Reminder("AL_STAB_TRIM", "AFTER_LANDING", "Set the stabilizer trim to 3 degrees nose up"),
        }
    };

    private static Group Parking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Items = new()
        {
            Auto("PK_PARK", "PARKING", "Parking brake: SET", Park, On, Set(Park, 1)),
            Auto("PK_FUEL", "PARKING", "Fuel switches: OFF", "FO_FUEL_SWITCHES_OFF", On,
                SetAll(("MD11_THR_L_FUEL_SW", 0), ("MD11_THR_C_FUEL_SW", 0), ("MD11_THR_R_FUEL_SW", 0))),
            Auto("PK_SEAT_BELTS", "PARKING", "Seat belt signs: OFF", SeatBelts, v => Is(v, 0), Set(SeatBelts, 0)),
            Auto("PK_ANTI_ICE", "PARKING", "Anti-ice: OFF", "FO_ANTI_ICE_OFF", On, Set(X.AntiIceOff, 0)),
            Auto("PK_IGNITION", "PARKING", "Engine ignition: OFF", "FO_IGNITION_OFF", On, Set(X.Ignition, 0)),
            Auto("PK_BEACON", "PARKING", "Beacon: OFF", "FO_BEACON_ON", Off, Set("MD11_OVHD_LTS_BCN_BT", 0)),
            Auto("PK_LANDING_LIGHTS", "PARKING", "Landing lights: retracted", "FO_LANDING_LIGHTS_RETRACTED", On, Set(X.LandingLights, 0)),
            Auto("PK_NOSE", "PARKING", "Nose light: OFF", Nose, v => Is(v, 0), Set(Nose, 0)),
            Auto("PK_STROBES", "PARKING", "Strobe lights: OFF", "FO_STROBES_ON", Off, Set("MD11_OVHD_LTS_HI_INT_BT", 0)),
            Auto("PK_LOGO", "PARKING", "Logo lights: OFF", "FO_LOGO_ON", Off, Set("MD11_OVHD_LTS_LOGO_BT", 0)),
            Auto("PK_TURNOFF", "PARKING", "Runway turnoff lights: OFF", "FO_RWY_TURNOFF_OFF", On,
                SetAll(("MD11_OVHD_LTS_RWY_TURNOFF_L_BT", 0), ("MD11_OVHD_LTS_RWY_TURNOFF_R_BT", 0))),
            Reminder("PK_EXT_PWR", "PARKING", "Connect ground power on the EFB Services page if required"),
        }
    };

    private static Group Shutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            Auto("SD_EMER_LTS", "SHUTDOWN", "Emergency lights: OFF", EmerLts, v => Is(v, 0), Set(EmerLts, 0)),
            Auto("SD_EMER_PWR", "SHUTDOWN", "Emergency power: OFF", EmerPwr, v => Is(v, 0), Set(EmerPwr, 0)),
            Auto("SD_WINDSHIELD", "SHUTDOWN", "Windshield anti-ice and defog: OFF", "FO_WINDSHIELD_OFF", On,
                SetAll(("MD11_OVHD_WNDSHLD_AICE_L_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_R_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", 1))),
            Auto("SD_IRS", "SHUTDOWN", "IRS switches: OFF", "FO_IRS_OFF", On,
                SetAll(("MD11_OVHD_IRS_1_KB", 0), ("MD11_OVHD_IRS_2_KB", 0), ("MD11_OVHD_IRS_3_KB", 0))),
            Auto("SD_CARGO_TEMP", "SHUTDOWN", "Cargo temperatures: OFF", "FO_CARGO_TEMPS_OFF", On,
                SetAll(("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 0), ("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 0))),
            Auto("SD_DOME", "SHUTDOWN", "Dome light: OFF", "FO_DOME_OFF", On, Set("MD11_OVHD_LTS_DOME_BT", 0)),
            Auto("SD_EVAC", "SHUTDOWN", "Evacuation switch: OFF", Evac, v => Is(v, 0), Set(Evac, 0)),
            Auto("SD_PACKS", "SHUTDOWN", "Packs: OFF", "FO_PACKS_OFF", On, Set(X.PacksOff, 0)),
            // SLOW: the APU stops up to 90 s after its bleed valve closes — hold the tick open.
            Auto("SD_APU", "SHUTDOWN", "APU: OFF", "FO_APU_OFF", On,
                SetThenWait(X.ApuShutdown, 0, "FO_APU_OFF", On, 120)),
            Auto("SD_BATTERY", "SHUTDOWN", "Battery: OFF", Batt, Off, Set(Batt, 0)),
        }
    };

    // ================================================================== TFDi readback groups (verbatim)

    private static Group CockpitEntryCL() => new()
    {
        Id = "COCKPIT_ENTRY_CL", Name = "Cockpit Entry Checklist",
        Items = new()
        {
            Read("CEC_WXR", "COCKPIT_ENTRY_CL", "Weather Radar: Off", "FO_WXR_OFF", On),
            Read("CEC_FUEL_SW", "COCKPIT_ENTRY_CL", "Fuel Switches: Off", "FO_FUEL_SWITCHES_OFF", On),
            Reminder("CEC_PARK", "COCKPIT_ENTRY_CL", "Parking Brake: Set / Chocks"),
            Read("CEC_FLAPS", "COCKPIT_ENTRY_CL", "Flap/Slats Handle: Up and Retracted", "FO_FLAPS_UP", On),
            Read("CEC_GEAR", "COCKPIT_ENTRY_CL", "Gear Handle: Down", "FO_GEAR_DOWN", On),
            Read("CEC_DUMP", "COCKPIT_ENTRY_CL", "Fuel Dump Switches: Covered and Off", "FO_FUEL_DUMP_SAFE", On),
            Read("CEC_MANF", "COCKPIT_ENTRY_CL", "Manifold Drain Switches: Covered and Off", "FO_MANF_DRAIN_SAFE", On),
            Read("CEC_EMER_PWR", "COCKPIT_ENTRY_CL", "Emergency Power Selector: Off", EmerPwr, v => Is(v, 0)),
        }
    };

    private static Group PreflightCL()
    {
        const string G = "PREFLIGHT_CL";
        return new Group
        {
            Id = G, Name = "Preflight Checklist",
            Items = new()
            {
                Read("PFC_BATT", G, "Battery Switch: On", Batt, On),
                Reminder("PFC_EXT_PWR", G, "External Power Switches: As Required"),
                Reminder("PFC_FIRE", G, "Engine/APU Fire Test: Perform"),
                Reminder("PFC_FIRE_HANDLES", G, "Engine/APU Fire Test: All 3 fire handles — Illuminates"),
                Reminder("PFC_FIRE_APU_HANDLE", G, "Engine/APU Fire Test: APU fire handle — Illuminates"),
                Reminder("PFC_FIRE_FUEL_SW", G, "Engine/APU Fire Test: All 3 Engine Fuel Shutoff Switches — Illuminates"),
                Reminder("PFC_FIRE_MW", G, "Engine/APU Fire Test: Both Master Warnings — Illuminates"),
                Reminder("PFC_FIRE_BELL", G, "Engine/APU Fire Test: Fire Bell — Sounds"),
                Reminder("PFC_FIRE_EAD", G, "Engine/APU Fire Test: Level 3 alert messages on EAD — Displayed"),
                Reminder("PFC_MW", G, "Master Warning: Press"),
                Reminder("PFC_FUEL_USED", G, "Fuel Used: Reset"),
                Reminder("PFC_ANNUN", G, "Annunciator Lights: Test/Check"),
                Reminder("PFC_BRTDIM", G, "Annunciator Bright/Dim Switch: Select"),
                Reminder("PFC_CARGO", G, "Cargo Fire: Manual Test"),
                Reminder("PFC_CARGO_HEAT", G, "Cargo Fire: FWD/AFT Heat/Smoke lights — Illuminates"),
                Reminder("PFC_CARGO_TEST_SW", G, "Cargo Fire: Manual Test switch — Illuminates"),
                Reminder("PFC_CARGO_AGENT", G, "Cargo Fire: All agent discharge lights — Illuminates"),
                Reminder("PFC_CARGO_DISAG", G, "Cargo Fire: FWD/AFT Flow Switch DISAG lights — Illuminates"),
                Reminder("PFC_CARGO_MW", G, "Cargo Fire: Both Master Warnings — Illuminates"),
                Reminder("PFC_CARGO_EAD", G, "Cargo Fire: CRG FIRE LWR FWD + CRG FIRE LWR AFT on EAD — Displayed"),
                Read("PFC_IRU", G, "IRU Switches: NAV", "FO_IRS_NAV", On),
                Reminder("PFC_IRU_INIT", G, "IRU Alignment: Initialize"),
                Reminder("PFC_CVR", G, "Cockpit Voice Recorder: Test"),
                Reminder("PFC_GALLEY", G, "Galley Bus Panel: All lights extinguished"),
                Reminder("PFC_CARGO_TEMP", G, "Cargo Temperature Selectors: As required"),
                Read("PFC_IGN_LIGHTS", G, "FADEC/Engine Ignition Panel Lights: Off Except ENG IGN OFF", "FO_IGNITION_OFF", On),
                Reminder("PFC_HYD", G, "Hydraulic Test: Perform"),
                Reminder("PFC_HYD_SD", G, "Hydraulic Test: Observe on SD — Normal"),
                Reminder("PFC_ELEC", G, "Electrical Panel: Check"),
                Read("PFC_ELEC_MAN", G, "Electrical Panel: Manual Light — Off", ElecSel, Off),
                Read("PFC_SMOKE", G, "Smoke Elec/Air Source: Normal", "MD11_OVHD_ELEC_SMOKE_ELEC_AIR_KB", v => Is(v, 0)),
                Reminder("PFC_DRIVES", G, "Drive 1/2/3 + CAB BUS: Guarded"),
                Read("PFC_EMER_PWR", G, "Emergency Power Selector: Armed, No Light", EmerPwr, v => Is(v, 1)),
                Reminder("PFC_AIR", G, "Air Panel: Verify"),
                Read("PFC_AIR_MAN", G, "Air Panel: Manual Light — Off", AirSel, Off),
                Read("PFC_AIR_ECON", G, "Air Panel: ECON Light — Off", "FO_ECON_ON", On),
                Reminder("PFC_AIR_TRIM", G, "Air Panel: TRIM AIR OFF Light — Off"),
                Reminder("PFC_OUTFLOW", G, "Cabin Outflow Valve: Open"),
                Reminder("PFC_AIRCOND", G, "Air Conditioning: Establish"),
                Reminder("PFC_TEMPS", G, "Temperature Selectors: As required"),
                Read("PFC_FUEL_MAN", G, "Fuel Panel Manual Light: Off", FuelSel, Off),
                Reminder("PFC_FQ", G, "Fuel Quantity: Test"),
                Reminder("PFC_FQ_TOTAL", G, "Fuel Quantity: 188880 Displayed — Checked"),
                Reminder("PFC_FQ_TANKS", G, "Fuel Quantity: 10500 Displayed in each tank — Checked"),
                Read("PFC_EMER_LTS", G, "Emergency Light Switch: Arm", EmerLts, v => Is(v, 1)),
                Read("PFC_NO_SMOKE", G, "No Smoking Switch: On", NoSmoke, v => Is(v, 2)),
                Read("PFC_SEAT_BELTS", G, "Seat Belt Switch: Off", SeatBelts, v => Is(v, 0)),
                Reminder("PFC_EXT_LTS", G, "Exterior Lights: As required"),
                Read("PFC_EVAC", G, "EVAC Panel: Armed and Guarded", "FO_EVAC_ARMED_GUARDED", On),
                Read("PFC_GPWS", G, "GPWS Test Switch: Test and Guarded", "FO_GPWS_NORMAL_GUARDED", On),
                Reminder("PFC_AFS", G, "AFS Panel: Check"),
                Reminder("PFC_AFS_LIGHTS", G, "AFS Panel: All lights — Off"),
                Read("PFC_AFS_FLAPLIM", G, "AFS Panel: Flap limit — Normal", "MD11_OVHD_FLTCTL_FLAPLIM_KB", v => Is(v, 2)),
                Reminder("PFC_AFS_FEEL", G, "AFS Panel: Elevator feel — Normal"),
                Reminder("PFC_PRESS", G, "Cabin Pressurization Panel: Check"),
                Read("PFC_PRESS_AUTO", G, "Cabin Pressurization Panel: In AUTO operation — Checked", CabinSel, Off),
                Reminder("PFC_PRESS_VALVE", G, "Cabin Pressurization Panel: Valve — Open"),
                Reminder("PFC_DITCH_SW", G, "Cabin Pressurization Panel: Ditching switch — Guarded"),
                Reminder("PFC_DITCH_LT", G, "Cabin Pressurization Panel: Ditching light — Extinguished"),
                Reminder("PFC_AICE", G, "Anti-Ice Panel: Check"),
                Read("PFC_AICE_LIGHTS", G, "Anti-Ice Panel: All lights — Off", "FO_ANTI_ICE_OFF", On),
                Read("PFC_WINDSHIELD", G, "Windshield Anti-Ice/Defog: NORM/ON/DEFOG Light Extinguished", "FO_WINDSHIELD_ON", On),
                Reminder("PFC_ALT", G, "Altimeter: Set QNH"),
                Reminder("PFC_IAS", G, "IAS: Auto"),
                Reminder("PFC_HDG", G, "HDG: Auto"),
                Reminder("PFC_FEET", G, "FEET: Auto"),
                Reminder("PFC_BANK", G, "Bank Selector: Auto"),
                Reminder("PFC_STATIC", G, "Static Air Switch: Normal"),
                Reminder("PFC_SOURCE", G, "Source Input Selector Lights: Off"),
                Reminder("PFC_DISPLAYS", G, "PDF/ND/EAD/SD: Check"),
                Reminder("PFC_DISP_FAULTS", G, "PDF/ND/EAD/SD: No faults — Checked"),
                Reminder("PFC_DISP_ALTS", G, "PDF/ND/EAD/SD: Altimeters — As desired"),
                Reminder("PFC_DISP_TIME", G, "PDF/ND/EAD/SD: Time — Correct"),
                Reminder("PFC_DISP_OIL", G, "PDF/ND/EAD/SD: Oil Quantity — >16 quarts"),
                Reminder("PFC_DISP_FMA", G, "PDF/ND/EAD/SD: PDF FMA annunciator — TAKEOFF"),
                Read("PFC_GEAR", G, "Gear Handle: Down, 4 green", "FO_GEAR_DOWN", On),
                Read("PFC_AUTOBRAKE", G, "Auto Brake Switch: RTO", Autobrake, v => Is(v, 0)),
                Reminder("PFC_OVERBOOST", G, "Overboost Breakout bar: Full AFT"),
                Reminder("PFC_THROTTLES", G, "Throttles: Check"),
                Reminder("PFC_THROTTLES_TRAVEL", G, "Throttles: Travel and aural warnings — Checked"),
                Reminder("PFC_THROTTLES_CLOSED", G, "Throttles: Closed"),
                Reminder("PFC_REVERSERS", G, "Reverse Levers: Down"),
                Read("PFC_DAF", G, "Dial-a-Flap: To Setting", "FO_DAF_TAKEOFF", On),
                Read("PFC_START_IN", G, "Engine Start Buttons: Pressed In", "FO_START_SWITCHES_IN", On),
                Read("PFC_FUEL_SW", G, "Fuel Switches: Off", "FO_FUEL_SWITCHES_OFF", On),
                Reminder("PFC_SDCP", G, "SDCP: Cue Lights and Clear"),
                Reminder("PFC_RADIOS", G, "Radio Panels: As desired"),
                Read("PFC_WXR", G, "Weather Radar: Test and Off", "FO_WXR_OFF", On),
                Reminder("PFC_XPDR", G, "Transponder: Set"),
                Reminder("PFC_RUDDER_TRIM", G, "Rudder Trim: Zero"),
                Reminder("PFC_AILERON_TRIM", G, "Aileron Trim: Zero"),
                Reminder("PFC_ADG", G, "Air Driven Generator (ADG): Handle Down and Wired"),
            }
        };
    }

    private static Group BeforeStartCL()
    {
        const string G = "BEFORE_START_CL";
        return new Group
        {
            Id = G, Name = "Before Start Checklist",
            Items = new()
            {
                Read("BSC_APU", G, "APU: Start", ApuState, ApuStartingOrRunning),
                Reminder("BSC_FMS", G, "FMS: Initialized and Checked"),
                Reminder("BSC_FMS_WEIGHTS", G, "FMS: Weights — Confirmed"),
                Reminder("BSC_FMS_WIND", G, "FMS: Headwind/Tailwind — Confirmed"),
                Reminder("BSC_FMS_SLOPE", G, "FMS: Runway Slope — Confirmed"),
                Reminder("BSC_FMS_TEMP", G, "FMS: Temperature — Confirmed"),
                Reminder("BSC_FMS_VSPEEDS", G, "FMS: V-Speeds — Confirmed"),
                Read("BSC_IRS", G, "IRS: Nav and Aligned", "FO_IRS_ALIGNED", On),
                Reminder("BSC_EIS", G, "EIS/BUGS: Set"),
                Read("BSC_SEAT_BELTS", G, "Seat Belt Sign: On", SeatBelts, v => Is(v, 2)),
                Read("BSC_EXT_PWR", G, "External Power Switches: Off", "FO_EXT_POWER_ON", Off),
                Read("BSC_AUX_PUMP", G, "AUX HYD Pump 1: On", "FO_AUX_PUMP_1_ON", On),
                Read("BSC_IGNITION", G, "Engine Ignition: A or B", "FO_IGNITION_SELECTED", On),
                Read("BSC_BEACON", G, "Beacon Light: On", "FO_BEACON_ON", On),
            }
        };
    }

    private static Group EngineStartCL()
    {
        const string G = "ENGINE_START_CL";
        return new Group
        {
            Id = G, Name = "Engine Start Checklist",
            Items = new()
            {
                Read("ESC_E3_START", G, "Engine 3 Start Switch: Pull", "FO_ENG3_N2", v => v >= Md11FoStateEvaluator.EngineRunningN2),
                Read("ESC_E3_FUEL", G, "Fuel Level: On", "MD11_THR_R_FUEL_SW", On),
                Read("ESC_E3_N2_15", G, "Wait until N2 >= 15%: Confirmed", "FO_ENG3_N2", v => v >= 15),
                Reminder("ESC_OIL", G, "Oil Pressure: Verify Rising"),
                Reminder("ESC_LIGHTOFF", G, "Engine Ignition: Verify <= 25 Seconds"),
                Reminder("ESC_EGT", G, "EGT: Check"),
                Read("ESC_CUTOUT", G, "Start Valve: Verify Closed at 45% N2", "FO_ENG3_START_CUTOUT", On),
                Read("ESC_E3_STABLE", G, "Engine 3: Verify Stabilized", "FO_ENG3_N2", v => v >= 55),
                Read("ESC_E3_N1", G, "Engine 3: N1 — Around 20%", "MD11_ENG3_N1", v => v >= 15),
                Reminder("ESC_E3_EGT", G, "Engine 3: EGT — Around 400C"),
                Read("ESC_E3_N2", G, "Engine 3: N2 — Around 60%", "FO_ENG3_N2", v => v >= 55),
                Reminder("ESC_E3_OIL", G, "Engine 3: Oil — +/- 2 quarts after start"),
                Reminder("ESC_E3_FF", G, "Engine 3: Fuel Flow — Around 1200 at sea level"),
                Reminder("ESC_ANTI_ICE", G, "Engine Anti-Ice: As required"),
                Read("ESC_E1", G, "Repeat for Engine 1: Checked", "FO_ENG1_N2", v => v >= Md11FoStateEvaluator.EngineRunningN2),
                Read("ESC_E2", G, "Repeat for Engine 2: Checked", "FO_ENG2_N2", v => v >= Md11FoStateEvaluator.EngineRunningN2),
            }
        };
    }

    private static Group AfterStartCL()
    {
        const string G = "AFTER_START_CL";
        return new Group
        {
            Id = G, Name = "After Start Checklist",
            Items = new()
            {
                Reminder("ASC_ANTI_ICE", G, "Engine Anti-Ice: As required"),
                Read("ASC_APU", G, "APU: Off", "FO_APU_OFF", On),
                Read("ASC_FLAPS", G, "Flaps: Set", "FO_FLAPS_DAF", On),
                Reminder("ASC_CONFIG", G, "Config Page: Select"),
                Reminder("ASC_FLT_CTRL", G, "Flight Controls: Check"),
                Reminder("ASC_STAB_TRIM", G, "Stab Trim: Set"),
                Read("ASC_TAXI_LIGHT", G, "Taxi Lights: On", Nose, v => Is(v, 1)),
            }
        };
    }

    private static Group BeforeTakeoffCL()
    {
        const string G = "BEFORE_TAKEOFF_CL";
        return new Group
        {
            Id = G, Name = "Before Takeoff Checklist",
            Items = new()
            {
                Reminder("BTC_EIS", G, "EIS/BUGS: Verify"),
                Reminder("BTC_RUNWAY", G, "Runway: Verify"),
                Read("BTC_ANTI_ICE", G, "Anti-Ice: Wings Off, Engine as required", "FO_WING_ANTI_ICE_OFF", On),
                Reminder("BTC_STAB_TRIM", G, "Stab Trim: Verify Green Band and Config"),
                Read("BTC_SPOILERS", G, "Spoilers: Armed", "FO_SPOILERS_ARMED", On),
                Read("BTC_AUTOBRAKE", G, "Autobrake: RTO", Autobrake, v => Is(v, 0)),
                Read("BTC_FLAPS", G, "Flaps and Slats: Set for takeoff", "FO_FLAPS_DAF", On),
                Reminder("BTC_TO_DATA", G, "Takeoff Data and Bugs: Verify"),
                Reminder("BTC_EAD", G, "EAD: Checked"),
                Read("BTC_LANDING_LIGHTS", G, "Landing Lights: On", "FO_LANDING_LIGHTS_ON", On),
                Read("BTC_STROBES", G, "High Intensity Lights (Strobes): On", "FO_STROBES_ON", On),
                Reminder("BTC_MODES", G, "Flight Modes: As required"),
                Reminder("BTC_NAV", G, "Flight Modes: NAV — Armed"),
                Reminder("BTC_PROF", G, "Flight Modes: PROF — Armed"),
                // AUTO FLIGHT engages the autopilot: MD11_AP_STATE 0 off, 1 AP 1, 2 AP 2, 3 both.
                Read("BTC_AUTOFLIGHT", G, "Flight Modes: AUTOFLIGHT — On", Md11AutopilotEngage.ApStateKey, On),
                Reminder("BTC_TOGA", G, "Flight Modes: TOGA power — Set"),
            }
        };
    }

    private static Group AfterTakeoffCL()
    {
        const string G = "AFTER_TAKEOFF_CL";
        return new Group
        {
            Id = G, Name = "After Takeoff Checklist",
            Items = new()
            {
                Read("ATC_GEAR", G, "Gear: Up", "FO_GEAR_UP", On),
                Read("ATC_SPOILERS", G, "Spoilers: Disarm", "FO_SPOILERS_DOWN", On),
                Read("ATC_AUTOBRAKE", G, "Autobrake: Verify", Autobrake, v => Is(v, 1)),
                Read("ATC_FLAPS", G, "Flaps/Slats: Up and Retracted", "FO_FLAPS_UP", On),
                Reminder("ATC_EAD", G, "EAD: Check No Alerts"),
            }
        };
    }

    private static Group Passing10000CL() => new()
    {
        Id = "PASSING_10000_CL", Name = "Passing 10,000 Feet Checklist",
        Items = new()
        {
            Reminder("P10_STERILE", "PASSING_10000_CL", "Sterile Cockpit: Chime"),
            Read("P10_LANDING_LIGHTS", "PASSING_10000_CL", "Landing Lights: Off", "FO_LANDING_LIGHTS_OFF", On),
        }
    };

    private static Group TransitionAltitudeCL() => new()
    {
        Id = "TRANSITION_ALTITUDE_CL", Name = "Passing Transition Altitude Checklist",
        Items = new()
        {
            Read("TAC_STD", "TRANSITION_ALTITUDE_CL", "Altimeters: STD (Pull)", "FO_ALTIMETERS_STD", On),
            Read("TAC_DAF", "TRANSITION_ALTITUDE_CL", "Dial-a-Flap: Set 15", "FO_DAF_15", On),
        }
    };

    private static Group CruiseCL() => new()
    {
        Id = "CRUISE_CL", Name = "Cruise Checklist",
        Items = new()
        {
            Reminder("CRC_FMA", "CRUISE_CL", "FMA: Verify"),
            Reminder("CRC_FMS", "CRUISE_CL", "FMS: Setup for descent"),
        }
    };

    private static Group TransitionLevelCL() => new()
    {
        Id = "TRANSITION_LEVEL_CL", Name = "Passing Below Transition Level Checklist",
        Items = new()
        {
            Reminder("TLC_QNH", "TRANSITION_LEVEL_CL", "Altimeters: Local QNH"),
        }
    };

    private static Group DescentCL() => new()
    {
        Id = "DESCENT_CL", Name = "Descending Below 10,000 Feet Checklist",
        Items = new()
        {
            Read("DSC_LANDING_LIGHTS", "DESCENT_CL", "Landing Lights: On", "FO_LANDING_LIGHTS_ON", On),
            Reminder("DSC_STERILE", "DESCENT_CL", "Sterile Cockpit: Chime"),
            Reminder("DSC_AUTOBRAKE", "DESCENT_CL", "Autobrakes: As required"),
        }
    };

    private static Group BeforeLandingCL() => new()
    {
        Id = "BEFORE_LANDING_CL", Name = "Before Landing Checklist",
        Items = new()
        {
            Read("BLC_GEAR", "BEFORE_LANDING_CL", "Gear: Down", "FO_GEAR_DOWN", On),
            Reminder("BLC_FLAPS", "BEFORE_LANDING_CL", "Flaps/Slats: As required"),
            Read("BLC_SPOILERS", "BEFORE_LANDING_CL", "Spoilers: Armed", "FO_SPOILERS_ARMED", On),
            Reminder("BLC_MISSED", "BEFORE_LANDING_CL", "Missed Approach Altitude: Set"),
        }
    };

    private static Group AfterLandingCL()
    {
        const string G = "AFTER_LANDING_CL";
        return new Group
        {
            Id = G, Name = "After Landing Checklist",
            Items = new()
            {
                Reminder("ALC_REVERSERS", G, "Reversers: Stowed"),
                Read("ALC_LANDING_LIGHTS", G, "Landing Lights: Off", "FO_LANDING_LIGHTS_OFF", On),
                Read("ALC_STROBES", G, "High Intensity Lights (Strobes): Off", "FO_STROBES_ON", Off),
                Read("ALC_FLAPS", G, "Flaps/Slats: Up and Retracted", "FO_FLAPS_UP", On),
                Read("ALC_SPOILERS", G, "Spoilers: Down", "FO_SPOILERS_DOWN", On),
                Reminder("ALC_STAB_TRIM", G, "Stabilizer Trim: Set 3 Degrees Up"),
                Read("ALC_WXR", G, "Weather Radar: Off", "FO_WXR_OFF", On),
                Read("ALC_APU", G, "APU: Start", ApuState, ApuStartingOrRunning),
            }
        };
    }

    private static Group ParkingCL()
    {
        const string G = "PARKING_CL";
        return new Group
        {
            Id = G, Name = "Parking Checklist",
            Items = new()
            {
                Read("PKC_PARK", G, "Parking Brakes: Set", Park, On),
                Read("PKC_FUEL", G, "Fuel Switches: Off", "FO_FUEL_SWITCHES_OFF", On),
                Read("PKC_SEAT_BELTS", G, "Seat Belt Sign: Off", SeatBelts, v => Is(v, 0)),
                Reminder("PKC_EXT_PWR", G, "External Power: As required"),
                Reminder("PKC_APU", G, "APU: As required"),
                Read("PKC_ANTI_ICE", G, "Anti-Ice: Off", "FO_ANTI_ICE_OFF", On),
                Read("PKC_IGNITION", G, "Engine Ignition: Off", "FO_IGNITION_OFF", On),
                Read("PKC_EXT_LIGHTS", G, "Exterior Lights: All off, except NAV", "FO_EXTERIOR_PARKED", On),
            }
        };
    }

    private static Group ShutdownCL()
    {
        const string G = "SHUTDOWN_CL";
        return new Group
        {
            Id = G, Name = "Shutdown Checklist",
            Items = new()
            {
                Read("SDC_EMER_LTS", G, "Emergency Light Switch: Off", EmerLts, v => Is(v, 0)),
                Read("SDC_EMER_PWR", G, "Emergency Power Switch: Off", EmerPwr, v => Is(v, 0)),
                Read("SDC_WINDSHIELD", G, "Windshield Anti-Ice and Defog: Off", "FO_WINDSHIELD_OFF", On),
                Read("SDC_IRS", G, "IRS Switches: Off", "FO_IRS_OFF", On),
                Read("SDC_CARGO_TEMP", G, "Cargo Temperatures: Off", "FO_CARGO_TEMPS_OFF", On),
                Read("SDC_COCKPIT_LTS", G, "Cockpit Lights: Off", "FO_DOME_OFF", On),
                Read("SDC_EVAC", G, "EVAC Control Switch: Off", Evac, v => Is(v, 0)),
                Read("SDC_PACKS", G, "Packs: Off", "FO_PACKS_OFF", On),
                Read("SDC_APU", G, "APU: Off", "FO_APU_OFF", On),
                Read("SDC_BATTERY", G, "Battery: Off", Batt, Off),
            }
        };
    }

    // ================================================================== builders

    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn? action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        AdditionalStateFields = Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    /// <summary>A readback (*_CL) item: auto-detected, action-free.</summary>
    private static Item Read(string id, string groupId, string label, string field, Func<double, bool> condition)
        => Auto(id, groupId, label, field, condition, action: null);

    /// <summary>The ONE sanctioned StayComplete: the engine-start switches, latched on the engine running.</summary>
    private static Item AutoLatch(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.StayComplete,
        AdditionalStateFields = Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };
}
