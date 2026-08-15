// Surface tests for IFly737ActionExecutor — the WRITE side of the iFly 737 MAX8 FO profile.
// See .superpowers/sdd/task-4-brief.md. Almost nothing here can be exercised sim-less: every
// real write goes through IFly737MAXDefinition.ApplyUIVariable -> the SDK client -> a
// WM_COPYDATA send to the live iFly plugin. What IS testable — and what Task 6's totality test
// depends on — is the declared pseudo-key surface and the refuse-cleanly-when-unavailable
// contract, so that is what these pin.

namespace MSFSBlindAssist.Tests.FirstOfficer;

using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.IFly737;
using MSFSBlindAssist.FirstOfficer.Models;

public class IFly737ExecutorTests
{
    private sealed class Step : IFlowStepDispatch
    {
        public FlowStepActionType ActionType { get; init; }
        public string? EventName { get; init; }
        public int? TargetValue { get; init; }
        public IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; init; }
            = Array.Empty<(string, int?)>();
        public bool UsesMouseFlag => false;
        public bool IsMomentary => false;
    }

    // Every pseudo-key the flows/checklists (Task 5/6) may name. Spelled out here rather than
    // read back from the executor so a silent rename or a dropped key FAILS instead of
    // re-asserting whatever the production list happens to say. WXR_TEST is deliberately
    // absent: the iFly SDK has no weather-radar TEST command (adaptation table, last row).
    private static readonly string[] Expected =
    {
        "FIRE_TEST", "STALL_TEST_1", "STALL_TEST_2", "OVSPD_TEST_1", "OVSPD_TEST_2",
        "TCAS_TEST", "GPWS_TEST", "APU_START", "BARO_STD_BOTH",
    };

    [Fact]
    public void PseudoKeys_AreDeclared()
    {
        var declared = IFly737ActionExecutor.PseudoKeys;
        Assert.Equal(Expected.Length, declared.Count);
        foreach (string key in Expected)
            Assert.Contains(key, declared);
        // No duplicates — a duplicated entry would make a totality check pass on a key the
        // dispatch switch never actually handles.
        Assert.Equal(declared.Count, declared.Distinct().Count());
        // A pseudo-key must never collide with a real SDK field name (those are the keys the
        // default branch resolves against the definition).
        Assert.DoesNotContain(declared, k => k.Contains("_Status", StringComparison.Ordinal));
    }

    [Fact]
    public void PseudoKeys_AreRecognisedByIsPseudoKey()
    {
        foreach (string key in Expected)
            Assert.True(IFly737ActionExecutor.IsPseudoKey(key), key);
        Assert.False(IFly737ActionExecutor.IsPseudoKey("WXR_TEST"));
        Assert.False(IFly737ActionExecutor.IsPseudoKey("Fuel_CENTER_L_Switch_Status"));
    }

    // An executor with no SimConnect/definition/announcer (the state before the profile wires
    // it, and the state after a sim disconnect) must REFUSE a step, not throw: an escaped
    // exception here would surface as an unobserved task fault inside a flow, with the step
    // silently never completing.
    [Fact]
    public async Task ExecuteStep_NotAvailable_ReturnsFalse()
    {
        var exec = new IFly737ActionExecutor();
        Assert.False(exec.IsAvailable);

        Assert.False(await exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitch,
            EventName = "Fuel_L_FWD_Switch_Status",
            TargetValue = 1,
        }));

        // Multi with an EMPTY action list must still refuse — an ok-conjunction over zero
        // actions is vacuously true, so the availability guard has to sit above the loop.
        Assert.False(await exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
        }));

        // Pseudo-keys take sequenced/held paths of their own — they must refuse too, and in
        // particular must not start a hold they cannot release.
        foreach (string key in Expected)
            Assert.False(await exec.ExecuteStepAsync(new Step
            {
                ActionType = FlowStepActionType.SetSwitch,
                EventName = key,
            }), key);
    }

    [Fact]
    public async Task ExecuteStep_UnknownActionType_ReturnsFalse()
    {
        var exec = new IFly737ActionExecutor();
        Assert.False(await exec.ExecuteStepAsync(new Step { ActionType = FlowStepActionType.WaitSeconds }));
    }

    // ChecklistManager holds the manual-tick revert grace open on this; if it can't complete
    // on an idle executor the grace never re-stamps and a just-ticked item reverts under the
    // pilot. Bounded so a regression to a permanently-held gate fails instead of hanging CI.
    [Fact]
    public async Task DrainCompletesWhenIdle()
    {
        var exec = new IFly737ActionExecutor();
        var drain = exec.WaitForDispatchDrainAsync();
        Assert.Same(drain, await Task.WhenAny(drain, Task.Delay(5000)));
        await drain;

        // Re-entrant after a refused dispatch — a guard that returned early WITHOUT releasing
        // the gate would deadlock every later write on the profile.
        await exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitch,
            EventName = "Fuel_L_FWD_Switch_Status",
            TargetValue = 1,
        });
        var again = exec.WaitForDispatchDrainAsync();
        Assert.Same(again, await Task.WhenAny(again, Task.Delay(5000)));
        await again;
    }

    // The typed methods are the surface Tasks 5-7 call. On an unavailable executor each must
    // report failure rather than claiming a switch moved (and must not throw).
    [Fact]
    public async Task TypedMethods_NotAvailable_ReturnFalse()
    {
        var exec = new IFly737ActionExecutor();
        Assert.False(await exec.Set("Fuel_L_FWD_Switch_Status", 1));
        Assert.False(await exec.SetBattery(true));
        Assert.False(await exec.SetCenterFuelPumps(1));
        Assert.False(await exec.SetWingFuelPumps(1));
        Assert.False(await exec.SetAltimetersStandardAsync());
        Assert.False(await exec.SetPressurizationAltitudesAsync(new IFly737StateEvaluator()));
        Assert.False(await exec.CabinCall());
    }
}
