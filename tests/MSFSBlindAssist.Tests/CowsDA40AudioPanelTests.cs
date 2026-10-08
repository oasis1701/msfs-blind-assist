using System.Globalization;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The GMA 1347 keys and the VOL settings on the Audio panel (measured live on the NG,
/// 2026-10-06): every key has a push node in the model, a placard name, an input event
/// that takes a value, and a stock event that moves the same simvar.
/// </summary>
public class CowsDA40AudioPanelTests
{
    [Theory]
    [InlineData(DA40Variant.NG)]
    [InlineData(DA40Variant.XLS)]
    public void EveryKeyIsAnAnnouncedSwitchOnTheAudioPanel(DA40Variant variant)
    {
        var def = new CowsDA40Definition(variant);
        var vars = def.GetVariables();
        var audio = def.GetPanelControls()["Audio"];

        foreach (var key in CowsDA40Definition.GmaKeys)
        {
            Assert.Contains(key.VarKey, audio);
            var v = vars[key.VarKey];
            Assert.Equal(key.Placard, v.DisplayName);
            Assert.True(v.IsAnnounced, key.VarKey);
            Assert.Equal(new[] { "Off", "On" }, v.ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
            Assert.StartsWith("AS1000_MID_", key.InputEvent);
        }
    }

    [Theory]
    [InlineData("DA40_AUDIO_COM1_VOL_SET", 62.4, "62 (>K:COM1_VOLUME_SET)")]
    [InlineData("DA40_AUDIO_COM2_VOL_SET", 100, "100 (>K:COM2_VOLUME_SET)")]
    [InlineData("DA40_AUDIO_NAV1_VOL_SET", -5, "0 (>K:NAV1_VOLUME_SET_EX1)")]
    [InlineData("DA40_AUDIO_NAV2_VOL_SET", 140, "100 (>K:NAV2_VOLUME_SET_EX1)")]
    public void AVolumeIsAWholePercentage(string key, double value, string rpn)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(rpn, CowsDA40Definition.VolumeWrite(key, value));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Fact]
    public void AToggleFallbackOnlyFiresWhenTheKeyIsNotAlreadyThere()
    {
        var nav1 = CowsDA40Definition.GmaKeys.Single(k => k.VarKey == "DA40_AUDIO_NAV1_IDENT");
        Assert.Equal("(A:NAV SOUND:1, Bool) 1 != if{ 1 (>K:RADIO_VOR1_IDENT_TOGGLE) }",
            CowsDA40Definition.GmaFallbackWrite(nav1, 1));

        var mkr = CowsDA40Definition.GmaKeys.Single(k => k.VarKey == "DA40_AUDIO_MKR_MUTE");
        Assert.Equal("0 (>K:MARKER_BEACON_TEST_MUTE)", CowsDA40Definition.GmaFallbackWrite(mkr, 0));
    }

    [Theory]
    // MSFSBA set COM2; COM RECEIVE ALL moved with it (measured) - not news.
    [InlineData("DA40_AUDIO_MONITOR_BOTH", "DA40_AUDIO_COM2_RECEIVE", 900, true)]
    [InlineData("DA40_AUDIO_COM2_RECEIVE", "DA40_AUDIO_MONITOR_BOTH", 1800, true)]
    // The control MSFSBA set is MainForm's own echo, not this one's.
    [InlineData("DA40_AUDIO_COM2_RECEIVE", "DA40_AUDIO_COM2_RECEIVE", 500, false)]
    // After the grace, or with no write of ours, a change is somebody else's and speaks.
    [InlineData("DA40_AUDIO_MONITOR_BOTH", "DA40_AUDIO_COM2_RECEIVE", 3000, false)]
    [InlineData("DA40_AUDIO_MONITOR_BOTH", "", 100, false)]
    // Only the four COM audio controls are coupled.
    [InlineData("DA40_AUDIO_NAV1_IDENT", "DA40_AUDIO_COM2_RECEIVE", 100, false)]
    public void AKnockOnChangeFromOurOwnWriteIsSilent(string changed, string ownKey, int msSince, bool silent)
        => Assert.Equal(silent,
            CowsDA40Definition.IsComAudioSideEffect(changed, ownKey, TimeSpan.FromMilliseconds(msSince)));

    [Fact]
    public void AKnockOnChangeStillMovesItsCombo()
    {
        var vars = new CowsDA40Definition(DA40Variant.NG).GetVariables();
        foreach (var key in CowsDA40Definition.ComAudioKeys)
            Assert.True(vars[key].RefreshesControlWhenDefHandled(1) && vars[key].RefreshesControlWhenDefHandled(0), key);
    }

    [Fact]
    public void OnlyPageSnapshotAircraftPauseTheStatusRefreshForACombo()
    {
        // SIM-19: the DA40 keeps its list live; an aircraft whose status box is a page
        // snapshot behind a page combo keeps the full pause.
        Assert.False(new CowsDA40Definition(DA40Variant.NG).StatusRefreshPausesWhileComboFocused);
        Assert.True(new MSFSBlindAssist.Aircraft.FlyByWireA380Definition().StatusRefreshPausesWhileComboFocused);
        Assert.True(new MSFSBlindAssist.Aircraft.FlyByWireA320Definition().StatusRefreshPausesWhileComboFocused);
    }

    [Theory]
    [InlineData("DA40_AUDIO_COM1_RECEIVE", 1)]
    [InlineData("DA40_AUDIO_COM2_RECEIVE", 2)]
    [InlineData("DA40_AUDIO_NAV1_IDENT", 0)]
    public void OnlyTheComReceiveKeysNameARadio(string key, int radio)
        // The transmitting radio is always heard; only these two can be refused for it.
        => Assert.Equal(radio, CowsDA40Definition.ReceiveKeyRadio(key));
}
