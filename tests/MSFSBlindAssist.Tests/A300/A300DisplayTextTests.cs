using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300DisplayTextTests
{
    private const double Degree = Math.PI / 180;

    [Theory]
    [InlineData(-5.0, "5.0 degrees up")]   // SimConnect pitch is positive nose DOWN
    [InlineData(2.5, "2.5 degrees down")]
    [InlineData(0.3, "Level")]
    public void Pitch_reads_up_or_down(double degrees, string text)
    {
        Assert.Equal(text, A300DisplayText.Pitch(degrees * Degree));
    }

    [Theory]
    [InlineData(10.0, "10.0 degrees left")]   // SimConnect bank is positive LEFT
    [InlineData(-25.0, "25.0 degrees right")]
    [InlineData(-0.2, "Wings level")]
    public void Bank_reads_left_or_right(double degrees, string text)
    {
        Assert.Equal(text, A300DisplayText.Bank(degrees * Degree));
    }

    [Theory]
    [InlineData(1520, "1,520 feet per minute up")]
    [InlineData(-804, "800 feet per minute down")]
    [InlineData(30, "Level")]
    [InlineData(-49, "Level")]
    public void Vertical_speed_reads_in_tens_with_its_direction(double fpm, string text)
    {
        Assert.Equal(text, A300DisplayText.VerticalSpeed(fpm));
    }

    [Theory]
    [InlineData(179.76, "180 knots")]
    [InlineData(0, "not available")]
    [InlineData(-1, "not available")]
    public void A_speed_reads_whole_knots_or_not_available(double knots, string text)
    {
        Assert.Equal(text, A300DisplayText.Speed(knots));
    }

    [Theory]
    [InlineData(A300PfdSpeed.GreenDot, 0, "187 knots")]
    [InlineData(A300PfdSpeed.GreenDot, 1, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.S, 1, "187 knots")]
    [InlineData(A300PfdSpeed.S, 0, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.S, 2, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.F, 2, "187 knots")]
    [InlineData(A300PfdSpeed.F, 3, "187 knots")]
    [InlineData(A300PfdSpeed.F, 1, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.F, 4, "not shown at this flap setting")]
    public void Green_dot_s_and_f_read_only_at_the_flap_lever_positions_the_speed_tape_shows_them(A300PfdSpeed speed, double flapLever, string text)
    {
        // PFD::drawSpeedTape: green dot at lever 0, S at lever 1, F at levers 2 and 3.
        Assert.Equal(text, A300DisplayText.FlapSpeed(speed, 187.4, flapLever));
    }

    [Fact]
    public void A_flap_speed_with_no_flap_lever_known_reads_not_available()
    {
        Assert.Equal("not available", A300DisplayText.FlapSpeed(A300PfdSpeed.GreenDot, 187.4, null));
    }

    [Theory]
    [InlineData(0, A300PfdSpeed.GreenDot)]
    [InlineData(1, A300PfdSpeed.S)]
    [InlineData(2, A300PfdSpeed.F)]
    [InlineData(3, A300PfdSpeed.F)]
    [InlineData(4, null)]
    public void The_tape_draws_one_of_the_three_speeds_at_each_lever_but_the_last(double flapLever, A300PfdSpeed? drawn)
    {
        // PFD::drawSpeedTape (1.0.11): green dot at lever 0, S at 1, F at 2 and 3, none of them at 4.
        Assert.Equal(drawn, A300DisplayText.TapeSpeedAt(flapLever));
    }

    [Theory]
    [InlineData(A300PfdSpeed.S, 197.6, "Green dot not shown. S speed 198 knots")]
    [InlineData(A300PfdSpeed.F, 155.2, "Green dot not shown. F speed 155 knots")]
    [InlineData(A300PfdSpeed.S, 0.0, "Green dot not shown. S speed not available")]
    [InlineData(A300PfdSpeed.F, null, "Green dot not shown. F speed unavailable")]
    public void Green_dot_not_shown_names_the_speed_the_tape_shows_instead(A300PfdSpeed shown, double? knots, string text)
    {
        Assert.Equal(text, A300DisplayText.GreenDotNotShown(shown, knots));
    }

    [Theory]
    [InlineData(200, "200 feet")]
    [InlineData(0, "not set")]
    public void Minimums_read_in_feet_or_not_set(double feet, string text)
    {
        Assert.Equal(text, A300DisplayText.Minimums(feet));
    }

    [Theory]
    [InlineData(12000.4, "12,000 feet")]
    [InlineData(-30, "-30 feet")]
    public void Altitudes_read_with_a_thousands_separator(double feet, string text)
    {
        Assert.Equal(text, A300DisplayText.Feet(feet));
    }

    [Fact]
    public void Heading_reads_three_digits_from_radians_and_wraps()
    {
        Assert.Equal("000", A300DisplayText.HeadingFromRadians(2 * Math.PI));
        Assert.Equal("090", A300DisplayText.HeadingFromRadians(Math.PI / 2));
        Assert.Equal("270", A300DisplayText.HeadingFromRadians(-Math.PI / 2));
    }

    [Theory]
    [InlineData(12.34, "12.3 nautical miles")]
    [InlineData(0, "not available")]
    public void A_waypoint_distance_reads_in_tenths(double nm, string text)
    {
        Assert.Equal(text, A300DisplayText.WaypointDistance(nm));
    }

    [Theory]
    [InlineData(1.78, "1.8 nautical miles")]
    [InlineData(0, "no DME")]
    public void A_dme_distance_reads_in_tenths_or_no_dme(double nm, string text)
    {
        Assert.Equal(text, A300DisplayText.Dme(nm));
    }

    [Theory]
    [InlineData(116.55, "116.55 megahertz")]
    [InlineData(110.3, "110.30 megahertz")]
    [InlineData(0, "not tuned")]
    public void A_nav_frequency_reads_two_decimals(double mhz, string text)
    {
        Assert.Equal(text, A300DisplayText.Megahertz(mhz));
    }

    [Theory]
    [InlineData(890, "890 kilohertz")]
    [InlineData(0, "not tuned")]
    public void An_adf_frequency_reads_whole_kilohertz(double khz, string text)
    {
        Assert.Equal(text, A300DisplayText.Kilohertz(khz));
    }

    [Theory]
    [InlineData(88.6, "089 true")]
    [InlineData(359.7, "000 true")]
    public void A_wind_direction_reads_three_digits_true(double degrees, string text)
    {
        Assert.Equal(text, A300DisplayText.WindDirection(degrees));
    }

    [Theory]
    [InlineData(1, "received")]
    [InlineData(0, "not received")]
    public void A_receiver_flag_reads_received_or_not(double flag, string text)
    {
        Assert.Equal(text, A300DisplayText.Received(flag));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Numbers_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("5.0 degrees up", A300DisplayText.Pitch(-5 * Degree));
            Assert.Equal("10.5 degrees left", A300DisplayText.Bank(10.5 * Degree));
            Assert.Equal("1,520 feet per minute up", A300DisplayText.VerticalSpeed(1520));
            Assert.Equal("12,000 feet", A300DisplayText.Feet(12000));
            Assert.Equal("12.3 nautical miles", A300DisplayText.WaypointDistance(12.34));
            Assert.Equal("116.55 megahertz", A300DisplayText.Megahertz(116.55));
            Assert.Equal("Green dot not shown. S speed 198 knots", A300DisplayText.GreenDotNotShown(A300PfdSpeed.S, 197.6));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
