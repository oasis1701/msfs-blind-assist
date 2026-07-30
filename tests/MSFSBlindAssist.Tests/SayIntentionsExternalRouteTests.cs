// Tests for the SayIntentions -> Taxi Guidance import: the pure logic behind
// MainForm.BuildTaxiRouteFromSayIntentionsAsync (MainForm.SayIntentions.cs) and the
// two matchers TaxiAssistForm uses to seat what it hands over (Forms/TaxiAssistForm.cs).
//
// These pin four fixes, each of which failed SILENTLY before — the failure mode a
// blind pilot cannot see:
//   1. A hold-short runway was matched against the combo by raw text, so the
//      clearance's zero-padded "05L" missed a navdata "5L" and the hold-short ATC
//      gave was dropped without a word. FindRunwayItemIndex normalizes both sides.
//   2. The hold-short was pinned to the LAST taxiway of the clearance instead of the
//      one it follows, and only the first hold-short survived at all.
//      ParseClearanceTaxiPlan/MapHoldShortsToTaxiways tie each one to its own taxiway.
//   3. BuildExternalRouteAnnouncement never mentioned hold-shorts, applied or lost.
//   4. Destination probing mutated the form once per candidate. The matching is now
//      a pure function (MatchDestinationLabel) over a list the form snapshots once.
//
// The form itself is not constructed here (full WinForms control tree, database and
// taxi-graph dependencies); seating behavior that needs live combos is verified in
// the sim per the PR's test plan.

