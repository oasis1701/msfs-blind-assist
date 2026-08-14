using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxConcourseLetterFiller"/> — the Remote API path's name-only borrow of the
/// concourse letter GSX's <c>uiGateName</c> usually omits.
///
/// <para>
/// The tests that matter most here run the REAL committed KJFK capture through the REAL reader
/// and then all the way out through <see cref="ParkingSpot.ToString"/> and
/// <see cref="SayIntentionsClearanceParser.NormalizeParkingName"/>, because that whole chain is
/// the defect: a hand-built spot with <c>Name</c> already set proves nothing about a reader that
/// never sets it. Of the capture's 231 selectable stands, 9 carry the letter in
/// <c>uiGateName</c>, 91 carry it only in <c>uiTerminalName</c>, and 131 have none anywhere.
/// </para>
///
/// <para>
/// The source PRIORITY is the other measured thing pinned here, and it is deliberately the
/// opposite of the usual instinct: GSX's <c>uiTerminalName</c> beats navdata. Resolved for all
/// 222 letterless KJFK stands against the real fs2024 navdata, the two disagree on <b>46</b>
/// (32 agree, 52 navdata-only, 13 terminal-only, 79 letterless) — and GSX is right every sampled
/// time, because navdata's letter rides in the BGL parking-name enum that scenery authors fill
/// inconsistently. See
/// <c>The_GSX_terminal_beats_navdata_on_the_real_KJFK_stand_they_actually_disagree_about</c>.
/// </para>
/// </summary>
public class GsxConcourseLetterFillerTests
{
    private const string Kjfk = "KJFK";

