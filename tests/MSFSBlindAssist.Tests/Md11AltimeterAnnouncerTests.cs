using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The captain's altimeter speaks once it SETTLES: a knob wind delivers a run of values a second
/// apart, and the pilot wants the final setting, not every hundredth on the way. Baseline-first
/// (connecting mid-flight must not read the altimeter), deduplicated on the sentence.
/// </summary>
public class Md11AltimeterAnnouncerTests
{
    [Fact]
    public void FirstSight_IsSilent()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        Assert.False(a.HasPending);
        Assert.Null(a.Due(5000));
    }

    [Fact]
    public void ARunOfValues_SpeaksOnce_AfterTheSettle()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);                       // baseline
        a.OnUpdate(29.93, 10000);
        a.OnUpdate(29.94, 10400);
        a.OnUpdate(29.95, 10800);
        Assert.Null(a.Due(11000));                  // 200 ms after the last value: not settled
        Assert.True(a.HasPending);
        Assert.Equal("Altimeter: 1014, 29.95", a.Due(10800 + Md11AltimeterAnnouncer.SettleMs));
        Assert.False(a.HasPending);
        Assert.Null(a.Due(20000));                  // nothing left to say
    }

    [Fact]
    public void TheSameSentenceAgain_IsSilent()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        a.OnUpdate(29.95, 1000);
        Assert.Equal("Altimeter: 1014, 29.95", a.Due(3000));
        a.OnUpdate(29.95, 5000);                    // re-delivered, unchanged
        Assert.Null(a.Due(7000));
    }

    /// <summary>
    /// A panel's status-display auto-refresh force-reads this variable every second while the
    /// "Minimums and Altimeters" panel is open, and a forced read re-delivers an UNCHANGED value.
    /// That redelivery must not re-arm the settle, or the setting is never spoken while the panel
    /// is open and is spoken stale when it closes.
    /// </summary>
    [Fact]
    public void ARedeliveryOfTheSameValue_DoesNotReArmTheSettle()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);                       // baseline
        a.OnUpdate(29.95, 1000);                    // the change
        a.OnUpdate(29.95, 2000);                    // a force-read redelivery, unchanged
        Assert.Equal("Altimeter: 1014, 29.95", a.Due(2600));   // 1600 ms after the CHANGE, not 600 after the redelivery
    }

    /// <summary>
    /// The two properties the definition's settle check leans on. It arms a check only when a
    /// value actually armed a settle, so an ignored redelivery with nothing pending must leave
    /// HasPending false (no check is spawned for it); and a check that fires marginally early
    /// finds nothing due but must still see HasPending, which is what tells it to re-arm once
    /// more rather than lose the sentence.
    /// </summary>
    [Fact]
    public void AnIgnoredRedelivery_ArmsNothing_AndAnEarlyDue_LeavesThePendingValueArmed()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);                       // baseline
        a.OnUpdate(29.92, 5000);                    // unchanged: ignored outright, nothing to wait for
        Assert.False(a.HasPending);

        var b = new Md11AltimeterAnnouncer();
        b.OnUpdate(29.92, 0);                       // baseline
        b.OnUpdate(29.95, 1000);                    // the change
        Assert.Null(b.Due(1200));                   // checked 200 ms in: not settled
        Assert.True(b.HasPending);                  // still armed — the caller must check again
        Assert.Equal("Altimeter: 1014, 29.95", b.Due(2600));
    }

    [Fact]
    public void StandardPressure_IsSpokenAsStandard_InEitherUnit()
    {
        Assert.Equal("Altimeter standard", Md11AltimeterAnnouncer.Sentence(29.92));
        Assert.Equal("Altimeter standard", Md11AltimeterAnnouncer.Sentence(1013));
        Assert.Equal("Altimeter: 1020, 30.12", Md11AltimeterAnnouncer.Sentence(30.12));
        Assert.Equal("Altimeter: 995, 29.38", Md11AltimeterAnnouncer.Sentence(995));
    }

    [Fact]
    public void Reset_ReBaselines()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        a.OnUpdate(29.95, 1000);
        a.Reset();
        a.OnUpdate(30.00, 2000);                    // the first value after a reset is a baseline again
        Assert.Null(a.Due(9000));
        a.OnUpdate(30.05, 10000);
        Assert.Equal("Altimeter: 1018, 30.05", a.Due(12000));
    }

    /// <summary>A flight load re-delivers only what changed, so an empty baseline is seeded from the cache and a present one is left alone.</summary>
    [Fact]
    public void SeedIfEmpty_SeedsOnlyWhenThereIsNoBaseline()
    {
        var a = new Md11AltimeterAnnouncer();
        Assert.True(a.SeedIfEmpty(29.92));
        Assert.False(a.SeedIfEmpty(30.12));                  // already seeded: untouched
        a.OnUpdate(29.92, 1_000);
        Assert.False(a.HasPending);                          // unchanged: nothing armed
        a.OnUpdate(30.12, 2_000);
        Assert.Equal("Altimeter: 1020, 30.12", a.Due(2_000 + Md11AltimeterAnnouncer.SettleMs));   // the first real change speaks
    }

    [Fact]
    public void A_zero_first_delivery_is_no_reading_so_the_real_setting_becomes_the_silent_baseline()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(0, 0);          // an MD-11 export reads a flat 0 until the aircraft publishes it
        a.OnUpdate(29.92, 100);    // the first REAL value is the baseline and is never spoken
        Assert.False(a.HasPending);
        Assert.Null(a.Due(10_000));
    }

    [Fact]
    public void A_zero_after_the_baseline_is_ignored_not_a_change()
    {
        var a = new Md11AltimeterAnnouncer();
        a.OnUpdate(29.92, 0);
        a.OnUpdate(0, 100);
        Assert.False(a.HasPending);
        a.OnUpdate(29.92, 200);    // back on the same setting: still nothing to say
        Assert.False(a.HasPending);
    }

    [Fact]
    public void SeedIfEmpty_refuses_zero()
    {
        var a = new Md11AltimeterAnnouncer();
        Assert.False(a.SeedIfEmpty(0));
        Assert.True(a.SeedIfEmpty(1013));
    }

    [Theory]
    [InlineData(null, "Altimeter unavailable")]
    [InlineData(0.0, "Altimeter unavailable")]
    [InlineData(-1.0, "Altimeter unavailable")]
    [InlineData(29.92, "Altimeter standard")]
    public void HotkeySentence_treats_zero_as_no_reading(double? reading, string expected)
        => Assert.Equal(expected, Md11AltimeterAnnouncer.HotkeySentence(reading));
}
