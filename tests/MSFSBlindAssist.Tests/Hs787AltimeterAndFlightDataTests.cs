// The WT-B78x 787 Ctrl+B altimeter dialog and Flight Data rows (HorizonSim 787-9, Kuro 787-8,
// stock Asobo 787-10 — one definition).
//
// Altimeter: K:KOHLSMAN_SET takes MILLIBARS x 16. The dialog sent inches x 16, which the sim
// clamped to 948 hPa for every entry; it also accepted hPa only. STD is the aircraft's own toggle
// (K:BAROMETRIC_STD_PRESSURE, parameter 1) read from L:XMLVAR_Baro1_ForcedToSTD.
//
// Flight Data: an L:var read as "number" returns the sim's base unit (metres, radians, m/s), not
// the unit the avionics wrote, so a destination distance read 13653434. Each navigation row is now
// read in its real unit and spoken with it. Measured live on the HorizonSim 787-9, 2026-11-19.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class Hs787AltimeterAndFlightDataTests
{
    [Theory]
    [InlineData("1013", 1013.0)]
    [InlineData("986", 986.0)]
    [InlineData(" 1013.2 ", 1013.2)]
    [InlineData("900", 900.0)]
    [InlineData("1100", 1100.0)]
    public void Hectopascals_are_taken_as_typed(string input, double expected)
    {
        Assert.True(HS787Altimeter.TryParseHpa(input, out double hpa));
        Assert.Equal(expected, hpa, 3);
    }

    [Theory]
    [InlineData("29.92")]
    [InlineData("29,92")]
    [InlineData("2992")]
    public void Inches_in_any_common_spelling_become_standard_hectopascals(string input)
    {
        Assert.True(HS787Altimeter.TryParseHpa(input, out double hpa));
        Assert.Equal(1013.2, hpa, 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("899")]
    [InlineData("1101")]
    [InlineData("25.99")]
    [InlineData("32.01")]
    [InlineData("3201")]
    [InlineData("500")]
    public void Entries_in_neither_range_are_refused(string input)
    {
        Assert.False(HS787Altimeter.TryParseHpa(input, out _));
    }

    [Theory]
    // Measured live: 16208 sets 1013 hPa, 16320 sets 1020, 15776 sets 986.
    [InlineData(1013.0, 16208u)]
    [InlineData(1020.0, 16320u)]
    [InlineData(986.0, 15776u)]
    public void Kohlsman_parameter_is_millibars_times_sixteen(double hpa, uint expected)
    {
        Assert.Equal(expected, HS787Altimeter.KohlsmanSetParameter(hpa));
    }

    [Fact]
    public void Kohlsman_parameter_is_never_the_inches_value_that_clamped_to_948()
    {
        // The old dialog sent round(29.91 * 16) = 479 — 29.9 mb, below the sim's floor.
        Assert.True(HS787Altimeter.KohlsmanSetParameter(1013) > 948 * 16);
    }

    [Fact]
    public void Baro_std_reads_the_flag_the_pfd_shows_std_from()
    {
        var def = new HorizonSim787Definition().GetVariables()["HS787_BaroSTD"];
        Assert.Equal("XMLVAR_Baro1_ForcedToSTD", def.Name);
    }

    public static TheoryData<string> FlightDataKeys() => new(HS787FlightData.Keys);

    [Theory]
    [MemberData(nameof(FlightDataKeys))]
    public void Every_flight_data_row_is_registered_and_read_in_its_real_unit(string key)
    {
        var vars = new HorizonSim787Definition().GetVariables();
        Assert.True(vars.TryGetValue(key, out var def), $"'{key}' is not a registered variable.");
        Assert.NotEqual("number", def!.Units);
        Assert.Equal(HS787FlightData.ReadUnit(key), def.Units);
    }

    [Fact]
    public void Every_flight_data_row_sits_on_a_flight_data_panel()
    {
        var def = new HorizonSim787Definition();
        var shown = def.GetPanelDisplayVariables().Values.SelectMany(v => v).ToHashSet();
        foreach (var key in HS787FlightData.Keys)
            Assert.Contains(key, shown);
    }

    [Theory]
    [InlineData("HS787_LV_WTBoeing_LNavData_Destination_Distance_Direct", 7372.14, "7372.1 nautical miles")]
    [InlineData("HS787_LV_WT_LNavData_XTK", -0.126, "-0.13 nautical miles")]
    [InlineData("HS787_LV_WTAP_LNav_DTK", 245.4, "245 degrees")]
    [InlineData("HS787_LV_WTAP_LNav_DTK", 359.7, "0 degrees")]
    [InlineData("HS787_LV_WTAP_LNav_DTK", -10, "350 degrees")]
    [InlineData("HS787_LV_WTAP_VNav_FPA", -3.0, "-3.0 degrees")]
    [InlineData("HS787_LV_WTAP_GP_FPA", 0.02, "0.0 degrees")]
    [InlineData("HS787_LV_WTAP_LNav_Along_Track_Speed", 486.6, "487 knots")]
    [InlineData("HS787_LV_WTAP_VNav_Vertical_Deviation", -0.3, "0 feet")]
    [InlineData("HS787_LV_WTAP_GP_Required_VS", -812.5, "-813 feet per minute")]
    public void Rows_are_spoken_with_their_unit(string key, double value, string expected)
    {
        Assert.True(HS787FlightData.TryFormat(key, value, out string text));
        Assert.Equal(expected, text);
    }

    [Fact]
    public void Rows_outside_the_table_fall_through_to_the_generic_display()
    {
        Assert.False(HS787FlightData.TryFormat("HS787_LV_WTAP_LNav_Is_Tracking", 1, out _));
        Assert.Null(HS787FlightData.ReadUnit("HS787_LV_WTAP_LNav_Is_Tracking"));
    }
}
