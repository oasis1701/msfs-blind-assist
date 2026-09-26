// tests/MSFSBlindAssist.Tests/TaxiBriefingPlannerAsyncNotesTests.cs
using MSFSBlindAssist.Navigation.Briefing;
using static MSFSBlindAssist.Tests.TaxiBriefingFixture;

namespace MSFSBlindAssist.Tests;

/// <summary>A leg made unavailable before planning even starts (no database, no airport, timed out, a failure) must
/// still carry the runway note the request was given -- the pilot needs to hear why SayIntentions' runway was used
/// even when the leg itself could not be computed.</summary>
public class TaxiBriefingPlannerAsyncNotesTests
{
    private static readonly AircraftProfile B738 = AircraftSizeClass.Resolve("B738", "Boeing 737-800", 189);

    [Fact]
    public async Task An_unavailable_leg_made_before_planning_still_carries_its_runway_note()
    {
        // Distinct notes per leg: PlanAsync must route each to its OWN leg, never cross the two -- a bug that
        // passed both request.OriginRunwayNote and request.DestinationRunwayNote to PlanLegSafelyAsync as the
        // same value would go undetected by a single shared note.
        var request = Request(B738) with { OriginRunwayNote = "out-note", DestinationRunwayNote = "in-note" };

        var b = await TaxiBriefingPlanner.PlanAsync(request, null, null, TaxiBriefingPlanner.DefaultBudget);

        Assert.Equal("no navigation database loaded", b.TaxiOut.Unavailable);
        Assert.Equal("no navigation database loaded", b.TaxiIn.Unavailable);
        Assert.Contains("out-note", b.TaxiOut.Notes);
        Assert.DoesNotContain("in-note", b.TaxiOut.Notes);
        Assert.Contains("in-note", b.TaxiIn.Notes);
        Assert.DoesNotContain("out-note", b.TaxiIn.Notes);
    }

    [Fact]
    public void TaxiBriefing_Unavailable_puts_each_runway_note_on_its_own_leg()
    {
        var b = TaxiBriefing.Unavailable(B738, "EGLL", "27R", "KJFK", "22L", "reason", "o-note", "d-note");

        Assert.Contains("o-note", b.TaxiOut.Notes);
        Assert.Contains("d-note", b.TaxiIn.Notes);
    }
}
