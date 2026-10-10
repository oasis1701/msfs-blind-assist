using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Taxi flow's takeoff-memo check on the real step, evaluator and checklists and a real FlowManager
/// (FOA-9), run by the shared <see cref="FbwA380FlowRun"/> harness. The evaluator reads a seeded cache that
/// counts as connected; the executor has no sim. Its own class and file, so xUnit runs it in parallel with
/// <see cref="FbwA380LandingChecksBehaviourTests"/> (a timed-out check takes 5 s).
/// </summary>
public sealed class FbwA380TaxiMemoCheckBehaviourTests
{
    private const string Label = "Seat belts, ground spoilers and flaps for takeoff";
    private static readonly string[] MemoLines = { "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS", "TXC_MEMO_FLAPS", "TXC_FLAPS" };

    // Seat belts lit and the ground spoilers armed, with the flap lever where the test puts it.
    private static (string Key, double Value)[] Seed(double flapLever) => new[]
    {
        ("SEATBELT_SIGN_LIGHT", 1.0), ("A32NX_SPOILERS_ARMED", 1.0), ("A32NX_FLAPS_HANDLE_INDEX", flapLever),
    };

    /// <summary>Runs the Taxi flow's memo check alone, then the end-of-flow latch, on the shared harness
    /// (<see cref="FbwA380FlowRun.Run"/>). A line ticked after a run that began with
    /// <paramref name="windowHasTicked"/> false was ticked by the check.</summary>
    private static Task<FbwA380FlowRun.RunResult> Run((string Key, double Value)[] seed, bool windowHasTicked) =>
        FbwA380FlowRun.Run("TAXI", "TAXI_CL", new[] { "TX_MEMO_CHECK" }, seed, windowHasTicked);

    /// <summary>The flaps still UP at the holding point (held back for icing), the window's auto-detection
    /// having run first. The check times out and is skipped aloud, and all four of its lines are in the
    /// run's unfinished set; the end-of-flow latch then frees only the ones that read wrong. "Flaps: T.O"
    /// and "Flaps setting: SET" stay unticked and out of the latch; the seat belts and ground spoilers
    /// lines read right, were ticked by the window before the flow ran, and stay ticked and latched with
    /// the rest of the Taxi Checklist. The pilot then sets takeoff flaps: "Flaps: T.O" ticks itself from
    /// the aircraft, and "Flaps setting: SET", a line only the pilot can confirm, waits for their tick.</summary>
    [Fact]
    public async Task With_the_flaps_up_the_check_is_skipped_aloud_and_frees_only_the_memo_lines_that_read_wrong()
    {
        var r = await Run(Seed(flapLever: 0), windowHasTicked: true);

        string[] open = { "TXC_MEMO_FLAPS", "TXC_FLAPS" };
        string[] right = { "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS" };
        Assert.Equal(MemoLines.Order().ToArray(), open.Concat(right).Order().ToArray());

        // The window had already ticked the two lines that read right, and not the two that read wrong.
        Assert.Equal(right.Order(StringComparer.Ordinal).ToArray(), r.TickedBeforeRun);

        // The check timed out and is skipped aloud, never "Already set", and it ticked nothing.
        Assert.Contains($"Timed out waiting for: {Label}", r.Speech.All);
        Assert.Contains($"Skipping: {Label}", r.Speech.All);
        Assert.DoesNotContain($"Already set: {Label}", r.Speech.All);
        Assert.Equal(r.TickedBeforeRun, r.TickedAfterRun);
        // All four of its lines are in the run's unfinished set: the latch decides which of them it frees.
        Assert.Equal(MemoLines.Order(StringComparer.Ordinal).ToArray(), r.Unfinished);

        // After the latch: the lines that read wrong are open and free to follow the aircraft ...
        foreach (var id in open)
        {
            Assert.False(r.Items[id].IsChecked, $"{id} was ticked over a memo check that failed");
            Assert.True(r.Items[id].ExemptFromCompletionLatch, $"{id} is latched: it cannot follow the lever once it is set");
        }
        // ... the lines that read right stay ticked and latched ...
        foreach (var id in right)
        {
            Assert.True(r.Items[id].IsChecked, id);
            Assert.False(r.Items[id].ExemptFromCompletionLatch, id);
        }
        // ... and a line only the pilot can confirm is ticked by the finishing flow (the shared rule).
        Assert.True(r.Items["TXC_FCTEST"].IsChecked);
        Assert.True(r.Group.CompletionLatched);

        // The pilot sets takeoff flaps (CONF 2): "Flaps: T.O" follows the aircraft by itself, the latched
        // lines stand as they were, and "Flaps setting: SET" waits for the pilot's tick.
        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(Seed(flapLever: 2)));
        r.Checklist.EvaluateAutoDetection();
        Assert.True(r.Items["TXC_MEMO_FLAPS"].IsChecked);
        Assert.False(r.Items["TXC_MEMO_FLAPS"].ExemptFromCompletionLatch);
        Assert.All(right, id => Assert.True(r.Items[id].IsChecked, id));
        Assert.False(r.Items["TXC_FLAPS"].IsChecked);
    }

    /// <summary>The memo set: nothing is ticked before the run, the check says "Already set", and it is the
    /// check, not the end-of-flow latch (which would tick every line it does not exclude), that ticks its
    /// four lines and no others.</summary>
    [Fact]
    public async Task With_the_memo_set_the_check_ticks_its_four_lines_and_the_latch_ticks_the_rest()
    {
        var r = await Run(Seed(flapLever: 2), windowHasTicked: false);

        Assert.Empty(r.TickedBeforeRun);
        Assert.Contains($"Already set: {Label}", r.Speech.All);
        Assert.Empty(r.Unfinished);
        Assert.Equal(MemoLines.Order(StringComparer.Ordinal).ToArray(), r.TickedAfterRun);

        // The latch then ticks every line this short run did not exclude, and nothing is left out of it.
        Assert.True(r.Group.CompletionLatched);
        Assert.All(r.Group.Items, i =>
        {
            Assert.True(i.IsChecked, i.Id);
            Assert.False(i.ExemptFromCompletionLatch, i.Id);
        });
    }
}
