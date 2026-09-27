using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's take-off speeds are announced as the FMS sets them, the PMDG way and as ONE
/// sentence in V1 / VR / V2 order: baseline-first, silent on an unchanged redelivery and on a
/// cleared speed, spoken again when the speeds come back. Reported 2026-09-08: "the v-speeds
/// aren't announced automatically when they are being set, as is the case with the PMDGs".
/// </summary>
public class Md11VSpeedsTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static bool NotMuted(string key) => false;

    /// <summary>The batch's alphabetical burst (V1, V2, VFR, VR, VSR) is spoken as one settled sentence in speaking order.</summary>
    [Fact]
    public void AComputedSet_IsOneSentence_InSpeakingOrder_AfterTheSettle()
    {
        var a = new Md11VSpeedAnnouncer();
        foreach (var key in Md11VSpeeds.Keys) a.OnUpdate(key, 0, nowMs: 0);   // the FMS has computed nothing yet: baselines

        long t = 10_000;
        Assert.True(a.OnUpdate("MD11_V1", 145, t));
        Assert.True(a.OnUpdate("MD11_V2", 158, t + 1));
        Assert.True(a.OnUpdate("MD11_VFR", 210, t + 2));
        Assert.True(a.OnUpdate("MD11_VR", 150, t + 3));
        Assert.True(a.OnUpdate("MD11_VSR", 190, t + 4));
        Assert.Null(a.Due(t + 100, NotMuted));                                // not settled yet
        Assert.True(a.HasPending);
        Assert.Equal("V1 145, VR 150, V2 158, slat retraction speed 190, flap retraction speed 210 knots",
            a.Due(t + 4 + Md11VSpeedAnnouncer.SettleMs, NotMuted));
        Assert.False(a.HasPending);
        Assert.Null(a.Due(t + 5_000, NotMuted));                              // spoken once
    }

    [Fact]
    public void ASingleChange_IsSpokenAlone_NotWhenFirstSeen()
    {
        var a = new Md11VSpeedAnnouncer();
        Assert.False(a.OnUpdate("MD11_V1", 145, 0));                          // connecting with speeds entered is not a change
        Assert.False(a.OnUpdate("MD11_V1", 145, 1_000));                      // the panel's per-second force-read
        Assert.False(a.OnUpdate("MD11_V1", 145.3, 2_000));                    // jitter inside half a knot
        Assert.True(a.OnUpdate("MD11_V1", 147, 3_000));
        Assert.Equal("V1 147 knots", a.Due(3_000 + Md11VSpeedAnnouncer.SettleMs, NotMuted));
    }

    [Fact]
    public void AClearedSpeed_IsSilent_AndItsReturnIsNot()
    {
        var a = new Md11VSpeedAnnouncer();
        a.OnUpdate("MD11_V2", 158, 0);
        Assert.False(a.OnUpdate("MD11_V2", 0, 1_000));                        // the FMS wiped the perf entry
        Assert.False(a.OnUpdate("MD11_V2", -999, 2_000));                     // TFDi's dashed sentinel, likewise
        Assert.True(a.OnUpdate("MD11_V2", 160, 3_000));
        Assert.Equal("V2 160 knots", a.Due(3_000 + Md11VSpeedAnnouncer.SettleMs, NotMuted));
    }

    /// <summary>A muted speed drops out of the sentence; muting all of them speaks nothing.</summary>
    [Fact]
    public void AMutedSpeed_IsLeftOutOfTheSentence()
    {
        var a = new Md11VSpeedAnnouncer();
        foreach (var key in Md11VSpeeds.Keys) a.OnUpdate(key, 0, 0);
        a.OnUpdate("MD11_V1", 145, 1_000);
        a.OnUpdate("MD11_VR", 150, 1_000);
        a.OnUpdate("MD11_V2", 158, 1_000);
        Assert.Equal("V1 145, V2 158 knots", a.Due(1_000 + Md11VSpeedAnnouncer.SettleMs, key => key == "MD11_VR"));

        a.OnUpdate("MD11_V1", 146, 5_000);
        Assert.Null(a.Due(5_000 + Md11VSpeedAnnouncer.SettleMs, _ => true));
        Assert.False(a.HasPending);
    }

    /// <summary>A speed cleared inside the settle window is dropped from the sentence it had armed; a reconnect drops the whole sentence and keeps the baselines.</summary>
    [Fact]
    public void AClearInsideTheSettle_CancelsThatSpeed_AndDropPending_CancelsTheSentence()
    {
        var a = new Md11VSpeedAnnouncer();
        foreach (var key in Md11VSpeeds.Keys) a.OnUpdate(key, 0, 0);
        a.OnUpdate("MD11_V1", 145, 1_000);
        a.OnUpdate("MD11_VR", 150, 1_000);
        a.OnUpdate("MD11_V1", 0, 1_100);                                      // the FMS took V1 back
        Assert.Equal("VR 150 knots", a.Due(1_100 + Md11VSpeedAnnouncer.SettleMs, NotMuted));

        a.OnUpdate("MD11_V2", 158, 5_000);
        a.DropPending();
        Assert.False(a.HasPending);
        Assert.Null(a.Due(5_000 + Md11VSpeedAnnouncer.SettleMs, NotMuted));
        Assert.False(a.OnUpdate("MD11_V2", 158, 6_000));                      // the baseline survived the drop: unchanged is silent
        Assert.True(a.OnUpdate("MD11_V2", 160, 7_000));
    }

    /// <summary>The disconnect wipe: the reconnect's first delivery of each speed is a baseline again, and nothing pending survives.</summary>
    [Fact]
    public void Reset_MakesEverySpeedABaselineAgain()
    {
        var a = new Md11VSpeedAnnouncer();
        a.OnUpdate("MD11_V1", 145, 0);
        a.OnUpdate("MD11_V1", 147, 1_000);
        a.Reset();
        Assert.False(a.HasPending);
        Assert.False(a.OnUpdate("MD11_V1", 150, 5_000));                      // re-seeds silently
        Assert.True(a.OnUpdate("MD11_V1", 152, 6_000));
        Assert.Equal("V1 152 knots", a.Due(6_000 + Md11VSpeedAnnouncer.SettleMs, NotMuted));
    }

    [Fact]
    public void TheSpeakingOrder_CoversEveryLabelledSpeed_AndNothingElse()
    {
        Assert.Equal(Md11VSpeeds.Labels.Keys.OrderBy(k => k), Md11VSpeeds.Keys.OrderBy(k => k));
    }

    [Fact]
    public void AnUnrelatedExport_IsNotASpeed()
    {
        Assert.False(new Md11VSpeedAnnouncer().OnUpdate("MD11_ENG1_N1", 95, 0));
        Assert.False(Md11VSpeeds.IsKey("MD11_ENG1_N1"));
    }

    /// <summary>Every speed that speaks has a Ctrl+M row: ExcludeFromMonitorManager means "muted by plumbing" and must never sit on a var that speaks.</summary>
    [Fact]
    public void EverySpeedThatSpeaks_HasAMuteRow()
    {
        var vars = Def.GetVariables();
        foreach (var key in Md11VSpeeds.Keys)
        {
            var d = vars[key];
            Assert.Equal(UpdateFrequency.Continuous, d.UpdateFrequency);
            Assert.True(d.IsAnnounced);
            Assert.False(d.ExcludeFromMonitorManager);
            Assert.True(Md11VSpeeds.IsKey(key));
        }
    }

    /// <summary>A flight load re-delivers only what changed, so an empty speed is seeded from the cache and a seeded one is left alone.</summary>
    [Fact]
    public void SeedIfEmpty_SeedsOnlyASpeedWithNoBaseline()
    {
        var a = new Md11VSpeedAnnouncer();
        Assert.True(a.SeedIfEmpty("MD11_V1", 145));
        Assert.False(a.SeedIfEmpty("MD11_V1", 150));                          // already seeded: untouched
        Assert.False(a.SeedIfEmpty("MD11_ENG1_N1", 95));                      // not a speed
        Assert.False(a.OnUpdate("MD11_V1", 145, 1_000));                      // unchanged
        Assert.True(a.OnUpdate("MD11_V1", 150, 2_000));                       // the first real change speaks
    }
}
