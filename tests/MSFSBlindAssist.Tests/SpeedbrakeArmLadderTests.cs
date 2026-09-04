using Xunit;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 speedbrake arm used to escalate across transports because the NG3 has a
/// documented family of CDA-deaf controls that only respond to TransmitClientEvent
/// mouse-clicks, and it could not be settled from the repo which family the speedbrake
/// detents belong to. Live probing (2026-08-25) settled it: a single CDA click arms the
/// lever first try. The ladder structure — the loop, the read-back, ShouldContinue, and
/// the DO NOT ARM / already-armed early exits — stays, because a faulted auto-speedbrake
/// system or an already-deployed lever are real conditions no transport can click through.
/// </summary>
public class SpeedbrakeArmLadderTests
{
    // Live-verified against a PMDG 737-800 in flight (2026-08-25): a single
    // CDA + MOUSE_FLAG_LEFTSINGLE on EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM armed the
    // lever on the first attempt (MAIN_annunSPEEDBRAKE_ARMED false -> true, audible to
    // the pilot). The escalation existed only because we could not tell which transport
    // worked; rungs 2 and 3 now only ever spend the pilot's time on an aircraft where
    // rung 1 failed for a real reason, which DoNotArmField already catches.
    [Fact]
    public void TheLadderIsASingleProvenRung()
    {
        Assert.Equal(new[] { SpeedbrakeArmTransport.CdaClick }, SpeedbrakeArmLadder.Attempts);
    }

    [Fact]
    public void ShouldContinue_StopsAsSoonAsTheLeverIsArmed()
    {
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: true, doNotArmLit: false));
    }

    // An auto-speedbrake fault cannot be cleared by more clicks, and the DO NOT ARM
    // annunciator is already announced separately, so the pilot hears the real reason.
    [Fact]
    public void ShouldContinue_StopsWhenDoNotArmIsLit()
    {
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: false, doNotArmLit: true));
    }

    [Fact]
    public void ShouldContinue_StopsAfterTheLastAttempt()
    {
        int last = SpeedbrakeArmLadder.Attempts.Count - 1;
        Assert.False(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: last, armed: false, doNotArmLit: false));
    }

    // These names are read by the flow step's VerifyFieldName and both checklist items;
    // a typo here is a silently non-detecting item, so pin them.
    [Fact]
    public void FieldNames_MatchThePmdgNg3Struct()
    {
        Assert.Equal("MAIN_annunSPEEDBRAKE_ARMED", SpeedbrakeArmLadder.ArmedField);
        Assert.Equal("MAIN_annunSPEEDBRAKE_DO_NOT_ARM", SpeedbrakeArmLadder.DoNotArmField);
        Assert.Equal("MAIN_annunSPEEDBRAKE_EXTENDED", SpeedbrakeArmLadder.ExtendedField);
        Assert.Equal("SPEEDBRAKE_ARM", SpeedbrakeArmLadder.PseudoKey);
    }
}
