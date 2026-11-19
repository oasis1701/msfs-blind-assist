using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.Fenix;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The Taxi flow's takeoff-memo check, the evaluators' <c>FO_TAXI_MEMO_OK</c>, against the Taxi
/// checklist lines it completes (FOA-8). The check is one field that stands for three Auto lines,
/// "Spoilers: ARMED", "Flaps: T.O" and the memo's seat-belts (Fenix: signs) line, so it must read
/// exactly what the AND of those lines' own conditions reads, from the aircraft's own checklist
/// definitions: 1 when all three hold, 0 when any does not, and NaN while any variable they read is
/// unknown (unknown is not false, and it outranks wrong). The fourth line the check completes,
/// "Flaps setting: SET" (<c>TXC_FLAPS</c>), is a Reminder with no condition, so it is not in the AND.
/// <para>The per-aircraft <c>TaxiMemoOk</c> tests pin its truth table with literal values; they cannot
/// say which variable feeds which argument. On the A32NX and A330 the first two are both "above one
/// half" tests, so swapping the spoilers and the flap lever passed every one of them. Only the flap
/// lever at FULL with the spoilers armed tells the two apart (the flap test refuses 4 and the spoiler
/// test accepts it), and this grid has that row.</para>
/// Each aircraft has a class of its own below, the way
/// <see cref="A320FamilyReadbackLinkBehaviourTests{TExec, TState}"/> does: the A330 duplicates the
/// A32NX profile ([FOA-3]) and the Fenix reads different variables, so each supplies the raw values.
/// </summary>
public abstract class A320FamilyTaxiMemoCheckTests<TExec, TState>
    where TExec : IFoActionExecutor
    where TState : LVarStateEvaluator
{
    protected abstract TState NewState();
    protected abstract List<ChecklistGroup<TExec, TState>> BuildChecklists();

    // The raw sim variables the three Auto memo lines read, and the value each holds in each named
    // state (the order is in the comments of the subclasses). The flap lever is the lever position.
    protected abstract string SpoilersVar { get; }
    protected abstract double[] SpoilersRaw { get; }     // armed, not armed
    protected abstract string FlapLeverVar { get; }
    protected abstract string SeatBeltsVar { get; }
    protected abstract double[] SeatBeltsRaw { get; }    // on, off
    protected abstract string NoSmokingVar { get; }
    protected abstract double[] NoSmokingRaw { get; }    // on, auto, off

    // One grid point as indexes into the named states above (the lever is its position, 0-4); null leaves
    // that input unread, as a cold cache does.
    private sealed record Row(int? Spoilers, int? Lever, int? SeatBelts, int? NoSmoking)
    {
        public override string ToString() =>
            $"spoilers {Name(Spoilers, "armed", "not armed")}, lever {Lever?.ToString() ?? "unread"}, " +
            $"seat belts {Name(SeatBelts, "on", "off")}, no smoking {Name(NoSmoking, "on", "auto", "off")}";

        private static string Name(int? state, params string[] names) => state is { } i ? names[i] : "unread";
    }

    private static IEnumerable<Row> Grid()
    {
        // Every known combination: spoilers armed or not, the lever at 0-4, seat belts on or off, no smoking on, auto or off.
        for (int spoilers = 0; spoilers < 2; spoilers++)
            for (int lever = 0; lever <= 4; lever++)
                for (int belts = 0; belts < 2; belts++)
                    for (int smoking = 0; smoking < 3; smoking++)
                        yield return new Row(spoilers, lever, belts, smoking);

        // Each input unread in turn: with the others all right, and with the others all wrong (the check
        // is still unknown, not 0).
        Row ready = new(0, 2, 0, 1), wrong = new(1, 0, 1, 2);
        foreach (var others in new[] { ready, wrong })
        {
            yield return others with { Spoilers = null };
            yield return others with { Lever = null };
            yield return others with { SeatBelts = null };
            yield return others with { NoSmoking = null };
        }
    }

    private (string Key, double Value)[] Inputs(Row row)
    {
        var seed = new List<(string Key, double Value)>();
        if (row.Spoilers is { } spoilers) seed.Add((SpoilersVar, SpoilersRaw[spoilers]));
        if (row.Lever is { } lever) seed.Add((FlapLeverVar, lever));
        if (row.SeatBelts is { } belts) seed.Add((SeatBeltsVar, SeatBeltsRaw[belts]));
        if (row.NoSmoking is { } smoking) seed.Add((NoSmokingVar, NoSmokingRaw[smoking]));
        return seed.ToArray();
    }

    // What one Auto line says about the aircraft, from its own definition: null while any variable it
    // reads (its field and its additional fields) is unknown, else whether its own condition holds.
    private static bool? LineReads(ChecklistItem<TExec, TState> line, TState state)
    {
        double primary = state.GetValue(line.StateFieldName!);
        var extra = line.AdditionalStateFields.Select(state.GetValue).ToArray();
        if (double.IsNaN(primary) || extra.Any(double.IsNaN)) return null;
        return line.EvaluateState(primary) && extra.All(line.EvaluateAdditionalState);
    }

    [Fact]
    public void Taxi_memo_check_equals_the_AND_of_its_three_auto_lines_over_a_grid()
    {
        var items = BuildChecklists().SelectMany(g => g.Items).ToDictionary(i => i.Id);
        var lines = A320FamilyReadbackLinkTests.MemoLines.Select(id => items[id])
            .Where(i => i.IsAutoDetectable).ToList();
        Assert.Equal(new[] { "TXC_MEMO_FLAPS", "TXC_MEMO_SIGNS", "TXC_MEMO_SPLRS" },
            lines.Select(i => i.Id).Order(StringComparer.Ordinal).ToArray());
        Assert.False(items["TXC_FLAPS"].IsAutoDetectable);   // the fourth line is a Reminder: no condition

        int ones = 0, zeros = 0, unknowns = 0;
        foreach (var row in Grid())
        {
            var state = NewState();
            state.SetSimConnect(SeededSimConnectCache.With(Inputs(row)));

            var reads = lines.Select(line => LineReads(line, state)).ToList();
            double expected = reads.Contains(null) ? double.NaN : reads.All(r => r == true) ? 1.0 : 0.0;
            double actual = state.GetValue(A320FamilyReadbackLinkTests.MemoField);

            // double.Equals: NaN equals NaN, so one comparison covers the unknown rows too.
            Assert.True(expected.Equals(actual),
                $"{row}: the memo check reads {actual}, its three lines say {expected}");

            if (double.IsNaN(expected)) unknowns++;
            else if (expected > 0.5) ones++;
            else zeros++;
        }

        // The grid really exercises every answer.
        Assert.True(ones > 0 && zeros > 0 && unknowns > 0, $"1: {ones}, 0: {zeros}, unknown: {unknowns}");
    }
}

