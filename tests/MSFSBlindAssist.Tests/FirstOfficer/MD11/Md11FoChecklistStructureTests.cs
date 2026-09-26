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

    // ---------------- TFDi's normal checklist, verbatim ----------------

    private static string[] Labels(string groupId) => Groups.Single(g => g.Id == groupId).Items.Select(i => i.Label).ToArray();

    [Fact]
    public void CockpitEntry_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "Weather Radar: Off", "Fuel Switches: Off", "Parking Brake: Set / Chocks", "Flap/Slats Handle: Up and Retracted",
        "Gear Handle: Down", "Fuel Dump Switches: Covered and Off", "Manifold Drain Switches: Covered and Off",
        "Emergency Power Selector: Off",
    }, Labels("COCKPIT_ENTRY_CL"));

    [Fact]
    public void Preflight_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "Battery Switch: On", "External Power Switches: As Required", "Engine/APU Fire Test: Perform",
        "Engine/APU Fire Test: All 3 fire handles — Illuminates", "Engine/APU Fire Test: APU fire handle — Illuminates",
        "Engine/APU Fire Test: All 3 Engine Fuel Shutoff Switches — Illuminates",
        "Engine/APU Fire Test: Both Master Warnings — Illuminates", "Engine/APU Fire Test: Fire Bell — Sounds",
        "Engine/APU Fire Test: Level 3 alert messages on EAD — Displayed",
        "Master Warning: Press", "Fuel Used: Reset", "Annunciator Lights: Test/Check", "Annunciator Bright/Dim Switch: Select",
        "Cargo Fire: Manual Test", "Cargo Fire: FWD/AFT Heat/Smoke lights — Illuminates",
        "Cargo Fire: Manual Test switch — Illuminates", "Cargo Fire: All agent discharge lights — Illuminates",
        "Cargo Fire: FWD/AFT Flow Switch DISAG lights — Illuminates", "Cargo Fire: Both Master Warnings — Illuminates",
        "Cargo Fire: CRG FIRE LWR FWD + CRG FIRE LWR AFT on EAD — Displayed",
        "IRU Switches: NAV", "IRU Alignment: Initialize", "Cockpit Voice Recorder: Test",
        "Galley Bus Panel: All lights extinguished", "Cargo Temperature Selectors: As required",
        "FADEC/Engine Ignition Panel Lights: Off Except ENG IGN OFF",
        "Hydraulic Test: Perform", "Hydraulic Test: Observe on SD — Normal",
        "Electrical Panel: Check", "Electrical Panel: Manual Light — Off",
        "Smoke Elec/Air Source: Normal", "Drive 1/2/3 + CAB BUS: Guarded", "Emergency Power Selector: Armed, No Light",
        "Air Panel: Verify", "Air Panel: Manual Light — Off", "Air Panel: ECON Light — Off", "Air Panel: TRIM AIR OFF Light — Off",
        "Cabin Outflow Valve: Open", "Air Conditioning: Establish", "Temperature Selectors: As required",
        "Fuel Panel Manual Light: Off", "Fuel Quantity: Test",
        "Fuel Quantity: 188880 Displayed — Checked", "Fuel Quantity: 10500 Displayed in each tank — Checked",
        "Emergency Light Switch: Arm", "No Smoking Switch: On", "Seat Belt Switch: Off", "Exterior Lights: As required",
        "EVAC Panel: Armed and Guarded", "GPWS Test Switch: Test and Guarded",
        "AFS Panel: Check", "AFS Panel: All lights — Off", "AFS Panel: Flap limit — Normal", "AFS Panel: Elevator feel — Normal",
        "Cabin Pressurization Panel: Check", "Cabin Pressurization Panel: In AUTO operation — Checked",
        "Cabin Pressurization Panel: Valve — Open", "Cabin Pressurization Panel: Ditching switch — Guarded",
        "Cabin Pressurization Panel: Ditching light — Extinguished",
        "Anti-Ice Panel: Check", "Anti-Ice Panel: All lights — Off",
        "Windshield Anti-Ice/Defog: NORM/ON/DEFOG Light Extinguished",
        "Altimeter: Set QNH", "IAS: Auto", "HDG: Auto", "FEET: Auto", "Bank Selector: Auto", "Static Air Switch: Normal",
        "Source Input Selector Lights: Off", "PDF/ND/EAD/SD: Check", "PDF/ND/EAD/SD: No faults — Checked",
        "PDF/ND/EAD/SD: Altimeters — As desired", "PDF/ND/EAD/SD: Time — Correct",
        "PDF/ND/EAD/SD: Oil Quantity — >16 quarts", "PDF/ND/EAD/SD: PDF FMA annunciator — TAKEOFF",
        "Gear Handle: Down, 4 green", "Auto Brake Switch: RTO", "Overboost Breakout bar: Full AFT",
        "Throttles: Check", "Throttles: Travel and aural warnings — Checked", "Throttles: Closed", "Reverse Levers: Down",
        "Dial-a-Flap: To Setting", "Engine Start Buttons: Pressed In", "Fuel Switches: Off", "SDCP: Cue Lights and Clear",
        "Radio Panels: As desired", "Weather Radar: Test and Off", "Transponder: Set", "Rudder Trim: Zero",
        "Aileron Trim: Zero", "Air Driven Generator (ADG): Handle Down and Wired",
    }, Labels("PREFLIGHT_CL"));

    [Fact]
    public void BeforeStart_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "APU: Start", "FMS: Initialized and Checked", "FMS: Weights — Confirmed", "FMS: Headwind/Tailwind — Confirmed",
        "FMS: Runway Slope — Confirmed", "FMS: Temperature — Confirmed", "FMS: V-Speeds — Confirmed",
        "IRS: Nav and Aligned", "EIS/BUGS: Set", "Seat Belt Sign: On", "External Power Switches: Off",
        "AUX HYD Pump 1: On", "Engine Ignition: A or B", "Beacon Light: On",
    }, Labels("BEFORE_START_CL"));

    [Fact]
    public void EngineStart_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "Engine 3 Start Switch: Pull", "Fuel Level: On", "Wait until N2 >= 15%: Confirmed", "Oil Pressure: Verify Rising",
        "Engine Ignition: Verify <= 25 Seconds", "EGT: Check", "Start Valve: Verify Closed at 45% N2",
        "Engine 3: Verify Stabilized", "Engine 3: N1 — Around 20%", "Engine 3: EGT — Around 400C", "Engine 3: N2 — Around 60%",
        "Engine 3: Oil — +/- 2 quarts after start", "Engine 3: Fuel Flow — Around 1200 at sea level",
        "Engine Anti-Ice: As required", "Repeat for Engine 1: Checked", "Repeat for Engine 2: Checked",
    }, Labels("ENGINE_START_CL"));

    [Fact]
    public void AfterStart_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "Engine Anti-Ice: As required", "APU: Off", "Flaps: Set", "Config Page: Select", "Flight Controls: Check",
        "Stab Trim: Set", "Taxi Lights: On",
    }, Labels("AFTER_START_CL"));

    [Fact]
    public void BeforeTakeoff_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "EIS/BUGS: Verify", "Runway: Verify", "Anti-Ice: Wings Off, Engine as required", "Stab Trim: Verify Green Band and Config",
        "Spoilers: Armed", "Autobrake: RTO", "Flaps and Slats: Set for takeoff", "Takeoff Data and Bugs: Verify", "EAD: Checked",
        "Landing Lights: On", "High Intensity Lights (Strobes): On", "Flight Modes: As required",
        "Flight Modes: NAV — Armed", "Flight Modes: PROF — Armed", "Flight Modes: AUTOFLIGHT — On", "Flight Modes: TOGA power — Set",
    }, Labels("BEFORE_TAKEOFF_CL"));

    [Fact]
    public void AfterTakeoff_IsTfdisVerbatim() => Assert.Equal(new[]
    {
        "Gear: Up", "Spoilers: Disarm", "Autobrake: Verify", "Flaps/Slats: Up and Retracted", "EAD: Check No Alerts",
    }, Labels("AFTER_TAKEOFF_CL"));

    [Fact]
    public void InFlightPhases_AreTfdisVerbatim()
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
    public void GroundPhases_AreTfdisVerbatim()
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
