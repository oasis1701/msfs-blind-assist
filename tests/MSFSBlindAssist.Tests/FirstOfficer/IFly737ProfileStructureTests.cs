using System.Linq;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Structural invariants for the iFly 737 MAX8 First Officer checklist data
/// (<see cref="IFly737ChecklistDefinitions"/>) — the checklist half of Task 5's
/// structure test suite. Pure-logic over the data-driven Build() list; no live SDK
/// needed, mirroring the fleet's other *ProfileStructureTests/ChecklistRefinementTests
/// suites.
/// </summary>
public class IFly737ProfileStructureTests
{
    // Same 24 group ids as the PMDG 737 template, in flight order.
    private static readonly string[] ExpectedGroupIds =
    {
        "ELEC_POWER_UP",
        "PREFLIGHT", "PREFLIGHT_CL",
        "BEFORE_START", "BEFORE_START_CL",
        "ENGINE_START",
        "BEFORE_TAXI", "BEFORE_TAXI_CL",
        "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL",
        "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL",
        "DESCENT", "DESCENT_CL",
        "APPROACH", "APPROACH_CL",
        "LANDING", "LANDING_CL",
        "AFTER_LANDING",
        "SHUTDOWN", "SHUTDOWN_CL",
        "SECURE", "SECURE_CL",
        "ELEC_POWER_DOWN",
    };

    private static System.Collections.Generic.List<ChecklistGroup<IFly737ActionExecutor, IFly737StateEvaluator>> Groups()
        => IFly737ChecklistDefinitions.Build();

    [Fact]
    public void AllPmdg737PhaseGroupsExist()
    {
        var groups = Groups();
        var ids = groups.Select(g => g.Id).ToArray();

        // Every expected id is present, no duplicates, and no stray extras — a totality
        // check, not just a subset check, so a typo'd or dropped group id fails loudly.
        Assert.Equal(ExpectedGroupIds.Length, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct().Count());
        foreach (string id in ExpectedGroupIds)
            Assert.Contains(id, ids);

        // Flight order must match exactly — a reordered group would silently change
        // where a pilot lands when opening the checklist tree at the current phase.
        Assert.Equal(ExpectedGroupIds, ids);

        // No group is empty — an empty group is either a typo'd Id (items landed under
        // the wrong group id) or a forgotten Build*() call.
        Assert.All(groups, g => Assert.NotEmpty(g.Items));
    }

    [Fact]
    public void ReadbackGroups_AreActionFree()
    {
        var groups = Groups();
        Assert.Contains(groups, g => g.Id.EndsWith("_CL"));
        foreach (var g in groups.Where(g => g.Id.EndsWith("_CL")))
            Assert.All(g.Items, i => Assert.True(i.CheckAction == null,
                $"{g.Id}.{i.Id} is a readback item but has a non-null CheckAction"));
    }

    [Fact]
    public void AutoItems_AreRevertToState()
    {
        var groups = Groups();
        var autoItems = groups.SelectMany(g => g.Items)
            .Where(i => i.Type == ChecklistItemType.AutoDetectable)
            .ToArray();
        Assert.NotEmpty(autoItems);
        Assert.All(autoItems, i => Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior));
    }

    [Fact]
    public void TakeoffFlaps_LandingAutobrake_Speedbrake_AreReminders()
    {
        var groups = Groups();

        // "Set the takeoff flaps" (Before Taxi) — Captain reminder, never automated.
        var takeoffFlaps = groups.First(g => g.Id == "BEFORE_TAXI").Items.Single(i => i.Id == "BT_FLAPS");
        Assert.Equal(ChecklistItemType.CaptainReminder, takeoffFlaps.Type);
        Assert.Null(takeoffFlaps.CheckAction);

        // "Set the landing autobrake" (Descent) — Captain reminder.
        var landingAutobrake = groups.First(g => g.Id == "DESCENT").Items.Single(i => i.Id == "DSA_AB");
        Assert.Equal(ChecklistItemType.CaptainReminder, landingAutobrake.Type);
        Assert.Null(landingAutobrake.CheckAction);

        // Speedbrake ARM (Landing) — Captain reminder on this aircraft (unverified lever
        // write scale, deliberately read-only).
        var speedbrakeArm = groups.First(g => g.Id == "LANDING").Items.Single(i => i.Id == "LDA_SPDBRK");
        Assert.Equal(ChecklistItemType.CaptainReminder, speedbrakeArm.Type);
        Assert.Null(speedbrakeArm.CheckAction);

        // Its Landing Checklist twin auto-detects (the iFly DOES expose a speedbrake-armed
        // readback the PMDG NG3 struct lacks) but must still be action-free per the _CL
        // invariant checked above.
        var speedbrakeReadback = groups.First(g => g.Id == "LANDING_CL").Items.Single(i => i.Id == "LDC_SPDBRK");
        Assert.Equal(ChecklistItemType.AutoDetectable, speedbrakeReadback.Type);
        Assert.Null(speedbrakeReadback.CheckAction);
        Assert.Equal("SPEED_BRAKE_ARMED_Light_Status", speedbrakeReadback.StateFieldName);
    }

    [Fact]
    public void MergedFuelPumpItems()
    {
        var groups = Groups();

        // Preflight: single "Fuel pumps: OFF" item, action-based (not a synthetic — a plain
        // all-fields-off condition), covering all six wing+center switches.
        var preflight = groups.First(g => g.Id == "PREFLIGHT").Items.Single(i => i.Id == "PF_FUEL_OFF");
        Assert.Equal("Fuel pumps: OFF", preflight.Label);
        Assert.Equal(ChecklistItemType.AutoDetectable, preflight.Type);
        Assert.Equal(5, preflight.AdditionalStateFields.Count);
        Assert.NotNull(preflight.CheckAction);

        // Before Start: single "Fuel pumps: ON" item keyed on the merged synthetic
        // FO_FUEL_PUMPS_BS_OK (wing ON AND center matches the fuel state) — never a
        // wing+center split, and never a standalone center-pump item.
        var beforeStart = groups.First(g => g.Id == "BEFORE_START").Items.Single(i => i.Id == "BS_FUEL");
        Assert.Equal("Fuel pumps: ON", beforeStart.Label);
        Assert.Equal("FO_FUEL_PUMPS_BS_OK", beforeStart.StateFieldName);
        Assert.Empty(beforeStart.AdditionalStateFields);
        var beforeStartIds = groups.First(g => g.Id == "BEFORE_START").Items.Select(i => i.Id);
        Assert.DoesNotContain("FO_CTR_PUMPS_ON", beforeStartIds);
        Assert.DoesNotContain("BS_CTR_PUMPS_ON", beforeStartIds);

        // Shutdown: single "Fuel pumps: OFF" item, wing-then-center ordering enforced by
        // the underlying executor's serialized dispatch (DispatchCoreAsync gate), not by
        // this data — pin only that both SetWingFuelPumps/SetCenterFuelPumps calls exist
        // by asserting a single merged item with the full six-field detection set.
        var shutdown = groups.First(g => g.Id == "SHUTDOWN").Items.Single(i => i.Id == "SD_FUEL");
        Assert.Equal("Fuel pumps: OFF", shutdown.Label);
        Assert.Equal(5, shutdown.AdditionalStateFields.Count);
        Assert.NotNull(shutdown.CheckAction);

        // No standalone "center pumps" items anywhere in the checklist set — the merged
        // item is the ONLY fuel-pump entry per phase.
        var allIds = groups.SelectMany(g => g.Items).Select(i => i.Id).ToArray();
        Assert.DoesNotContain(allIds, id => id.Contains("CTR_PUMP"));
    }
}
