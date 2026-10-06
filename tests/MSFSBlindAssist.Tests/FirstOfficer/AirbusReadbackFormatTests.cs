using System.Globalization;
using MSFSBlindAssist.FirstOfficer.Airbus;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class AirbusReadbackFormatTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static T InCulture<T>(string name, System.Func<T> f)
    {
        var old = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name);
            return f();
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Theory]
    [InlineData(0, "flaps up")] [InlineData(1, "flaps 1")] [InlineData(2, "flaps 2")]
    [InlineData(3, "flaps 3")] [InlineData(4, "flaps full")]
    public void Flaps(double index, string expected) => Assert.Equal(expected, AirbusReadbackFormat.FlapsLever(index));

    [Fact]
    public void Flaps_unknown_or_out_of_range_is_null()
    {
        Assert.Null(AirbusReadbackFormat.FlapsLever(double.NaN));
        Assert.Null(AirbusReadbackFormat.FlapsLever(5));
        Assert.Null(AirbusReadbackFormat.FlapsLever(-1));
    }

    [Theory]
    [InlineData(0, "crank")] [InlineData(1, "normal")] [InlineData(2, "ignition start")]
    public void EngineMode(double v, string expected) => Assert.Equal(expected, AirbusReadbackFormat.EngineMode(v));

    [Theory]
    [InlineData(0, "standby")] [InlineData(1, "TA only")] [InlineData(2, "TA/RA")]
    public void Tcas(double v, string expected) => Assert.Equal(expected, AirbusReadbackFormat.Tcas(v));

    [Fact]
    public void OnOff_and_packs()
    {
        Assert.Equal("on", AirbusReadbackFormat.OnOff(1));
        Assert.Equal("off", AirbusReadbackFormat.OnOff(0));
        Assert.Null(AirbusReadbackFormat.OnOff(double.NaN));
        Assert.Equal("pack 1 on, pack 2 off", AirbusReadbackFormat.Packs(1, 0));
        Assert.Null(AirbusReadbackFormat.Packs(1, double.NaN));
    }

    [Fact]
    public void AntiIce_groups_matching_engines()
    {
        Assert.Equal("engine anti-ice off, wing anti-ice off", AirbusReadbackFormat.AntiIce(0, 0, 0));
        Assert.Equal("engine anti-ice on, wing anti-ice on", AirbusReadbackFormat.AntiIce(1, 1, 1));
        Assert.Equal("engine 1 anti-ice on, engine 2 anti-ice off, wing anti-ice off",
            AirbusReadbackFormat.AntiIce(1, 0, 0));
        Assert.Null(AirbusReadbackFormat.AntiIce(double.NaN, 0, 0));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Baro_is_culture_invariant(string culture)
    {
        Assert.Equal("standard", InCulture(culture, () => AirbusReadbackFormat.Baro(1, double.NaN, false)));
        Assert.Equal("QNH 1013", InCulture(culture, () => AirbusReadbackFormat.Baro(0, 1013.2, false)));
        Assert.Equal("QNH 29.92", InCulture(culture, () => AirbusReadbackFormat.Baro(0, 29.921, true)));
        Assert.Null(InCulture(culture, () => AirbusReadbackFormat.Baro(double.NaN, 1013, false)));
        Assert.Null(InCulture(culture, () => AirbusReadbackFormat.Baro(0, double.NaN, false)));
    }

    [Theory]
    [InlineData(0, "off")] [InlineData(1, "low")] [InlineData(2, "medium")] [InlineData(3, "max")]
    public void Autobrake(double v, string expected) => Assert.Equal(expected, AirbusReadbackFormat.Autobrake(v));

    [Fact]
    public void Autobrake_from_lamps()
    {
        Assert.Equal(3, AirbusReadbackFormat.AutobrakeModeFromLamps(0, 0, 1));
        Assert.Equal(2, AirbusReadbackFormat.AutobrakeModeFromLamps(0, 1, 0));
        Assert.Equal(1, AirbusReadbackFormat.AutobrakeModeFromLamps(1, 0, 0));
        Assert.Equal(0, AirbusReadbackFormat.AutobrakeModeFromLamps(0, 0, 0));
        Assert.True(double.IsNaN(AirbusReadbackFormat.AutobrakeModeFromLamps(double.NaN, 0, 0)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void TakeoffSpeeds_is_culture_invariant(string culture)
    {
        Assert.Equal("V1 142, VR 145, V2 149, flex 55",
            InCulture(culture, () => AirbusReadbackFormat.TakeoffSpeeds(142.4, 145, 149, 55)));
        Assert.Equal("V1 143, VR 145, V2 149, flex 56",
            InCulture(culture, () => AirbusReadbackFormat.TakeoffSpeeds(142.6, 145.2, 149.4, 55.5)));
        Assert.Equal("V1 142, VR 145, V2 149",
            InCulture(culture, () => AirbusReadbackFormat.TakeoffSpeeds(142, 145, 149, 0)));
        Assert.Equal("not set", InCulture(culture, () => AirbusReadbackFormat.TakeoffSpeeds(0, 145, 149, 55)));
        Assert.Null(InCulture(culture, () => AirbusReadbackFormat.TakeoffSpeeds(double.NaN, 1, 1, 1)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void FuelQuantity_rounds_to_100_with_invariant_grouping(string culture)
    {
        Assert.Equal("12,400 kilograms", InCulture(culture, () => AirbusReadbackFormat.FuelQuantity(12_380, false)));
        Assert.Equal("27,300 pounds", InCulture(culture, () => AirbusReadbackFormat.FuelQuantity(12_380, true)));
        Assert.Null(InCulture(culture, () => AirbusReadbackFormat.FuelQuantity(double.NaN, false)));
    }
}
