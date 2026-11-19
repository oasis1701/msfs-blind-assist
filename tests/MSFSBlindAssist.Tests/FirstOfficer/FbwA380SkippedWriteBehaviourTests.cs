using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The behaviour half of <see cref="FbwA380FlowChecklistLinkTests"/> (FO-6, FO-8): a write that fails is
/// skipped aloud, and every line it names stays unticked through the end-of-flow latch and keeps following
/// the aircraft. The real step, checklists and FlowManager, run by the shared <see cref="FbwA380FlowRun"/>
/// harness: the evaluator reads a seeded cache that counts as connected, and the executor has no sim, so
/// the write fails ("Sim not connected") and is skipped. The aircraft reads the way the failed write left
/// it, and the window's auto-detection runs first, as it does the whole time the First Officer window is
/// open. The latch ticks every line a run does not exclude, so a line still unticked after it was kept out
/// of the latch by the skip: the latch cannot make these assertions pass. One step per run, so no run pays
/// an inter-step pause (FO-5).
/// </summary>
public sealed class FbwA380SkippedWriteBehaviourTests
{
    // Flow, step, the lines it names, and which of those have no state of their own (an action line the
    // pilot confirms by ticking it). The rudder-trim reset completes only its own line: the read-only check
    // after it completes "Rudder trim: NEUTRAL" (FbwA380RudderTrimCheckBehaviourTests).
    public static IEnumerable<object[]> Writes() => new[]
    {
        new object[] { "COCKPIT_PREP", "CP_GEAR", new[] { "CP_GEAR" }, NoStatelessLine },
        new object[] { "BEFORE_START", "BS_BEACON", new[] { "BS_BEACON", "BSC_BEACON" }, NoStatelessLine },
        new object[] { "AFTER_START", "AS_RUDDERTRIM", new[] { "AS_RUDDERTRIM" }, new[] { "AS_RUDDERTRIM" } },
        new object[] { "TAXI", "TX_WXR", new[] { "TX_WXR", "TXC_WXR" }, NoStatelessLine },
        new object[] { "APPROACH", "AP_CABIN", new[] { "AP_CABIN" }, new[] { "AP_CABIN" } },
        new object[] { "SECURE", "SC_BAT_OFF", new[] { "SC_BAT_OFF", "SCC_BAT" }, NoStatelessLine },
    };

    private static readonly string[] NoStatelessLine = Array.Empty<string>();

    // What the aircraft reads for each step: Wrong, as the failed write left it; Right, once the pilot has
    // done it. Written out here, not read from the code under test. The rudder-trim reset and the cabin call
    // have no state of their own, so they read nothing.
    private static readonly Dictionary<string, ((string Key, double Value)[] Wrong, (string Key, double Value)[] Right)> Readings = new()
    {
        ["CP_GEAR"] = (new[] { ("A32NX_GEAR_HANDLE_POSITION", 0.0) }, new[] { ("A32NX_GEAR_HANDLE_POSITION", 1.0) }),
        ["BS_BEACON"] = (new[] { ("LIGHT_BEACON", 0.0) }, new[] { ("LIGHT_BEACON", 1.0) }),
        ["AS_RUDDERTRIM"] = (Array.Empty<(string Key, double Value)>(), Array.Empty<(string Key, double Value)>()),
        ["TX_WXR"] = (new[] { ("XMLVAR_A320_WeatherRadar_Sys", 0.0) }, new[] { ("XMLVAR_A320_WeatherRadar_Sys", 1.0) }),
        ["AP_CABIN"] = (Array.Empty<(string Key, double Value)>(), Array.Empty<(string Key, double Value)>()),
        ["SC_BAT_OFF"] = (Batteries(1.0), Batteries(0.0)),
    };

    // The four batteries (BAT 1, BAT 2, ESS, APU), all at one value: 1 is on, 0 is off.
    private static (string Key, double Value)[] Batteries(double value) => new[]
    {
        ("A32NX_OVHD_ELEC_BAT_1_PB_IS_AUTO", value), ("A32NX_OVHD_ELEC_BAT_2_PB_IS_AUTO", value),
        ("A32NX_OVHD_ELEC_BAT_ESS_PB_IS_AUTO", value), ("A32NX_OVHD_ELEC_BAT_APU_PB_IS_AUTO", value),
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task A_skipped_write_leaves_every_line_it_names_unticked_and_free(
        string flowId, string stepId, string[] lineIds, string[] statelessLineIds)
    {
        var (wrong, right) = Readings[stepId];
        string label = FbwA380FlowDefinitions.Build().Single(f => f.Id == flowId).Steps.Single(s => s.Id == stepId).AnnounceText;

        // The step's own line is in the action group named after its flow: the snapshots are of that group.
        var r = await FbwA380FlowRun.Run(flowId, flowId, new[] { stepId }, wrong, windowHasTicked: true);

        // The write failed and was skipped aloud, never "Already set", and it ticked nothing.
        Assert.Contains($"Sim not connected — cannot perform: {label}", r.Speech.All);
        Assert.Equal(new[] { $"Skipping: {label}" },
            r.Speech.All.Where(s => s.StartsWith("Skipping: ", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain($"Already set: {label}", r.Speech.All);
        Assert.DoesNotContain(stepId, r.TickedBeforeRun);   // it reads wrong, so the window had not ticked it
        Assert.Equal(r.TickedBeforeRun, r.TickedAfterRun);
        // Every line the step names, and no other, is in the run's unfinished set.
        Assert.Equal(lineIds.Order(StringComparer.Ordinal).ToArray(), r.Unfinished);

        // After the latch: every line the step names is open and free to follow the aircraft.
        foreach (var id in lineIds)
        {
            Assert.False(r.Items[id].IsChecked, $"{id} was ticked over a write that failed");
            Assert.True(r.Items[id].ExemptFromCompletionLatch,
                $"{id} is latched: it cannot follow the aircraft once the pilot does it");
        }

        // The pilot does it: a line that reads the aircraft ticks itself and joins the latch; a line with no
        // state of its own (the rudder-trim reset, the cabin call) waits for the pilot's tick. Which is which
        // is the row's written-out list, and each line's kind is pinned against it.
        r.State.SetSimConnect(SeededSimConnectCache.ConnectedWith(right));
        r.Checklist.EvaluateAutoDetection();
        foreach (var id in lineIds)
        {
            var item = r.Items[id];
            bool hasState = !statelessLineIds.Contains(id);
            Assert.True(hasState == item.IsAutoDetectable,
                hasState ? $"{id} should read the aircraft, but has no state field" : $"{id} should have no state of its own");
            if (hasState)
            {
                Assert.True(item.IsChecked, $"{id} did not tick itself once the aircraft agreed");
                Assert.False(item.ExemptFromCompletionLatch, id);
            }
            else
            {
                Assert.False(item.IsChecked, $"{id} has no state of its own, so only the pilot's tick confirms it");
                Assert.True(item.ExemptFromCompletionLatch, id);
            }
        }
    }
}
