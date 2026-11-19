using System.Globalization;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The FBW A380X First Officer's own read-back value text (docs/invariants/first-officer-airbus.md#foa-9):
/// the A380's autobrake modes, four-engine anti-ice, flaps in CONF terms, the FCU baro word and the
/// approach minimum. Culture-invariant: the rows below also run under de-DE and sv-SE, because CI runs
/// under en-US and would never catch a comma decimal in speech.
/// </summary>
public class FbwA380ReadbackFormatTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static T InCulture<T>(string name, Func<T> f)
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

    // An FBW ARINC 429 word: the SSM in bits 32-33, the value's float bits in the low 32. The one packer the
    // FCU tests already use, not a pasted copy of it.
    private static double Word(uint ssm, float value) => FcuValuePhrasesTests.Word(ssm, value);

    [Theory, MemberData(nameof(Cultures))]
    public void Autobrake_names_every_A380_armed_mode(string culture)
    {
        (double Mode, string Expected)[] rows =
        {
            (0, "off"), (1, "brake to vacate"), (2, "low"), (3, "L2"), (4, "L3"), (5, "high"), (6, "RTO"),
        };
        foreach (var (mode, expected) in rows)
            Assert.Equal(expected, InCulture(culture, () => FbwA380ReadbackFormat.Autobrake(mode)));
    }

    [Fact]
    public void Autobrake_unknown_or_out_of_range_is_null()
    {
        Assert.Null(FbwA380ReadbackFormat.Autobrake(double.NaN));
        Assert.Null(FbwA380ReadbackFormat.Autobrake(7));
        Assert.Null(FbwA380ReadbackFormat.Autobrake(-1));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Flaps_are_named_in_CONF_terms(string culture)
    {
        (double Lever, string Expected)[] rows =
        {
            (0, "CONF 0"), (1, "CONF 1 plus F"), (2, "CONF 2"), (3, "CONF 3"), (4, "CONF full"),
        };
        foreach (var (lever, expected) in rows)
            Assert.Equal(expected, InCulture(culture, () => FbwA380ReadbackFormat.FlapsConf(lever)));
    }

    [Fact]
    public void Flaps_unknown_or_out_of_range_is_null()
    {
        Assert.Null(FbwA380ReadbackFormat.FlapsConf(double.NaN));
        Assert.Null(FbwA380ReadbackFormat.FlapsConf(5));
        Assert.Null(FbwA380ReadbackFormat.FlapsConf(-1));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Anti_ice_groups_four_matching_engines_and_names_each_when_they_differ(string culture)
    {
        Assert.Equal("engine anti-ice off, wing anti-ice off",
            InCulture(culture, () => FbwA380ReadbackFormat.AntiIce(0, 0, 0, 0, 0)));
        Assert.Equal("engine anti-ice on, wing anti-ice on",
            InCulture(culture, () => FbwA380ReadbackFormat.AntiIce(1, 1, 1, 1, 1)));
        Assert.Equal(
            "engine 1 anti-ice on, engine 2 anti-ice off, engine 3 anti-ice off, engine 4 anti-ice on, wing anti-ice off",
            InCulture(culture, () => FbwA380ReadbackFormat.AntiIce(1, 0, 0, 1, 0)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.AntiIce(0, 0, double.NaN, 0, 0)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.AntiIce(0, 0, 0, 0, double.NaN)));
    }

    [Fact]
    public void The_baro_word_is_read_like_the_A380_definition_reads_it()
    {
        Assert.Equal(1013.2, FbwA380ReadbackFormat.BaroHpaFromWord(Word(0b11, 1013.2f)), 3);
        Assert.Equal(29.92 * 33.8639, FbwA380ReadbackFormat.BaroHpaFromWord(Word(0b11, 29.92f)), 2);
        Assert.True(double.IsNaN(FbwA380ReadbackFormat.BaroHpaFromWord(double.NaN)));
        Assert.True(double.IsNaN(FbwA380ReadbackFormat.BaroHpaFromWord(0.0)));                  // never written
        Assert.True(double.IsNaN(FbwA380ReadbackFormat.BaroHpaFromWord(Word(0b00, 1013f))));   // failure warning
        Assert.True(double.IsNaN(FbwA380ReadbackFormat.BaroHpaFromWord(Word(0b01, 1013f))));   // no computed data
        Assert.True(double.IsNaN(FbwA380ReadbackFormat.BaroHpaFromWord(Word(0b11, 500f))));    // out of range
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Baro_speaks_standard_or_the_QNH_in_the_selected_unit(string culture)
    {
        Assert.Equal("standard", InCulture(culture, () => FbwA380ReadbackFormat.Baro(1, double.NaN, double.NaN)));
        Assert.Equal("QNH 1013", InCulture(culture, () => FbwA380ReadbackFormat.Baro(0, Word(0b11, 1013.2f), 0)));
        Assert.Equal("QNH 29.92", InCulture(culture, () => FbwA380ReadbackFormat.Baro(0, Word(0b11, 1013.2f), 1)));
        Assert.Equal("QNH 29.92", InCulture(culture, () => FbwA380ReadbackFormat.Baro(0, Word(0b11, 29.92f), 1)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.Baro(double.NaN, Word(0b11, 1013f), 0)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.Baro(0, Word(0b00, 1013f), 0)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.Baro(0, Word(0b11, 1013f), double.NaN)));
    }

    [Theory, MemberData(nameof(Cultures))]
    public void Minimum_speaks_the_baro_or_radio_minimum_that_is_set(string culture)
    {
        Assert.Equal("baro 940 feet", InCulture(culture, () => FbwA380ReadbackFormat.Minimum(940, -1)));
        Assert.Equal("radio 200 feet", InCulture(culture, () => FbwA380ReadbackFormat.Minimum(0, 200)));
        // A DH of 0 is the A380's NO DH (the MFD's "NO" entry, A380Minimums), never "radio 0 feet".
        Assert.Equal("No DH", InCulture(culture, () => FbwA380ReadbackFormat.Minimum(0, 0)));
        Assert.Equal("baro 940 feet, No DH", InCulture(culture, () => FbwA380ReadbackFormat.Minimum(940, 0)));
        Assert.Equal("baro 1250 feet, radio 100 feet",
            InCulture(culture, () => FbwA380ReadbackFormat.Minimum(1250, 100)));
        Assert.Equal("not set", InCulture(culture, () => FbwA380ReadbackFormat.Minimum(0, -1)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.Minimum(double.NaN, -1)));
        Assert.Null(InCulture(culture, () => FbwA380ReadbackFormat.Minimum(940, double.NaN)));
    }
}
