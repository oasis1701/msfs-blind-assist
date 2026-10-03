using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The DA40's quiet period after a flight load: record, do not narrate.</summary>
public class DA40LoadSettleTests
{
    [Fact]
    public void NothingIsSettlingUntilALoadBegins()
        => Assert.False(new DA40LoadSettle().Settling);

    [Fact]
    public void ItEndsOnceTheAircraftHasPublishedAndGoneQuiet()
    {
        var s = new DA40LoadSettle();
        s.Begin();

        s.NoteChange();
        s.OnBatchDelivered(1);          // the load's values arrive
        for (int i = 0; i < DA40LoadSettle.QuietDeliveries - 1; i++)
        {
            s.OnBatchDelivered(1);
            Assert.True(s.Settling);
        }
        s.OnBatchDelivered(1);

        Assert.False(s.Settling);
    }

    [Fact]
    public void ItWaitsForTheAircraftToPublishBeforeCountingQuiet()
    {
        // A loaded aircraft can take several seconds to publish anything; quiet before that
        // is the load still happening, not the load having finished.
        var s = new DA40LoadSettle();
        s.Begin();
        for (int i = 0; i < DA40LoadSettle.QuietDeliveries * 2; i++) s.OnBatchDelivered(1);

        Assert.True(s.Settling);
    }

    [Fact]
    public void ItEndsAfterTheCeilingRegardless()
    {
        var s = new DA40LoadSettle();
        s.Begin();
        for (int i = 0; i < DA40LoadSettle.MaxDeliveries; i++) { s.NoteChange(); s.OnBatchDelivered(1); }

        Assert.False(s.Settling);
    }

    [Fact]
    public void OnlyTheFirstBatchOfACycleCounts()
    {
        var s = new DA40LoadSettle();
        s.Begin();
        s.NoteChange();
        for (int i = 0; i < 20; i++) s.OnBatchDelivered(2);

        Assert.True(s.Settling);
    }
}

/// <summary>The settle wired into the definition, end to end.</summary>
public class CowsDA40LoadSettleWiringTests
{
    private sealed class Capture : MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer
    {
        public Capture() : base(System.IntPtr.Zero) { }
        public System.Collections.Generic.List<string> All { get; } = new();
        public override void Announce(string m) { if (!Suppressed && !OwnerMute) All.Add(m); }
        public override void AnnounceImmediate(string m) { if (!OwnerMute) All.Add(m); }
        public override void AnnounceQueued(string m) { if (!Suppressed && !OwnerMute) All.Add(m); }
        public override void AnnounceWithQueue(string m) { if (!Suppressed && !OwnerMute) All.Add(m); }
    }

    [Fact]
    public void AValueThatDiffersFromTheLastFlightIsRecordedNotNarrated()
    {
        var def = new CowsDA40Definition(DA40Variant.NG);
        var speech = new Capture();

        def.ProcessSimVarUpdate("DA40_FAIL_COOLANT_LEAK_SET", 0, speech);      // flight 1 baseline
        def.OnSimContextReset();                                              // flight 2 loads
        Assert.True(def.ProcessSimVarUpdate("DA40_FAIL_COOLANT_LEAK_SET", 0.35, speech));
        Assert.Empty(speech.All);

        for (int i = 0; i <= DA40LoadSettle.QuietDeliveries; i++) def.OnContinuousBatchDelivered(1);

        // The recorded 35 percent is the baseline now, so a real worsening still speaks.
        def.ProcessSimVarUpdate("DA40_FAIL_COOLANT_LEAK_SET", 0.65, speech);
        Assert.Equal(new[] { "Coolant leak worsening, 65 percent" }, speech.All);
    }
}
