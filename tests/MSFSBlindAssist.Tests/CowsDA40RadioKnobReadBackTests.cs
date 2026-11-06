using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ Turning a radio knob read the frequency from BEFORE the keystroke, and then said it
/// twice.
///
/// The handler fired the event and scraped in the same breath, so the display had not had a
/// frame: the row looked unchanged, nothing was announced, and the pilot's NEXT press read
/// back the PREVIOUS one's frequency. Reported as "it was at 710, I pressed twice, I got 715"
/// - 715 was the first press, and the radio was already on 725 by the time it was spoken.
///
/// Then, once the read-back worked, the 1 Hz settle announcer repeated it a second later:
///   "COM 1, active 127.850, standby 121.725"   (the window)
///   "COM 1 standby 121.725"                    (the settle, 1.2 s on)
///
/// ⚠️ Note what is NOT a bug in that report: 710 to 715 to 725 to 730 is CORRECT 8.33 kHz
/// stepping. The channel names are not evenly spaced - 720, 745, 770 and 795 are not
/// channels at all - so the radio steps over them and the gap is real.
/// </summary>
public class CowsDA40RadioKnobReadBackTests
{
    [Fact]
    public void OnlyTheFieldThatMovedIsSpoken()
    {
        // ⚠️ THE WHOLE POINT. The row form recited the ACTIVE frequency - which the pilot did
        // not touch - on every step of the knob, with the one that moved at the end of it.
        string before = "COM 1, active 127.850, standby 121.710";
        string after = "COM 1, active 127.850, standby 121.735";

        var c = CowsDA40DisplayForm.FirstChangedRadioField(before, after);
        Assert.Equal("COM 1 standby 121.735", c.Spoken);
        Assert.DoesNotContain("127.850", c.Spoken);
    }

    [Fact]
    public void ItNamesTheKeySoTheSettleAnnouncerCanStaySilent()
    {
        // Without this the frequency is spoken twice - once here off the Coherent socket,
        // once more when the 1 Hz batch delivers the same change to the settle announcer.
        var c = CowsDA40DisplayForm.FirstChangedRadioField(
            "COM 1, active 127.850, standby 121.710",
            "COM 1, active 127.850, standby 121.735");
        Assert.Equal("DA40_RADIO_COM1_SET", c.VarKey);
    }

    [Fact]
    public void AnActiveFrequencyMovingNamesTheActiveKey()
    {
        // A swap moves the ACTIVE frequency, and that is the half a pilot most needs.
        var c = CowsDA40DisplayForm.FirstChangedRadioField(
            "NAV 2, active 110.50, standby 113.90",
            "NAV 2, active 113.90, standby 110.50");
        Assert.Contains("active 113.90", c.Spoken);
        Assert.Equal("DA40_RADIO_NAV2_ACTIVE", c.VarKey);
    }

    [Fact]
    public void ASecondRadioMovingIsFoundEvenWhenTheFirstDidNot()
    {
        string before = "NAV 1, active 116.70, standby 110.50 | NAV 2, active 110.50, standby 113.90";
        string after = "NAV 1, active 116.70, standby 110.50 | NAV 2, active 110.50, standby 113.95";

        var c = CowsDA40DisplayForm.FirstChangedRadioField(before, after);
        Assert.Equal("NAV 2 standby 113.95", c.Spoken);
    }

    [Fact]
    public void TheTuningCursorMovingIsNotReportedAsAFrequency()
    {
        // The knob PUSH shifts TUNING between radios. That is a real change, but it is not a
        // frequency and must never be dressed as one.
        var c = CowsDA40DisplayForm.FirstChangedRadioField(
            "COM 1, active 127.850, standby 121.710, TUNING",
            "COM 1, active 127.850, standby 121.710");
        Assert.False(c.Found);
    }

    [Fact]
    public void NothingChangedStaysSilentRatherThanRepeatingTheOldValue()
    {
        // ⚠️ THE SPECIFIC LIE THIS EXISTS TO STOP TELLING. Re-speaking the unchanged value is
        // what made a press that had not landed yet sound like a press that had.
        string same = "COM 1, active 127.850, standby 121.710";
        Assert.False(CowsDA40DisplayForm.FirstChangedRadioField(same, same).Found);
    }

    [Fact]
    public void AnEmptyReadIsSilentToo()
    {
        // The socket can answer with nothing while the display is between frames; that is not
        // a frequency and must never be announced as one.
        Assert.False(CowsDA40DisplayForm.FirstChangedRadioField("COM 1, standby 121.710", "").Found);
    }

    [Theory]
    // The 8.33 channel names that DO exist, so a future "fix" cannot decide the gaps are bugs.
    [InlineData("121.705")]
    [InlineData("121.710")]
    [InlineData("121.715")]
    [InlineData("121.725")]
    [InlineData("121.730")]
    public void TheEightThirtyThreeChannelsInThatSequenceAreAllReportedVerbatim(string freq)
    {
        var c = CowsDA40DisplayForm.FirstChangedRadioField(
            "COM 1, active 127.850, standby 118.000",
            $"COM 1, active 127.850, standby {freq}");
        Assert.Contains(freq, c.Spoken);
    }
}
