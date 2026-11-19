using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.MD11.Md11FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.MD11;

using Flow = Models.FlowDefinition<Md11FoStateEvaluator>;
using Step = Models.FlowStep<Md11FoStateEvaluator>;

/// <summary>
/// The TFDi MD-11 First Officer flows, following TFDi's published normal checklist and quick-start
/// guide (docs/superpowers/specs/md11-fo-research). Every switch step carries a SkipCondition on
/// its own state ("Already set"), and the executor re-reads before acting anyway. Captain steps
/// are spoken reminders, never automated. The First Officer never moves the flap handle in flight,
/// never sets a landing autobrake, never moves thrust levers or trims, and never programs the FMS.
///
/// Every read-back (*_CL) line a flow latches has a delivering step (FO-6, FO-7): a write
/// completes its own action line and the read-back line it sets (<see cref="Also"/>), and a
/// read-only <see cref="Check"/> completes a line no write of that flow sets. Until 2026-10-18 the
/// read-back lines had no step behind them, so a finished flow ticked and latched 74 of them over
/// whatever the aircraft showed ("Spoilers: Armed" over a refused arm, "Emergency Power Selector:
/// Off" right after the flow armed it). Pinned by Md11FoFlowChecklistLinkTests.
/// </summary>
public static class Md11FoFlowDefinitions
{
    // Frequently used keys
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
    private const string FuelL = "MD11_THR_L_FUEL_SW", FuelC = "MD11_THR_C_FUEL_SW", FuelR = "MD11_THR_R_FUEL_SW";
    private const string Irs1 = "MD11_OVHD_IRS_1_KB", Irs2 = "MD11_OVHD_IRS_2_KB", Irs3 = "MD11_OVHD_IRS_3_KB";

    public static List<Flow> Build() => new()
    {
        BuildPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildBeforeLanding(),
        BuildAfterLanding(),
        BuildParking(),
        BuildShutdown(),
    };

    /// <summary>
    /// Before Start's and After Landing's "Already set" for the APU: starting, or running WITH its power on. Running
    /// with its power off (started from the aft-overhead APU START button, or external power
    /// connected over it) is the executor's to finish: it requests APU power, or the flow would
    /// wait for APU power and stop. A start from that button still running when the step reads
    /// it gets its power from a later step once it runs: Before Start's BS_APU_PWR, Parking's
    /// PK_APU_PWR.
    /// </summary>
    private static bool ApuStartingOrPowering(Md11FoStateEvaluator s)
        => s.GetValue("MD11_APU_STATE") is var v && (v > 0.5 && v < 1.5 || v >= 1.5 && v < 2.5 && s.IsOn("FO_APU_POWER_ON"));

    /// <summary>Parking's APU power step runs (else it is silent, FO-20): no power source reads on
    /// yet, and the APU is starting or running.</summary>
    internal static bool ApuOwedItsPower(Md11FoStateEvaluator s)
        => !s.IsOn("FO_APU_OR_EXT_POWER_ON") && s.GetValue("MD11_APU_STATE") is var v && v > 0.5 && v < 2.5;

