// Characterization tests for MSFSBlindAssist.Forms.TcasForm's pure string-classification
// helpers (Forms/TcasForm.cs): callsign spacing, aircraft-type shortening, route
// formatting, and the flat "AircraftItem" line builder. All targets were already
// `private static` pure functions with no WinForms coupling — promoted to `internal static`
// (zero logic change) so they're directly testable without constructing the form.

using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TcasFormParsingTests
{
    // --- FormatCallsign ----------------------------------------------------------------

    [Theory]
    [InlineData("UAL123", "UAL 123")]
    [InlineData("DLH45A", "DLH 45A")]
    [InlineData("BAW1", "BAW 1")]
    [InlineData("SWA2846", "SWA 2846")]
    public void FormatCallsign_inserts_a_space_between_airline_prefix_and_flight_number(string raw, string expected)
        => Assert.Equal(expected, TcasForm.FormatCallsign(raw));

    [Theory]
    [InlineData("N12345")]   // US registration - already has no space to add, and doesn't match the pattern
    [InlineData("G-ABCD")]   // has a hyphen -> left unchanged
    [InlineData("UAL 123")]  // already spaced -> left unchanged
    public void FormatCallsign_leaves_registrations_and_already_spaced_strings_unchanged(string raw)
        => Assert.Equal(raw, TcasForm.FormatCallsign(raw));

    [Fact]
    public void FormatCallsign_trims_whitespace_before_matching()
        => Assert.Equal("UAL 123", TcasForm.FormatCallsign("  UAL123  "));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FormatCallsign_passes_through_null_or_whitespace_only_input(string raw)
        => Assert.Equal(raw, TcasForm.FormatCallsign(raw));

    [Fact]
    public void FormatCallsign_unrecognized_shape_is_returned_unchanged()
        // Doesn't match ^([A-Z]{2,4})(\d{1,4}[A-Z]?)$ (5 digits) -> falls through unchanged.
        => Assert.Equal("UAL12345", TcasForm.FormatCallsign("UAL12345"));

    // --- ShortenAircraftType -------------------------------------------------------------

    [Theory]
    [InlineData("B738", "B738")]
    [InlineData("A20N", "A20N")]
    [InlineData("B77W", "B77W")]
    [InlineData("MD11", "MD11")]
    [InlineData("CRJ9", "CRJ9")]
    public void ShortenAircraftType_bare_icao_code_is_returned_uppercased_unchanged(string raw, string expected)
        => Assert.Equal(expected, TcasForm.ShortenAircraftType(raw));

    [Fact]
    public void ShortenAircraftType_strips_a_trailing_wake_category_suffix()
        // "/H" heavy-wake suffix is dropped before classification.
        => Assert.Equal("B77W", TcasForm.ShortenAircraftType("B77W/H"));

    [Fact]
    public void ShortenAircraftType_extracts_an_icao_code_embedded_in_a_longer_string()
        => Assert.Equal("A320", TcasForm.ShortenAircraftType("Airbus A320 Neo Leap"));

    [Theory]
    [InlineData("737", "B737")]
    [InlineData("738", "B738")]
    [InlineData("747", "B747")]
    [InlineData("777", "B777")]
    [InlineData("787", "B787")]
    [InlineData("320", "A320")]
    [InlineData("380", "A380")]
    public void ShortenAircraftType_maps_a_bare_digit_model_number_to_its_icao_prefix(string digits, string expectedIcao)
        => Assert.Equal(expectedIcao, TcasForm.ShortenAircraftType($"Generic {digits} Model"));

    [Fact]
    public void ShortenAircraftType_strips_a_known_manufacturer_prefix_when_no_icao_code_is_found()
        => Assert.Equal("Citation X", TcasForm.ShortenAircraftType("Cessna Citation X"));

    [Fact]
    public void ShortenAircraftType_empty_input_returns_empty()
        => Assert.Equal("", TcasForm.ShortenAircraftType(""));

    // --- FormatRoute -----------------------------------------------------------------------

    [Fact]
    public void FormatRoute_with_both_airports_joins_them_with_to()
        => Assert.Equal("KJFK to EGLL", TcasForm.FormatRoute("KJFK", "EGLL"));

    [Fact]
    public void FormatRoute_with_only_origin_prefixes_from()
        => Assert.Equal("from KJFK", TcasForm.FormatRoute("KJFK", ""));

    [Fact]
    public void FormatRoute_with_only_destination_prefixes_to()
        => Assert.Equal("to EGLL", TcasForm.FormatRoute("", "EGLL"));

    [Fact]
    public void FormatRoute_with_neither_returns_empty()
        => Assert.Equal("", TcasForm.FormatRoute("", ""));

    // --- TrafficKey --------------------------------------------------------------------

    [Fact]
    public void TrafficKey_uses_the_callsign_when_present()
    {
        var t = new TcasTraffic { Callsign = "UAL123", ObjectId = 42 };
        Assert.Equal("UAL123", TcasForm.TrafficKey(t));
    }

    [Fact]
    public void TrafficKey_falls_back_to_object_id_when_callsign_is_empty()
    {
        var t = new TcasTraffic { Callsign = "", ObjectId = 42 };
        Assert.Equal("42", TcasForm.TrafficKey(t));
    }

    // --- BuildItemText -------------------------------------------------------------------

    private static TcasTraffic MakeAirborneTraffic() => new()
    {
        ObjectId = 1,
        Callsign = "UAL123",
        AircraftType = "B738",
        OnGround = false,
        GroundSpeedKnots = 250,
        HeadingMagnetic = 90,
        AltitudeFt = 35000,
        Airline = "United",
        DistanceNm = 5.0,
        AltitudeDiffFt = 0,
        RelativeBearing = 0,
    };

    [Fact]
    public void BuildItemText_airborne_includes_callsign_position_speed_and_altitude()
    {
        string text = TcasForm.BuildItemText(MakeAirborneTraffic(), gateResolver: null);

        Assert.Contains("UAL 123", text);
        Assert.Contains("250 knots", text);
        Assert.Contains("heading 90", text);
        Assert.Contains("35,000 feet", text);
        Assert.Contains("United", text);
        Assert.Contains("type B738", text);
    }

    [Fact]
    public void BuildItemText_airborne_never_shows_a_gate_label_even_when_a_resolver_is_supplied()
    {
        var resolver = new GateResolver(provider: null);
        string text = TcasForm.BuildItemText(MakeAirborneTraffic(), resolver);
        Assert.DoesNotContain(" at ", text);
    }

    [Fact]
    public void BuildItemText_ground_traffic_omits_altitude()
    {
        var t = MakeAirborneTraffic();
        t.OnGround = true;
        string text = TcasForm.BuildItemText(t, gateResolver: null);
        Assert.DoesNotContain("feet", text);
    }

    [Fact]
    public void BuildItemText_unknown_callsign_falls_back_to_object_id_label()
    {
        var t = MakeAirborneTraffic();
        t.Callsign = "";
        t.ObjectId = 99;
        string text = TcasForm.BuildItemText(t, gateResolver: null);
        Assert.StartsWith("unknown 99", text);
    }

    [Fact]
    public void BuildItemText_includes_a_route_when_both_airports_are_known()
    {
        var t = MakeAirborneTraffic();
        t.FromAirport = "KJFK";
        t.ToAirport = "EGLL";
        string text = TcasForm.BuildItemText(t, gateResolver: null);
        Assert.Contains("KJFK to EGLL", text);
    }

    [Fact]
    public void BuildItemText_omits_type_and_airline_when_not_provided()
    {
        var t = MakeAirborneTraffic();
        t.AircraftType = "";
        t.Airline = "";
        string text = TcasForm.BuildItemText(t, gateResolver: null);
        Assert.DoesNotContain("type", text);
    }
}
