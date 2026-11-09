using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300BaroTests
{
    private const string Standard = "1 16212 (>K:2:KOHLSMAN_SET) 2 16212 (>K:2:KOHLSMAN_SET) 3 16212 (>K:2:KOHLSMAN_SET)";

    [Theory]
    [InlineData(0.0, 0.0, "QNH")]
    [InlineData(1.0, 1.0, "STD")]
    [InlineData(1.0, 0.0, "captain STD, first officer QNH")]
    [InlineData(0.0, 1.0, "captain QNH, first officer STD")]
    [InlineData(null, 1.0, "captain unknown, first officer STD")]
    [InlineData(null, null, "")]
    public void The_mode_button_reads_one_mode_or_each_side(double? captain, double? firstOfficer, string state) =>
        Assert.Equal(state, A300Baro.ModeState(captain, firstOfficer));

    [Theory]
    [InlineData(0.0, 0.0, A300BaroToggle.Standard)]
    [InlineData(1.0, 1.0, A300BaroToggle.Qnh)]
    [InlineData(1.0, 0.0, A300BaroToggle.Qnh)]     // sides differ: both end in QNH (design decision)
    [InlineData(0.0, 1.0, A300BaroToggle.Qnh)]
    [InlineData(null, 0.0, A300BaroToggle.Unknown)]
    [InlineData(0.0, null, A300BaroToggle.Unknown)]
    public void The_mode_button_goes_to_std_only_from_both_qnh(double? captain, double? firstOfficer, A300BaroToggle toggle) =>
        Assert.Equal(toggle, A300Baro.ToggleFor(captain, firstOfficer));

    [Fact]
    public void A_typed_value_pushes_only_the_sides_in_std()
    {
        Assert.Empty(A300Baro.SidesInStd(0, 0)!);
        Assert.Equal(new[] { A300Baro.Captain }, A300Baro.SidesInStd(1, 0));
        Assert.Equal(new[] { A300Baro.Captain, A300Baro.FirstOfficer }, A300Baro.SidesInStd(1, 1));
        Assert.Null(A300Baro.SidesInStd(null, 0));
        Assert.Null(A300Baro.SidesInStd(1, null));
    }

    [Fact]
    public void Standard_pulls_both_sides_in_qnh_then_sets_all_three_to_1013()
    {
        var plan = A300Baro.Standard(0, 0);
        Assert.Equal(new[] { A300Baro.Captain, A300Baro.FirstOfficer }, plan.Pressed);
        Assert.Equal(Standard, plan.Rpn);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void Standard_never_pulls_a_side_already_in_std_which_would_save_1013_over_its_qnh()
    {
        var plan = A300Baro.Standard(1, 0);
        Assert.Equal(new[] { A300Baro.FirstOfficer }, plan.Pressed);
        Assert.Equal(Standard, plan.Rpn);
    }

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData(0.0, null)]
    public void An_unread_mode_is_refused(double? captain, double? firstOfficer)
    {
        Assert.Equal(A300Baro.UnknownModeRefusal, A300Baro.Standard(captain, firstOfficer).Refusal);
        Assert.Equal(A300Baro.UnknownModeRefusal, A300Baro.Qnh(captain, firstOfficer, 16212, 16212).Refusal);
    }

    [Fact]
    public void Qnh_pushes_both_and_restores_each_sides_saved_setting_with_the_standby_on_the_captains()
    {
        var plan = A300Baro.Qnh(1, 1, 16399, 16288);
        Assert.Equal(new[] { A300Baro.Captain, A300Baro.FirstOfficer }, plan.Pressed);
        Assert.Equal("1 16399 (>K:2:KOHLSMAN_SET) 2 16288 (>K:2:KOHLSMAN_SET) 3 16399 (>K:2:KOHLSMAN_SET)", plan.Rpn);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Qnh_leaves_a_side_already_in_qnh_alone_since_its_saved_setting_may_be_older()
    {
        var plan = A300Baro.Qnh(0, 1, 16399, 16288);
        Assert.Equal(new[] { A300Baro.FirstOfficer }, plan.Pressed);
        Assert.Equal("2 16288 (>K:2:KOHLSMAN_SET) 3 16288 (>K:2:KOHLSMAN_SET)", plan.Rpn);
    }

    [Fact]
    public void A_missing_or_implausible_saved_setting_is_not_used_and_is_reported()
    {
        var plan = A300Baro.Qnh(1, 1, 0, 16288);
        Assert.Equal(new[] { A300Baro.Captain, A300Baro.FirstOfficer }, plan.Pressed);
        Assert.Equal("2 16288 (>K:2:KOHLSMAN_SET) 3 16288 (>K:2:KOHLSMAN_SET)", plan.Rpn);
        Assert.Equal(new[] { "Captain saved setting unavailable" }, plan.Warnings);

        var neither = A300Baro.Qnh(1, 1, null, 20000);
        Assert.Null(neither.Rpn);
        Assert.Equal(new[] { "Captain saved setting unavailable", "First officer saved setting unavailable" }, neither.Warnings);
    }

    [Fact]
    public void Qnh_with_both_sides_already_in_qnh_says_so() =>
        Assert.Equal(A300Baro.AlreadyQnh, A300Baro.Qnh(0, 0, 16212, 16212).Refusal);

    [Theory]
    [InlineData(16212.0, 1013.25)]
    [InlineData(0.0, null)]
    [InlineData(20000.0, null)]
    [InlineData(null, null)]
    public void A_saved_setting_is_millibars_times_sixteen(double? saved, double? millibars) =>
        Assert.Equal(millibars, A300Baro.SavedMillibars(saved));

    [Fact]
    public void Agreeing_altimeters_read_back_as_one_setting() =>
        Assert.Equal("Altimeters standard, 1013 hectopascals, 29.92 inches",
            A300Baro.Confirmation("Altimeters standard", 1013.25, 1013.2, 1013.25));

    [Fact]
    public void Differing_altimeters_read_back_each_in_hectopascals() =>
        Assert.Equal("Altimeters QNH: captain 1025, first officer 1018, standby 1025 hectopascals",
            A300Baro.Confirmation("Altimeters QNH", 1024.94, 1018, 1024.94));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Plans_and_read_backs_are_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal(Standard, A300Baro.Standard(0, 0).Rpn);
            Assert.Equal("1 16399 (>K:2:KOHLSMAN_SET) 3 16399 (>K:2:KOHLSMAN_SET)", A300Baro.Qnh(1, 0, 16399, null).Rpn);
            Assert.Equal("Altimeters standard, 1013 hectopascals, 29.92 inches",
                A300Baro.Confirmation("Altimeters standard", 1013.25, 1013.25, 1013.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
