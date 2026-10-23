using System.Globalization;
using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's tank read-outs are in the EFB's weight unit, which bit 0 of
/// <c>L:MD11_EFB_OPTIONS_GENERAL</c> names (set for pounds). Issue #327: with Metric selected the
/// fuel keys called kilograms pounds, and Shift+F converted the kilograms a second time.
/// The values below are the live readings of 2026-10-23 (GE N821TD).
/// </summary>
public class Md11FuelUnitsTests
{
    private static List<(string Name, double? Value)> Readings(params double?[] values)
        => Md11FuelUnits.Tanks.Select((t, i) => (t.Name, i < values.Length ? values[i] : null)).ToList();

    [Theory]
    [InlineData(5.0, true)]     // Imperial, severe failures on (measured)
    [InlineData(4.0, false)]    // Metric, severe failures on (measured)
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(31.0, true)]    // every option bit set
    [InlineData(32.0, false)]   // a bit TFDi might add later never costs the unit
    [InlineData(33.0, true)]
    public void TanksInPounds_ReadsBitZero(double options, bool pounds)
        => Assert.Equal(pounds, Md11FuelUnits.TanksInPounds(options));

    /// <summary>Anything the EFB never writes is no unit at all, never a guess.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1.0)]
    [InlineData(16777216.0)]    // 2^24: past the float-exact integers, so bit 0 cannot be trusted
    [InlineData(4.5)]           // not a bitmask
    public void TanksInPounds_RefusesWhatTheEfbNeverWrites(double options)
        => Assert.Null(Md11FuelUnits.TanksInPounds(options));

    [Fact]
    public void TanksInPounds_IsUnknownBeforeDelivery()
        => Assert.Null(Md11FuelUnits.TanksInPounds(null));

    [Fact]
    public void Metric_PoundsKey_ConvertsTheKilogramsOnce()
        => Assert.Equal("Tank 1 26235, Tank 2 26235, Tank 3 26235, Auxiliary 0, Tail 0, Total 78705 pounds",
            Md11FuelUnits.Describe(Readings(11900, 11900, 11900, 0, 0), 4, null, kilograms: false));

    [Fact]
    public void Metric_KilogramsKey_ReadsTheTanksAsTheyAre()
        => Assert.Equal("Tank 1 11900, Tank 2 11900, Tank 3 11900, Auxiliary 0, Tail 0, Total 35700 kilograms",
            Md11FuelUnits.Describe(Readings(11900, 11900, 11900, 0, 0), 4, null, kilograms: true));

    [Fact]
    public void Imperial_PoundsKey_ReadsTheTanksAsTheyAre()
        => Assert.Equal("Tank 1 26200, Tank 2 26200, Tank 3 26200, Auxiliary 0, Tail 0, Total 78600 pounds",
            Md11FuelUnits.Describe(Readings(26200, 26200, 26200, 0, 0), 5, null, kilograms: false));

    [Fact]
    public void Imperial_KilogramsKey_ConvertsThePounds()
        => Assert.Equal("Tank 1 11884, Tank 2 11884, Tank 3 11884, Auxiliary 0, Tail 0, Total 35652 kilograms",
            Md11FuelUnits.Describe(Readings(26200, 26200, 26200, 0, 0), 5, null, kilograms: true));

    [Fact]
    public void UnknownUnit_ReadsNoNumber()
    {
        Assert.Equal(Md11FuelUnits.UnitUnknown, Md11FuelUnits.Describe(Readings(11900, 11900, 11900, 0, 0), null, null, kilograms: false));
        Assert.Equal(Md11FuelUnits.UnitUnknown, Md11FuelUnits.Describe(Readings(11900, 11900, 11900, 0, 0), 4.5, null, kilograms: true));
    }

    [Fact]
    public void NoTankDelivered_SaysUnavailable_WhateverTheUnit()
    {
        Assert.Equal(Md11FuelUnits.Unavailable, Md11FuelUnits.Describe(Readings(), 4, null, kilograms: false));
        Assert.Equal(Md11FuelUnits.Unavailable, Md11FuelUnits.Describe(Readings(), null, null, kilograms: false));
    }

    [Fact]
    public void ATankNeverDelivered_IsLeftOut()
        => Assert.Equal("Tank 1 100, Tank 3 100, Total 200 kilograms",
            Md11FuelUnits.Describe(Readings(100, null, 100), 4, null, kilograms: true));

    /// <summary>
    /// The cold load measured on the PW (2026-10-23): every tank read 0 for about 20 s while the
    /// sim already carried 142,579 lb. Those zeros are the aircraft not having published the tanks,
    /// never an empty aircraft, so the keys say unavailable instead of "Total 0".
    /// </summary>
    [Fact]
    public void AllTanksZero_WhileTheSimCarriesFuel_IsUnavailable()
    {
        Assert.Equal(Md11FuelUnits.Unavailable, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), 0, 142579, kilograms: false));
        Assert.Equal(Md11FuelUnits.Unavailable, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), null, 142579, kilograms: true));
    }

    /// <summary>A truly empty aircraft, or one whose sim weight is unknown, still reads its zeros.</summary>
    [Fact]
    public void AllTanksZero_WithNoFuelInTheSim_IsSpoken()
    {
        const string zero = "Tank 1 0, Tank 2 0, Tank 3 0, Auxiliary 0, Tail 0, Total 0 pounds";
        Assert.Equal(zero, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), 5, 0, kilograms: false));
        Assert.Equal(zero, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), 5, 999, kilograms: false));    // under the bound
        Assert.Equal(zero, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), 5, null, kilograms: false));   // no witness
        Assert.Equal(zero, Md11FuelUnits.Describe(Readings(0, 0, 0, 0, 0), 5, double.NaN, kilograms: false));
    }

    /// <summary>One tank with fuel means the tanks are published: an empty aux and tail are real.</summary>
    [Fact]
    public void TanksUnpublished_NeedsEveryDeliveredTankAtZero()
    {
        Assert.False(Md11FuelUnits.TanksUnpublished(new double?[] { 26500, 26500, 26500, 0, 0 }, 79554));
        Assert.False(Md11FuelUnits.TanksUnpublished(new double?[] { null, null, null, null, null }, 79554));
        Assert.True(Md11FuelUnits.TanksUnpublished(new double?[] { 0, null, 0, 0, 0 }, 1000));
    }

    [Fact]
    public void DescribeTank_NamesTheUnitOnlyWhenKnown()
    {
        Assert.Equal("26200 pounds", Md11FuelUnits.DescribeTank(26200, true));
        Assert.Equal("11900 kilograms", Md11FuelUnits.DescribeTank(11900, false));
        Assert.Equal("11900", Md11FuelUnits.DescribeTank(11900, null));
    }

    /// <summary>CI runs under en-US; a comma-decimal culture must not change a digit or a word.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Sentences_AreTheSameUnderACommaDecimalCulture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Tank 1 11884, Tank 2 11884, Tank 3 11884, Auxiliary 0, Tail 0, Total 35652 kilograms",
                Md11FuelUnits.Describe(Readings(26200, 26200, 26200, 0, 0), 5, null, kilograms: true));
            Assert.Equal("11900 kilograms", Md11FuelUnits.DescribeTank(11900.4, false));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
