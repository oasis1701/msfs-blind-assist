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
        "TCAS_TEST", "GPWS_TEST", "APU_START", "BARO_STD_BOTH", "PRESS_ALTS",
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

    // Fix pass 2 (2026-08): the BARO_STD_Status guard (and its pure classifier
    // ClassifyBaroStd, which these tests used to pin) is GONE. The field is MOMENTARY —
    // v1.5 SDK_Defines.h "0:switch released / 1:switch pressed", the cockpit STD button is a
    // momentary clickspot, and no persistent STD-mode variable exists in the model XML — so
    // it reads 0 essentially always: the guard pressed BOTH toggles on every push (taking an
    // already-standard side back to QNH) and then announced a false failure on every success.
    // Standard is now set BY VALUE through the stock Kohlsman, the same mechanism the Ctrl+B
    // altimeter dialog uses and has live-verified. The pure halves of that are pinned below.

    // The KOHLSMAN_SET parameter must be computed exactly the way Ctrl+B computes it:
    // inches → millibars (×33.8639) → ×16, rounded. 29.92 inHg = 1013.2079 mb → 16211.
    [Fact]
    public void StandardKohlsmanParameter_MatchesTheCtrlBDialogMath()
    {
        Assert.Equal(16211u, IFly737ActionExecutor.StandardKohlsmanParameter);
        Assert.Equal(16211u, IFly737ActionExecutor.KohlsmanParameterFor(29.92));
        // A hPa-side sanity check: 1013 mb is one whole millibar below standard, so it must
        // land on a DIFFERENT parameter — the ×16 scale is what makes the two distinguishable.
        Assert.NotEqual(IFly737ActionExecutor.StandardKohlsmanParameter,
                        (uint)Math.Round(1013.0 * 16));
    }

    // The skip-if-already-standard compare. An unreadable cache (null/NaN) is NOT standard —
    // the caller sends anyway, which is safe precisely because a value-set is idempotent.
    [Fact]
    public void IsStandardInHg_AcceptsOnlyStandardWithinTheKnobStep()
    {
        Assert.True(IFly737ActionExecutor.IsStandardInHg(29.92));
        Assert.True(IFly737ActionExecutor.IsStandardInHg(29.9245));  // inside half a 0.01 step
        Assert.True(IFly737ActionExecutor.IsStandardInHg(29.9155));
        Assert.False(IFly737ActionExecutor.IsStandardInHg(29.91));   // one full knob step off
        Assert.False(IFly737ActionExecutor.IsStandardInHg(29.93));
        Assert.False(IFly737ActionExecutor.IsStandardInHg(30.12));   // an ordinary local QNH
        Assert.False(IFly737ActionExecutor.IsStandardInHg(null));    // unreadable — not standard
        Assert.False(IFly737ActionExecutor.IsStandardInHg(double.NaN));
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

    // -----------------------------------------------------------------------
    // Whole-branch review fix pass — executor-only write-key totality.
    //
    // IFly737ProfileStructureTests.EverySetSwitchStep_Resolves pins every varKey that appears
    // as a literal SetSwitch/SetSwitchMultiple EventName in IFly737FlowDefinitions.cs. It does
    // NOT see a key that only ever reaches the SDK through one of this executor's typed public
    // methods (SetBattery, PressApuGenerator, ...) or one of its pseudo-key handler bodies
    // (FireTestCoreAsync, ClickAndSettleCoreAsync, ...) — several of which are called ONLY from
    // IFly737ChecklistDefinitions.cs's CheckAction lambdas, a surface no totality test walks
    // (lambdas aren't statically inspectable the way a flow step's EventName string is). A typo
    // in one of those literals fails exactly like every other mapping bug this suite exists to
    // catch: ApplySilent logs "no registered iFly control" and returns false, and the control
    // silently never moves — for BTN_FIRE_TEST_RELEASE specifically, that means the fire bell
    // the held OVHT/FIRE test just triggered never gets silenced.
    //
    // This is every string literal written from inside IFly737ActionExecutor.cs — both the
    // pseudo-key handler bodies and every typed public method's Set/MultiAsync call — checked
    // against IFly737MAXDefinition.HasWriteCommand (the same "really writable" check
    // EverySetSwitchStep_Resolves uses; a registered-but-read-only key would pass a bare
    // ContainsKey the same way Spoiler_Lever_Status once did there). Some of these ARE also
    // reachable via a flow step and so are already covered above — re-checking them here is
    // harmless and keeps this list a complete, self-contained inventory of the executor's write
    // surface rather than a hand-picked diff against the flow test.
    private static readonly string[] ExecutorWriteKeys =
    {
        // Pseudo-key handler bodies (FireTestCoreAsync, ClickAndSettleCoreAsync, and the two
        // ApplySilent-only TCAS/GPWS handlers).
        //
        // BTN_EFIS_CAPT_BARO_STD / BTN_EFIS_FO_BARO_STD were here until fix pass 2 (2026-08).
        // SetAltimetersStandardCoreAsync no longer presses them: BARO_STD_Status is momentary
        // and the command is a toggle, so standard is now set by VALUE via the stock
        // KOHLSMAN_SET (not an ApplyUIVariable key at all, hence nothing to pin here). The
        // definition still registers both buttons as the pilot's own manual STD controls —
        // they are simply no longer part of this executor's write surface.
        "BTN_FIRE_TEST_OVHT", "BTN_FIRE_TEST_RELEASE",
        "BTN_STALL_WARNING_TEST_1", "BTN_STALL_WARNING_TEST_2",
        "BTN_MACH_AIRSPEED_TEST_1", "BTN_MACH_AIRSPEED_TEST_2",
        "BTN_XPDR_TEST", "BTN_GPWS_SYS_TEST",

        // Typed public methods.
        "Battery_Switch_Mode",
        "STANDBY_POWER_Switch_Mode",
        "BTN_GRD_PWR_ON", "BTN_GRD_PWR_OFF",
        "BTN_GEN_1_ON", "BTN_GEN_1_OFF", "BTN_GEN_2_ON", "BTN_GEN_2_OFF",
        "BTN_APU_GEN_1_ON", "BTN_APU_GEN_1_OFF", "BTN_APU_GEN_2_ON", "BTN_APU_GEN_2_OFF",
        "APU_Switch_Status",
        "Fuel_L_AFT_Switch_Status", "Fuel_L_FWD_Switch_Status",
        "Fuel_R_FWD_Switch_Status", "Fuel_R_AFT_Switch_Status",
        "Fuel_CENTER_L_Switch_Status", "Fuel_CENTER_R_Switch_Status",
        "Fuel_Crossfeed_Selector_Status",
        "Pack_Switch_Status_0", "Pack_Switch_Status_1",
        "Engine_Bleed_Air_Switch_Status_0", "Engine_Bleed_Air_Switch_Status_1",
        "APU_Bleed_Air_Switch_Status",
        "Isolation_Valve_Switch_Status",
        "Trim_Air_Switch_Status",
        "RecircFan_Switch_Status_0", "RecircFan_Switch_Status_1",
        "Window_Heat_Switch_1_Status", "Window_Heat_Switch_2_Status",
        "Window_Heat_Switch_3_Status", "Window_Heat_Switch_4_Status",
        "Probe_Heat_Switch_1_Status", "Probe_Heat_Switch_2_Status",
        "Wing_AntiIce_Switch_Status",
        "Eng_1_AntiIce_Switch_Status", "Eng_2_AntiIce_Switch_Status",
        "ENG_1_HYD_Switch_Status", "ENG_2_HYD_Switch_Status",
        "ELEC_1_HYD_Switch_Status", "ELEC_2_HYD_Switch_Status",
        "Anti_Collision_Light_Switch_Status",
        "Position_Light_Switch_Status",
        "Logo_Light_Switch_Status",
        "Wing_Light_Switch_Status",
        "Taxi_Light_Switch_Status",
        "Runway_Turnoff_Light_1_Switch_Status", "Runway_Turnoff_Light_2_Switch_Status",
        "Landing_Light_1_Switch_Status", "Landing_Light_2_Switch_Status",
        "Fasten_Belts_Switch_Status",
        "No_Smoking_Switch_Status",
        "Emergency_Light_Switch_Status",
        "Ignition_Select_Switch_Status",
        "Engine_Start_Switch_Status_0", "Engine_Start_Switch_Status_1",
        "Engine_Start_Lever_Status_0", "Engine_Start_Lever_Status_1",
        "IRS_Mode_Switch_Status_0", "IRS_Mode_Switch_Status_1",
        "FD_1_Switch_Status", "FD_2_Switch_Status",
        "AT_Switch_Status",
        "LNAV_Switch_Status",
        "VNAV_Switch_Status",
        "Transponder_Mode_Switch_Status",
        "Autobrake_Selector_Status",
        "Gear_Lever_Status",
        "ND_Mode_Status_0",
        "ND_Range_Status_0",
        "BTN_ATTENDANT_CALL",
        "BTN_SIX_PACK_RECALL",

        // Written directly by a checklist action via the generic Set() path (not a typed
        // wrapper — see PF_YD/AL_FLAPS's own comments), but still part of the executor's
        // write surface and named explicitly by the review.
        "FLAP_Status",
    };

    [Fact]
    public void ExecutorWriteKeys_AreAllRealWritableSdkControls()
    {
        var def = new MSFSBlindAssist.Aircraft.IFly737MAXDefinition();
        var vars = def.GetVariables();

        Assert.True(ExecutorWriteKeys.Length > 60,
            $"only {ExecutorWriteKeys.Length} executor write keys listed — expected well over " +
            "60; this list should be a near-complete inventory of the executor's write surface");
        Assert.Equal(ExecutorWriteKeys.Length, ExecutorWriteKeys.Distinct().Count());

        foreach (string key in ExecutorWriteKeys)
        {
            Assert.True(vars.ContainsKey(key) && def.HasWriteCommand(key),
                $"'{key}' is not a registered, writable iFly control — a flow/checklist action " +
                "naming it would silently do nothing in the sim.");
        }
    }
}
