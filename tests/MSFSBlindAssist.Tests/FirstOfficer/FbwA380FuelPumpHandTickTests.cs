using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The two A380 action lines that switch the fuel pumps when the pilot ticks them by hand, Before Start
/// "Fuel pumps: ON" and Parking "Fuel pumps: OFF", write the pumps by literal key: the write-key
/// registration scan (<see cref="FbwA380FoKeyRegistrationTests"/>) reads those literals from the source, so
/// they stay literals. Nothing but this test ties those two lists of 20 to
/// <see cref="FbwA380ChecklistDefinitions.FuelPumps"/>, the list the three pump lines detect and the flows
/// switch (<see cref="FbwA380ReadbackTests"/> pins both): a pump added to or dropped from one hand-tick
/// alone would leave a pump the line reads but never switches, or switch one no line reads. This runs each
/// line's own <c>CheckAction</c>, the code a hand-tick runs, against an executor whose definition records
/// what it is asked to write.
/// </summary>
public sealed class FbwA380FuelPumpHandTickTests
{
    /// <summary>
    /// A definition that records every write the executor asks of it instead of sending it, and reports
    /// success. The executor is sealed and its <c>Set</c> is not virtual, so the recorder sits one step
    /// down: <c>ApplyUIVariable</c> (what every plain-key write goes through) calls
    /// <c>HandleUIVariableSet</c>, which is virtual.
    /// </summary>
    private sealed class RecordingA380Definition : FlyByWireA380Definition
    {
        private readonly object _gate = new();
        private readonly List<(string Key, double Value)> _writes = new();

        public (string Key, double Value)[] Writes
        {
            get { lock (_gate) return _writes.ToArray(); }
        }

        public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
            SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        {
            lock (_gate) _writes.Add((varKey, value));
            return true;
        }
    }

    /// <summary>What the line's hand-tick action writes, in order. The executor reads as connected (its
    /// sim is a manager that never opened a handle), so its dispatch runs; its definition records instead
    /// of sending.</summary>
    private static async Task<(string Key, double Value)[]> HandTickWrites(string lineId)
    {
        var line = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == lineId);
        Assert.NotNull(line.CheckAction);

        var definition = new RecordingA380Definition();
        var executor = new FbwA380ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        executor.SetDefinition(definition);
        executor.SetAnnouncer(new GatedSpeechCapture());

        await line.CheckAction!(executor, new FbwA380StateEvaluator());
        return definition.Writes;
    }

    /// <summary>Everything wrong with what the line wrote: it must write every pump of
    /// <see cref="FbwA380ChecklistDefinitions.FuelPumps"/>, once each and nothing else, all to
    /// <paramref name="value"/>. Empty when the hand-tick is right.</summary>
    private static List<string> Problems(string lineId, (string Key, double Value)[] writes, double value)
    {
        var problems = new List<string>();
        var pumps = FbwA380ChecklistDefinitions.FuelPumps;
        if (pumps.Count == 0)
            problems.Add("FuelPumps is empty, so there is nothing to compare the hand-ticks with");
        if (writes.Length == 0)
        {
            problems.Add($"{lineId}'s hand-tick wrote nothing: the recorder never saw the action");
            return problems;
        }

        var written = writes.Select(w => w.Key).ToList();
        var missing = pumps.Except(written).ToList();
        var notPumps = written.Except(pumps).ToList();
        var twice = written.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (missing.Count > 0)
            problems.Add($"{lineId} never writes [{string.Join(", ", missing)}], which FuelPumps lists");
        if (notPumps.Count > 0)
            problems.Add($"{lineId} writes [{string.Join(", ", notPumps)}], which FuelPumps does not list");
        if (twice.Count > 0)
            problems.Add($"{lineId} writes [{string.Join(", ", twice)}] more than once");

        var wrongValue = writes.Where(w => w.Value != value).Select(w => $"{w.Key} = {w.Value}").ToList();
        if (wrongValue.Count > 0)
            problems.Add($"{lineId} must write every pump to {value}, but writes [{string.Join(", ", wrongValue)}]");
        return problems;
    }

    /// <summary>Before Start switches all 20 pumps ON (1) and Parking all 20 OFF (0). The executor spaces
    /// its writes 200 ms apart (its own <c>WriteSpacingMs</c>), about 4 s a line, so the two lines run side
    /// by side, each on its own executor.</summary>
    [Fact]
    public async Task The_before_start_and_parking_hand_ticks_switch_exactly_the_fuel_pumps_on_and_off()
    {
        var beforeStart = HandTickWrites("BS_FUELPUMPS");
        var parking = HandTickWrites("PK_FUELPUMPS_OFF");

        var problems = Problems("BS_FUELPUMPS", await beforeStart, value: 1)
            .Concat(Problems("PK_FUELPUMPS_OFF", await parking, value: 0))
            .ToList();

        Assert.True(problems.Count == 0,
            "A hand-tick of a fuel-pump line must write exactly the pumps in FbwA380ChecklistDefinitions.FuelPumps: "
            + string.Join("; ", problems));
    }
}