    private static JsonElement KjfkFixture()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-handlerdata-parkings-kjfk.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static List<ParkingSpot> ReadKjfk() => GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);

    /// <summary>A navdata-shaped donor: <see cref="ParkingSpot.Name"/> is the bare concourse
    /// letter, exactly as <c>LittleNavMapProvider.MapParkingName</c> already produces it
    /// ("GA" -> "A").</summary>
    private static ParkingSpot Nav(string name, int number, double lat, double lon) =>
        new() { AirportICAO = Kjfk, Name = name, Number = number, Latitude = lat, Longitude = lon, Source = GateSource.Navdata };

    private static ParkingSpot Api(string identifier, string letter, int number, double lat, double lon,
                                   string terminal = "") =>
        new()
        {
            AirportICAO = Kjfk, GsxIdentifier = identifier, Name = letter, Number = number,
            Latitude = lat, Longitude = lon, TerminalName = terminal, Source = GateSource.Gsx,
        };

    /// <summary>Offsets a coordinate due north by <paramref name="metres"/> — lets a test place a
    /// donor a KNOWN distance away and straddle <c>MatchRadiusMetres</c> from both sides.</summary>
    private static double LatPlusMetres(double lat, double metres) => lat + metres / 111_320.0;

    // ── The end-to-end defect, on real captured data ────────────────────────────────────────

    [Fact]
    public void A_terminal_lettered_KJFK_stand_ends_up_matching_what_SayIntentions_asks_for()
    {
        // THE regression. "Gate 25" @ "Terminal 4 - Concourse B" is stand B25; SayIntentions
        // publishes assigned_gate as the full label carrying the letter ("Terminal 3 Gate J1",
        // "Terminal 2 Gate C6", "South Terminal Gate A24" are the real captures). Before this
        // filler the label normalized to "25" while SI asked for "B25", MatchDestinationLabel
        // failed, and destination resolution ran its whole chain to the ARRIVAL RUNWAY.
        //
        // Deliberately NO navdata here: the GSX terminal wording alone must recover it (all 91
        // such KJFK stands), because that is the source this fix leads with and the one that
        // needs no navigation database at all.
        var spots = GsxConcourseLetterFiller.Fill(ReadKjfk(), () => Array.Empty<ParkingSpot>());
        var spot = spots.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");

        Assert.Equal(SayIntentionsClearanceParser.NormalizeParkingName("Terminal 4 Gate B25"),
                     SayIntentionsClearanceParser.NormalizeParkingName(spot.ToString()));
        Assert.Equal("B25", SayIntentionsClearanceParser.NormalizeParkingName(spot.ToString()));

        // ...and the label still reads as a stand id first, with the terminal after the first
        // spaced dash where NormalizeParkingName discards it.
        Assert.Equal("B 25 - Gate Medium, Terminal 4 - Concourse B (Jetway) [SafeDock]", spot.ToString());
    }

    [Fact]
    public void The_GSX_terminal_beats_navdata_on_the_real_KJFK_stand_they_actually_disagree_about()
    {
        // THE measured conflict, encoded from the committed capture rather than invented. For all
        // 222 letterless KJFK stands resolved against the real fs2024 navdata: 32 agree, 46
        // DISAGREE, 52 navdata-only, 13 terminal-only, 79 stay letterless. Every sampled
        // disagreement looks exactly like this one --
        //     'Gate 25' @ 'Terminal 4 - Concourse B'   navdata=A   terminal=B
        // -- and GSX is RIGHT: KJFK Terminal 4 is Concourse A (A2-A7) and Concourse B (B20-B41),
        // so gate 25 is B25, which is what a controller and SayIntentions say. Navdata's letter
        // comes from the BGL parking-name enum (GATE_A/GATE_B/…, which MapParkingName strips to
        // A/B/…), and at KJFK the author set GATE_A across a whole concourse.
        //
        // So navdata is authoritative for stand GEOMETRY and demonstrably NOT for the concourse
        // letter. Navdata-first produced the wrong letter for 46 of 222 stands. Do not flip this
        // back on general "navdata is authoritative" grounds — this is a measured exception.
        var api = ReadKjfk();
        var target = api.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        var navdataSaysA = Nav("A", 25, LatPlusMetres(target.Latitude, 3.0), target.Longitude);

        GsxConcourseLetterFiller.Fill(api, () => new[] { navdataSaysA });

        Assert.Equal("B", target.Name);
        Assert.Equal("B25", SayIntentionsClearanceParser.NormalizeParkingName(target.ToString()));
    }

    [Fact]
    public void Navdata_supplies_the_letter_when_the_GSX_terminal_names_no_concourse()
    {
        // The other half: "Gate 1" @ "Terminal 5" (real capture, 40.6444781780333 /
        // -73.7766514464756). GSX's terminal names no concourse there, so navdata decides — this
        // is the population that keeps the navdata path load-bearing (52 of KJFK's 222).
        var api = ReadKjfk();
        var target = api.Single(s => s.GsxIdentifier == "Gate 1" && s.TerminalName == "Terminal 5");
        var donor = Nav("D", 1, LatPlusMetres(target.Latitude, 3.0), target.Longitude);

        GsxConcourseLetterFiller.Fill(api, () => new[] { donor });

        Assert.Equal("D", target.Name);
        Assert.Equal("D1", SayIntentionsClearanceParser.NormalizeParkingName(target.ToString()));
    }

    [Fact]
    public void Every_KJFK_stand_still_gets_a_distinct_label_after_the_fill()
    {
        // The dropdown de-duplicates by label text, so a collision does not make a confusing
        // entry — it makes an UNREACHABLE stand, and the dropdown is a blind pilot's only way
        // in. Adding letters must not merge two stands into one label.
        var spots = GsxConcourseLetterFiller.Fill(ReadKjfk(), () => Array.Empty<ParkingSpot>());
        var labels = spots.Select(s => s.ToString()).ToList();

        Assert.Equal(231, labels.Count);
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_real_capture_splits_into_the_three_populations_it_is_supposed_to()
    {
        // 9 lettered by GSX itself, 91 recoverable from the terminal wording, 131 genuinely
        // letterless. That last group is a CORRECT answer, not a shortfall — Name = "" must
        // stay a supported shape (a live ENGM read is entirely of this kind).
        var spots = GsxConcourseLetterFiller.Fill(ReadKjfk(), () => Array.Empty<ParkingSpot>());

        Assert.Equal(9, spots.Count(s => s.GsxIdentifier!.StartsWith("Stand H", StringComparison.Ordinal)));
        Assert.Equal(100, spots.Count(s => !string.IsNullOrEmpty(s.Name)));
        Assert.Equal(131, spots.Count(s => string.IsNullOrEmpty(s.Name)));
    }

    [Fact]
    public void A_stand_GSX_already_lettered_is_never_overwritten_by_navdata()
    {
        // "Stand H6" @ "Terminal 5 - Remote": GSX's own name carries the letter. Even a navdata
        // donor sitting on top of it saying "K" must not touch it — this fills what is EMPTY,
        // it does not offer a second opinion. (Same rule GsxNavdataMerger's borrow follows.)
        var api = ReadKjfk();
        var target = api.Single(s => s.GsxIdentifier == "Stand H6");
        var donor = Nav("K", 6, target.Latitude, target.Longitude);

        GsxConcourseLetterFiller.Fill(api, () => new[] { donor });

        Assert.Equal("H", target.Name);
    }

    // ── Source priority ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_terminal_wording_wins_over_navdata_when_the_two_disagree()
    {
        // The isolated form of the KJFK conflict above. Navdata remains the authority for stand
        // GEOMETRY — nothing here reads its coordinates — but its concourse letter comes from an
        // author-filled BGL enum and loses.
        var spot = Api("Gate 25", letter: "", number: 25, lat: 40.0, lon: -73.0, terminal: "Terminal 4 - Concourse B");

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("A", 25, 40.0, -73.0) });

        Assert.Equal("B", spot.Name);
    }

    [Fact]
    public void Navdata_still_decides_when_navdata_is_all_there_is()
    {
        var spot = Api("Gate 25", "", 25, 40.0, -73.0, terminal: "Terminal 5");

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("A", 25, 40.0, -73.0) });

        Assert.Equal("A", spot.Name);
    }

    [Fact]
    public void The_terminal_wording_fills_in_when_navdata_matched_nothing()
    {
        var spot = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("A", 25, 41.0, -73.0) }); // ~111 km away

        Assert.Equal("B", spot.Name);
    }

    [Theory]
    [InlineData("Terminal 4 - Concourse B", "B")]
    [InlineData("Concourse C", "C")]
    [InlineData("concourse d", "D")]                 // GSX's own casing is not guaranteed
    [InlineData("Terminal 8 - Concourse B - North", "B")]
    [InlineData("Terminal 5", "")]                   // a terminal number is not a concourse letter
    [InlineData("Terminal 4 - Remote", "")]
    [InlineData("North Cargo Ramp", "")]
    [InlineData("Concourse 4", "")]                  // a digit is never a letter
    [InlineData("Concourse BC", "")]                 // the letter must stand alone
    [InlineData("Pier B", "")]                       // only "Concourse" is trusted — see the doc comment
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_terminal_wording_is_read_narrowly(string? terminalName, string expected)
    {
        Assert.Equal(expected, GsxConcourseLetterFiller.ConcourseLetterFromTerminal(terminalName));
    }

    // ── The position match ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_donor_just_inside_the_radius_is_accepted_and_one_just_outside_is_not()
    {
        var inside = Api("Gate 25", "", 25, 40.0, -73.0);
        var outside = Api("Gate 26", "", 26, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { inside, outside }, () => new[]
        {
            Nav("B", 25, LatPlusMetres(40.0, 9.0), -73.0),
            Nav("B", 26, LatPlusMetres(40.0, 11.0), -73.0),
        });

        Assert.Equal("B", inside.Name);
        Assert.Equal(string.Empty, outside.Name);
    }

    [Fact]
    public void A_donor_at_the_same_place_but_a_different_number_is_refused()
    {
        // Position and number are two INDEPENDENT axes of evidence, and both are required: two
        // datasets agreeing on where a stand is AND what it is numbered is what makes it the
        // same stand. It is also how GsxNavdataMerger's own borrow has always been constrained.
        var spot = Api("Gate 25", "", 25, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("B", 27, 40.0, -73.0) });

        Assert.Equal(string.Empty, spot.Name);
    }

    [Fact]
    public void Two_navdata_stands_disagreeing_about_the_concourse_are_refused_not_arbitrated()
    {
        // The same guard GateAliasResolver applies for the same reason: with two concourses in
        // range the stand's real one is unknown, and adopting either would let the pilot "find"
        // the stand by the wrong concourse. Refusing costs only a letter; guessing costs a pier.
        var spot = Api("Gate 25", "", 25, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[]
        {
            Nav("A", 25, LatPlusMetres(40.0, 2.0), -73.0),
            Nav("B", 25, LatPlusMetres(40.0, 3.0), -73.0),
        });

        Assert.Equal(string.Empty, spot.Name);
    }

    [Fact]
    public void Two_navdata_stands_agreeing_about_the_concourse_still_donate()
    {
        // A duplicated navdata row, or a MARS pair ("232N"/"232S"), is ambiguous about which
        // STAND it is but not about the CONCOURSE — which is the only thing being borrowed.
        var spot = Api("Stand 232", "", 232, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[]
        {
            Nav("B", 232, LatPlusMetres(40.0, 2.0), -73.0),
            Nav("B", 232, LatPlusMetres(40.0, 3.0), -73.0),
        });

        Assert.Equal("B", spot.Name);
    }

    [Fact]
    public void An_ambiguous_navdata_match_never_disturbs_the_terminal_answer()
    {
        // The terminal already decides this stand, so a navdata ambiguity underneath it must be
        // absorbed silently rather than blanking the letter.
        var spot = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[]
        {
            Nav("A", 25, 40.0, -73.0),
            Nav("C", 25, 40.0, -73.0),
        });

        Assert.Equal("B", spot.Name);
    }

    // ── Which navdata rows may donate at all ────────────────────────────────────────────────

    [Theory]
    [InlineData("A")]
    [InlineData("b")]      // navdata casing is not guaranteed; the borrowed letter is uppercased
    [InlineData(" C ")]
    public void A_single_letter_navdata_name_donates(string navName)
    {
        var spot = Api("Gate 25", "", 25, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav(navName, 25, 40.0, -73.0) });

        Assert.Equal(navName.Trim().ToUpperInvariant(), spot.Name);
    }

    [Theory]
    // LittleNavMapProvider.MapParkingName turns the MSFS GATE_A…GATE_Z enum into a bare letter
    // ("GA" -> "A") but renders every NON-concourse parking name as a WORD. A stand CATEGORY is
    // not a concourse and must never enter the identity slot — and this filter is also what
    // structurally stops terminal prose being borrowed back into Name, the defect being fixed.
    [InlineData("North")]
    [InlineData("Parking")]
    [InlineData("Dock")]
    [InlineData("Southwest")]
    [InlineData("Terminal 4 - Concourse B")]
    [InlineData("GA")]     // a two-letter raw name that MapParkingName would already have reduced
    [InlineData("")]
    public void A_navdata_name_that_is_not_a_single_letter_never_donates(string navName)
    {
        var spot = Api("Gate 25", "", 25, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav(navName, 25, 40.0, -73.0) });

        Assert.Equal(string.Empty, spot.Name);
    }

    [Fact]
    public void A_navdata_row_at_null_island_never_donates()
    {
        // (0,0) is a real coordinate to a distance test. A navdata row with no position would
        // otherwise sit 0 m from any API stand that also lacked one.
        var spot = Api("Gate 25", "", 25, 0.0, 0.0);

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("B", 25, 0.0, 0.0) });

        Assert.Equal(string.Empty, spot.Name);
    }

    [Fact]
    public void A_stand_with_no_number_is_never_given_a_letter()
    {
        // A letter with no number is not a stand identity, nothing downstream can match on it,
        // and a numberless stand would "agree" with every other numberless navdata row in range.
        // (Same opening guard GateAliasResolver uses.) "Helipad" keeps its whole label as Name
        // anyway, so this pins the number rule on a spot that really has an empty Name.
        var spot = Api("Ramp 0", "", 0, 40.0, -73.0, "Terminal 4 - Concourse B");

        GsxConcourseLetterFiller.Fill(new[] { spot }, () => new[] { Nav("B", 0, 40.0, -73.0) });

        Assert.Equal(string.Empty, spot.Name);
    }

    // ── Degradation: every miss behaves exactly like "no filler at all" ─────────────────────

    [Fact]
    public void A_null_spot_list_returns_empty_and_never_asks_for_navdata()
    {
        bool asked = false;
        Assert.Empty(GsxConcourseLetterFiller.Fill(null, () => { asked = true; return Array.Empty<ParkingSpot>(); }));
        Assert.False(asked);
    }

    [Fact]
    public void Navdata_is_not_consulted_when_no_stand_needs_a_letter()
    {
        // The perf contract: this runs on the UI thread while the gate dropdown is being built,
        // so a database read that cannot change the answer must not be made at all.
        int calls = 0;
        var lettered = Api("Stand H6", "H", 6, 40.0, -73.0);
        var numberless = Api("Helipad", "Helipad", 0, 40.0, -73.0);

        GsxConcourseLetterFiller.Fill(new[] { lettered, numberless },
                                      () => { calls++; return Array.Empty<ParkingSpot>(); });

        Assert.Equal(0, calls);
    }

    [Fact]
    public void Navdata_is_read_at_most_once_however_many_stands_need_a_letter()
    {
        // Never a per-stand query — the whole KJFK list is 231 stands.
        int calls = 0;
        GsxConcourseLetterFiller.Fill(ReadKjfk(), () => { calls++; return Array.Empty<ParkingSpot>(); });

        Assert.Equal(1, calls);
    }

    [Fact]
    public void A_throwing_navdata_provider_degrades_to_the_terminal_wording_and_never_throws()
    {
        // A database that is missing, locked or mid-rebuild must never cost the pilot the gate
        // list — the same rule the .ini stop join follows.
        var lettered = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");
        var plain = Api("Gate 51", "", 51, 40.0, -73.0, "Terminal 5");

        var result = GsxConcourseLetterFiller.Fill(
            new[] { lettered, plain },
            () => throw new InvalidOperationException("database locked"));

        Assert.Equal(2, result.Count);
        Assert.Equal("B", lettered.Name);
        Assert.Equal(string.Empty, plain.Name);
    }

    [Fact]
    public void A_null_navdata_delegate_or_a_null_result_degrades_the_same_way()
    {
        var a = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");
        var b = Api("Gate 26", "", 26, 40.0, -73.0, "Terminal 4 - Concourse B");

        Assert.Single(GsxConcourseLetterFiller.Fill(new[] { a }, null));
        Assert.Single(GsxConcourseLetterFiller.Fill(new[] { b }, () => null));

        Assert.Equal("B", a.Name);   // the terminal fallback still runs
        Assert.Equal("B", b.Name);
    }

    [Fact]
    public void Null_entries_in_the_spot_list_are_skipped_rather_than_throwing()
    {
        var real = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");

        var result = GsxConcourseLetterFiller.Fill(new List<ParkingSpot> { null!, real, null! },
                                                   () => Array.Empty<ParkingSpot>());

        Assert.Same(real, Assert.Single(result));
        Assert.Equal("B", real.Name);
    }

    [Fact]
    public void The_same_instances_are_returned_not_copies()
    {
        // GsxStopPositionJoiner has the same contract; GateDataSource chains the two.
        var spot = Api("Gate 25", "", 25, 40.0, -73.0, "Terminal 4 - Concourse B");

        var result = GsxConcourseLetterFiller.Fill(new[] { spot }, () => Array.Empty<ParkingSpot>());

        Assert.Same(spot, Assert.Single(result));
    }
}
