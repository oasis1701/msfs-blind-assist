// Wording only. Both units are still spoken, so there is no state here and no way for the
// readout to be wrong -- what these pin is that each number is labelled as the thing it is.

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

    [Fact]
    public void The_standard_band_is_tight_enough_that_a_real_setting_beside_it_still_reads()
    {
        // 29.93 and 29.91 are settings a controller can actually issue; neither is STD.
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.93));
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterPhrase.Describe(29.91));
    }

    [Fact]
    public void Inches_always_carry_two_decimals_so_a_round_value_is_not_clipped()
        => Assert.Equal("QNH 1016, Altimeter 30.00", Pmdg777AltimeterPhrase.Describe(30.00));

    [Theory]
    [InlineData(29.92, 1013)]
    [InlineData(30.06, 1018)]
    [InlineData(28.50, 965)]
    public void Hectopascals_are_rounded_whole_the_way_a_baro_window_shows_them(double inHg, int hpa)
        => Assert.Equal(hpa, (int)Math.Round(inHg * Pmdg777AltimeterPhrase.InHgToHpa));
}
