using MSFSBlindAssist.FirstOfficer.Models;
using K = MSFSBlindAssist.FirstOfficer.A300.A300FoKeys;
using V = MSFSBlindAssist.FirstOfficer.A300.A300FoLiveValues;

namespace MSFSBlindAssist.FirstOfficer.A300;

using Item = ChecklistItem<A300FoActionExecutor, A300FoStateEvaluator>;
using Group = ChecklistGroup<A300FoActionExecutor, A300FoStateEvaluator>;
using Flow = FlowDefinition<A300FoStateEvaluator>;

/// <summary>
/// The A300 First Officer checklists. Two layers, as on every profile. An ACTION group per flow is built from the
/// flow's own lines (A300FoFlowDefinitions.BuildWithLines), so the two can never drift: ticking a line runs the
/// same write. The READ-BACK (*_CL) groups are the cockpit's A300-600 Normal Checklist card, every line kept in its
/// order, action-free ([FO-3]). A line the aircraft can read ticks itself; the rest are the Captain's, and where the
/// card leaves a blank the First Officer speaks the real value on the tick (ChecklistItem.LiveValue).
/// </summary>
public static class A300FoChecklistDefinitions
{
    public static List<Group> Build()
    {
        var (flows, lines) = A300FoFlowDefinitions.BuildWithLines();
        Group Of(string id) => ActionGroup(flows.Single(f => f.Id == id), lines);
        return new()
        {
            Of("POWER_UP"), Of("PREFLIGHT"),
            Of("BEFORE_START"), BeforeStartCL(), BeforeStartBelowCL(),
            Of("ENGINE_START"),
            Of("AFTER_START"), AfterStartCL(),
            Of("BEFORE_TAKEOFF"), BeforeTakeoffCL(),
            Of("LINE_UP"), LineUpCL(),
            Of("AFTER_TAKEOFF"), AfterTakeoffCL(),
            Of("DESCENT"),
            Of("APPROACH"), ApproachCL(),
            Of("LANDING"), LandingCL(),
            Of("AFTER_LANDING"), AfterLandingCL(),
            Of("PARKING"), ParkingCL(),
            Of("SECURING"), SecuringCL(),
        };
    }

    /// <summary>A flow's lines in its order, and its unconditional Captain reminders; waits, checks and the
    /// reminders that speak only when needed stay flow-only.</summary>
    private static Group ActionGroup(Flow flow, IReadOnlyDictionary<string, A300FoLine> lines) => new()
    {
        Id = flow.Id, Name = flow.Name,
        Items = flow.Steps
            .Select(s => lines.TryGetValue(s.Id, out var line) ? Guarded(ItemFor(line, flow.Id), s, flow)
                : s.ActionType == FlowStepActionType.CaptainReminder && s.SkipCondition == null
                    ? Captain(s.Id, flow.Id, s.ReminderText ?? s.Label)
                    : null)
            .Where(i => i != null).Select(i => i!).ToList(),
    };

    /// <summary>A tick runs the same write as its flow step, so it refuses where the step would: the step's own
    /// leave-alone rule, the wait it depends on read as not yet true (unread included), and, for a line that cuts
    /// the engines, the pumps, the IRS or the batteries, the guards its flow opens with ([FO-25], <see cref="CutGuards"/>).</summary>
    private static Item Guarded(Item item, FlowStep<A300FoStateEvaluator> step, Flow flow)
    {
        Func<A300FoStateEvaluator, bool>? dependency = null;
        if (step.RequiresStepId is string id)
        {
            var wait = flow.Steps.Single(s => s.Id == id);
            if (wait.ConditionFieldName is not string field || wait.Condition is not { } done)
                throw new InvalidOperationException($"{step.Id} depends on {id}, which is not a wait");
            dependency = s => !done(s.GetValue(field));
        }
        var own = step.LeaveAloneWhen;
        var cut = CutGuards.TryGetValue(item.Id, out var g) ? g : default;
        if (dependency == null && own == null && cut.When == null)
            return item;
        item.LeaveAloneWhen = s => (dependency?.Invoke(s) ?? false) || (own?.Invoke(s) ?? false) || (cut.When?.Invoke(s) ?? false);
        item.LeaveAloneText = cut.Text ?? (own != null ? step.LeaveAloneText : step.RequiresStepSkipText);
        return item;
    }