/// <summary>The Fenix A320 (see <see cref="A320FamilyTaxiMemoCheckTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyTaxiMemoCheckFenixTests
    : A320FamilyTaxiMemoCheckTests<FenixActionExecutor, FenixStateEvaluator>
{
    protected override FenixStateEvaluator NewState() => new();
    protected override List<ChecklistGroup<FenixActionExecutor, FenixStateEvaluator>> BuildChecklists() =>
        FenixChecklistDefinitions.Build();
    protected override string SpoilersVar => "A_FC_SPEEDBRAKE";
    protected override double[] SpoilersRaw => new[] { 0.0, 1.0 };         // 0 = ARMED
    protected override string FlapLeverVar => "S_FC_FLAPS";
    protected override string SeatBeltsVar => "S_OH_SIGNS";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string NoSmokingVar => "S_OH_SIGNS_SMOKING";
    protected override double[] NoSmokingRaw => new[] { 2.0, 1.0, 0.0 };   // 2 On / 1 Auto / 0 Off
}

/// <summary>The FlyByWire A32NX (see <see cref="A320FamilyTaxiMemoCheckTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyTaxiMemoCheckA32nxTests
    : A320FamilyTaxiMemoCheckTests<FbwA320ActionExecutor, FbwA320StateEvaluator>
{
    protected override FbwA320StateEvaluator NewState() => new();
    protected override List<ChecklistGroup<FbwA320ActionExecutor, FbwA320StateEvaluator>> BuildChecklists() =>
        FbwA320ChecklistDefinitions.Build();
    protected override string SpoilersVar => "A32NX_SPOILERS_ARMED";
    protected override double[] SpoilersRaw => new[] { 1.0, 0.0 };
    protected override string FlapLeverVar => "A32NX_FLAPS_HANDLE_INDEX";
    protected override string SeatBeltsVar => "CABIN SEATBELTS ALERT SWITCH";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string NoSmokingVar => "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION";
    protected override double[] NoSmokingRaw => new[] { 0.0, 1.0, 2.0 };   // 0 On / 1 Auto / 2 Off
}

/// <summary>The Headwind A330 (see <see cref="A320FamilyTaxiMemoCheckTests{TExec, TState}"/>).</summary>
public sealed class A320FamilyTaxiMemoCheckA330Tests
    : A320FamilyTaxiMemoCheckTests<HwA330ActionExecutor, HwA330StateEvaluator>
{
    protected override HwA330StateEvaluator NewState() => new();
    protected override List<ChecklistGroup<HwA330ActionExecutor, HwA330StateEvaluator>> BuildChecklists() =>
        HwA330ChecklistDefinitions.Build();
    protected override string SpoilersVar => "A32NX_SPOILERS_ARMED";
    protected override double[] SpoilersRaw => new[] { 1.0, 0.0 };
    protected override string FlapLeverVar => "A32NX_FLAPS_HANDLE_INDEX";
    protected override string SeatBeltsVar => "CABIN SEATBELTS ALERT SWITCH";
    protected override double[] SeatBeltsRaw => new[] { 1.0, 0.0 };
    protected override string NoSmokingVar => "XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION";
    protected override double[] NoSmokingRaw => new[] { 0.0, 1.0, 2.0 };   // 0 On / 1 Auto / 2 Off
}
