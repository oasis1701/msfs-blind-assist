using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 777's read-back (*_CL) checklists are PMDG's own checklist pages, line for line
/// (FOB-4): <c>B777_Checklist.xml</c> in the aircraft package (<c>SimObjects/Airplanes/PMDG
/// 777-300ER/presets/pmdg/PMDG 777-300ER/checklist/</c>, revision 13NOV23). <see cref="Pmdg"/>
/// transcribes each page's subjects (the part before PMDG's ".."); two vendor typos are read as
/// meant ("Parkign Brake", "Approach Breifing"). PMDG's expectation column has blanks ("Passenger
/// Signs ___") and typos, so our labels keep PMDG's subject and say the expectation in our own
/// words, and <see cref="Ours"/> pins them exactly.
///
/// The documented differences from PMDG's file (FOB-4), and where each is pinned:
/// - Seat belts ON, not the vendor's AUTO: "Passenger Signs" (BSCL_SIGNS) reads the selector at
///   ON, below and in Pmdg777FlowOrderingTests.SeatBeltItems_require_ON_and_reject_AUTO.
/// - The oxygen tests run before the fire test: a Preflight PROCEDURE difference (PMDG's Preflight
///   Checklist starts with Oxygen, as ours does), pinned by
///   Pmdg777FlowOrderingTests.PreflightChecklist_runs_the_oxygen_tests_before_the_fire_test.
/// - The vendor's LNAV/VNAV arm and "clearance to pressurize the hydraulic systems" lines are
///   omitted: Before Start PROCEDURE lines, on no checklist page, pinned by
///   Pmdg777FlowOrderingTests.Preflight_and_BeforeStart_no_longer_ask_for_*.
/// - "Start ACARS" (BSCL_ACARS) is the last line of the Before Start Checklist though it is not on
///   PMDG's page (owner, 2026-10-14): the one owner-added read-back line, named in
///   <see cref="OwnerAdded"/>, left out of the line-for-line check and pinned in
///   <see cref="Ours"/> and BeforeStartAcarsReadbackTests.
/// </summary>
public class Pmdg777ReadbackTests
{
    // PMDG's checklist pages in the file's order: the group that renders each page, the page's
    // name, and its lines' subjects as printed (typos read as meant, see above).
    private static readonly (string Group, string Page, string[] Subjects)[] Pmdg =
    {
        ("PREFLIGHT_CL", "Preflight Checklist",
            new[] { "Oxygen", "Flight Instruments", "Parking Brake", "Fuel Control Switches" }),
        ("BEFORE_START_CL", "Before Start Checklist",
            new[] { "Flight Deck Door", "Passenger Signs", "MCP", "Takeoff Speeds", "CDU Preflight", "Trim",
                    "Taxi and Takeoff Briefing", "Beacon" }),
        ("BEFORE_TAXI_CL", "Before Taxi Checklist",
            new[] { "Anti Ice", "Recall", "Auto Brake", "Flight Controls", "Ground Equipment" }),
        ("BEFORE_TKOF_CL", "Before Takeoff Checklist", new[] { "Flaps" }),
        ("AFTER_TKOF_CL", "After Takeoff Checklist", new[] { "Landing Gear", "Flaps" }),
        ("DESCENT_CL", "Descent Checklist",
            new[] { "Recall", "Notes", "Autobrake", "Landing Data", "Approach Briefing" }),
        ("APPROACH_CL", "Approach Checklist", new[] { "Altimeters" }),
        ("LANDING_CL", "Landing Checklist", new[] { "Speedbrake", "Landing Gear", "Flaps" }),
        ("SHUTDOWN_CL", "Shutdown Checklist",
            new[] { "hydraulic panel", "Fuel Pumps", "Flaps", "Parking Brake", "Fuel Control Switches", "weather Radar" }),
        ("SECURE_CL", "Secure Checklist", new[] { "ADIRU", "Emergency Lights", "PACKS" }),
    };

    // Our lines, exactly: PMDG's subject, our wording of the expectation.
    private static readonly (string Group, string[] Labels)[] Ours =
    {
        ("PREFLIGHT_CL", new[] { "Oxygen: Tested 100%", "Flight Instruments: Set heading and altimeter",
            "Parking Brake: SET", "Fuel Control Switches: CUTOFF" }),
        ("BEFORE_START_CL", new[] { "Flight Deck Door: Closed and locked", "Passenger Signs: Set",
            "MCP: Set speed, heading and altitude", "Takeoff Speeds: Set V1, VR and V2 from CDU",
            "CDU Preflight: Complete", "Trim: Elevator set, aileron 0, rudder 0",
            "Taxi and Takeoff Briefing: Complete", "Beacon: ON",
            "Start ACARS" /* not on PMDG's page: the owner's last line, 2026-10-14 (ACARS is the Captain's) */ }),
        ("BEFORE_TAXI_CL", new[] { "Anti-Ice: As required", "Recall: Checked", "Auto Brake: RTO",
            "Flight Controls: Checked", "Ground Equipment: Clear" }),
        ("BEFORE_TKOF_CL", new[] { "Flaps: Set for takeoff" }),
        ("AFTER_TKOF_CL", new[] { "Landing Gear: UP", "Flaps: UP" }),
        ("DESCENT_CL", new[] { "Recall: Checked", "Notes: Checked", "Autobrake: As required",
            "Landing Data: Set VREF and minimums", "Approach Briefing: Complete" }),
        ("APPROACH_CL", new[] { "Altimeters: SET" }),
        ("LANDING_CL", new[] { "Speedbrake: ARMED", "Landing Gear: DOWN", "Flaps: Set for landing" }),
        ("SHUTDOWN_CL", new[] { "Hydraulic panel: SET", "Fuel Pumps: OFF", "Flaps: UP",
            "Parking Brake: As required", "Fuel Control Switches: CUTOFF", "Weather Radar: OFF" }),
        ("SECURE_CL", new[] { "ADIRU: OFF", "Emergency Lights: OFF", "Packs: OFF" }),
    };

