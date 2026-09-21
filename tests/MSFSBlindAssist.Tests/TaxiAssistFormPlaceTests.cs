// Which destinations ask GSX to prepare a stand (TaxiAssistForm.ShouldSendGateSelect).
//
// The live regression this pins: a PLACE (FBO, hangar, fuel, terminal, cargo) resolves onto
// whatever navdata stand sits nearest it, and most of those are stands GSX never listed — a
// fuel point, a GA ramp, a stand GSX dropped for having no usable heading. Sending gate.select
// for one carries no GsxIdentifier, so GSX answers bad_args and the pilot heard "GSX could not
// prepare this stand." after nearly every FBO route. That phrase is correct for the Gate /
// Parking type, where the .ini/navdata fallback genuinely is a degraded gate list the pilot
// should know about; it is a false alarm for a place that was never a GSX stand to begin with.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class TaxiAssistFormPlaceTests
{
    private static ParkingSpot Spot(string gsxId) => new() { Name = "A", Number = 1, GsxIdentifier = gsxId };

    [Fact]
    public void A_place_asks_GSX_to_prepare_a_stand_only_when_GSX_published_that_stand()
    {
        Assert.True(TaxiAssistForm.ShouldSendGateSelect(4, Spot(" Gate 1")));
        Assert.False(TaxiAssistForm.ShouldSendGateSelect(4, Spot("")));      // a navdata-only stand: silence, not "GSX could not prepare this stand."
        Assert.False(TaxiAssistForm.ShouldSendGateSelect(4, null));          // an end-of-taxiway place
    }

    [Fact]
    public void The_other_destination_types_are_unchanged()
    {
        Assert.True(TaxiAssistForm.ShouldSendGateSelect(1, Spot("")));       // Gate / Parking: the documented fallback still reports
        Assert.False(TaxiAssistForm.ShouldSendGateSelect(0, Spot("x")));     // runway
        Assert.False(TaxiAssistForm.ShouldSendGateSelect(2, Spot("x")));     // progressive
        Assert.False(TaxiAssistForm.ShouldSendGateSelect(3, Spot("x")));     // de-ice
    }
}
