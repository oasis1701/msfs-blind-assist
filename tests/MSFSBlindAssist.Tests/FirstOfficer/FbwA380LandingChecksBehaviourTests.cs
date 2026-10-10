using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Landing flow's two read-only checks on the real steps, evaluator and checklists and a real
/// FlowManager (FO-13, FOA-9): the landing-memo check, then the gear-down check, which is the last thing
/// the pilot hears before "flow complete". The evaluator reads a seeded cache that counts as connected;
/// the executor has no sim. Its own class and file, so xUnit runs it in parallel with
/// <see cref="FbwA380TaxiMemoCheckBehaviourTests"/> (a timed-out check takes 5 s).
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

    private sealed record RunResult(
        GatedSpeechCapture Speech,
        FbwA380StateEvaluator State,
        ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator> Checklist,
        ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator> Landing,
        Dictionary<string, ChecklistItem<FbwA380ActionExecutor, FbwA380StateEvaluator>> Items,
        string[] TickedBeforeRun, string[] TickedAfterRun, string[] Unfinished);

    private static string[] Ticked(ChecklistGroup<FbwA380ActionExecutor, FbwA380StateEvaluator> group) =>
        group.Items.Where(i => i.IsChecked).Select(i => i.Id).Order(StringComparer.Ordinal).ToArray();

    /// <summary>Runs the Landing flow's two checks alone, then the end-of-flow latch. With
    /// <paramref name="windowHasTicked"/> the checklist's auto-detection runs first, as the First Officer
    /// window's one-second timer does the whole time it is open: every line that already reads right is
    /// ticked when the flow starts. Without it nothing is ticked before the run, so a line ticked after it
    /// was ticked by a check. The snapshots are taken before the run and after it; the latch that follows
    /// ticks every line the run did not exclude and so cannot show what a check did.</summary>
    private static async Task<RunResult> Run((string Key, double Value)[] seed, bool windowHasTicked)
    {
        var state = new FbwA380StateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed));
        var executor = new FbwA380ActionExecutor();
        var groups = FbwA380ChecklistDefinitions.Build();
        var flow = FbwA380FlowRun.Subset("LANDING", "LD_MEMO_CHECK", "LD_GEAR_DOWN_CHECK");
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator>(state, executor, groups);
        if (windowHasTicked)
            checklist.EvaluateAutoDetection();
        var landing = groups.Single(g => g.Id == "LANDING_CL");
        var tickedBeforeRun = Ticked(landing);
        var flowManager = new FlowManager<FbwA380ActionExecutor, FbwA380StateEvaluator>(state, executor, checklist, speech);

        await FbwA380FlowRun.RunToEnd(flowManager, flow);
        var tickedAfterRun = Ticked(landing);
        var unfinished = flowManager.UnfinishedChecklistItemIds.Order(StringComparer.Ordinal).ToArray();

        FbwA380FlowRun.Latch(checklist, groups, flow, flowManager.UnfinishedChecklistItemIds);
        return new RunResult(speech, state, checklist, landing, groups.SelectMany(g => g.Items).ToDictionary(i => i.Id),
            tickedBeforeRun, tickedAfterRun, unfinished);
    }

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
        Assert.Equal("Already set: Landing gear: DOWN", r.Speech.All[^2]);
        Assert.Equal("Landing flow complete", r.Speech.All[^1]);
        Assert.Empty(r.Unfinished);
        Assert.Equal(new[] { "LDC_FLAPS", "LDC_GEAR", "LDC_SEATBELT" }, r.TickedAfterRun);

        // The latch then ticks the line only the aircraft would answer, and nothing is left out of it.
        Assert.True(r.Landing.CompletionLatched);
        Assert.All(r.Landing.Items, i =>
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
        // ... and the line only the aircraft would answer is ticked by the finishing flow (the shared rule).
        Assert.True(r.Items["LDC_SPOILERS"].IsChecked);
        Assert.True(r.Landing.CompletionLatched);

        // The pilot sets FULL flaps: "Flaps: LDG" follows the aircraft by itself.
        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(Seed(flapLever: 4, conf3: 0)));
        r.Checklist.EvaluateAutoDetection();
        Assert.True(r.Items["LDC_FLAPS"].IsChecked);
        Assert.False(r.Items["LDC_FLAPS"].ExemptFromCompletionLatch);
    }
}
