using MSFSBlindAssist.FirstOfficer.Models;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using X = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

using Item = Models.ChecklistItem<Bbs757FoActionExecutor, Bbs757FoStateEvaluator>;
using Group = Models.ChecklistGroup<Bbs757FoActionExecutor, Bbs757FoStateEvaluator>;
using CheckFn = System.Func<Bbs757FoActionExecutor, Bbs757FoStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The BlueBird 757 First Officer checklists. Two layers, as on every profile:
///
///   • ACTION groups, one per flow, from BlueBird's simplified procedures (manual v1.00, part 3A):
///     ticking an item sets its switch through the executor, and it auto-detects from the cockpit. An action
///     group holds what someone DOES: a check only read is its checklist's line alone, never repeated here
///     (owner, 2026-11-08).
///   • READ-BACK (*_CL) groups, BlueBird's one-page Normal Checklist (manual p. 201) in its order and
///     words, action-free (FO-3). A line the app can read ticks itself from BlueBird's own variables;
///     a line that needs the pilot's judgment or a figure (fuel against the plan, the briefing, the
///     altimeters) is a Captain reminder. BlueBird's In Range and Approach cards are ONE Descent and Approach
///     Checklist (DESCENT_CL), latched by the Descent flow: the arrival is two flows, as on the MD-11 (owner,
///     2026-11-08). BlueBird has no Preflight or Secure read-back page.
///
/// Engine-variant differences ride the variant (the aircraft title, then <c>L:B757_ENG_VARIANT</c>
/// reading 1 for a PW): the fuel control switches (PW RUN/CUTOFF, RR RUN/RICH/CUTOFF, RICH for a cold
/// RR start), the Rolls-Royce-only EEC switches, the idle N2 an engine reads running at, and the
/// Rolls-Royce hot-start rule (motored to an EGT below 100 °C before fuel). The engine limiters are
/// set on both: the manual lists them as Rolls-Royce only, but the PW has the same switches and
/// BlueBird loads them ON on both (control-verification E3).
///
/// The First Officer never moves the flap lever, the gear lever or the thrust levers, never sets the
/// landing autobrake (FO-14) and never programs the FMC (FO-2).
/// </summary>
public static class Bbs757FoChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        PowerUp(),
        Preflight(),
        BeforeStart(), BeforeStartCL(),
        EngineStart(),
        AfterStart(), AfterStartCL(),
        BeforeTakeoff(), BeforeTakeoffCL(),
        AfterTakeoff(), AfterTakeoffCL(),
        Descent(), DescentCL(),
        BeforeLanding(), BeforeLandingCL(),
        AfterLanding(), AfterLandingCL(),
        Shutdown(), ShutdownCL(),
        Secure(),
    };

    private static bool Is(double v, double p) => Bbs757FoStateEvaluator.Pos(v, p);
    private static bool On(double v) => v > 0.5;
    private static bool Off(double v) => !double.IsNaN(v) && v < 0.5;

    private static CheckFn Set(string key, double target) => (e, _) => e.Set(key, (int)target);

    private static CheckFn SetAll(params (string Key, double Target)[] writes) => async (e, _) =>
    {
        foreach (var (k, t) in writes) await e.Set(k, (int)t);
    };

    private static CheckFn AllTo(IEnumerable<string> keys, double target) => async (e, _) =>
    {
        foreach (var k in keys) await e.Set(k, (int)target);
    };

    // ================================================================== action groups

    private static Group PowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Items = new()
        {
            Auto("PU_GEAR", "POWER_UP", "Landing gear lever: DOWN", "FO_GEAR_DOWN", On, null),
            LeaveAlone(Auto("PU_FUEL_CUTOFF", "POWER_UP", "Fuel control switches: CUTOFF", "FO_FUEL_CONTROLS_CUTOFF", On,
                SetAll((X.FuelControl1, 0), (X.FuelControl2, 0))), NotParked, FuelStaysText),
            LeaveAlone(Auto("PU_ELEC_HYD_OFF", "POWER_UP", "Electric hydraulic pumps: OFF", "FO_ELEC_HYD_PUMPS_OFF", On,
                AllTo(new[] { K.LeftElecHyd, K.C2ElecHyd, K.C1ElecHyd, K.RightElecHyd }, 0)), NotParked, HydStaysText),
            Auto("PU_BATTERY", "POWER_UP", "Battery: ON", K.Battery, On, Set(K.Battery, 1)),
            Auto("PU_STBY_POWER", "POWER_UP", "Standby power: AUTO", K.StandbyPower, v => Is(v, 1), Set(K.StandbyPower, 1)),
            Auto("PU_ELEC_PANEL", "POWER_UP", "Bus ties, generator and utility bus controls: ON", "FO_ELEC_PANEL_SET", On,
                AllTo(ElecPanel, 1)),
            Auto("PU_EXT_PWR", "POWER_UP", "External power: ON", "FO_EXT_POWER_ON", On, Set(X.ExtPower, 1)),
            Auto("PU_IRS", "POWER_UP", "IRS selectors: NAV", "FO_IRS_ALL_NAV", On, AllTo(K.IrsSwitches, K.IrsNav)),
            Auto("PU_POSITION", "POWER_UP", "Position lights: ON", K.PositionLights, On, Set(K.PositionLights, 1)),
        }
    };

    internal static readonly string[] ElecPanel =
    {
        K.LeftBusTie, K.RightBusTie, K.LeftGenCont, K.RightGenCont, K.ApuGen, K.LeftUtilBus, K.RightUtilBus,
    };

    private static Group Preflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Items =
        [
            Auto("PF_ENGINE_CONTROLS", "PREFLIGHT", "Engine limiters and, on a Rolls-Royce, EEC: ON", "FO_ENGINE_CONTROLS_ON", On,
                Set(X.EngineControls, 1)),
            Auto("PF_YAW_DAMPERS", "PREFLIGHT", "Yaw dampers: ON", "FO_YAW_DAMPERS_ON", On,
                SetAll((K.LeftYawDamper, 1), (K.RightYawDamper, 1))),
            Auto("PF_ENG_HYD", "PREFLIGHT", "Engine hydraulic pumps: ON", "FO_ENG_HYD_PUMPS_ON", On,
                SetAll((K.LeftEngHyd, 1), (K.RightEngHyd, 1))),
            Auto("PF_EMER_LTS", "PREFLIGHT", "Emergency lights: ARMED", K.EmergencyLights, v => Is(v, 1), Set(K.EmergencyLights, 1)),
            Auto("PF_IGNITION", "PREFLIGHT", "Ignition: 1 or 2", K.Ignition, v => !double.IsNaN(v) && v < 1.5, Set(K.Ignition, 0)),
            Auto("PF_START_SEL", "PREFLIGHT", "Engine start selectors: AUTO", "FO_START_SELECTORS_AUTO", On,
                SetAll((K.LeftStart, K.StartAuto), (K.RightStart, K.StartAuto))),
            Auto("PF_XFEED", "PREFLIGHT", "Fuel crossfeed: OFF", "FO_CROSSFEEDS_OFF", On, SetAll((K.XfeedFwd, 0), (K.XfeedAft, 0))),
            LeaveAlone(Auto("PF_FUEL_PUMPS", "PREFLIGHT", "Fuel pumps: OFF", "FO_FUEL_PUMPS_OFF", On, AllTo(AllFuelPumps, 0)),
                NotParked, PumpsStayText),
            Auto("PF_ANTI_ICE", "PREFLIGHT", "Wing and engine anti-ice: OFF", "FO_ANTI_ICE_OFF", On,
                SetAll((K.WingAntiIce, 0), (K.LeftEngAntiIce, 0), (K.RightEngAntiIce, 0))),
            Auto("PF_WIPERS", "PREFLIGHT", "Wipers: OFF", K.Wipers, v => Is(v, 0), Set(K.Wipers, 0)),
            Auto("PF_LIGHTS_OFF", "PREFLIGHT", "Anti-collision, wing, landing, runway turnoff and taxi lights: OFF", "FO_PREFLIGHT_LIGHTS_OFF", On,
                SetAll((K.BeaconLight, 0), (K.StrobeLights, 0), (K.WingLights, 0), (K.LeftLanding, 0), (K.RightLanding, 0),
                    (K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 0))),
            Auto("PF_WINDOW_HEAT", "PREFLIGHT", "Window heat: ON", "FO_WINDOW_HEAT_ON", On, AllTo(K.WindowHeat, 1)),
            Auto("PF_NO_SMOKING", "PREFLIGHT", "No smoking signs: ON", K.NoSmoking, v => Is(v, K.SignOn), Set(K.NoSmoking, K.SignOn)),
            Auto("PF_PRESS_MODE", "PREFLIGHT", "Pressurization mode: AUTO", K.PressMode, v => !double.IsNaN(v) && v < 1.5, Set(K.PressMode, 0)),
            Reminder("PF_LANDING_ALT", "PREFLIGHT", "Landing altitude: set to the destination elevation"),
            Auto("PF_TRIM_AIR", "PREFLIGHT", "Trim air and recirculation fans: ON", "FO_TRIM_AIR_RECIRC_ON", On,
                SetAll((K.TrimAir, 1), (K.LeftRecirc, 1), (K.RightRecirc, 1))),
            Auto("PF_ISOLATION", "PREFLIGHT", "Isolation valve: OPEN", "FO_ISOLATION_OPEN", On, Set(K.Isolation, Bbs757FoStateEvaluator.IsolationOpen)),
            Auto("PF_BLEEDS", "PREFLIGHT", "Engine and APU bleeds: ON", "FO_ALL_BLEEDS_ON", On,
                SetAll((K.LeftEngBleed, 1), (K.RightEngBleed, 1), (K.ApuBleed, 1))),
            Auto("PF_FDS", "PREFLIGHT", "Flight directors: ON", "FO_FDS_ON", On, SetAll((K.CaptainFd, 1), (K.FirstOfficerFd, 1))),
            // BlueBird's own A/T flag, not the switch (R40).
            Auto("PF_AT_ARM", "PREFLIGHT", "Autothrottle: ARM", "FO_AT_ARMED", On, Set(X.ArmAutothrottle, 1)),
            Auto("PF_BANK_LIMIT", "PREFLIGHT", "Bank limit: AUTO", K.BankLimit, v => Is(v, 0), Set(K.BankLimit, 0)),
            Auto("PF_AUTOBRAKE", "PREFLIGHT", "Autobrake: RTO", K.Autobrake, v => Is(v, K.AutobrakeRto), Set(K.Autobrake, K.AutobrakeRto)),
            // BlueBird's tests, as the flow runs them: a tick runs the test. A self-test has no state, so every tick
            // runs it again (FO-12). Only parked with both engines stopped, as the flow's guards have it (FO-25's
            // pattern): never a TCAS test, stick shaker, GPWS or fire bell in flight or during an engine start.
            .. Bbs757FoTests.Preflight.Select(t => LeaveAlone(
                ActionManual(Bbs757FoTests.PreflightId(t), "PREFLIGHT", t.Label, Set(t.Key, 1)), NotParked, TestsStayText)),
            Reminder("PF_FMS", "PREFLIGHT", "FMS: route, winds, performance and takeoff data loaded"),
            Reminder("PF_ALTIMETERS", "PREFLIGHT", "Altimeters: set"),
        ]
    };

    internal static readonly string[] AllFuelPumps =
    {
        K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump, K.CenterLeftPump, K.CenterRightPump,
    };

    private static Group BeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            // "Doors: CLOSED" is the Before Start Checklist's line alone (BSC_DOORS), never repeated here.
            // The manual's order: R, then C, then L.
            Auto("BS_HYD", "BEFORE_START", "Electric hydraulic pumps: ON, R then C then L", "FO_ELEC_HYD_PUMPS_ON", On,
                SetAll((K.RightElecHyd, 1), (K.C1ElecHyd, 1), (K.C2ElecHyd, 1), (K.LeftElecHyd, 1))),
            // After the pumps: the brake needs their pressure (owner, 2026-11-08).
            LeaveAlone(Auto("BS_PARK", "BEFORE_START", "Parking brake: SET", K.ParkingBrake, On, Set(X.ParkingBrake, 1)),
                NoBrakePressure, BrakeStaysText),
            Auto("BS_FUEL", "BEFORE_START", "Fuel pumps: ON, center pumps with center fuel", "FO_FUEL_PANEL_SET", On, FuelPumpsForStart),
            Auto("BS_SEAT_BELTS", "BEFORE_START", "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn), Set(K.SeatBelts, K.SignOn)),
            // SLOW (FO-11): a tick starts the APU and waits for it to run, outside the executor's gate.
            Auto("BS_APU", "BEFORE_START", "APU: running", "FO_APU_RUNNING", On, (e, _) => e.StartApuAndWaitAsync()),
            // The transfer to the APU (S36 #10-11): only with the APU on line.
            LeaveAlone(Auto("BS_EXT_PWR_OFF", "BEFORE_START", "External power: OFF", "FO_EXT_POWER_OFF", On, Set(X.ExtPower, 0)),
                s => !s.IsOn("FO_APU_ON_LINE"), ExtPowerStaysText),
            Reminder("BS_GPU", "BEFORE_START", "Disconnect the GPU on the EFB; recheck TAKEOFF REF speeds and autobrake"),
            Auto("BS_APU_BLEED", "BEFORE_START", "APU bleed: ON", K.ApuBleed, On, Set(K.ApuBleed, 1)),
            Auto("BS_XPDR", "BEFORE_START", "Transponder: TA/RA", K.TcasMode, v => Is(v, K.TcasTaRa), Set(K.TcasMode, K.TcasTaRa)),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON", K.BeaconLight, On, Set(K.BeaconLight, 1)),
            Auto("BS_LOGO", "BEFORE_START", "Logo lights: ON", K.LogoLights, On, Set(K.LogoLights, 1)),
            Reminder("BS_MCP", "BEFORE_START", "MCP: speed, heading and initial altitude set"),
            // The Captain's, last on every profile (FoChecklistRefinementTests).
            Reminder("BS_ACARS", "BEFORE_START", "Start ACARS"),
            Reminder("BS_CLEARANCE", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    /// <summary>The four wing pumps ON, the two center pumps ON only with center fuel, crossfeeds OFF.</summary>
    internal static readonly CheckFn FuelPumpsForStart = async (e, s) =>
    {
        foreach (var k in new[] { K.LeftFwdPump, K.LeftAftPump, K.RightFwdPump, K.RightAftPump }) await e.Set(k, 1);
        double centerWanted = s.GetValue("FO_CENTER_FUEL_FOR_PUMPS");
        if (!double.IsNaN(centerWanted))
            foreach (var k in new[] { K.CenterLeftPump, K.CenterRightPump }) await e.Set(k, centerWanted > 0.5 ? 1 : 0);
        await e.Set(K.XfeedFwd, 0);
        await e.Set(K.XfeedAft, 0);
    };

    private static Group EngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            LeaveAlone(Auto("ES_PACKS_OFF", "ENGINE_START", "Packs: OFF", "FO_PACKS_OFF", On,
                SetAll((K.LeftPackSelector, K.PackOff), (K.RightPackSelector, K.PackOff))), NotOnGround, PacksStayText),
            Auto("ES_BLEEDS", "ENGINE_START", "Engine and APU bleeds: ON", "FO_ALL_BLEEDS_ON", On,
                SetAll((K.LeftEngBleed, 1), (K.RightEngBleed, 1), (K.ApuBleed, 1))),
            Auto("ES_ISOLATION", "ENGINE_START", "Isolation valve: OPEN", "FO_ISOLATION_OPEN", On, Set(K.Isolation, Bbs757FoStateEvaluator.IsolationOpen)),
            // The engine-start SELECTOR lines latch on the engine's N2 past the starter (FOB-1): the selector
            // returns to AUTO at cut-out, so its own position would untick a finished start.
            AutoLatch("ES_E2_GND", "ENGINE_START", "Right engine start selector: GND", "FO_ENG2_N2", EngineLit, Set(K.RightStart, K.StartGnd)),
            FuelForStartLine(2, "ES_E2_FUEL", "Right"),
            AutoLatch("ES_E1_GND", "ENGINE_START", "Left engine start selector: GND", "FO_ENG1_N2", EngineLit, Set(K.LeftStart, K.StartGnd)),
            FuelForStartLine(1, "ES_E1_FUEL", "Left"),
        }
    };

    /// <summary>
    /// An engine's fuel control line: RUN (RICH on a cold Rolls-Royce) by hand only at the fuel point (manual
    /// p. 208: "Do not introduce fuel into a stationary engine"); a tick before it is refused and says why.
    /// </summary>
    private static Item FuelForStartLine(int n, string id, string side)
        => LeaveAlone(Auto(id, "ENGINE_START", $"{side} fuel control switch: RUN (at the fuel point)", $"FO_FUEL_CONTROL_{n}_RUN", On,
                Set(n == 1 ? X.FuelControl1 : X.FuelControl2, X.FuelForStart)),
            s => NotAtFuelPoint(s, n), $"{side} fuel control switch left at cutoff: the engine is not at the fuel point.");

    /// <summary>A hand RUN is refused unless that engine reads at its fuel point (unread refuses), or its switch already reads RUN.</summary>
    internal static bool NotAtFuelPoint(Bbs757FoStateEvaluator s, int n) =>
        !s.IsOn($"FO_ENG{n}_FUEL_POINT") && !s.IsOn($"FO_FUEL_CONTROL_{n}_RUN");

    private static Group AfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_START_SEL", "AFTER_START", "Engine start selectors: AUTO", "FO_START_SELECTORS_AUTO", On, null),
            Auto("AS_PACKS", "AFTER_START", "Packs: AUTO", "FO_PACKS_AUTO", On,
                SetAll((K.LeftPackSelector, K.PackAuto), (K.RightPackSelector, K.PackAuto))),
            Auto("AS_ISOLATION", "AFTER_START", "Isolation valve: CLOSED", "FO_ISOLATION_CLOSED", On,
                Set(K.Isolation, Bbs757FoStateEvaluator.IsolationClosed)),
            Auto("AS_APU_BLEED", "AFTER_START", "APU bleed: OFF", K.ApuBleed, Off, Set(K.ApuBleed, 0)),
            LeaveAlone(Auto("AS_APU", "AFTER_START", "APU: OFF", "FO_APU_OFF", On, Set(K.ApuSelector, 0)), NoOtherPower, ApuStaysText),
            Auto("AS_SPEEDBRAKE", "AFTER_START", "Speedbrake lever: DOWN", "FO_SPEEDBRAKE_DOWN", On, Set(X.SpeedbrakeDown, 1)),
            Auto("AS_AUTOBRAKE", "AFTER_START", "Autobrake: RTO", K.Autobrake, v => Is(v, K.AutobrakeRto), Set(K.Autobrake, K.AutobrakeRto)),
            Auto("AS_TAXI_LIGHT", "AFTER_START", "Taxi light: ON", K.TaxiLight, On, Set(K.TaxiLight, 1)),
            Reminder("AS_FLAPS", "AFTER_START", "Flaps: set for takeoff"),
            Reminder("AS_TRIMS", "AFTER_START", "Trims: rudder zero, aileron zero, stabilizer set for takeoff"),
        }
    };

    private static Group BeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Items = new()
        {
            // "Engines running" is the Before Takeoff Checklist's line alone (BTC_ENGINES), never repeated here.
            Auto("BT_SEAT_BELTS", "BEFORE_TAKEOFF", "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn), Set(K.SeatBelts, K.SignOn)),
            Auto("BT_LANDING", "BEFORE_TAKEOFF", "Landing lights: ON", "FO_LANDING_LIGHTS_ON", On,
                SetAll((K.LeftLanding, 1), (K.RightLanding, 1))),
            Auto("BT_STROBES", "BEFORE_TAKEOFF", "Strobe lights: ON", K.StrobeLights, On, Set(K.StrobeLights, 1)),
            Auto("BT_WING", "BEFORE_TAKEOFF", "Wing lights: ON", K.WingLights, On, Set(K.WingLights, 1)),
            Auto("BT_TURNOFF", "BEFORE_TAKEOFF", "Runway turnoff lights: ON", "FO_TURNOFF_LIGHTS_ON", On,
                SetAll((K.LeftTurnoff, 1), (K.RightTurnoff, 1))),
            Auto("BT_TAXI_LIGHT", "BEFORE_TAKEOFF", "Taxi light: ON", K.TaxiLight, On, Set(K.TaxiLight, 1)),
            // The cabin chime: no state, so every tick rings it again (FO-12).
            ActionManual("BT_CABIN", "BEFORE_TAKEOFF", "Cabin call: advise the cabin crew for takeoff", Set(K.CabinCall, 1)),
            Reminder("BT_CONFIG", "BEFORE_TAKEOFF", "Takeoff configuration: press the CONFIG button and check no warning"),
        }
    };

    private static Group AfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Reminder("AT_GEAR_OFF", "AFTER_TAKEOFF", "Landing gear lever: OFF after retraction"),
            Auto("AT_GROUND_LIGHTS", "AFTER_TAKEOFF", "Runway turnoff and taxi lights: OFF", "FO_GROUND_LIGHTS_OFF", On,
                SetAll((K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 0))),
            // Flaps up and the gear up and off are the After Takeoff Checklist's lines alone (ATC_FLAPS, ATC_GEAR).
        }
    };

    private static Group Descent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Items = new()
        {
            Auto("DS_SEAT_BELTS", "DESCENT", "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn), Set(K.SeatBelts, K.SignOn)),
            // BlueBird's landing-preparation chime: no state, so every tick rings it again (FO-12).
            ActionManual("DS_CABIN", "DESCENT", "Cabin call: landing preparation", Set(K.CabinCall, 1)),
            // FO-14: the landing autobrake is the Captain's; the reminder names where, never a setting.
            Reminder("DS_AUTOBRAKE", "DESCENT", "Set the landing autobrake on the main panel"),
            Reminder("DS_ILS", "DESCENT", "ILS: frequency and course set (NAV radios window, Input Ctrl+N)"),
            Reminder("DS_LANDING_DATA", "DESCENT", "Landing data and approach speeds: checked, minimums set"),
        }
    };

    private static Group BeforeLanding() => new()
    {
        Id = "BEFORE_LANDING", Name = "Before Landing",
        Items = new()
        {
            LeaveAlone(Auto("BL_SPEEDBRAKE", "BEFORE_LANDING", "Speedbrake: ARMED", "FO_SPEEDBRAKE_ARMED", On, Set(X.SpeedbrakeArm, 1)),
                s => s.IsOn("FO_SPEEDBRAKE_EXTENDED"), SpeedbrakeStaysText),
            Auto("BL_LIGHTS", "BEFORE_LANDING", "Taxi light and runway turnoff lights: ON", "FO_GROUND_LIGHTS_ON", On,
                SetAll((K.LeftTurnoff, 1), (K.RightTurnoff, 1), (K.TaxiLight, 1))),
            Reminder("BL_MISSED", "BEFORE_LANDING", "Missed approach altitude: set"),
            // The gear down, three green, is the Before Landing Checklist's line alone (BLC_GEAR).
        }
    };

    private static Group AfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPEEDBRAKE", "AFTER_LANDING", "Speedbrake: DOWN", "FO_SPEEDBRAKE_DOWN", On, Set(X.SpeedbrakeDown, 1)),
            Auto("AL_AUTOBRAKE", "AFTER_LANDING", "Autobrake: OFF", K.Autobrake, v => Is(v, K.AutobrakeOff), Set(K.Autobrake, K.AutobrakeOff)),
            Auto("AL_WXR_TERR", "AFTER_LANDING", "Weather radar and terrain displays: OFF", "FO_WXR_TERR_OFF", On, AllTo(K.TerrainAndWeather, 0)),
            Auto("AL_WING_ANTI_ICE", "AFTER_LANDING", "Wing anti-ice: OFF", K.WingAntiIce, Off, Set(K.WingAntiIce, 0)),
            Auto("AL_LANDING", "AFTER_LANDING", "Landing lights: OFF", "FO_LANDING_LIGHTS_OFF", On,
                SetAll((K.LeftLanding, 0), (K.RightLanding, 0))),
            Auto("AL_STROBES", "AFTER_LANDING", "Strobe lights: OFF", K.StrobeLights, Off, Set(K.StrobeLights, 0)),
            Auto("AL_WING_LIGHTS", "AFTER_LANDING", "Wing lights: OFF", K.WingLights, Off, Set(K.WingLights, 0)),
            Auto("AL_TURNOFF", "AFTER_LANDING", "Runway turnoff lights: OFF", "FO_TURNOFF_LIGHTS_OFF", On,
                SetAll((K.LeftTurnoff, 0), (K.RightTurnoff, 0))),
            Auto("AL_TAXI_LIGHT", "AFTER_LANDING", "Taxi light: ON", K.TaxiLight, On, Set(K.TaxiLight, 1)),
            Auto("AL_FDS", "AFTER_LANDING", "Flight directors: OFF", "FO_FDS_OFF", On, SetAll((K.CaptainFd, 0), (K.FirstOfficerFd, 0))),
            Reminder("AL_FLAPS", "AFTER_LANDING", "Flaps: up"),
            Reminder("AL_TRIMS", "AFTER_LANDING", "Trims: zero, zero, stabilizer 4 units"),
            // Last, as in the flow: the APU started and waited for.
            Auto("AL_APU", "AFTER_LANDING", "APU: running", "FO_APU_RUNNING", On, (e, _) => e.StartApuAndWaitAsync()),
        }
    };

    private static Group Shutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Items = new()
        {
            LeaveAlone(Auto("SD_PARK", "SHUTDOWN", "Parking brake: SET", K.ParkingBrake, On, Set(X.ParkingBrake, 1)),
                NoBrakePressure, BrakeStaysText),
            Auto("SD_ENG_ANTI_ICE", "SHUTDOWN", "Engine anti-ice: OFF", "FO_ENG_ANTI_ICE_OFF", On,
                SetAll((K.LeftEngAntiIce, 0), (K.RightEngAntiIce, 0))),
            // Never airborne, and only with APU or external power on the busses (FO-25).
            LeaveAlone(Auto("SD_FUEL_CUTOFF", "SHUTDOWN", "Fuel control switches: CUTOFF", "FO_FUEL_CONTROLS_CUTOFF", On,
                SetAll((X.FuelControl1, 0), (X.FuelControl2, 0))), NoGroundPower, FuelCutoffStaysText),
            Auto("SD_BEACON", "SHUTDOWN", "Beacon: OFF", K.BeaconLight, Off, Set(K.BeaconLight, 0)),
            Auto("SD_SEAT_BELTS", "SHUTDOWN", "Seat belt signs: OFF", K.SeatBelts, v => Is(v, K.SignOff), Set(K.SeatBelts, K.SignOff)),
            Reminder("SD_STAB_TRIM", "SHUTDOWN", "Stabilizer trim: set before the hydraulics go off"),
            // L, then C, then R (the manual), and never with an engine turning.
            LeaveAlone(Auto("SD_ELEC_HYD", "SHUTDOWN", "Electric hydraulic pumps: OFF, L then C then R", "FO_ELEC_HYD_PUMPS_OFF", On,
                AllTo(new[] { K.LeftElecHyd, K.C2ElecHyd, K.C1ElecHyd, K.RightElecHyd }, 0)), NotParked, HydStaysText),
            LeaveAlone(Auto("SD_FUEL_PUMPS", "SHUTDOWN", "Fuel pumps: OFF", "FO_FUEL_PUMPS_OFF", On, AllTo(AllFuelPumps, 0)),
                NotParked, PumpsStayText),
            Auto("SD_WING_ANTI_ICE", "SHUTDOWN", "Wing anti-ice: OFF", K.WingAntiIce, Off, Set(K.WingAntiIce, 0)),
            Auto("SD_WIPERS", "SHUTDOWN", "Wipers: OFF", K.Wipers, v => Is(v, 0), Set(K.Wipers, 0)),
            Auto("SD_WINDOW_HEAT", "SHUTDOWN", "Window heat: OFF", "FO_WINDOW_HEAT_OFF", On, AllTo(K.WindowHeat, 0)),
            Auto("SD_ISOLATION", "SHUTDOWN", "Isolation valve: OPEN", "FO_ISOLATION_OPEN", On, Set(K.Isolation, Bbs757FoStateEvaluator.IsolationOpen)),
            LeaveAlone(Auto("SD_APU_BLEED", "SHUTDOWN", "APU bleed: ON", K.ApuBleed, On, Set(K.ApuBleed, 1)),
                s => !s.IsOn("FO_APU_RUNNING"), ApuBleedStaysText),
            Auto("SD_FDS", "SHUTDOWN", "Flight directors: OFF", "FO_FDS_OFF", On, SetAll((K.CaptainFd, 0), (K.FirstOfficerFd, 0))),
            Auto("SD_HEATERS", "SHUTDOWN", "Foot and shoulder heaters: OFF", "FO_HEATERS_OFF", On, AllTo(K.Heaters, K.HeaterOff)),
            Auto("SD_TURNOFF", "SHUTDOWN", "Runway turnoff lights: OFF", "FO_TURNOFF_LIGHTS_OFF", On,
                SetAll((K.LeftTurnoff, 0), (K.RightTurnoff, 0))),
            Auto("SD_EXT_LIGHTS", "SHUTDOWN", "Exterior lights: position only, wing, logo and taxi lights OFF", "FO_PARKED_LIGHTS_OFF", On,
                SetAll((K.WingLights, 0), (K.LogoLights, 0), (K.TaxiLight, 0))),
            Auto("SD_XPDR", "SHUTDOWN", "Transponder: STBY", K.TcasMode, v => Is(v, K.TcasStandby), Set(K.TcasMode, K.TcasStandby)),
        }
    };

    private static Group Secure() => new()
    {
        Id = "SECURE", Name = "Final Securing",
        Items = new()
        {
            LeaveAlone(Auto("SC_IRS", "SECURE", "IRS selectors: OFF", "FO_IRS_ALL_OFF", On, AllTo(K.IrsSwitches, K.IrsOff)),
                NotParked, IrsStaysText),
            Auto("SC_EMER_LTS", "SECURE", "Emergency lights: OFF", K.EmergencyLights, v => Is(v, 0), Set(K.EmergencyLights, 0)),
            Auto("SC_APU", "SECURE", "APU: OFF", "FO_APU_OFF", On, Set(K.ApuSelector, 0)),
            Auto("SC_EXT_PWR", "SECURE", "External power: OFF", "FO_EXT_POWER_OFF", On, Set(X.ExtPower, 0)),
            LeaveAlone(Auto("SC_BATTERY", "SECURE", "Battery: OFF", K.Battery, Off, Set(K.Battery, 0)), BatteryMustStay, BatteryStaysText),
        }
    };

    // ================================================================== BlueBird Normal Checklist (read-back)

    private static Group BeforeStartCL()
    {
        const string G = "BEFORE_START_CL";
        return new Group
        {
            Id = G, Name = "Before Start Checklist",
            Items = new()
            {
                Read("BSC_PARK", G, "Parking brake: SET", K.ParkingBrake, On),
                Reminder("BSC_FUEL_QTY", G, "Fuel quantity: CHECKED AGAINST PLAN"),
                Reminder("BSC_OXYGEN", G, "Oxygen: CHECKED"),
                Read("BSC_IRS", G, "IRS: ALIGNED", "FO_IRS_ALIGNED", On),
                Reminder("BSC_ALTIMETERS", G, "Altimeters: SET"),
                Reminder("BSC_RECALL", G, "Recall: CHECKED"),
                Read("BSC_DOORS", G, "Doors: CLOSED", "FO_DOORS_CLOSED", On),
                Reminder("BSC_WINDOWS", G, "Windows: CLOSED AND LOCKED"),
                Read("BSC_HYD", G, "Hydraulic panel: SET", "FO_HYD_PANEL_SET", On),
                Read("BSC_FUEL", G, "Fuel panel: SET", "FO_FUEL_PANEL_SET", On),
                Read("BSC_SEAT_BELTS", G, "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn)),
                Reminder("BSC_DUCT", G, "Duct pressure: NORMAL"),
                Read("BSC_BEACON", G, "Beacon: ON", K.BeaconLight, On),
                // Not on BlueBird's card: the owners' last line on every profile (ACARS is the Captain's).
                Reminder("BSC_ACARS", G, "Start ACARS"),
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
                Reminder("ASC_APU", G, "APU: AS REQUIRED"),
                Reminder("ASC_ENG_ANTI_ICE", G, "Engine anti-ice: AS REQUIRED"),
                Read("ASC_AIRCON", G, "Air conditioning panel: SET", "FO_AIRCON_SET", On),
                Read("ASC_FLAPS", G, "Flaps: SET", "FO_FLAPS_TAKEOFF", On),
                Reminder("ASC_TRIM", G, "Trim: SET"),
                Reminder("ASC_RECALL", G, "Recall: CHECKED"),
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
                Read("BTC_FLAPS", G, "Flaps: CHECKED", "FO_FLAPS_TAKEOFF", On),
                Reminder("BTC_FLT_CTRL", G, "Flight controls: CHECKED"),
                Reminder("BTC_TRIMS", G, "Trims: zero, zero, stabilizer set"),
                Read("BTC_AUTOBRAKE", G, "Autobrakes: RTO", K.Autobrake, v => Is(v, K.AutobrakeRto)),
                Reminder("BTC_BRIEFING", G, "Briefing: COMPLETE"),
                Reminder("BTC_CREW_BELTS", G, "Crew seat belts and harnesses: FASTENED"),
                Read("BTC_SEAT_BELTS", G, "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn)),
                Reminder("BTC_CABIN", G, "Cabin: SECURE"),
                // Read from the engines themselves: both running.
                Read("BTC_ENGINES", G, "Engine indications: NORMAL, both running", "FO_ENGINES_RUNNING", On),
                Reminder("BTC_CONFIG", G, "Takeoff configuration: CHECKED"),
                Reminder("BTC_VSPEEDS", G, "V speeds: CHECKED"),
                Reminder("BTC_MCP", G, "MCP: SELECTED"),
                Reminder("BTC_WXR", G, "Weather radar and terrain: SELECTED"),
                Read("BTC_LIGHTS", G, "Lights: ON", "FO_LANDING_LIGHTS_ON", On),
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
                // The lever AND the legs (FO-13), confirmed by After Takeoff's last step.
                Read("ATC_GEAR", G, "Gear: UP, THEN OFF", "FO_GEAR_UP_OFF", On),
                Read("ATC_FLAPS", G, "Flaps: UP", "FO_FLAPS_UP", On),
            }
        };
    }

    /// <summary>
    /// BlueBird's In Range and Approach cards as ONE checklist (owner, 2026-11-08: the arrival condensed to two flows, as
    /// on the MD-11), in the cards' order; the Approach card's "Altimeters: SET" is the In Range card's, said once.
    /// </summary>
    private static Group DescentCL()
    {
        const string G = "DESCENT_CL";
        return new Group
        {
            Id = G, Name = "Descent and Approach Checklist",
            Items = new()
            {
                // In Range.
                Reminder("IRC_REPORT", G, "In Range report: SENT"),
                Reminder("IRC_ALTIMETERS", G, "Altimeters: SET"),
                Reminder("IRC_LANDING_DATA", G, "Landing data: CHECKED"),
                Reminder("IRC_AUTOBRAKES", G, "Autobrakes: CHECKED"),
                Reminder("IRC_RECALL", G, "Recall: CHECKED"),
                Read("IRC_LIGHTS", G, "Lights: ON", "FO_LANDING_LIGHTS_ON", On),
                Reminder("IRC_CREW_BELTS", G, "Crew seat belts and harnesses: FASTENED"),
                // Approach.
                Reminder("APC_BRIEFING", G, "Briefing: COMPLETE"),
                Reminder("APC_MINIMUMS", G, "Minimums: SET, radio or baro"),
                Reminder("APC_NAVAIDS", G, "Navaids: CHECKED"),
            }
        };
    }

    private static Group BeforeLandingCL()
    {
        const string G = "BEFORE_LANDING_CL";
        return new Group
        {
            Id = G, Name = "Before Landing Checklist",
            Items = new()
            {
                Read("BLC_SPEEDBRAKE", G, "Speed brakes: ARMED", "FO_SPEEDBRAKE_ARMED", On),
                // The lever AND the legs (FO-13), confirmed by Before Landing's last step.
                Read("BLC_GEAR", G, "Gear: DOWN, 3 GREEN", "FO_GEAR_DOWN", On),
                // Read from the flap lever: 25 or 30 (E181).
                Read("BLC_FLAPS", G, "Flaps: CHECKED, 25 or 30", "FO_FLAPS_LANDING", On),
                Read("BLC_SEAT_BELTS", G, "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn)),
                Reminder("BLC_CABIN", G, "Cabin: SECURE"),
            }
        };
    }

    private static Group AfterLandingCL()
    {
        const string G = "AFTER_LANDING_CL";
        return new Group
        {
            Id = G, Name = "After Landing Checklist",
            Items = new()
            {
                Reminder("ALC_APU", G, "APU: AS REQUIRED"),
                Reminder("ALC_LIGHTS", G, "Lights: SET"),
                Read("ALC_WXR", G, "Weather radar and terrain: DESELECTED", "FO_WXR_TERR_OFF", On),
                Reminder("ALC_ILS", G, "ILS control panel: PARKED"),
                Read("ALC_FLAPS", G, "Flaps: UP", "FO_FLAPS_UP", On),
                Read("ALC_AUTOBRAKES", G, "Autobrakes: OFF", K.Autobrake, v => Is(v, K.AutobrakeOff)),
                Read("ALC_SPEEDBRAKE", G, "Speed brakes: DOWN", "FO_SPEEDBRAKE_DOWN", On),
                Reminder("ALC_STAB_TRIM", G, "Stabilizer trim: 4 UNITS"),
                Reminder("ALC_ENG_ANTI_ICE", G, "Engine anti-ice: AS REQUIRED"),
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
                Read("SDC_ENG_ANTI_ICE", G, "Engine anti-ice: OFF", "FO_ENG_ANTI_ICE_OFF", On),
                Read("SDC_FUEL", G, "Fuel control switches: CUTOFF", "FO_FUEL_CONTROLS_CUTOFF", On),
                Read("SDC_BEACON", G, "Beacon: OFF", K.BeaconLight, Off),
                Read("SDC_SEAT_BELTS", G, "Seat belt signs: OFF", K.SeatBelts, v => Is(v, K.SignOff)),
                Read("SDC_HYD", G, "Hydraulic panel: SET", "FO_ELEC_HYD_PUMPS_OFF", On),
                Reminder("SDC_EMER_LTS", G, "Emergency lights: AS REQUIRED"),
                Read("SDC_FUEL_PUMPS", G, "Fuel pumps: OFF", "FO_FUEL_PUMPS_OFF", On),
                Read("SDC_WINDOW_HEAT", G, "Window heat: OFF", "FO_WINDOW_HEAT_OFF", On),
                Reminder("SDC_PACKS", G, "Packs: AS REQUIRED"),
                Read("SDC_ISOLATION", G, "Isolation valve: OPEN", "FO_ISOLATION_OPEN", On),
                Read("SDC_FDS", G, "Flight directors: OFF", "FO_FDS_OFF", On),
                Reminder("SDC_EICAS", G, "EICAS messages: CHECKED"),
                Reminder("SDC_STATUS", G, "Status messages: CHECKED"),
                Read("SDC_HEATERS", G, "Foot and shoulder heaters: OFF", "FO_HEATERS_OFF", On),
                Reminder("SDC_EFB", G, "EFB: SHUTDOWN WHEN FINISHED"),
                Reminder("SDC_IRS", G, "IRS: OFF FOR TERMINATION"),
                Reminder("SDC_DEBRIEF", G, "Debrief: COMPLETE"),
                Reminder("SDC_SECURING", G, "Airplane securing: AS REQUIRED"),
            }
        };
    }

    // ================================================================== builders (the MD-11's)

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

    /// <summary>A read-back (*_CL) item: auto-detected, action-free.</summary>
    private static Item Read(string id, string groupId, string label, string field, Func<double, bool> condition)
        => Auto(id, groupId, label, field, condition, action: null);

    /// <summary>The ONE sanctioned StayComplete (FOB-1): the engine start selectors, latched on the engine running.</summary>
    private static Item AutoLatch(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn action)
    {
        var item = Auto(id, groupId, label, field, condition, action);
        item.RevertBehavior = RevertBehavior.StayComplete;
        return item;
    }

    /// <summary>A hand-ticked line that acts and reads nothing (the FBW A320's): a self-test, run on every tick (FO-12).</summary>
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

    /// <summary>A line whose hand-tick the First Officer refuses in this state (ChecklistItem.LeaveAloneWhen), speaking <paramref name="text"/> instead.</summary>
    private static Item LeaveAlone(Item item, Func<Bbs757FoStateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }

    // FO-25: on the ground with both engines read stopped; airborne, an engine turning, or either
    // unread, fuel, IRS, hydraulics and battery stay as they are, and no preflight test is run.
    internal static bool NotParked(Bbs757FoStateEvaluator s) =>
        !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn("FO_ENGINES_STOPPED");

    /// <summary>A start selector line's latch: the engine's N2 past the starter (both variants, FOB-1).</summary>
    internal static bool EngineLit(double n2) => !double.IsNaN(n2) && n2 >= Bbs757FoStateEvaluator.EngineRunningN2;

    // Packs OFF only on the ground (FO-25): airborne or unread, the packs stay as they are.
    internal static bool NotOnGround(Bbs757FoStateEvaluator s) => !s.IsPosition("SIM_ON_GROUND", 1);

    // The APU off only with another source on the busses: both engines running, or external power on.
    internal static bool NoOtherPower(Bbs757FoStateEvaluator s) => !s.IsOn("FO_ENGINES_RUNNING") && !s.IsOn("FO_EXT_POWER_ON");

    /// <summary>
    /// The battery stays ON (E199) unless parked with the engines stopped (FO-25), the APU reads stopped
    /// (<c>B757_APU_STATE</c> 0: never the battery to stop it) and external power reads off. Anything unread keeps it.
    /// </summary>
    internal static bool BatteryMustStay(Bbs757FoStateEvaluator s) =>
        NotParked(s) || !s.IsPosition(K.ApuState, 0) || !s.IsOn("FO_EXT_POWER_OFF");

    /// <summary>
    /// No pressure to set the parking brake with (owner, 2026-11-08: it "doesn't seem to fully work unless the electric
    /// hydraulic pumps are on", as on the iFly): neither the left nor the right electric hydraulic pump (the 757's
    /// alternate and normal brake systems; the center pumps feed no brakes) reads ON, and no engine reads running.
    /// Unread is no pressure. The First Officer never turns a pump on just to set the brake.
    /// </summary>
    internal static bool NoBrakePressure(Bbs757FoStateEvaluator s) =>
        !s.IsOn(K.LeftElecHyd) && !s.IsOn(K.RightElecHyd) && !s.IsOn("FO_ENG1_RUNNING") && !s.IsOn("FO_ENG2_RUNNING");

    internal const string BrakeStaysText = "Parking brake left released: no hydraulic pressure. Turn on the electric hydraulic pumps first.";

    // The shutdown cut needs the ground and APU or external power on the busses.
    internal static bool NoGroundPower(Bbs757FoStateEvaluator s) =>
        !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn("FO_APU_OR_EXT_POWER_ON");

    internal const string FuelStaysText = "Fuel control switches left as they are: not read on the ground, or an engine is not read stopped.";
    internal const string FuelCutoffStaysText = "Fuel control switches left on: not on the ground, or no APU or external power on line.";
    internal const string HydStaysText = "Electric hydraulic pumps left as they are: not read on the ground, or an engine is not read stopped.";
    internal const string PumpsStayText = "Fuel pumps left on: not read on the ground, or an engine is not read stopped.";
    internal const string IrsStaysText = "IRS left on: not read on the ground, or an engine is not read stopped.";
    internal const string BatteryStaysText = "Battery left on: not read on the ground, an engine or the APU not read stopped, or external power on.";
    internal const string ExtPowerStaysText = "External power left on: the APU is not on line.";
    internal const string ApuBleedStaysText = "APU bleed left as it is: the APU is not running.";
    internal const string PacksStayText = "Packs left on: not read on the ground.";
    internal const string ApuStaysText = "APU left running: neither both engines nor external power read on line.";
    internal const string SpeedbrakeStaysText = "Speedbrake extended, not armed. Left as it is.";
    internal const string TestsStayText = "Tests left alone: not read on the ground with the engines stopped.";
}
