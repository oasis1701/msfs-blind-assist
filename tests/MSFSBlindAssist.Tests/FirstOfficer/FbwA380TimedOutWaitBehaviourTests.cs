using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The three A380 waits that read the field a write before them sets (FO-6, FO-8, FOA-9): Cockpit
/// Preparation's seat-belt sign wait after "Seatbelt signs: ON", and Parking's engines-off wait after
/// "Engine masters 1 to 4: OFF" and seat-belt sign wait after "Seatbelt signs: OFF". The write lands and
/// ticks its action line, and only the wait tells whether the aircraft followed, so each wait also completes
/// that action line: when the wait times out, the line stays out of the end-of-flow latch with the read-back
/// line the wait confirms, and keeps following the aircraft. The real steps, checklists and FlowManager, run
/// by the shared <see cref="FbwA380FlowRun"/> harness; the writes land on a
/// <see cref="RecordingA380Definition"/>, and the seeded cache keeps reading what the aircraft showed before
/// the run, as an aircraft that never follows would. Each wait gives up after 1 s here instead of its own
/// 60 s or 120 s (FbwA380FlowRun's <c>waitTimeoutSeconds</c>). Each run pays one inter-step pause, between
/// the write and its wait (FO-5).
/// </summary>
public sealed class FbwA380TimedOutWaitBehaviourTests
{
    // Flow, write, wait, and every line the wait keeps out of the latch when it times out: the action line its
    // write ticked first, then the read-back line it confirms, where it has one.
    public static IEnumerable<object[]> Waits() => new[]
    {
        new object[] { "COCKPIT_PREP", "CP_SEATBELT", "CP_WAIT_SB", new[] { "CP_SEATBELT", "CPC_SEATBELT" } },
        new object[] { "PARKING", "PK_MASTERS_OFF", "PK_WAIT_ENG", new[] { "PK_MASTERS_OFF", "PKC_ENGINES" } },
        new object[] { "PARKING", "PK_SEATBELTS_OFF", "PK_WAIT_SB", new[] { "PK_SEATBELTS_OFF" } },
    };

    // For each write, written out here and not read from the code under test: what the aircraft reads when it
    // never follows the write (Stays) and once it has (Follows), and what the write sends (Writes). The sign
    // light is 1 lit, 0 dark; an engine master is 0 off, 1 on.
    private static readonly Dictionary<string, ((string Key, double Value)[] Stays, (string Key, double Value)[] Follows,
        (string Key, double Value)[] Writes)> Aircraft = new()
    {
        ["CP_SEATBELT"] = (new[] { ("SEATBELT_SIGN_LIGHT", 0.0) }, new[] { ("SEATBELT_SIGN_LIGHT", 1.0) },
            new[] { ("SEATBELT_SIGN", 0.0) }),
        // Engine 3's master never reads off.
        ["PK_MASTERS_OFF"] = (Masters(0.0, 0.0, 1.0, 0.0), Masters(0.0, 0.0, 0.0, 0.0), Masters(0.0, 0.0, 0.0, 0.0)),
        ["PK_SEATBELTS_OFF"] = (new[] { ("SEATBELT_SIGN_LIGHT", 1.0) }, new[] { ("SEATBELT_SIGN_LIGHT", 0.0) },
            new[] { ("SEATBELT_SIGN", 2.0) }),
    };

    // The four engine masters (the fuel valve switches), in order.
    private static (string Key, double Value)[] Masters(double m1, double m2, double m3, double m4) => new[]
    {
        ("ENG_VALVE_SWITCH:1", m1), ("ENG_VALVE_SWITCH:2", m2), ("ENG_VALVE_SWITCH:3", m3), ("ENG_VALVE_SWITCH:4", m4),
    };

