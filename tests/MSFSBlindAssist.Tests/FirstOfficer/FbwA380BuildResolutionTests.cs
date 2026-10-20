using System;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A380 First Officer on BOTH installed FlyByWire builds (FS2020 fs2020-master 2020.16.0, FS2024
/// 2024.2.0): every build-dependent name resolves to the one that build's own files use
/// (<see cref="A380BuildNames"/>), an unknown simulator resolves to nothing and writes nothing, and no
/// flight-director toggle goes out on an unread state.
/// </summary>
public class FbwA380BuildResolutionTests
{
    private static Func<string, double> Values(params (string Key, double Value)[] values)
    {
        var d = values.ToDictionary(v => v.Key, v => v.Value);
        return k => d.TryGetValue(k, out var v) ? v : double.NaN;
    }

    private const string Fd1 = A380BuildNames.Fs2020FdCaptKey, Fd2 = A380BuildNames.Fs2020FdFoKey;
    private const string Light = A380BuildNames.Fs2024FdLightKey, Prim = A380BuildNames.Fs2024PrimFgWord1Key;

    [Theory]
    [InlineData("FS2020", FbwSimBuild.Fs2020)]
    [InlineData("FS2024", FbwSimBuild.Fs2024)]
    [InlineData("Unknown", FbwSimBuild.Unknown)]
    [InlineData(null, FbwSimBuild.Unknown)]
    [InlineData("", FbwSimBuild.Unknown)]
    public void The_build_comes_from_the_detected_simulator(string? sim, FbwSimBuild expected) =>
        Assert.Equal(expected, FbwSimBuilds.From(sim));

    [Fact]
    public void The_build_is_read_live_so_a_reconnect_to_the_other_simulator_re_resolves()
    {
        var sc = SeededSimConnectCache.ConnectedWith().On("FS2020");
        Assert.Equal(FbwSimBuild.Fs2020, FbwSimBuilds.From(sc));
        sc.On("FS2024");
        Assert.Equal(FbwSimBuild.Fs2024, FbwSimBuilds.From(sc));
        Assert.Equal(FbwSimBuild.Unknown, FbwSimBuilds.From((SimConnectManager?)null));
    }

    // ---------------------------------------------------------------- names per build

    [Fact]
    public void Each_build_names_its_own_variables()
    {
        // FS2020: ONE selector, HPA_1 — both cockpit selectors and both BaroManagers use it.
        Assert.Equal(new[] { "XMLVAR_Baro_Selector_HPA_1" }, A380BuildNames.BaroUnitKeys(FbwSimBuild.Fs2020));
        Assert.Equal(new[] { "A32NX_FCU_EFIS_L_BARO_IS_INHG", "A32NX_FCU_EFIS_R_BARO_IS_INHG" }, A380BuildNames.BaroUnitKeys(FbwSimBuild.Fs2024));
        Assert.Empty(A380BuildNames.BaroUnitKeys(FbwSimBuild.Unknown));
        Assert.Equal(1, A380BuildNames.HectopascalsValue(FbwSimBuild.Fs2020));
        Assert.Equal(0, A380BuildNames.HectopascalsValue(FbwSimBuild.Fs2024));

        Assert.Equal(A380BuildNames.Fs2020BaroStdCaptKey, A380BuildNames.BaroStdKey(FbwSimBuild.Fs2020, 1));
        Assert.Equal(A380BuildNames.Fs2020BaroStdFoKey, A380BuildNames.BaroStdKey(FbwSimBuild.Fs2020, 2));
        Assert.Equal("A32NX_FCU_LEFT_EIS_BARO_IS_STD", A380BuildNames.BaroStdKey(FbwSimBuild.Fs2024, 1));
        Assert.Equal("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", A380BuildNames.BaroStdKey(FbwSimBuild.Fs2024, 2));
        Assert.Null(A380BuildNames.BaroStdKey(FbwSimBuild.Unknown, 1));

        Assert.Equal("H:A380X_EFIS_CP_BARO_PUSH_1", A380BuildNames.Fs2020BaroModeEvent(1, true));
        Assert.Equal("H:A380X_EFIS_CP_BARO_PULL_2", A380BuildNames.Fs2020BaroModeEvent(2, false));

        Assert.Equal("A32NX_SPEEDS_LANDING_CONF3", A380BuildNames.LandingConf3Key(FbwSimBuild.Fs2020));
        Assert.Equal("A380X_FM_LANDING_CONF3", A380BuildNames.LandingConf3Key(FbwSimBuild.Fs2024));
        Assert.Null(A380BuildNames.LandingConf3Key(FbwSimBuild.Unknown));
    }

    [Fact]
    public void Every_per_build_variable_is_registered_on_request_and_silent()
    {
        var vars = new FlyByWireA380Definition().GetVariables();
        foreach (var k in A380BuildNames.AllStateKeys)
            Assert.True(vars.ContainsKey(k), k + " is not registered");

        // The FS2020 ones are the First Officer's own: the stock state that build's FCU writes,
        // never announced, never a panel row.
        Assert.Equal("AUTOPILOT FLIGHT DIRECTOR ACTIVE:1", vars[Fd1].Name);
        Assert.Equal("AUTOPILOT FLIGHT DIRECTOR ACTIVE:2", vars[Fd2].Name);
        Assert.Equal("KOHLSMAN SETTING STD:1", vars[A380BuildNames.Fs2020BaroStdCaptKey].Name);
        Assert.Equal("KOHLSMAN SETTING STD:2", vars[A380BuildNames.Fs2020BaroStdFoKey].Name);
        Assert.Equal("XMLVAR_Baro_Selector_HPA_1", vars[A380BuildNames.Fs2020BaroUnitKey].Name);
        Assert.Equal("A32NX_SPEEDS_LANDING_CONF3", vars[A380BuildNames.Fs2020LandingConf3Key].Name);
        foreach (var (key, _) in A380BuildNames.Fs2020FoRegistrations())
        {
            var d = vars[key];
            Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
            Assert.False(d.IsAnnounced, key);
            Assert.True(d.ExcludeFromMonitorManager, key);
            // A stock SimVar (space or colon) is never registered as an L:var, and an L:var never as one.
            bool stockShape = d.Name.Contains(' ') || d.Name.Contains(':');
            Assert.Equal(stockShape ? SimVarType.SimVar : SimVarType.LVar, d.Type);
        }
        // The FS2024 ones are the definition's own panel keys.
        Assert.Equal("A32NX_FCU_EFIS_L_DISPLAY_BARO_IS_STD", vars["A32NX_FCU_LEFT_EIS_BARO_IS_STD"].Name);
        Assert.Equal(A380FlightDirector.PrimFgWord1, vars[Prim].Name);
        Assert.Equal("A32NX_FCU_FD_LIGHT_ON", vars[Light].Name);
    }

    [Fact]
    public void The_fo_polls_both_builds_state()
    {
        var poll = new FbwA380StateEvaluator().OnRequestPollFields;
        foreach (var k in A380BuildNames.AllStateKeys) Assert.Contains(k, poll);
    }

    // ---------------------------------------------------------------- flight directors

    [Theory]
    [InlineData(1, 1, 1.0)]
    [InlineData(1, 0, 0.0)]
    [InlineData(0, 1, 0.0)]
    [InlineData(0, 0, 0.0)]
    public void Fs2020_flight_directors_are_on_only_when_both_sides_are(double capt, double fo, double expected) =>
        Assert.Equal(expected, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2020, Values((Fd1, capt), (Fd2, fo))));

    [Fact]
    public void Flight_director_state_is_unknown_when_unread_or_the_build_is_unknown()
    {
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2020, Values((Fd1, 1)))));
        // One side read OFF is enough to say "not on".
        Assert.Equal(0.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2020, Values((Fd1, 0))));
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values())));
        // The FS2024 light means nothing on FS2020, and the FS2020 stock vars nothing on FS2024.
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2020, Values((Light, 1)))));
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values((Fd1, 1), (Fd2, 1)))));
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Unknown, Values((Fd1, 1), (Fd2, 1), (Light, 1)))));
        // No PRIM word: the light decides.
        Assert.Equal(1.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values((Light, 1))));
        Assert.Equal(0.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values((Light, 0))));
    }

    // PRIM FG discrete word 1: SSM 0b11 = normal operation, bit 13 = FD 1 engaged, bit 14 = FD 2.
    private static double PrimWord(bool fd1, bool fd2, uint ssm = 0b11) =>
        FcuValuePhrasesTests.Word(ssm, (fd1 ? 1u << 12 : 0) | (fd2 ? 1u << 13 : 0));

    [Theory]
    [InlineData(true, true, 1.0)]
    [InlineData(true, false, 0.0)]
    [InlineData(false, true, 0.0)]
    [InlineData(false, false, 0.0)]
    public void Fs2024_both_on_is_both_prim_engaged_bits_whatever_the_light_says(bool fd1, bool fd2, double expected) =>
        Assert.Equal(expected, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024,
            Values((Prim, PrimWord(fd1, fd2)), (Light, 1))));

    [Fact]
    public void Fs2024_falls_back_to_the_light_only_when_the_prim_word_has_no_data()
    {
        Assert.Equal(1.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024,
            Values((Prim, PrimWord(false, false, ssm: 0)), (Light, 1))));
        // A never-written L:var reads 0, which is no data too.
        Assert.Equal(0.0, A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values((Prim, 0), (Light, 0))));
        Assert.True(double.IsNaN(A380BuildNames.FlightDirectorsOn(FbwSimBuild.Fs2024, Values((Prim, 0)))));
    }

    [Fact]
    public void Fs2020_presses_each_side_that_differs_and_only_that_side()
    {
        var p = A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values((Fd1, 0), (Fd2, 1)))!;
        Assert.Equal(new[] { new A380BuildNames.FdPress("TOGGLE_FLIGHT_DIRECTOR", 1) }, p);
        p = A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values((Fd1, 1), (Fd2, 0)))!;
        Assert.Equal(new[] { new A380BuildNames.FdPress("TOGGLE_FLIGHT_DIRECTOR", 2) }, p);
        Assert.Equal(2, A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values((Fd1, 0), (Fd2, 0)))!.Count);
        Assert.Empty(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values((Fd1, 1), (Fd2, 1)))!);
        Assert.Equal("2 (>K:TOGGLE_FLIGHT_DIRECTOR)",
            A380BuildNames.PressCalculatorCode(new A380BuildNames.FdPress("TOGGLE_FLIGHT_DIRECTOR", 2)));
    }

    [Fact]
    public void Fs2024_presses_the_one_fd_button_only_when_its_light_differs()
    {
        Assert.Equal(new[] { new A380BuildNames.FdPress("A32NX.FCU_FD_PUSH", 0) },
            A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, true, Values((Light, 0))));
        Assert.Empty(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, true, Values((Light, 1)))!);
        Assert.Equal("0 (>K:A32NX.FCU_FD_PUSH)",
            A380BuildNames.PressCalculatorCode(new A380BuildNames.FdPress("A32NX.FCU_FD_PUSH", 0)));
    }

    [Fact]
    public void No_fd_press_goes_out_on_an_unread_state_or_an_unknown_build()
    {
        Assert.Null(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values((Fd1, 0))));
        Assert.Null(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2020, true, Values()));
        Assert.Null(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Fs2024, true, Values()));
        Assert.Null(A380BuildNames.FlightDirectorPresses(FbwSimBuild.Unknown, true, Values((Fd1, 0), (Fd2, 0), (Light, 0))));
    }

    // ---------------------------------------------------------------- baro unit

    [Theory]
    [InlineData(0, 0, 1.0)]   // L:var "IS INHG": 0 = hPa
    [InlineData(1, 0, 0.0)]
    [InlineData(0, 1, 0.0)]
    public void Fs2024_baro_unit_is_both_is_inhg_selectors_at_zero(double capt, double fo, double expected) =>
        Assert.Equal(expected, A380BuildNames.BaroUnitsHectopascals(FbwSimBuild.Fs2024,
            Values(("A32NX_FCU_EFIS_L_BARO_IS_INHG", capt), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", fo))));

    [Theory]
    [InlineData(1, 0, 1.0)]   // XMLVAR "IS HPA": 1 = hPa; HPA_2 drives nothing on FS2020 and is ignored
    [InlineData(1, 1, 1.0)]
    [InlineData(0, 1, 0.0)]
    public void Fs2020_baro_unit_is_hpa_1_alone(double hpa1, double hpa2, double expected) =>
        Assert.Equal(expected, A380BuildNames.BaroUnitsHectopascals(FbwSimBuild.Fs2020,
            Values(("XMLVAR_Baro_Selector_HPA_1", hpa1), ("XMLVAR_Baro_Selector_HPA_2", hpa2))));

    [Fact]
    public void Baro_unit_is_unknown_when_unread_or_on_an_unknown_build()
    {
        Assert.True(double.IsNaN(A380BuildNames.BaroUnitsHectopascals(FbwSimBuild.Fs2020, Values(("XMLVAR_Baro_Selector_HPA_2", 1)))));
        Assert.True(double.IsNaN(A380BuildNames.BaroUnitsHectopascals(FbwSimBuild.Fs2024, Values(("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0)))));
        Assert.True(double.IsNaN(A380BuildNames.BaroUnitsHectopascals(FbwSimBuild.Unknown, Values(("XMLVAR_Baro_Selector_HPA_1", 1)))));
        Assert.Equal(1.0, A380BuildNames.CaptainBaroIsInHg(FbwSimBuild.Fs2020, Values(("XMLVAR_Baro_Selector_HPA_1", 0))));
        Assert.Equal(0.0, A380BuildNames.CaptainBaroIsInHg(FbwSimBuild.Fs2024, Values(("A32NX_FCU_EFIS_L_BARO_IS_INHG", 0))));
    }

    // ---------------------------------------------------------------- the evaluator

    private static FbwA380StateEvaluator Evaluator(string sim, params (string Key, double Value)[] values)
    {
        var e = new FbwA380StateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(values).On(sim));
        return e;
    }

    [Fact]
    public void The_evaluator_reads_its_builds_flight_directors()
    {
        Assert.Equal(1.0, Evaluator("FS2020", (Fd1, 1), (Fd2, 1), (Light, 0)).GetValue(FbwA380StateEvaluator.FlightDirectorsOnField));
        Assert.Equal(0.0, Evaluator("FS2024", (Fd1, 1), (Fd2, 1), (Light, 0)).GetValue(FbwA380StateEvaluator.FlightDirectorsOnField));
        Assert.True(double.IsNaN(Evaluator("Unknown", (Fd1, 1), (Fd2, 1)).GetValue(FbwA380StateEvaluator.FlightDirectorsOnField)));
    }

    [Fact]
    public void The_evaluator_reads_its_builds_baro_unit()
    {
        var both = new[] { ("XMLVAR_Baro_Selector_HPA_1", 1.0), ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1.0), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", 1.0) };
        Assert.Equal(1.0, Evaluator("FS2020", both).GetValue(FbwA380StateEvaluator.BaroUnitHectopascalsField));
        Assert.Equal(0.0, Evaluator("FS2024", both).GetValue(FbwA380StateEvaluator.BaroUnitHectopascalsField));
        Assert.True(double.IsNaN(Evaluator("Unknown", both).GetValue(FbwA380StateEvaluator.BaroUnitHectopascalsField)));
    }

    [Fact]
    public void The_evaluator_reads_its_builds_landing_conf3()
    {
        Assert.Equal(1.0, Evaluator("FS2020", ("A32NX_SPEEDS_LANDING_CONF3", 1), ("A380X_FM_LANDING_CONF3", 0))
            .GetValue(FbwA380StateEvaluator.LandingConf3Field));
        Assert.Equal(0.0, Evaluator("FS2024", ("A32NX_SPEEDS_LANDING_CONF3", 1), ("A380X_FM_LANDING_CONF3", 0))
            .GetValue(FbwA380StateEvaluator.LandingConf3Field));
        Assert.True(double.IsNaN(Evaluator("Unknown", ("A32NX_SPEEDS_LANDING_CONF3", 1))
            .GetValue(FbwA380StateEvaluator.LandingConf3Field)));
    }

    [Fact]
    public void The_baro_readback_uses_its_builds_std_flag()
    {
        // FS2020: STD from the stock KOHLSMAN flag; the FS2024 FCU flag (which reads 0 there) is ignored.
        Assert.Equal("standard", Evaluator("FS2020", (A380BuildNames.Fs2020BaroStdCaptKey, 1),
            ("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 0)).BaroText());
        Assert.Equal("standard", Evaluator("FS2024", (A380BuildNames.Fs2020BaroStdCaptKey, 0),
            ("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 1)).BaroText());
        // FS2024 with only the FS2020 flag read: unknown, never "standard".
        Assert.Null(Evaluator("FS2024", (A380BuildNames.Fs2020BaroStdCaptKey, 1)).BaroText());
        Assert.Null(Evaluator("Unknown", ("A32NX_FCU_LEFT_EIS_BARO_IS_STD", 1), (A380BuildNames.Fs2020BaroStdCaptKey, 1)).BaroText());
    }

    // ---------------------------------------------------------------- the definition's writes

    private static SimConnectManager Sim(string sim, params (string Key, double Value)[] values) =>
        SeededSimConnectCache.ConnectedWith(values).On(sim);

    [Fact]
    public void Definition_fd_write_presses_the_builds_control_and_refuses_unread_or_unknown()
    {
        var def = new RecordingA380Definition();
        Assert.Equal(A380FoWrite.AlreadySet, def.SetFlightDirectorsForFo(true, Sim("FS2020", (Fd1, 1), (Fd2, 1))));
        Assert.Equal(A380FoWrite.Sent, def.SetFlightDirectorsForFo(true, Sim("FS2020", (Fd1, 0), (Fd2, 1))));
        Assert.Equal(new[] { "1 (>K:TOGGLE_FLIGHT_DIRECTOR)" }, def.CalculatorCode);

        def = new RecordingA380Definition();
        Assert.Equal(A380FoWrite.Unknown, def.SetFlightDirectorsForFo(true, Sim("FS2020", (Fd1, 0))));
        Assert.Equal(A380FoWrite.AlreadySet, def.SetFlightDirectorsForFo(true, Sim("FS2024", (Light, 1))));
        Assert.Equal(A380FoWrite.Unknown, def.SetFlightDirectorsForFo(true, Sim("Unknown", (Fd1, 0), (Fd2, 0), (Light, 0))));
        Assert.Empty(def.CalculatorCode);

        Assert.Equal(A380FoWrite.Sent, def.SetFlightDirectorsForFo(true, Sim("FS2024", (Light, 0))));
        Assert.Equal(new[] { "0 (>K:A32NX.FCU_FD_PUSH)" }, def.CalculatorCode);
    }

    [Fact]
    public void A_second_fd_pick_inside_the_batch_window_does_not_press_again()
    {
        // Same definition, same stale cache: the first call presses, the second judges by what was
        // just commanded and reports already set instead of pressing again and undoing the first.
        foreach (var (sim, seed) in new[] { ("FS2024", new[] { (Light, 0.0) }), ("FS2020", new[] { (Fd1, 0.0), (Fd2, 0.0) }) })
        {
            var def = new RecordingA380Definition();
            var sc = Sim(sim, seed);
            Assert.Equal(A380FoWrite.Sent, def.SetFlightDirectorsForFo(true, sc));
            Assert.Equal(A380FoWrite.AlreadySet, def.SetFlightDirectorsForFo(true, sc));
        }
    }

    [Fact]
    public void Definition_baro_unit_write_names_the_builds_selector()
    {
        var def = new RecordingA380Definition();
        Assert.Equal(A380FoWrite.AlreadySet, def.SetBaroUnitHectopascalsForFo(Sim("FS2020", ("XMLVAR_Baro_Selector_HPA_1", 1))));
        Assert.Equal(A380FoWrite.Sent, def.SetBaroUnitHectopascalsForFo(Sim("FS2020", ("XMLVAR_Baro_Selector_HPA_1", 0))));
        Assert.Equal(A380FoWrite.Sent, def.SetBaroUnitHectopascalsForFo(Sim("FS2024",
            ("A32NX_FCU_EFIS_L_BARO_IS_INHG", 1), ("A32NX_FCU_EFIS_R_BARO_IS_INHG", 0))));
        Assert.Equal(A380FoWrite.Unknown, def.SetBaroUnitHectopascalsForFo(Sim("Unknown", ("XMLVAR_Baro_Selector_HPA_1", 0))));
        // FS2020 writes 1 = hPa to its one selector; FS2024 writes 0 = not inches to the side at inches only.
        Assert.Equal(new[] { "1 (>L:XMLVAR_Baro_Selector_HPA_1)", "0 (>L:A32NX_FCU_EFIS_L_BARO_IS_INHG)" }, def.CalculatorCode);
    }

    // ---------------------------------------------------------------- the executor, per build

    [Theory]
    [InlineData("FS2020", new[] { "(>H:A380X_EFIS_CP_BARO_PUSH_1)", "(>H:A380X_EFIS_CP_BARO_PUSH_2)" }, new string[0])]
    [InlineData("FS2024", new string[0], new[] { "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "A32NX_FCU_RIGHT_EIS_BARO_IS_STD" })]
    public async Task Baro_std_goes_to_the_builds_own_control(string sim, string[] calc, string[] keys)
    {
        var def = new RecordingA380Definition();
        Assert.True(await def.Executor(sim).SetBaroStd(true));
        Assert.Equal(calc, def.CalculatorCode);
        Assert.Equal(keys, def.Writes.Select(w => w.Key).ToArray());
    }

    [Theory]
    [InlineData("FS2020", new[] { "3 (>L:A32NX_EFIS_L_ND_RANGE)" }, new string[0])]
    [InlineData("FS2024", new string[0], new[] { "A32NX_EFIS_L_ND_RANGE" })]
    public async Task Nd_knobs_go_to_the_builds_own_control(string sim, string[] calc, string[] keys)
    {
        var def = new RecordingA380Definition();
        Assert.True(await def.Executor(sim).Set("A32NX_EFIS_L_ND_RANGE", 3));
        Assert.Equal(calc, def.CalculatorCode);
        Assert.Equal(keys, def.Writes.Select(w => w.Key).ToArray());
    }

    [Fact]
    public async Task On_an_unknown_simulator_no_build_dependent_write_goes_out_and_each_reports_failure()
    {
        var def = new RecordingA380Definition();
        var exec = def.Executor("Unknown");
        Assert.False(await exec.SetBaroStd(true));
        Assert.False(await exec.SetBaroStd(false));
        Assert.False(await exec.Set("A32NX_EFIS_L_ND_MODE", 3));
        Assert.False(await exec.Set(FbwA380ActionExecutor.FlightDirectorsKey, 1));
        Assert.False(await exec.Set(FbwA380ActionExecutor.BaroUnitHectopascalsKey, 1));
        Assert.Empty(def.CalculatorCode);
        Assert.Empty(def.Writes);
    }

    [Fact]
    public void The_phase_monitor_says_set_only_when_the_write_went_out()
    {
        // The refusal hands the Captain the action instead of reporting a change nobody made.
        Assert.Contains("Set both altimeters to standard", FbwA380FlightPhaseMonitor.ClimbToStdRefused);
        Assert.Contains("Set both altimeters to QNH", FbwA380FlightPhaseMonitor.DescendToQnhRefused);
        Assert.Contains("set to standard", FbwA380FlightPhaseMonitor.ClimbToStdDone);
    }
}
