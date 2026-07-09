using System;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="SimConnectManager.FormatVariableValue"/>'s weight-unit handling.
/// Regression guard for the A320 Shift+F kilograms readout, which was announced as a
/// raw "Fuel on board: 13139.6" (one decimal, no unit) because "kilograms" had no
/// case in the unit switch and fell to the F1 default. Weight vars now round to whole
/// units and speak the unit.
/// </summary>
public class FuelReadoutFormatTests
{
    // The handle is only used when actually connecting to the sim; IntPtr.Zero is fine
    // for exercising the pure formatting logic.
    private static readonly SimConnectManager Mgr = new SimConnectManager(IntPtr.Zero);

    private static SimVarDefinition Def(string displayName, string units) =>
        new SimVarDefinition { DisplayName = displayName, Units = units };

    [Theory]
    [InlineData(12724.3, "Fuel on board: 12724 kilograms")] // the exact reported case (rounds down)
    [InlineData(13139.6, "Fuel on board: 13140 kilograms")] // rounds up
    [InlineData(5960.0, "Fuel on board: 5960 kilograms")]
    public void Kilograms_readout_rounds_and_speaks_the_unit(double value, string expected)
        => Assert.Equal(expected, Mgr.FormatVariableValue("FUEL_QUANTITY_KG", Def("Fuel on board", "kilograms"), value));

    [Theory]
    [InlineData(28001.4, "Fuel on board: 28001 pounds")]
    [InlineData(6614.5, "Fuel on board: 6614 pounds")] // banker's rounding to even
    public void Pounds_readout_rounds_and_speaks_the_unit(double value, string expected)
        => Assert.Equal(expected, Mgr.FormatVariableValue("FUEL_QTY", Def("Fuel on board", "pounds"), value));

    // Sibling unit cases must be unaffected by the new weight case.
    [Theory]
    [InlineData("knots", 250.7, "Airspeed: 251 knots")]
    [InlineData("feet", 5234.4, "Altitude: 5234 feet")]
    [InlineData("degrees", 89.6, "Heading: 90 degrees")]
    public void Existing_unit_cases_are_unchanged(string units, double value, string expected)
        => Assert.Equal(expected, Mgr.FormatVariableValue("X", Def(units == "knots" ? "Airspeed" : units == "feet" ? "Altitude" : "Heading", units), value));

    // A var with an unrecognized unit still hits the F1 default (one decimal, no unit).
    [Fact]
    public void Unknown_unit_still_uses_the_one_decimal_default()
        => Assert.Equal("Ratio: 5.5", Mgr.FormatVariableValue("X", Def("Ratio", "number"), 5.5));
}
