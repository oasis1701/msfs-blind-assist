using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE KNOB PUSH SAID NOTHING A PILOT COULD USE, so "which COM am I about to tune" had
/// no answer.
///
/// The push moves the TUNING box between radio 1 and radio 2 - it is how a pilot CHOOSES
/// which radio the next turn will retune. It was routed as an ordinary bezel key, so what
/// came back was the PFD page summary, which does not change when the box moves. Reported
/// from the cockpit as Alt+Enter announcing nothing, with the pilot then pressing the
/// cursor key to find out whether the window was alive at all.
///
/// ⚠️ This does NOT contradict FirstChangedRadioField's rule that a cursor move must never
/// be dressed as a frequency. That rule stands and is still pinned. A cursor move is named
/// as ITSELF here - the radio, that it now holds the knob, and its standby - which is a
/// different sentence from "COM 2 standby 118.205" said as though the pilot had tuned it.
/// </summary>
public class CowsDA40TuningCursorTests
{
    [Fact]
    public void ThePushNamesTheRadioItLandedOnAndItsStandby()
    {
        string before = "COM 1, active 127.850, standby 121.710, TUNING | COM 2, active 118.000, standby 118.205";
        string after  = "COM 1, active 127.850, standby 121.710 | COM 2, active 118.000, standby 118.205, TUNING";

        Assert.Equal("COM 2 tuning, standby 118.205",
            CowsDA40DisplayForm.TuningCursorMove(before, after));
    }

    [Fact]
    public void ItReportsTheRowThatGAINEDTheBoxNeverTheOneThatLostIt()
    {
        // ⚠️ A push changes TWO rows and only one of them is where the knob now is. Reading
        // the first changed row would name the radio the pilot has just left.
        string before = "NAV 1, active 116.70, standby 110.50, TUNING | NAV 2, active 110.50, standby 113.90";
        string after  = "NAV 1, active 116.70, standby 110.50 | NAV 2, active 110.50, standby 113.90, TUNING";

        string said = CowsDA40DisplayForm.TuningCursorMove(before, after);
        Assert.StartsWith("NAV 2", said);
        Assert.DoesNotContain("NAV 1", said);
    }

    [Fact]
    public void ItNamesAFailedRadioTheBoxLandsOn()
    {
        // The ruling that a failed radio is SCANNED for, not announced, is about unprompted
        // speech. This is the answer to a key the pilot just pressed, at the moment they
        // have selected that radio - which is what a sighted pilot gets from the red X.
        // Silence here is what cost a session: the box parked on a failed COM 2, the knob
        // moved nothing, and the radios were reported broken.
        string before = "COM 1, active 127.850, standby 121.710, TUNING | COM 2, active 118.000, standby 118.205, FAILED";
        string after  = "COM 1, active 127.850, standby 121.710 | COM 2, active 118.000, standby 118.205, TUNING, FAILED";

        Assert.Equal("COM 2 tuning, standby 118.205, failed",
            CowsDA40DisplayForm.TuningCursorMove(before, after));
    }

    [Fact]
    public void APushThatMovedNothingIsNotDressedAsAMove()
    {
        // The caller falls back to the key's own name, so the key never sounds dead - but
        // this must not invent a landing that did not happen.
        string same = "COM 1, active 127.850, standby 121.710, TUNING | COM 2, active 118.000, standby 118.205";
        Assert.Equal("", CowsDA40DisplayForm.TuningCursorMove(same, same));
    }

    [Fact]
    public void AnEmptyReadIsSilent()
    {
        // The socket can answer with nothing while the display is between frames.
        Assert.Equal("", CowsDA40DisplayForm.TuningCursorMove("COM 1, standby 121.710, TUNING", ""));
    }

    [Fact]
    public void TRANSMITIsNotMistakenForTUNING()
    {
        // ⚠️ Both markers ride the same row and only one of them says where the KNOB is.
        // A substring test on the row would read TRANSMIT moving as the tuning box moving.
        string before = "COM 1, active 127.850, standby 121.710, TUNING, TRANSMIT | COM 2, active 118.000, standby 118.205";
        string after  = "COM 1, active 127.850, standby 121.710, TUNING | COM 2, active 118.000, standby 118.205, TRANSMIT";

        Assert.Equal("", CowsDA40DisplayForm.TuningCursorMove(before, after));
    }

    [Fact]
    public void AFrequencyChangeAloneIsNotATuningCursorMove()
    {
        // Turning the knob must stay FirstChangedRadioField's business - if this fired too,
        // one turn would announce twice.
        string before = "COM 1, active 127.850, standby 121.710, TUNING";
        string after  = "COM 1, active 127.850, standby 121.735, TUNING";
        Assert.Equal("", CowsDA40DisplayForm.TuningCursorMove(before, after));
    }
}