    /// <summary>
    /// The ticks that cut ([FO-25]): refused, and the reason spoken, where the flow would not reach them. On the
    /// ground with both engines read stopped (fuel levers off, N2 below 10), the cold-and-dark fuel levers, the
    /// fuel pumps, the IRS and the batteries go off; airborne, with an engine turning, or either unread, they stay.
    /// The Parking fuel levers cut a running engine by design, so they need the ground and something to take the
    /// electrics, as the flow's PK_POWER_SOURCE does.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (Func<A300FoStateEvaluator, bool>? When, string? Text)> CutGuards =
        new Dictionary<string, (Func<A300FoStateEvaluator, bool>?, string?)>(StringComparer.Ordinal)
        {
            ["PU_FUEL_LEVERS"] = (NotParked, "Fuel levers left on: not read on the ground, or an engine is not read stopped."),
            ["PK_FUEL_LEVERS"] = (s => !On(s, K.OnGround) || !On(s, K.ApuOrExtPower),
                "Fuel levers left on: not on the ground, or no APU or external power to take the electrics."),
            ["PK_FUEL_PUMPS"] = (NotParked, "Fuel pumps left on: not read on the ground, or an engine is not read stopped."),
            ["SC_IRS"] = (NotParked, "IRS left on: not read on the ground, or an engine is not read stopped."),
            ["SC_BATTERIES"] = (NotParked, "Batteries left on: not read on the ground, or an engine is not read stopped."),
        };

    private static bool NotParked(A300FoStateEvaluator s) => !On(s, K.OnGround) || !On(s, K.EnginesStopped);

    private static bool On(A300FoStateEvaluator s, string field) => s.GetValue(field) is var v && !double.IsNaN(v) && v >= 0.5;

    private static Item ItemFor(A300FoLine line, string groupId)
    {
        if (line.Field == null)
            return new Item
            {
                Id = line.Id, GroupId = groupId, Label = line.Label,
                Type = ChecklistItemType.Actionable,
                ManualCompletionAllowed = true,
                CheckAction = (e, _) => e.Set(line.SequenceKey!, 1),
            };
        var writes = line.Writes.ToArray();
        var item = Read(line.Id, groupId, line.Label, line.Field, line.Done!);
        item.CheckAction = (e, _) => e.SetAll(writes);
        if (line.LatchesOnField)
            item.RevertBehavior = RevertBehavior.StayComplete;   // the engine-start latch ([FO-8], A300FoLine.Starter)
        return item;
    }

    // ================================================================== read-back groups (the cockpit card)

    private static Group BeforeStartCL() => ReadBack("BEFORE_START_CL", "Before Start Checklist",
        Captain("BSC_COCKPIT_PREP", G, "Cockpit prep: COMPLETED"),
        Captain("BSC_FUEL", G, "Fuel quantity: CHECKED", s => V.Fuel(s.ReadValue)),
        Captain("BSC_TO_DATA", G, "Takeoff data: SET, both"),
        Captain("BSC_LDG_ELEV", G, "Landing elevation: SET", s => V.LandingElevation(s.ReadValue)),
        Captain("BSC_ALTIMETERS", G, "Altimeters: SET, both", s => V.Altimeters(s.ReadValue)),
        Read("BSC_BRAKES", G, "Brakes and anti-skid: NORM and ON", K.AntiSkid, v => v < 0.5),
        Captain("BSC_DOORS", G, "Windows and doors: CLOSED, both"),
        Captain("BSC_ACARS", G, "Start ACARS"));

    private static Group BeforeStartBelowCL() => ReadBack("BEFORE_START_BELOW_CL", "Before Start Checklist, Below the Line",
        Read("BSB_BEACON", G, "Beacon: ON", K.Beacon, v => v >= 0.5),
        Captain("BSB_PARKING_BRAKE", G, "Parking brake: AS REQUIRED", s => V.ParkingBrake(s.ReadValue)));

