// tests/MSFSBlindAssist.Tests/Cessna172EngineStartTests.cs
// The Start Engine button walks the key to START, waits for GENERAL ENG COMBUSTION:1, and
// returns the key to BOTH — on catch, or after 10 s. It is a pure machine: the definition sends
// the events and speaks the sentences, this decides WHEN. Time is injected so the timeout is
// exact rather than a Thread.Sleep guess.
using MSFSBlindAssist.Aircraft.C172;

namespace MSFSBlindAssist.Tests;

public class Cessna172EngineStartTests
{
    [Fact]
    public void A_fresh_machine_is_idle_and_ticks_to_nothing()
    {
        var m = new Cessna172EngineStart();
        Assert.False(m.IsInFlight);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(1000, combustion: true));
    }

    [Fact]
    public void Begin_arms_the_machine_and_a_second_press_is_ignored()
    {
        var m = new Cessna172EngineStart();
        Assert.True(m.Begin(1000));
        Assert.True(m.IsInFlight);
        Assert.False(m.Begin(1500));           // serialized: one start at a time
        Assert.True(m.IsInFlight);
    }

    [Fact]
    public void Combustion_true_ends_the_start_as_running()
    {
        var m = new Cessna172EngineStart();
        m.Begin(1000);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(2000, combustion: false));
        Assert.Equal(Cessna172EngineStart.Outcome.Running, m.Tick(3000, combustion: true));
        Assert.False(m.IsInFlight);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(4000, combustion: true)); // spoken once
    }

    [Fact]
    public void No_combustion_reading_yet_keeps_waiting()
    {
        var m = new Cessna172EngineStart();
        m.Begin(1000);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(2000, combustion: null));
        Assert.True(m.IsInFlight);
    }

    [Fact]
    public void Ten_seconds_without_combustion_ends_the_start_as_failed()
    {
        var m = new Cessna172EngineStart();
        m.Begin(1000);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(1000 + Cessna172EngineStart.TimeoutMs - 1, false));
        Assert.Equal(Cessna172EngineStart.Outcome.Failed, m.Tick(1000 + Cessna172EngineStart.TimeoutMs, false));
        Assert.False(m.IsInFlight);
    }

    [Fact]
    public void A_catch_on_the_timeout_sample_counts_as_running()
    {
        var m = new Cessna172EngineStart();
        m.Begin(1000);
        Assert.Equal(Cessna172EngineStart.Outcome.Running, m.Tick(1000 + Cessna172EngineStart.TimeoutMs, true));
    }

    [Fact]
    public void Cancel_reports_whether_a_start_was_in_flight()
    {
        var m = new Cessna172EngineStart();
        Assert.False(m.Cancel());
        m.Begin(1000);
        Assert.True(m.Cancel());
        Assert.False(m.IsInFlight);
        Assert.Equal(Cessna172EngineStart.Outcome.None, m.Tick(2000, true));
    }

    [Fact]
    public void A_start_can_be_repeated_after_it_ends()
    {
        var m = new Cessna172EngineStart();
        m.Begin(1000);
        m.Tick(2000, true);
        Assert.True(m.Begin(3000));
    }

    [Fact]
    public void The_sentences_are_short_and_distinct()
    {
        Assert.Equal("Engine running", Cessna172EngineStart.RunningSentence);
        Assert.Equal("Engine did not start", Cessna172EngineStart.FailedSentence);
    }
}
