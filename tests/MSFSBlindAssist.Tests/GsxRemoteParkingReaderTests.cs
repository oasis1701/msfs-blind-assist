using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Services.Gsx.Remote;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxRemoteParkingReader"/> against a REAL, redacted KJFK
/// <c>handlerData.airport.parkings</c> capture (238 stands; verbatim values, trimmed to the
/// fields this reader touches — Fixtures/gsx-handlerdata-parkings-kjfk.json), plus targeted
/// synthetic shape tests (clearly separated below) for absence/malformed-input handling the
/// real capture never exercises.
/// See docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"Data reference" and §"ParkingSpot is both the list model and the docking input".
/// </summary>
public class GsxRemoteParkingReaderTests
{
    private const string Kjfk = "KJFK";

    private static JsonElement KjfkFixture()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-handlerdata-parkings-kjfk.json"));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // ── Real-capture-backed tests (KJFK, 238 stands) ────────────────────────

    [Fact]
    public void Reads_every_selectable_KJFK_stand()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        // 238 total - 6 Vehicle - 1 Fuel = 231. The one real Gate Heavy stand GSX published
        // with no heading ("Gate 1A" @ Terminal 8 - Concourse B) is KEPT (Heading=NaN, not
        // dropped) -- see Gate_missing_heading_is_kept_with_NaN_not_dropped below.
        Assert.Equal(231, spots.Count);
    }

    [Fact]
    public void Excludes_vehicle_and_fuel_spots()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        string[] excludedRawNames =
            { "Parking 301", "Parking 302", "Parking 106", "Parking 107", "Parking 108", "Ramp 0", "Parking 500" };
        foreach (var name in excludedRawNames)
            Assert.DoesNotContain(spots, s => s.GsxIdentifier == name);
    }

    [Fact]
    public void Radius_is_metres_half_maxWingspan_for_a_gsx_spot()
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.Equal(GateSource.Gsx, spot.Source);
        Assert.Equal(50.0, spot.MaxWingspanMeters);
        Assert.Equal(25.0, spot.Radius);   // METRES, never feet -- half of maxWingspan verbatim
    }

    [Fact]
    public void Gate_missing_heading_is_kept_with_NaN_not_dropped()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);

        // GSX's own capture omits the "heading" key entirely for this one otherwise-normal,
        // otherwise-selectable Gate Heavy stand -- but it is a real, selectable stand, and
        // dropping it would leave a blind pilot unable to find it with no explanation. It is
        // KEPT, with Heading=NaN (never a fabricated 0, which would point due north and could
        // silently steer docking there) so a later stage (the .ini join) has a chance to
        // recover the real value, and anything still unusable is unmistakably NaN rather than
        // a plausible-but-wrong bearing.
        var noHeading = spots.Single(s => s.GsxIdentifier == "Gate 1A" && s.TerminalName == "Terminal 8 - Concourse B");
        Assert.True(double.IsNaN(noHeading.Heading));
        Assert.False(GsxRemoteParkingReader.HasUsableHeading(noHeading));

        // The OTHER "Gate 1A" (a different physical stand, Terminal 1) DOES carry a real
        // heading in the capture and must be entirely unaffected by the rule above.
        var kept = spots.Single(s => s.GsxIdentifier == "Gate 1A" && s.TerminalName == "Terminal 1");
        Assert.Equal(44.2120719909668, kept.Heading, 6);
        Assert.True(GsxRemoteParkingReader.HasUsableHeading(kept));
    }

    [Fact]
    public void HasUsableHeading_is_true_for_a_normal_stand()
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.True(GsxRemoteParkingReader.HasUsableHeading(spot));
    }

    [Fact]
    public void HasUsableHeading_is_false_for_a_null_spot()
    {
        Assert.False(GsxRemoteParkingReader.HasUsableHeading(null));
    }

    [Theory]
    [InlineData("Gate 25", "Terminal 4 - Concourse B", 10)]   // wire type 9,  constant GATE_MEDIUM      -> navdata Gate Medium
    [InlineData("Gate 27", "Terminal 4 - Concourse B", 13)]   // wire type 10, constant GATE_HEAVY       -> navdata Gate Heavy
    [InlineData("Stand H6", "Terminal 5 - Remote", 4)]        // wire type 3,  constant RAMP_GA_MEDIUM   -> navdata Ramp GA Medium
    public void Type_resolves_via_the_published_enum_constants(string gateName, string terminal, int expectedNavdataType)
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == gateName && s.TerminalName == terminal);
        Assert.Equal(expectedNavdataType, spot.Type);
    }

    [Fact]
    public void GsxIdentifier_is_the_raw_uiGateName_verbatim()
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Gate 20A");
        Assert.Equal("Gate 20A", spot.GsxIdentifier);
        Assert.Equal(20, spot.Number);
        Assert.Equal("A", spot.Suffix);
    }

    [Fact]
    public void The_terminal_rides_in_TerminalName_never_in_Name()
    {
        // uiGateName ALONE collides across terminals at KJFK -- "Gate 2" alone names 5
        // physically different stands across 5 terminals in this capture. uiTerminalName
        // never repeats a shared uiGateName (verified: 0 collisions across all 238
        // (uiTerminalName, uiGateName) pairs), which is what actually keeps a pilot's
        // dropdown distinguishable -- so it is kept, in its OWN field.
        //
        // It must NEVER be ParkingSpot.Name. Name is the CONCOURSE LETTER app-wide, and
        // three subsystems parse it as one: GateAliasResolver (StandId.Parse), SayIntentions'
        // assigned-gate resolution (NormalizeParkingName) and MainForm's parked-at-the-right-
        // stand check. Terminal prose matches no stand-id shape, so all three failed silently
        // -- and SayIntentions' failure ends at the ARRIVAL RUNWAY.
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Gate 20A");
        Assert.Equal("Terminal 4 - Concourse B", spot.TerminalName);
        // "Gate 20A" carries no concourse letter, so THIS READER leaves Name empty — it derives
        // identity from uiGateName alone and never invents one. At KJFK that is 222 of 231
        // stands, and leaving it there is what routed SayIntentions arrivals to the runway:
        // GsxConcourseLetterFiller (run by GateDataSource straight after this reader) is what
        // completes it, from navdata or from the terminal wording. See its own test suite.
        Assert.Equal(string.Empty, spot.Name);
        Assert.Equal(20, spot.Number);
        Assert.Equal("A", spot.Suffix);
    }

    [Fact]
    public void A_stand_label_matches_what_SayIntentions_would_ask_for()
    {
        // The end-to-end shape of the C2 fix, on real captured data: the label the destination
        // combo carries must still normalize to the bare stand id, because that is what
        // MatchDestinationLabel compares a controller's/SayIntentions' gate against.
        // "Terminal 4 - Concourse B 20A - Gate Heavy" normalized to "TERMINAL4" -- the stand
        // number gone entirely -- and matched nothing.
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Stand H6");

        Assert.Equal("H6", SayIntentionsClearanceParser.NormalizeParkingName(spot.ToString()));

        // ...and the terminal is still IN the label, after the first spaced dash, so the
        // dropdown can still tell colliding stands apart.
        Assert.Contains("Terminal 5 - Remote", spot.ToString());
    }

    [Fact]
    public void Every_selectable_KJFK_stand_gets_a_distinct_label()
    {
        // The dropdown de-duplicates by label text (TaxiAssistForm:
        // `if (_destinationNodeMap.ContainsKey(label)) continue;`), so two stands sharing a
        // label means one of them is UNREACHABLE for a blind pilot -- there is no other way
        // into the list. 48 distinct uiGateName values collide at least once here, which is
        // exactly why the terminal has to survive somewhere in the label.
        var labels = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Select(s => s.ToString()).ToList();
        Assert.Equal(231, labels.Count);
        Assert.Equal(labels.Count, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_headings_sign_is_normalized_into_0_to_360()
    {
        // GSX publishes SIGNED headings -- 122 of the 231 selectable stands in this capture
        // are negative. The value is SPOKEN ("Align with {stand}, heading -90"), and a pilot
        // cannot find -90 on a heading indicator. The .ini path has always normalized; the
        // same data must not read differently for having arrived over the Remote API.
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);

        Assert.All(spots, s => Assert.True(double.IsNaN(s.Heading) || (s.Heading >= 0.0 && s.Heading < 360.0),
            $"{s.GsxIdentifier} has heading {s.Heading}"));

        // A specific stand whose raw wire value is negative, checked against the fold rather
        // than merely "in range" -- so a bug that clamped instead of wrapping would fail.
        // Wire value: -89.8518591308594.
        var wrapped = spots.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.Equal(360.0 - 89.8518591308594, wrapped.Heading, 6);

        // ...and a positive one is left exactly as published (wire value: 61.0564002990723).
        var untouched = spots.Single(s => s.GsxIdentifier == "Gate 20A");
        Assert.Equal(61.0564002990723, untouched.Heading, 6);
    }

    [Fact]
    public void Same_number_different_letter_suffix_stay_distinct()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        var n = spots.Single(s => s.GsxIdentifier == "Stand 232N");
        var s = spots.Single(s => s.GsxIdentifier == "Stand 232S");
        Assert.Equal(232, n.Number);
        Assert.Equal("N", n.Suffix);
        Assert.Equal(232, s.Number);
        Assert.Equal("S", s.Suffix);
    }

    [Fact]
    public void Letter_prefixed_stand_number_parses_the_letter_as_the_concourse()
    {
        // "Stand H6" glues the letter BEFORE the digits (9/238 KJFK remote GA hardstands).
        // Sharing StandId.Parse with GateAliasResolver puts the letter in Name, where every
        // stand-identity consumer expects it -- and incidentally retires the old display-only
        // quirk that rendered this stand "6H".
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Stand H6");
        Assert.Equal("H", spot.Name);
        Assert.Equal(6, spot.Number);
        Assert.Equal(string.Empty, spot.Suffix);
        // GsxIdentifier (what actually gets SENT to gate.select) is untouched by the parse.
        Assert.Equal("Stand H6", spot.GsxIdentifier);
    }

    [Theory]
    // The shape that matters most and that the retired pair of regexes could not read at all:
    // a concourse letter AND a trailing MARS suffix on the same stand. Neither old regex
    // matched it, so BOTH the number and the letter were silently lost (0, "").
    [InlineData("Gate A12A", "A", 12, "A")]
    [InlineData("Gate B25", "B", 25, "")]
    [InlineData("Gate 20A", "", 20, "A")]
    [InlineData("Stand H6", "H", 6, "")]
    // The category word is dropped wherever it appears, by the same shared parse.
    [InlineData("Ramp 51", "", 51, "")]
    public void Stand_identity_is_split_by_the_shared_StandId_parse(
        string uiGateName, string expectedName, int expectedNumber, string expectedSuffix)
    {
        string json = $$"""
            {"parkings":[{"uiGateName":"{{uiGateName}}","uiTerminalName":"T1","uiType":"Gate Small",
                          "type":8,"GATE_SMALL":8,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal(expectedName, spot.Name);
        Assert.Equal(expectedNumber, spot.Number);
        Assert.Equal(expectedSuffix, spot.Suffix);
    }

    [Fact]
    public void A_stand_name_with_no_number_keeps_its_whole_label_as_the_Name()
    {
        // Nothing to split, so the label survives whole rather than becoming blank. Not
        // observed at KJFK (every stand there carries a number); this pins the degrade.
        const string json = """
            {"parkings":[{"uiGateName":"Helipad","uiTerminalName":"T1","uiType":"Gate Small",
                          "type":8,"GATE_SMALL":8,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal("Helipad", spot.Name);
        Assert.Equal(0, spot.Number);
        Assert.Equal("Helipad", spot.GsxIdentifier);
    }

    [Fact]
    public void Airline_codes_are_comma_joined()
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.Equal("DAL, AMX", spot.AirlineCodes);
    }

    [Fact]
    public void Airline_codes_holding_one_empty_string_become_empty_not_a_blank_entry()
    {
        // Real KJFK capture: "Stand 232N"/"Stand 232S" publish airlineCodes: [""].
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Stand 232N");
        Assert.Equal(string.Empty, spot.AirlineCodes);
    }

    [Fact]
    public void HasJetway_true_and_false_both_read_from_the_integer_wire_value()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        var withJetway = spots.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        var withoutJetway = spots.Single(s => s.GsxIdentifier == "Stand H6");
        Assert.True(withJetway.HasJetway);   // wire hasJetway: 1
        Assert.False(withoutJetway.HasJetway); // wire hasJetway: 0
    }

    [Fact]
    public void VdgsType_is_the_raw_parkingSystem_string_not_friendly_shortened()
    {
        // FriendlyVdgs() (ParkingSpot.Describe()) does the "SafeDockTS42LSupport" -> "SafeDock"
        // shortening at DISPLAY time -- the reader must store the raw value untouched.
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B");
        Assert.Equal("SafeDockTS42LSupport", spot.VdgsType);
    }

    [Fact]
    public void Gate_distance_threshold_carries_both_real_observed_values()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        Assert.Equal(25.0, spots.Single(s => s.GsxIdentifier == "Gate 25" && s.TerminalName == "Terminal 4 - Concourse B").GateDistanceThreshold);
        Assert.Equal(15.0, spots.Single(s => s.GsxIdentifier == "Stand H6").GateDistanceThreshold);
    }

    [Fact]
    public void Stop_position_fields_are_always_null_here()
    {
        // The API never publishes a stop position -- GsxStopPositionJoiner (a later task)
        // fills these from the GSX .ini. This reader must never guess at one.
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        Assert.All(spots, s =>
        {
            Assert.Null(s.StopLatitude);
            Assert.Null(s.StopLongitude);
            Assert.Null(s.StopHeading);
        });
    }

    [Fact]
    public void No_spot_from_this_reader_is_ever_marked_a_deice_area()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        Assert.All(spots, s => Assert.False(s.IsDeiceArea));
    }

    [Fact]
    public void Airport_icao_is_stamped_from_the_parameter_not_derived_from_the_json()
    {
        Assert.All(GsxRemoteParkingReader.Read(KjfkFixture(), "KJFK"), s => Assert.Equal("KJFK", s.AirportICAO));
        // Same fixture (whose own JSON "icao" is "KJFK"), a different caller-supplied value --
        // proves Read() doesn't quietly read/trust handlerDataAirport's own icao field for this.
        Assert.All(GsxRemoteParkingReader.Read(KjfkFixture(), "TEST"), s => Assert.Equal("TEST", s.AirportICAO));
    }

    // ── Synthetic shape tests -- NOT from the real capture ──────────────────
    // These exercise absence/malformed-input handling the real 238-stand KJFK capture
    // never triggers (every field it needs is well-formed there). Each uses hand-written
    // minimal JSON, clearly not presented as captured data.

    [Fact]
    public void Default_JsonElement_returns_empty_not_throw()
    {
        Assert.Empty(GsxRemoteParkingReader.Read(default, Kjfk));
    }

    [Fact]
    public void Object_with_no_parkings_key_returns_empty()
    {
        Assert.Empty(GsxRemoteParkingReader.Read(Parse("{}"), Kjfk));
    }

    [Fact]
    public void Non_array_parkings_returns_empty()
    {
        Assert.Empty(GsxRemoteParkingReader.Read(Parse("""{"parkings":"nope"}"""), Kjfk));
    }

    [Fact]
    public void Array_with_non_object_entries_never_throws_and_skips_them()
    {
        var spots = GsxRemoteParkingReader.Read(Parse("""{"parkings":[123,"str",null,[1,2],{}]}"""), Kjfk);
        Assert.Empty(spots); // the bare {} has no uiGateName either, so it is skipped too
    }

    [Fact]
    public void Entry_missing_uiGateName_is_skipped()
    {
        const string json = """
            {"parkings":[{"uiTerminalName":"T1","uiType":"Gate Small","type":8,"GATE_SMALL":8,
                          "lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        Assert.Empty(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
    }

    [Fact]
    public void Entry_missing_lat_or_lon_is_dropped()
    {
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Small","type":8,
                          "GATE_SMALL":8,"lon":2.0,"heading":3.0}]}
            """;
        Assert.Empty(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
    }

    [Fact]
    public void Entry_missing_heading_only_is_kept_with_NaN_not_dropped()
    {
        // Same shape as the real "Gate 1A" case, isolated from its terminal-name-collision
        // complexity: lat/lon present, heading absent.
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Small","type":8,
                          "GATE_SMALL":8,"lat":1.0,"lon":2.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.True(double.IsNaN(spot.Heading));
        Assert.False(GsxRemoteParkingReader.HasUsableHeading(spot));
        // Position is still stored -- only heading is affected.
        Assert.Equal(1.0, spot.Latitude);
        Assert.Equal(2.0, spot.Longitude);
    }

    [Fact]
    public void Entry_with_no_matching_type_constant_degrades_to_unknown_type_zero()
    {
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Small","type":999,
                          "lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal(0, spot.Type);
    }

    [Fact]
    public void A_GATE_EXTRA_stand_reads_as_navdata_Gate_Extra_and_renders_as_a_gate()
    {
        // GSX publishes GATE_EXTRA (15) and RAMP_GA_EXTRA (14) on EVERY parking (238/238 in the
        // KJFK capture) — they are the two largest size classes, the ones an A380 stand profile
        // uses. Navdata has a type for both (ParkingSpot: 14 = "Gate Extra", 15 = "Ramp GA
        // Extra"), so a stand of either kind must resolve to it, not fall through to 0 —
        // which rendered as "Spot 1 - Unknown", dropped it into "Other" in the teleport
        // dialog's category filter and out of its gate count. Note the wire<->navdata SWAP:
        // GSX 15=GATE_EXTRA -> navdata 14; GSX 14=RAMP_GA_EXTRA -> navdata 15.
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Extra","type":15,
                          "GATE_HEAVY":10,"RAMP_GA_EXTRA":14,"GATE_EXTRA":15,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal(14, spot.Type);
        Assert.Equal("Gate Extra", spot.GetParkingType());
        Assert.Equal("Gate Extra", spot.GetFilterCategory());
        // No concourse letter on this stand, so Describe() leans on IsGateType() to say "Gate",
        // never the generic "Spot".
        Assert.StartsWith("Gate 1 - Gate Extra", spot.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_RAMP_GA_EXTRA_stand_reads_as_navdata_Ramp_GA_Extra()
    {
        const string json = """
            {"parkings":[{"uiGateName":"Stand 7","uiTerminalName":"Remote","uiType":"Ramp GA Extra","type":14,
                          "RAMP_GA_LARGE":4,"RAMP_GA_EXTRA":14,"GATE_EXTRA":15,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal(15, spot.Type);
        Assert.Equal("Ramp GA Extra", spot.GetParkingType());
        Assert.Equal("Ramp GA", spot.GetFilterCategory());
    }

    [Fact]
    public void Type_mapping_survives_a_hypothetical_gsx_renumbering()
    {
        // GATE_MEDIUM is redefined here to 42 instead of GSX's real current value (9), and
        // this entry's own `type` is set to match -- i.e. GSX has renumbered its enum in some
        // future build. The reader must still resolve navdata "Gate Medium" because it reads
        // the published NAME, never a hardcoded raw wire int.
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Medium","type":42,
                          "GATE_MEDIUM":42,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Equal(GsxGateMapper.MapGsxTypeToNavdataType(9), spot.Type); // "Gate Medium"'s current navdata number
        Assert.Equal(10, spot.Type);
    }

    [Fact]
    public void HasJetway_also_accepts_a_real_json_boolean()
    {
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Small","type":8,
                          "GATE_SMALL":8,"lat":1.0,"lon":2.0,"heading":3.0,"hasJetway":true}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.True(spot.HasJetway);
    }

    [Fact]
    public void Missing_maxWingspan_falls_back_to_a_permissive_metre_radius()
    {
        // Never observed missing in the real KJFK capture (238/238 present) -- this mirrors
        // GsxGateMapper.ToParkingSpot's existing .ini-path fallback (same 100 m value, same
        // "don't spuriously filter the stand out" reasoning) for the case where it ever is.
        const string json = """
            {"parkings":[{"uiGateName":"Gate 1","uiTerminalName":"T1","uiType":"Gate Small","type":8,
                          "GATE_SMALL":8,"lat":1.0,"lon":2.0,"heading":3.0}]}
            """;
        var spot = Assert.Single(GsxRemoteParkingReader.Read(Parse(json), Kjfk));
        Assert.Null(spot.MaxWingspanMeters);
        Assert.Equal(100.0, spot.Radius);
    }

    [Theory]
    [InlineData("42")]
    [InlineData("\"just a string\"")]
    [InlineData("[1,2,3]")]
    [InlineData("true")]
    [InlineData("null")]
    public void Non_object_top_level_input_never_throws(string rawJson)
    {
        Assert.Empty(GsxRemoteParkingReader.Read(Parse(rawJson), Kjfk));
    }
}
