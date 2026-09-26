// FBW #10855 ("add FG part to PRIM", a380x 1bbd304) gave the A380 ONE flight-director
// pushbutton, on the FCU: it fires A32NX.FCU_FD_PUSH, and its light, L:A32NX_FCU_FD_LIGHT_ON, is
// "FD 1 or FD 2 engaged" as the master PRIM reports it. The same commit MASKED the stock
// TOGGLE_FLIGHT_DIRECTOR — every one is now a press of that single button, whatever its
// parameter says — and deleted everything that wrote the stock AUTOPILOT FLIGHT DIRECTOR
// ACTIVE:n. MSFSBA kept reading the stock var and sending TOGGLE_FLIGHT_DIRECTOR once per side:
// the state it judged by was stale, and the two presses cancelled, so "Flight directors: ON"
// left the flight directors exactly as they were (live, 2026-09-25: TOGGLE_FLIGHT_DIRECTOR 1
// then 2, 209 ms apart).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380FlightDirectorTests
{
    // A PRIM FG discrete word as FBW packs it: the bitfield converted to a float NUMERICALLY,
    // that float's IEEE-754 bits in the low 32, the SSM above them (Arinc429WordTests).
    private static double FgWord(uint ssm, uint bitfield) =>
        (double)(((ulong)ssm << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));
    private const uint NormalOperation = 0b11, NoComputedData = 0b01;

    private static Dictionary<string, SimVarDefinition> Vars() => new FlyByWireA380Definition().GetVariables();

    [Fact]
    public void No_a380_variable_reads_the_stock_flight_director_simvar()
    {
        var stale = Vars()
            .Where(kv => kv.Value.Name.StartsWith("AUTOPILOT FLIGHT DIRECTOR ACTIVE", StringComparison.Ordinal))
            .Select(kv => kv.Key).ToList();

        Assert.True(stale.Count == 0,
            "Nothing on the A380 writes the stock flight-director SimVar since FBW #10855, so these "
            + "read a stale value: " + string.Join(", ", stale));
    }

    [Fact]
    public void The_flight_director_control_reads_the_fcu_pushbutton_light()
    {
        Assert.True(Vars().TryGetValue(A380FlightDirector.StateKey, out var def));
        Assert.Equal("A32NX_FCU_FD_LIGHT_ON", def!.Name);
        Assert.Equal(SimVarType.LVar, def.Type);
    }

    [Theory]
    [InlineData("FD_1_CTL")]
    [InlineData("FD_2_CTL")]
    public void The_per_side_keys_of_the_stock_var_era_are_gone(string key)
    {
        // One button drives both flight directors: a per-side key would be a second name for it,
        // and a set of each would be two presses that cancel.
        Assert.False(Vars().ContainsKey(key), $"'{key}' is still registered.");
    }

    [Fact]
    public void Exactly_one_flight_director_key_reads_the_light()
    {
        // One button, one state, one call-out: a second key on the same Name would, as a Continuous
        // var, shift every later slot of the batch (VarNameCollisionTests) and speak the one change twice.
        var keys = Vars()
            .Where(kv => kv.Value.Name == A380FlightDirector.StateKey)
            .Select(kv => kv.Key).ToList();

        Assert.Equal(new[] { A380FlightDirector.StateKey }, keys);
        Assert.Equal(UpdateFrequency.Continuous, Vars()[A380FlightDirector.StateKey].UpdateFrequency);
        Assert.True(Vars()[A380FlightDirector.StateKey].IsAnnounced);
    }

    [Theory]
    [InlineData(1.0, 0.0, true)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(0.0, 1.0, true)]
    [InlineData(0.0, 0.0, false)]
    public void A_set_presses_the_button_only_when_the_state_differs(double desired, double current, bool presses)
    {
        Assert.Equal(presses ? "A32NX.FCU_FD_PUSH" : null, A380FlightDirector.Command(desired, current));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    public void An_unknown_state_presses(double desired)
    {
        // Same rule as every other A380 toggle (A380ToggleCommand): unknown is not "off".
        Assert.Equal("A32NX.FCU_FD_PUSH", A380FlightDirector.Command(desired, null));
    }

    [Fact]
    public void A_repeated_pick_inside_one_batch_period_presses_once()
    {
        // Two "on" picks well inside one batch period: the cache still shows the pre-press state for
        // the second, so it is judged against the state the first one commanded.
        var def = new FlyByWireA380Definition();
        var mgr = new SimConnectManager(IntPtr.Zero);   // never connected: the cache is empty

        Assert.Equal("A32NX.FCU_FD_PUSH", def.FlightDirectorCommand(1, mgr));
        Assert.Null(def.FlightDirectorCommand(1, mgr));
        // …and the opposite pick is still honoured straight away.
        Assert.Equal("A32NX.FCU_FD_PUSH", def.FlightDirectorCommand(0, mgr));
    }

    [Fact]
    public void The_autopilot_window_button_records_what_it_commands()
    {
        // Ctrl+P's FD button flips the flight directors. It sent the push straight to the sim, so a
        // Flight Directors combo pick in the next second was judged against the stale cache and
        // pressed again, undoing it.
        var def = new FlyByWireA380Definition();
        var mgr = new SimConnectManager(IntPtr.Zero);   // never connected: the cache is empty

        // Unknown state: a press, recorded as "on".
        Assert.Equal("A32NX.FCU_FD_PUSH", def.FlightDirectorToggleCommand(mgr));
        Assert.Null(def.FlightDirectorCommand(1, mgr));
        Assert.Equal("A32NX.FCU_FD_PUSH", def.FlightDirectorCommand(0, mgr));
    }

    [Fact]
    public void The_push_event_bypasses_the_calc_path_probe_like_every_other_a380_fcu_button()
    {
        Assert.StartsWith("A32NX.FCU_", A380FlightDirector.PushEvent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_control_sits_on_the_fcu_panel_and_no_longer_on_the_efis_panels()
    {
        var panels = new FlyByWireA380Definition().GetPanelControls();

        Assert.Contains(A380FlightDirector.StateKey, panels["FCU"]);
        Assert.DoesNotContain("FD_1_CTL", panels["EFIS Captain"]);
        Assert.DoesNotContain("FD_2_CTL", panels["EFIS First Officer"]);
    }

    // ---- Per-side engagement: PRIM FG discrete word 1, bits 13/14 (engaged), 17/18 (inop) ----
    // Exactly what the PFD's FMA E2 cell ("1FD2") reads (FMA.tsx E2Cell).

    [Theory]
    [InlineData(1, (1u << 12), "on")]                    // bit 13: FD 1 engaged
    [InlineData(2, (1u << 12), "off")]
    [InlineData(2, (1u << 13), "on")]                    // bit 14: FD 2 engaged
    [InlineData(1, (1u << 16), "inoperative")]           // bit 17: FD 1 inop
    [InlineData(2, (1u << 17), "inoperative")]           // bit 18: FD 2 inop
    [InlineData(1, 0u, "off")]
    public void The_side_readout_decodes_the_prim_word(int side, uint bits, string expected)
    {
        Assert.Equal(expected, A380FlightDirector.DescribeSide(FgWord(NormalOperation, bits), side));
    }

    [Fact]
    public void A_prim_word_without_data_reads_not_available()
    {
        Assert.Equal("not available",
            A380FlightDirector.DescribeSide(FgWord(NoComputedData, 1u << 12), 1));
    }

    [Theory]
    [InlineData("FD_1")]
    [InlineData("FD_2")]
    public void The_side_readouts_read_the_prim_word_on_request(string key)
    {
        var def = Vars()[key];
        Assert.Equal(A380FlightDirector.PrimFgWord1, def.Name);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
    }

    [Fact]
    public void The_pfd_status_box_renders_the_side_readouts_through_the_definition()
    {
        // FD 1 engaged, FD 2 inop — what the definition's display override must say for each row.
        var def = new FlyByWireA380Definition();
        double word = FgWord(NormalOperation, (1u << 12) | (1u << 17));

        Assert.True(def.TryGetDisplayOverride("FD_1", word, out var fd1));
        Assert.True(def.TryGetDisplayOverride("FD_2", word, out var fd2));
        Assert.Equal("on", fd1);
        Assert.Equal("inoperative", fd2);
    }
}
