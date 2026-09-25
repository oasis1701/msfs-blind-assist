using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ These tests are written against a real accident. A DA40 rolled into a 65-degree bank at
/// 3,200 ft and flew into the ground in under a minute with hand fly ACTIVE and its bank tone
/// sounding, while the pilot pressed a readout key twelve times in thirteen seconds trying to
/// work out what was wrong. Nothing ever said "bank".
///
/// The case that matters is therefore not "does it report a bank" but "does it report one
/// NOBODY ASKED ABOUT, once, in words, without then nagging".
/// </summary>
public class UnusualAttitudeMonitorTests
{
    private static UnusualAttitudeMonitor.State Fresh => default;

    [Fact]
    public void TheAccidentBankIsAnnouncedWithItsDirection()
    {
        // ⚠️ BANK IS LEFT-POSITIVE out of SimConnect, so the 65-degree RIGHT bank that killed
        // the aeroplane arrives as NEGATIVE 65. Getting this backwards would send a pilot's
        // stick the wrong way, which is worse than saying nothing at all.
        var v = UnusualAttitudeMonitor.Evaluate(-65, pitchDeg: -3, onGround: false, Fresh);

        Assert.Equal("Bank right 65 degrees.", v.Message);
        Assert.True(v.Next.BankAlerted);
    }

    [Fact]
    public void ALeftBankIsNamedLeft()
    {
        var v = UnusualAttitudeMonitor.Evaluate(50, pitchDeg: 0, onGround: false, Fresh);
        Assert.Equal("Bank left 50 degrees.", v.Message);
    }

    [Fact]
    public void ANormalManoeuvreIsSilent()
    {
        // A rate-one turn is 15-25 degrees and a deliberate steep turn is 45. An app that
        // announces every turn is an app the pilot switches off.
        Assert.Equal("", UnusualAttitudeMonitor.Evaluate(25, 0, false, Fresh).Message);
        Assert.Equal("", UnusualAttitudeMonitor.Evaluate(-30, 0, false, Fresh).Message);
    }

    [Fact]
    public void ItSaysItOnceAndThenStopsTalking()
    {
        var v1 = UnusualAttitudeMonitor.Evaluate(-50, 0, false, Fresh);
        Assert.NotEqual("", v1.Message);

        // Still banked, not materially worse: silence. The pilot is busy flying.
        var v2 = UnusualAttitudeMonitor.Evaluate(-52, 0, false, v1.Next);
        Assert.Equal("", v2.Message);
    }

    [Fact]
    public void ItSpeaksAgainWhenItGetsMateriallyWorse()
    {
        var v1 = UnusualAttitudeMonitor.Evaluate(-50, 0, false, Fresh);
        var v2 = UnusualAttitudeMonitor.Evaluate(-75, 0, false, v1.Next);
        Assert.Equal("Bank right 75 degrees.", v2.Message);
    }

    [Fact]
    public void RecoveryIsAnnounced()
    {
        // ⚠️ A deliberate exception to "a cleared fault stays silent". That rule fits a lamp,
        // where the pilot flicked the switch and knows. "Am I level yet?" is the whole question
        // a pilot rolling out of a bank they never noticed is asking.
        var banked = UnusualAttitudeMonitor.Evaluate(-60, 0, false, Fresh);
        var rolled = UnusualAttitudeMonitor.Evaluate(-10, 0, false, banked.Next);

        Assert.Equal("Wings level.", rolled.Message);
        Assert.False(rolled.Next.BankAlerted);
    }

    [Fact]
    public void RecoveryUsesHysteresisSoItCannotChatter()
    {
        var banked = UnusualAttitudeMonitor.Evaluate(-60, 0, false, Fresh);

        // Between the clear threshold and the alert threshold: still recovering, say nothing.
        var midway = UnusualAttitudeMonitor.Evaluate(-30, 0, false, banked.Next);
        Assert.Equal("", midway.Message);
        Assert.True(midway.Next.BankAlerted);
    }

    [Fact]
    public void OnTheGroundNothingIsAnUnusualAttitude()
    {
        // A parked aeroplane on a slope, or one banked on its gear, is scenery - not an
        // attitude. Announcing it would make every start-up shout.
        Assert.Equal("", UnusualAttitudeMonitor.Evaluate(-65, -20, onGround: true, Fresh).Message);
    }

    [Fact]
    public void BankIsReportedBeforePitchInASpiral()
    {
        // The accident attitude had BOTH out: 65 degrees of bank and a dropping nose. Bank is
        // the one that must be said, because rolling level is what stops a spiral - pulling on
        // a steeply banked aeroplane only tightens it.
        var v = UnusualAttitudeMonitor.Evaluate(-65, pitchDeg: -25, onGround: false, Fresh);
        Assert.StartsWith("Bank right", v.Message);
    }

    [Fact]
    public void PitchAloneIsStillReported()
    {
        var v = UnusualAttitudeMonitor.Evaluate(0, pitchDeg: -25, onGround: false, Fresh);
        Assert.Equal("Pitch down 25 degrees.", v.Message);

        var back = UnusualAttitudeMonitor.Evaluate(0, pitchDeg: 2, onGround: false, v.Next);
        Assert.Equal("Pitch normal.", back.Message);
    }

    [Fact]
    public void APitchUpIsNamedUp()
    {
        var v = UnusualAttitudeMonitor.Evaluate(0, pitchDeg: 25, onGround: false, Fresh);
        Assert.Equal("Pitch up 25 degrees.", v.Message);
    }
}
