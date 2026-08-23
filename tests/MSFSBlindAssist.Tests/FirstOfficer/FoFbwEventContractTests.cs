// The First Officer executors' INPUT-EVENT contract with the FlyByWire builds.
//
// FlyByWireA380EventContractTests pins the DEFINITION's event contract by reflecting over
// GetVariables(). That reflection cannot see an event name the First Officer fires as a bare
// string literal through FireFCUButton(...), so the FO executors sit entirely outside it.
//
// That gap is not hypothetical. FBW #10855 renamed A32NX.FCU_TO_AP_HDG_PUSH to
// A32NX.FCU_HDG_PUSH. The A380 definition was migrated; FbwA380ActionExecutor was not, because
// it lives in a directory the migration never touched, so git merged it clean. A K-event nobody
// registered is silently swallowed by the sim: no error, no log line, no wrong value — the
// FO's "FCU heading: managed" step simply stops doing anything, which for a blind pilot is
// indistinguishable from the FO having skipped the step.
//
// So: every dotted FBW event an FO executor can fire must be an event its own aircraft
// definition registers. The definition is the only thing that maps a dotted name onto a
// transport, so an unregistered name cannot reach the aircraft by any path.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FoFbwEventContractTests
{
    private static HashSet<string> RegisteredEvents(IAircraftDefinition def) =>
        def.GetVariables()
            .Where(kv => kv.Value.Type == SimVarType.Event)
            .Select(kv => kv.Value.Name)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void A380_first_officer_fires_only_events_the_a380_definition_registers()
    {
        var registered = RegisteredEvents(new FlyByWireA380Definition());

        var unregistered = FbwA380ActionExecutor.FiredEventNames
            .Where(e => !registered.Contains(e))
            .Order()
            .ToList();

        Assert.True(unregistered.Count == 0,
            "A380 First Officer fires events the A380 definition does not register — these are "
            + "silent no-ops in the sim: " + string.Join(", ", unregistered));
    }

    [Fact]
    public void A320_first_officer_fires_only_events_the_a320_definition_registers()
    {
        var registered = RegisteredEvents(new FlyByWireA320Definition());

        var unregistered = FbwA320ActionExecutor.FiredEventNames
            .Where(e => !registered.Contains(e))
            .Order()
            .ToList();

        Assert.True(unregistered.Count == 0,
            "A320 First Officer fires events the A320 definition does not register — these are "
            + "silent no-ops in the sim: " + string.Join(", ", unregistered));
    }
}