    private static Group AfterStartCL() => ReadBack("AFTER_START_CL", "After Start Checklist",
        Captain("ASC_PITCH_TRIM", G, "Pitch trim: SET", s => V.PitchTrim(s.ReadValue)),
        Read("ASC_RUDDER_TRIM", G, "Rudder trim: ZERO", K.RudderTrimZero, v => v >= 0.5),
        Read("ASC_SPOILERS", G, "Spoilers: ARMED", K.SpoilersArm, v => v >= 0.5),
        Read("ASC_FLAPS", G, "Slats and flaps: SET", K.FlapsTakeoff, v => v >= 0.5, s => V.Flaps(s.ReadValue)),
        Captain("ASC_ECAM_STATUS", G, "ECAM status: CHECKED", s => V.EcamStatus(s.ReadValue)),
        Captain("ASC_ANTI_ICE", G, "Anti-ice: AS REQUIRED", s => V.AntiIce(s.ReadValue)),
        Captain("ASC_HAND_SIGNAL", G, "Hand signal: RECEIVED"));

    private static Group BeforeTakeoffCL() => ReadBack("BEFORE_TAKEOFF_CL", "Before Takeoff Checklist",
        Captain("BTC_FLIGHT_CONTROLS", G, "Flight controls: CHECKED, both"),
        Captain("BTC_FLIGHT_INST", G, "Flight instruments: CHECKED, both"),
        Captain("BTC_BRIEFING", G, "Briefing: CONFIRMED"),
        Captain("BTC_SPEEDS", G, "V1, VR, V2 and flex temperature: SET, both", s => V.Speeds(s.ReadValue)),
        Read("BTC_FLAPS", G, "Slats and flaps: SET, both", K.FlapsTakeoff, v => v >= 0.5, s => V.Flaps(s.ReadValue)),
        Captain("BTC_TO_CONFIG", G, "Takeoff config: NORMAL FOR TAKEOFF"));

    private static Group LineUpCL() => ReadBack("LINE_UP_CL", "Before Takeoff Checklist, Below the Line",
        Read("LUC_TRANSPONDER", G, "Transponder: SET", K.TransponderOn, v => v >= 0.5, s => V.Transponder(s.ReadValue)),
        Read("LUC_TCAS", G, "TCAS: TA/RA", K.Transponder, v => Math.Abs(v - 2) < 0.5),
        Read("LUC_AUTOBRAKE", G, "Autobrake: MAX", K.AutobrakeLevel, v => Math.Abs(v - 3) < 0.5),
        Captain("LUC_IGNITION", G, "Ignition: AS REQUIRED", s => V.Ignition(s.ReadValue)));

    private static Group AfterTakeoffCL() => ReadBack("AFTER_TAKEOFF_CL", "After Takeoff and Climb Checklist",
        Read("ATC_FLAPS", G, "Slats and flaps: RETRACTED", K.Flaps, v => v < 0.5),
        Read("ATC_GEAR", G, "Landing gear: UP/NEUTRAL", K.GearUp, v => v >= 0.5),
        Read("ATC_PACKS", G, "Packs: ON", "FO_PACKS_ON", v => v >= 0.5),
        Captain("ATC_ALTIMETERS", G, "Altimeters: SET, both", s => V.Altimeters(s.ReadValue)));

    private static Group ApproachCL() => ReadBack("APPROACH_CL", "Approach Checklist",
        Read("APC_SIGNS", G, "Signs: SET", "FO_SIGNS_SET", v => v >= 0.5),
        Captain("APC_BRIEFING", G, "Briefing: CONFIRMED"),
        Captain("APC_ECAM_STATUS", G, "ECAM status: CHECKED", s => V.EcamStatus(s.ReadValue)),
        Captain("APC_ALTIMETERS", G, "Altimeters: SET, both", s => V.Altimeters(s.ReadValue)),
        Captain("APC_MINIMUMS", G, "Minimums: SET, both", s => V.Minimums(s.ReadValue)),
        Read("APC_IGNITION", G, "Ignition: CONT RELIGHT", K.Ignition, v => Math.Abs(v - 4) < 0.5),
        Captain("APC_LDG_ELEV", G, "Landing elevation: SET", s => V.LandingElevation(s.ReadValue)));

