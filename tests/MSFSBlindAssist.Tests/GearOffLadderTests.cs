using Xunit;
using MSFSBlindAssist.FirstOfficer.PMDG737;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The PMDG 737 gear-lever-OFF item used to be acknowledge-only: 21 probing shapes all
/// left MAIN_GearLever unchanged, including a fire-and-forget SetSwitch dispatch of
/// EVT_GEAR_LEVER that reported success while the lever never moved — a safety defect,
/// since the checklist ticked for a lever still at UP. Live ground testing (2026-08-26)
/// found transmit mouse-clicks on EVT_GEAR_LEVER audibly reach the aircraft (the owner
/// hears the click) while the same click sent over the ROTOR_BRAKE encoded channel is
/// silent — so the transmit path reaches the lever; whether it can pull it out of its
/// detent is unsettled. GearOffLadder is a real, closed-loop, VERIFIED attempt: it ticks
/// only when MAIN_GearLever is confirmed at OFF, and reports failure honestly otherwise,
/// so it is safe to ship under either outcome and an ordinary flight's log line tells us
/// which transport (if any) actually works.
/// </summary>
public class GearOffLadderTests
{
    [Fact]
    public void Attempts_TriesTransmitClickFirst()
    {
        Assert.Equal(GearOffTransport.TransmitClick, GearOffLadder.Attempts[0]);
    }

    [Fact]
    public void Attempts_TriesTheHeldUnlockClickSecond()
    {
        Assert.Equal(GearOffTransport.TransmitUnlockHeldClick, GearOffLadder.Attempts[1]);
    }

    // Owner reports this one is silent for the gear lever (unlike the speedbrake, where
    // it worked) — last because it costs nothing to still try, not because it's likely.
    [Fact]
    public void Attempts_TriesRotorBrakeLast()
    {
        Assert.Equal(GearOffTransport.RotorBrakeClick, GearOffLadder.Attempts[^1]);
    }

    [Fact]
    public void Attempts_HasExactlyThreeRungs()
    {
        Assert.Equal(3, GearOffLadder.Attempts.Count);
    }

    [Fact]
    public void ShouldContinue_StopsAsSoonAsTheLeverReachesOff()
    {
        Assert.False(GearOffLadder.ShouldContinue(attemptIndex: 0, reachedOff: true));
    }

    [Fact]
    public void ShouldContinue_ContinuesWhenNotYetOffAndRungsRemain()
    {
        Assert.True(GearOffLadder.ShouldContinue(attemptIndex: 0, reachedOff: false));
    }

    [Fact]
    public void ShouldContinue_StopsAfterTheLastAttempt()
    {
        int last = GearOffLadder.Attempts.Count - 1;
        Assert.False(GearOffLadder.ShouldContinue(attemptIndex: last, reachedOff: false));
    }

    // Read by the flow step's VerifyFieldName and the checklist item's StateFieldName —
    // a typo here is a silently non-detecting item, so pin it against the real
    // PMDGNG3DataStruct field name.
    [Fact]
    public void StateField_MatchesThePmdgNg3Struct()
    {
        Assert.Equal("MAIN_GearLever", GearOffLadder.StateField);
    }

    // The pseudo-key is intercepted before the dispatch table is consulted (same
    // mechanism as SPEEDBRAKE_ARM / FIRE_TEST / GPWS_TEST), so it must never collide
    // with a real PMDG event name.
    [Fact]
    public void PseudoKey_IsNotARealPmdgEvent()
    {
        Assert.Equal("GEAR_LEVER_OFF", GearOffLadder.PseudoKey);
        Assert.False(MSFSBlindAssist.Aircraft.PMDG737Definition.EventIds
            .ContainsKey(GearOffLadder.PseudoKey));
    }
}
