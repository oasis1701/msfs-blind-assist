// The Fenix RMP frequencies (kHz L:vars) are spoken as MHz with three decimals. They used to be formatted
// in the PC's culture, so a comma-decimal Windows heard "RMP1 active 132,475". CI runs under en-US, so
// these tests set the culture themselves.

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FenixRmpFrequencyTests
{
    [Theory]
    [InlineData("N_PED_RMP1_ACTIVE", 132475, "RMP1 active 132.475")]
    [InlineData("N_PED_RMP1_STDBY", 136975, "RMP1 standby 136.975")]
    [InlineData("N_PED_RMP2_ACTIVE", 121500, "RMP2 active 121.500")]
    [InlineData("N_PED_RMP2_STDBY", 118000, "RMP2 standby 118.000")]
    [InlineData("N_PED_RMP3_ACTIVE", 123450, "RMP3 active 123.450")]
    [InlineData("N_PED_RMP3_STDBY", 127825, "RMP3 standby 127.825")]
    public void The_frequency_is_spoken_with_a_point_under_any_culture(string varName, double kHz, string expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            string spoken = TestCulture.Run(culture, () => FenixA320Definition.RmpFrequencyAnnouncement(varName, kHz));
            Assert.Equal(expected, spoken);
        }
    }

    // Guards the test itself: under de-DE a plain format of the same number does use a comma, so the
    // theory above would fail if the helper formatted in the current culture.
    [Fact]
    public void The_comma_culture_really_is_in_effect()
    {
        string plain = TestCulture.Run("de-DE", () => (132475 / 1000.0).ToString("F3", CultureInfo.CurrentCulture));

        Assert.Equal("132,475", plain);
    }
}
