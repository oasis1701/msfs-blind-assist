using System;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// A press an executor holds (a fire test, the T.O CONFIG test, a cabin call) is released when the
/// First Officer window closes, at once, through <see cref="IFoActionExecutor.ReleaseHeldPresses"/>
/// (<see cref="HeldPresses"/>). On app exit MainForm disconnects right after closing the window, and a
/// release left to the hold's own timer then went nowhere: the fire bell, a held T.O CONFIG key or the
/// cabin chime kept going in the sim after MSFSBA had closed (PR 160 second review). Whoever ends a hold
/// first sends its release, once.
/// </summary>
public class FoHeldPressReleaseTests
{
    [Fact]
    public void A_hold_ended_on_its_own_timer_is_released_once()
    {
        var held = new HeldPresses();
        int released = 0;

        long hold = held.Begin(() => released++);
        held.End(hold);
        held.End(hold);
        held.ReleaseAll();

        Assert.Equal(1, released);
        Assert.Equal(0, held.Count);
    }

    [Fact]
    public void Releasing_all_sends_every_outstanding_release_and_the_holds_own_end_sends_nothing()
    {
        var held = new HeldPresses();
        int a = 0, b = 0;
        long holdA = held.Begin(() => a++);
        long holdB = held.Begin(() => b++);

        Assert.Equal(2, held.ReleaseAll());
        held.End(holdA);
        held.End(holdB);

        Assert.Equal(1, a);
        Assert.Equal(1, b);
        Assert.Equal(0, held.ReleaseAll());
    }

    [Fact]
    public void A_release_that_throws_is_logged_and_the_others_still_go_out()
    {
        var held = new HeldPresses();
        int released = 0;
        held.Begin(() => throw new InvalidOperationException("the sim went away"));
        held.Begin(() => released++);

        Assert.Equal(2, held.ReleaseAll());
        Assert.Equal(1, released);
    }

    // Nested holds unwind: a test button held inside the weather radar test is released before the
    // test's own restore, and that restore's WX mode before its overlay off.
    [Fact]
    public void Releasing_all_goes_latest_hold_first()
    {
        var held = new HeldPresses();
        var order = new System.Collections.Generic.List<string>();
        held.Begin(() => order.Add("overlay off"));
        held.Begin(() => order.Add("WX mode"));
        held.Begin(() => order.Add("TEST button up"));

        held.ReleaseAll();

        Assert.Equal(new[] { "TEST button up", "WX mode", "overlay off" }, order);
    }

    // The owner sends a release itself, awaited and paced (the weather radar test's restore), unless
    // the window's close already sent it.
    [Fact]
    public void A_release_taken_over_is_never_sent_and_one_already_sent_cannot_be_taken_over()
    {
        var held = new HeldPresses();
        int sent = 0;
        long kept = held.Begin(() => sent++);
        Assert.True(held.TakeOver(kept));
        Assert.Equal(0, held.ReleaseAll());
        Assert.Equal(0, sent);

        long closed = held.Begin(() => sent++);
        held.ReleaseAll();                                                   // the window closes first
        Assert.False(held.TakeOver(closed));
        Assert.Equal(1, sent);
    }

    // The window's close (FirstOfficerForm.Dispose) calls ReleaseHeldPresses, then detaches the executor.
    [Fact]
    public async Task A32nx_fire_test_held_when_the_window_closes_is_released_at_once_and_once()
    {
        var definition = new RecordingFbwA320Definition();
        var executor = definition.Executor();

        var test = executor.FireTestAsync("FIRE_TEST_APU");
        Assert.Single(definition.Writes);           // pressed; the 1.5 s hold runs
        executor.ReleaseHeldPresses();             // the window closes
        executor.SetSimConnect(null);

        // Released now, before the hold's own end, while the sim still listens.
        Assert.Equal(new[] { ("A32NX_FIRE_TEST_APU", 1.0), ("A32NX_FIRE_TEST_APU", 0.0) },
            definition.Writes.Select(w => (w.Key, w.Value)));
        Assert.True(await test);
        Assert.Equal(2, definition.Writes.Length);   // and not again when the hold ends
    }

    [Fact]
    public async Task A330_cabin_call_held_when_the_window_closes_is_released_at_once_and_once()
    {
        var definition = new RecordingHwA330Definition();
        var executor = definition.Executor();

        var call = executor.CabinCall();
        Assert.Single(definition.Writes);
        executor.ReleaseHeldPresses();
        executor.SetSimConnect(null);

        Assert.Equal(new[] { ("PUSH_OVHD_CALLS_ALL", 1.0), ("PUSH_OVHD_CALLS_ALL", 0.0) },
            definition.Writes.Select(w => (w.Key, w.Value)));
        Assert.True(await call);
        Assert.Equal(2, definition.Writes.Length);
    }

    [Fact]
    public async Task A380_fire_test_held_when_the_window_closes_is_released_at_once_and_once()
    {
        var definition = new RecordingA380Definition();
        var executor = definition.Executor();

        var test = executor.FireTestAsync();
        Assert.Single(definition.Writes);
        executor.ReleaseHeldPresses();
        executor.SetSimConnect(null);

        Assert.Equal(new[] { ("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 1.0), ("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 0.0) },
            definition.Writes);
        Assert.True(await test);
        Assert.Equal(2, definition.Writes.Length);
    }

    // The window's close calls the executor's ReleaseHeldPresses before it detaches the executor, so the
    // release goes out while the sim is still connected (on exit MainForm disconnects right after).
    [Fact]
    public void The_window_releases_held_presses_before_it_detaches_the_executor()
    {
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
            "MSFSBlindAssist", "Forms", "FirstOfficer", "FirstOfficerForm.cs"));
        int release = source.IndexOf("_actionExec.ReleaseHeldPresses();", StringComparison.Ordinal);
        int detach = source.IndexOf("_profile.SetExecutorSimConnect(_actionExec, null);", StringComparison.Ordinal);

        Assert.True(release > 0, "Dispose no longer releases the executor's held presses");
        Assert.True(detach > release, "the executor is detached before its held presses are released");
    }
}
