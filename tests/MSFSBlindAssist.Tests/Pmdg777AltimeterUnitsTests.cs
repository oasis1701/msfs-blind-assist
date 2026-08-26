// The 777 baro readout is spoken the way it is read on the frequency — "QNH 1013" or
// "Altimeter 29.92", no colon, no unit word. What these pin is mostly the REFUSAL to pick
// a unit: naming one is only safe when both EFIS selectors agree AND the aircraft has
// actually said what they are set to.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class Pmdg777AltimeterUnitsTests
{
    [Fact]
    public void Hectopascals_are_read_as_a_QNH()
        => Assert.Equal("QNH 1013", Pmdg777AltimeterUnits.Describe(29.914, true, true));

    [Fact]
    public void Inches_are_read_as_an_altimeter_setting()
        => Assert.Equal("Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, false, false));

    [Fact]
    public void There_is_no_colon_so_the_phrase_matches_an_ATIS_or_METAR()
    {
        Assert.DoesNotContain(":", Pmdg777AltimeterUnits.Describe(29.914, true, true));
        Assert.DoesNotContain(":", Pmdg777AltimeterUnits.Describe(29.98, false, false));
        Assert.DoesNotContain(":", Pmdg777AltimeterUnits.Describe(29.98, true, false));
    }

    [Fact]
    public void A_split_pair_speaks_both_because_neither_pilot_may_get_the_others_unit()
    {
        // The two EFIS selectors are independent and a genuine split happens. There is only
        // ONE pressure to report - PMDG drives a single Kohlsman value - so the split is in
        // the units alone, and both readings of that one pressure are given.
        Assert.Equal("QNH 1015, Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, true, false));
        Assert.Equal("QNH 1015, Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, false, true));
    }

    [Fact]
    public void An_unknown_selector_speaks_both_never_a_guess()
    {
        // Every PMDG field reads 0.0 before the first CDA snapshot, and 0 means INCHES - so
        // an ungated read announces inches on an aeroplane set to hectopascals. Callers pass
        // null instead, and null must never resolve to a unit.
        Assert.Equal("QNH 1015, Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, null, null));
        Assert.Equal("QNH 1015, Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, true, null));
        Assert.Equal("QNH 1015, Altimeter 29.98", Pmdg777AltimeterUnits.Describe(29.98, null, false));
    }

    [Fact]
    public void Standard_outranks_the_unit_choice_entirely()
    {
        // STD is a state, not a pressure - there is no QNH or inches answer to give.
        foreach (var (c, f) in new (bool?, bool?)[] { (true, true), (false, false), (true, false), (null, null) })
            Assert.Equal("Altimeter standard", Pmdg777AltimeterUnits.Describe(29.92, c, f));
    }

    [Fact]
    public void The_standard_band_is_tight_enough_that_a_real_setting_beside_it_still_reads()
    {
        // 29.93 and 29.91 are settings a controller can actually issue; neither is STD.
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterUnits.Describe(29.93, false, false));
        Assert.NotEqual("Altimeter standard", Pmdg777AltimeterUnits.Describe(29.91, false, false));
    }

    [Fact]
    public void Inches_always_carry_two_decimals_so_a_round_value_is_not_clipped()
        => Assert.Equal("Altimeter 30.00", Pmdg777AltimeterUnits.Describe(30.00, false, false));

    [Theory]
    [InlineData(29.92, 1013)]
    [InlineData(30.06, 1018)]
    [InlineData(28.50, 965)]
    public void Hectopascals_are_rounded_whole_the_way_a_baro_window_shows_them(double inHg, int hpa)
        => Assert.Equal(hpa, (int)Math.Round(inHg * Pmdg777AltimeterUnits.InHgToHpa));
}

/// <summary>
/// The baro readout depends on TWO spellings of the same thing, and they are reached by
/// different routes: the background path matches the VAR KEY (OnPMDGVariableChanged has
/// already translated the struct field name away by then), while the hotkey pulls the
/// STRUCT FIELD NAME through GetFieldValue. Matching the field name in the background path
/// silently never fires — it shipped that way once, leaving the unit cache null forever and
/// the readout stuck announcing both units. Neither spelling can move without the other.
/// </summary>
public class Pmdg777BaroSelectorContractTests
{
    [Fact]
    public void Both_spellings_of_the_baro_selectors_are_the_ones_the_readout_relies_on()
    {
        var vars = new MSFSBlindAssist.Aircraft.PMDG777Definition().GetVariables();

        Assert.True(vars.ContainsKey("EFIS_BaroSelHPA_Capt"), "background path matches this key");
        Assert.True(vars.ContainsKey("EFIS_BaroSelHPA_FO"), "background path matches this key");
        Assert.Equal("EFIS_BaroSelHPA_0", vars["EFIS_BaroSelHPA_Capt"].Name);   // hotkey pulls this
        Assert.Equal("EFIS_BaroSelHPA_1", vars["EFIS_BaroSelHPA_FO"].Name);     // hotkey pulls this
    }
}
