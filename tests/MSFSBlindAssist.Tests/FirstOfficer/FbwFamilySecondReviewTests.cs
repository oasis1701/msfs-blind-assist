// PR #160 second review (2026-10-18), the FlyByWire family's executors, the Headwind A330's nav and logo
// write, the A380's T.O CONFIG press and its Securing exterior lights. The auto-flap fixes are
// FbwFamilyAutoManagerSecondReviewTests.
//
// Held presses. FirstOfficerForm.Dispose detaches the executor (SetExecutorSimConnect(exec, null)), so an
// Aircraft-menu switch (the sim keeps the aircraft) or an app exit inside a hold left the release, which
// re-read the field, unsent: a continuous fire bell and master warning, a held T.O CONFIG key, an endless
// cabin chime. Each held press now captures its manager after its pacing wait (a detach there presses
// nothing) and releases through it.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwFamilySecondReviewTests
{
    // -----------------------------------------------------------------------
    // Held presses release through the manager they pressed through
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A32nx_fire_test_is_released_when_the_window_closes_mid_hold()
    {
        var definition = new RecordingFbwA320Definition();
        var executor = definition.Executor();

        var test = executor.FireTestAsync("FIRE_TEST_APU");
        var press = Assert.Single(definition.Writes);   // pressed at once; the 1.5 s hold runs
        executor.SetSimConnect(null);                    // FirstOfficerForm.Dispose, mid-hold

        Assert.True(await test);
        AssertPressedThenReleased(definition.Writes.Select(w => (w.Key, w.Value, w.Sim)),
            "A32NX_FIRE_TEST_APU", press.Sim);
    }

    [Fact]
    public async Task A330_fire_test_is_released_when_the_window_closes_mid_hold()
    {
        var definition = new RecordingHwA330Definition();
        var executor = definition.Executor();

        var test = executor.FireTestAsync("FIRE_TEST_ENG1");
        var press = Assert.Single(definition.Writes);
        executor.SetSimConnect(null);

        Assert.True(await test);
        AssertPressedThenReleased(definition.Writes.Select(w => (w.Key, w.Value, w.Sim)),
            "A32NX_FIRE_TEST_ENG1", press.Sim);
    }

    [Fact]
    public async Task A380_fire_test_is_released_when_the_window_closes_mid_hold()
    {
        var definition = new RecordingA380Definition();
        var executor = definition.Executor();

        var test = executor.FireTestAsync();
        Assert.Single(definition.Writes);   // pressed at once; the 3 s hold runs
        executor.SetSimConnect(null);

        Assert.True(await test);
        Assert.Equal(new[] { ("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 1.0), ("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 0.0) },
            definition.Writes);
    }

    [Fact]
    public async Task A32nx_cabin_call_is_released_when_the_window_closes_mid_hold()
    {
        var definition = new RecordingFbwA320Definition();
        var executor = definition.Executor();

        var call = executor.CabinCall();
        var press = Assert.Single(definition.Writes);   // a stuck 1 is an endless chime
        executor.SetSimConnect(null);

        Assert.True(await call);
        AssertPressedThenReleased(definition.Writes.Select(w => (w.Key, w.Value, w.Sim)),
            "PUSH_OVHD_CALLS_ALL", press.Sim);
    }

    [Fact]
    public async Task A330_cabin_call_is_released_when_the_window_closes_mid_hold()
    {
        var definition = new RecordingHwA330Definition();
        var executor = definition.Executor();

        var call = executor.CabinCall();
        var press = Assert.Single(definition.Writes);
        executor.SetSimConnect(null);

        Assert.True(await call);
        AssertPressedThenReleased(definition.Writes.Select(w => (w.Key, w.Value, w.Sim)),
            "PUSH_OVHD_CALLS_ALL", press.Sim);
    }

    private static void AssertPressedThenReleased(
        IEnumerable<(string Key, double Value, SimConnectManager? Sim)> writes,
        string key, SimConnectManager? pressedThrough)
    {
        Assert.NotNull(pressedThrough);
        Assert.Equal(new[] { (key, 1.0), (key, 0.0) }, writes.Select(w => (w.Key, w.Value)));
        // The release went through the manager the press went through, not the executor's field (null now).
        Assert.All(writes, w => Assert.Same(pressedThrough, w.Sim));
    }

    // A detach inside the pacing wait (the executor wrote less than 200 ms before) refuses the press: the
    // field was read again after the wait and dereferenced (a NullReferenceException for the T.O CONFIG
    // test), or the press went out with no manager to release it through. The pacing wait is real time; on
    // a machine that stalled past it before the detach the case never arose, and the test checks nothing.

    [Fact]
    public async Task A32nx_held_tests_detached_while_pacing_press_nothing()
    {
        foreach (var start in new Func<FbwA320ActionExecutor, Task<bool>>[]
                 { e => e.FireTestAsync("FIRE_TEST_APU"), e => e.CvrTestAsync(), e => e.TakeoffConfigTestAsync() })
        {
            var definition = new RecordingFbwA320Definition();
            var executor = definition.Executor();
            var clock = Stopwatch.StartNew();
            Assert.True(await executor.Set("A32NX_OVHD_INTLT_ANN", 1));   // the next write paces 200 ms

            var test = start(executor);
            executor.SetSimConnect(null);
            bool insideThePacingWait = clock.ElapsedMilliseconds < 150;
            bool pressed = await test;

            if (!insideThePacingWait) continue;
            Assert.False(pressed);
            Assert.Equal(new[] { "A32NX_OVHD_INTLT_ANN" }, definition.Writes.Select(w => w.Key));
        }
    }

    [Fact]
    public async Task A330_held_tests_detached_while_pacing_press_nothing()
    {
        foreach (var start in new Func<HwA330ActionExecutor, Task<bool>>[]
                 { e => e.FireTestAsync("FIRE_TEST_APU"), e => e.CvrTestAsync(), e => e.TakeoffConfigTestAsync() })
        {
            var definition = new RecordingHwA330Definition();
            var executor = definition.Executor();
            var clock = Stopwatch.StartNew();
            Assert.True(await executor.Set("A32NX_OVHD_INTLT_ANN", 1));

            var test = start(executor);
            executor.SetSimConnect(null);
            bool insideThePacingWait = clock.ElapsedMilliseconds < 150;
            bool pressed = await test;

            if (!insideThePacingWait) continue;
            Assert.False(pressed);
            Assert.Equal(new[] { "A32NX_OVHD_INTLT_ANN" }, definition.Writes.Select(w => w.Key));
        }
    }

    [Fact]
    public async Task A380_held_tests_detached_while_pacing_press_nothing()
    {
        foreach (var start in new Func<FbwA380ActionExecutor, Task<bool>>[]
                 { e => e.FireTestAsync(), e => e.TakeoffConfigTestAsync() })
        {
            var definition = new RecordingA380Definition();
            var executor = definition.Executor();
            var clock = Stopwatch.StartNew();
            Assert.True(await executor.Set("A32NX_OVHD_INTLT_ANN", 1));

            var test = start(executor);
            executor.SetSimConnect(null);
            bool insideThePacingWait = clock.ElapsedMilliseconds < 150;
            bool pressed = await test;

            if (!insideThePacingWait) continue;
            Assert.False(pressed);
            Assert.Equal(new[] { "A32NX_OVHD_INTLT_ANN" }, definition.Writes.Select(w => w.Key));
        }
    }

    /// <summary>The T.O CONFIG tests and the CVR test write where no recorder can see them (an H-event or a
    /// raw calc string), so the shape is pinned on the source of every held press: it captures its manager
    /// once, presses and releases through that capture, and never reads the field for either.</summary>
    [Theory]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs", "FireTestAsync")]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs", "CvrTestAsync")]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs", "TakeoffConfigTestAsync")]
    [InlineData("FBWA320", "FbwA320ActionExecutor.cs", "FireCabinCallAsync")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs", "FireTestAsync")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs", "CvrTestAsync")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs", "TakeoffConfigTestAsync")]
    [InlineData("HWA330", "HwA330ActionExecutor.cs", "FireCabinCallAsync")]
    [InlineData("FBWA380", "FbwA380ActionExecutor.cs", "FireTestAsync")]
    [InlineData("FBWA380", "FbwA380ActionExecutor.cs", "TakeoffConfigTestAsync")]
    public void Every_held_press_releases_through_the_manager_it_captured(string folder, string file, string method)
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(
            FoFbwUnclaimedEventKeyTests.ExecutorSourcePath(folder, file), method);

        Assert.Contains("var sc = _sc;", body);
        Assert.DoesNotMatch(@"\b_sc\s*[!.?]", body);   // no _sc.X, _sc!.X or _sc?.X: the field is checked, never used
        foreach (Match call in Regex.Matches(body, @"\bApplySilent\(([^;]*)\)\s*;"))
            Assert.EndsWith(", sc", call.Groups[1].Value);
    }

    // -----------------------------------------------------------------------
    // The A380 T.O CONFIG test presses on the app's one ECP press clock (A380-13)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A380_takeoff_config_test_reserves_the_shared_ecp_clock_for_its_whole_hold()
    {
        var executor = new RecordingA380Definition().Executor();

        var test = executor.TakeoffConfigTestAsync();
        // A panel or checklist-window ECP press now (they reserve the same clock) waits out the test's
        // 2 s hold and the release time after it, instead of merging with the held key.
        int wait = A380EcpKeyPulse.Shared.Reserve(Environment.TickCount64);

        Assert.True(wait > FbwA380ActionExecutor.ToConfigHoldMs - 500,
            $"An ECP press during the First Officer's T.O CONFIG hold would wait {wait} ms, not the hold.");
        Assert.True(await test);
    }

    // -----------------------------------------------------------------------
    // The Headwind A330's nav and logo write is unique per call (SIM-17)
    // -----------------------------------------------------------------------

    // The calc-event sequence ExecuteCalculatorCodeUnique numbers each command with, reached as
    // SeededSimConnectCache reaches the variable cache (a renamed field fails loudly here).
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "calcEventSeq")]
    private static extern ref long CalcEventSeq(SimConnectManager manager);

    /// <summary>The RPN is byte-identical per state (HwA330DivergenceTests pins it), and MobiFlight drops a
    /// command identical to the one before, so an ON after the switch went off in the cockpit did nothing:
    /// each write must be numbered, as every inherited A32NX exterior-light write is.</summary>
    [Fact]
    public void A330_nav_and_logo_write_is_numbered_so_a_repeat_is_never_dropped()
    {
        var definition = new HeadwindA330Definition();
        var sim = SeededSimConnectCache.ConnectedWith();
        var speech = new GatedSpeechCapture();
        long before = CalcEventSeq(sim);

        Assert.True(definition.ApplyUIVariable("A32NX_LIGHTS_NAV_LOGO", 1, sim, speech));
        Assert.True(definition.ApplyUIVariable("A32NX_LIGHTS_NAV_LOGO", 1, sim, speech));   // the same ON again

        Assert.Equal(before + 2, CalcEventSeq(sim));
    }

    // -----------------------------------------------------------------------
    // A380 Securing switches the runway turn-off lights off too
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0, 0, true)]
    [InlineData(0, 0, 1, false)]   // Lineup's runway turn-off lights, never switched off (After Takeoff skipped)
    [InlineData(1, 0, 0, false)]
    [InlineData(0, 1, 0, false)]
    public void A380_securing_exterior_lights_read_the_runway_turnoff_lights_too(
        double nav, double logo, double turnoff, bool allOff)
    {
        var state = new FbwA380StateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(
            ("LIGHT_NAV", nav), ("LIGHT_LOGO", logo), ("LIGHT_RWY_TURNOFF", turnoff)));

        // The flow's step is "Already set" only with all three off...
        var step = FbwA380FlowDefinitions.Build().Single(f => f.Id == "SECURE").Steps.Single(s => s.Id == "SC_EXTLT_OFF");
        Assert.Equal(allOff, step.SkipCondition!(state));

        // ...and the Securing line "Exterior lights: OFF" ticks itself only then.
        var groups = FbwA380ChecklistDefinitions.Build();
        var checklist = new ChecklistManager<FbwA380ActionExecutor, FbwA380StateEvaluator>(
            state, new FbwA380ActionExecutor(), groups);
        checklist.EvaluateAutoDetection();
        Assert.Equal(allOff, groups.SelectMany(g => g.Items).Single(i => i.Id == "SC_EXTLT_OFF").IsChecked);
    }

    [Fact]
    public async Task A380_securing_exterior_lights_hand_tick_switches_the_runway_turnoff_lights_off()
    {
        var line = FbwA380ChecklistDefinitions.Build().SelectMany(g => g.Items).Single(i => i.Id == "SC_EXTLT_OFF");
        var definition = new RecordingA380Definition();

        await line.CheckAction!(definition.Executor(), new FbwA380StateEvaluator());

        Assert.Equal(new[] { ("LIGHT_NAV", 0.0), ("LIGHT_LOGO", 0.0), ("LIGHT_RWY_TURNOFF", 0.0) }, definition.Writes);
    }
}
