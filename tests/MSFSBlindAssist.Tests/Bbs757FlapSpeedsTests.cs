using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's flap maneuver speeds (plan 06, <see cref="Bbs757FlapSpeeds"/>): VREF 30 and 25 from the twelve
/// points measured on BlueBird's own APPROACH REF page, linear between them and unavailable outside them,
/// and the owner's schedule (flaps up VREF30 + 80, 1 + 60, 5 + 40, 15 + 20, 20 + 20).
/// </summary>
public class Bbs757FlapSpeedsTests
{
    private const string Unavailable = "Flap speeds unavailable.";

    [Theory]
    [InlineData(140_000, 109, 111)]
    [InlineData(150_000, 113, 115)]
    [InlineData(160_000, 117, 119)]
    [InlineData(170_000, 121, 123)]
    [InlineData(180_000, 125, 127)]
    [InlineData(190_000, 129, 131)]
    [InlineData(200_000, 133, 135)]
    [InlineData(210_000, 137, 139)]
    [InlineData(220_000, 140, 142)]
    [InlineData(230_000, 144, 146)]
    [InlineData(240_000, 148, 150)]
    [InlineData(250_000, 152, 154)]
    public void Vref_at_the_measured_points(double lbs, int vref30, int vref25)
    {
        Assert.Equal(vref30, Bbs757FlapSpeeds.Vref30(lbs));
        Assert.Equal(vref25, Bbs757FlapSpeeds.Vref25(lbs));
    }

    [Theory]
    [InlineData(178_600, 124, 126)]   // the FMC's own at this weight (session 8)
    [InlineData(145_000, 111, 113)]
    [InlineData(215_000, 139, 141)]   // 138.5 and 140.5: midpoints away from zero
    public void Vref_between_points(double lbs, int vref30, int vref25)
    {
        Assert.Equal(vref30, Bbs757FlapSpeeds.Vref30(lbs));
        Assert.Equal(vref25, Bbs757FlapSpeeds.Vref25(lbs));
    }

    [Fact]
    public void Maneuver_speeds_at_178600()
    {
        Assert.Equal("Flaps up maneuver speed 204 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 0));
        Assert.Equal("Flaps 1 maneuver speed 184 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 1));
        Assert.Equal("Flaps 5 maneuver speed 164 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 2));
        Assert.Equal("Flaps 15 maneuver speed 144 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 3));
        Assert.Equal("Flaps 20 maneuver speed 144 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 4));
        Assert.Equal("VREF 30 124 knots, VREF 25 126 knots, calculated", Bbs757FlapSpeeds.Describe(178_600, 5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0.0)]
    [InlineData(139_999.0)]
    [InlineData(250_001.0)]
    [InlineData(251_000.0)]   // heavy: never an extrapolated number
    public void Outside_the_table_is_unavailable(double? lbs)
    {
        Assert.Null(Bbs757FlapSpeeds.Vref30(lbs));
        Assert.Null(Bbs757FlapSpeeds.Vref25(lbs));
        for (int i = 0; i <= 5; i++) Assert.Equal(Unavailable, Bbs757FlapSpeeds.Describe(lbs, i));
    }

    [Theory]
    [InlineData(HotkeyAction.ReadSpeedGD, 0)]    // Shift+1: flaps up
    [InlineData(HotkeyAction.ReadSpeedS, 1)]     // Shift+2: flaps 1
    [InlineData(HotkeyAction.ReadSpeedF, 2)]     // Shift+3: flaps 5
    [InlineData(HotkeyAction.ReadSpeedVLS, 3)]   // Shift+4: flaps 15
    [InlineData(HotkeyAction.ReadSpeedVS, 4)]    // Shift+5: flaps 20
    [InlineData(HotkeyAction.ReadSpeedVFE, 5)]   // Shift+6: VREF 30 and 25
    [InlineData(HotkeyAction.ReadFlaps, -1)]     // not a flap speed key: left to the rest of the definition
    public void Keys_map_to_the_schedule(HotkeyAction action, int index)
    {
        Assert.Equal(index, BBS757Definition.FlapSpeedIndex(action));
    }

    [Fact]
    public void Sentences_under_de_DE()
    {
        List<string> Sentences() => new double?[] { 140_000, 178_600, 215_000, 250_000, 251_000, null }
            .SelectMany(w => Enumerable.Range(0, 6).Select(i => Bbs757FlapSpeeds.Describe(w, i))).ToList();

        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var expected = Sentences();
            foreach (string culture in new[] { "de-DE", "sv-SE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(expected, Sentences());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
