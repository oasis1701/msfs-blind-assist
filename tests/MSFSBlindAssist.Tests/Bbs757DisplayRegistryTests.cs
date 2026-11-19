// The 757's display registry (plan 23 Task 7): one list of the display readers' monitors, their Ctrl+M rows and their
// keys, filled on its first use, so Ctrl+M and the hotkeys see every reader before the readers start; a reader's key runs
// before anything else the definition does with it, and can hand the key to the AI read (S15).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class Bbs757DisplayRegistryTests
{
    private static readonly MonitorRow FmaRow = new("MSFSBA_BBS757_FMA", "Flight Mode Annunciator");

    [Fact]
    public void A_second_handler_for_one_key_throws()
    {
        var registry = new Bbs757DisplayRegistry();
        registry.OnKey(HotkeyAction.ReadDisplayUpperECAM, _ => true);

        Assert.Throws<InvalidOperationException>(() => registry.OnKey(HotkeyAction.ReadDisplayUpperECAM, _ => false));

        // The first handler stays; a key with none is not handled.
        Assert.True(registry.TryHandle(HotkeyAction.ReadDisplayUpperECAM, default));
        Assert.False(registry.TryHandle(HotkeyAction.ReadDisplayLowerECAM, default));
    }

    private static readonly Bbs757SimInput Latitude = new("PLANE LATITUDE", "PLANE LATITUDE", SimVarType.SimVar, "degrees");

    [Fact]
    public void Each_readers_inputs_are_recorded_once_in_registration_order()
    {
        // Taken from each monitor (its reader's), so they can never disagree with the reader's.
        var registry = new Bbs757DisplayRegistry();

        registry.Add(new InputMonitor(Bbs757SimInput.LVar("L_A"), Bbs757SimInput.LVar("L_B")));
        registry.Add(new InputMonitor());   // a reader with no input
        registry.Add(new InputMonitor(Bbs757SimInput.LVar("L_B"), Latitude, Bbs757SimInput.LVar("L_C")));
        registry.Add(new InputMonitor(Latitude with { Units = "Degrees" }));   // the same variable: units in any case

        Assert.Equal(new[] { "L_A", "L_B", "PLANE LATITUDE", "L_C" }, registry.SimInputs.Select(i => i.Key));
        Assert.Equal(Latitude, registry.SimInputs[2]);
        Assert.Equal(4, registry.Monitors.Count);
    }

    [Fact]
    public void An_input_key_declared_as_another_variable_is_refused_whole()
    {
        var registry = new Bbs757DisplayRegistry();
        var pitch = new Bbs757SimInput("PITCH", "PLANE PITCH DEGREES", SimVarType.SimVar, "degrees");
        registry.Add(new InputMonitor(pitch));

        // One key, two variables: a wiring error, and nothing of that reader is registered.
        Assert.Throws<InvalidOperationException>(() =>
            registry.Add(new InputMonitor(Bbs757SimInput.LVar("L_NEW"), pitch with { Units = "radians" })));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new InputMonitor(Bbs757SimInput.LVar("PITCH"))));
        // Not a reader input: an event, a stock variable with no units, no key.
        Assert.Throws<ArgumentException>(() => registry.Add(new InputMonitor(new Bbs757SimInput("E", "E", SimVarType.Event, null))));
        Assert.Throws<ArgumentException>(() => registry.Add(new InputMonitor(new Bbs757SimInput("S", "S", SimVarType.SimVar, null))));
        Assert.Throws<ArgumentException>(() => registry.Add(new InputMonitor(new Bbs757SimInput("", "S", SimVarType.LVar, null))));

        Assert.Single(registry.Monitors);
        Assert.Equal(new[] { pitch }, registry.SimInputs);
    }

    [Fact]
    public void Only_the_upper_eicas_takes_a_simulator_input()
    {
        // B757-30: no reader judges memory by a simulator value. The FMA and the lower EICAS read memory alone; the upper
        // EICAS takes BlueBird's manual thrust reference flag where its draw does (S10).
        var def = new BBS757Definition();
        var lower = def.Displays.Monitors.OfType<Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>>().Single();

        Assert.Empty(def.Fma.SimInputs);
        Assert.Empty(lower.SimInputs);
        Assert.Equal(new[] { Bbs757SimInput.LVar("BBS757_THRREF_PULL") }, def.UpperEicas.SimInputs);
        Assert.Contains(Bbs757SimInput.LVar("BBS757_THRREF_PULL"), def.Displays.SimInputs);
        Assert.DoesNotContain(def.Displays.SimInputs, i => i.Name.StartsWith("TURB ENG", StringComparison.Ordinal));
    }

    [Fact]
    public void Monitor_rows_list_every_reader_before_the_readers_start()
    {
        var def = new BBS757Definition();

        var rows = def.MonitorRows();

        Assert.Null(def.DisplaySession);   // nothing has started
        Assert.Contains(FmaRow, rows);
        Assert.Same(def.Fma, def.Displays.Monitors[0]);   // the FMA registers itself first, the seams after it
        Assert.All(def.Displays.Monitors, m => Assert.True(m.KeyFallsBackToAi()));   // not started: the AI read
        Assert.All(def.Displays.Rows, r => Assert.Contains(r, rows));
        Assert.Same(def.Displays, def.Displays);   // filled once
    }

    [Fact]
    public void Monitor_rows_end_with_the_flight_mode_annunciator()
    {
        var def = new BBS757Definition();

        var rows = def.MonitorRows();
        var readers = def.Displays.Rows;

        // The readers' rows come last, the FMA's first among them.
        Assert.Equal(FmaRow, readers[0]);
        Assert.Equal(readers, rows.Skip(rows.Count - readers.Count));
        Assert.Equal(FmaRow, rows[rows.Count - readers.Count]);
        // Every row once.
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_registered_key_runs_before_the_base_and_can_fall_back_to_ai()
    {
        var def = new BBS757Definition();
        var aiReads = new List<HotkeyAction>();
        def.AiReadOverride = a => { aiReads.Add(a); return true; };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var contexts = new List<Bbs757KeyContext>();
        bool handles = true;
        // The base answers this key itself ("Trim announcements off"), so a handler that runs first is heard by silence.
        def.Displays.OnKey(HotkeyAction.ToggleTrimAnnouncements, ctx => { contexts.Add(ctx); return handles; });
        // A key no reader has claimed yet (plan 24 owns the upper EICAS's).
        def.Displays.OnKey(HotkeyAction.ReadDisplayISIS, ctx => ctx.AiRead());

        Assert.True(def.HandleHotkeyAction(HotkeyAction.ToggleTrimAnnouncements, sim, speech, null!, null!));

        Assert.Empty(speech.All);
        Assert.Empty(aiReads);
        var context = Assert.Single(contexts);
        Assert.Same(sim, context.Sim);
        Assert.Same(speech, context.Announcer);

        // A handler that does not take the key leaves it to the definition and the base.
        handles = false;
        Assert.True(def.HandleHotkeyAction(HotkeyAction.ToggleTrimAnnouncements, sim, speech, null!, null!));
        Assert.Single(speech.All);

        // The fallback is the AI read of the same key.
        Assert.True(def.HandleHotkeyAction(HotkeyAction.ReadDisplayISIS, sim, speech, null!, null!));
        Assert.Equal(new[] { HotkeyAction.ReadDisplayISIS }, aiReads);
        Assert.Single(speech.All);
    }

    /// <summary>A monitor whose reader declares <paramref name="inputs"/>.</summary>
    private sealed class InputMonitor(params Bbs757SimInput[] inputs) : IBbs757DisplayMonitor
    {
        public IReadOnlyList<Bbs757SimInput> SimInputs { get; } = inputs;
        public void Start(IBbs757DisplayEnvironment env) { }
        public void Restart() { }
        public void Stop() { }
        public bool KeyFallsBackToAi() => true;
        public IReadOnlyList<string> View() => Array.Empty<string>();
        public void Dispose() { }
    }
}
