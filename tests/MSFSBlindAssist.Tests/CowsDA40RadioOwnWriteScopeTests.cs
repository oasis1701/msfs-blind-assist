using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ The "we wrote it ourselves, do not echo it" grace must cover RADIO STANDBYS ONLY.
///
/// It was written as key.EndsWith("_SET"), which is true of the standby frequencies and
/// ALSO of every autopilot selected value - DA40_AP_ALT_SET, _VS_SET, _IAS_SET, _HDG_SET,
/// _CRS_SET. Typing a COM standby or pressing swap therefore silenced any autopilot
/// preselect that moved within the next 2.5 seconds: a heading bug turned on real hardware,
/// or an altitude the G1000 changed on its own, lost for a reason unrelated to either.
///
/// The suppression is only ever justified for a value MSFSBA echoed back as it was typed.
/// </summary>
public class CowsDA40RadioOwnWriteScopeTests
{
    [Theory]
    [InlineData("DA40_RADIO_COM1_SET")]
    [InlineData("DA40_RADIO_COM2_SET")]
    [InlineData("DA40_RADIO_NAV1_SET")]
    [InlineData("DA40_RADIO_NAV2_SET")]
    public void AStandbyFrequencyIsOursToSwallow(string key)
        => Assert.True(CowsDA40Definition.IsRadioStandbyKey(key));

    [Theory]
    [InlineData("DA40_AP_ALT_SET")]
    [InlineData("DA40_AP_VS_SET")]
    [InlineData("DA40_AP_IAS_SET")]
    [InlineData("DA40_AP_HDG_SET")]
    [InlineData("DA40_AP_CRS_SET")]
    public void AnAutopilotPreselectIsNot(string key)
        => Assert.False(CowsDA40Definition.IsRadioStandbyKey(key));

    [Theory]
    [InlineData("DA40_RADIO_COM1_ACTIVE")]
    [InlineData("DA40_RADIO_NAV1_ACTIVE")]
    public void AnActiveFrequencyIsNotEitherBecauseASwapMustStillSpeak(string key)
        => Assert.False(CowsDA40Definition.IsRadioStandbyKey(key));

    [Fact]
    public void EveryLabelledKeyIsClassifiedTheWayItsNameImplies()
    {
        // Guards the whole table at once, so a key added later cannot quietly land on the
        // wrong side of the grace.
        foreach (string key in CowsDA40Definition.RadioAnnouncedKeys)
        {
            bool expected = key.StartsWith("DA40_RADIO_") && key.EndsWith("_SET");
            Assert.Equal(expected, CowsDA40Definition.IsRadioStandbyKey(key));
        }
    }
}
