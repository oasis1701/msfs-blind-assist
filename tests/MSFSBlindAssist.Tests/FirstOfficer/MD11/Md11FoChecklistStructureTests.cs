using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.MD11;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

public class Md11FoChecklistStructureTests
{
    private static readonly List<ChecklistGroup<Md11FoActionExecutor, Md11FoStateEvaluator>> Groups = Md11FoChecklistDefinitions.Build();
    private static readonly List<FlowDefinition<Md11FoStateEvaluator>> Flows = Md11FoFlowDefinitions.Build();

    [Fact]
    public void GroupIds_InFlightOrder()
        => Assert.Equal(new[]
        {
            "POWER_UP", "COCKPIT_ENTRY_CL", "PREFLIGHT", "PREFLIGHT_CL", "BEFORE_START", "BEFORE_START_CL",
            "ENGINE_START", "ENGINE_START_CL", "AFTER_START", "AFTER_START_CL", "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL",
            "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "PASSING_10000_CL", "TRANSITION_ALTITUDE_CL", "CRUISE_CL",
            "TRANSITION_LEVEL_CL", "DESCENT", "DESCENT_CL", "BEFORE_LANDING", "BEFORE_LANDING_CL",
            "AFTER_LANDING", "AFTER_LANDING_CL", "PARKING", "PARKING_CL", "SHUTDOWN", "SHUTDOWN_CL",
        }, Groups.Select(g => g.Id));

