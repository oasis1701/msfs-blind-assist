using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using A320 = MSFSBlindAssist.FirstOfficer.FBWA320;
using A330 = MSFSBlindAssist.FirstOfficer.HWA330;
using Fenix = MSFSBlindAssist.FirstOfficer.Fenix;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A320-family First Officer lines are spoken to a pilot flying single-pilot
/// (docs/invariants/first-officer-airbus.md#foa-8, the owner's choice of 2026-10-07): no "(both)",
/// Airbus's both-pilots marker; no "Takeoff memo," / "Landing memo," prefix on each memo line (the
/// list name already says which memo); no "ANN", which a screen reader says as the name "Ann"; and
/// one wording for the CVR line in the Preflight list and the Preflight flow. Pinned on all three
/// profiles, checklist lines and flow steps alike.
/// </summary>
public class A320FamilyLabelWordingTests
{
    public static TheoryData<string> Aircraft => new() { "A32NX", "Fenix", "A330" };

    private static Dictionary<string, string> ItemLabels(string aircraft) => aircraft switch
    {
        "A32NX" => A320.FbwA320ChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id, i => i.Label),
        "Fenix" => Fenix.FenixChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id, i => i.Label),
        "A330" => A330.HwA330ChecklistDefinitions.Build().SelectMany(g => g.Items).ToDictionary(i => i.Id, i => i.Label),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft)),
    };

    private static List<(string Id, string Label)> StepLabels(string aircraft) => aircraft switch
    {
        "A32NX" => A320.FbwA320FlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => (s.Id, s.Label)).ToList(),
        "Fenix" => Fenix.FenixFlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => (s.Id, s.Label)).ToList(),
        "A330" => A330.HwA330FlowDefinitions.Build().SelectMany(f => f.Steps).Select(s => (s.Id, s.Label)).ToList(),
        _ => throw new ArgumentOutOfRangeException(nameof(aircraft)),
    };

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void Memo_lines_and_both_pilot_lines_read_as_the_bare_item(string aircraft)
    {
        string signs = aircraft == "Fenix" ? "Signs: ON" : "Seat belts: ON";
        var expected = new Dictionary<string, string>
        {
            ["TXC_MEMO_AUTOBRK"] = "Autobrake: MAX",
            ["TXC_MEMO_SIGNS"] = signs,
            ["TXC_MEMO_CABIN"] = "Cabin: READY",
            ["TXC_MEMO_SPLRS"] = "Spoilers: ARMED",
            ["TXC_MEMO_FLAPS"] = "Flaps: T.O",
            ["TXC_MEMO_TOCFG"] = "T.O config: NORMAL",
            ["LDC_MEMO_GEAR"] = "Landing gear: DOWN",
            ["LDC_MEMO_SIGNS"] = signs,
            ["LDC_MEMO_CABIN"] = "Cabin: READY",
            ["LDC_MEMO_SPLRS"] = "Spoilers: ARMED",
            ["LDC_MEMO_FLAPS"] = "Flaps: SET",
            ["CPC_BARO"] = "Baro reference: SET",
            ["APC_BARO"] = "Baro reference: SET",
            ["BSC_TOSPEEDS"] = "Takeoff speeds and thrust: SET",
            ["BSC_WINDOWS"] = "Windows: CLOSED",
            ["TXC_FCTEST"] = "Flight controls: CHECKED",
            ["TXC_FLAPS"] = "Flaps setting: SET",
            ["LUC_RUNWAY"] = "Takeoff runway: CONFIRMED",
        };
        var items = ItemLabels(aircraft);
        foreach (var (id, label) in expected)
            Assert.True(items[id] == label, $"{aircraft} {id}: expected \"{label}\", got \"{items[id]}\"");
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void Cockpit_lights_and_CVR_lines_are_spoken_in_full(string aircraft)
    {
        var items = ItemLabels(aircraft);
        Assert.Equal("Cockpit lights: annunciator bright", items["EPU_COCKPITLT"]);
        Assert.Equal("Cockpit lights: annunciator dim", items["AS_COCKPITLT"]);
        Assert.Equal("Cockpit lights: annunciator bright", items["SD_COCKPITLT"]);
        Assert.Equal("Cockpit lights: annunciator bright", items["SC_COCKPITLT"]);
        Assert.Equal("CVR test: listen for the test tone", items["PF_CVR"]);
        Assert.Equal("CVR test: listen for the test tone", StepLabels(aircraft).Single(s => s.Id == "PF_CVR").Label);
    }

    [Theory]
    [MemberData(nameof(Aircraft))]
    public void No_line_or_step_carries_two_pilot_or_memo_markup_or_ANN(string aircraft)
    {
        var labels = ItemLabels(aircraft).Select(kv => (Where: $"item {kv.Key}", Label: kv.Value))
            .Concat(StepLabels(aircraft).Select(s => (Where: $"step {s.Id}", s.Label)));
        var bad = labels
            .Where(l => l.Label.Contains("(both)", StringComparison.OrdinalIgnoreCase)
                     || Regex.IsMatch(l.Label, @"^(Takeoff|Landing) memo\b", RegexOptions.IgnoreCase)
                     || Regex.IsMatch(l.Label, @"\bANN\b"))
            .Select(l => $"{l.Where}: \"{l.Label}\"")
            .ToList();
        Assert.True(bad.Count == 0,
            $"{aircraft}: these lines carry wording a single pilot does not need (FOA-8, 2026-10-07): "
            + string.Join("; ", bad));
    }
}