    // ------------------------------------------------------------------ Power Up (TFDi Cockpit Entry + quick-start power-up)
    private static Flow BuildPowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Description = "Weather radar and fuel switches off, parking brake, battery, external power, emergency power armed, IRS to NAV, nav lights.",
        RelatedChecklistGroupIds = new[] { "POWER_UP", "COCKPIT_ENTRY_CL" },
        Steps = new()
        {
            // A cold-and-dark flow: started by mistake with engines turning, the next step would
            // cut all three fuel switches (seen live). Stop instead.
            EnginesStoppedGuard("PU_ENGINES_STOPPED"),
            // ...and never airborne (FO-25), as the PU_FUEL_OFF tick requires: all three fuel switches
            // off in flight is a triple flame-out. Links no line (the 777's EPU_ON_GROUND).
            OnGroundGuard("PU_ON_GROUND"),
            // TFDi's Cockpit Entry checks the First Officer finds and never moves: read only. The
            // emergency power selector is checked OFF before the flow arms it below.
            Check("PU_CHK_FLAPS", "Flap handle: UP", "FO_FLAPS_UP", On, CheckSeconds, "CEC_FLAPS"),
            Check("PU_CHK_GEAR", "Gear handle: DOWN", "FO_GEAR_DOWN", On, CheckSeconds, "CEC_GEAR"),
            Check("PU_CHK_DUMP", "Fuel dump switches: covered and off", "FO_FUEL_DUMP_SAFE", On, CheckSeconds, "CEC_DUMP"),
            Check("PU_CHK_MANF", "Manifold drain switches: covered and off", "FO_MANF_DRAIN_SAFE", On, CheckSeconds, "CEC_MANF"),
            Check("PU_CHK_EMER_PWR", "Emergency power selector: OFF", EmerPwr, v => Md11FoSwitching.AtPosition(v, 0),
                CheckSeconds, "CEC_EMER_PWR"),
            Also(Done(Skip(Multi("PU_FUEL_OFF", "Fuel switches: OFF", (FuelL, 0), (FuelC, 0), (FuelR, 0)),
                s => s.IsOn("FO_FUEL_SWITCHES_OFF")), "PU_FUEL_OFF"), "CEC_FUEL_SW"),
            Also(Done(Skip(SW("PU_PARK", "Parking brake: SET", Park, 1), s => s.IsOn(Park)), "PU_PARK"), "CEC_PARK"),
            Done(Skip(SW("PU_BATT", "Battery: ON", Batt, 1), s => s.IsOn(Batt)), "PU_BATT"),
            Done(Skip(SW("PU_EXT_PWR", "External power: ON", X.ExtPower, 1), s => s.IsOn("FO_EXT_POWER_ON")), "PU_EXT_PWR"),
            // A skipped step is announced "Already set: <label>", so the label is a short noun
            // phrase and the instruction lives in the reminder text (the PMDG 737 convention).
            Skip(Captain("PU_EXT_PWR_CPT", "Ground power",
                    "Connect ground power on the EFB Services page, or start the APU."),
                s => s.IsOn("FO_EXT_POWER_ON")),
            Also(Done(Skip(SW("PU_WXR_OFF", "Weather radar: OFF", X.WeatherRadarOff, 1), s => s.IsOn("FO_WXR_OFF")), "PU_WXR_OFF"),
                "CEC_WXR"),
            Done(Skip(SW("PU_EMER_PWR", "Emergency power: ARMED", EmerPwr, 1), s => s.IsPosition(EmerPwr, 1)), "PU_EMER_PWR"),
            Done(Skip(Multi("PU_IRS", "IRS 1, 2 and auxiliary: NAV", (Irs1, 1), (Irs2, 1), (Irs3, 1)),
                s => s.IsOn("FO_IRS_NAV")), "PU_IRS"),
            Done(Skip(SW("PU_NAV", "Navigation lights: ON", "MD11_OVHD_LTS_NAV_BT", 1), s => s.IsOn("FO_NAV_ON")), "PU_NAV"),
            Captain("PU_IRS_INIT", "Initialize the IRS on the MCDU F-PLN INIT page. Alignment takes about ten minutes."),
        }
    };

    // ------------------------------------------------------------------ Preflight (every system test)
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Every system test, systems to AUTO, emergency lights, signs, EVAC armed, windshield heat, RTO, Dial-A-Flap.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT", "PREFLIGHT_CL" },
        Steps = new()
        {
            // What Power Up left set, read only (TFDi's Preflight card opens with these).
            Check("PF_CHK_BATT", "Battery: ON", Batt, On, CheckSeconds, "PFC_BATT"),
            Check("PF_CHK_IRS", "IRS switches: NAV", "FO_IRS_NAV", On, CheckSeconds, "PFC_IRU"),
            Check("PF_CHK_IGNITION", "Engine ignition: OFF", "FO_IGNITION_OFF", On, CheckSeconds, "PFC_IGNITION"),
            Check("PF_CHK_START_IN", "Engine start switches: IN", "FO_START_SWITCHES_IN", On, CheckSeconds, "PFC_START_IN"),
            Done(SW("PF_FIRE_TEST", "Engine and APU fire test", "MD11_AOVHD_FIRETEST_BT", 1), "PF_FIRE_TEST"),
            Done(SW("PF_FUEL_USED", "Fuel used: reset", "MD11_OVHD_FUELUSEDRESET_BT", 1), "PF_FUEL_USED"),
            Done(SW("PF_ANNUN_TEST", "Annunciator light test", X.AnnunciatorTest, 1), "PF_ANNUN_TEST"),
            Done(SW("PF_CARGO_FIRE_TEST", "Cargo fire manual test", "MD11_AOVHD_CRGSMK_TEST_BT", 1), "PF_CARGO_FIRE_TEST"),
            // Both fire tests light the master warnings: ONE reset, after the later test (owner
            // decision, TFDi's order kept), the same line the Preflight checklist carries.
            Done(SW("PF_MW_RESET", "Master warning: reset", "MD11_GSL_MST_WRN_BT", 1), "PF_MW_RESET"),
            Done(SW("PF_CVR_TEST", "Cockpit voice recorder test", "MD11_OVHD_CVR_TEST_BT", 1), "PF_CVR_TEST"),
            Also(Done(Skip(SW("PF_HYD_AUTO", "Hydraulic system: AUTO", HydSel, 0), s => s.IsPosition(HydSel, 0)), "PF_HYD_AUTO"),
                "PFC_HYD"),
            Done(SW("PF_HYD_TEST", "Hydraulic test: start", X.HydraulicTest, 1), "PF_HYD_TEST"),
            Also(Done(Skip(SW("PF_ELEC_AUTO", "Electrical system: AUTO", ElecSel, 0), s => s.IsPosition(ElecSel, 0)), "PF_ELEC_AUTO"),
                "PFC_ELEC"),
            Also(Done(Skip(SW("PF_EMER_PWR", "Emergency power: ARMED", EmerPwr, 1), s => s.IsPosition(EmerPwr, 1)), "PF_EMER_PWR"),
                "PFC_EMER_PWR"),
            Also(Done(Skip(SW("PF_AIR_AUTO", "Air system: AUTO", AirSel, 0), s => s.IsPosition(AirSel, 0)), "PF_AIR_AUTO"),
                "PFC_AIR"),
            Also(Done(Skip(SW("PF_ECON", "Economy: ON", "MD11_OVHD_PNEU_ECON_BT", 1), s => s.IsOn("FO_ECON_ON")), "PF_ECON"),
                "PFC_ECON"),
            Also(Done(Skip(SW("PF_FUEL_AUTO", "Fuel system: AUTO", FuelSel, 0), s => s.IsPosition(FuelSel, 0)), "PF_FUEL_AUTO"),
                "PFC_FUEL"),
            Also(Done(Skip(SW("PF_CABIN_AUTO", "Cabin pressure: AUTO", CabinSel, 0), s => s.IsPosition(CabinSel, 0)), "PF_CABIN_AUTO"),
                "PFC_PRESS"),
            Done(SW("PF_FUEL_QTY_TEST", "Fuel quantity test", "MD11_OVHD_FUEL_QTY_TEST_BT", 1), "PF_FUEL_QTY_TEST"),
            Done(SW("PF_EMER_LT_TEST", "Emergency lights test", "MD11_OVHD_LTS_EMER_TEST_BT", 1), "PF_EMER_LT_TEST"),
            Also(Done(Skip(SW("PF_EMER_LTS", "Emergency lights: ARMED", EmerLts, 1), s => s.IsPosition(EmerLts, 1)), "PF_EMER_LTS"),
                "PFC_EMER_LTS"),
            Also(Done(Skip(SW("PF_NO_SMOKE", "No smoking signs: ON", NoSmoke, 2), s => s.IsPosition(NoSmoke, 2)), "PF_NO_SMOKE"),
                "PFC_NO_SMOKE"),
            Also(Done(Skip(SW("PF_SEAT_BELTS", "Seat belt signs: OFF", SeatBelts, 0), s => s.IsPosition(SeatBelts, 0)), "PF_SEAT_BELTS"),
                "PFC_SEAT_BELTS"),
            Done(Skip(SW("PF_EVAC", "Evacuation switch: ARMED", Evac, 1), s => s.IsPosition(Evac, 1)), "PF_EVAC"),
            // "Armed and Guarded": the switch write leaves the guard to the aircraft, so it is read.
            Check("PF_CHK_EVAC", "Evacuation switch: armed and guarded", "FO_EVAC_ARMED_GUARDED", On, CheckSeconds, "PFC_EVAC"),
            Done(Stop(SW("PF_GPWS_TEST", "GPWS test, and back to normal", X.GpwsTest, 1)), "PF_GPWS_TEST"),
            Check("PF_CHK_GPWS", "GPWS: normal and guarded", "FO_GPWS_NORMAL_GUARDED", On, CheckSeconds, "PFC_GPWS"),
            Also(Done(Skip(Multi("PF_WINDSHIELD", "Windshield anti-ice: ON, normal, defog on",
                    ("MD11_OVHD_WNDSHLD_AICE_L_BT", 1), ("MD11_OVHD_WNDSHLD_AICE_R_BT", 1),
                    ("MD11_OVHD_WNDSHLD_AICE_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", 0)),
                s => s.IsOn("FO_WINDSHIELD_ON")), "PF_WINDSHIELD"), "PFC_WINDSHIELD"),
            Also(Done(Skip(SW("PF_AUTOBRAKE", "Autobrake: T.O.", Autobrake, 0), s => s.IsPosition(Autobrake, 0)), "PF_AUTOBRAKE"),
                "PFC_AUTOBRAKE"),
            // No SimBrief plan: the provider returns 0, which the executor refuses — the step FAILS
            // ("Skipping"), so the item is never falsely ticked, and the Captain step below speaks.
            Also(Done(Skip(Provider("PF_DAF", "Dial-A-Flap: takeoff setting", X.DialAFlap, s => s.TakeoffDialAFlap ?? 0),
                s => s.IsOn("FO_DAF_TAKEOFF")), "PF_DAF"), "PFC_DAF"),
            // The Captain is asked unless the wheel already reads the plan's takeoff setting: without
            // a plan the step above fails, and a write that did not take leaves the wheel where it
            // was. Keyed on the WHEEL, never on the plan (the A320 family's takeoff-flaps rule): keyed
            // on the plan, a failed write was heard as "Skipping" then a false "Already set", and the
            // reminder was lost. No plan to compare with, or an unread wheel (NaN), is not set. The
            // words hold either way: they never say whether a plan is loaded.
            Skip(Captain("PF_DAF_CPT", "Dial-A-Flap takeoff setting",
                    "Set the Dial-A-Flap to the takeoff flap setting."),
                s => s.IsOn("FO_DAF_TAKEOFF")),
            Done(SW("PF_WXR_TEST", "Weather radar test, then off", X.WeatherRadarTest, 1), "PF_WXR_TEST"),
            Done(SW("PF_OXY_CAPT", "Captain oxygen test", "MD11_LSIDE_OXY_TEST_BT", 1), "PF_OXY_CAPT"),
            Done(SW("PF_OXY_FO", "First officer oxygen test", "MD11_RSIDE_OXY_TEST_BT", 1), "PF_OXY_FO"),
            Done(SW("PF_ISFD_TEST", "Standby display test", "MD11_MIP_ISFD_TEST_BT", 1), "PF_ISFD_TEST"),
            Done(SW("PF_TCAS_TEST", "TCAS test", "MD11_PED_XPNDR_TEST_BT", 1), "PF_TCAS_TEST"),
            Done(SW("PF_CARGO_DOOR_TEST", "Cargo door test", "MD11_OVHD_CRG_DOOR_TEST_BT", 1), "PF_CARGO_DOOR_TEST"),
            Captain("PF_CPT_ALTIMETERS", "Set the altimeters to local QNH"),
            Captain("PF_CPT_THROTTLES", "Check throttle travel and the aural warnings, then close the throttles"),
            Captain("PF_CPT_RADIOS", "Set the radio panels and the transponder code"),
            Captain("PF_CPT_TRIMS", "Check rudder and aileron trim at zero"),
            Captain("PF_CPT_DISPLAYS", "Check the displays for faults, and clear the system display cue lights"),
            WaitForField("PF_HYD_TEST_DONE", "Hydraulic test to finish", "FO_HYD_TEST_RUNNING", v => v < 0.5, 120),
        }
    };

    // ------------------------------------------------------------------ Before Start
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "APU start, seat belts, external power off, auxiliary hydraulic pump 1, ignition A, APU bleed, beacon.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START", "BEFORE_START_CL" },
        Steps = new()
        {
            Done(Skip(SW("BS_APU", "APU: start", X.ApuStart, 1), ApuStartingOrPowering), "BS_APU"),
            Done(WaitForField("BS_APU_RUNNING", "APU running", "MD11_APU_STATE", v => v > 1.5 && v < 2.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop), "BSC_APU"),
            Also(Done(Skip(SW("BS_SEAT_BELTS", "Seat belt signs: ON", SeatBelts, 2), s => s.IsPosition(SeatBelts, 2)), "BS_SEAT_BELTS"),
                "BSC_SEAT_BELTS"),
            // An APU started from the aft-overhead APU START button runs with its power off (AVAIL
            // lit, APU PWR ON dark): still starting when BS_APU read it, it was "Already set", and the
            // wait below stopped the flow after 60 s. Its power is requested now that it runs; a
            // request already made (APU PWR started it) lights ON within the settle and nothing is
            // pressed (X.ApuPower).
            Skip(SW("BS_APU_PWR", "APU power: ON", X.ApuPower, 1), s => s.IsOn("FO_APU_POWER_ON")),
            // Never pull external power before the APU is powering the busses.
            WaitForField("BS_APU_POWER", "APU power on", "FO_APU_POWER_ON", v => v > 0.5, 60,
                onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Skip(SW("BS_EXT_OFF", "External power: OFF", X.ExtPower, 0), s => s.GetValue("FO_EXT_POWER_ON") < 0.5), "BS_EXT_OFF"),
                "BSC_EXT_PWR"),
            // An AUX PUMP press stops a running hydraulic test.
            WaitForField("BS_HYD_TEST_DONE", "Hydraulic test to finish", "FO_HYD_TEST_RUNNING", v => v < 0.5, 120),
            Also(Done(Skip(SW("BS_AUX_PUMP", "Auxiliary hydraulic pump 1: ON", "MD11_OVHD_HYD_AUX_PUMP_1_BT", 1),
                s => s.IsOn("FO_AUX_PUMP_1_ON")), "BS_AUX_PUMP"), "BSC_AUX_PUMP"),
            Also(Done(Skip(SW("BS_IGNITION", "Engine ignition: A", X.Ignition, 1), s => s.IsOn("FO_IGNITION_SELECTED")), "BS_IGNITION"),
                "BSC_IGNITION"),
            Also(Done(Skip(SW("BS_APU_BLEED", "APU bleed: ON", ApuBleed, 1), s => s.IsOn(ApuBleed)), "BS_APU_BLEED"),
                "BSC_APU_BLEED"),
            Also(Done(Skip(SW("BS_BEACON", "Beacon: ON", "MD11_OVHD_LTS_BCN_BT", 1), s => s.IsOn("FO_BEACON_ON")), "BS_BEACON"),
                "BSC_BEACON"),
            // About ten minutes after Power Up; read only, and not waited for.
            Check("BS_CHK_IRS", "IRS: aligned", "FO_IRS_ALIGNED", On, CheckSeconds, "BSC_IRS"),
            Captain("BS_FMS", "Confirm the FMS: weights, wind, runway slope, temperature and V-speeds"),
            Captain("BS_EIS", "Set the EIS bugs"),
            Captain("BS_ACARS", "Start ACARS"),
            Captain("BS_CLEARANCE", "Obtain pushback and start clearance"),
        }
    };

    // ------------------------------------------------------------------ Engine Start (3, 1, 2)
    private static Flow BuildEngineStart()
    {
        var steps = new List<Step>
        {
            WaitForField("ES_DATA", "Engine data", "FO_ENG3_N2", v => !double.IsNaN(v), 10, onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("ES_AIR", "APU running with its bleed on", "FO_START_AIR_READY", v => v > 0.5, 10,
                onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("ES_IGN", "Engine ignition selected", "FO_IGNITION_SELECTED", v => v > 0.5, 10,
                onTimeout: FlowStepFailurePolicy.Stop),
        };
        foreach (var (n, start, lightUp, fuel, sw) in new[]
        {
            (3, X.EngineStart3, X.EngineLightUp3, "MD11_THR_R_FUEL_SW", "MD11_THR_R_START_SW"),
            (1, X.EngineStart1, X.EngineLightUp1, "MD11_THR_L_FUEL_SW", "MD11_THR_L_START_SW"),
            (2, X.EngineStart2, X.EngineLightUp2, "MD11_THR_C_FUEL_SW", "MD11_THR_C_START_SW"),
        })
        {
            string n2 = $"FO_ENG{n}_N2";
            // Never linked to its StayComplete line ("Engine n start switch: PULL", latched on the
            // engine RUNNING): a pull marked it, a failed light-up then stopped the flow, and the line
            // stayed ticked for good over an engine that never ran (the Boeing selectors' rule, FO-6).
            // Stop: an engine turning below the running line (a coast-down after an abort) cannot be
            // pulled, and the light-up gate below passes on N2 alone, so going on put the fuel ON
            // with no starter engaged.
            steps.Add(Stop(Skip(SW($"ES_E{n}_START", $"Engine {n} start switch: PULL", start, 1),
                s => s.GetValue(n2) >= Md11FoStateEvaluator.EngineRunningN2)));
            // The executor waits for 15 % N2; with no light-up in 60 s it pushes the START switch
            // back in and fails, and Stop ends the flow before any fuel goes on. A switch that never
            // pops in (a hung start) stops the flow at the cutout wait with the starter engaged and the
            // fuel on: that abort is the Captain's.
            steps.Add(Stop(SW($"ES_E{n}_N2_15", $"Engine {n} N2 fifteen percent", lightUp, 1)));
            steps.Add(Done(Skip(SW($"ES_E{n}_FUEL", $"Engine {n} fuel switch: ON", fuel, 1), s => s.IsOn(fuel)), $"ES_E{n}_FUEL"));
            steps.Add(WaitForField($"ES_E{n}_CUTOUT", $"Engine {n} start switch to pop in", sw, v => v < 0.5, 90,
                onTimeout: FlowStepFailurePolicy.Stop));
            steps.Add(Done(WaitForField($"ES_E{n}_STABLE", $"Engine {n} stabilized", n2, v => v >= 55, 60,
                onTimeout: FlowStepFailurePolicy.Stop), $"ESC_E{n}"));
        }
        steps.Add(Check("ES_CHK_START_IN", "Engine start switches: IN", "FO_START_SWITCHES_IN", On, CheckSeconds, "ESC_START_IN"));
        steps.Add(Captain("ES_ANTI_ICE", "Engine anti-ice as required"));
        return new Flow
        {
            Id = "ENGINE_START", Name = "Engine Start",
            Description = "Engines 3, 1 and 2: pull the start switch, fuel on at 15 percent N2, wait for cutout and stabilization.",
            RelatedChecklistGroupIds = new[] { "ENGINE_START", "ENGINE_START_CL" },
            Steps = steps,
        };
    }

    // ------------------------------------------------------------------ After Start
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "APU bleed off, flap handle to the Dial-A-Flap detent, spoilers armed, autobrake T.O., config page, taxi light, transponder XPDR with altitude reporting.",
        RelatedChecklistGroupIds = new[] { "AFTER_START", "AFTER_START_CL" },
        Steps = new()
        {
            Also(Done(Skip(SW("AS_APU_BLEED", "APU bleed: OFF", ApuBleed, 0), s => s.IsPosition(ApuBleed, 0)), "AS_APU_BLEED"),
                "ASC_APU_BLEED"),
            Also(Done(Skip(SW("AS_FLAPS", "Flap handle: Dial-A-Flap detent", X.FlapHandle, 2), s => s.IsOn("FO_FLAPS_DAF")), "AS_FLAPS"),
                "ASC_FLAPS"),
            Done(Skip(SW("AS_SPOILERS", "Spoilers: ARM", X.Spoilers, 1), s => s.IsOn("FO_SPOILERS_ARMED")), "AS_SPOILERS"),
            Done(Skip(SW("AS_AUTOBRAKE", "Autobrake: T.O.", Autobrake, 0), s => s.IsPosition(Autobrake, 0)), "AS_AUTOBRAKE"),
            Done(SW("AS_CONFIG_PAGE", "System display: config page", "MD11_PED_SD_CONFIG_BT", 1), "AS_CONFIG_PAGE"),
            Also(Done(Skip(SW("AS_TAXI_LIGHT", "Nose light: TAXI", Nose, 1), s => s.IsPosition(Nose, 1)), "AS_TAXI_LIGHT"),
                "ASC_TAXI_LIGHT"),
            // Altitude reporting before the aircraft taxis, through the same knob and switch Before
            // Takeoff turns to TA/RA (AftFootPedestal.xml: MD11_PED_XPNDR_MODE_KB 0 Standby /
            // 1 Transponder only / 2 TA only / 3 TA-RA, MD11_PED_XPNDR_ALT_RPTG_KB 1 Enabled). A mode
            // already past standby (XPDR, TA or TA/RA) is left where it is.
            Done(Skip(SW("AS_XPDR_MODE", "Transponder: XPDR", XpdrMode, 1), s => s.GetValue(XpdrMode) > 0.5), "AS_XPDR_MODE"),
            Done(Skip(SW("AS_ALT_RPTG", "Altitude reporting: ON", AltRptg, 1), s => s.IsOn(AltRptg)), "AS_ALT_RPTG"),
            Captain("AS_FLT_CTRL", "Check the flight controls"),
            Captain("AS_STAB_TRIM", "Set the stabilizer trim from the takeoff data"),
        }
    };

    // ------------------------------------------------------------------ Before Takeoff
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Landing lights, strobes, transponder TA/RA with altitude reporting, spoilers and autobrake verified, then NAV, PROF and AUTO FLIGHT.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            Also(Done(Skip(SW("BT_LANDING_LIGHTS", "Landing lights: ON", X.LandingLights, 2), s => s.IsOn("FO_LANDING_LIGHTS_ON")),
                "BT_LANDING_LIGHTS"), "BTC_LANDING_LIGHTS"),
            Also(Done(Skip(SW("BT_STROBES", "Strobe lights: ON", "MD11_OVHD_LTS_HI_INT_BT", 1), s => s.IsOn("FO_STROBES_ON")),
                "BT_STROBES"), "BTC_STROBES"),
            Done(Skip(SW("BT_XPDR_MODE", "Transponder: TA/RA", XpdrMode, 3), s => s.IsPosition(XpdrMode, 3)), "BT_XPDR_MODE"),
            Done(Skip(SW("BT_ALT_RPTG", "Altitude reporting: ON", AltRptg, 1), s => s.IsOn(AltRptg)), "BT_ALT_RPTG"),
            Also(Done(Skip(SW("BT_SPOILERS", "Spoilers: ARM", X.Spoilers, 1), s => s.IsOn("FO_SPOILERS_ARMED")), "BT_SPOILERS"),
                "BTC_SPOILERS"),
            Also(Done(Skip(SW("BT_AUTOBRAKE", "Autobrake: T.O.", Autobrake, 0), s => s.IsPosition(Autobrake, 0)), "BT_AUTOBRAKE"),
                "BTC_AUTOBRAKE"),
            // Read only: After Start set the flaps, and wing anti-ice is the Captain's.
            Check("BT_CHK_FLAPS", "Flaps and slats: set for takeoff", "FO_FLAPS_DAF", On, CheckSeconds, "BTC_FLAPS"),
            Check("BT_CHK_ANTI_ICE", "Wing anti-ice: OFF", "FO_WING_ANTI_ICE_OFF", On, CheckSeconds, "BTC_ANTI_ICE"),
            Captain("BT_EIS", "Verify the EIS bugs, the runway, the stabilizer trim green band and the takeoff data"),
            // The checklist is run at lineup (owner). NAV and PROF export no armed state, so their
            // presses cannot be read back; a second press leaves them armed, so a re-run is safe.
            // AUTO FLIGHT arms the autothrottle. Each also completes its read-back line.
            Also(Done(SW("BT_NAV", "NAV: ARM", "MD11_CGS_NAV_BT", 1), "BT_NAV"), "BTC_NAV"),
            Also(Done(SW("BT_PROF", "PROF: ARM", "MD11_CGS_PROF_BT", 1), "BT_PROF"), "BTC_PROF"),
            Also(Done(Skip(SW("BT_AUTO_FLIGHT", "Auto flight: ON", X.AutoFlight, 1), s => s.IsOn("FO_AUTO_FLIGHT_ON")),
                "BT_AUTO_FLIGHT"), "BTC_AUTOFLIGHT"),
        }
    };

    // ------------------------------------------------------------------ After Takeoff
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Gear up, spoilers disarmed, autobrake off. Flaps and slats stay with the Captain.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Done(Skip(SW("AT_GEAR", "Gear: UP", X.Gear, 0), s => s.IsOn("FO_GEAR_UP")), "AT_GEAR"),
            Also(Done(Skip(SW("AT_SPOILERS", "Spoilers: DISARM", X.Spoilers, 0), s => s.IsOn("FO_SPOILERS_DOWN")), "AT_SPOILERS"),
                "ATC_SPOILERS"),
            Also(Done(Skip(SW("AT_AUTOBRAKE", "Autobrake: OFF", Autobrake, 1), s => s.IsPosition(Autobrake, 1)), "AT_AUTOBRAKE"),
                "ATC_AUTOBRAKE"),
            Captain("AT_FLAPS", "Flaps and slats up and retracted on schedule"),
            // The Captain's flaps, read only: TFDi's After Takeoff checklist follows the retraction,
            // so the flow waits for the handle UP, up to five minutes, then skips aloud.
            Check("AT_FLAPS_UP_CHECK", "Flaps and slats: up and retracted", "FO_FLAPS_UP", On, FlapsUpCheckSeconds, "ATC_FLAPS"),
            Captain("AT_EAD", "Check the engine and alert display for alerts"),
            // LAST (FO-13): the gear confirmed up (lever up, every leg in), read only. 20 s, then skip.
            Check("AT_GEAR_UP_CHECK", "Gear: UP", Md11GearConfirmation.UpField, On, GearCheckSeconds, "ATC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ Descent (below 10,000 ft / transition level)
    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Landing lights on; reminders for local QNH, the landing autobrake and the sterile cockpit chime.",
        // No read-back list: TFDi's in-flight pages are the automatic modes' work (owner, 2026-10-13).
        RelatedChecklistGroupIds = new[] { "DESCENT" },
        Steps = new()
        {
            Done(Skip(SW("DS_LANDING_LIGHTS", "Landing lights: ON", X.LandingLights, 2), s => s.IsOn("FO_LANDING_LIGHTS_ON")), "DS_LANDING_LIGHTS"),
            Captain("DS_QNH", "Set the altimeters to local QNH"),
            Captain("DS_AUTOBRAKE", "Set the landing autobrake on the center instrument panel"),
            Captain("DS_STERILE", "Sterile cockpit chime"),
        }
    };

    // ------------------------------------------------------------------ Before Landing
    private static Flow BuildBeforeLanding() => new()
    {
        Id = "BEFORE_LANDING", Name = "Before Landing",
        Description = "Gear down, spoilers armed. Flaps, slats and the missed approach altitude stay with the Captain.",
        RelatedChecklistGroupIds = new[] { "BEFORE_LANDING", "BEFORE_LANDING_CL" },
        Steps = new()
        {
            Done(Skip(SW("BL_GEAR", "Gear: DOWN", X.Gear, 1), s => s.IsOn("FO_GEAR_DOWN")), "BL_GEAR"),
            Also(Done(Skip(SW("BL_SPOILERS", "Spoilers: ARM", X.Spoilers, 1), s => s.IsOn("FO_SPOILERS_ARMED")), "BL_SPOILERS"),
                "BLC_SPOILERS"),
            Captain("BL_FLAPS", "Flaps and slats as required"),
            Captain("BL_MISSED", "Set the missed approach altitude"),
            // LAST (FO-13): the gear confirmed down (lever down, every leg out), read only. 20 s, then skip.
            Check("BL_GEAR_DOWN_CHECK", "Gear: DOWN", Md11GearConfirmation.DownField, On, GearCheckSeconds, "BLC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ After Landing (on the ground)
    // TFDi quick-start order: stow the spoilers, then flaps and slats, strobes, APU.
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Ground spoilers stowed, flaps up, landing lights retracted, strobes off, weather radar off, APU start.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING", "AFTER_LANDING_CL" },
        Steps = new()
        {
            // Deployed on landing: one lever click, and TFDi's spring stows the lever and disarms.
            Also(Done(Skip(SW("AL_SPOILERS", "Spoilers: DOWN", X.Spoilers, 0), s => s.IsOn("FO_SPOILERS_DOWN")), "AL_SPOILERS"),
                "ALC_SPOILERS"),
            Also(Done(Skip(SW("AL_FLAPS", "Flap handle: UP", X.FlapHandle, 0), s => s.IsOn("FO_FLAPS_UP")), "AL_FLAPS"),
                "ALC_FLAPS"),
            Also(Done(Skip(SW("AL_LANDING_LIGHTS", "Landing lights: retracted", X.LandingLights, 0),
                s => s.IsOn("FO_LANDING_LIGHTS_RETRACTED")), "AL_LANDING_LIGHTS"), "ALC_LANDING_LIGHTS"),
            Also(Done(Skip(SW("AL_STROBES", "Strobe lights: OFF", "MD11_OVHD_LTS_HI_INT_BT", 0),
                s => s.GetValue("FO_STROBES_ON") < 0.5), "AL_STROBES"), "ALC_STROBES"),
            Also(Done(Skip(SW("AL_WXR", "Weather radar: OFF", X.WeatherRadarOff, 1), s => s.IsOn("FO_WXR_OFF")), "AL_WXR"),
                "ALC_WXR"),
            // "Already set" only starting, or running with its power on: running with its power off,
            // the step requests APU power (X.ApuStart), or Parking would wait for a power source and
            // stop with the engines running.
            Also(Done(Skip(SW("AL_APU", "APU: start", X.ApuStart, 1), ApuStartingOrPowering), "AL_APU"), "ALC_APU"),
            Captain("AL_REVERSERS", "Confirm the reversers are stowed"),
            Captain("AL_STAB_TRIM", "Set the stabilizer trim to 3 degrees nose up"),
        }
    };

    // ------------------------------------------------------------------ Parking
    private static Flow BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Parking brake, fuel switches off once APU or external power is on, seat belts off, anti-ice and ignition off, exterior lights off except nav.",
        RelatedChecklistGroupIds = new[] { "PARKING", "PARKING_CL" },
        Steps = new()
        {
            WaitForField("PK_STOPPED", "Aircraft stopped", "GROUND_VELOCITY", v => v < 1, 120, onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Skip(SW("PK_PARK", "Parking brake: SET", Park, 1), s => s.IsOn(Park)), "PK_PARK"), "PKC_PARK"),
            // After Landing's "APU: start" is "Already set" over an APU still starting: started from
            // the aft-overhead APU START button, it then ran with its power off, and the guard below
            // waited for a power source and stopped the flow with the engines running. With no power
            // source on and the APU starting or running, its power is requested once it runs
            // (X.ApuPower waits out the start; a request already made lights ON within the settle and
            // nothing is pressed). Otherwise silent (FO-20).
            // Spoken before the wait, so it says the wait.
            Provider("PK_APU_PWR", "APU power: ON once the APU runs", X.ApuPower, s => ApuOwedItsPower(s) ? 1 : null),
            // The fuel cut takes the engine generators off the busses: APU power or external power
            // must be on first, or the aircraft drops to its battery (After Landing's APU start is a
            // press with no wait). Before Start guards the reverse. Read only; up to two minutes for
            // an APU still starting, then the flow stops with the engines running.
            Skip(WaitForField("PK_POWER_SOURCE", "APU or external power on", "FO_APU_OR_EXT_POWER_ON", On, 120,
                onTimeout: FlowStepFailurePolicy.Stop), s => s.IsOn("FO_APU_OR_EXT_POWER_ON")),
            Also(Done(Skip(Multi("PK_FUEL", "Fuel switches: OFF", (FuelL, 0), (FuelC, 0), (FuelR, 0)),
                s => s.IsOn("FO_FUEL_SWITCHES_OFF")), "PK_FUEL"), "PKC_FUEL"),
            Also(Done(Skip(SW("PK_SEAT_BELTS", "Seat belt signs: OFF", SeatBelts, 0), s => s.IsPosition(SeatBelts, 0)), "PK_SEAT_BELTS"),
                "PKC_SEAT_BELTS"),
            Done(Skip(SW("PK_ANTI_ICE", "Anti-ice: OFF", X.AntiIceOff, 0), s => s.IsOn("FO_ANTI_ICE_OFF")), "PK_ANTI_ICE"),
            WaitForField("PK_SPOOL_DOWN", "Engines to spool down", "FO_ENGINES_STOPPED", v => v > 0.5, 90),
            Also(Done(Skip(SW("PK_IGNITION", "Engine ignition: OFF", X.Ignition, 0), s => s.IsOn("FO_IGNITION_OFF")), "PK_IGNITION"),
                "PKC_IGNITION"),
            Done(Skip(SW("PK_BEACON", "Beacon: OFF", "MD11_OVHD_LTS_BCN_BT", 0), s => s.GetValue("FO_BEACON_ON") < 0.5), "PK_BEACON"),
            Done(Skip(SW("PK_LANDING_LIGHTS", "Landing lights: retracted", X.LandingLights, 0),
                s => s.IsOn("FO_LANDING_LIGHTS_RETRACTED")), "PK_LANDING_LIGHTS"),
            Done(Skip(SW("PK_NOSE", "Nose light: OFF", Nose, 0), s => s.IsPosition(Nose, 0)), "PK_NOSE"),
            Done(Skip(SW("PK_STROBES", "Strobe lights: OFF", "MD11_OVHD_LTS_HI_INT_BT", 0), s => s.GetValue("FO_STROBES_ON") < 0.5), "PK_STROBES"),
            Done(Skip(SW("PK_LOGO", "Logo lights: OFF", "MD11_OVHD_LTS_LOGO_BT", 0), s => s.GetValue("FO_LOGO_ON") < 0.5), "PK_LOGO"),
            Done(Skip(Multi("PK_TURNOFF", "Runway turnoff lights: OFF",
                    ("MD11_OVHD_LTS_RWY_TURNOFF_L_BT", 0), ("MD11_OVHD_LTS_RWY_TURNOFF_R_BT", 0)),
                s => s.IsOn("FO_RWY_TURNOFF_OFF")), "PK_TURNOFF"),
            // The sum of the light steps above, with the nav lights still on: read only.
            Check("PK_CHK_EXT_LIGHTS", "Exterior lights: all off except nav", "FO_EXTERIOR_PARKED", On, CheckSeconds, "PKC_EXT_LIGHTS"),
            // Never claims which source powers the aircraft: the guard above accepts either one.
            Captain("PK_EXT_PWR", "Connect ground power on the EFB Services page if required"),
        }
    };

    // ------------------------------------------------------------------ Shutdown (TFDi "Shutdown")
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Emergency lights and power off, windshield heat off, IRS off, cargo temperatures off, dome off, EVAC off, packs off, APU off, external power off, battery off.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN", "SHUTDOWN_CL" },
        Steps = new()
        {
            // Switches the IRS and the battery off: never airborne, never with engines turning.
            OnGroundGuard("SD_ON_GROUND"),
            EnginesStoppedGuard("SD_ENGINES_STOPPED"),
            Also(Done(Skip(SW("SD_EMER_LTS", "Emergency lights: OFF", EmerLts, 0), s => s.IsPosition(EmerLts, 0)), "SD_EMER_LTS"),
                "SDC_EMER_LTS"),
            Also(Done(Skip(SW("SD_EMER_PWR", "Emergency power: OFF", EmerPwr, 0), s => s.IsPosition(EmerPwr, 0)), "SD_EMER_PWR"),
                "SDC_EMER_PWR"),
            Also(Done(Skip(Multi("SD_WINDSHIELD", "Windshield anti-ice and defog: OFF",
                    ("MD11_OVHD_WNDSHLD_AICE_L_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_R_BT", 0), ("MD11_OVHD_WNDSHLD_AICE_DEFOG_BT", 1)),
                s => s.IsOn("FO_WINDSHIELD_OFF")), "SD_WINDSHIELD"), "SDC_WINDSHIELD"),
            Also(Done(Skip(Multi("SD_IRS", "IRS switches: OFF", (Irs1, 0), (Irs2, 0), (Irs3, 0)), s => s.IsOn("FO_IRS_OFF")), "SD_IRS"),
                "SDC_IRS"),
            Also(Done(Skip(Multi("SD_CARGO_TEMP", "Cargo temperatures: OFF",
                    ("MD11_OVHD_PNEU_FWD_CARGO_TEMP", 0), ("MD11_OVHD_PNEU_AFT_CARGO_TEMP", 0)),
                s => s.IsOn("FO_CARGO_TEMPS_OFF")), "SD_CARGO_TEMP"), "SDC_CARGO_TEMP"),
            Also(Done(Skip(SW("SD_DOME", "Dome light: OFF", "MD11_OVHD_LTS_DOME_BT", 0), s => s.IsOn("FO_DOME_OFF")), "SD_DOME"),
                "SDC_COCKPIT_LTS"),
            Also(Done(Skip(SW("SD_EVAC", "Evacuation switch: OFF", Evac, 0), s => s.IsPosition(Evac, 0)), "SD_EVAC"),
                "SDC_EVAC"),
            Done(Skip(SW("SD_PACKS", "Packs: OFF", X.PacksOff, 0), s => s.IsOn("FO_PACKS_OFF")), "SD_PACKS"),
            Also(Done(Skip(SW("SD_APU", "APU: OFF", X.ApuShutdown, 0), s => s.IsOn("FO_APU_OFF")), "SD_APU"), "SDC_APU"),
            WaitForField("SD_APU_STOPPED", "APU to stop", "MD11_APU_STATE", v => v < 0.5, 150),
            // Ground power, when it was used (owner, 2026-10-13): the last source before the battery.
            Done(Skip(SW("SD_EXT_PWR", "External power: OFF", X.ExtPowerShutdown, 0), s => s.IsOn("FO_EXT_POWER_OFF")), "SD_EXT_PWR"),
            Also(Done(Skip(SW("SD_BATTERY", "Battery: OFF", Batt, 0), s => s.IsPosition(Batt, 0)), "SD_BATTERY"), "SDC_BATTERY"),
        }
    };

    // ------------------------------------------------------------------ step builders (the A330's, retyped)

    /// <summary>A read-only check of a switch the flow finds: 5 s, then skipped aloud.</summary>
    private const int CheckSeconds = 5;

    /// <summary>The read-only gear checks (FO-13): 20 s, then skipped aloud.</summary>
    private const int GearCheckSeconds = 20;

    /// <summary>After Takeoff waits this long for the Captain's flaps and slats to come up.</summary>
    private const int FlapsUpCheckSeconds = 300;

    private static bool On(double v) => v > 0.5;

    /// <summary>
    /// A read-only check (FO-6, FO-7): "Already set" when <paramref name="field"/> already satisfies
    /// <paramref name="ok"/>, else a wait of <paramref name="timeoutSec"/> that is skipped aloud. It
    /// never writes, and it completes the read-back lines that read the same state, so a line no
    /// write of the flow sets is latched only when the aircraft shows it; a timed-out check keeps
    /// its lines out of the latch, mirroring the aircraft.
    /// </summary>
    private static Step Check(string id, string label, string field, Func<double, bool> ok, int timeoutSec,
        string readBackItemId, params string[] moreReadBackItemIds)
        => Also(Done(Skip(WaitForField(id, label, field, ok, timeoutSec), s => ok(s.GetValue(field))), readBackItemId),
            moreReadBackItemIds);

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<Md11FoStateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,    };

    /// <summary>
    /// Stops a cold-and-dark flow unless every engine reads stopped (N2 below 10 %). Engine N2 is
    /// fed each second by the window, so an unread value — NaN — fails the guard rather than
    /// letting a destructive step through.
    /// </summary>
    /// <summary>
    /// Stops a flow that switches engines, IRS or batteries off unless the aircraft reads on the
    /// ground (the stock SIM_ON_GROUND; unread fails the guard).
    /// </summary>
    private static Step OnGroundGuard(string id)
        => WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, 5,
            onTimeout: FlowStepFailurePolicy.Stop);

    private static Step EnginesStoppedGuard(string id)
        => WaitForField(id, "Engines stopped", "FO_ENGINES_STOPPED", v => v > 0.5, 5,
            onTimeout: FlowStepFailurePolicy.Stop);

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,    };

    private static Step Skip(Step step, Func<Md11FoStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step Stop(Step step)
    {
        step.FailurePolicy = FlowStepFailurePolicy.Stop;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    /// <summary>The read-back line this step also delivers: ticked with it, and kept unticked when it is skipped (FO-6).</summary>
    private static Step Also(Step step, params string[] readBackItemIds)
    {
        step.AlsoCompletesChecklistItemIds = readBackItemIds;
        return step;
    }
}
