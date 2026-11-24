using MSFSBlindAssist.FirstOfficer.Models;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using X = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.BBS757;

using Flow = Models.FlowDefinition<Bbs757FoStateEvaluator>;
using Step = Models.FlowStep<Bbs757FoStateEvaluator>;

/// <summary>
/// The BlueBird 757 First Officer flows, in the order of BlueBird's simplified procedures (manual
/// v1.00, part 3A). Every switch step carries a SkipCondition on its own state ("Already set"), and
/// the executor re-reads before acting anyway. Captain steps are spoken reminders, never automated:
/// the First Officer never moves the flap lever, the gear lever or the thrust levers, never sets the
/// landing autobrake (FO-14) and never programs the FMC (FO-2).
///
/// Every read-back (*_CL) line a flow latches has a delivering step (FO-6, FO-7): a write completes
/// its action line and the read-back line it sets (<see cref="Also"/>), and a read-only
/// <see cref="Check"/> completes a line no write of the flow sets. Pinned by Bbs757FoFlowTests.
///
/// The engine start follows BlueBird's own criteria: start selector GND, fuel on at the variant's
/// fuel point (Pratt &amp; Whitney about 25 % N2; Rolls-Royce about 25 % N3, its stock N2, with the EGT
/// below 100 °C), the starter released (the selector back in AUTO at cut-out) before anything else,
/// then the engine running. Right engine first. Measured on the PW (2026-10-27; docs/b757.md, "Flows"): N2
/// reaches 25 % about 25 s after GND, the selector releases about 29 s after fuel, idle 59.5 %.
/// </summary>
public static class Bbs757FoFlowDefinitions
{
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
        BuildShutdown(),
        BuildSecure(),
    };

    private const int CheckSeconds = 5;
    private const int IrsAlignSeconds = 600;
    private const int GearCheckSeconds = 20;
    private const int FlapsUpCheckSeconds = 300;
    private const int CaptainFlapsSeconds = 120;
    private const int FuelPointSeconds = 90;
    private const int EngineRunSeconds = 60;
    private const int ApuStartSeconds = 150;
    private const int EnginesStopSeconds = 120;
    private const int ApuStopSeconds = 150;

    /// <summary>The APU's cool-down after its switch goes OFF: 2 minutes, as on both 737s (owner, 2026-11-08).</summary>
    internal const int ApuCooldownSeconds = 120;

    private static bool On(double v) => v > 0.5;
    private static bool Is(double v, double p) => Bbs757FoStateEvaluator.Pos(v, p);

    // ------------------------------------------------------------------ Power Up (Cockpit Safety Inspection + Establishing Power)
    private static Flow BuildPowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Description = "Gear lever down, fuel controls cutoff, electric hydraulic pumps off, battery, standby power, electrical panel, external power or else the APU, IRS to NAV, position lights.",
        RelatedChecklistGroupIds = new[] { "POWER_UP" },
        Steps = new()
        {
            EnginesStoppedGuard("PU_ENGINES_STOPPED"),
            OnGroundGuard("PU_ON_GROUND"),
            Check("PU_CHK_GEAR", "Landing gear lever: DOWN", "FO_GEAR_DOWN", On, CheckSeconds, "PU_GEAR"),
            Done(Skip(Multi("PU_FUEL_CUTOFF", "Fuel control switches: CUTOFF", (X.FuelControl1, 0), (X.FuelControl2, 0)),
                s => s.IsOn("FO_FUEL_CONTROLS_CUTOFF")), "PU_FUEL_CUTOFF"),
            Done(Skip(Multi("PU_ELEC_HYD_OFF", "Electric hydraulic pumps: OFF",
                    (K.LeftElecHyd, 0), (K.C2ElecHyd, 0), (K.C1ElecHyd, 0), (K.RightElecHyd, 0)),
                s => s.IsOn("FO_ELEC_HYD_PUMPS_OFF")), "PU_ELEC_HYD_OFF"),
            Done(Skip(SW("PU_BATTERY", "Battery: ON", K.Battery, 1), s => s.IsOn(K.Battery)), "PU_BATTERY"),
            Done(Skip(SW("PU_STBY_POWER", "Standby power: AUTO", K.StandbyPower, 1), s => s.IsPosition(K.StandbyPower, 1)), "PU_STBY_POWER"),
            Done(Skip(Multi("PU_ELEC_PANEL", "Bus ties, generator and utility bus controls: ON",
                    Bbs757FoChecklistDefinitions.ElecPanel.Select(k => (k, (int?)1)).ToArray()),
                s => s.IsOn("FO_ELEC_PANEL_SET")), "PU_ELEC_PANEL"),
            // External power only when the GPU is connected (AVAIL lit). Without one the line is LEFT
            // ALONE, never "Already set" (that would latch "External power: ON" over power that is off).
            Done(LeaveAlone(Skip(SW("PU_EXT_PWR", "External power: ON", X.ExtPower, 1), s => s.IsOn("FO_EXT_POWER_ON")),
                s => !s.IsOn("FO_EXT_POWER_ON") && !s.IsOn(K.ExtPwrAvail), "External power left off: no ground power available."), "PU_EXT_PWR"),
            // No external power on line: the APU instead (S36 #8-9, "APU-power option"; a battery-only start needs no
            // fuel pump, E71), then its wait (FO-11). With external power on, the APU is not needed.
            LeaveAlone(Skip(SW("PU_APU", "APU: start", X.ApuStart, 1), s => s.IsOn("FO_APU_RUNNING")),
                s => s.IsOn("FO_EXT_POWER_ON"), "APU not started: external power is on line."),
            RunOnlyIfSent(WaitForField("PU_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ApuStartSeconds), "PU_APU"),
            // Neither worked: the Captain is asked.
            Skip(Captain("PU_EXT_PWR_CPT", "Ground power",
                    "No external power and the APU is not running. Connect the GPU on the EFB Ground Services page, or start the APU."),
                s => s.IsOn("FO_EXT_POWER_ON") || s.IsOn("FO_APU_RUNNING")),
            Done(Skip(Multi("PU_IRS", "IRS selectors: NAV",
                    (K.IrsSwitches[0], (int)K.IrsNav), (K.IrsSwitches[1], (int)K.IrsNav), (K.IrsSwitches[2], (int)K.IrsNav)),
                s => s.IsOn("FO_IRS_ALL_NAV")), "PU_IRS"),
            Done(Skip(SW("PU_POSITION", "Position lights: ON", K.PositionLights, 1), s => s.IsOn(K.PositionLights)), "PU_POSITION"),
        }
    };

    // ------------------------------------------------------------------ Preflight (overhead, MCP, main panel)
    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Overhead left to right: EEC (Rolls-Royce), yaw dampers, engine hydraulic pumps, emergency lights armed, ignition, start selectors, fuel, anti-ice, lights (anti-collision, wing, landing, runway turnoff and taxi off), window heat, signs, pressurization, air; then the MCP and autobrake RTO; then BlueBird's tests, shortest first: both stall warnings, CVR, the takeoff configuration warning, the wheel well, engine, APU and cargo fire tests, both crew oxygen masks, TCAS and ground proximity.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT" },
        Steps =
        [
            OnGroundGuard("PF_ON_GROUND"),
            EnginesStoppedGuard("PF_ENGINES_STOPPED"),
            Done(Skip(SW("PF_ENGINE_CONTROLS", "Engine limiters and, on a Rolls-Royce, EEC: ON", X.EngineControls, 1),
                s => s.IsOn("FO_ENGINE_CONTROLS_ON")), "PF_ENGINE_CONTROLS"),
            Done(Skip(Multi("PF_YAW_DAMPERS", "Yaw dampers: ON", (K.LeftYawDamper, 1), (K.RightYawDamper, 1)),
                s => s.IsOn("FO_YAW_DAMPERS_ON")), "PF_YAW_DAMPERS"),
            Done(Skip(Multi("PF_ENG_HYD", "Engine hydraulic pumps: ON", (K.LeftEngHyd, 1), (K.RightEngHyd, 1)),
                s => s.IsOn("FO_ENG_HYD_PUMPS_ON")), "PF_ENG_HYD"),
            Done(Skip(SW("PF_EMER_LTS", "Emergency lights: ARMED", K.EmergencyLights, 1), s => s.IsPosition(K.EmergencyLights, 1)), "PF_EMER_LTS"),
            Done(Skip(SW("PF_IGNITION", "Ignition: 1", K.Ignition, 0), s => s.GetValue(K.Ignition) is var v && !double.IsNaN(v) && v < 1.5), "PF_IGNITION"),
            Done(Skip(Multi("PF_START_SEL", "Engine start selectors: AUTO", (K.LeftStart, (int)K.StartAuto), (K.RightStart, (int)K.StartAuto)),
                s => s.IsOn("FO_START_SELECTORS_AUTO")), "PF_START_SEL"),
            Done(Skip(Multi("PF_XFEED", "Fuel crossfeed: OFF", (K.XfeedFwd, 0), (K.XfeedAft, 0)), s => s.IsOn("FO_CROSSFEEDS_OFF")), "PF_XFEED"),
            Done(LeaveAlone(Skip(Multi("PF_FUEL_PUMPS", "Fuel pumps: OFF",
                    Bbs757FoChecklistDefinitions.AllFuelPumps.Select(k => (k, (int?)0)).ToArray()),
                s => s.IsOn("FO_FUEL_PUMPS_OFF")), Bbs757FoChecklistDefinitions.NotParked, Bbs757FoChecklistDefinitions.PumpsStayText), "PF_FUEL_PUMPS"),
            Done(Skip(Multi("PF_ANTI_ICE", "Wing and engine anti-ice: OFF", (K.WingAntiIce, 0), (K.LeftEngAntiIce, 0), (K.RightEngAntiIce, 0)),
                s => s.IsOn("FO_ANTI_ICE_OFF")), "PF_ANTI_ICE"),
            Done(Skip(SW("PF_WIPERS", "Wipers: OFF", K.Wipers, 0), s => s.IsPosition(K.Wipers, 0)), "PF_WIPERS"),
            Done(Skip(Multi("PF_LIGHTS_OFF", "Anti-collision, wing, landing, runway turnoff and taxi lights: OFF",
                    (K.BeaconLight, 0), (K.StrobeLights, 0), (K.WingLights, 0), (K.LeftLanding, 0), (K.RightLanding, 0),
                    (K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 0)),
                s => s.IsOn("FO_PREFLIGHT_LIGHTS_OFF")), "PF_LIGHTS_OFF"),
            Done(Skip(Multi("PF_WINDOW_HEAT", "Window heat: ON", K.WindowHeat.Select(k => (k, (int?)1)).ToArray()),
                s => s.IsOn("FO_WINDOW_HEAT_ON")), "PF_WINDOW_HEAT"),
            Done(Skip(SW("PF_NO_SMOKING", "No smoking signs: ON", K.NoSmoking, (int)K.SignOn), s => s.IsPosition(K.NoSmoking, (int)K.SignOn)), "PF_NO_SMOKING"),
            Done(Skip(SW("PF_PRESS_MODE", "Pressurization mode: AUTO 1", K.PressMode, 0),
                s => s.GetValue(K.PressMode) is var v && !double.IsNaN(v) && v < 1.5), "PF_PRESS_MODE"),
            // The cabin altitude controls' landing elevation (S40 #45) is the destination's: the Captain's.
            Done(Captain("PF_LANDING_ALT", "Landing altitude",
                "Set the landing altitude: the destination elevation, in the Pressurization panel's Landing Altitude field."), "PF_LANDING_ALT"),
            Done(Skip(Multi("PF_TRIM_AIR", "Trim air and recirculation fans: ON", (K.TrimAir, 1), (K.LeftRecirc, 1), (K.RightRecirc, 1)),
                s => s.IsOn("FO_TRIM_AIR_RECIRC_ON")), "PF_TRIM_AIR"),
            Done(Skip(SW("PF_ISOLATION", "Isolation valve: OPEN", K.Isolation, (int)Bbs757FoStateEvaluator.IsolationOpen),
                s => s.IsOn("FO_ISOLATION_OPEN")), "PF_ISOLATION"),
            Done(Skip(Multi("PF_BLEEDS", "Engine and APU bleeds: ON", (K.LeftEngBleed, 1), (K.RightEngBleed, 1), (K.ApuBleed, 1)),
                s => s.IsOn("FO_ALL_BLEEDS_ON")), "PF_BLEEDS"),
            Done(Skip(Multi("PF_FDS", "Flight directors: ON", (K.CaptainFd, 1), (K.FirstOfficerFd, 1)), s => s.IsOn("FO_FDS_ON")), "PF_FDS"),
            // Judged by BlueBird's own A/T flag, not the switch (R40); a switch at ARM over a disarmed A/T is cycled.
            Done(Skip(SW("PF_AT_ARM", "Autothrottle: ARM", X.ArmAutothrottle, 1), s => s.IsOn("FO_AT_ARMED")), "PF_AT_ARM"),
            Done(Skip(SW("PF_BANK_LIMIT", "Bank limit: AUTO", K.BankLimit, 0), s => s.IsPosition(K.BankLimit, 0)), "PF_BANK_LIMIT"),
            Done(Skip(SW("PF_AUTOBRAKE", "Autobrake: RTO", K.Autobrake, (int)K.AutobrakeRto), s => s.IsPosition(K.Autobrake, (int)K.AutobrakeRto)), "PF_AUTOBRAKE"),
            // BlueBird's tests shortest first (Bbs757FoTests), each a press of its own key and a wait for what it sounds; one that cannot
            // be pressed is skipped aloud, never stops the flow. A self-test has no state: never "Already set".
            .. Bbs757FoTests.Preflight.Select(t => Done(SW(Bbs757FoTests.PreflightId(t), t.Label, t.Key, 1), Bbs757FoTests.PreflightId(t))),
            Done(Captain("PF_FMS", "FMS", "Load the FMS route, winds, performance and takeoff data."), "PF_FMS"),
            Done(Captain("PF_ALTIMETERS", "Altimeters", "Set the altimeters."), "PF_ALTIMETERS"),
        ]
    };

    // ------------------------------------------------------------------ Before Start
    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "Doors, electric hydraulic pumps R then C then L, then the parking brake (it needs their pressure), fuel pumps, seat belts, APU running, external power off once the APU is on line, APU bleed, transponder TA/RA, beacon and logo lights on, IRS aligned; the MCP, ACARS and the clearance are the Captain's.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START", "BEFORE_START_CL" },
        Steps = new()
        {
            OnGroundGuard("BS_ON_GROUND"),
            // A check only read: the checklist's line alone, never repeated in the action group (owner, 2026-11-08).
            Check("BS_CHK_DOORS", "Doors: CLOSED", "FO_DOORS_CLOSED", On, CheckSeconds, "BSC_DOORS"),
            Done(Skip(Multi("BS_HYD", "Electric hydraulic pumps: ON, R then C then L",
                    (K.RightElecHyd, 1), (K.C1ElecHyd, 1), (K.C2ElecHyd, 1), (K.LeftElecHyd, 1)),
                s => s.IsOn("FO_ELEC_HYD_PUMPS_ON")), "BS_HYD"),
            Check("BS_CHK_HYD", "Hydraulic panel: SET", "FO_HYD_PANEL_SET", On, CheckSeconds, "BSC_HYD"),
            // After the pumps (owner, 2026-11-08: the brake does not fully set without them); never sent without pressure.
            Also(Done(LeaveAlone(Skip(SW("BS_PARK", "Parking brake: SET", X.ParkingBrake, 1), s => s.IsOn(K.ParkingBrake)),
                Bbs757FoChecklistDefinitions.NoBrakePressure, Bbs757FoChecklistDefinitions.BrakeStaysText), "BS_PARK"), "BSC_PARK"),
            // Center pumps only with center fuel: a provider that knows nothing skips aloud the
            // center half (FO-20); the wing half is its own step.
            Skip(Multi("BS_WING_PUMPS", "Wing fuel pumps: ON",
                    (K.LeftFwdPump, 1), (K.LeftAftPump, 1), (K.RightFwdPump, 1), (K.RightAftPump, 1)),
                s => s.IsOn(K.LeftFwdPump) && s.IsOn(K.LeftAftPump) && s.IsOn(K.RightFwdPump) && s.IsOn(K.RightAftPump)),
            Skip(Provider("BS_CTR_PUMP_L", "Center left fuel pump", K.CenterLeftPump, CenterPumpTarget),
                s => CenterPumpTarget(s) is int t && s.IsPosition(K.CenterLeftPump, t)),
            Skip(Provider("BS_CTR_PUMP_R", "Center right fuel pump", K.CenterRightPump, CenterPumpTarget),
                s => CenterPumpTarget(s) is int t && s.IsPosition(K.CenterRightPump, t)),
            // The whole panel, read: the only step that ticks the fuel lines (wing pumps, center pumps by fuel, crossfeed off).
            Check("BS_CHK_FUEL", "Fuel panel: SET", "FO_FUEL_PANEL_SET", On, CheckSeconds, "BS_FUEL", "BSC_FUEL"),
            Also(Done(Skip(SW("BS_SEAT_BELTS", "Seat belt signs: ON", K.SeatBelts, (int)K.SignOn), s => s.IsPosition(K.SeatBelts, (int)K.SignOn)),
                "BS_SEAT_BELTS"), "BSC_SEAT_BELTS"),
            // FO-11: the start returns once the APU reads starting; the flow then WAITS for it to run (about 80 s,
            // measured), so Stop and Pause reach the wait. No APU, no start air: a start that never runs stops here.
            Skip(SW("BS_APU", "APU: start", X.ApuStart, 1), s => s.IsOn("FO_APU_RUNNING")),
            Done(Skip(WaitForField("BS_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ApuStartSeconds, FlowStepFailurePolicy.Stop),
                s => s.IsOn("FO_APU_RUNNING")), "BS_APU"),
            // The transfer to the APU (S36 #10-11): EXT PWR deselected only with the APU on line, then the GPU and
            // what a source change can delete (E100, E158) are the Captain's, only after a transfer THIS run made.
            Done(LeaveAlone(Skip(SW("BS_EXT_PWR_OFF", "External power: OFF", X.ExtPower, 0), s => s.IsOn("FO_EXT_POWER_OFF")),
                s => !s.IsOn("FO_APU_ON_LINE"), Bbs757FoChecklistDefinitions.ExtPowerStaysText), "BS_EXT_PWR_OFF"),
            RunOnlyIfSent(Done(Captain("BS_GPU", "Disconnect the GPU",
                "Disconnect the GPU on the EFB; recheck TAKEOFF REF speeds and autobrake."), "BS_GPU"), "BS_EXT_PWR_OFF"),
            Done(Skip(SW("BS_APU_BLEED", "APU bleed: ON", K.ApuBleed, 1), s => s.IsOn(K.ApuBleed)), "BS_APU_BLEED"),
            Done(Skip(SW("BS_XPDR", "Transponder: TA/RA", K.TcasMode, (int)K.TcasTaRa), s => s.IsPosition(K.TcasMode, (int)K.TcasTaRa)), "BS_XPDR"),
            Also(Done(Skip(SW("BS_BEACON", "Beacon: ON", K.BeaconLight, 1), s => s.IsOn(K.BeaconLight)), "BS_BEACON"), "BSC_BEACON"),
            Done(Skip(SW("BS_LOGO", "Logo lights: ON", K.LogoLights, 1), s => s.IsOn(K.LogoLights)), "BS_LOGO"),
            // Last before the Captain's items, so the rest of the flow runs while the IRS aligns (about 10 min;
            // a 5 s check here failed every normal start from cold, live 2026-10-28).
            Check("BS_CHK_IRS", "IRS: ALIGNED", "FO_IRS_ALIGNED", On, IrsAlignSeconds, "BSC_IRS"),
            // The 737s' "Set MCP airspeed, heading and initial altitude".
            Done(Captain("BS_MCP", "MCP", "Set the MCP speed, heading and initial altitude."), "BS_MCP"),
            // The Captain's, last on every profile (FoChecklistRefinementTests).
            Also(Done(Captain("BS_ACARS", "Start ACARS", "Start ACARS: on the CDU, MENU, then ACARS."), "BS_ACARS"), "BSC_ACARS"),
            Done(Captain("BS_CLEARANCE", "Obtain pushback and start clearance"), "BS_CLEARANCE"),
        }
    };

    /// <summary>A center pump's target: ON with center fuel, OFF without; null (silent skip, FO-20) while the tank is unread.</summary>
    internal static int? CenterPumpTarget(Bbs757FoStateEvaluator s)
    {
        double v = s.GetValue("FO_CENTER_FUEL_FOR_PUMPS");
        return double.IsNaN(v) ? null : v > 0.5 ? 1 : 0;
    }

    // ------------------------------------------------------------------ Engine Start (APU air, right engine first)
    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Packs off, bleeds on, isolation valve open, APU running; then each engine, right first: start selector GND, fuel control RUN at the fuel point, light-off, the starter released, the engine running. A start with no light-off or no cut-out is aborted.",
        RelatedChecklistGroupIds = new[] { "ENGINE_START" },
        Steps = EngineStartSteps(),
    };

    private static List<Step> EngineStartSteps()
    {
        var steps = new List<Step>
        {
            OnGroundGuard("ES_ON_GROUND"),
            // Never a start on an N2 the window has not read yet: every wait below reads it.
            WaitForField("ES_N2_DATA", "Engine N2 being read", "FO_N2_READ", On, CheckSeconds, FlowStepFailurePolicy.Stop),
            Done(LeaveAlone(Skip(Multi("ES_PACKS_OFF", "Packs: OFF", (K.LeftPackSelector, (int)K.PackOff), (K.RightPackSelector, (int)K.PackOff)),
                s => s.IsOn("FO_PACKS_OFF")), Bbs757FoChecklistDefinitions.NotOnGround, Bbs757FoChecklistDefinitions.PacksStayText), "ES_PACKS_OFF"),
            Done(Skip(Multi("ES_BLEEDS", "Engine and APU bleeds: ON", (K.LeftEngBleed, 1), (K.RightEngBleed, 1), (K.ApuBleed, 1)),
                s => s.IsOn("FO_ALL_BLEEDS_ON")), "ES_BLEEDS"),
            // The start-air path (E144-145): "Keep the isolation valve OPEN and both engine bleeds ON".
            Done(Skip(SW("ES_ISOLATION", "Isolation valve: OPEN", K.Isolation, (int)Bbs757FoStateEvaluator.IsolationOpen),
                s => s.IsOn("FO_ISOLATION_OPEN")), "ES_ISOLATION"),
            // No start air, no start: the APU must be running.
            WaitForField("ES_APU", "APU running for start air", "FO_APU_RUNNING", On, CheckSeconds, FlowStepFailurePolicy.Stop),
        };
        steps.AddRange(EngineSteps(2, "Right", K.RightStart, X.FuelControl2, X.LightOff2, X.Cutout2, "ES_E2_GND", "ES_E2_FUEL"));
        steps.AddRange(EngineSteps(1, "Left", K.LeftStart, X.FuelControl1, X.LightOff1, X.Cutout1, "ES_E1_GND", "ES_E1_FUEL"));
        return steps;
    }

    private static IEnumerable<Step> EngineSteps(int n, string side, string startNode, string fuelKey, string lightOffKey,
        string cutoutKey, string gndItem, string fuelItem)
    {
        string running = $"FO_ENG{n}_RUNNING";
        // Never linked to its line: the selector line latches on the engine's N2 past the starter (FOB-1),
        // so a GND that never becomes a lit engine leaves it unticked. A GND that does not land stops the
        // flow: nothing after it makes sense. The executor refuses GND on an unread N2 or above 20 %.
        var gnd = Skip(SW(gndItem, $"{side} engine start selector: GND", startNode, (int)K.StartGnd), s => s.IsOn(running));
        gnd.FailurePolicy = FlowStepFailurePolicy.Stop;
        yield return gnd;
        // Fuel only at the variant's fuel point; a start that never gets there stops the flow, and the
        // executor puts the selector back in AUTO (OnFlowStoppedAsync). Only after THIS run's GND.
        yield return RunOnlyIfSent(WaitForField($"ES_E{n}_FUEL_POINT", $"{side} engine at the fuel point", $"FO_ENG{n}_FUEL_POINT", On,
            FuelPointSeconds, FlowStepFailurePolicy.Stop), gndItem);
        // RUN (RICH for a cold Rolls-Royce). A fuel control that does not land stops the flow: the starter
        // is then motoring a dry engine, and the stop puts it back.
        string fuelStep = $"ES_E{n}_FUEL_STEP";
        var fuel = Done(Skip(SW(fuelStep, $"{side} fuel control switch: RUN (RICH on a cold Rolls-Royce)", fuelKey, X.FuelForStart),
            s => s.IsOn($"FO_FUEL_CONTROL_{n}_RUN")), fuelItem);
        fuel.FailurePolicy = FlowStepFailurePolicy.Stop;
        yield return fuel;
        // The executor's watches (read only, held by Pause, ended by Stop). Light-off: the EGT rising within about
        // 30 s of the fuel (E147), only after THIS run's fuel. Cut-out: the starter released before anything else,
        // so the next engine's starter never engages with this one's still in (the 737's rule); BlueBird returns
        // the selector to AUTO at cut-out. Either one timing out on this run's own start aborts it (manual p. 204:
        // fuel CUTOFF, dry motoring, selector AUTO, the Captain told) and stops the flow.
        yield return RunOnlyIfSent(Watch($"ES_E{n}_LIGHT_OFF", $"{side} engine light-off, EGT rising", lightOffKey), fuelStep);
        yield return RunOnlyIfSent(Watch($"ES_E{n}_CUTOUT", $"{side} start selector back in AUTO", cutoutKey), gndItem);
        yield return WaitForField($"ES_E{n}_RUNNING", $"{side} engine running", running, On, EngineRunSeconds, FlowStepFailurePolicy.Stop);
    }

    /// <summary>The step runs only when <paramref name="stepId"/> SENT its write in this run (else passes silently as done).</summary>
    private static Step RunOnlyIfSent(Step step, string stepId)
    {
        step.RunOnlyIfStepSent = stepId;
        return step;
    }

    // ------------------------------------------------------------------ After Start
    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Start selectors in AUTO, packs AUTO, isolation closed, APU bleed and APU off, speedbrake down, autobrake RTO, taxi light on; flaps and trims are the Captain's.",
        RelatedChecklistGroupIds = new[] { "AFTER_START", "AFTER_START_CL" },
        Steps = new()
        {
            Check("AS_CHK_START_SEL", "Engine start selectors: AUTO", "FO_START_SELECTORS_AUTO", On, CheckSeconds, "AS_START_SEL"),
            // A Rolls-Royce started RICH (EGT at or below 0 °C) goes back to RUN on the ground (the manual). Only from
            // RICH: never opens a switch read CUTOFF, never airborne, never on an unread switch.
            LeaveAlone(Skip(Multi("AS_FUEL_RUN", "Fuel control switches: RUN", (X.FuelControl1, 1), (X.FuelControl2, 1)),
                    s => s.IsOn("FO_FUEL_CONTROLS_RUN") && s.GetValue("FO_FUEL_CONTROLS_RICH") is var rich && !double.IsNaN(rich) && rich < 0.5),
                s => !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn("FO_FUEL_CONTROLS_RUN") || !s.IsOn("FO_FUEL_CONTROLS_RICH"),
                "Fuel control switches left as they are: not on the ground, not in RICH, or not read."),
            Done(Skip(Multi("AS_PACKS", "Packs: AUTO", (K.LeftPackSelector, (int)K.PackAuto), (K.RightPackSelector, (int)K.PackAuto)),
                s => s.IsOn("FO_PACKS_AUTO")), "AS_PACKS"),
            Done(Skip(SW("AS_ISOLATION", "Isolation valve: CLOSED", K.Isolation, (int)Bbs757FoStateEvaluator.IsolationClosed),
                s => s.IsOn("FO_ISOLATION_CLOSED")), "AS_ISOLATION"),
            Check("AS_CHK_AIRCON", "Air conditioning panel: SET", "FO_AIRCON_SET", On, CheckSeconds, "ASC_AIRCON"),
            Done(Skip(SW("AS_APU_BLEED", "APU bleed: OFF", K.ApuBleed, 0), s => s.IsPosition(K.ApuBleed, 0)), "AS_APU_BLEED"),
            Done(LeaveAlone(Skip(SW("AS_APU", "APU: OFF", K.ApuSelector, 0), s => s.IsOn("FO_APU_OFF")),
                Bbs757FoChecklistDefinitions.NoOtherPower, Bbs757FoChecklistDefinitions.ApuStaysText), "AS_APU"),
            Done(Skip(SW("AS_SPEEDBRAKE", "Speedbrake lever: DOWN", X.SpeedbrakeDown, 1), s => s.IsOn("FO_SPEEDBRAKE_DOWN")), "AS_SPEEDBRAKE"),
            Done(Skip(SW("AS_AUTOBRAKE", "Autobrake: RTO", K.Autobrake, (int)K.AutobrakeRto), s => s.IsPosition(K.Autobrake, (int)K.AutobrakeRto)), "AS_AUTOBRAKE"),
            Done(Skip(SW("AS_TAXI_LIGHT", "Taxi light: ON", K.TaxiLight, 1), s => s.IsOn(K.TaxiLight)), "AS_TAXI_LIGHT"),
            Done(Skip(Captain("AS_FLAPS", "Takeoff flaps", "Set the flaps for takeoff."), s => s.IsOn("FO_FLAPS_TAKEOFF")), "AS_FLAPS"),
            Check("AS_CHK_FLAPS", "Flaps: set for takeoff", "FO_FLAPS_TAKEOFF", On, CaptainFlapsSeconds, "ASC_FLAPS"),
            Done(Captain("AS_TRIMS", "Trims", "Set the trims: rudder zero, aileron zero, stabilizer for takeoff."), "AS_TRIMS"),
        }
    };

    // ------------------------------------------------------------------ Before Takeoff
    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Both engines running, seat belts on, flaps and autobrake checked, landing, strobe, wing, runway turnoff and taxi lights on, then a cabin call for takeoff.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            Check("BT_CHK_ENGINES", "Engines: both running", "FO_ENGINES_RUNNING", On, CheckSeconds, "BTC_ENGINES"),
            Check("BT_CHK_FLAPS", "Flaps: set for takeoff", "FO_FLAPS_TAKEOFF", On, CheckSeconds, "BTC_FLAPS"),
            Check("BT_CHK_AUTOBRAKE", "Autobrake: RTO", K.Autobrake, v => Is(v, K.AutobrakeRto), CheckSeconds, "BTC_AUTOBRAKE"),
            Also(Done(Skip(SW("BT_SEAT_BELTS", "Seat belt signs: ON", K.SeatBelts, (int)K.SignOn), s => s.IsPosition(K.SeatBelts, (int)K.SignOn)),
                "BT_SEAT_BELTS"), "BTC_SEAT_BELTS"),
            Also(Done(Skip(Multi("BT_LANDING", "Landing lights: ON", (K.LeftLanding, 1), (K.RightLanding, 1)),
                s => s.IsOn("FO_LANDING_LIGHTS_ON")), "BT_LANDING"), "BTC_LIGHTS"),
            Done(Skip(SW("BT_STROBES", "Strobe lights: ON", K.StrobeLights, 1), s => s.IsOn(K.StrobeLights)), "BT_STROBES"),
            Done(Skip(SW("BT_WING", "Wing lights: ON", K.WingLights, 1), s => s.IsOn(K.WingLights)), "BT_WING"),
            Done(Skip(Multi("BT_TURNOFF", "Runway turnoff lights: ON", (K.LeftTurnoff, 1), (K.RightTurnoff, 1)),
                s => s.IsOn("FO_TURNOFF_LIGHTS_ON")), "BT_TURNOFF"),
            Done(Skip(SW("BT_TAXI_LIGHT", "Taxi light: ON", K.TaxiLight, 1), s => s.IsOn(K.TaxiLight)), "BT_TAXI_LIGHT"),
            // The cabin chime (the 737s' "Advise the cabin crew for takeoff"): a press with no state, never "Already set".
            Done(SW("BT_CABIN", "Cabin call: advise the cabin crew for takeoff", K.CabinCall, 1), "BT_CABIN"),
            Done(Captain("BT_CONFIG", "Takeoff configuration", "Press the CONFIG button and check there is no configuration warning."), "BT_CONFIG"),
        }
    };

    // ------------------------------------------------------------------ After Takeoff
    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Gear lever OFF after retraction (the Captain's), runway turnoff and taxi lights off, then flaps up and gear up and off confirmed, read only.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Done(Skip(Captain("AT_GEAR_OFF", "Gear lever OFF", "Gear lever to OFF once the gear is up."),
                s => s.IsOn("FO_GEAR_UP_OFF")), "AT_GEAR_OFF"),
            Done(Skip(Multi("AT_GROUND_LIGHTS", "Runway turnoff and taxi lights: OFF", (K.LeftTurnoff, 0), (K.RightTurnoff, 0), (K.TaxiLight, 0)),
                s => s.IsOn("FO_GROUND_LIGHTS_OFF")), "AT_GROUND_LIGHTS"),
            // Checks only read: the checklist's lines alone (owner, 2026-11-08).
            Check("AT_CHK_FLAPS", "Flaps: up", "FO_FLAPS_UP", On, FlapsUpCheckSeconds, "ATC_FLAPS"),
            // Last (FO-13): the lever at OFF and every leg up.
            Check("AT_CHK_GEAR", "Gear: up and off", "FO_GEAR_UP_OFF", On, GearCheckSeconds, "ATC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ Descent (In Range)
    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seat belts on, a cabin call for landing preparation, landing lights on; the landing autobrake, the ILS, landing data and minimums are the Captain's.",
        // The arrival is two flows, as on the MD-11 (owner, 2026-11-08): this one and Before Landing. DESCENT_CL is BlueBird's
        // In Range and Approach cards as one checklist, so there is no Approach flow or group.
        RelatedChecklistGroupIds = new[] { "DESCENT", "DESCENT_CL" },
        Steps = new()
        {
            Done(Skip(SW("DS_SEAT_BELTS", "Seat belt signs: ON", K.SeatBelts, (int)K.SignOn), s => s.IsPosition(K.SeatBelts, (int)K.SignOn)), "DS_SEAT_BELTS"),
            // BlueBird's descent procedure (p. 55): "Press one of the blue cabin-call buttons to sound the hi-lo chime for
            // landing preparation". A press with no state, never "Already set".
            Done(SW("DS_CABIN", "Cabin call: landing preparation", K.CabinCall, 1), "DS_CABIN"),
            Also(Skip(Multi("DS_LANDING", "Landing lights: ON", (K.LeftLanding, 1), (K.RightLanding, 1)),
                s => s.IsOn("FO_LANDING_LIGHTS_ON")), "IRC_LIGHTS"),
            Done(Captain("DS_AUTOBRAKE", "Landing autobrake", "Set the landing autobrake on the main panel."), "DS_AUTOBRAKE"),
            // The 737s' "Set the ILS frequencies and course": the frequency and course only, no audio ident (owner, 2026-11-08).
            Done(Captain("DS_ILS", "ILS", "Set the ILS frequency and course in the NAV radios window, Input Ctrl+N."), "DS_ILS"),
            Done(Captain("DS_LANDING_DATA", "Landing data", "Check the landing data and approach speeds, and set the minimums."), "DS_LANDING_DATA"),
        }
    };

    // ------------------------------------------------------------------ Before Landing
    private static Flow BuildBeforeLanding() => new()
    {
        Id = "BEFORE_LANDING", Name = "Before Landing",
        Description = "Speedbrake armed, taxi light and runway turnoff lights on, the missed approach altitude (the Captain's), seat belts checked, landing flaps 25 or 30 checked, then gear down and three green confirmed, read only.",
        RelatedChecklistGroupIds = new[] { "BEFORE_LANDING", "BEFORE_LANDING_CL" },
        Steps = new()
        {
            Also(Done(LeaveAlone(Skip(SW("BL_SPEEDBRAKE", "Speedbrake: ARMED", X.SpeedbrakeArm, 1), s => s.IsOn("FO_SPEEDBRAKE_ARMED")),
                s => s.IsOn("FO_SPEEDBRAKE_EXTENDED"), Bbs757FoChecklistDefinitions.SpeedbrakeStaysText), "BL_SPEEDBRAKE"), "BLC_SPEEDBRAKE"),
            Done(Skip(Multi("BL_LIGHTS", "Taxi light and runway turnoff lights: ON", (K.LeftTurnoff, 1), (K.RightTurnoff, 1), (K.TaxiLight, 1)),
                s => s.IsOn("FO_GROUND_LIGHTS_ON")), "BL_LIGHTS"),
            // The 737s' Landing flow: "Set the missed approach altitude."
            Done(Captain("BL_MISSED", "Missed approach altitude", "Set the missed approach altitude."), "BL_MISSED"),
            Check("BL_CHK_SEAT_BELTS", "Seat belt signs: ON", K.SeatBelts, v => Is(v, K.SignOn), CheckSeconds, "BLC_SEAT_BELTS"),
            // The Captain's flap lever, read (E181: "The normal landing choices are 25 or 30").
            Check("BL_CHK_FLAPS", "Flaps: landing, 25 or 30", "FO_FLAPS_LANDING", On, CaptainFlapsSeconds, "BLC_FLAPS"),
            // Last (FO-13): the lever down and every leg down.
            Check("BL_CHK_GEAR", "Gear: down, three green", "FO_GEAR_DOWN", On, GearCheckSeconds, "BLC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ After Landing
    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Speedbrake down, autobrake off, weather radar and terrain off, wing anti-ice off, landing, strobe, wing and runway turnoff lights off, taxi light on, flight directors off; flaps and trims are the Captain's; then the APU start.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING", "AFTER_LANDING_CL" },
        Steps = new()
        {
            OnGroundGuard("AL_ON_GROUND"),
            Also(Done(Skip(SW("AL_SPEEDBRAKE", "Speedbrake: DOWN", X.SpeedbrakeDown, 1), s => s.IsOn("FO_SPEEDBRAKE_DOWN")), "AL_SPEEDBRAKE"), "ALC_SPEEDBRAKE"),
            Also(Done(Skip(SW("AL_AUTOBRAKE", "Autobrake: OFF", K.Autobrake, (int)K.AutobrakeOff), s => s.IsPosition(K.Autobrake, (int)K.AutobrakeOff)),
                "AL_AUTOBRAKE"), "ALC_AUTOBRAKES"),
            Also(Done(Skip(Multi("AL_WXR_TERR", "Weather radar and terrain displays: OFF", K.TerrainAndWeather.Select(k => (k, (int?)0)).ToArray()),
                s => s.IsOn("FO_WXR_TERR_OFF")), "AL_WXR_TERR"), "ALC_WXR"),
            Done(Skip(SW("AL_WING_ANTI_ICE", "Wing anti-ice: OFF", K.WingAntiIce, 0), s => s.IsPosition(K.WingAntiIce, 0)), "AL_WING_ANTI_ICE"),
            Done(Skip(Multi("AL_LANDING", "Landing lights: OFF", (K.LeftLanding, 0), (K.RightLanding, 0)), s => s.IsOn("FO_LANDING_LIGHTS_OFF")), "AL_LANDING"),
            Done(Skip(SW("AL_STROBES", "Strobe lights: OFF", K.StrobeLights, 0), s => s.IsPosition(K.StrobeLights, 0)), "AL_STROBES"),
            Done(Skip(SW("AL_WING_LIGHTS", "Wing lights: OFF", K.WingLights, 0), s => s.IsPosition(K.WingLights, 0)), "AL_WING_LIGHTS"),
            Done(Skip(Multi("AL_TURNOFF", "Runway turnoff lights: OFF", (K.LeftTurnoff, 0), (K.RightTurnoff, 0)),
                s => s.IsOn("FO_TURNOFF_LIGHTS_OFF")), "AL_TURNOFF"),
            Done(Skip(SW("AL_TAXI_LIGHT", "Taxi light: ON", K.TaxiLight, 1), s => s.IsOn(K.TaxiLight)), "AL_TAXI_LIGHT"),
            Done(Skip(Multi("AL_FDS", "Flight directors: OFF", (K.CaptainFd, 0), (K.FirstOfficerFd, 0)), s => s.IsOn("FO_FDS_OFF")), "AL_FDS"),
            Done(Skip(Captain("AL_FLAPS", "Flaps up", "Flaps up."), s => s.IsOn("FO_FLAPS_UP")), "AL_FLAPS"),
            Check("AL_CHK_FLAPS", "Flaps: up", "FO_FLAPS_UP", On, CaptainFlapsSeconds, "ALC_FLAPS"),
            Done(Captain("AL_TRIMS", "Trims", "Trims: rudder and aileron zero, stabilizer 4 units."), "AL_TRIMS"),
            // Last: the APU started, then waited for (about 80 s), so "APU: running" is never latched while it only starts.
            Skip(SW("AL_APU", "APU: start", X.ApuStart, 1), s => s.IsOn("FO_APU_RUNNING")),
            Done(Skip(WaitForField("AL_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ApuStartSeconds),
                s => s.IsOn("FO_APU_RUNNING")), "AL_APU"),
        }
    };

    // ------------------------------------------------------------------ Shutdown
    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Parking brake, engine anti-ice off, fuel controls cutoff, engines stopped, beacon and seat belts off, stabilizer trim (the Captain's), electric hydraulic pumps L then C then R, fuel pumps, wing anti-ice and wipers off, window heat, isolation open, APU bleed on with the APU running, flight directors, heaters, exterior lights to position only, transponder standby.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN", "SHUTDOWN_CL" },
        Steps = new()
        {
            OnGroundGuard("SD_ON_GROUND"),
            Done(LeaveAlone(Skip(SW("SD_PARK", "Parking brake: SET", X.ParkingBrake, 1), s => s.IsOn(K.ParkingBrake)),
                Bbs757FoChecklistDefinitions.NoBrakePressure, Bbs757FoChecklistDefinitions.BrakeStaysText), "SD_PARK"),
            // The engines' supply must be on the busses before the fuel goes (the manual's
            // "AC power source: confirmed").
            WaitForField("SD_POWER", "APU or external power on line", "FO_APU_OR_EXT_POWER_ON", On, CheckSeconds, FlowStepFailurePolicy.Stop),
            Also(Done(Skip(Multi("SD_ENG_ANTI_ICE", "Engine anti-ice: OFF", (K.LeftEngAntiIce, 0), (K.RightEngAntiIce, 0)),
                s => s.IsOn("FO_ENG_ANTI_ICE_OFF")), "SD_ENG_ANTI_ICE"), "SDC_ENG_ANTI_ICE"),
            Also(Done(Skip(Multi("SD_FUEL_CUTOFF", "Fuel control switches: CUTOFF", (X.FuelControl1, 0), (X.FuelControl2, 0)),
                s => s.IsOn("FO_FUEL_CONTROLS_CUTOFF")), "SD_FUEL_CUTOFF"), "SDC_FUEL"),
            WaitForField("SD_ENGINES_STOPPED", "Engines stopped", "FO_ENGINES_STOPPED", On, EnginesStopSeconds, FlowStepFailurePolicy.Stop),
            Also(Done(Skip(SW("SD_BEACON", "Beacon: OFF", K.BeaconLight, 0), s => s.IsPosition(K.BeaconLight, 0)), "SD_BEACON"), "SDC_BEACON"),
            Also(Done(Skip(SW("SD_SEAT_BELTS", "Seat belt signs: OFF", K.SeatBelts, (int)K.SignOff), s => s.IsPosition(K.SeatBelts, (int)K.SignOff)),
                "SD_SEAT_BELTS"), "SDC_SEAT_BELTS"),
            // Before the hydraulics go: the stabilizer needs pressure to move (E195, E198).
            Done(Captain("SD_STAB_TRIM", "Stabilizer trim", "Stabilizer trim set before the hydraulics go off."), "SD_STAB_TRIM"),
            Also(Done(Skip(Multi("SD_ELEC_HYD", "Electric hydraulic pumps: OFF, L then C then R",
                    (K.LeftElecHyd, 0), (K.C2ElecHyd, 0), (K.C1ElecHyd, 0), (K.RightElecHyd, 0)),
                s => s.IsOn("FO_ELEC_HYD_PUMPS_OFF")), "SD_ELEC_HYD"), "SDC_HYD"),
            Also(Done(Skip(Multi("SD_FUEL_PUMPS", "Fuel pumps: OFF", Bbs757FoChecklistDefinitions.AllFuelPumps.Select(k => (k, (int?)0)).ToArray()),
                s => s.IsOn("FO_FUEL_PUMPS_OFF")), "SD_FUEL_PUMPS"), "SDC_FUEL_PUMPS"),
            // "ANTI-ICE AND WIPERS ... OFF" (E196).
            Done(Skip(SW("SD_WING_ANTI_ICE", "Wing anti-ice: OFF", K.WingAntiIce, 0), s => s.IsPosition(K.WingAntiIce, 0)), "SD_WING_ANTI_ICE"),
            Done(Skip(SW("SD_WIPERS", "Wipers: OFF", K.Wipers, 0), s => s.IsPosition(K.Wipers, 0)), "SD_WIPERS"),
            Also(Done(Skip(Multi("SD_WINDOW_HEAT", "Window heat: OFF", K.WindowHeat.Select(k => (k, (int?)0)).ToArray()),
                s => s.IsOn("FO_WINDOW_HEAT_OFF")), "SD_WINDOW_HEAT"), "SDC_WINDOW_HEAT"),
            Also(Done(Skip(SW("SD_ISOLATION", "Isolation valve: OPEN", K.Isolation, (int)Bbs757FoStateEvaluator.IsolationOpen),
                s => s.IsOn("FO_ISOLATION_OPEN")), "SD_ISOLATION"), "SDC_ISOLATION"),
            // With the APU supplying the parked aircraft, its bleed feeds the packs; without it, nothing to feed.
            Done(LeaveAlone(Skip(SW("SD_APU_BLEED", "APU bleed: ON", K.ApuBleed, 1), s => s.IsOn(K.ApuBleed)),
                s => !s.IsOn("FO_APU_RUNNING"), Bbs757FoChecklistDefinitions.ApuBleedStaysText), "SD_APU_BLEED"),
            Also(Done(Skip(Multi("SD_FDS", "Flight directors: OFF", (K.CaptainFd, 0), (K.FirstOfficerFd, 0)), s => s.IsOn("FO_FDS_OFF")), "SD_FDS"), "SDC_FDS"),
            Also(Done(Skip(Multi("SD_HEATERS", "Foot and shoulder heaters: OFF", K.Heaters.Select(k => (k, (int?)(int)K.HeaterOff)).ToArray()),
                s => s.IsOn("FO_HEATERS_OFF")), "SD_HEATERS"), "SDC_HEATERS"),
            Done(Skip(Multi("SD_TURNOFF", "Runway turnoff lights: OFF", (K.LeftTurnoff, 0), (K.RightTurnoff, 0)),
                s => s.IsOn("FO_TURNOFF_LIGHTS_OFF")), "SD_TURNOFF"),
            // The powered parked configuration (E196): "Leave POSITION ON and the other exterior lights OFF".
            Done(Skip(Multi("SD_EXT_LIGHTS", "Exterior lights: position only, wing, logo and taxi lights OFF",
                    (K.WingLights, 0), (K.LogoLights, 0), (K.TaxiLight, 0)),
                s => s.IsOn("FO_PARKED_LIGHTS_OFF")), "SD_EXT_LIGHTS"),
            Done(Skip(SW("SD_XPDR", "Transponder: STBY", K.TcasMode, (int)K.TcasStandby), s => s.IsPosition(K.TcasMode, (int)K.TcasStandby)), "SD_XPDR"),
        }
    };

    // ------------------------------------------------------------------ Final Securing (terminating)
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Final Securing",
        Description = "IRS off, emergency lights off, APU off, then a 2-minute APU cool-down and the APU stopped, external power off, then the battery off: the aircraft cold and dark.",
        RelatedChecklistGroupIds = new[] { "SECURE" },
        Steps = new()
        {
            OnGroundGuard("SC_ON_GROUND"),
            EnginesStoppedGuard("SC_ENGINES_STOPPED"),
            Done(Skip(Multi("SC_IRS", "IRS selectors: OFF",
                    (K.IrsSwitches[0], 0), (K.IrsSwitches[1], 0), (K.IrsSwitches[2], 0)),
                s => s.IsOn("FO_IRS_ALL_OFF")), "SC_IRS"),
            Done(Skip(SW("SC_EMER_LTS", "Emergency lights: OFF", K.EmergencyLights, 0), s => s.IsPosition(K.EmergencyLights, 0)), "SC_EMER_LTS"),
            // The APU by its selector, never the battery (the manual), and the battery kept on until it stops. The 737s'
            // rules (FOB-22; owner, 2026-11-08): "Already set" over a switch already OFF, so the cool-down below follows
            // only an APU THIS run switched off: a fixed 2 minutes (the iFly's 120 s), then the read wait for the APU to
            // stop, as the PMDG 737 follows its fixed wait with a read of the APU itself.
            Done(Skip(SW("SC_APU", "APU: OFF", K.ApuSelector, 0), s => s.IsOn("FO_APU_OFF")), "SC_APU"),
            RunOnlyIfSent(Wait("SC_APU_COOLDOWN", "APU cool-down", ApuCooldownSeconds), "SC_APU"),
            WaitForField("SC_APU_STOPPED", "APU to stop", K.ApuState, v => Is(v, 0), ApuStopSeconds, FlowStepFailurePolicy.Stop),
            Done(Skip(SW("SC_EXT_PWR", "External power: OFF", X.ExtPower, 0), s => s.IsOn("FO_EXT_POWER_OFF")), "SC_EXT_PWR"),
            // External power that would not switch off: the GPU itself is removed (E199: "remove the external supply
            // before selecting the battery OFF").
            Skip(Captain("SC_GPU", "External power off",
                    "External power did not switch off. Remove the GPU on the EFB."),
                s => s.IsOn("FO_EXT_POWER_OFF")),
            // Never with the APU or external power still supplying (E199).
            Done(LeaveAlone(Skip(SW("SC_BATTERY", "Battery: OFF", K.Battery, 0), s => s.IsPosition(K.Battery, 0)),
                Bbs757FoChecklistDefinitions.BatteryMustStay, Bbs757FoChecklistDefinitions.BatteryStaysText), "SC_BATTERY"),
        }
    };

    // ------------------------------------------------------------------ step builders (the MD-11's)

    /// <summary>A read-only check: "Already set" when the field already satisfies it, else a wait skipped aloud; never writes.</summary>
    private static Step Check(string id, string label, string field, Func<double, bool> ok, int timeoutSec,
        string checklistItemId, params string[] moreItemIds)
        => Also(Done(Skip(WaitForField(id, label, field, ok, timeoutSec), s => ok(s.GetValue(field))), checklistItemId), moreItemIds);

    private static Step SW(string id, string label, string key, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = key,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    /// <summary>An engine start's watch: run by the executor (it aborts a start that times out), so a timeout stops the flow.</summary>
    private static Step Watch(string id, string label, string key) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = key,
        TargetValue = 1,
        FailurePolicy = FlowStepFailurePolicy.Stop,
    };

    private static Step Provider(string id, string label, string key, Func<Bbs757FoStateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = key,
        TargetValueProvider = provider,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string Key, int? Target)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    /// <summary>A fixed wait, spoken "Waiting N seconds: label" (the 737s' cool-downs).</summary>
    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    /// <summary>Stops a flow unless the aircraft reads on the ground (unread fails the guard).</summary>
    private static Step OnGroundGuard(string id)
        => WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, CheckSeconds, FlowStepFailurePolicy.Stop);

    /// <summary>Stops a flow unless both engines read stopped (N2 below 10 %; unread fails the guard).</summary>
    private static Step EnginesStoppedGuard(string id)
        => WaitForField(id, "Engines stopped", "FO_ENGINES_STOPPED", v => v > 0.5, CheckSeconds, FlowStepFailurePolicy.Stop);

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
    };

    private static Step Skip(Step step, Func<Bbs757FoStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step LeaveAlone(Step step, Func<Bbs757FoStateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    /// <summary>The read-back lines this step also delivers: ticked with it, kept unticked when it is skipped (FO-6).</summary>
    private static Step Also(Step step, params string[] readBackItemIds)
    {
        step.AlsoCompletesChecklistItemIds = step.AlsoCompletesChecklistItemIds.Concat(readBackItemIds).ToArray();
        return step;
    }
}
