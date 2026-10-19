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

    // ⚠️ A MODE THAT SYNCS ITS TARGET MAKES THE TARGET SOMEBODY ELSE'S NEWS. VCBI climb,
    // 2026-10-15: "Selected vertical speed 500 feet per minute up", then VS pressed - the
    // GFC 700 synced the reference to the CURRENT vertical speed, 0, and the aeroplane sat
    // level while the pilot had heard 500. The 0 settled inside the 500's own-write grace
    // and was dropped as MSFSBA's own echo.
    [Theory]
    [InlineData("DA40_AP_VS", true, "DA40_AP_VS_SET")]
    [InlineData("DA40_AP_FLC", true, "DA40_AP_IAS_SET")]
    [InlineData("DA40_AP_VS", false, null)]
    [InlineData("DA40_AP_FLC", false, null)]
    [InlineData("DA40_AP_ALT", true, null)]
    [InlineData("DA40_AP_HDG", true, null)]
    public void OnlyVsAndFlcSyncTheirTargetAsTheyEngage(string modeKey, bool on, string? synced)
        => Assert.Equal(synced, CowsDA40Definition.ApTargetSyncedBy(modeKey, on));

    [Fact]
    public void PressingVsMakesTheVerticalSpeedTargetNoLongerOurs()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        var now = System.DateTime.UtcNow;
        def.MarkRadioSetByUs("DA40_AP_VS_SET", "DA40_AP_ALT_SET");
        Assert.True(def.WrittenByUs("DA40_AP_VS_SET", now));

        def.NoteOwnApModePress("DA40_AP_VS", on: true);

        Assert.False(def.WrittenByUs("DA40_AP_VS_SET", now));
        // Only the synced target: the altitude typed beside it is still ours.
        Assert.True(def.WrittenByUs("DA40_AP_ALT_SET", now));
    }

    [Fact]
    public void VsEngagedOnHardwareDoesTheSameButOnlyOnItsEdge()
    {
        // The mode rows are per-frame, so the row turning on arrives before the target's
        // 300 ms settle flushes - the same sync, from a VS key pressed on the bezel.
        var def = new CowsDA40Definition(DA40Variant.XLS);
        def.NoteApModeRow("DA40_AP_VS", 0);
        def.MarkRadioSetByUs("DA40_AP_VS_SET");
        def.NoteApModeRow("DA40_AP_VS", 1);
        Assert.False(def.WrittenByUs("DA40_AP_VS_SET", System.DateTime.UtcNow));

        // Already in VS, the row arriving again is no sync: a rate typed now is our echo,
        // and saying it twice is the double announcement the grace exists to stop.
        def.MarkRadioSetByUs("DA40_AP_VS_SET");
        def.NoteApModeRow("DA40_AP_VS", 1);
        Assert.True(def.WrittenByUs("DA40_AP_VS_SET", System.DateTime.UtcNow));
    }

    // ⚠️ WHAT THE PILOT LAST HEARD IS WHAT A SETTER SAID. MTL climb, 2026-10-19: FLC pressed,
    // 76 typed ("Selected airspeed 76 knots"), and the GFC 700 synced the reference back to the
    // current 70. The 70 matched the last SETTLED value, so it was dropped as noise, and the
    // queued echo 76 was spoken instead - "Selected airspeed 76" over a target of 70.
    [Fact]
    public void AFlcSyncBackToTheOldSpeedIsSpokenAsTheSpeedItReallyIs()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        def.StageRadioValue("DA40_AP_IAS_SET", 70);            // first reading: the baseline
        def.NoteApModeRow("DA40_AP_FLC", 0);

        def.NoteOwnApModePress("DA40_AP_FLC", on: true);
        def.MarkApTargetSpoken("DA40_AP_IAS_SET", 76);         // the setter said 76
        def.StageRadioValue("DA40_AP_IAS_SET", 76);            // its echo: nothing new
        Assert.False(def.PendingRadioValue("DA40_AP_IAS_SET").HasValue);

        def.NoteApModeRow("DA40_AP_FLC", 1);                   // FLC engages, syncing to 70
        def.StageRadioValue("DA40_AP_IAS_SET", 70);
        Assert.Equal(70, def.PendingRadioValue("DA40_AP_IAS_SET"));
        Assert.False(def.WrittenByUs("DA40_AP_IAS_SET", System.DateTime.UtcNow));
    }

    [Fact]
    public void AValueBackWhereThePilotLastHeardItHasNothingQueued()
    {
        var def = new CowsDA40Definition(DA40Variant.XLS);
        def.StageRadioValue("DA40_AP_HDG_SET", 110);
        def.StageRadioValue("DA40_AP_HDG_SET", 115);
        Assert.Equal(115, def.PendingRadioValue("DA40_AP_HDG_SET"));
        def.StageRadioValue("DA40_AP_HDG_SET", 110);
        Assert.False(def.PendingRadioValue("DA40_AP_HDG_SET").HasValue);
    }
}
