using System.Globalization;
using MSFSBlindAssist.Services.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Services/Volanta/VolantaTrafficFormatter. Every test runs under de-DE, where the
// PC's own number format would write "4,3" and "6.900": the Volanta window must read the invariant forms.
public sealed class VolantaTrafficFormatterTests : IDisposable
{
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public VolantaTrafficFormatterTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    private static VolantaTrafficEntry Entry(VolantaFlight f, double distanceNm = 31.2, double altitudeDiff = -6880,
        int clock = 12, double? age = 12)
        => new(f, f.Latitude, f.Longitude, distanceNm, 5, 0, clock, altitudeDiff, age);

    private static readonly VolantaFlight Learjet = new()
    {
        Id = "n-1", Source = VolantaSource.Vatsim, Callsign = "DLX145", UserName = "Pilot Two", AircraftIcao = "LJ35",
        Origin = "KIND", Destination = "MMAS", AltitudeFt = 29145, GroundSpeedKt = 280.4, HeadingTrue = 4.6,
    };

    private static readonly VolantaFlight Airbus = new()
    {
        Id = "f-9", Source = VolantaSource.Volanta, AlsoOnNetwork = VolantaSource.Vatsim, Callsign = "AAL6336",
        FlightNumber = "AA6336", UserName = "pilot_three", AircraftIcao = "A319", Registration = "N701UW",
        Origin = "KORD", Destination = "KLEX", Route = "DCT", Simulator = "msfs24", AltitudeFt = 14680,
        AltitudeAglFt = 13900.4, GroundSpeedKt = 380, VerticalSpeedFpm = -2004, HeadingTrue = 160,
        Transponder = "2201",
    };

    [Theory]
    [InlineData(4.34, "4.3 nm")]
    [InlineData(9.96, "10.0 nm")]
    [InlineData(31.2, "31 nm")]
    [InlineData(1234.6, "1235 nm")]
    public void Distance_has_one_decimal_under_ten_nm(double nm, string expected)
        => Assert.Equal(expected, VolantaTrafficFormatter.Distance(nm));

    [Theory]
    [InlineData(150, "same altitude")]
    [InlineData(-199, "same altitude")]
    [InlineData(1234, "1,200 feet above")]
    [InlineData(-6880, "6,900 feet below")]
    [InlineData(-29200, "29,200 feet below")]
    public void Relative_altitude_is_rounded_to_100_feet(double diff, string expected)
        => Assert.Equal(expected, VolantaTrafficFormatter.RelativeAltitude(diff));

    [Fact]
    public void A_network_pilot_s_line_leads_with_the_spoken_callsign_and_its_place()
        => Assert.Equal("DLX 145, 12 o'clock, 31 nm, 6,900 feet below, 280 knots, LJ35, VATSIM, Pilot Two, KIND to MMAS",
            VolantaTrafficFormatter.ListLine(Entry(Learjet)));

    [Fact]
    public void A_Volanta_user_s_line_names_the_user_and_the_network_they_also_fly_on()
        => Assert.Equal("AAL 6336, 5 o'clock, 4.3 nm, 15,300 feet below, descending, 380 knots, A319, Volanta user pilot_three, on VATSIM, KORD to KLEX",
            VolantaTrafficFormatter.ListLine(Entry(Airbus, distanceNm: 4.3, altitudeDiff: -15320, clock: 5)));

    [Fact]
    public void Once_looked_up_the_location_closes_the_line_after_a_full_stop()
    {
        var united = new VolantaFlight
        {
            Id = "n-7", Source = VolantaSource.Vatsim, Callsign = "UAL763", UserName = "Ryan Burke KDEN", AircraftIcao = "A320",
            Origin = "CYYZ", Destination = "KORD", AltitudeFt = 13300, GroundSpeedKt = 190,
        };
        var entry = Entry(united, distanceNm: 75, altitudeDiff: -22700, clock: 7) with { Location = "Pike Lake, Wisconsin, United States" };
        Assert.Equal("UAL 763, 7 o'clock, 75 nm, 22,700 feet below, 190 knots, A320, VATSIM, Ryan Burke KDEN, CYYZ to KORD. "
            + "Location: Pike Lake, Wisconsin, United States", VolantaTrafficFormatter.ListLine(entry));
        Assert.Equal("UAL 763, 7 o'clock, 75 nm, 22,700 feet below, 190 knots, A320, VATSIM, Ryan Burke KDEN, CYYZ to KORD. "
            + "Location: over Lake Michigan", VolantaTrafficFormatter.ListLine(entry with { Location = "over Lake Michigan" }));
        Assert.Contains("Position: 7 o'clock, 75 nm, bearing 005 true" + Environment.NewLine + "Location: Pike Lake, Wisconsin, United States",
            VolantaTrafficFormatter.Details(entry));
    }

    [Fact]
    public void Before_the_lookup_the_line_and_details_have_no_location()
    {
        Assert.DoesNotContain("Location", VolantaTrafficFormatter.ListLine(Entry(Learjet)));
        Assert.DoesNotContain("Location", VolantaTrafficFormatter.Details(Entry(Learjet)));
    }

