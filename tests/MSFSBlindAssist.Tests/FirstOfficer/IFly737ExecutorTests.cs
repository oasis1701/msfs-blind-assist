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

    // -----------------------------------------------------------------------
    // Fix pass 1 (post-review hardening) — see .superpowers/sdd/task-4-report.md,
    // "## Fix pass 1" for the write-up.
    // -----------------------------------------------------------------------

    // Fix 1: a combo write must be range-checked against the DEFINITION's own declared
    // positions for that key before it can reach the SDK. The gear lever is the concrete
    // hazard the review named: this airframe registers only two positions (0 Up / 1 Down,
    // no OFF detent — the sibling PMDG 737 has three), so a later author writing
    // SetGearLever(2) (a plausible copy of the PMDG's numbering) must be refused rather
    // than sending an undefined Value2 to the SDK. IsDeclaredPosition only needs
    // SetDefinition to have been called — not the full IsAvailable=true state a unit test
    // can't reach (ScreenReaderAnnouncer has no parameterless ctor) — so this exercises the
    // exact guard ApplySilent runs, with no live SDK involved.
    [Fact]
    public void IsDeclaredPosition_RejectsOutOfRangeGearPosition()
    {
        var exec = new IFly737ActionExecutor();
        exec.SetDefinition(new MSFSBlindAssist.Aircraft.IFly737MAXDefinition());

        // Every legal position (Gear_Lever_Status declares exactly {0, 1} — ForwardPedestal.cs:24).
        Assert.True(exec.IsDeclaredPosition("Gear_Lever_Status", 0));
        Assert.True(exec.IsDeclaredPosition("Gear_Lever_Status", 1));

        // 2 = the PMDG's OFF detent, which this airframe does not have — must be refused.
        Assert.False(exec.IsDeclaredPosition("Gear_Lever_Status", 2));
        Assert.False(exec.IsDeclaredPosition("Gear_Lever_Status", -1));
    }

    // A key with NO declared positions (a plain momentary button — ValueDescriptions is an
    // empty dictionary) must never be refused by the range check; there is nothing to
    // range-check, and the momentary-button/NumSet/display paths must stay exactly as
    // permissive as before this fix.
    [Fact]
    public void IsDeclaredPosition_NoDeclaredPositions_AlwaysAccepts()
    {
        var exec = new IFly737ActionExecutor();
        exec.SetDefinition(new MSFSBlindAssist.Aircraft.IFly737MAXDefinition());

        Assert.True(exec.IsDeclaredPosition("BTN_ATTENDANT_CALL", 1));
        Assert.True(exec.IsDeclaredPosition("BTN_ATTENDANT_CALL", 42)); // still nothing to check against
    }

    // An unrecognised key is also accepted by the range check itself — ApplyUIVariable's
    // own "key not registered" branch is what refuses that case, not this guard.
    [Fact]
    public void IsDeclaredPosition_UnknownKey_Accepts()
    {
        var exec = new IFly737ActionExecutor();
        exec.SetDefinition(new MSFSBlindAssist.Aircraft.IFly737MAXDefinition());
        Assert.True(exec.IsDeclaredPosition("NO_SUCH_KEY", 999));
    }

    // Fix 2: PseudoKeys is now DERIVED from the handler map, so every declared pseudo-key
    // must resolve to a handler — the gap the review found (a key declared without a
    // matching switch arm would silently fall through to the ordinary write path and
    // return false, while PseudoKeys_AreDeclared kept passing) can no longer exist by
    // construction, and this pins that invariant rather than assuming it.
    [Fact]
    public void EveryDeclaredPseudoKey_HasHandler()
    {
        foreach (string key in Expected)
            Assert.True(IFly737ActionExecutor.HasPseudoKeyHandler(key), key);
        // And the converse holds too: nothing claims to be a pseudo-key without also being
        // in the declared list (they are now the same set by construction).
        Assert.Equal(IFly737ActionExecutor.PseudoKeys.Count,
            IFly737ActionExecutor.PseudoKeys.Count(IFly737ActionExecutor.HasPseudoKeyHandler));
    }

    // Fix 3: IsBaroStd used to collapse "unreadable" (NaN) and "genuinely on QNH" (false)
    // into the same bool, so a side that merely went unreadable right as the post-write
    // verify ran got the same "did not set" wording as a side that truly stayed on QNH.
    // ClassifyBaroStd is the extracted pure classifier that tells the two apart.
    [Fact]
    public void ClassifyBaroStd_DistinguishesUnreadableFromQnhFromStd()
    {
        Assert.Null(IFly737ActionExecutor.ClassifyBaroStd(double.NaN));   // unreadable — indeterminate
        Assert.False(IFly737ActionExecutor.ClassifyBaroStd(0));          // genuine QNH reading
        Assert.True(IFly737ActionExecutor.ClassifyBaroStd(1));           // genuine STD reading
    }

    // Fix 4: PressGenerator / PressApuGenerator used to interpolate an unchecked index
    // straight into a synthesized key ("BTN_GEN_3_ON") — it failed safe (ApplySilent's
    // "not registered" branch) but the log named the synthesized key, not the caller's
    // actual mistake. IsValidGeneratorIndex is the extracted boundary both methods now
    // check before building that key at all.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void IsValidGeneratorIndex_AcceptsTheTwoRealEngines(int n) =>
        Assert.True(IFly737ActionExecutor.IsValidGeneratorIndex(n));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void IsValidGeneratorIndex_RejectsAnythingElse(int n) =>
        Assert.False(IFly737ActionExecutor.IsValidGeneratorIndex(n));

    // End-to-end on an unavailable executor: an invalid index must still refuse (same
    // outward contract as every other typed method on this executor), and must not throw
    // building the bogus key string.
    [Fact]
    public async Task PressGenerator_InvalidIndex_ReturnsFalseWithoutThrowing()
    {
        var exec = new IFly737ActionExecutor();
        Assert.False(await exec.PressGenerator(3, true));
        Assert.False(await exec.PressApuGenerator(0, false));
    }
}
