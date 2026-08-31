// Wording only. Both units are still spoken, so there is no state here and no way for the
// readout to be wrong -- what these pin is that each number is labelled as the thing it is.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class Pmdg777AltimeterPhraseTests
{
    [Fact]
    public void Hectopascals_are_labelled_a_QNH_and_inches_an_altimeter_setting()
        => Assert.Equal("QNH 1013, Altimeter 29.91", Pmdg777AltimeterPhrase.Describe(29.914));

    [Fact]
    public void There_is_no_colon_so_it_reads_like_an_ATIS_rather_than_a_form_field()
        => Assert.DoesNotContain(":", Pmdg777AltimeterPhrase.Describe(29.98));

    [Fact]
    public void Both_units_are_always_spoken_so_the_readout_cannot_be_wrong()
    {
        // The point of the old readout was never brevity - it was that it states both
        // numbers and lets the pilot take the one they are working in. Naming a single unit
        // would need the app to know the EFIS selectors, and a stale answer would announce
        // the right pressure in the unit the pilot did not ask for.
        string p = Pmdg777AltimeterPhrase.Describe(29.98);
        Assert.Contains("QNH", p);
        Assert.Contains("Altimeter", p);
    }

    [Fact]
    public void Standard_is_a_state_not_a_pressure_so_no_number_is_read()
    {
        Assert.Equal("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.92));
        Assert.DoesNotContain("QNH", Pmdg777AltimeterPhrase.Describe(29.92));
    }

    // Hectopascals are the tight side of the STD band, and the side the old test missed:
    // it checked only inches, whose nearest neighbours are 0.0100 away, while QNH 1013 is
    // 0.0061 away. A pressure wrongly read as STD is a number replaced by a state, with
    // nothing missing for the pilot to notice.
    [Theory]
    [InlineData(1012)]
    [InlineData(1013)]   // the close one - 29.9139 inHg, 0.0061 from standard
    [InlineData(1014)]
    public void A_whole_hectopascal_QNH_is_never_swallowed_as_standard(int hpa)
    {
        string p = Pmdg777AltimeterPhrase.Describe(hpa / Pmdg777AltimeterPhrase.InHgToHpa);
        Assert.NotEqual("Altimeter standard", p);
        Assert.StartsWith($"QNH {hpa},", p);
    }

    [Fact]
    public void An_inches_setting_beside_standard_still_reads()
    {
        // 29.93 and 29.91 are settings a controller can actually issue; neither is STD.
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.93));
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.91));
    }

    [Fact]
    public void The_standard_band_keeps_real_margin_on_both_sides()
    {
        // THIS is the test that protects the tolerance; the value-based ones above do not.
        // At the previous 0.005 an exact QNH 1013 was already excluded, so every one of them
        // passed then too. What was wrong was the MARGIN: 0.005 sat only 1.2x below a real
        // QNH 1013, so any rounding in the PMDG-to-SimConnect path could push a set pressure
        // into the band and announce it as "Altimeter standard".
        //
        // Both sides are pinned, because the fix for one is the bug for the other: widen it
        // and a QNH is swallowed, narrow it past true standard and STD stops registering.
        const double MinMargin = 1.8;
        double toQnh1013 = Math.Abs(
            Pmdg777AltimeterPhrase.StandardInHg - 1013 / Pmdg777AltimeterPhrase.InHgToHpa);
        double toTrueStandard = Math.Abs(
            Pmdg777AltimeterPhrase.StandardInHg - 1013.25 / Pmdg777AltimeterPhrase.InHgToHpa);

        Assert.True(Pmdg777AltimeterPhrase.StandardToleranceInHg * MinMargin < toQnh1013,
            $"band {Pmdg777AltimeterPhrase.StandardToleranceInHg} is within {MinMargin}x of "
            + $"QNH 1013 at {toQnh1013:0.0000} - a set pressure could read as standard");
        Assert.True(Pmdg777AltimeterPhrase.StandardToleranceInHg > toTrueStandard * MinMargin,
            $"band {Pmdg777AltimeterPhrase.StandardToleranceInHg} is within {MinMargin}x of "
            + $"true standard at {toTrueStandard:0.0000} - STD itself could stop reading");
    }

    [Fact]
    public void True_standard_still_reads_as_standard_however_it_arrives()
    {
        // 29.92 exactly, and 1013.25 hPa converted (29.9212) - both must be inside the band,
        // which is what stops it being narrowed until STD itself stops registering.
        Assert.Equal("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.92));
        Assert.Equal("Altimeter standard",
            Pmdg777AltimeterPhrase.Describe(1013.25 / Pmdg777AltimeterPhrase.InHgToHpa));
    }

    [Fact]
    public void Inches_always_carry_two_decimals_so_a_round_value_is_not_clipped()
        => Assert.Equal("QNH 1016, Altimeter 30.00", Pmdg777AltimeterPhrase.Describe(30.00));

    [Theory]
    [InlineData(30.06, 1018)]
    [InlineData(28.50, 965)]
    [InlineData(29.91, 1013)]
    public void Hectopascals_are_rounded_whole_the_way_a_baro_window_shows_them(double inHg, int hpa)
    {
        // Goes through Describe rather than re-doing the arithmetic, so it can actually
        // catch a regression in the phrase instead of only pinning the constant.
        Assert.StartsWith($"QNH {hpa},", Pmdg777AltimeterPhrase.Describe(inHg));
    }

    [Fact]
    public void The_numbers_survive_a_comma_decimal_locale()
    {
        // The format strings this replaced used the current culture, so a German-locale
        // pilot was told "29,92". InvariantCulture is the fix; nothing else pinned it.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("QNH 1018, Altimeter 30.06", Pmdg777AltimeterPhrase.Describe(30.06));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