    [Fact]
    public void NoReadbackGroupHasACheckAction()
    {
        foreach (var g in Groups.Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)))
            foreach (var i in g.Items)
                Assert.True(i.CheckAction == null, $"{g.Id}/{i.Id}: *_CL items are action-free");
    }

    [Fact]
    public void ItemIds_AreUnique()
    {
        var ids = Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void LabelsAreUniqueWithinEachGroup()
    {
        foreach (var g in Groups)
        {
            var labels = g.Items.Select(i => i.Label).ToList();
            Assert.True(labels.Count == labels.Distinct().Count(), $"{g.Id} repeats a label");
        }
    }

    [Fact]
    public void EveryFlowTick_NamesAnItemOfOneOfTheFlowsOwnGroups()
    {
        foreach (var f in Flows)
        {
            var related = f.RelatedChecklistGroupIds.ToHashSet();
            foreach (var s in f.Steps.Where(s => s.CompletesChecklistItemId != null))
                Assert.True(Groups.Any(g => related.Contains(g.Id) && g.Items.Any(i => i.Id == s.CompletesChecklistItemId)),
                    $"{f.Id}.{s.Id} ticks {s.CompletesChecklistItemId}, not an item of the flow's own groups");
        }
    }

    [Fact]
    public void EveryFlowRelatedGroup_Exists()
    {
        var ids = Groups.Select(g => g.Id).ToHashSet();
        foreach (var f in Flows)
            foreach (var gid in f.RelatedChecklistGroupIds)
                Assert.Contains(gid, ids);
    }

    /// <summary>
    /// A finished flow ticks and LATCHES every item it did not explicitly fail, so an actionable
    /// item with no delivering step would read complete with its switch untouched — a silent false
    /// completion a blind pilot cannot see (CLAUDE.md, the BS_TRANSPONDER lesson).
    /// </summary>
    [Fact]
    public void EveryActionableItemInAnActionGroup_HasADeliveringFlowStep()
    {
        foreach (var f in Flows)
        {
            var delivered = f.Steps.Select(s => s.CompletesChecklistItemId).Where(id => id != null).ToHashSet();
            var group = Groups.SingleOrDefault(g => g.Id == f.Id);
            if (group == null) continue;
            foreach (var i in group.Items.Where(i => i.CheckAction != null))
                Assert.True(delivered.Contains(i.Id), $"{group.Id}/{i.Id} has a CheckAction but no flow step delivers it");
        }
    }

    [Fact]
    public void AutoItemsAreLiveMirrors_ExceptTheEngineStartSwitches()
    {
        var latched = new[] { "ES_E1_START", "ES_E2_START", "ES_E3_START" };
        var auto = Groups.SelectMany(g => g.Items).Where(i => i.Type == ChecklistItemType.AutoDetectable).ToArray();
        Assert.All(auto.Where(i => !latched.Contains(i.Id)), i => Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior));
        Assert.All(auto.Where(i => latched.Contains(i.Id)), i => Assert.Equal(RevertBehavior.StayComplete, i.RevertBehavior));
        Assert.Equal(latched.Length, auto.Count(i => latched.Contains(i.Id)));
    }

    [Fact]
    public void EveryItemField_IsSyntheticOrRegistered()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        var synthetic = Md11FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new Md11FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.StateFieldName != null))
            foreach (var field in i.AdditionalStateFields.Prepend(i.StateFieldName!))
            {
                if (synthetic.Contains(field)) continue;
                Assert.True(vars.TryGetValue(field, out var def), $"{i.Id}: field {field} not registered");
                Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
                if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                    Assert.True(polled.Contains(field), $"{i.Id}: OnRequest field {field} is not polled");
            }
    }

    [Fact]
    public void EveryFlowConditionField_IsSyntheticOrRegistered()
    {
        var vars = new TFDiMD11Definition().GetVariables();
        var synthetic = Md11FoStateEvaluator.SyntheticKeys.ToHashSet();
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.ConditionFieldName != null))
            Assert.True(synthetic.Contains(s.ConditionFieldName!) || vars.ContainsKey(s.ConditionFieldName!),
                $"{s.Id}: {s.ConditionFieldName}");
    }

    // ---------------- The read-back checklists ----------------
    // Condensed from TFDi's normal checklist to what a screen-reader pilot can check through the
    // app: switch positions the FO reads back, plus Captain items reachable from MSFSBA (MCDU,
    // altimeters, radios). Sighted-only sub-checks (lamps that illuminate during a test, gauge
    // readings, overboost bar, ADG wiring) are gone; the throttle check stays for its aural
    // warnings. The flows and action groups are unchanged.

    private static string[] Labels(string groupId) => Groups.Single(g => g.Id == groupId).Items.Select(i => i.Label).ToArray();

    [Fact]
    public void CockpitEntry_Labels() => Assert.Equal(new[]
    {
        "Weather Radar: Off", "Fuel Switches: Off", "Parking Brake: Set", "Flap/Slats Handle: Up and Retracted",
        "Gear Handle: Down", "Fuel Dump Switches: Covered and Off", "Manifold Drain Switches: Covered and Off",
        "Emergency Power Selector: Off",
    }, Labels("COCKPIT_ENTRY_CL"));

    [Fact]
    public void Preflight_Labels() => Assert.Equal(new[]
    {
        "Battery: On", "IRS Switches: NAV", "System Tests: Complete", "Electrical System: Auto",
        "Emergency Power: Armed", "Hydraulic System: Auto", "Air System: Auto", "Economy: On", "Fuel System: Auto",
        "Cabin Pressure: Auto", "Engine Ignition: Off", "Emergency Lights: Armed", "No Smoking Signs: On",
        "Seat Belt Signs: Off", "EVAC Switch: Armed and Guarded", "GPWS: Normal and Guarded", "Windshield Anti-Ice: On",
        "Autobrake: RTO", "Dial-A-Flap: Takeoff Setting", "Engine Start Switches: In",
        "Throttles: Travel and Aural Warnings Checked, Closed", "Altimeters: Set QNH",
        "Radios and Transponder: Set",
    }, Labels("PREFLIGHT_CL"));

    [Fact]
    public void BeforeStart_Labels() => Assert.Equal(new[]
    {
        "FMS: Initialized, V-Speeds Set", "IRS: Aligned", "APU: Running", "External Power: Off", "Seat Belt Signs: On",
        "AUX Hydraulic Pump 1: On", "Engine Ignition: A or B", "APU Bleed: On", "Beacon: On",
        "Pushback and Start Clearance: Obtained",
    }, Labels("BEFORE_START_CL"));

    [Fact]
    public void EngineStart_Labels() => Assert.Equal(new[]
    {
        "Engine 3: Running", "Engine 1: Running", "Engine 2: Running", "Engine Start Switches: In",
        "Engine Anti-Ice: As Required",
    }, Labels("ENGINE_START_CL"));

    [Fact]
    public void AfterStart_Labels() => Assert.Equal(new[]
    {
        "APU Bleed: Off", "Flaps: Set", "Spoilers: Armed", "Autobrake: RTO", "Flight Controls: Checked",
        "Stab Trim: Set", "Taxi Light: On",
    }, Labels("AFTER_START_CL"));

    [Fact]
    public void BeforeTakeoff_Labels() => Assert.Equal(new[]
    {
        "Runway and Takeoff Data: Verified", "Flaps and Slats: Set for Takeoff", "Stab Trim: Set", "Wing Anti-Ice: Off",
        "Spoilers: Armed", "Autobrake: RTO", "Transponder: TA/RA", "Landing Lights: On", "Strobes: On",
        "NAV and PROF: Armed", "Auto Flight: On",
    }, Labels("BEFORE_TAKEOFF_CL"));

    [Fact]
    public void AfterTakeoff_Labels() => Assert.Equal(new[]
    {
        "Gear: Up", "Spoilers: Disarmed", "Autobrake: Off", "Flaps and Slats: Up and Retracted",
    }, Labels("AFTER_TAKEOFF_CL"));

    [Fact]
    public void InFlightPhases_Labels()
    {
        Assert.Equal(new[] { "Sterile Cockpit: Chime", "Landing Lights: Off" }, Labels("PASSING_10000_CL"));
        Assert.Equal(new[] { "Altimeters: STD (Pull)", "Dial-a-Flap: Set 15" }, Labels("TRANSITION_ALTITUDE_CL"));
        Assert.Equal(new[] { "FMA: Verify", "FMS: Setup for descent" }, Labels("CRUISE_CL"));
        Assert.Equal(new[] { "Altimeters: Local QNH" }, Labels("TRANSITION_LEVEL_CL"));
        Assert.Equal(new[] { "Landing Lights: On", "Sterile Cockpit: Chime", "Autobrakes: As required" }, Labels("DESCENT_CL"));
        Assert.Equal(new[] { "Gear: Down", "Flaps/Slats: As required", "Spoilers: Armed", "Missed Approach Altitude: Set" },
            Labels("BEFORE_LANDING_CL"));
    }

    [Fact]
    public void GroundPhases_Labels()
    {
        Assert.Equal(new[]
        {
            "Reversers: Stowed", "Landing Lights: Off", "High Intensity Lights (Strobes): Off", "Flaps/Slats: Up and Retracted",
            "Spoilers: Down", "Stabilizer Trim: Set 3 Degrees Up", "Weather Radar: Off", "APU: Start",
        }, Labels("AFTER_LANDING_CL"));
        Assert.Equal(new[]
        {
            "Parking Brakes: Set", "Fuel Switches: Off", "Seat Belt Sign: Off", "External Power: As required",
            "APU: As required", "Anti-Ice: Off", "Engine Ignition: Off", "Exterior Lights: All off, except NAV",
        }, Labels("PARKING_CL"));
        Assert.Equal(new[]
        {
            "Emergency Light Switch: Off", "Emergency Power Switch: Off", "Windshield Anti-Ice and Defog: Off",
            "IRS Switches: Off", "Cargo Temperatures: Off", "Cockpit Lights: Off", "EVAC Control Switch: Off",
            "Packs: Off", "APU: Off", "Battery: Off",
        }, Labels("SHUTDOWN_CL"));
    }
}
