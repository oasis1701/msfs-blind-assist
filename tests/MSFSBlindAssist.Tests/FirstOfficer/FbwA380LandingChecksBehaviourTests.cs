using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Landing flow's two read-only checks on the real steps, evaluator and checklists and a real
/// FlowManager (FO-13, FOA-9): the landing-memo check, then the gear-down check, which is the last thing
/// the pilot hears before "flow complete". Run by the shared <see cref="FbwA380FlowRun"/> harness. The
/// evaluator reads a seeded cache that counts as connected; the executor has no sim. Its own class and
/// file, so xUnit runs it in parallel with <see cref="FbwA380TaxiMemoCheckBehaviourTests"/> (a timed-out
/// check takes 5 s).
/// </summary>
public sealed class FbwA380LandingChecksBehaviourTests
{
    // Seat belts lit and the gear down (lever down, every leg fully out), with the flaps where the test
    // puts them: the lever position, and whether CONF 3 is selected on the MFD PERF APPR page.
    private static (string Key, double Value)[] Seed(double flapLever, double conf3) => new[]
    {
        ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", flapLever), (FbwA380StateEvaluator.LandingConf3Field, conf3),
        (FbwA380GearConfirmation.HandleField, 1.0),
        ("A32NX_GEAR_CENTER_POSITION", 100.0), ("A32NX_GEAR_LEFT_POSITION", 100.0), ("A32NX_GEAR_RIGHT_POSITION", 100.0),
    };

    /// <summary>Runs the Landing flow's two checks alone, then the end-of-flow latch, on the shared harness
    /// (<see cref="FbwA380FlowRun.Run"/>). A line ticked after a run that began with
    /// <paramref name="windowHasTicked"/> false was ticked by a check.</summary>
    private static Task<FbwA380FlowRun.RunResult> Run((string Key, double Value)[] seed, bool windowHasTicked) =>
        FbwA380FlowRun.Run("LANDING", "LANDING_CL", new[] { "LD_MEMO_CHECK", "LD_GEAR_DOWN_CHECK" }, seed, windowHasTicked);

    /// <summary>Both checks pass ("Already set"), the memo one after FULL flaps, or after flaps 3 with
    /// CONF 3 selected. Nothing is ticked before the run, so the three lines ticked after it, and no
    /// others, were ticked by the checks; the gear check is the last thing said before "flow complete".</summary>
    [Theory]
    [InlineData(4.0, 0.0)]   // FULL, CONF 3 not selected
    [InlineData(3.0, 1.0)]   // 3, CONF 3 selected on the MFD PERF APPR page
    public async Task A_landing_run_ends_with_the_gear_check_and_the_checks_tick_their_three_lines(double flaps, double conf3)
    {
        var r = await Run(Seed(flaps, conf3), windowHasTicked: false);

        Assert.Empty(r.TickedBeforeRun);
        Assert.Contains("Already set: Seat belts and landing flaps", r.Speech.All);
        Assert.Equal("Already set: Landing gear: DOWN", r.Speech.All[^2]);
        Assert.Equal("Landing flow complete", r.Speech.All[^1]);
        Assert.Empty(r.Unfinished);
        Assert.Equal(new[] { "LDC_FLAPS", "LDC_GEAR", "LDC_SEATBELT" }, r.TickedAfterRun);

        // The latch then ticks every line this short run did not exclude (here the ground spoilers line),
        // and nothing is left out of it.
        Assert.True(r.Group.CompletionLatched);
        Assert.All(r.Group.Items, i =>
        {
            Assert.True(i.IsChecked, i.Id);
            Assert.False(i.ExemptFromCompletionLatch, i.Id);
        });
    }

    /// <summary>The flaps short of the landing setting (3 with CONF 3 not selected), the window's
    /// auto-detection having run first, as it does the whole time the First Officer window is open: the
    /// seat belts and gear lines already read right and are ticked when the flow starts. The memo check
    /// times out and is skipped aloud, and the gear check still runs last and says "Already set". The
    /// memo check's two lines are in the run's unfinished set; the end-of-flow latch frees only the one
    /// that reads wrong. "Flaps: LDG" stays unticked and out of the latch; "Seat belts: ON" reads right,
    /// was ticked by the window, and stays ticked and latched with the rest of the Landing Checklist.
    /// The pilot then sets FULL flaps and "Flaps: LDG" ticks itself from the aircraft.</summary>
    [Fact]
    public async Task With_the_flaps_short_the_memo_check_is_skipped_aloud_and_frees_only_the_flaps_line_while_the_gear_check_still_runs_last()
    {
        var r = await Run(Seed(flapLever: 3, conf3: 0), windowHasTicked: true);

        // The window had ticked the lines that read right (belts on, gear down) and not the flaps.
        Assert.Equal(new[] { "LDC_GEAR", "LDC_SEATBELT" }, r.TickedBeforeRun);

        Assert.Contains("Timed out waiting for: Seat belts and landing flaps", r.Speech.All);
        Assert.Contains("Skipping: Seat belts and landing flaps", r.Speech.All);
        Assert.DoesNotContain("Already set: Seat belts and landing flaps", r.Speech.All);
        Assert.Equal("Already set: Landing gear: DOWN", r.Speech.All[^2]);
        Assert.Equal("Landing flow complete", r.Speech.All[^1]);
        Assert.Equal(r.TickedBeforeRun, r.TickedAfterRun);
        Assert.Equal(new[] { "LDC_FLAPS", "LDC_SEATBELT" }, r.Unfinished);

        // After the latch: the flaps line is open and free to follow the aircraft ...
        Assert.False(r.Items["LDC_FLAPS"].IsChecked, "LDC_FLAPS was ticked over a memo check that failed");
        Assert.True(r.Items["LDC_FLAPS"].ExemptFromCompletionLatch, "LDC_FLAPS is latched: it cannot follow the flaps once they are set");
        // ... the seat belts line reads right, so it stays ticked and latched with the gear line ...
        foreach (var id in new[] { "LDC_SEATBELT", "LDC_GEAR" })
        {
            Assert.True(r.Items[id].IsChecked, id);
            Assert.False(r.Items[id].ExemptFromCompletionLatch, id);
        }
        // ... and the ground spoilers line, which this short run did not exclude, is ticked by the finishing
        // flow (the latch ticks every line a run does not exclude).
        Assert.True(r.Items["LDC_SPOILERS"].IsChecked);
        Assert.True(r.Group.CompletionLatched);

        // The pilot sets FULL flaps: "Flaps: LDG" follows the aircraft by itself.
        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(Seed(flapLever: 4, conf3: 0)));
        r.Checklist.EvaluateAutoDetection();
        Assert.True(r.Items["LDC_FLAPS"].IsChecked);
        Assert.False(r.Items["LDC_FLAPS"].ExemptFromCompletionLatch);
    }
}
