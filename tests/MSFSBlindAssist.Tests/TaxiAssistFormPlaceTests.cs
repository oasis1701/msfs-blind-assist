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

    // ---- Arriving in front of the list mid-refresh (TaxiAssistForm.DescribePlaceModeEntry) ----

    [Fact]
    public void Arriving_in_Place_mode_over_a_silent_refresh_promotes_it_and_says_it_is_loading()
    {
        // The pilot switches type away and back while a background refresh is in flight: the list
        // is empty, the catalog was just invalidated so nothing is cached, the warm-up guard blocks
        // a second one and the "no places" line is suppressed — silence. Then the refresh settles
        // with an unchanged count and says nothing either. They are left in front of an empty list
        // with no word spoken at all. A refresh stops being background the moment they arrive.
        var entry = TaxiAssistForm.DescribePlaceModeEntry(
            warmUpInFlight: true, warmUpIsBackground: true, silentRestore: false, visible: true);
        Assert.True(entry.PromoteWarmUp);
        Assert.True(entry.SpeakLoading);
    }

    [Fact]
    public void A_foreground_warm_up_already_said_it_and_is_left_alone()
    {
        var entry = TaxiAssistForm.DescribePlaceModeEntry(true, warmUpIsBackground: false, silentRestore: false, visible: true);
        Assert.False(entry.PromoteWarmUp);
        Assert.False(entry.SpeakLoading);
    }

    [Fact]
    public void With_no_warm_up_in_flight_there_is_nothing_to_promote()
    {
        var entry = TaxiAssistForm.DescribePlaceModeEntry(warmUpInFlight: false, warmUpIsBackground: true, silentRestore: false, visible: true);
        Assert.False(entry.PromoteWarmUp);
        Assert.False(entry.SpeakLoading);
    }

    [Fact]
    public void A_silent_restore_arriving_in_Place_mode_promotes_nothing_and_stays_silent()
    {
        // "Probing leaves no mark": the restore performs no pilot-visible action, so neither the
        // loading line nor the settle's count may be unlocked by it.
        var entry = TaxiAssistForm.DescribePlaceModeEntry(true, true, silentRestore: true, visible: true);
        Assert.False(entry.PromoteWarmUp);
        Assert.False(entry.SpeakLoading);
    }

    [Fact]
    public void A_hidden_form_promotes_the_refresh_but_speaks_nothing_here()
    {
        // The promotion is about the warm-up's OBLIGATION; the settle applies its own visibility
        // gate at the moment of settling, which is this file's established rule. The loading line
        // is spoken HERE, so it reads visibility here — exactly as WarmPlaces' own start line does.
        var entry = TaxiAssistForm.DescribePlaceModeEntry(true, true, silentRestore: false, visible: false);
        Assert.True(entry.PromoteWarmUp);
        Assert.False(entry.SpeakLoading);
    }

    [Fact]
    public void A_settle_that_no_longer_owns_the_ticket_falls_back_to_how_it_started()
    {
        // The promotion lives in a FIELD, which LoadAirportDataCoreAsync clears. In its
        // synchronous populate stretch a background warm-up's build can store its catalog, the
        // populate then finds it fresh and starts NO successor — so the old continuation reaches
        // the settle with the ticket gone (the ownership test's "field is null" disjunct) and the
        // field already false. Read blindly, a refresh nobody asked for announced a count and
        // skipped the rename fallback. The field is the truth only while it still names us.
        Assert.True(TaxiAssistForm.ResolveWarmUpBackground(stillOwnsTicket: false, fieldValue: false, startedAsBackground: true));
        Assert.False(TaxiAssistForm.ResolveWarmUpBackground(stillOwnsTicket: false, fieldValue: true, startedAsBackground: false));
        // Still ours: the field wins, which is how a promotion reaches the settle at all.
        Assert.False(TaxiAssistForm.ResolveWarmUpBackground(stillOwnsTicket: true, fieldValue: false, startedAsBackground: true));
    }

    // ---- A selection that could not be put back (Classify/DescribePlaceSelectionLoss) ----

    [Fact]
    public void A_selection_that_was_put_back_is_no_loss_at_all()
    {
        // Either the label survived the rebuild, or a background refresh found the same TARGET
        // under a new name. Both are silent re-seats.
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.None,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(hadLabel: true, reseated: true, listEmpty: false,
                fromGateSourceRefresh: true, backgroundRefresh: false));
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.None,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(true, reseated: true, listEmpty: false,
                fromGateSourceRefresh: false, backgroundRefresh: true));
    }

    [Fact]
    public void A_background_refresh_that_could_not_put_the_selection_back_says_so_in_its_own_words()
    {
        // GSX did not do this — the catalog's own merge rule renamed the place (a proper OSM name
        // absorbs a synthesized one, so "GA ramp, Parking 3" becomes "Narrows Aviation, FBO,
        // Parking 3"). Without a word, Calculate later aborts with "Please select a destination."
        // for no reason the pilot can see.
        var loss = TaxiAssistForm.ClassifyPlaceSelectionLoss(hadLabel: true, reseated: false, listEmpty: false,
            fromGateSourceRefresh: false, backgroundRefresh: true);
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.BackgroundRefresh, loss);
        Assert.Equal("Places updated. Please choose the destination again.",
            TaxiAssistForm.DescribePlaceSelectionLoss(loss, visible: true));
        Assert.Null(TaxiAssistForm.DescribePlaceSelectionLoss(loss, visible: false));
    }

    [Fact]
    public void A_gate_source_refresh_keeps_the_GSX_sentence()
    {
        var loss = TaxiAssistForm.ClassifyPlaceSelectionLoss(true, reseated: false, listEmpty: false,
            fromGateSourceRefresh: true, backgroundRefresh: false);
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.GateSourceRefresh, loss);
        Assert.Equal(TaxiAssistForm.GateListUpdatedMessage, TaxiAssistForm.DescribePlaceSelectionLoss(loss, visible: true));
    }

    [Fact]
    public void Neither_a_foreground_settle_nor_an_empty_list_nor_a_cleared_selection_is_announced()
    {
        // A FOREGROUND settle on a restore-origin pending says nothing — the pilot performed no
        // action there; "choose again" over an empty list would be an instruction to choose from
        // nothing; and a selection that was already clear lost nothing.
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.None,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(true, false, listEmpty: false, fromGateSourceRefresh: false, backgroundRefresh: false));
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.None,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(true, false, listEmpty: true, fromGateSourceRefresh: false, backgroundRefresh: true));
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.None,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(hadLabel: false, reseated: false, listEmpty: false, fromGateSourceRefresh: true, backgroundRefresh: true));
    }

    [Fact]
    public void A_restore_origin_pending_a_BACKGROUND_refresh_cannot_put_back_IS_announced()
    {
        // DELIBERATE, and the one case where a restore-origin pending speaks. A SayIntentions
        // probe fails and RestoreDestinationState arms the pilot's pre-probe place (kept, because
        // a warm-up is in flight); the background refresh then renames or drops it. What took the
        // destination away is the REFRESH, not the probe — so the sentence is true and actionable,
        // and the alternative is a silently cleared destination and a baffling "Please select a
        // destination." at the next Calculate. Silent on a foreground settle, spoken here.
        Assert.Equal(TaxiAssistForm.PlaceSelectionLoss.BackgroundRefresh,
            TaxiAssistForm.ClassifyPlaceSelectionLoss(hadLabel: true, reseated: false, listEmpty: false,
                fromGateSourceRefresh: false, backgroundRefresh: true));
    }
}