    private static Group LandingCL() => ReadBack("LANDING_CL", "Landing Checklist",
        Read("LDC_GEAR", G, "Landing gear: DOWN", K.GearDown, v => v >= 0.5),
        Captain("LDC_AUTOBRAKE", G, "Autobrake: AS REQUIRED", s => V.Autobrake(s.ReadValue)),
        Read("LDC_ANTI_SKID", G, "Anti-skid: CHECKED", K.AntiSkid, v => v < 0.5),
        Captain("LDC_FLAPS", G, "Slats and flaps: SET", s => V.Flaps(s.ReadValue)),
        Read("LDC_SPOILERS", G, "Spoilers: ARMED", K.SpoilersArm, v => v >= 0.5));

    private static Group AfterLandingCL() => ReadBack("AFTER_LANDING_CL", "After Landing Checklist",
        Read("ALC_FLAPS", G, "Slats and flaps: RETRACTED", K.Flaps, v => v < 0.5),
        Captain("ALC_TRANSPONDER", G, "Transponder: AS REQUIRED", s => V.Transponder(s.ReadValue)),
        Read("ALC_WXR", G, "Weather radar: OFF", K.WxrSystem, v => Math.Abs(v - 1) < 0.5),
        Read("ALC_SPOILERS", G, "Spoilers: DISARMED", K.SpoilersArm, v => v < 0.5),
        Read("ALC_APU", G, "APU: STARTED", K.ApuAvailLight, v => v >= 0.5));

    private static Group ParkingCL() => ReadBack("PARKING_CL", "Parking Checklist",
        Captain("PKC_APU_BLEED", G, "APU bleed: AS REQUIRED", s => V.ApuBleed(s.ReadValue)),
        Read("PKC_ENGINES", G, "Engines: OFF", "FO_FUEL_LEVERS_OFF", v => v >= 0.5),
        Captain("PKC_DIFF_PRESS", G, "Differential pressure: CHECK ZERO", s => V.DeltaP(s.ReadValue)),
        Captain("PKC_LIGHTS_SIGNS", G, "Lights and signs: AS REQUIRED"),
        Read("PKC_FUEL_PUMPS", G, "Fuel pumps: OFF", "FO_FUEL_PUMPS_OFF", v => v >= 0.5),
        Read("PKC_HEAT", G, "Window and probe heat: OFF", "FO_WINDOW_PROBE_HEAT_OFF", v => v >= 0.5),
        Captain("PKC_PARKING_BRAKE", G, "Parking brake and chocks: AS REQUIRED", s => V.ParkingBrake(s.ReadValue)));

    private static Group SecuringCL() => ReadBack("SECURING_CL", "Securing Aircraft Checklist",
        Read("SCC_NAV", G, "Nav systems: OFF", "FO_IRS_OFF", v => v >= 0.5),
        Read("SCC_OXYGEN", G, "Oxygen: OFF", K.CrewOxygen, v => v < 0.5),
        Read("SCC_APU_BLEED", G, "APU bleed: OFF", K.ApuBleed, v => v < 0.5),
        Read("SCC_EMER_EXIT", G, "Emergency exit lights: DISARMED", K.EmerExit, v => Math.Abs(v - 2) < 0.5),
        Read("SCC_APU_BATT", G, "APU and batteries: OFF", "FO_APU_AND_BATTERIES_OFF", v => v >= 0.5));

    // ================================================================== builders

    /// <summary>Placeholder group id: <see cref="ReadBack(string, string, Item[])"/> stamps the real one.</summary>
    private const string G = "";

    private static Group ReadBack(string id, string name, params Item[] items)
    {
        foreach (var item in items)
            item.GroupId = id;
        return new Group { Id = id, Name = name, Items = items.ToList() };
    }

    /// <summary>An auto line: ticks from the aircraft and follows it ([FO-8]'s RevertToState); action-free until
    /// <see cref="ItemFor"/> gives an action-group line its write.</summary>
    private static Item Read(string id, string groupId, string label, string field, Func<double, bool> condition,
        Func<A300FoStateEvaluator, string?>? live = null) => new()
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
        LiveValue = live,
    };

    private static Item Captain(string id, string groupId, string label, Func<A300FoStateEvaluator, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = label,
        LiveValue = live,
    };
}