using MSFSBlindAssist;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsExternalRouteTests
{
    private static readonly string[] KnownTaxiways = { "A", "B", "C", "D", "K", "N" };

    private static List<string> Taxiways(string clearance) =>
        MainForm.ParseClearanceTaxiPlan(clearance, KnownTaxiways).Taxiways;

    private static List<MainForm.ClearanceHoldShort> HoldShorts(string clearance) =>
        MainForm.ParseClearanceTaxiPlan(clearance, KnownTaxiways).HoldShorts;

    private static List<string> UnknownTaxiways(string clearance) =>
        MainForm.ParseClearanceTaxiPlan(clearance, KnownTaxiways).UnknownTaxiways;

    // --- ParseClearanceTaxiPlan: taxiway sequence ------------------------------------

    [Fact]
    public void Taxiways_come_from_the_whole_clearance_across_a_hold_short()
    {
        Assert.Equal(
            new[] { "A", "B", "C", "D" },
            Taxiways("Taxi to gate A9 via Alpha, Bravo, hold short of runway 22, Charlie, Delta"));
    }

    [Fact]
    public void Taxiways_are_not_read_before_the_via_keyword()
    {
        // "gate D9" must not become taxiway D: only the route after "via" is a
        // taxiway list, and a continuation piece is only given a "via" once one
        // has actually been seen.
        Assert.Empty(Taxiways("Taxi to gate D9, hold short of runway 22"));
    }

    [Fact]
    public void Clearance_without_taxiways_or_via_yields_nothing()
    {
        Assert.Empty(Taxiways(""));
        Assert.Empty(Taxiways("Taxi to gate A9"));
    }

    [Fact]
    public void Repeat_across_a_plain_crossing_still_collapses()
    {
        // No hold-short separates the two Charlies, so the form has no row that
        // could carry the repeat — same result a single ParseTaxiways call gives.
        Assert.Equal(
            new[] { "C" },
            Taxiways("Taxi to gate A9 via Charlie, cross runway 04L, Charlie"));
    }

    [Fact]
    public void Repeat_across_a_hold_short_is_kept()
    {
        // KBOS pattern: each November needs its own row so each hold-short gets a
        // combo of its own.
        Assert.Equal(
            new[] { "K", "B", "N", "N" },
            Taxiways("Taxi to runway 22R via Kilo, Bravo, November, hold short of runway 15R, November"));
    }

    // --- ParseClearanceTaxiPlan: taxiways the airport does not have -------------------

    [Fact]
    public void Missing_taxiways_are_collected_from_every_piece_of_the_clearance()
    {
        // The clearance is scanned in pieces cut at each hold-short, so a taxiway the
        // airport lacks has to survive whichever piece it fell in.
        Assert.Equal(
            new[] { "Z", "Q" },
            UnknownTaxiways("Taxi to runway 22 via Alpha, Zulu, hold short of runway 15, Quebec, Bravo"));
    }

    [Fact]
    public void A_clearance_the_airport_can_fully_honour_reports_nothing_missing()
    {
        Assert.Empty(UnknownTaxiways(
            "Taxi to gate A9 via Alpha, Bravo, hold short of runway 22, Charlie, Delta"));
    }

    [Fact]
    public void A_gate_named_before_via_is_not_a_missing_taxiway()
    {
        Assert.Empty(UnknownTaxiways("Taxi to gate D9, hold short of runway 22"));
    }

    // --- ParseClearanceTaxiPlan: hold-short association ------------------------------

    [Fact]
    public void Hold_short_lands_on_the_taxiway_it_follows_not_the_last_one()
    {
        var holds = HoldShorts("Taxi to gate A9 via Alpha, Bravo, hold short of runway 22, Charlie, Delta");

        var hold = Assert.Single(holds);
        Assert.Equal("B", hold.AfterTaxiway);
        Assert.Equal("22", hold.Runway);
    }

    [Fact]
    public void Every_hold_short_in_the_clearance_survives()
    {
        var holds = HoldShorts(
            "Taxi to runway 22 via Alpha, hold short of runway 15, Bravo, hold short of runway 04, Charlie");

        Assert.Equal(2, holds.Count);
        Assert.Equal(new MainForm.ClearanceHoldShort("A", "15"), holds[0]);
        Assert.Equal(new MainForm.ClearanceHoldShort("B", "04"), holds[1]);
    }

    [Fact]
    public void Two_hold_shorts_on_a_repeated_taxiway_both_survive()
    {
        var holds = HoldShorts(
            "Taxi to runway 22R via Kilo, Bravo, November, hold short of runway 15R, November, hold short of runway 22R");

        Assert.Equal(2, holds.Count);
        Assert.Equal(new MainForm.ClearanceHoldShort("N", "15R"), holds[0]);
        Assert.Equal(new MainForm.ClearanceHoldShort("N", "22R"), holds[1]);
    }

    [Fact]
    public void Spoken_hold_short_runway_is_normalized()
    {
        var hold = Assert.Single(HoldShorts("Taxi to gate A9 via Alpha, hold short of runway one five left"));
        Assert.Equal("15L", hold.Runway);
    }

    [Fact]
    public void A_crossing_is_not_a_hold_short()
    {
        Assert.Empty(HoldShorts("Taxi to runway 22 via Alpha, cross runway 15, Bravo"));
    }

    [Fact]
    public void Hold_short_with_no_taxiway_ahead_of_it_has_no_anchor()
    {
        var hold = Assert.Single(HoldShorts("Taxi to gate A9, hold short of runway 22"));
        Assert.Equal("", hold.AfterTaxiway);
        Assert.Equal("22", hold.Runway);
    }

    // --- MapHoldShortsToTaxiways -----------------------------------------------------

    [Fact]
    public void Hold_short_maps_to_the_position_of_its_taxiway()
    {
        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[] { new MainForm.ClearanceHoldShort("B", "22") },
            new[] { "A", "B", "C", "D" });

        Assert.Equal(new TaxiAssistForm.ExternalHoldShort(1, "22"), Assert.Single(mapped));
    }

    [Fact]
    public void Repeated_taxiways_are_consumed_in_order()
    {
        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[]
            {
                new MainForm.ClearanceHoldShort("N", "15R"),
                new MainForm.ClearanceHoldShort("N", "22R")
            },
            new[] { "K", "B", "N", "N" });

        Assert.Equal(2, mapped[0].TaxiwayIndex);
        Assert.Equal(3, mapped[1].TaxiwayIndex);
    }

    [Fact]
    public void A_hold_short_anchor_matches_its_taxiway_however_the_graph_spaces_it()
    {
        // On the GEOMETRY path this lookup is the one place a clearance-derived anchor
        // meets snapper output, and the two spellings need not agree. The agreement walk
        // that let the track win compares them NORMALIZED ("N 5 E" is N5E), so a raw
        // compare here would pass the walk and then fail this lookup — silently
        // downgrading a hold-short ATC gave to "could not set" over a route that has the
        // taxiway it names.
        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[] { new MainForm.ClearanceHoldShort("N5E", "27L") },
            new[] { "A", "N 5 E", "F" });

        Assert.Equal(new TaxiAssistForm.ExternalHoldShort(1, "27L"), Assert.Single(mapped));
    }

    [Fact]
    public void A_hold_short_whose_taxiway_is_not_in_the_sequence_maps_to_no_row()
    {
        // A hold-short can name a taxiway the applied sequence does not carry — the
        // clearance named it before any taxiway, or the taxiway did not resolve at
        // this airport. Hanging it on whatever row is last would put the stop at the
        // wrong crossing, so it maps nowhere and gets reported instead.
        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[]
            {
                new MainForm.ClearanceHoldShort("Q", "22"),
                new MainForm.ClearanceHoldShort("", "15")
            },
            new[] { "A", "B" });

        Assert.Equal(-1, mapped[0].TaxiwayIndex);
        Assert.Equal(-1, mapped[1].TaxiwayIndex);
    }

    // --- FindRunwayItemIndex: the hold-short combo match ------------------------------

    [Theory]
    [InlineData("05L", 1)]   // clearance zero-pads, navdata does not
    [InlineData("5L", 1)]
    [InlineData("runway 05L", 1)]
    [InlineData("23", 2)]
    [InlineData("23R", -1)]  // a side the airport does not have
    [InlineData("31", -1)]
    [InlineData("", -1)]
    [InlineData(null, -1)]
    public void Hold_short_runway_matches_the_combo_entry_however_it_is_spelled(
        string? runway, int expected)
    {
        var items = new[] { "(none)", "5L", "23" };
        Assert.Equal(expected, TaxiAssistForm.FindRunwayItemIndex(items, runway));
    }

    [Fact]
    public void The_none_sentinel_is_never_matched()
    {
        Assert.Equal(-1, TaxiAssistForm.FindRunwayItemIndex(new[] { "(none)" }, "22"));
    }

    // --- MatchDestinationLabel --------------------------------------------------------

    [Fact]
    public void Runway_destination_matches_however_the_designator_is_padded()
    {
        var runways = new[] { "Runway 05L", "Runway 23" };

        Assert.Equal("Runway 05L", TaxiAssistForm.MatchDestinationLabel(runways, true, "5L"));
        Assert.Equal("Runway 05L", TaxiAssistForm.MatchDestinationLabel(runways, true, "05L"));
        Assert.Equal("Runway 23", TaxiAssistForm.MatchDestinationLabel(runways, true, "23"));
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(runways, true, "23R"));
    }

    [Fact]
    public void Gate_destination_matches_through_the_terminal_descriptor()
    {
        var gates = new[] { "A9 - Terminal 1", "B12" };

        Assert.Equal("A9 - Terminal 1", TaxiAssistForm.MatchDestinationLabel(gates, false, "A9"));
        Assert.Equal("A9 - Terminal 1", TaxiAssistForm.MatchDestinationLabel(gates, false, "Gate A-9"));
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(gates, false, "C3"));
    }

    // Live EDDB taxi-in, 2026-07-30. SayIntentions assigned "Gate B06"; EDDB navdata
    // stores that stand as parking GB + 6, which renders "B 6". The zero-padded label
    // matched nothing, so the assigned gate could not resolve and destination
    // resolution ran to the end of its chain and took the ARRIVAL RUNWAY: guidance
    // drove a just-landed aircraft at 24L, along exactly the M3/B/V2 the controller
    // had given for the gate. The runway half of this comparison already tolerated
    // the padding via CleanRunway ("05L" vs "5L"); the gate half did not.
    [Fact]
    public void Gate_destination_matches_across_a_zero_padded_stand_number()
    {
        // The labels are what the combo really offers — ParkingSpot.Describe()'s
        // "{Name} {Number}{Suffix} - {type}", not a tidied-up stand id.
        var gates = new[]
        {
            "B 5 - Gate Heavy (Jetway)",
            "B 6 - Gate Heavy (Jetway)",
            "B 7A - Gate Medium",
            "B 10 - Gate Heavy (Jetway)",
        };

        Assert.Equal("B 6 - Gate Heavy (Jetway)",
            TaxiAssistForm.MatchDestinationLabel(gates, false, "Gate B06"));
        Assert.Equal("B 6 - Gate Heavy (Jetway)",
            TaxiAssistForm.MatchDestinationLabel(gates, false, "B6"));
        Assert.Equal("B 7A - Gate Medium",
            TaxiAssistForm.MatchDestinationLabel(gates, false, "Gate B07A"));

        // The padding is not identity, but the digits are: B10 is its own stand and
        // must never be reachable by asking for B1.
        Assert.Equal("B 10 - Gate Heavy (Jetway)",
            TaxiAssistForm.MatchDestinationLabel(gates, false, "Gate B10"));
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(gates, false, "Gate B1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_candidate_never_matches(string? identifier)
    {
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(new[] { "Runway 05L" }, true, identifier));
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(new[] { "A9" }, false, identifier));
    }

    // --- MatchDestinationAlias --------------------------------------------------------
    //
    // Live KDTW, 2026-07-31: "Taxi to South Terminal Gate A24 via Alpha-5, Alpha, Romeo,
    // hold short of runway 4R", with assigned_gate "South Terminal Gate A24". The scenery
    // calls that stand A24A (navdata parking name='GA', number=24, suffix='A'). OSM calls
    // it A24, GateAliasResolver accepts that as an alias, and the form's own gate search
    // box finds the stand by typing it.
    //
    // The IMPORT could not see it. The combo carries ParkingSpot.ToString() —
    // "A 24A - Gate Medium, also A24 (online)" — and NormalizeParkingName deletes
    // everything from the first spaced dash onward, which every Describe() branch puts
    // ahead of the alias (" - {type}"). So the assigned gate resolved by neither name nor
    // alias, and destination resolution ran its whole chain to the ARRIVAL RUNWAY: a
    // landed aircraft routed at 04L while the taxiway half of the import (A5, A, R, hold
    // short of 4R) was perfect, which is the shape that sounds right all the way down.

    private const string KdtwA24ALabel = "A 24A - Gate Medium, also A24 (online)";

    private static TaxiAssistForm.AliasedDestination Aliased(string label, params string[] aliases)
        => new(label, aliases);

    [Fact]
    public void The_live_KDTW_stand_resolves_through_the_alias_its_label_buries()
    {
        Assert.Equal(KdtwA24ALabel, TaxiAssistForm.MatchDestinationAlias(
            new[] { Aliased(KdtwA24ALabel, "A24") }, "South Terminal Gate A24"));
    }

    [Fact]
    public void The_KDTW_label_is_unreachable_by_name_which_is_why_the_alias_step_exists()
    {
        // The defect itself. Normalized, the label is A24A and the assigned gate is A24 —
        // the online A24 sitting in the same string never reaches the comparison.
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(
            new[] { KdtwA24ALabel }, false, "South Terminal Gate A24"));
    }

    [Fact]
    public void A_stand_carrying_no_alias_stays_unreachable()
    {
        // The alias is what resolves this, not the step: the same scenery without the
        // online name is still a stand nothing can seat by name, and the caller falls
        // through to the published coordinate.
        Assert.Null(TaxiAssistForm.MatchDestinationAlias(
            new[] { Aliased("A 24A - Gate Medium") }, "South Terminal Gate A24"));
    }

    [Fact]
    public void An_alias_is_matched_whole_never_as_a_substring_or_a_shorter_number()
    {
        // A stand id is one or two characters, so a Contains test would match almost
        // anything the combo offers — including "(None - calculate shortest path)". And
        // A2 is its own stand at most airports: seating A24 for it is the wrong-stand
        // failure the zero-padding rules already exist to prevent, pointed sideways.
        var offered = new[] { Aliased(KdtwA24ALabel, "A24") };

        Assert.Null(TaxiAssistForm.MatchDestinationAlias(offered, "Gate A2"));
        Assert.Null(TaxiAssistForm.MatchDestinationAlias(offered, "Gate A"));
        Assert.Null(TaxiAssistForm.MatchDestinationAlias(offered, "Gate A240"));
        Assert.Null(TaxiAssistForm.MatchDestinationAlias(offered, "Gate A24A"));
    }

    [Fact]
    public void An_alias_tolerates_the_zero_padding_the_name_match_tolerates()
    {
        // Both sides go through NormalizeParkingName, so the padding SayIntentions
        // published at EDDB ("Gate B06") is a spelling here too, not an identity.
        Assert.Equal(KdtwA24ALabel, TaxiAssistForm.MatchDestinationAlias(
            new[] { Aliased(KdtwA24ALabel, "A24") }, "South Terminal Gate A024"));
    }

    [Fact]
    public void An_alias_seats_the_spot_that_carries_it_and_not_a_neighbour()
    {
        var offered = new[]
        {
            Aliased("A 23A - Gate Medium, also A23 (online)", "A23"),
            Aliased(KdtwA24ALabel, "A24"),
            Aliased("A 25A - Gate Medium, also A25 (online)", "A25"),
        };

        Assert.Equal(KdtwA24ALabel,
            TaxiAssistForm.MatchDestinationAlias(offered, "South Terminal Gate A24"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_candidate_matches_no_alias_either(string? identifier)
    {
        Assert.Null(TaxiAssistForm.MatchDestinationAlias(
            new[] { Aliased(KdtwA24ALabel, "A24") }, identifier));
    }

    // --- BuildExternalRouteAnnouncement ----------------------------------------------

    private static TaxiAssistForm.ExternalRouteOutcome Outcome(
        bool destinationApplied = true,
        string[]? applied = null,
        string[]? skipped = null,
        TaxiAssistForm.AppliedHoldShort[]? appliedHoldShorts = null,
        string[]? skippedHoldShorts = null)
        => new(destinationApplied,
               applied ?? Array.Empty<string>(),
               skipped ?? Array.Empty<string>(),
               appliedHoldShorts ?? Array.Empty<TaxiAssistForm.AppliedHoldShort>(),
               skippedHoldShorts ?? Array.Empty<string>());

    private static string Announce(
        TaxiAssistForm.ExternalRouteOutcome outcome, string destination, bool autoStart,
        string[]? unknownTaxiways = null,
        MainForm.TaxiwaySource source = MainForm.TaxiwaySource.Clearance,
        SnapResult? snap = null,
        bool disagreed = false,
        bool clearanceNamedTaxiways = true,
        string? clearanceLookupProblem = null,
        TaxiAssistForm.GateSubstitution? gateSubstitution = null)
        => MainForm.BuildExternalRouteAnnouncement(
            outcome, unknownTaxiways ?? Array.Empty<string>(), destination,
            gateSubstitution, autoStart,
            source, disagreed, snap, clearanceNamedTaxiways, clearanceLookupProblem);

    private static TaxiAssistForm.GateSubstitution ByAlias(string assignedName)
        => new(assignedName, TaxiAssistForm.GateSubstitutionKind.Alias);

    private static TaxiAssistForm.GateSubstitution ByPosition(string assignedName)
        => new(assignedName, TaxiAssistForm.GateSubstitutionKind.Position);

    [Fact]
    public void Announcement_names_destination_taxiways_and_the_review_step()
    {
        string spoken = Announce(Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false);

        Assert.Equal(
            "SayIntentions route to Gate A9. Via A, B. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);
    }

    [Fact]
    public void A_stand_seated_by_position_is_named_right_after_the_lead()
    {
        // The lead names only the stand that WON, so without this the substitution is
        // invisible: the pilot is taxied somewhere the controller never named and hears
        // nothing to say so. It sits second because the destination is what the rest of
        // the summary is about — a correction to it cannot queue behind the route.
        string spoken = Announce(
            Outcome(applied: new[] { "M3", "B" }), "B 6", autoStart: false,
            gateSubstitution: ByPosition("Gate B06"));

        Assert.Equal(
            "SayIntentions route to B 6. " +
            "SayIntentions assigned Gate B06, which this airport does not have. " +
            "This is the nearest stand to the assigned position. " +
            "Via M3, B. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);
    }

    [Fact]
    public void A_stand_seated_by_alias_says_the_scenery_spells_it_differently()
    {
        // The KDTW arrival. The pilot was told A24 and is being taxied to a row the form
        // calls A 24A, so the substitution is exactly as invisible as the position one and
        // is announced in the same slot — but it must NOT claim the airport does not have
        // the stand. It does have it; this scenery just writes the name another way, and
        // saying otherwise about a stand the controller assigned reads as a controller
        // error rather than a labelling one.
        string spoken = Announce(
            Outcome(applied: new[] { "A5", "A", "R" }), "A 24A", autoStart: false,
            gateSubstitution: ByAlias("South Terminal Gate A24"));

        Assert.Equal(
            "SayIntentions route to A 24A. " +
            "SayIntentions assigned South Terminal Gate A24, " +
            "which this scenery lists under another name. " +
            "Via A5, A, R. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);

        Assert.DoesNotContain("does not have", spoken);
        Assert.DoesNotContain("nearest stand", spoken);
    }

    [Fact]
    public void The_substitution_leads_the_other_warnings_too()
    {
        // Warnings lead the route body, and this one leads the warnings: everything else
        // describes the route being flown, this says the route is to a different place
        // than the one that was assigned.
        string spoken = Announce(
            Outcome(applied: new[] { "M3" }, skipped: new[] { "V2" }), "B 6", autoStart: true,
            source: MainForm.TaxiwaySource.Clearance, disagreed: true,
            gateSubstitution: ByPosition("Gate B06"));

        Assert.True(
            spoken.IndexOf("which this airport does not have", StringComparison.Ordinal)
            < spoken.IndexOf("ground track differs", StringComparison.Ordinal));
        Assert.True(
            spoken.IndexOf("which this airport does not have", StringComparison.Ordinal)
            < spoken.IndexOf("Could not apply V2.", StringComparison.Ordinal));
    }

    [Fact]
    public void A_destination_matched_by_name_says_nothing_about_a_substitution()
    {
        // The overwhelmingly common case, and it must gain no words at all: the stand SI
        // named is the stand the form seated, by either spelling.
        string spoken = Announce(
            Outcome(applied: new[] { "M3" }), "B 6", autoStart: false);

        Assert.Equal(
            "SayIntentions route to B 6. Via M3. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);
        Assert.DoesNotContain("does not have", spoken);
        Assert.DoesNotContain("nearest stand", spoken);
        Assert.DoesNotContain("another name", spoken);
    }

    [Fact]
    public void The_alias_substitution_leads_the_warnings_the_same_way()
    {
        // Same slot as the position one — ahead of everything else, because everything
        // else describes the route being flown and this says the route is to a stand
        // under a label the controller did not use.
        string spoken = Announce(
            Outcome(applied: new[] { "A5" }, skipped: new[] { "R" }), "A 24A", autoStart: true,
            disagreed: true, gateSubstitution: ByAlias("South Terminal Gate A24"));

        Assert.True(
            spoken.IndexOf("under another name", StringComparison.Ordinal)
            < spoken.IndexOf("ground track differs", StringComparison.Ordinal));
        Assert.True(
            spoken.IndexOf("under another name", StringComparison.Ordinal)
            < spoken.IndexOf("Could not apply R.", StringComparison.Ordinal));
    }

    [Fact]
    public void Announcement_says_guidance_started_when_auto_start_is_on()
    {
        string spoken = Announce(Outcome(applied: new[] { "A" }), "Runway 22", autoStart: true);

        Assert.EndsWith("Guidance started.", spoken);
        Assert.DoesNotContain("Calculate Route", spoken);
    }

    [Fact]
    public void Announcement_reports_a_shortest_path_fallback()
    {
        string spoken = Announce(Outcome(), "Gate A9", autoStart: false);

        Assert.Contains("No taxiways from the clearance matched this airport. Using shortest path.", spoken);
    }

    [Fact]
    public void Announcement_names_taxiways_that_could_not_be_applied()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: new[] { "K", "N" }), "Gate A9", autoStart: false);

        Assert.Contains("Could not apply K, N.", spoken);
    }

    [Fact]
    public void Announcement_names_a_taxiway_the_airport_does_not_have()
    {
        // The CYYZ report: "via Alpha, Kilo, Romeo" at an airport with no K announced
        // "Via A, R." and said nothing at all about Kilo.
        string spoken = Announce(
            Outcome(applied: new[] { "A", "R" }), "Runway 15L", autoStart: false,
            unknownTaxiways: new[] { "K" });

        Assert.Contains("Could not apply K.", spoken);
    }

    [Fact]
    public void Announcement_merges_unseated_and_missing_taxiways_into_one_line()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: new[] { "N" }), "Gate A9", autoStart: false,
            unknownTaxiways: new[] { "K" });

        Assert.Equal(
            "SayIntentions route to Gate A9. Could not apply N, K. Via A. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);
    }

    [Fact]
    public void Announcement_reports_a_missing_taxiway_even_when_nothing_else_matched()
    {
        string spoken = Announce(Outcome(), "Gate A9", autoStart: false, unknownTaxiways: new[] { "K" });

        Assert.Contains("No taxiways from the clearance matched this airport. Using shortest path.", spoken);
        Assert.Contains("Could not apply K.", spoken);
    }

    [Fact]
    public void Announcement_reports_every_hold_short_that_was_set()
    {
        string spoken = Announce(
            Outcome(
                applied: new[] { "K", "B", "N", "N" },
                appliedHoldShorts: new[]
                {
                    new TaxiAssistForm.AppliedHoldShort("15R", "N"),
                    new TaxiAssistForm.AppliedHoldShort("22R", "N")
                }),
            "Runway 22R", autoStart: false);

        Assert.Contains("Hold short of runway 15R after N.", spoken);
        Assert.Contains("Hold short of runway 22R after N.", spoken);
    }

    [Fact]
    public void Announcement_reports_a_hold_short_that_could_not_be_set()
    {
        // The pilot has to hear this: an unset hold-short looks exactly like a route
        // that was never told to stop.
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skippedHoldShorts: new[] { "22", "15L" }),
            "Gate A9", autoStart: false);

        Assert.Contains("Could not set hold short of runway 22, 15L.", spoken);
    }

    [Fact]
    public void Announcement_reports_a_destination_that_did_not_seat()
    {
        string spoken = Announce(
            Outcome(destinationApplied: false, applied: new[] { "A" }), "Gate A9", autoStart: false);

        Assert.Contains("Destination Gate A9 not set. Check the destination field.", spoken);
    }

    [Fact]
    public void An_unseated_destination_is_never_also_claimed_as_the_route_s_destination()
    {
        // The first two sentences used to contradict each other: "SayIntentions route to
        // Gate A9." immediately followed by "Destination not set." A blind pilot has only
        // the words, so the lead has to be true on its own.
        string spoken = Announce(
            Outcome(destinationApplied: false, applied: new[] { "A" }), "Gate A9", autoStart: false);

        Assert.DoesNotContain("route to Gate A9", spoken);
        Assert.StartsWith("SayIntentions route. Destination Gate A9 not set.", spoken);
    }

    [Fact]
    public void Announcement_stays_silent_about_hold_shorts_when_the_clearance_had_none()
    {
        string spoken = Announce(Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false);

        Assert.DoesNotContain("hold short", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Announcement_stays_silent_when_the_whole_clearance_was_applied()
    {
        string spoken = Announce(Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false);

        Assert.DoesNotContain("Could not apply", spoken);
    }

    // --- ChooseTaxiwaySource: the geometry has to AGREE with the clearance -------------
    //
    // SayIntentions publishes its own taxi-route geometry, and before a clearance that
    // geometry is SI's own plan rather than the route the controller gave — a live LSZH
    // capture a minute before Ground spoke gave a completely different route. So the
    // track has to earn the route.
    //
    // It cannot earn it on TIME. flight_details.timestamp is when SI wrote the FILE, not
    // when it computed the path: three committed capture pairs carry a byte-identical
    // taxi_path under stamps 68 s, 116 s and 252 s apart, and a file write is always
    // later than a transmission already on the frequency — so a stamp comparison passes
    // on every stale path there is. What CAN tell them apart is whether the clearance
    // runs through the track. Each of the four cases below is a real capture.

    private static (MainForm.TaxiwaySource Source, IReadOnlyList<string> Taxiways, bool Disagreed)
        Choose(string[] clearance, string[] geometry) =>
        MainForm.ChooseTaxiwaySource(clearance, geometry);

    [Fact]
    public void LiveLszhArrivalTrackReproducesTheClearanceExactly()
    {
        // "Taxi to Gate E52 via E4, E, C", captured 9 s after Zurich Ground said it.
        // Equality needs no branch of its own — it is the trivial subsequence.
        var choice = Choose(new[] { "E4", "E", "C" }, new[] { "E4", "E", "C" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.Equal(new[] { "E4", "E", "C" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void LiveEgllArrivalTrackReproducesTheClearanceExactly()
    {
        // "Taxi to Gate 325 via N5E, A, F, G", captured after the clearance.
        var choice = Choose(
            new[] { "N5E", "A", "F", "G" }, new[] { "N5E", "A", "F", "G" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.Equal(new[] { "N5E", "A", "F", "G" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void LiveLepaClearanceWhoseTextDroppedALegTakesTheTrack()
    {
        // The reason this feature exists. SayIntentions said "North" for a taxiway
        // navdata calls N, so the text parsed to LE, E, H2 — one leg short of the
        // cleared route. The track holds all four, and the parsed three run straight
        // through it, so the track is this clearance with the missing leg restored.
        var choice = Choose(new[] { "LE", "E", "H2" }, new[] { "LE", "E", "N", "H2" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.Equal(new[] { "LE", "E", "N", "H2" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void ThePreClearanceEgllTrackIsRejectedAndSaidOutLoud()
    {
        // Captured BEFORE Ground spoke: SI's own plan across the airfield, not the
        // cleared route. Every stamp test passes it. The words win, and the pilot is
        // told the two disagreed.
        var choice = Choose(
            new[] { "N5E", "A", "F", "G" },
            new[] { "NB2W", "S3", "NB3", "S4E", "N4W", "N5W", "R", "B", "F", "G" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Equal(new[] { "N5E", "A", "F", "G" }, choice.Taxiways);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void ALookalikeNameIsNotTheClearedTaxiway()
    {
        // The mechanism that catches the stale EGLL track, isolated: it carries N5W and
        // no N5E, so the walk fails on the very first cleared leg. One character apart
        // is a different taxiway — on the other side of the stand group.
        var choice = Choose(new[] { "N5E" }, new[] { "N5W", "A", "F", "G" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void OneClearedTaxiwayMissingFromTheTrackRejectsIt()
    {
        // Everything else agrees and is in order; the single absent leg decides it.
        // An "any overlap" or "mostly agrees" rule would take this track — and would
        // take the stale EGLL one, which shares F and G with the clearance.
        var choice = Choose(new[] { "A", "X", "F", "G" }, new[] { "A", "F", "G" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void TheSameLegsInADifferentOrderAreADifferentRoute()
    {
        var choice = Choose(new[] { "F", "A" }, new[] { "A", "F" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void TheTrackMayRunOnEitherSideOfWhatWasCleared()
    {
        // A real track starts where the aircraft is standing and ends on the stand
        // lead-in, so it routinely carries legs before and after the cleared ones.
        var choice = Choose(new[] { "E", "C" }, new[] { "E4", "E", "C", "Link 5" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void NamesAreComparedTheWayTheRestOfTheImportComparesThem()
    {
        // SayIntentionsClearanceParser.NormalizeTaxiwayName: spacing and punctuation
        // stripped, case-insensitive.
        var choice = Choose(new[] { "n 5 e", "a" }, new[] { "N5E", "A", "F" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void NoTrackAtAllLeavesTheClearanceInCharge()
    {
        // Covers both "SI published no path" and "the path snapped to nothing" — a
        // track that matched no taxiway is no better than no track.
        var choice = Choose(new[] { "A", "B" }, Array.Empty<string>());

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Equal(new[] { "A", "B" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void AClearanceThatParsedToNothingFallsBackToTheTrack()
    {
        // There was no clearance text, or the parse found nothing in it. Either way
        // the track is all there is, and there is nothing for it to contradict.
        var choice = Choose(Array.Empty<string>(), new[] { "E4", "E", "C" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.Equal(new[] { "E4", "E", "C" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void WithNeitherSourceThereIsNothingToDisagreeAbout()
    {
        var choice = Choose(Array.Empty<string>(), Array.Empty<string>());

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Empty(choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    // --- ChooseTaxiwaySource: a repeat the track structurally cannot carry -------------
    //
    // ParseClearanceTaxiPlan deliberately KEEPS a taxiway repeated across a hold-short —
    // the KBOS pattern — because the form carries one hold-short per row and collapsing
    // the repeat throws the second one away. The snapper cannot produce that repeat: it
    // drops unsnapped and too-short runs BEFORE collapsing consecutive duplicates, so
    // [N … N] separated only by those runs reaches this comparison as a single N.
    //
    // Walked raw, such a clearance therefore NEVER agrees with its own track, and the
    // pilot is told the two differ about two descriptions of the same pavement — which
    // also switches the geometry path off for every clearance of that shape.

    [Fact]
    public void ATaxiwayRepeatedAcrossAHoldShortStillAgreesWithTheTrack()
    {
        // KBOS: "Taxi to runway 22R via November, hold short of runway 15R, then
        // November, Kilo." The clearance holds N twice, the track along that same
        // pavement holds it once. Same route.
        var choice = Choose(new[] { "N", "N", "K" }, new[] { "N", "K" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.Equal(new[] { "N", "K" }, choice.Taxiways);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void TheHoldShortOfARepeatingClearanceStillSeatsOnTheTrack()
    {
        // The clearance keeps the hold-shorts on both paths, so a collapse that made the
        // track win must not cost the stop its row.
        var choice = Choose(new[] { "N", "N", "K" }, new[] { "N", "K" });

        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[] { new MainForm.ClearanceHoldShort("N", "15R") }, choice.Taxiways);

        Assert.Equal(new TaxiAssistForm.ExternalHoldShort(0, "15R"), Assert.Single(mapped));
    }

    [Fact]
    public void AClearanceThatWinsKeepsItsRepeatSoEachHoldShortGetsItsOwnRow()
    {
        // The collapse is for the COMPARISON only. What goes to the form — and what the
        // hold-shorts are seated against — is still the raw clearance, or the second
        // November loses its row and its hold-short lands at the wrong crossing.
        var choice = Choose(new[] { "N", "N", "K" }, new[] { "A", "B", "C" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Equal(new[] { "N", "N", "K" }, choice.Taxiways);
        Assert.True(choice.Disagreed);

        var mapped = MainForm.MapHoldShortsToTaxiways(
            new[]
            {
                new MainForm.ClearanceHoldShort("N", "15R"),
                new MainForm.ClearanceHoldShort("N", "22R")
            },
            choice.Taxiways);

        Assert.Equal(0, mapped[0].TaxiwayIndex);
        Assert.Equal(1, mapped[1].TaxiwayIndex);
    }

    [Fact]
    public void ATaxiwayRevisitedLaterIsStillALegTheTrackHasToCarryTwice()
    {
        // Only ADJACENT repeats collapse, on both sides. A clearance that leaves a
        // taxiway and returns to it later names a leg the track shows twice as well —
        // the snapper keeps a non-consecutive revisit — so this is a real disagreement,
        // not the KBOS shape. A Distinct() instead of a consecutive collapse would take
        // this track.
        var choice = Choose(new[] { "A", "B", "A" }, new[] { "A", "B" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    // --- ChooseTaxiwaySource: a short clearance cannot licence a long track ------------
    //
    // The subsequence walk's discriminating power scales with how many legs the
    // clearance has. Two or three legs run through almost any track that touches the
    // same corner of the airfield, so a stale plan passes on the strength of the pilot
    // being given a SHORT clearance — silently, since a track that agrees is not
    // announced as a disagreement.
    //
    // Below is the real LSZH pre-clearance publication, snapped: SayIntentions' own
    // 12-leg plan across the airfield, sitting in taxi_path a minute before Zurich
    // Ground said anything. Real agreements measured against it run 1.0-1.33 track legs
    // per cleared leg; the stale readings run 2.5-12.

    private static readonly string[] StaleLszhPreClearancePlan =
        { "R7", "E7", "E6", "E7", "N", "E", "Inner", "E", "B", "E5", "F", "C" };

    [Fact]
    public void TheRealLszhClearanceRejectsTheStalePreClearancePlan()
    {
        // The control: "via E4, E, C" fails the walk outright — the stale plan has no
        // E4 — so this case was always right and must stay right.
        var choice = Choose(new[] { "E4", "E", "C" }, StaleLszhPreClearancePlan);

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void ATwoLegClearanceCannotLicenceTheStaleTwelveLegPlan()
    {
        // "Taxi to Gate E52 via E, C" — an abbreviated clearance over the same stand.
        // Both legs are in the stale plan, in order, so the walk alone accepts it and
        // the pilot is routed on SI's 12-leg pre-clearance plan without a word.
        var choice = Choose(new[] { "E", "C" }, StaleLszhPreClearancePlan);

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Equal(new[] { "E", "C" }, choice.Taxiways);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void AThreeLegClearanceCannotLicenceTheStaleTwelveLegPlan()
    {
        var choice = Choose(new[] { "N", "E", "B" }, StaleLszhPreClearancePlan);

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void ASingleClearedTaxiwayCannotLicenceTheStaleTwelveLegPlan()
    {
        var choice = Choose(new[] { "E" }, StaleLszhPreClearancePlan);

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void ATrackAtTheEdgeOfTheLengthGuardIsStillTaken()
    {
        // Two cleared legs allow five. A real track legitimately carries the stand it
        // starts on, the lead-in it ends on, and a leg the text parse could not name.
        var choice = Choose(new[] { "A", "B" }, new[] { "X", "A", "Y", "B", "Z" });

        Assert.Equal(MainForm.TaxiwaySource.Geometry, choice.Source);
        Assert.False(choice.Disagreed);
    }

    [Fact]
    public void ATrackOneLegPastTheGuardIsRejectedAndSaidOutLoud()
    {
        // Rejected by LENGTH rather than by the walk, and it still counts as a
        // disagreement: the two sources really do describe different routes, and
        // silence is what let the stale plan through.
        var choice = Choose(new[] { "A", "B" }, new[] { "X", "A", "Y", "B", "Z", "W" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.Equal(new[] { "A", "B" }, choice.Taxiways);
        Assert.True(choice.Disagreed);
    }

    [Fact]
    public void ARepeatedLegBuysTheTrackNoExtraLength()
    {
        // The guard measures the COLLAPSED clearance, because a taxiway said twice is
        // one leg of evidence, not two: it constrains the walk exactly as much as one
        // does. Counted raw, this three-name clearance would allow seven track legs and
        // take a six-leg track that two real legs cannot vouch for.
        var choice = Choose(
            new[] { "N", "N", "K" }, new[] { "N", "V", "W", "X", "Y", "K" });

        Assert.Equal(MainForm.TaxiwaySource.Clearance, choice.Source);
        Assert.True(choice.Disagreed);
    }

    // --- Announcement provenance ------------------------------------------------------

    private static SnapResult Snap(
        string[] taxiways, int pointCount, int unsnapped = 0, int droppedRuns = 0) =>
        new(taxiways, pointCount, unsnapped, droppedRuns);

    [Fact]
    public void TheAnnouncementSaysWhereTheRouteCameFrom()
    {
        // The two sources fail differently — a clearance route drops a leg it could not
        // name, a ground-track route follows SI's pavement rather than the controller's
        // words — so the pilot has to be able to tell them apart.
        string fromGeometry = Announce(
            Outcome(applied: new[] { "E4", "E", "C" }), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4", "E", "C" }, 40, 4, 3));

        Assert.Contains("Route from SayIntentions ground track.", fromGeometry);

        string fromClearance = Announce(
            Outcome(applied: new[] { "E4", "E", "C" }), "Gate E52", autoStart: false);

        Assert.DoesNotContain("ground track", fromClearance);
    }

    [Fact]
    public void AGeometryRouteNeverNamesATaxiwayOnlyTheClearanceKnew()
    {
        // Every name on the geometry path came from the airport's own graph, so there is
        // no "taxiway this airport does not have". A live LEPA clearance said "North"
        // for taxiway N; announcing "Could not apply North" over a route that DOES
        // include N teaches the pilot to distrust the whole readout.
        string spoken = Announce(
            Outcome(applied: new[] { "LE", "E", "N", "H2" }), "Gate 10", autoStart: false,
            unknownTaxiways: new[] { "North" },
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "LE", "E", "N", "H2" }, 60));

        Assert.DoesNotContain("Could not apply", spoken);
        Assert.DoesNotContain("North", spoken);
    }

    [Fact]
    public void AGeometryRouteStillReportsALegTheFormCouldNotSeat()
    {
        // Different from the case above: this name DID come from the graph and is a leg
        // of the route being applied, so the route really is missing it.
        string spoken = Announce(
            Outcome(applied: new[] { "E4", "E" }, skipped: new[] { "C" }), "Gate E52",
            autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4", "E", "C" }, 40));

        Assert.Contains("Could not apply C.", spoken);
    }

    [Fact]
    public void AGeometryFallbackToShortestPathDoesNotBlameTheClearance()
    {
        string spoken = Announce(
            Outcome(), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4" }, 40));

        Assert.Contains("No taxiways from the ground track matched this airport.", spoken);
        Assert.DoesNotContain("from the clearance", spoken);
    }

    [Fact]
    public void ATrackMostlyOffTheTaxiwaysIsReported()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "E4" }), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4" }, 40, unsnapped: 20));

        Assert.Contains("20 of 40 ground track points were off the taxiways", spoken);
    }

    [Fact]
    public void TheLiveStandLeadInIsNotWorthSaying()
    {
        // The real LSZH arrival read 4 unsnapped of 40 points — the turn into stand E52,
        // which is apron rather than taxiway pavement — on a perfectly clean import that
        // reproduced the cleared route exactly. Reporting that would fire on every
        // normal arrival. Its 3 dropped runs (the unnamed connector stubs SI clips the
        // corners of) are equally routine and equally silent.
        string spoken = Announce(
            Outcome(applied: new[] { "E4", "E", "C" }), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4", "E", "C" }, 40, 4, 3));

        Assert.DoesNotContain("off the taxiways", spoken);
        Assert.DoesNotContain("dropped", spoken, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AClearanceRouteSaysNothingAboutTrackPoints()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false,
            unknownTaxiways: new[] { "K" });

        Assert.DoesNotContain("points", spoken);
    }

    // --- Announcement: the two sources disagreed --------------------------------------

    [Fact]
    public void ADisagreementBetweenTheTrackAndTheWordsIsSaidOutLoud()
    {
        // SayIntentions' own idea of the route differs from what the controller said.
        // The clearance is used, and the pilot hears which — a route that is not the
        // one ATC gave is not something to discover on the taxiway.
        string spoken = Announce(
            Outcome(applied: new[] { "N5E", "A", "F", "G" }), "Gate 325", autoStart: false,
            disagreed: true);

        Assert.Contains(
            "SayIntentions ground track differs from the clearance. Using the clearance.",
            spoken);
    }

    [Fact]
    public void AnImportTheTwoSourcesAgreeOnSaysNothingAboutADisagreement()
    {
        Assert.DoesNotContain(
            "differs",
            Announce(Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false));

        Assert.DoesNotContain(
            "differs",
            Announce(
                Outcome(applied: new[] { "E4", "E", "C" }), "Gate E52", autoStart: false,
                source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4", "E", "C" }, 40)));
    }

    // --- Announcement: the unapplied-leg list must not become a recital ---------------

    [Fact]
    public void AGeometryRouteDoesNotReciteAWallOfLegsThePilotNeverHeard()
    {
        // Graph names, not the controller's words. Ten of them spoken in a row is a
        // wall of unfamiliar sounds — so the line keeps the first few as a hint about
        // where the route breaks and says how many more there are.
        string[] tenLegs = { "S3", "NB3", "S4E", "N4W", "N5W", "R", "B", "F", "G", "L" };

        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: tenLegs), "Gate 325", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "A" }, 60));

        Assert.Contains("Could not apply S3, NB3, S4E and 7 more.", spoken);
        Assert.DoesNotContain("N4W", spoken);
    }

    [Fact]
    public void OneLegOverTheCapKeepsTheNamesAndAddsTheCount()
    {
        // The cliff this replaced: at exactly four skipped legs all four names vanished
        // into a bare count, so one extra leg cost the pilot every hint about WHERE the
        // route breaks — and told them less than the three-leg case did.
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: new[] { "F", "G", "R", "L" }),
            "Gate 325", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "A" }, 60));

        Assert.Contains("Could not apply F, G, R and 1 more.", spoken);
    }

    [Fact]
    public void AGeometryRouteStillNamesAHandfulOfLegs()
    {
        // At the cap the names stand alone: three is a hint, not a recital, and there
        // is no remainder to count.
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: new[] { "F", "G", "R" }), "Gate 325",
            autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "A" }, 60));

        Assert.Contains("Could not apply F, G, R.", spoken);
        Assert.DoesNotContain("more", spoken);
    }

    // --- Announcement: a track nothing checked it against -----------------------------
    //
    // ChooseTaxiwaySource rule 2 takes the published track whenever the clearance parsed
    // to no taxiways, with no disagreement to report. That case is NOT rare: flight.json
    // carries no clearance text, so every import depends on a live getCommsHistory
    // round-trip on a 5 s timeout — and before the pilot has even requested taxi there is
    // nothing on the frequency to read. What sits in taxi_path then is SayIntentions' OWN
    // pre-clearance plan, and the pilot was routed along twelve legs across the airfield
    // that no controller had given, with nothing spoken to say the clearance was never
    // read. The route is still built — a live track is often the only thing that survives
    // a slow SAPI — but it has to say what it is.

    [Fact]
    public void ATrackWithNoClearedTaxiwaysToCheckItAgainstSaysSo()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "R7", "E7", "E6" }), "Runway 16", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "R7", "E7", "E6" }, 60),
            clearanceNamedTaxiways: false);

        Assert.Contains("Route from SayIntentions ground track.", spoken);
        Assert.Contains(
            "No cleared taxiways to check it against, so this is SayIntentions' own plan, not ATC's.",
            spoken);
    }

    [Fact]
    public void ATrackTheClearanceAgreedWithSaysNothingAboutBeingUnchecked()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "E4", "E", "C" }), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4", "E", "C" }, 40));

        Assert.DoesNotContain("own plan", spoken);
        Assert.DoesNotContain("check it against", spoken);
    }

    [Fact]
    public void WhyTheClearanceCouldNotBeReadIsSpoken()
    {
        // GetLastTransmissionAsync's Error was discarded, so a SAPI timeout, an HTTP
        // failure and a transmission that simply was not a taxi clearance all produced
        // the same silence.
        string spoken = Announce(
            Outcome(applied: new[] { "R7", "E7" }), "Runway 16", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "R7", "E7" }, 60),
            clearanceNamedTaxiways: false,
            clearanceLookupProblem: "SayIntentions comms history timed out.");

        Assert.Contains("SayIntentions comms history timed out.", spoken);
    }

    [Fact]
    public void TheReasonIsSpokenOnTheClearancePathToo()
    {
        // No track either: the route degrades to a shortest path, and "no taxiways from
        // the clearance matched this airport" on its own claims a clearance was read.
        string spoken = Announce(
            Outcome(), "Runway 16", autoStart: false,
            clearanceNamedTaxiways: false,
            clearanceLookupProblem: "SayIntentions comms history timed out.");

        Assert.Contains("SayIntentions comms history timed out.", spoken);
        Assert.Contains("Using shortest path.", spoken);
        // The "SI's own plan" clause belongs to the ground-track path only — a shortest
        // path is this app's, not SayIntentions'.
        Assert.DoesNotContain("own plan", spoken);
    }

    [Fact]
    public void AClearanceThatWasReadNormallyGainsNoExtraClause()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "A", "B" }), "Gate A9", autoStart: false,
            clearanceLookupProblem: "SayIntentions comms history timed out.");

        Assert.Equal(
            "SayIntentions route to Gate A9. Via A, B. " +
            "Review the fields, then press Calculate Route to start guidance.",
            spoken);
    }

    [Fact]
    public void TheClearancePathStillNamesEveryTaxiwayHoweverManyThereAre()
    {
        // Unchanged, deliberately: every one of these is a name the controller said,
        // and a leg of the cleared route that the pilot is not being routed along.
        string[] tenLegs = { "S3", "NB3", "S4E", "N4W", "N5W", "R", "B", "F", "G", "L" };

        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: tenLegs), "Gate A9", autoStart: false);

        Assert.Contains($"Could not apply {string.Join(", ", tenLegs)}.", spoken);
    }

    // --- Announcement: warnings before the route body ---------------------------------
    //
    // This announcement is now folded into the SINGLE post-StartGuidance AnnounceImmediate
    // the form already makes at standstill, so it can no longer be discarded by the first
    // tactical callout. It is also long, and the first callout after the pilot starts
    // rolling still cuts whatever is left — so the same rule the router's own summary
    // learned twice (TaxiGuidanceManager.Routing.cs: "a warning at the tail of a long
    // summary never gets heard") applies here: every warning goes ahead of "Via …".

    [Fact]
    public void EveryWarningIsSpokenBeforeTheRouteItself()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "A" }, skipped: new[] { "N" },
                    skippedHoldShorts: new[] { "22" }),
            "Gate A9", autoStart: true, unknownTaxiways: new[] { "K" });

        Assert.True(spoken.IndexOf("Could not apply", StringComparison.Ordinal)
                    < spoken.IndexOf("Via A.", StringComparison.Ordinal));
        Assert.True(spoken.IndexOf("Could not set hold short", StringComparison.Ordinal)
                    < spoken.IndexOf("Via A.", StringComparison.Ordinal));
        Assert.EndsWith("Guidance started.", spoken);
    }

    [Fact]
    public void ATrackMostlyOffTheTaxiwaysIsWarnedAboutBeforeTheRoute()
    {
        string spoken = Announce(
            Outcome(applied: new[] { "E4" }), "Gate E52", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(new[] { "E4" }, 40, unsnapped: 20));

        Assert.True(spoken.IndexOf("off the taxiways", StringComparison.Ordinal)
                    < spoken.IndexOf("Via E4.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheHoldShortsThatWereSetStillFollowTheRouteTheyBelongTo()
    {
        // These are not warnings — they describe the route that WAS built, so they stay
        // with it, after "Via …".
        string spoken = Announce(
            Outcome(applied: new[] { "N" },
                    appliedHoldShorts: new[] { new TaxiAssistForm.AppliedHoldShort("15R", "N") }),
            "Runway 22R", autoStart: false);

        Assert.True(spoken.IndexOf("Via N.", StringComparison.Ordinal)
                    < spoken.IndexOf("Hold short of runway 15R after N.", StringComparison.Ordinal));
    }

    // --- Announcement: the Via list is capped on the geometry path too -----------------

    [Fact]
    public void AGeometryRouteDoesNotReciteAWallOfLegsItIsTaking_either()
    {
        // Same reasoning as the skipped-leg cap, and the same cap: these are names off
        // the airport's graph that the pilot never heard a controller say, so a twelve-leg
        // ground track recited twelve unfamiliar syllables in a row. The first few say
        // where the route starts, the count says how far it runs; the form's route-summary
        // box and sayintentions.log keep the whole sequence.
        string[] twelveLegs =
            { "R7", "E7", "E6", "E7", "N", "E", "Inner", "E", "B", "E5", "F", "C" };

        string spoken = Announce(
            Outcome(applied: twelveLegs), "Runway 16", autoStart: false,
            source: MainForm.TaxiwaySource.Geometry, snap: Snap(twelveLegs, 120));

        Assert.Contains("Via R7, E7, E6 and 9 more.", spoken);
        Assert.DoesNotContain("Inner", spoken);
    }

    [Fact]
    public void TheClearancePathStillNamesEveryLegItIsTaking()
    {
        // Every one of these is a word the controller said, so the pilot hears all of
        // them however many there are — exactly as for the skipped legs.
        string[] sixLegs = { "A", "B", "C", "D", "K", "N" };

        string spoken = Announce(Outcome(applied: sixLegs), "Gate A9", autoStart: false);

        Assert.Contains($"Via {string.Join(", ", sixLegs)}.", spoken);
        Assert.DoesNotContain("more", spoken);
    }

    // --- The route-shaping state a FAILED import must put back -------------------------
    //
    // TryResolveExternalDestination promises "probing leaves no mark", but probing a gate
    // candidate switches the destination type, and OnDestTypeChanged unticks the
    // intersection-departure and CAT III boxes on the way out. A pilot who hand-built an
    // intersection departure, pressed Ctrl+Shift+Y and heard "SayIntentions route
    // unavailable" — i.e. "nothing happened" — silently lost it, and the next Calculate
    // lined them up at the full-length threshold holding at the CAT I line.

    [Fact]
    public void A_restored_intersection_departure_goes_back_to_the_taxiway_it_named()
    {
        Assert.Equal(1, TaxiAssistForm.RestoredIntersectionIndex(
            new[] { "T3 — 2100 m remaining", "T4 — 1800 m remaining" }, "T4 — 1800 m remaining"));
    }

    [Fact]
    public void A_restored_intersection_falls_back_to_the_first_when_its_taxiway_is_gone()
    {
        // The probe can leave a different runway selected than the one the pilot's
        // intersection belonged to. A checked box over a blank list is the worse outcome:
        // Calculate silently reverts to a full-length departure while the box still reads
        // as ticked.
        Assert.Equal(0, TaxiAssistForm.RestoredIntersectionIndex(
            new[] { "T3 — 2100 m remaining" }, "T4 — 1800 m remaining"));

        Assert.Equal(0, TaxiAssistForm.RestoredIntersectionIndex(
            new[] { "T3 — 2100 m remaining" }, null));
    }

    [Fact]
    public void A_runway_with_no_intersections_restores_nothing()
    {
        Assert.Equal(-1, TaxiAssistForm.RestoredIntersectionIndex(
            Array.Empty<string>(), "T4 — 1800 m remaining"));
    }
}