    // Read-back lines the OWNER added that PMDG's pages do not have (owner, 2026-10-14): the only
    // ones, excluded from the line-for-line comparison and pinned by wording in Ours. Never add a
    // line here without an owner decision; every other line must stay PMDG's (FOB-4).
    private static readonly (string Group, string ItemId)[] OwnerAdded =
    {
        ("BEFORE_START_CL", "BSCL_ACARS"),
    };

    private static List<ChecklistGroup<AircraftActionExecutor, AircraftStateEvaluator>> Readbacks() =>
        PMDG777ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)).ToList();

    private static ChecklistItem<AircraftActionExecutor, AircraftStateEvaluator> Line(string groupId, string itemId) =>
        PMDG777ChecklistDefinitions.Build().Single(g => g.Id == groupId).Items.Single(i => i.Id == itemId);

    // Case and a hyphen are spelling, not wording: our "Anti-Ice" is PMDG's "Anti Ice".
    private static string Subject(string text) => text.Replace('-', ' ').ToUpperInvariant();

    [Fact]
    public void Readback_groups_are_PMDGs_checklist_pages_in_order()
    {
        Assert.Equal(Pmdg.Select(p => p.Group), Readbacks().Select(g => g.Id));
        Assert.Equal(Pmdg.Select(p => p.Page), Readbacks().Select(g => g.Name));
    }

    [Fact]
    public void Every_readback_line_is_PMDGs_line_in_PMDGs_order()
    {
        var built = Readbacks().ToDictionary(g => g.Id);
        foreach (var (group, _, subjects) in Pmdg)
            Assert.Equal(subjects.Select(Subject),
                built[group].Items.Where(i => !OwnerAdded.Contains((group, i.Id)))
                                  .Select(i => Subject(i.Label.Split(": ")[0])));
    }

    [Fact]
    public void Owner_added_lines_exist_and_are_the_only_extras()
    {
        var built = Readbacks().ToDictionary(g => g.Id);
        foreach (var (group, itemId) in OwnerAdded)
            Assert.Contains(built[group].Items, i => i.Id == itemId);
        // Anything beyond PMDG's subjects must be on the allow-list.
        foreach (var (group, _, subjects) in Pmdg)
            Assert.Equal(subjects.Length + OwnerAdded.Count(o => o.Group == group), built[group].Items.Count);
    }

    [Fact]
    public void Readback_wording_is_pinned()
    {
        Assert.Equal(Pmdg.Select(p => p.Group), Ours.Select(o => o.Group));
        var built = Readbacks().ToDictionary(g => g.Id);
        foreach (var (group, labels) in Ours)
            Assert.Equal(labels, built[group].Items.Select(i => i.Label));
    }

    [Fact]
    public void Descent_notes_and_approach_briefing_are_lines_the_pilot_confirms()
    {
        Assert.Equal(new[] { "DSC_RECALL", "DSC_NOTES", "DSC_AUTOBRAKE", "DSC_LANDING_DATA", "DSC_APPROACH_BRIEF" },
            Readbacks().Single(g => g.Id == "DESCENT_CL").Items.Select(i => i.Id));
        Assert.Equal(ChecklistItemType.CaptainReminder, Line("DESCENT_CL", "DSC_NOTES").Type);
        Assert.Equal(ChecklistItemType.CaptainReminder, Line("DESCENT_CL", "DSC_APPROACH_BRIEF").Type);
    }

    [Fact]
    public void Shutdown_parking_brake_is_PMDGs_As_required_and_reads_nothing()
    {
        var line = Line("SHUTDOWN_CL", "SDCL_PARK_BRAKE");
        Assert.Equal(ChecklistItemType.Actionable, line.Type);
        Assert.False(line.IsAutoDetectable);   // released on chocks is as right as set
        Assert.Null(line.CheckAction);
    }

    [Fact]
    public void Shutdown_fuel_pumps_line_reads_all_six_pumps_as_the_Shutdown_group_does()
    {
        var readback = Line("SHUTDOWN_CL", "SDCL_FUEL_PUMPS");
        var group = Line("SHUTDOWN", "SD_FUEL_PUMPS");
        Assert.Equal(new[] { group.StateFieldName }.Concat(group.AdditionalStateFields),
                     new[] { readback.StateFieldName }.Concat(readback.AdditionalStateFields));
        Assert.True(readback.EvaluateAdditionalState(0));
        Assert.False(readback.EvaluateAdditionalState(1));
    }

    // FO-3: ticking a read-back line fires nothing. The Landing Checklist's "Speedbrake: ARMED"
    // was the one read-back line with an action on any aircraft until 2026-10-09; the arming is
    // the Landing section's LDA_SPEEDBRAKE now.
    [Fact]
    public void Readbacks_are_action_free()
    {
        foreach (var g in Readbacks())
            foreach (var i in g.Items)
                Assert.True(i.CheckAction == null, $"{g.Id}/{i.Id} is a read-back line with a CheckAction (FO-3)");
    }

    [Fact]
    public void Passenger_signs_read_ON_the_documented_difference_from_PMDGs_AUTO()
    {
        var signs = Line("BEFORE_START_CL", "BSCL_SIGNS");
        Assert.True(signs.EvaluateState(2));    // ON
        Assert.False(signs.EvaluateState(1));   // AUTO
    }
}
