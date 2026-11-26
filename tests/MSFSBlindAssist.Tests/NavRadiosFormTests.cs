// What the shared NAV Radios dialog (the PMDG 737's and the iFly 737's Ctrl+N) says when it refuses
// a frequency. The band's limits are spoken with a decimal point whatever the PC's regional
// settings: under a comma-decimal culture they were "108,00 and 117,95".

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Forms;

public class NavRadiosFormTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_frequency_that_is_no_number_is_refused_with_the_band_in_decimal_points(string culture) =>
        Assert.Equal("NAV 1 frequency must be a number between 108.00 and 117.95 megahertz",
            TestCulture.Run(culture, () => NavRadiosForm.FrequencyNotANumberMessage("NAV 1 frequency")));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_frequency_outside_the_band_is_refused_with_the_band_in_decimal_points(string culture) =>
        Assert.Equal("NAV 2 frequency must be between 108.00 and 117.95 megahertz",
            TestCulture.Run(culture, () => NavRadiosForm.FrequencyOutOfRangeMessage("NAV 2 frequency")));
}
