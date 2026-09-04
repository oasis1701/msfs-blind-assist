// Characterization tests for the wingspan "Show fitting only" filter on the taxi
// destination list (TaxiAssistForm.ShouldApplyFitFilter / PopulateDestinations).
//
// The live regression this pins: a SayIntentions taxi clearance to a GATE
// (Ctrl+Shift+Y) could not seat its stand whenever the scenery's own size data
// called that stand too small for the aircraft. TryResolveExternalDestination
// already neutralised the other two gate-list filters before probing — the gate
// search box and the occupied-stands filter — but not this one, which is ticked by
// DEFAULT whenever wingspan data exists. PopulateDestinations drops a non-fitting
// spot BEFORE its label reaches cmbDestination.Items, _destinationSpotMap and
// _destinationThresholdMap, so all three resolution steps went blind at once: the
// name (MatchDestinationLabel), this scenery's aliases (MatchGateByAlias) and the
// coordinate SayIntentions published beside the gate (MatchGateByPosition). On a
// known arrival that is a loud "this scenery has no stand under that label" for a
// stand the airport has and the aircraft fits.
//
// Wingspan data is scenery-authored and often wrong (ParkingSpot.FitsAircraft reads
// a navdata parking RADIUS or a GSX max wing span), so a controller naming the stand
// outranks it: the pilot was TOLD to go there.

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class TaxiDestinationFitFilterTests
{
    // An A380 (261.8 ft) at a stand the scenery sized for narrowbodies.
    private const double WidebodyWingspanFeet = 261.8;

    [Fact]
    public void Pilot_browsing_the_gate_list_keeps_the_wingspan_filter()
        => Assert.True(TaxiAssistForm.ShouldApplyFitFilter(
            fitFilterChecked: true,
            suppressedForExternalDestination: false,
            aircraftWingspanFeet: WidebodyWingspanFeet));

    [Fact]
    public void A_gate_named_by_an_external_clearance_is_never_wingspan_filtered()
        => Assert.False(TaxiAssistForm.ShouldApplyFitFilter(
            fitFilterChecked: true,
            suppressedForExternalDestination: true,
            aircraftWingspanFeet: WidebodyWingspanFeet));

    [Fact]
    public void Unticking_the_box_drops_the_filter()
        => Assert.False(TaxiAssistForm.ShouldApplyFitFilter(
            fitFilterChecked: false,
            suppressedForExternalDestination: false,
            aircraftWingspanFeet: WidebodyWingspanFeet));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void No_wingspan_data_means_nothing_to_filter_against(double wingspanFeet)
        => Assert.False(TaxiAssistForm.ShouldApplyFitFilter(
            fitFilterChecked: true,
            suppressedForExternalDestination: false,
            aircraftWingspanFeet: wingspanFeet));
}
