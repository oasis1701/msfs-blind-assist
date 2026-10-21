using MSFSBlindAssist.FirstOfficer.Models;
using K = MSFSBlindAssist.FirstOfficer.A300.A300FoKeys;
using T = MSFSBlindAssist.FirstOfficer.A300.A300FoSystemTests;

namespace MSFSBlindAssist.FirstOfficer.A300;

using Flow = FlowDefinition<A300FoStateEvaluator>;
using Step = FlowStep<A300FoStateEvaluator>;

/// <summary>
/// The iniBuilds A300 First Officer flows: iniBuilds' Simplified Procedures (A300 MSFS Manual, pages 29 to 46), each
/// position the target iniBuilds' own checklist tests. Every switch line carries "Already set" on its own field,
/// and the executor reads back. Captain steps are spoken reminders. The FO never programs the MCDU ([FO-2]), never
/// sets the takeoff flaps (design decision, 2026-10-10), never moves the flaps in flight, never sets the landing autobrake
/// ([FO-14]), and never moves the thrust levers or trims. Visual checks a blind pilot cannot perform are left out.
/// </summary>
public static class A300FoFlowDefinitions
{
    public static List<Flow> Build() => BuildWithLines().Flows;

    /// <summary>The flows, and every line they perform by id: the action groups are built from these
    /// (A300FoChecklistDefinitions), so a flow and its group are one list.</summary>
    internal static (List<Flow> Flows, IReadOnlyDictionary<string, A300FoLine> Lines) BuildWithLines()
    {
        var lines = new Dictionary<string, A300FoLine>(StringComparer.Ordinal);
        Step L(A300FoLine line)
        {
            lines.Add(line.Id, line);   // throws on a repeated id
            return StepFor(line);
        }
        var flows = new List<Flow>
        {
            PowerUp(L), Preflight(L), BeforeStart(L), EngineStart(L), AfterStart(L), BeforeTakeoff(L), LineUp(L),
            AfterTakeoff(L), Descent(), Approach(L), Landing(L), AfterLanding(L), Parking(L), Securing(L),
        };
        return (flows, lines);
    }

    // ================================================================== flows

