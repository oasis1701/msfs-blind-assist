using System;
using System.Threading.Tasks;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The After Start flow's rudder-trim check on the real step, evaluator and checklists and a real
/// FlowManager (FO-6, FO-8), run by the shared <see cref="FbwA380FlowRun"/> harness. "Rudder trim:
/// NEUTRAL" reads the SEC trim word, which the reset only commands, so this read-only check, not the
/// reset, completes the line. The evaluator reads a seeded cache that counts as connected; the executor
/// has no sim. The check gives up after 1 s here instead of its 20 s (FbwA380FlowRun's
/// <c>waitTimeoutSeconds</c>; FbwA380ReadOnlyCheckTests pins the 20 s on the real step).
/// </summary>
public sealed class FbwA380RudderTrimCheckBehaviourTests
{
    private const string Label = "Rudder trim: NEUTRAL";
    private const string Line = "ASC_RUDDERTRIM";

    // The SEC 1 rudder-trim word (ARINC 429, normal operation, degrees): 0 is neutral, 2 is off neutral.
    private static (string Key, double Value)[] Trim(float degrees) =>
        new[] { ("A32NX_SEC_1_RUDDER_ACTUAL_POSITION", FcuValuePhrasesTests.Word(0b11, degrees)) };

    private static Task<FbwA380FlowRun.RunResult> Run((string Key, double Value)[] seed, bool windowHasTicked) =>
        FbwA380FlowRun.Run("AFTER_START", "AFTER_START_CL", new[] { "AS_RUDDERTRIM_CHECK" }, seed, windowHasTicked,
            waitTimeoutSeconds: 1);

    /// <summary>The trim reads neutral: the check says "Already set" and ticks the line itself. Nothing is
    /// ticked before the run, so the line ticked after it, before the end-of-flow latch (which ticks every
    /// line it does not exclude), was ticked by the check.</summary>
    [Fact]
    public async Task With_the_trim_neutral_the_check_ticks_the_rudder_trim_line()
    {
        var r = await Run(Trim(0.0f), windowHasTicked: false);

        Assert.Empty(r.TickedBeforeRun);
        Assert.Contains($"Already set: {Label}", r.Speech.All);
        Assert.Empty(r.Unfinished);
        Assert.Equal(new[] { Line }, r.TickedAfterRun);

        Assert.True(r.Group.CompletionLatched);
        Assert.True(r.Items[Line].IsChecked);
        Assert.False(r.Items[Line].ExemptFromCompletionLatch);
    }

    /// <summary>The trim never reads neutral, off neutral (2 degrees: the reset never centred it) or
    /// unread (no SEC trim word, NaN), the window's auto-detection having run first as in the app. The
    /// check times out and is skipped aloud, ticks nothing, and keeps the line out of the end-of-flow latch
    /// (FO-8): it stays unticked while the rest of the After Start Checklist latches. Once the trim reads
    /// neutral, the line ticks itself.</summary>
    [Theory]
    [InlineData(2.0f)]          // off neutral
    [InlineData(float.NaN)]     // unread: the word is not in the cache
    public async Task With_the_trim_never_neutral_the_check_times_out_and_the_line_stays_open(float trimDegrees)
    {
        var seed = float.IsNaN(trimDegrees) ? Array.Empty<(string Key, double Value)>() : Trim(trimDegrees);
        var r = await Run(seed, windowHasTicked: true);

        Assert.DoesNotContain(Line, r.TickedBeforeRun);
        Assert.Contains($"Timed out waiting for: {Label}", r.Speech.All);
        Assert.Contains($"Skipping: {Label}", r.Speech.All);
        Assert.DoesNotContain($"Already set: {Label}", r.Speech.All);
        Assert.Equal(r.TickedBeforeRun, r.TickedAfterRun);
        Assert.Equal(new[] { Line }, r.Unfinished);

        Assert.True(r.Group.CompletionLatched);
        Assert.False(r.Items[Line].IsChecked, $"{Line} was ticked over a trim that never read neutral");
        Assert.True(r.Items[Line].ExemptFromCompletionLatch, $"{Line} is latched: it cannot follow the trim");
        Assert.True(r.Items["ASC_PITCHTRIM"].IsChecked);   // a pilot-confirm line, ticked by the finishing flow

        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(Trim(0.0f)));
        r.Checklist.EvaluateAutoDetection();
        Assert.True(r.Items[Line].IsChecked);
        Assert.False(r.Items[Line].ExemptFromCompletionLatch);
    }
}