    private static string Label(string flowId, string stepId) =>
        FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId).AnnounceText;

    /// <summary>The write lands and ticks its action line, the aircraft never follows, and the wait times out
    /// and is skipped aloud. The window's auto-detection runs first, as it does the whole time the First
    /// Officer window is open, and finds the line reading wrong, so the tick the run leaves was the write's.
    /// After the end-of-flow latch every line the wait completes is free (MarkGroupComplete frees the action
    /// line although the write ticked it, because it reads definitely false: FO-8), the window's next pass
    /// un-ticks it, and it ticks itself once the aircraft follows.</summary>
    [Theory]
    [MemberData(nameof(Waits))]
    public async Task A_timed_out_wait_keeps_the_line_its_write_ticked_out_of_the_latch(
        string flowId, string writeId, string waitId, string[] lineIds)
    {
        var (stays, follows, writes) = Aircraft[writeId];
        string writeLabel = Label(flowId, writeId), waitLabel = Label(flowId, waitId);
        var definition = new RecordingA380Definition();

        // The action line is in the action group named after its flow: the snapshots are of that group.
        var r = await FbwA380FlowRun.Run(flowId, flowId, new[] { writeId, waitId }, stays, windowHasTicked: true,
            waitTimeoutSeconds: 1, writesLandOn: definition);

        // The write landed, was not skipped, and ticked its own line; the wait timed out and was skipped aloud.
        Assert.Equal(writes, definition.Writes);
        Assert.Contains(writeLabel, r.Speech.All);
        Assert.DoesNotContain(r.Speech.All, s => s.StartsWith("Sim not connected", StringComparison.Ordinal));
        Assert.Contains($"Timed out waiting for: {waitLabel}", r.Speech.All);
        Assert.Equal(new[] { $"Skipping: {waitLabel}" },
            r.Speech.All.Where(s => s.StartsWith("Skipping: ", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain(writeId, r.TickedBeforeRun);   // it reads wrong, so the window had not ticked it
        Assert.Contains(writeId, r.TickedAfterRun);          // the write's tick, before the latch

        // After the latch the group is latched, but none of the wait's lines with it: each is free.
        Assert.True(r.Group.CompletionLatched);
        foreach (var id in lineIds)
            Assert.True(r.Items[id].ExemptFromCompletionLatch, $"{id} is latched over an aircraft that never followed");
        // Because the timed-out wait names every one of them, and no other line.
        Assert.Equal(lineIds.Order(StringComparer.Ordinal).ToArray(), r.Unfinished);

        // The window's next pass un-ticks the action line: no line stays ticked over the aircraft.
        r.Checklist.EvaluateAutoDetection();
        foreach (var id in lineIds)
            Assert.False(r.Items[id].IsChecked, $"{id} stayed ticked over an aircraft that never followed");

        // The aircraft follows: every line ticks itself and joins the latch.
        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(follows));
        r.Checklist.EvaluateAutoDetection();
        foreach (var id in lineIds)
        {
            Assert.True(r.Items[id].IsChecked, $"{id} did not tick itself once the aircraft followed");
            Assert.False(r.Items[id].ExemptFromCompletionLatch, id);
        }
    }

    /// <summary>The masters land and the engines read off: the wait passes at once, and the masters' line,
    /// ticked by the write and marked again by the wait, stays ticked and latches with its group, as does the
    /// Parking Checklist's "Engines: OFF". Nothing is ticked before the run, so a line ticked after it was
    /// ticked by a step.</summary>
    [Fact]
    public async Task When_the_engines_read_off_the_wait_passes_and_the_masters_line_stays_ticked_and_latched()
    {
        var (_, follows, writes) = Aircraft["PK_MASTERS_OFF"];
        string waitLabel = Label("PARKING", "PK_WAIT_ENG");
        var definition = new RecordingA380Definition();

        var r = await FbwA380FlowRun.Run("PARKING", "PARKING", new[] { "PK_MASTERS_OFF", "PK_WAIT_ENG" }, follows,
            windowHasTicked: false, waitTimeoutSeconds: 1, writesLandOn: definition);

        Assert.Equal(writes, definition.Writes);
        Assert.Empty(r.TickedBeforeRun);
        Assert.Contains($"Waiting for: {waitLabel}", r.Speech.All);
        Assert.DoesNotContain($"Timed out waiting for: {waitLabel}", r.Speech.All);
        Assert.DoesNotContain(r.Speech.All, s => s.StartsWith("Skipping: ", StringComparison.Ordinal));
        Assert.Empty(r.Unfinished);
        Assert.Equal(new[] { "PK_MASTERS_OFF" }, r.TickedAfterRun);

        Assert.True(r.Group.CompletionLatched);
        foreach (var id in new[] { "PK_MASTERS_OFF", "PKC_ENGINES" })
        {
            Assert.True(r.Items[id].IsChecked, id);
            Assert.False(r.Items[id].ExemptFromCompletionLatch, id);
        }
        r.Checklist.EvaluateAutoDetection();
        Assert.True(r.Items["PK_MASTERS_OFF"].IsChecked);
    }
}
