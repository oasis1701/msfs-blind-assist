using System.Linq;
using Xunit;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 speedbrake arm escalates across transports because the NG3 has a
/// documented family of CDA-deaf controls that only respond to TransmitClientEvent
/// mouse-clicks, and it could not be settled from the repo which family the speedbrake
/// detents belong to. The ORDER matters (cheapest/most-likely first) and the DO NOT ARM
/// early exit matters (an auto-speedbrake fault cannot be clicked away).
/// </summary>
public class SpeedbrakeArmLadderTests
{
    [Fact]
    public void Attempts_EscalateFromCdaToTransmitToPressRelease()
    {
        Assert.Equal(
            new[]
            {
                SpeedbrakeArmTransport.CdaClick,
                SpeedbrakeArmTransport.TransmitClick,
                SpeedbrakeArmTransport.TransmitPressRelease,
            },
            SpeedbrakeArmLadder.Attempts.ToArray());
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
    public void ShouldContinue_KeepsGoingWhileAttemptsRemain()
    {
        Assert.True(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 0, armed: false, doNotArmLit: false));
        Assert.True(SpeedbrakeArmLadder.ShouldContinue(
            attemptIndex: 1, armed: false, doNotArmLit: false));
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
