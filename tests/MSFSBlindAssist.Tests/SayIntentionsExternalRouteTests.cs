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

using System.Globalization;
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_candidate_never_matches(string? identifier)
    {
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(new[] { "Runway 05L" }, true, identifier));
        Assert.Null(TaxiAssistForm.MatchDestinationLabel(new[] { "A9" }, false, identifier));
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
        SnapResult? snap = null)
        => MainForm.BuildExternalRouteAnnouncement(
            outcome, unknownTaxiways ?? Array.Empty<string>(), destination, autoStart, source, snap);

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
            "SayIntentions route to Gate A9. Via A. Could not apply N, K. " +
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

        Assert.Contains("Destination not set. Check the destination field.", spoken);
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

    // --- GeometryIsFresherThanClearance: the freshness gate ---------------------------
    //
    // SayIntentions publishes its own taxi-route geometry, and BEFORE a clearance that
    // geometry is SI's own plan rather than the route the controller gave. Two live LSZH
    // captures either side of one clearance measured the difference, so this gate is a
    // correctness requirement: without it the import is confidently wrong.

    private static DateTime? Utc(string? iso) =>
        iso == null
            ? null
            : DateTime.Parse(iso, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    [Theory]
    // geometry newer than the clearance -> geometry
    [InlineData("2026-07-29T20:33:51Z", "2026-07-29T20:33:42Z", true)]
    // same instant counts: "at or after" the clearance, not strictly after
    [InlineData("2026-07-29T20:33:42Z", "2026-07-29T20:33:42Z", true)]
    // geometry OLDER -> clearance text. The live 20:32:41Z capture, a minute before
    // Ground spoke, gave R7,E7,E6,E7,N,E,Inner,E,B,E5,F,C — SI's own plan, not the
    // cleared route. Using it would be confidently wrong.
    [InlineData("2026-07-29T20:32:41Z", "2026-07-29T20:33:42Z", false)]
    [InlineData(null, "2026-07-29T20:33:42Z", false)]   // unknown -> trust what was heard
    [InlineData("2026-07-29T20:33:51Z", null, false)]
    [InlineData(null, null, false)]
    public void GeometryIsOnlyPreferredWhenItIsFresherThanTheClearance(
        string? geo, string? clearance, bool expected)
    {
        Assert.Equal(expected, MainForm.GeometryIsFresherThanClearance(Utc(geo), Utc(clearance)));
    }

    // --- ResolveClearanceStampUtc: what the geometry has to beat ----------------------

    private static SayIntentionsTransmission Transmission(string message, DateTime? stamp) =>
        new("ATC", message, "Zurich Ground", "GND", stamp, 42);

    [Fact]
    public void TheClearanceStampIsTheStampOfTheTransmissionItCameFrom()
    {
        var heard = Utc("2026-07-29T20:33:42Z");

        Assert.Equal(
            heard,
            MainForm.ResolveClearanceStampUtc(
                "Taxi to Gate E52 via E4, E, C",
                Transmission("Taxi to Gate E52 via E4, E, C", heard)));
    }

    [Fact]
    public void AClearanceFromSomewhereElseInFlightJsonHasNoStamp()
    {
        // flight.json's own clearance fields carry no time. Guessing the latest
        // transmission's stamp for one of them would hand the geometry a reference it
        // has not actually been measured against, which is exactly how the gate would
        // start passing on unverifiable evidence.
        Assert.Null(MainForm.ResolveClearanceStampUtc(
            "Taxi to Gate E52 via E4, E, C",
            Transmission("Contact Tower on 118.1", Utc("2026-07-29T20:33:42Z"))));
    }

    [Fact]
    public void NoClearanceAndNoTransmissionLeaveNoStamp()
    {
        Assert.Null(MainForm.ResolveClearanceStampUtc(null, Transmission("x", Utc("2026-07-29T20:33:42Z"))));
        Assert.Null(MainForm.ResolveClearanceStampUtc("Taxi via E4", null));
        Assert.Null(MainForm.ResolveClearanceStampUtc("Taxi via E4", Transmission("Taxi via E4", null)));
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
}
