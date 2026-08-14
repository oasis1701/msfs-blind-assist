using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx;
using MSFSBlindAssist.Services.Gsx.Remote;

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
        // 238 total - 6 Vehicle - 1 Fuel - 1 real Gate Heavy stand GSX published with no
        // heading ("Gate 1A" @ Terminal 8 - Concourse B, dropped -- see the test below) = 230.
        Assert.Equal(230, spots.Count);
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
            .Single(s => s.GsxIdentifier == "Gate 25" && s.Name == "Terminal 4 - Concourse B");
        Assert.Equal(GateSource.Gsx, spot.Source);
        Assert.Equal(50.0, spot.MaxWingspanMeters);
        Assert.Equal(25.0, spot.Radius);   // METRES, never feet -- half of maxWingspan verbatim
    }

    [Fact]
    public void Real_gate_with_no_published_heading_is_dropped_not_zeroed()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);

        // GSX's own capture omits the "heading" key entirely for this one otherwise-normal,
        // otherwise-selectable Gate Heavy stand. Latitude/Longitude/Heading are non-nullable
        // on ParkingSpot, so there is no "unknown" to store -- fabricating 0 would silently
        // steer a blind pilot at a wrong heading, so the spot is dropped instead.
        Assert.DoesNotContain(spots, s => s.GsxIdentifier == "Gate 1A" && s.Name == "Terminal 8 - Concourse B");

        // The OTHER "Gate 1A" (a different physical stand, Terminal 1) DOES carry a real
        // heading in the capture and must be entirely unaffected by the rule above.
        var kept = spots.Single(s => s.GsxIdentifier == "Gate 1A" && s.Name == "Terminal 1");
        Assert.Equal(44.2120719909668, kept.Heading, 6);
    }

    [Theory]
    [InlineData("Gate 25", "Terminal 4 - Concourse B", 10)]   // wire type 9,  constant GATE_MEDIUM      -> navdata Gate Medium
    [InlineData("Gate 27", "Terminal 4 - Concourse B", 13)]   // wire type 10, constant GATE_HEAVY       -> navdata Gate Heavy
    [InlineData("Stand H6", "Terminal 5 - Remote", 4)]        // wire type 3,  constant RAMP_GA_MEDIUM   -> navdata Ramp GA Medium
    public void Type_resolves_via_the_published_enum_constants(string gateName, string terminal, int expectedNavdataType)
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == gateName && s.Name == terminal);
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
    public void Name_carries_the_terminal_context_not_a_bare_category_word()
    {
        // uiGateName ALONE collides across terminals at KJFK -- "Gate 2" alone names 5
        // physically different stands across 5 terminals in this capture. uiTerminalName
        // never repeats a shared uiGateName (verified: 0 collisions across all 238
        // (uiTerminalName, uiGateName) pairs), which is what actually keeps a pilot's
        // dropdown distinguishable, so it is used for ParkingSpot.Name here.
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Gate 20A");
        Assert.Equal("Terminal 4 - Concourse B", spot.Name);
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
    public void Letter_prefixed_stand_number_still_parses_a_number_and_suffix()
    {
        // "Stand H6" glues the letter BEFORE the digits (9/238 KJFK remote GA hardstands).
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk).Single(s => s.GsxIdentifier == "Stand H6");
        Assert.Equal(6, spot.Number);
        Assert.Equal("H", spot.Suffix);
        // GsxIdentifier (what actually gets SENT to gate.select) is untouched by the parse
        // above regardless -- this is a display-only quirk, never a selection-safety issue.
        Assert.Equal("Stand H6", spot.GsxIdentifier);
    }

    [Fact]
    public void Airline_codes_are_comma_joined()
    {
        var spot = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk)
            .Single(s => s.GsxIdentifier == "Gate 25" && s.Name == "Terminal 4 - Concourse B");
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
        var withJetway = spots.Single(s => s.GsxIdentifier == "Gate 25" && s.Name == "Terminal 4 - Concourse B");
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
            .Single(s => s.GsxIdentifier == "Gate 25" && s.Name == "Terminal 4 - Concourse B");
        Assert.Equal("SafeDockTS42LSupport", spot.VdgsType);
    }

    [Fact]
    public void Gate_distance_threshold_carries_both_real_observed_values()
    {
        var spots = GsxRemoteParkingReader.Read(KjfkFixture(), Kjfk);
        Assert.Equal(25.0, spots.Single(s => s.GsxIdentifier == "Gate 25" && s.Name == "Terminal 4 - Concourse B").GateDistanceThreshold);
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
