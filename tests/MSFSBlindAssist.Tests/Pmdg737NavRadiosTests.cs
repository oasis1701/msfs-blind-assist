// What the PMDG 737's NAV Radios dialog (Ctrl+N) says once it has set both radios. A frequency is
// spoken with a decimal point whatever the PC's regional settings: under a comma-decimal culture
// the confirmation said "NAV 1 110,30".

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;

public class Pmdg737NavRadiosTests
{
    private static readonly NavRadioSettings Settings = new(110.30, 45, 113.95, 270);
    private const string Spoken = "NAV 1 110.30, course 45. NAV 2 113.95, course 270.";

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_set_confirmation_speaks_a_decimal_point_in_any_culture(string culture) =>
        Assert.Equal(Spoken, TestCulture.Run(culture, () => PMDG737Definition.NavRadiosSetMessage(Settings)));

    [Fact]
    public void A_whole_frequency_keeps_its_two_decimals() =>
        Assert.Equal("NAV 1 108.00, course 0. NAV 2 117.95, course 359.",
            TestCulture.Run("de-DE", () => PMDG737Definition.NavRadiosSetMessage(new NavRadioSettings(108.0, 0, 117.95, 359))));
}