    private static Flow PowerUp(Func<A300FoLine, Step> L) => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Description = "Batteries, wipers and fuel levers off, weather radar off, external power, APU fire test, IRS to NAV, crew oxygen, annunciator test.",
        Steps = new()
        {
            EnginesStoppedGuard("PU_ENGINES_STOPPED"),
            OnGroundGuard("PU_ON_GROUND"),
            L(A300FoLine.Rows("PU_BATTERIES", "Batteries 1, 2 and 3: ON", "FO_BATTERIES_ON")),
            L(A300FoLine.Rows("PU_WIPERS", "Wipers: OFF", "FO_WIPERS_OFF")),
            Check("PU_GEAR_DOWN", "Gear lever: DOWN", K.GearLever, v => v >= 0.5, 5),
            L(A300FoLine.Rows("PU_FUEL_LEVERS", "Fuel levers: OFF", "FO_FUEL_LEVERS_OFF")),
            L(A300FoLine.Row("PU_WXR", "Weather radar: OFF", K.WxrSystem, 1)),
            Leave(L(A300FoLine.Row("PU_EXT_POWER", "External power: ON", K.ExtPower, 1)),
                s => !Is(s, K.ExtPowerAvailLight, On), "No ground power available."),
            Skip(Captain("PU_CPT_GROUND_POWER", "Ground power", "Connect ground power on the tablet, or start the APU."),
                s => Is(s, K.ExtPower, On)),
            L(A300FoLine.Sequence("PU_APU_FIRE_TEST", "APU fire test", T.ApuFire.Key)),
            L(A300FoLine.Rows("PU_IRS", "IRS 1, 2 and 3: NAV", "FO_IRS_NAV")),
            L(A300FoLine.Row("PU_IRS_DISPLAY", "IRS display: HDG/STS", K.IrsDisplay, 4)),
            L(A300FoLine.Row("PU_CREW_OXYGEN", "Crew oxygen supply: ON", K.CrewOxygen, 1)),
            L(A300FoLine.Sequence("PU_ANN_TEST", "Annunciator light test", K.AnnunciatorTest)),
            Captain("PU_CPT_RADIOS", "Set the radios"),
            Captain("PU_CPT_IRS_INIT", "Initialise the IRS position on the MCDU INIT page; alignment takes about three minutes after ALIGN IRS"),
        },
    };

    private static Flow Preflight(Func<A300FoLine, Step> L) => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "Signs, recorder, exterior lights, autothrottle, pitch trim and yaw dampers, fire, smoke, GPWS, gear warning and takeoff warning tests, fuel pumps, heat, emergency exit lights, flight directors, DH, parking brake, transponder, weather radar test, cockpit door.",
        Steps = new()
        {
            L(A300FoLine.Row("PF_NO_SMOKING", "No smoking: AUTO", K.NoSmoking, 1)),
            L(A300FoLine.Row("PF_SEAT_BELTS", "Seat belts: ON", K.SeatBelts, 1)),
            L(A300FoLine.Row("PF_FLIGHT_RECORDER", "Flight recorder: ON", K.FlightRecorder, 1)),
            L(A300FoLine.Rows("PF_EXT_LIGHTS", "Exterior lights: set for preflight", "FO_EXT_LIGHTS_PREFLIGHT")),
            L(A300FoLine.Rows("PF_ATS", "Autothrottle levers: ON", "FO_ATS_ON")),
            Leave(L(A300FoLine.Rows("PF_PITCH_YAW", "Pitch trim and yaw damper levers: ON", "FO_PITCH_YAW_ON")),
                IrsNotAligned, IrsAligningText),
            L(A300FoLine.Sequence("PF_ENG1_FIRE_TEST", "Engine 1 fire test", T.Engine1Fire.Key)),
            L(A300FoLine.Rows("PF_ELEC_INDICATION", "Electrical indication: EMER and ESS", "FO_ELEC_INDICATION")),
            L(A300FoLine.Row("PF_IGNITION", "Ignition: OFF", K.Ignition, 3)),
            L(A300FoLine.Rows("PF_FUEL_PUMPS", "Fuel pumps: ON", "FO_FUEL_PUMPS_ON")),
            L(A300FoLine.Rows("PF_WINDOW_HEAT", "Window heat: ON", "FO_WINDOW_HEAT_ON")),
            L(A300FoLine.Rows("PF_PROBE_HEAT", "Probe heat: ON", "FO_PROBE_HEAT_ON")),
            L(A300FoLine.Sequence("PF_CARGO_SMOKE_TEST", "Cargo smoke test", T.CargoSmoke.Key)),
            L(A300FoLine.Sequence("PF_ENG2_FIRE_TEST", "Engine 2 fire test", T.Engine2Fire.Key)),
            L(A300FoLine.Row("PF_OVERBOARD_VALVE", "Overboard valve: ON", K.OverboardValve, 1)),
            L(A300FoLine.Row("PF_EMER_EXIT", "Emergency exit lights: ARM", K.EmerExit, 1)),
            L(A300FoLine.Rows("PF_FLIGHT_DIRECTORS", "Flight directors: ON", "FO_FLIGHT_DIRECTORS_ON")),
            L(A300FoLine.Rows("PF_NAV_SELECTORS", "Nav selectors: NAV", "FO_NAV_SELECTORS_NAV")),
            L(A300FoLine.Press("PF_DH_CPT", "Captain decision height mode: DH", K.DhCpt)),
            L(A300FoLine.Press("PF_DH_FO", "First officer decision height mode: DH", K.DhFo)),
            L(A300FoLine.Sequence("PF_GPWS_TEST", "GPWS test", T.Gpws.Key)),
            L(A300FoLine.Row("PF_PARKING_BRAKE", "Parking brake: SET", K.ParkingBrake, 1)),
            L(A300FoLine.Sequence("PF_GEAR_WARNING_TEST", "Landing gear warning test", T.GearWarning.Key)),
            Captain("PF_CPT_BRAKE_FANS", "Set the brake fans as required"),   // iniBuilds: "Brake Fan: As Required"
            L(A300FoLine.Row("PF_SPOILERS", "Ground spoilers: DISARMED", K.SpoilersArm, 0)),
            L(A300FoLine.Sequence("PF_TAKEOFF_WARNING_TEST", "Takeoff warning test", T.TakeoffWarning.Key)),
            L(A300FoLine.Rows("PF_ADF_TONES", "ADF tones: ON", "FO_ADF_TONES_ON")),
            L(A300FoLine.Press("PF_RUDDER_TRIM", "Rudder trim: ZERO", K.RudderTrimReset)),
            L(A300FoLine.Row("PF_TRANSPONDER", "Transponder: STANDBY", K.Transponder, 0)),
            L(A300FoLine.Sequence("PF_WXR_TEST", "Weather radar test, then off", K.WeatherRadarTest)),
            L(A300FoLine.Row("PF_COCKPIT_DOOR", "Cockpit door: CLOSED", K.CockpitDoor, 0)),
            Captain("PF_CPT_MCDU", "Program the MCDU: INIT A, flight plan, performance and INIT B"),
            Captain("PF_CPT_TRP", "Set the TRP: FLEX TO temperature, AUTO for a profile takeoff or TOGA for manual thrust"),
            Captain("PF_CPT_DH", "Set the decision height"),
            Captain("PF_CPT_LDG_ELEV", "Set the landing elevation"),
            Captain("PF_CPT_ALTIMETERS", "Set the altimeters"),
            Captain("PF_CPT_SQUAWK", "Set the transponder code"),
        },
    };

    private static Flow BeforeStart(Func<A300FoLine, Step> L) => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "APU start and bleed, external power off, anti-skid, beacon.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START_BELOW_CL" },
        Steps = new()
        {
            Skip(L(A300FoLine.Row("BS_APU_MASTER", "APU master: ON", K.ApuMaster, 1)), s => Is(s, K.ApuAvailLight, On)),
            L(A300FoLine.Press("BS_APU_START", "APU start", K.ApuStart)),
            L(A300FoLine.Press("BS_ECAM_APU", "ECAM APU page", K.EcamApu)),
            Wait("BS_APU_AVAIL", "APU available", K.ApuAvailLight, On, 180),
            Requires(L(A300FoLine.Row("BS_APU_BLEED", "APU bleed: ON", K.ApuBleed, 1)), "BS_APU_AVAIL",
                "The APU is not available, so its bleed stays off."),
            Leave(Requires(L(A300FoLine.Row("BS_EXT_POWER_OFF", "External power: OFF", K.ExtPower, 0)), "BS_APU_AVAIL",
                    "The APU is not available, so external power stays connected."),
                // Worded to hold for a hand-tick too, where the APU not being available leads to it as well.
                s => !Is(s, K.ApuGen, On), "The APU generator is not supplying power, so external power stays connected."),
            Also(L(A300FoLine.Row("BS_ANTI_SKID", "Anti-skid: NORM", K.AntiSkid, 0)), "BSC_BRAKES"),
            Also(L(A300FoLine.Row("BS_BEACON", "Beacon: ON", K.Beacon, 1)), "BSB_BEACON"),
            Captain("BS_CPT_DOORS", "Close the doors"),
            Captain("BS_CPT_ACARS", "Start ACARS"),
            Captain("BS_CPT_CLEARANCE", "Obtain pushback and start clearance"),
        },
    };

    private static Flow EngineStart(Func<A300FoLine, Step> L)
    {
        var steps = new List<Step>
        {
            L(A300FoLine.Press("ES_ECAM_ENG", "ECAM engine page", K.EcamEng)),
            L(A300FoLine.Row("ES_IGNITION", "Ignition: START A", K.Ignition, 1)),
        };
        foreach (var (n, starter, lightUp, fuel, valve) in new[]
        {
            (2, K.Starter2, K.LightUp2, K.FuelLever2, K.StartValve2Light),
            (1, K.Starter1, K.LightUp1, K.FuelLever1, K.StartValve1Light),
        })
        {
            string n2 = n == 1 ? K.Eng1N2Field : K.Eng2N2Field;
            // "Already set" once the engine runs; the line ticks and latches on N2 itself (A300FoLine.Starter).
            steps.Add(L(A300FoLine.Starter($"ES_STARTER_{n}", $"Engine {n} starter: ON", starter, n2)));
            steps.Add(Skip(Stop(Sw($"ES_LIGHT_UP_{n}", $"Engine {n} N2 twenty percent", lightUp)),
                s => s.GetValue(n2) >= A300FoActionExecutor.LightUpN2));
            steps.Add(L(A300FoLine.Row($"ES_FUEL_{n}", $"Engine {n} fuel lever: ON", fuel, 1)));
            steps.Add(Wait($"ES_VALVE_{n}", $"Engine {n} start valve to close", valve, Off, 90, FlowStepFailurePolicy.Stop));
        }
        steps.Add(Captain("ES_CPT_ANTI_ICE", "Engine anti-ice as required"));
        return new Flow
        {
            Id = "ENGINE_START", Name = "Engine Start",
            Description = "Engine 2, then engine 1: starter on, fuel lever on at 20 percent N2, wait for the start valve to close.",
            Steps = steps,
        };
    }

    private static Flow AfterStart(Func<A300FoLine, Step> L) => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Ignition, APU bleed and APU off, ground spoilers armed, rudder trim zero. The takeoff flaps are yours.",
        Steps = new()
        {
            L(A300FoLine.Row("AS_IGNITION", "Ignition: OFF", K.Ignition, 3)),
            L(A300FoLine.Row("AS_APU_BLEED", "APU bleed: OFF", K.ApuBleed, 0)),
            L(A300FoLine.Row("AS_APU_MASTER", "APU master: OFF", K.ApuMaster, 0)),
            Also(L(A300FoLine.Row("AS_SPOILERS", "Ground spoilers: ARMED", K.SpoilersArm, 1)), "ASC_SPOILERS"),
            Also(L(A300FoLine.Press("AS_RUDDER_TRIM", "Rudder trim: ZERO", K.RudderTrimReset)), "ASC_RUDDER_TRIM"),
            Skip(Captain("AS_CPT_FLAPS", "Flaps: set for takeoff"), s => Is(s, K.FlapsTakeoff, On)),
            Links(Check("AS_FLAPS_CHECK", "Flaps: takeoff position", K.FlapsTakeoff, On, 2), "ASC_FLAPS"),
            Captain("AS_CPT_PITCH_TRIM", "Set the pitch trim for takeoff"),
            Captain("AS_CPT_ANTI_ICE", "Anti-ice as required"),
        },
    };

    private static Flow BeforeTakeoff(Func<A300FoLine, Step> L) => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Taxi light, PROF and NAV, autobrake MAX, transponder, weather radar, takeoff config test.",
        Steps = new()
        {
            L(A300FoLine.Row("BT_NOSE", "Nose light: TAXI", K.Nose, 1)),
            L(A300FoLine.Press("BT_PROF", "PROF: ON", K.Prof)),
            L(A300FoLine.Press("BT_NAV", "NAV: ON", K.Nav)),
            L(A300FoLine.Press("BT_AUTOBRAKE", "Autobrake: MAX", K.AutobrakeMax)),
            L(A300FoLine.Row("BT_TRANSPONDER", "Transponder: XPDR", K.Transponder, 1)),
            L(A300FoLine.Row("BT_WXR", "Weather radar: ON", K.WxrSystem, 2)),
            Also(L(A300FoLine.Sequence("BT_TO_CONFIG_TEST", "Takeoff config test", T.TakeoffConfig.Key)), "BTC_TO_CONFIG"),
            Links(Check("BT_FLAPS_CHECK", "Flaps: takeoff position", K.FlapsTakeoff, On, 2), "BTC_FLAPS"),
            Captain("BT_CPT_FLIGHT_CONTROLS", "Check the flight controls"),
            Captain("BT_CPT_FCU", "Set the FCU speed and first altitude"),
            Captain("BT_CPT_BRIEFING", "Takeoff briefing"),
        },
    };

    private static Flow LineUp(Func<A300FoLine, Step> L) => new()
    {
        Id = "LINE_UP", Name = "Line Up",
        Description = "Brake fans off, takeoff lights, TCAS TA/RA.",
        Steps = new()
        {
            L(A300FoLine.Row("LU_BRAKE_FANS", "Brake fans: OFF", K.BrakeFan, 0)),
            L(A300FoLine.Row("LU_NOSE", "Nose light: T.O.", K.Nose, 0)),
            L(A300FoLine.Rows("LU_LANDING_LIGHTS", "Landing lights: ON", "FO_LANDING_LIGHTS_ON")),
            L(A300FoLine.Row("LU_WING", "Wing light: ON", K.Wing, 1)),
            L(A300FoLine.Row("LU_STROBE", "Strobe: ON", K.Strobe, 0)),
            L(A300FoLine.Rows("LU_TURNOFF", "Runway turnoff lights: ON", "FO_TURNOFF_LIGHTS_ON")),
            Also(L(A300FoLine.Row("LU_TCAS", "TCAS: TA/RA", K.Transponder, 2)), "LUC_TRANSPONDER", "LUC_TCAS"),
            Links(Check("LU_AUTOBRAKE_CHECK", "Autobrake: MAX", K.AutobrakeLevel, v => Math.Abs(v - 3) < 0.5, 2), "LUC_AUTOBRAKE"),
            Captain("LU_CPT_PACKS_IGNITION", "Packs and ignition as required"),
        },
    };

    private static Flow AfterTakeoff(Func<A300FoLine, Step> L) => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Ground spoilers disarmed, packs on, taxi and turnoff lights off, APU off if used, gear confirmed up.",
        Steps = new()
        {
            L(A300FoLine.Row("AT_SPOILERS", "Ground spoilers: DISARMED", K.SpoilersArm, 0)),
            Also(L(A300FoLine.Rows("AT_PACKS", "Packs: ON", "FO_PACKS_ON")), "ATC_PACKS"),
            L(A300FoLine.Row("AT_NOSE", "Nose light: OFF", K.Nose, 2)),
            L(A300FoLine.Rows("AT_TURNOFF", "Runway turnoff lights: OFF", "FO_TURNOFF_LIGHTS_OFF")),
            L(A300FoLine.Row("AT_APU_BLEED", "APU bleed: OFF", K.ApuBleed, 0)),
            L(A300FoLine.Row("AT_APU_MASTER", "APU master: OFF", K.ApuMaster, 0)),
            Captain("AT_CPT_FLAPS", "Flaps and slats up on schedule"),
            Links(Check("AT_FLAPS_CHECK", "Flaps: retracted", K.Flaps, v => v < 0.5, 2), "ATC_FLAPS"),
            Links(Wait("AT_GEAR_UP", "Landing gear: UP", K.GearUp, On, 20), "ATC_GEAR"),   // [FO-13]: last
        },
    };

    private static Flow Descent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Reminders: landing information, landing elevation, autobrake, GPWS landing flaps, decision height, briefing.",
        Steps = new()
        {
            Captain("DS_CPT_WEATHER", "Obtain the weather and landing information"),
            Captain("DS_CPT_LDG_ELEV", "Set the landing elevation"),
            Captain("DS_CPT_AUTOBRAKE", "Set the landing autobrake on the centre instrument panel"),   // [FO-14]
            Captain("DS_CPT_GPWS_FLAPS", "Set the GPWS landing flaps switch on the pedestal for the landing flaps"),
            Captain("DS_CPT_DH", "Set the decision height"),
            Captain("DS_CPT_BRIEFING", "Approach briefing"),
        },
    };

    private static Flow Approach(Func<A300FoLine, Step> L) => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "Signs on, ignition continuous relight.",
        Steps = new()
        {
            Also(L(A300FoLine.Rows("AP_SIGNS", "Seat belts: ON, no smoking: AUTO", "FO_SIGNS_SET")), "APC_SIGNS"),
            Also(L(A300FoLine.Row("AP_IGNITION", "Ignition: CONT RELIGHT", K.Ignition, 4)), "APC_IGNITION"),
            Captain("AP_CPT_ALTIMETERS", "Set the altimeters"),
            Captain("AP_CPT_MINIMUMS", "Set the minimums"),
            Captain("AP_CPT_LDG_ELEV", "Check the landing elevation"),
        },
    };

    private static Flow Landing(Func<A300FoLine, Step> L) => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Ground spoilers armed, nose light T.O., gear confirmed down. Gear, flaps, autopilot and autothrottle are yours.",
        Steps = new()
        {
            Also(L(A300FoLine.Row("LD_SPOILERS", "Ground spoilers: ARMED", K.SpoilersArm, 1)), "LDC_SPOILERS"),
            L(A300FoLine.Row("LD_NOSE", "Nose light: T.O.", K.Nose, 0)),
            Captain("LD_CPT_CONFIG", "Gear down, flaps as required, autopilot and autothrottle as required"),
            Links(Check("LD_ANTI_SKID_CHECK", "Anti-skid: NORM", K.AntiSkid, v => v < 0.5, 2), "LDC_ANTI_SKID"),
            Links(Wait("LD_GEAR_DOWN", "Landing gear: DOWN", K.GearDown, On, 20), "LDC_GEAR"),   // [FO-13]: last
        },
    };

    private static Flow AfterLanding(Func<A300FoLine, Step> L) => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Taxi lights, landing lights retracted, strobe auto, wing anti-ice and ignition off, APU start, spoilers disarmed, transponder standby, weather radar off, flaps up.",
        Steps = new()
        {
            L(A300FoLine.Row("AL_NOSE", "Nose light: TAXI", K.Nose, 1)),
            L(A300FoLine.Rows("AL_LANDING_LIGHTS", "Landing lights: RETRACTED", "FO_LANDING_LIGHTS_RETRACTED")),
            L(A300FoLine.Row("AL_WING", "Wing light: OFF", K.Wing, 0)),
            L(A300FoLine.Row("AL_STROBE", "Strobe: AUTO", K.Strobe, 1)),
            L(A300FoLine.Rows("AL_TURNOFF", "Runway turnoff lights: OFF", "FO_TURNOFF_LIGHTS_OFF")),
            L(A300FoLine.Rows("AL_WING_ANTI_ICE", "Wing anti-ice: OFF", "FO_WING_ANTI_ICE_OFF")),
            Captain("AL_CPT_ENG_ANTI_ICE", "Engine anti-ice may be left on for taxi, or off"),
            L(A300FoLine.Row("AL_IGNITION", "Ignition: OFF", K.Ignition, 3)),
            Skip(L(A300FoLine.Row("AL_APU_MASTER", "APU master: ON", K.ApuMaster, 1)), s => Is(s, K.ApuAvailLight, On)),
            L(A300FoLine.Press("AL_APU_START", "APU start", K.ApuStart)),
            Leave(Also(L(A300FoLine.Row("AL_SPOILERS", "Ground spoilers: DISARMED", K.SpoilersArm, 0)), "ALC_SPOILERS"),
                s => s.GetValue(K.SpeedBrake) is var sb && !double.IsNaN(sb) && sb > 0.05,
                "The speed brake lever is out, so the ground spoilers stay as they are."),
            L(A300FoLine.Row("AL_TRANSPONDER", "Transponder: STANDBY", K.Transponder, 0)),
            Also(L(A300FoLine.Row("AL_WXR", "Weather radar: OFF", K.WxrSystem, 1)), "ALC_WXR"),
            Captain("AL_CPT_PITCH_TRIM", "Set the pitch trim 1 degree nose up"),
            Leave(Also(L(A300FoLine.Row("AL_FLAPS", "Flaps: UP", K.Flaps, 0)), "ALC_FLAPS"),
                s => !Is(s, K.OnGround, On), "Not on the ground, so the flaps stay where they are."),
            // iniBuilds: "Brake fans: As Required", "Turn on brake fans if required." (Copilot="False": the pilot's call).
            Captain("AL_CPT_BRAKE_FANS", "Turn on the brake fans if required"),
            Links(Wait("AL_APU_AVAIL", "APU available", K.ApuAvailLight, On, 180), "ALC_APU"),
        },
    };

    private static Flow Parking(Func<A300FoLine, Step> L) => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Nose light off, parking brake, APU bleed, fuel levers off, beacon off once spooled down, seat belts, fuel pumps and heat off, brake fans off below 100 degrees.",
        Steps = new()
        {
            OnGroundGuard("PK_ON_GROUND"),
            Wait("PK_STOPPED", "Aircraft stopped", K.GroundSpeed, v => v < 1, 120, FlowStepFailurePolicy.Stop),
            L(A300FoLine.Row("PK_NOSE", "Nose light: OFF", K.Nose, 2)),
            L(A300FoLine.Row("PK_PARKING_BRAKE", "Parking brake: SET", K.ParkingBrake, 1)),
            Leave(L(A300FoLine.Row("PK_APU_BLEED", "APU bleed: ON", K.ApuBleed, 1)),
                s => !Is(s, K.ApuAvailLight, On), "The APU is not available, so its bleed stays off."),
            // The engines' generators go with the fuel: something must take the electrics first. Passes at once
            // with both fuel levers already off.
            Skip(Wait("PK_POWER_SOURCE", "APU or external power available", K.ApuOrExtPower, On, 120,
                    FlowStepFailurePolicy.Stop),
                s => Is(s, "FO_FUEL_LEVERS_OFF", On)),
            Also(L(A300FoLine.Rows("PK_FUEL_LEVERS", "Fuel levers: OFF", "FO_FUEL_LEVERS_OFF")), "PKC_ENGINES"),
            Wait("PK_SPOOL_DOWN", "Engines below 20 percent N2", K.EnginesSpooledDown, On, 90),
            Requires(L(A300FoLine.Row("PK_BEACON", "Beacon: OFF", K.Beacon, 0)), "PK_SPOOL_DOWN",
                "The engines are still turning, so the beacon stays on."),
            L(A300FoLine.Row("PK_SEAT_BELTS", "Seat belts: OFF", K.SeatBelts, 0)),
            // The pumps feed a turning engine: off only once both read stopped ([FO-25]).
            Wait("PK_ENGINES_STOPPED", "Engines stopped", K.EnginesStopped, On, 120),
            Requires(Also(L(A300FoLine.Rows("PK_FUEL_PUMPS", "Fuel pumps: OFF", "FO_FUEL_PUMPS_OFF")), "PKC_FUEL_PUMPS"),
                "PK_ENGINES_STOPPED", "The engines are not read stopped, so the fuel pumps stay on."),
            Also(L(A300FoLine.Rows("PK_HEAT", "Window and probe heat: OFF", "FO_WINDOW_PROBE_HEAT_OFF")), "PKC_HEAT"),
            Captain("PK_CPT_IRS", "IRS reset or off as required"),
            // iniBuilds: "Brake Fans: OFF < 100 deg C", tested on both brake temperatures below 100.
            Leave(L(A300FoLine.Row("PK_BRAKE_FANS", "Brake fans: OFF below 100 degrees", K.BrakeFan, 0)),
                s => !Is(s, K.BrakesBelow100, On), "The brakes are not below 100 degrees yet, so the brake fans stay on."),
            Captain("PK_CPT_CHOCKS", "Chocks as required"),
        },
    };

    private static Flow Securing(Func<A300FoLine, Step> L) => new()
    {
        Id = "SECURING", Name = "Securing",
        Description = "IRS, crew oxygen and exterior lights off, APU bleed and APU off, emergency exit lights disarmed, batteries off.",
        Steps = new()
        {
            OnGroundGuard("SC_ON_GROUND"),
            EnginesStoppedGuard("SC_ENGINES_STOPPED"),
            Also(L(A300FoLine.Rows("SC_IRS", "IRS 1, 2 and 3: OFF", "FO_IRS_OFF")), "SCC_NAV"),
            Also(L(A300FoLine.Row("SC_OXYGEN", "Crew oxygen supply: OFF", K.CrewOxygen, 0)), "SCC_OXYGEN"),
            L(A300FoLine.Rows("SC_EXT_LIGHTS", "Exterior lights: OFF", "FO_EXT_LIGHTS_OFF")),
            Also(L(A300FoLine.Row("SC_APU_BLEED", "APU bleed: OFF", K.ApuBleed, 0)), "SCC_APU_BLEED"),
            Captain("SC_CPT_EXT_POWER", "External power as required"),
            Also(L(A300FoLine.Row("SC_APU_MASTER", "APU master: OFF", K.ApuMaster, 0)), "SCC_APU_BATT"),
            // The batteries go only once the APU has run down (the fleet's Securing waits on its APU the same way):
            // an APU that never ran reads 0 and passes at once; an unread speed times out and the flow carries on.
            Wait("SC_APU_WAIT", "APU to wind down", K.ApuSpeed, v => v < A300FoStateEvaluator.ApuStoppedPercent, 150),
            Also(L(A300FoLine.Row("SC_EMER_EXIT", "Emergency exit lights: DISARM", K.EmerExit, 2)), "SCC_EMER_EXIT"),
            Also(L(A300FoLine.Rows("SC_BATTERIES", "Batteries 1, 2 and 3: OFF", "FO_BATTERIES_OFF")), "SCC_APU_BATT"),
        },
    };

    // ================================================================== builders

    private static bool On(double v) => v >= 0.5;
    private static bool Off(double v) => v < 0.5;

    /// <summary>A field read and true; NaN is never true ([FO-12]'s unknown-is-not-done).</summary>
    private static bool Is(A300FoStateEvaluator s, string field, Func<double, bool> done)
    {
        double v = s.GetValue(field);
        return !double.IsNaN(v) && done(v);
    }

    private static bool IrsNotAligned(A300FoStateEvaluator s) => !Is(s, A300FoKeys.IrsAllAligned, On);

    private const string IrsAligningText =
        "The IRS is still aligning, so the pitch trim and yaw damper levers stay off. Tick the line once it has aligned.";

    /// <summary>A line's step: its writes (or its sequence), "Already set" on its own field, and its action-group line.</summary>
    private static Step StepFor(A300FoLine line)
    {
        var step = line.SequenceKey != null ? Sw(line.Id, line.Label, line.SequenceKey)
            : line.Writes.Count == 1 ? Sw(line.Id, line.Label, line.Writes[0].Key, line.Writes[0].Target)
            : new Step
            {
                Id = line.Id, Label = line.Label,
                ActionType = FlowStepActionType.SetSwitchMultiple,
                MultiActions = line.Writes.Select(w => (w.Key, (int?)w.Target)).ToList(),
                FailurePolicy = FlowStepFailurePolicy.Skip,
            };
        if (line.Field is string field && line.Done is { } done)
            step.SkipCondition = s => Is(s, field, done);
        if (!line.LatchesOnField)
            step.CompletesChecklistItemId = line.Id;
        return step;
    }

    private static Step Sw(string id, string label, string key, int target = 1) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = key,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Wait(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        // NaN is never true: an unread field waits, then times out.
        Condition = v => !double.IsNaN(v) && condition(v),
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    /// <summary>A read-only check of something the Captain sets: it never writes; on a miss its line stays live.</summary>
    private static Step Check(string id, string label, string field, Func<double, bool> condition, int timeoutSec)
        => Wait(id, label, field, condition, timeoutSec);

    /// <summary>Stops a cold-and-dark flow unless both engines read stopped (fuel levers read off, N2 below 10).</summary>
    private static Step EnginesStoppedGuard(string id)
        => Wait(id, "Engines stopped", A300FoKeys.EnginesStopped, On, 5, FlowStepFailurePolicy.Stop);

    /// <summary>Stops a flow unless the aircraft reads on the ground (unread fails it) ([FO-25]).</summary>
    private static Step OnGroundGuard(string id)
        => Wait(id, "On the ground", A300FoKeys.OnGround, On, 5, FlowStepFailurePolicy.Stop);

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
    };

    private static Step Skip(Step step, Func<A300FoStateEvaluator, bool> condition)
    {
        step.SkipCondition = condition;
        return step;
    }

    private static Step Stop(Step step)
    {
        step.FailurePolicy = FlowStepFailurePolicy.Stop;
        return step;
    }

    private static Step Leave(Step step, Func<A300FoStateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    private static Step Requires(Step step, string stepId, string text)
    {
        step.RequiresStepId = stepId;
        step.RequiresStepSkipText = text;
        return step;
    }

    /// <summary>The read-back lines this step also delivers ([FO-6]): ticked with it, kept out of the latch when skipped.</summary>
    private static Step Also(Step step, params string[] readBackIds)
    {
        step.AlsoCompletesChecklistItemIds = readBackIds;
        return step;
    }

    /// <summary>A check or wait that delivers one read-back line.</summary>
    private static Step Links(Step step, string readBackId)
    {
        step.CompletesChecklistItemId = readBackId;
        return step;
    }
}