    [Fact]
    public void Traffic_on_the_ground_says_so_instead_of_altitude_and_speed()
    {
        var parked = Learjet with { OnGround = true, GroundSpeedKt = 3 };
        Assert.Equal("DLX 145, 12 o'clock, 31 nm, on the ground, LJ35, VATSIM, Pilot Two, KIND to MMAS",
            VolantaTrafficFormatter.ListLine(Entry(parked)));
    }

    [Fact]
    public void Without_a_callsign_the_registration_then_the_user_leads()
    {
        Assert.Equal("N701UW", VolantaTrafficFormatter.DisplayName(Airbus with { Callsign = "" }));
        Assert.Equal("pilot_three", VolantaTrafficFormatter.DisplayName(Airbus with { Callsign = "", Registration = "" }));
        Assert.Equal("Unknown aircraft", VolantaTrafficFormatter.DisplayName(Airbus with { Callsign = "", Registration = "", UserName = "" }));
    }

    [Fact]
    public void Details_list_every_known_field_in_invariant_numbers()
    {
        string details = VolantaTrafficFormatter.Details(Entry(Airbus, distanceNm: 4.3, altitudeDiff: -15320, clock: 5, age: 95));
        Assert.Equal(string.Join(Environment.NewLine, new[]
        {
            "Callsign: AAL6336",
            "Flight number: AA6336",
            "Pilot: Volanta user pilot_three, on VATSIM",
            "Aircraft: A319",
            "Registration: N701UW",
            "From: KORD",
            "To: KLEX",
            "Route: DCT",
            "Simulator: Microsoft Flight Simulator 2024",
            "Position: 5 o'clock, 4.3 nm, bearing 005 true",
            "Altitude: 14,680 feet, 15,300 feet below, descending",
            "Height above ground: 13,900 feet",
            "Ground speed: 380 knots",
            "Vertical speed: -2000 feet per minute",
            "Heading: 160 true",
            "Squawk: 2201",
            "Position reported: 2 minutes ago",
        }), details);
    }

    [Fact]
    public void A_vertical_speed_that_rounds_to_zero_never_reads_minus_zero()
        => Assert.Contains("Vertical speed: 0 feet per minute",
            VolantaTrafficFormatter.Details(Entry(Airbus with { VerticalSpeedFpm = -4 })));

    [Theory]
    [InlineData(4.6, "005")]
    [InlineData(0.2, "360")]
    [InlineData(359.6, "360")]
    [InlineData(-90, "270")]
    public void Headings_are_three_digits_with_360_for_north(double deg, string expected)
        => Assert.Equal(expected, VolantaTrafficFormatter.Heading(deg));

    [Theory]
    [InlineData(0.4, "just now")]
    [InlineData(12.4, "12 seconds ago")]
    [InlineData(89, "89 seconds ago")]
    [InlineData(95, "2 minutes ago")]
    [InlineData(65 * 60, "65 minutes ago")]
    public void Ages_read_in_seconds_then_minutes(double seconds, string expected)
        => Assert.Equal(expected, VolantaTrafficFormatter.Age(seconds));

    [Theory]
    [InlineData("Atis", "ATIS")]
    [InlineData("ApproachDeparture", "Approach Departure")]
    [InlineData("Ground", "Ground")]
    [InlineData("Fss", "Flight Service")]
    [InlineData("", "")]
    public void Facility_words_read_as_a_pilot_says_them(string facility, string expected)
        => Assert.Equal(expected, VolantaTrafficFormatter.FacilityName(facility));

    [Fact]
    public void A_controller_line_and_its_details_include_frequency_and_ATIS()
    {
        var c = new VolantaController
        {
            Id = "c-1", Callsign = "EDDP_GND", Network = VolantaSource.Vatsim, Facility = "Ground", Frequency = "121.805",
            UserName = "Controller One", AirportIcao = "EDDP", Atis = "Leipzig Ground\n\nBriefing at example",
            OnlineSince = new DateTimeOffset(2026, 10, 15, 17, 28, 39, TimeSpan.Zero),
        };
        Assert.Equal("EDDP GND, Ground, 121.805, VATSIM, 12 nm", VolantaTrafficFormatter.ControllerLine(c, 12.2));
        Assert.Equal("EDDP GND, Ground, 121.805, VATSIM", VolantaTrafficFormatter.ControllerLine(c, null));
        Assert.Equal(string.Join(Environment.NewLine, new[]
        {
            "Station: EDDP_GND", "Facility: Ground", "Frequency: 121.805", "Network: VATSIM", "Controller: Controller One",
            "Airport: EDDP", "Distance: 4.3 nm", "Online since: 17:28 UTC", "ATIS:", "Leipzig Ground", "Briefing at example",
        }), VolantaTrafficFormatter.ControllerDetails(c, 4.3));
    }
}
