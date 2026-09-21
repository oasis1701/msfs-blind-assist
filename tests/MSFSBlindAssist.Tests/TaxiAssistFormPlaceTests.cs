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

    // ---- What the Place list says about itself (TaxiAssistForm.DescribePlaceList) ----------

    [Fact]
    public void An_empty_list_says_whether_the_airport_has_no_places_or_none_could_be_loaded()
    {
        // A build that FAILED, or was discarded twice, lists nothing because nothing came back —
        // which is not the same fact as an airport that genuinely has nothing to route to, and
        // the pilot acts on the two differently (press again vs. pick another destination type).
        Assert.Equal("No places to route to at KTIW.", TaxiAssistForm.DescribePlaceList(true, 0, "KTIW"));
        Assert.Equal("Places could not be loaded for KTIW.", TaxiAssistForm.DescribePlaceList(false, 0, "KTIW"));
    }

    [Fact]
    public void One_place_is_a_place_not_places()
    {
        Assert.Equal("1 place listed.", TaxiAssistForm.DescribePlaceList(true, 1, "KTIW"));
        Assert.Equal("2 places listed.", TaxiAssistForm.DescribePlaceList(true, 2, "KTIW"));
    }

    [Fact]
    public void With_no_airport_loaded_nothing_is_said_rather_than_a_sentence_naming_none()
    {
        // Selecting Place while pre-planning in the air (no graph, _currentIcao "") used to say
        // "No places to route to at ." — and during or after a failed load it named the PREVIOUS
        // airport. The caller gates on the graph; this is the second half of the same rule.
        Assert.Null(TaxiAssistForm.DescribePlaceList(true, 0, ""));
        Assert.Null(TaxiAssistForm.DescribePlaceList(false, 0, "   "));
    }

    // ---- Which pending selection survives (TaxiAssistForm.MergePendingPlaceSelection) ------

    private static TaxiAssistForm.PendingPlaceSelection Pending(string? label, bool fromGsx)
        => new(label, fromGsx);

    [Fact]
    public void A_pending_that_names_a_destination_is_never_replaced_by_one_that_names_none()
    {
        // The second Calculate press the first one's "Please select a destination." invites can
        // reach RefreshDestinationsIfGateSourceChanged again while the list is still empty, so
        // `previous` is null — and the pilot's own "X" was overwritten by it and never re-seated.
        var held = Pending("Narrows Aviation, FBO, Parking 12", fromGsx: true);
        Assert.Equal(held, TaxiAssistForm.MergePendingPlaceSelection(held, Pending(null, fromGsx: true)));
        Assert.Equal(held, TaxiAssistForm.MergePendingPlaceSelection(held, Pending("", fromGsx: false)));
    }

    [Fact]
    public void The_origin_flag_follows_the_label_that_survives()
    {
        // A SILENT restore's label outliving a gate-source refresh's empty request keeps the
        // restore's origin: the loss may be announced only when the pilot's chosen destination
        // was really lost to a GSX-triggered rebuild, and a restore performs no pilot action.
        var restored = Pending("A 12 - Gate Medium", fromGsx: false);
        var merged = TaxiAssistForm.MergePendingPlaceSelection(restored, Pending(null, fromGsx: true));
        Assert.False(merged.FromGateSourceRefresh);
        Assert.Equal("A 12 - Gate Medium", merged.Label);
    }

    [Fact]
    public void A_newer_label_is_the_pilots_newer_choice_and_wins()
    {
        var older = Pending("A 12 - Gate Medium", fromGsx: false);
        var newer = Pending("B 7 - Gate Small", fromGsx: true);
        Assert.Equal(newer, TaxiAssistForm.MergePendingPlaceSelection(older, newer));
        Assert.Equal(newer, TaxiAssistForm.MergePendingPlaceSelection(null, newer));
        // Nothing held: an empty request still arms (it means "keep the selection cleared").
        Assert.Equal(Pending(null, true), TaxiAssistForm.MergePendingPlaceSelection(null, Pending(null, true)));
        Assert.Equal(Pending("X", false), TaxiAssistForm.MergePendingPlaceSelection(Pending(null, true), Pending("X", false)));
    }

    // ---- What a BACKGROUND refresh says (TaxiAssistForm.DescribeBackgroundPlaceRefresh) ----

    [Fact]
    public void A_background_place_refresh_speaks_only_when_the_list_really_changed()
    {
        // The late OSM answer usually adds nothing routable, and a count spoken into a dialog
        // the pilot is not waiting on is noise. Only a list that actually moved is worth a line.
        Assert.Null(TaxiAssistForm.DescribeBackgroundPlaceRefresh(visible: true, countBefore: 6, countAfter: 6, catalogPresent: true, "KTIW"));
        Assert.Equal("8 places listed.",
            TaxiAssistForm.DescribeBackgroundPlaceRefresh(visible: true, countBefore: 6, countAfter: 8, catalogPresent: true, "KTIW"));
    }

    [Fact]
    public void A_background_place_refresh_says_nothing_into_a_dialog_the_pilot_has_closed()
        => Assert.Null(TaxiAssistForm.DescribeBackgroundPlaceRefresh(visible: false, countBefore: 0, countAfter: 8, catalogPresent: true, "KTIW"));
}
