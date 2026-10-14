using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300LeversAndReadoutsTests
{
    [Theory]
    [InlineData(0, "0 (>K:FLAPS_SET)")]
    [InlineData(2, "8192 (>K:FLAPS_SET)")]
    [InlineData(4, "16384 (>K:FLAPS_SET)")]
    [InlineData(9, "16384 (>K:FLAPS_SET)")]
    [InlineData(-1, "0 (>K:FLAPS_SET)")]
    public void Flaps_go_to_a_detent_by_index(int index, string rpn) => Assert.Equal(rpn, A300Levers.FlapsRpn(index));

    [Theory]
    [InlineData(1, "1 (>K:SPOILERS_ARM_SET)")]
    [InlineData(0, "0 (>K:SPOILERS_ARM_SET)")]
    public void Ground_spoilers_arm_and_disarm(double value, string rpn) => Assert.Equal(rpn, A300Levers.SpoilersArmRpn(value));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_speed_brake_is_written_in_whole_units_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("8192 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(0.5));
            Assert.Equal("16384 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(1.7));
            Assert.Equal("0 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(-0.2));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static A300Readout Readout(string key) => A300Readouts.All.Single(r => r.Key == key);

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Readouts_read_the_same_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1013 hectopascals, 29.91 inches", Readout("A300_RO_BARO_CPT").Format(1013));
            Assert.Equal("1013 hectopascals, 29.92 inches", A300Readouts.Altimeter(1013.25));
            Assert.Equal("35,000 feet", Readout("A300_RO_FCU_ALTITUDE").Format(35000));
            Assert.Equal("250 knots", Readout("A300_RO_FCU_SPEED").Format(250.4));
            Assert.Equal("-1500 feet per minute", Readout("A300_RO_FCU_VS").Format(-1500));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_altimeter_and_fuel_phrases_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            // The fleet's B key: both units, hectopascals first, or "standard".
            Assert.Equal("Altimeter: 1018, 30.06", A300Readouts.AltimeterReading(1017.9375, standard: false));
            Assert.Equal("Altimeter: 1013, 29.92", A300Readouts.AltimeterReading(1013.25, standard: false));
            Assert.Equal("Altimeter standard", A300Readouts.AltimeterReading(1013.25, standard: true));
            Assert.Equal("Fuel 12,345 pounds", A300Readouts.Fuel(12345.4, kilograms: false));
            Assert.Equal("Fuel 5,600 kilograms", A300Readouts.Fuel(12345.4, kilograms: true));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Standard_ignores_the_setting_the_aircraft_still_holds()
    {
        // STD is a flag only on the A300: the setting keeps its old value, which the pilot does not hear.
        Assert.Equal("Altimeter standard", A300Readouts.AltimeterReading(1024.94, standard: true));
        Assert.Equal("Altimeter: 1025, 30.27", A300Readouts.AltimeterReading(1024.94, standard: false));
    }

    [Theory]
    [InlineData(0, "Fuel 0 pounds", "Fuel 0 kilograms")]
    [InlineData(999.5, "Fuel 1,000 pounds", "Fuel 453 kilograms")]
    [InlineData(1234567.2, "Fuel 1,234,567 pounds", "Fuel 559,990 kilograms")]
    public void Fuel_reads_whole_units_with_a_thousands_separator(double pounds, string inPounds, string inKilograms)
    {
        Assert.Equal(inPounds, A300Readouts.Fuel(pounds, kilograms: false));
        Assert.Equal(inKilograms, A300Readouts.Fuel(pounds, kilograms: true));
    }

    [Theory]
    [InlineData(0, "000")]
    [InlineData(360, "000")]
    [InlineData(5.9, "005")]
    [InlineData(-10, "350")]
    [InlineData(359.99, "359")]
    public void Headings_read_as_the_window_shows_them(double degrees, string text) =>
        Assert.Equal(text, A300Readouts.Heading(degrees));

    [Fact]
    public void A_negative_zero_reads_as_zero() => Assert.Equal("0 feet per minute", Readout("A300_RO_FCU_VS").Format(-0.3));

    [Fact]
    public void The_oxygen_panel_reads_its_six_gauges_named_from_what_they_measure()
    {
        // The needles' own animation code, on the overhead oxygen panel. The courier gauge's variable is
        // iniBuilds' "HIGH_PRESSURE_CURRENT", but it rises with the courier low pressure supply (live, 2026-10-09).
        Assert.Equal(new[]
        {
            ("Crew oxygen low pressure", "INI_OXYGEN_LOW_PRESSURE_CURRENT"),
            ("Courier oxygen low pressure", "INI_OXYGEN_HIGH_PRESSURE_CURRENT"),
            ("Crew oxygen high pressure 1", "INI_OXYGEN_HIGH_PRESSURE_CURRENT1"),
            ("Crew oxygen high pressure 2", "INI_OXYGEN_HIGH_PRESSURE_CURRENT2"),
            ("Crew oxygen high pressure 3", "INI_OXYGEN_HIGH_PRESSURE_CURRENT3"),
            ("Crew oxygen high pressure 4", "INI_OXYGEN_HIGH_PRESSURE_CURRENT4"),
        }, A300Readouts.All.Where(r => r.Panel == "Oxygen").Select(r => (r.Name, r.Var)).ToArray());
    }

    [Fact]
    public void The_autobrake_panel_reads_the_brake_gauge()
    {
        // The triple brake indicator's needles (ACCU PRESS 0 to 4, BRAKES 0 to 3, PSI x 1000).
        var brakes = A300Readouts.All.Where(r => r.Panel == "Autobrake").ToArray();
        Assert.Equal(new[]
        {
            ("Brake accumulator pressure", "INI_BRAKE_PRESSURE_ACCU_PRESS"),
            ("Left brake pressure", "INI_BRAKE_PRESSURE_LEFT"),
            ("Right brake pressure", "INI_BRAKE_PRESSURE_RIGHT"),
        }, brakes.Select(r => (r.Name, r.Var)).ToArray());
        Assert.Equal("3,000 psi", brakes[0].Format(2.9999992847442627));
        Assert.Equal("2,300 psi", brakes[1].Format(2.2999987602233887));
    }

    [Theory]
    // The tilt knob's slider runs 0 to 100; the antenna tilt it sets, INI_WXR_TILT, runs 15 down to 15 up
    // (knob 40 is 3 down, measured 2026-10-10). Up is positive.
    [InlineData(-3, "3 degrees down")]
    [InlineData(15, "15 degrees up")]
    [InlineData(0, "0 degrees")]
    [InlineData(-0.4, "0 degrees")]
    public void The_weather_radar_panel_reads_its_antenna_tilt(double tilt, string text)
    {
        var readout = A300Readouts.All.Single(r => r.Panel == "Weather Radar");
        Assert.Equal(("Antenna tilt", "INI_WXR_TILT"), (readout.Name, readout.Var));
        Assert.Equal(text, readout.Format(tilt));
    }

    [Theory]
    // TRANSPONDER CODE:1 read as BCO16, one digit a nibble, as the FBW A320 and A380 read it.
    [InlineData(0x7000, "7000")]
    [InlineData(0x1200, "1200")]
    [InlineData(0x0042, "0042")]
    public void The_transponder_panel_reads_the_squawk_code(int bcd, string code)
    {
        var readout = A300Readouts.All.Single(r => r.Panel == "Transponder");
        Assert.Equal(("Squawk code", "TRANSPONDER CODE:1", "BCO16"), (readout.Name, readout.Var, readout.Units));
        Assert.Equal(code, readout.Format(bcd));
    }

    [Fact]
    public void The_navigation_radios_panel_reads_its_frequencies_and_courses()
    {
        // The VOR/ILS frequency selectors' windows: VOR 1 and 2 are NAV 1 and 2, the ILS is NAV 3 ([A300-10]).
        var lines = A300Readouts.All.Where(r => r.Panel == "Navigation Radios").Select(r => (r.Name, r.Var)).ToArray();
        Assert.Equal(new[]
        {
            ("VOR 1 course", "NAV OBS:1"),
            ("VOR 2 course", "NAV OBS:2"),
            ("ILS course", "INI_ils_course"),
            ("VOR 1", "NAV ACTIVE FREQUENCY:1"),
            ("VOR 2", "NAV ACTIVE FREQUENCY:2"),
            ("ILS", "NAV ACTIVE FREQUENCY:3"),
        }, lines);
    }

    [Fact]
    public void The_knob_read_backs_are_the_knobs_that_are_rows()
    {
        // Every knob with a typed box left the panels (design decision, Robin, 2026-10-14), and its box confirms the value
        // (the FCU windows, the altimeters, the three courses, the squawk). What is left reads back the landing elevation
        // and the flex temperature; the NAV frequency windows and the courses stay on the Navigation Radios status display.
        var expected = new[] { "A300_LANDING_ELEV_SET", A300Trp.FlexKnobKey };
        Assert.Equal(expected.Order(StringComparer.Ordinal), A300Readouts.KnobReadBacks.Keys.Order(StringComparer.Ordinal));
        Assert.Contains(A300Readouts.All, r => r.Key == A300Readouts.NavVor1Key);
        Assert.Contains(A300Readouts.All, r => r.Key == A300Readouts.NavVor2Key);
        Assert.Contains(A300Readouts.All, r => r.Key == A300Readouts.NavIlsKey);
        Assert.Contains(A300Readouts.All, r => r.Key == A300Readouts.IlsCourseKey);
        Assert.Contains(A300Readouts.All, r => r.Key == "A300_RO_VOR1_COURSE");
        Assert.Contains(A300Readouts.All, r => r.Key == "A300_RO_VOR2_COURSE");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_oxygen_gauges_read_in_psi_as_their_faces_show_them(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var oxygen = A300Readouts.All.Where(r => r.Panel == "Oxygen").ToArray();
            // The LP face reads PSI; the HP face reads "PSI x 1000", 0 to 2.
            Assert.Equal("70 psi", oxygen[0].Format(69.99998474121094));
            Assert.Equal("1,500 psi", oxygen[2].Format(1.5));
            Assert.Equal("1,850 psi", oxygen[5].Format(1.85));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
