// The 757 definition owns its display readers' module session and monitors (plan 23 Task 7, S2): StartDisplays makes a
// new session and starts every registered monitor on it, a flight reload invalidates the session and restarts them, a
// switch away stops them and disposes the session, and the tablet reads the EFB through the same session. The app's side
// of a monitor (Bbs757DisplayEnvironment) waits while the simulator is disconnected, checked first ([WASM-7]'s order), or
// another aircraft is loaded, and speaks without interrupting (S5).

using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

public class Bbs757DisplaysWiringTests
{
    private const string BlueBirdTitle = "BlueBird 757-200 RR N/W 4 Door";

    [Fact]
    public void Switching_away_stops_every_monitor_and_disposes_the_session()
    {
        var client = new CoherentBbs757TabletClient(() => "");   // never started: no socket
        var def = new BBS757Definition { StartMonitor = StartWithoutTimer, TabletClientFactory = () => client };
        def.EnsureTabletClient();
        var other = new FakeMonitor { TabletClient = client };
        def.Displays.Add(other);
        def.StartDisplays(new FakeHost(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var session = def.DisplaySession;
        Assert.NotNull(session);
        Assert.Equal("WaitingForSimulator", def.Fma.StateName);
        Assert.Equal(new[] { "start" }, other.Calls);

        def.OnSwitchedAway();

        Assert.Equal("Stopped", def.Fma.StateName);
        Assert.Equal(new[] { "start", "stop while the tablet client lived" }, other.Calls);
        Assert.Null(def.DisplaySession);
        Assert.Throws<ObjectDisposedException>(() => StartSearch(session!));
        Assert.True(client.IsDisposed);

        // A start that lands after the switch starts nothing.
        def.StartDisplays(new FakeHost(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        Assert.Null(def.DisplaySession);
        Assert.Equal("Stopped", def.Fma.StateName);
        Assert.Equal(2, other.Calls.Count);
    }

    [Fact]
    public void A_switch_away_closes_every_owned_window_for_good()
    {
        // Plans 24-27 make their window once and show it again: one open at a switch away must not stay up showing the
        // stopped monitor's frozen rows, and the window itself cancels every close but the app's exit.
        var def = new BBS757Definition { StartMonitor = StartWithoutTimer };
        def.StartDisplays(new FakeHost(), new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var shown = new BBS757DisplayForm("Upper EICAS", "Upper EICAS", def.Fma.View);
        var hidden = new BBS757DisplayForm("Lower EICAS", "Lower EICAS", def.Fma.View);
        var failing = new FailingForm();
        var plain = new Form();
        def.Displays.OwnWindow(shown);
        def.Displays.OwnWindow(failing);   // a window whose close throws leaves the others to close
        def.Displays.OwnWindow(hidden);
        def.Displays.OwnWindow(plain);
        shown.ShowForm();

        def.OnSwitchedAway();

        Assert.True(shown.IsDisposed);
        Assert.True(hidden.IsDisposed);
        Assert.True(plain.IsDisposed);
        Assert.Equal(1, failing.Attempts);
        Assert.Equal("Stopped", def.Fma.StateName);
        Assert.Null(def.DisplaySession);

        Assert.Null(Record.Exception(def.OnSwitchedAway));   // a second switch away throws nothing
        failing.Fail = false;
        failing.Dispose();
    }

    [Fact]
    public void A_flight_reload_invalidates_the_session_and_restarts_every_monitor()
    {
        var host = new FakeHost();   // no installed files: an analysis with no builds
        var speech = new SpeechCapture();
        var def = new BBS757Definition { NestRunning = () => true, StartMonitor = (m, env) => StartWithoutTimer(m, new SimulatorUp(env)) };
        var other = new FakeMonitor();
        def.Displays.Add(other);
        def.StartDisplays(host, new NoLayouts(), new SimConnectManager(IntPtr.Zero), speech);
        var session = def.DisplaySession;

        def.Fma.Tick();   // the simulator is up: the session analyses the files
        def.Fma.Tick();   // no layout file
        Assert.Equal("Unavailable", def.Fma.StateName);
        Assert.Equal(1, host.FindCalls);

        def.OnSimContextReset();

        Assert.Same(session, def.DisplaySession);   // a reload keeps the session
        Assert.Equal("WaitingForSimulator", def.Fma.StateName);
        Assert.Equal(new[] { "start", "restart" }, other.Calls);
        def.Fma.Tick();
        def.Fma.Tick();
        Assert.Equal(2, host.FindCalls);   // the analysis that found nothing was dropped and runs again
        Assert.Equal("Unavailable", def.Fma.StateName);
        // The session's one files line, said once for it, reload or not (S14), and never interrupting (S5).
        Assert.Equal(new[] { Bbs757DisplayFilesNotice.Line }, speech.All);
        Assert.Empty(speech.Interrupts);
        def.OnSwitchedAway();
    }

    [Fact]
    public void Every_reader_shares_one_files_notice_per_session()
    {
        var envs = new List<IBbs757DisplayEnvironment>();
        var def = new BBS757Definition { StartMonitor = (m, env) => { envs.Add(env); StartWithoutTimer(m, env); } };
        def.Displays.Add(new FakeMonitor());
        var sim = new SimConnectManager(IntPtr.Zero);

        def.StartDisplays(new FakeHost(), new NoLayouts(), sim, new SpeechCapture());
        def.StartDisplays(new FakeHost(), new NoLayouts(), sim, new SpeechCapture());   // a new session: the simulator changed

        // Every registered reader (the FMA, the fake, and each later plan's seam) starts on each session.
        int readers = def.Displays.Monitors.Count;
        Assert.Equal(2 * readers, envs.Count);
        Assert.Single(envs.Take(readers).Select(e => e.FilesNotice).Distinct());
        Assert.Single(envs.Skip(readers).Select(e => e.FilesNotice).Distinct());
        Assert.NotSame(envs[0].FilesNotice, envs[readers].FilesNotice);
        def.OnSwitchedAway();
    }

    [Fact]
    public async Task The_tablet_reads_through_the_definitions_session()
    {
        var host = new FakeHost();
        var def = new BBS757Definition { StartMonitor = StartWithoutTimer };
        var notFound = Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.MemoryNotFound);

        Assert.Equal(notFound, await def.ReadEfbModelAsync());   // no session yet: nothing searched
        Assert.Equal(0, host.FindCalls);

        def.StartDisplays(host, new NoLayouts(), new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var read = await def.ReadEfbModelAsync();

        // The session's own analysis (no installed files here), in the 757's words.
        Assert.Equal(Bbs757EfbModelRead.Unreadable(Bbs757DisplayReasons.FilesNotFound), read);
        Assert.Equal(1, host.FindCalls);
        await def.DisplaySession!.AnalyseAsync();
        Assert.Equal(1, host.FindCalls);   // one analysis for the tablet and the readers (WASM-11)

        def.OnSwitchedAway();
        Assert.Equal(notFound, await def.ReadEfbModelAsync());
        Assert.Equal(1, host.FindCalls);
    }

    [Fact]
    public void The_environment_reads_null_while_disconnected_first()
    {
        using var session = NewSession();
        var asked = new List<string>();
        var env = new Bbs757DisplayEnvironment(connected: () => false,
            flightRunning: () => { asked.Add("flight"); return true; },
            title: () => { asked.Add("title"); return BlueBirdTitle; },
            cached: key => { asked.Add(key); return 1; },
            cacheKeys: new Dictionary<string, string> { ["L_A"] = "L_A" },
            settings: () => new UserSettings(), new SpeechCapture(), session, new Bbs757DisplayFilesNotice());

        Assert.Null(env.ReadSimulator(new[] { "L_A" }));
        Assert.Null(env.ReadSimulator(Array.Empty<string>()));   // a reader with no input waits the same
        Assert.Empty(asked);   // neither the flight, the title nor a value is read while disconnected

        // The app's own manager, never connected here.
        var real = new Bbs757DisplayEnvironment(new SimConnectManager(IntPtr.Zero), new SpeechCapture(), session,
            new Bbs757DisplayFilesNotice(), new Dictionary<string, string>());
        Assert.Null(real.ReadSimulator(Array.Empty<string>()));
    }

    [Fact]
    public void With_no_inputs_the_environment_still_waits_while_disconnected_no_flight_runs_or_another_title_is_loaded()
    {
        // The FMA and the lower EICAS take no simulator input: their monitors' silent wait still comes from this null.
        using var session = NewSession();
        bool connected = false, flight = true;
        string? title = BlueBirdTitle;
        var env = new Bbs757DisplayEnvironment(() => connected, () => flight, () => title, _ => null,
            new Dictionary<string, string>(), () => new UserSettings(), new SpeechCapture(), session, new Bbs757DisplayFilesNotice());
        var none = Array.Empty<string>();

        Assert.Null(env.ReadSimulator(none));   // disconnected
        connected = true;
        flight = false;
        Assert.Null(env.ReadSimulator(none));   // the menus, or a flight's load
        flight = true;
        title = "FenixA320 BAW";
        Assert.Null(env.ReadSimulator(none));   // another aircraft under the 757 profile
        title = BlueBirdTitle;
        var values = env.ReadSimulator(none);   // up: no value of any input, and nothing to wait for
        Assert.NotNull(values);
        Assert.Empty(values!.Value.Values);
    }

    [Fact]
    public void The_environment_waits_while_no_flight_runs()
    {
        // The menus, or a flight's load: the "Sim" system event says no flight runs, while the title stays the last
        // flight's and the cache keeps its values.
        using var session = NewSession();
        bool flight = false;
        var asked = new List<string>();
        var cache = new Dictionary<string, double> { ["CACHE_A"] = 1 };
        var env = new Bbs757DisplayEnvironment(() => true, () => flight,
            () => { asked.Add("title"); return "BlueBird 757-200 RR"; },
            key => { asked.Add(key); return cache.TryGetValue(key, out double v) ? v : null; },
            new Dictionary<string, string> { ["L_A"] = "CACHE_A" }, () => new UserSettings(), new SpeechCapture(), session,
            new Bbs757DisplayFilesNotice());

        Assert.Null(env.ReadSimulator(new[] { "L_A" }));
        Assert.Empty(asked);   // the flight is asked before the title or a value

        flight = true;
        Assert.Equal(1, env.ReadSimulator(new[] { "L_A" })!.Value["L_A"]);
    }

    [Fact]
    public void Leaving_a_flight_for_the_menus_says_nothing_for_10_minutes()
    {
        // A running reader, then the flight ends: BlueBird's memory goes, the title stays a 757's. With the flight's end the
        // environment reads null, so the monitor waits: never "stopped: aircraft memory not found".
        using var session = NewSession();
        var fake = new Bbs757DisplayMonitorTests.FakeEnvironment();
        bool flight = true;
        var real = new Bbs757DisplayEnvironment(() => true, () => flight, () => BlueBirdTitle, _ => 1,
            new Dictionary<string, string>(), () => new UserSettings(), new SpeechCapture(), session,
            new Bbs757DisplayFilesNotice());   // the fake reader takes no input, as the FMA
        var env = new SimulatorFrom(real, fake);
        var reader = new Bbs757DisplayMonitorTests.FakeReader();
        var monitor = new Bbs757DisplayMonitor<Bbs757DisplayMonitorTests.FakeLayout, Bbs757DisplayMonitorTests.FakeReading>(reader);
        monitor.Start(env, startTimer: false);
        for (int i = 0; i < 300 && monitor.StateName != "Running"; i++) Tick();
        Assert.Equal("Running", monitor.StateName);

        flight = false;
        reader.Samplers.Single(s => !s.Disposed).Fail = true;
        fake.Locate = () => Task.FromResult(new ModuleLocation(Array.Empty<LocatedModule>(), null));
        for (int i = 0; i < 6_000; i++) Tick();   // 10 minutes at 100 ms

        Assert.Equal("WaitingForSimulator", monitor.StateName);
        Assert.Empty(fake.Spoken);
        Assert.False(monitor.KeyFallsBackToAi());   // the window opens and says it waits
        monitor.Stop();

        void Tick()
        {
            monitor.Tick();
            fake.NowMs += 100;
        }
    }

    [Theory]
    [InlineData("BlueBird 757-200 PW", true)]
    [InlineData("BlueBird 757-200 RR Scimitar/W", true)]
    [InlineData("bluebird 757-200 pw no cabin", true)]
    [InlineData("FenixA320 BAW", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_environment_waits_while_another_aircraft_is_loaded(string? title, bool reads)
    {
        using var session = NewSession();
        var env = Connected(session, title, new Dictionary<string, double> { ["CACHE_A"] = 1 }, new() { ["L_A"] = "CACHE_A" });

        Assert.Equal(reads, env.ReadSimulator(new[] { "L_A" }) is not null);
    }

    [Fact]
    public void The_environment_reads_each_input_value_from_its_batched_copy()
    {
        using var session = NewSession();
        var cache = new Dictionary<string, double> { ["CACHE_A"] = 1, ["CACHE_B"] = 35000 };
        var env = Connected(session, BlueBirdTitle, cache, new() { ["L_A"] = "CACHE_A", ["L_B"] = "CACHE_B", ["L_C"] = "CACHE_C" });

        var values = env.ReadSimulator(new[] { "L_A", "L_B" });

        Assert.NotNull(values);
        Assert.Equal(1, values!.Value["L_A"]);
        Assert.Equal(35000, values.Value["L_B"]);
        // A value not delivered yet, or a key with no batched copy: nothing to give the sampler, so the monitor waits.
        Assert.Null(env.ReadSimulator(new[] { "L_A", "L_C" }));
        Assert.Null(env.ReadSimulator(new[] { "L_A", "L_UNREGISTERED" }));
    }

    [Fact]
    public void The_environment_speaks_without_interrupting()
    {
        using var session = NewSession();
        var speech = new SpeechCapture();
        var env = new Bbs757DisplayEnvironment(new SimConnectManager(IntPtr.Zero), speech, session, new Bbs757DisplayFilesNotice(),
            new Dictionary<string, string>());

        env.Speak("FMA: Roll: LOC, Pitch: G/S");

        Assert.Equal(new[] { "FMA: Roll: LOC, Pitch: G/S" }, speech.All);
        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void The_environment_mutes_by_the_757s_ctrl_m_rows()
    {
        using var session = NewSession();
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { Bbs757FmaDisplay.MuteKey } };
        settings.RebuildDisabledMonitorVariableCaches();
        var env = new Bbs757DisplayEnvironment(() => true, () => true, () => BlueBirdTitle, _ => 1, new Dictionary<string, string>(),
            () => settings, new SpeechCapture(), session, new Bbs757DisplayFilesNotice());

        Assert.True(env.IsMuted(Bbs757FmaDisplay.MuteKey));
        Assert.False(env.IsMuted("MSFSBA_BBS757_OTHER"));
    }

    [Fact]
    public void The_display_inputs_are_the_manual_thrust_flag_and_the_ehsis_position()
    {
        // B757-30: the FMA's F/D switches and MCP targets and the lower EICAS's N2 and page, once gate variables, are no
        // longer read for the readers; the upper EICAS's manual thrust reference flag (S10) is the one input, read from one
        // streamed variable, hidden from Ctrl+M when it is a copy ([VAR-9]).
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        // Plan 26 added the EHSI's two position inputs (PLANE LATITUDE and PLANE LONGITUDE, S10): the readers' inputs now
        // are exactly these three, the thrust flag being the only L:var. Plan 28 added the EHSI's map extras' three (the
        // wind's speed and direction, the gear position, S10): six. Plan 27 added the ADI's four (the bank, the pitch, the
        // ground speed and the Mach number its draw takes, S10): ten.
        // As a set: a dictionary's key order is not part of its contract.
        Assert.Equal(new[] { "AIRSPEED MACH", "AMBIENT WIND DIRECTION", "AMBIENT WIND VELOCITY", "BBS757_THRREF_PULL",
                "GEAR POSITION", "GROUND VELOCITY", "PLANE BANK DEGREES", "PLANE LATITUDE", "PLANE LONGITUDE",
                "PLANE PITCH DEGREES" },
            keys.Keys.Order(StringComparer.Ordinal));
        var carrier = vars[keys["BBS757_THRREF_PULL"]];
        Assert.Equal(("BBS757_THRREF_PULL", SimVarType.LVar), (carrier.Name, carrier.Type));
        Assert.Equal(UpdateFrequency.Continuous, carrier.UpdateFrequency);
        Assert.True(carrier.IsAnnounced);   // streamed: on the batch, or on its own subscription
        Assert.True(ContinuousBatchLayout.RidesBatch(carrier) || carrier.ExcludeFromBatch);
        Assert.True(vars.Values.Count(v => v.Name == "BBS757_THRREF_PULL" && ContinuousBatchLayout.RidesBatch(v)) <= 1);
        Assert.All(vars.Where(kv => kv.Key.StartsWith(BBS757Definition.DisplayInputCopyPrefix, StringComparison.Ordinal)), kv =>
        {
            Assert.Contains(kv.Key[BBS757Definition.DisplayInputCopyPrefix.Length..], keys.Keys);
            Assert.True(kv.Value.ExcludeFromMonitorManager);
            Assert.EndsWith(" (display reader input)", kv.Value.DisplayName);
        });
        foreach (string gone in new[] { "TURB ENG N1:1", "TURB ENG N1:2", "TURB ENG N2:1", "TURB ENG N2:2", "BBS_ECAM_Mode",
            "BBS_FD1_SWITCH", "BBS_FD2_SWITCH", "BBS_AP_TARGET_ALTITUDE", "BBS_AP_TARGET_HEADING" })
            Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + gone, vars.Keys);
        Assert.DoesNotContain(vars.Keys, k => k.StartsWith("BBS757_DISPLAY_GATE_", StringComparison.Ordinal));
    }

    [Fact]
    public void An_input_copy_is_consumed_silently()
    {
        const string input = "BBS_TEST_READER_INPUT";
        var def = new BBS757Definition { NestRunning = () => true };
        def.Displays.Add(new FakeMonitor { SimInputs = new[] { Bbs757SimInput.LVar(input) } });
        var speech = new SpeechCapture();
        string copy = def.DisplayCacheKeys()[input];
        Assert.Equal(BBS757Definition.DisplayInputCopyPrefix + input, copy);

        Assert.True(def.ProcessSimVarUpdate(copy, 1, speech));
        Assert.True(def.ProcessSimVarUpdate(copy, 0, speech));

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_later_readers_input_gets_its_copy_and_cache_key()
    {
        // As a later plan's seam registers its reader (S16), with its own inputs: nothing in Displays.cs lists them.
        const string input = "BBS_TEST_READER_INPUT";
        const string heading = "BBS_AP_TARGET_HEADING";   // the MCP's heading, on the batch for the autoflight call-outs
        var def = new BBS757Definition();
        def.Displays.Add(new FakeMonitor
        {
            SimInputs = new[] { Bbs757SimInput.LVar(input), Bbs757SimInput.LVar(heading) },
        });

        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        Assert.Equal(BBS757Definition.DisplayInputCopyPrefix + input, keys[input]);
        var copy = vars[keys[input]];
        Assert.Equal(input, copy.Name);
        Assert.Equal(SimVarType.LVar, copy.Type);
        Assert.True(ContinuousBatchLayout.RidesBatch(copy));
        Assert.True(copy.ExcludeFromMonitorManager);
        // A key another variable already carries on the batch gets no copy, and is read from that variable.
        Assert.Equal(Bbs757AutoflightAnnouncements.HeadingWatchKey, keys[heading]);
        Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + heading, vars.Keys);
        // Every registered input resolves to a variable of its name, and no name rides the batch twice ([VAR-7]).
        Assert.All(def.Displays.SimInputs, i =>
        {
            var cache = vars[keys[i.Key]];
            Assert.Equal(i.Name, cache.Name);
            Assert.Equal(UpdateFrequency.Continuous, cache.UpdateFrequency);   // it streams
            Assert.True(cache.IsAnnounced);
            Assert.True(vars.Values.Count(v => v.Name == i.Name && ContinuousBatchLayout.RidesBatch(v)) <= 1);
            Assert.True(ContinuousBatchLayout.RidesBatch(cache) || cache.ExcludeFromBatch);   // on the batch, or its own subscription
        });
        Assert.Equal(def.Displays.SimInputs.Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal),
            keys.Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void A_stock_input_a_continuous_row_streams_unannounced_gets_its_copy_off_the_batch()
    {
        // Plan 25's case, kept now no reader takes N2 ([VAR-7]): the Engines panel's N2 rows stream TURB ENG N2:1 and :2
        // unannounced, so never on the batch, yet the batch counts every Continuous name. An input of that name gets its
        // own copy that streams on its own subscription into the same cache, off the batch; an L:var input with no such
        // twin rides the batch.
        var def = new BBS757Definition();
        var left = new Bbs757SimInput("BBS757_TEST_N2_1", "TURB ENG N2:1", SimVarType.SimVar, "percent");
        var right = new Bbs757SimInput("BBS757_TEST_N2_2", "TURB ENG N2:2", SimVarType.SimVar, "percent");
        var page = Bbs757SimInput.LVar("BBS_ECAM_Mode");
        def.Displays.Add(new FakeMonitor { SimInputs = new[] { left, right, page } });

        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        foreach (var (input, rowKey) in new[] { (left, "BBS757_ENG1_N2"), (right, "BBS757_ENG2_N2") })
        {
            var row = vars[rowKey];   // the Engines panel's silent row: Continuous, not announced, so never on the batch
            Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
            Assert.False(ContinuousBatchLayout.RidesBatch(row));
            Assert.Equal(BBS757Definition.DisplayInputCopyPrefix + input.Key, keys[input.Key]);
            var copy = vars[keys[input.Key]];
            Assert.Equal(row.Name, copy.Name);
            Assert.Equal((SimVarType.SimVar, "percent"), (copy.Type, copy.Units));
            Assert.True(copy.ExcludeFromBatch);
            Assert.Equal(UpdateFrequency.Continuous, copy.UpdateFrequency);   // streams on its own subscription
            Assert.True(copy.IsAnnounced);
            Assert.True(copy.ExcludeFromMonitorManager);
            Assert.False(ContinuousBatchLayout.RidesBatch(copy));
        }
        // The page copy has no such twin: it rides the batch, once.
        var pageCopy = vars[keys[page.Key]];
        Assert.False(pageCopy.ExcludeFromBatch);
        Assert.Single(vars.Values, v => v.Name == "BBS_ECAM_Mode" && ContinuousBatchLayout.RidesBatch(v));
    }

    [Fact]
    public void A_later_readers_stock_input_gets_a_stock_copy_read_through_its_key()
    {
        // A stock input: a hidden copy of the declared type and units, never an L:var of that name ([VAR-2]), and its
        // cached value reaches ReadSimulator under the reader's key.
        var latitude = new Bbs757SimInput("PLANE LATITUDE", "PLANE LATITUDE", SimVarType.SimVar, "degrees");
        var def = new BBS757Definition();
        def.Displays.Add(new FakeMonitor { SimInputs = new[] { latitude } });

        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        Assert.Equal(BBS757Definition.DisplayInputCopyPrefix + latitude.Key, keys[latitude.Key]);
        var copy = vars[keys[latitude.Key]];
        Assert.Equal(("PLANE LATITUDE", SimVarType.SimVar, "degrees"), (copy.Name, copy.Type, copy.Units));
        Assert.True(ContinuousBatchLayout.RidesBatch(copy));
        Assert.True(copy.ExcludeFromMonitorManager);   // consumed silently, no Ctrl+M row ([VAR-9])
        Assert.DoesNotContain(vars.Values, v => v.Type == SimVarType.LVar && v.Name == latitude.Name);
        // [VAR-7]: one batched variable of the name (the base's on-request latitude does not ride the batch).
        Assert.Single(vars.Values, v => v.Name == latitude.Name && ContinuousBatchLayout.RidesBatch(v));

        using var session = NewSession();
        var cache = new Dictionary<string, double> { [keys[latitude.Key]] = 51.4706 };
        var env = Connected(session, BlueBirdTitle, cache, new Dictionary<string, string>(keys));
        Assert.Equal(51.4706, env.ReadSimulator(new[] { latitude.Key })!.Value[latitude.Key]);
    }

    [Fact]
    public void A_stock_input_reuses_a_batched_one_of_the_same_name_type_and_units()
    {
        // ELEVATOR TRIM POSITION in degrees already rides the batch (the trim announcements): an input in degrees, in any
        // case, is read from it with no copy. One in other units gets its own copy, off the batch, so no two batched
        // variables share the name ([VAR-7]), streaming into the same cache on its own subscription.
        var def = new BBS757Definition();
        var degrees = new Bbs757SimInput("TRIM_DEG", "ELEVATOR TRIM POSITION", SimVarType.SimVar, "Degrees");
        var radians = new Bbs757SimInput("TRIM_RAD", "ELEVATOR TRIM POSITION", SimVarType.SimVar, "radians");
        def.Displays.Add(new FakeMonitor { SimInputs = new[] { degrees, radians } });

        var vars = def.GetVariables();
        var keys = def.DisplayCacheKeys();

        Assert.True(ContinuousBatchLayout.RidesBatch(vars["MON_ElevatorTrim"]));
        Assert.Equal("degrees", vars["MON_ElevatorTrim"].Units);
        Assert.Equal("MON_ElevatorTrim", keys[degrees.Key]);
        Assert.DoesNotContain(BBS757Definition.DisplayInputCopyPrefix + degrees.Key, vars.Keys);

        Assert.Equal(BBS757Definition.DisplayInputCopyPrefix + radians.Key, keys[radians.Key]);
        var copy = vars[keys[radians.Key]];
        Assert.Equal((SimVarType.SimVar, "radians"), (copy.Type, copy.Units));
        Assert.False(ContinuousBatchLayout.RidesBatch(copy));
        Assert.True(copy.ExcludeFromBatch);
        Assert.Equal(UpdateFrequency.Continuous, copy.UpdateFrequency);
        Assert.True(copy.IsAnnounced);
        Assert.True(copy.ExcludeFromMonitorManager);
        Assert.Single(vars.Values, v => v.Name == degrees.Name && ContinuousBatchLayout.RidesBatch(v));
    }

    // ---- fakes ----

    /// <summary>A window whose disposal throws while <see cref="Fail"/> holds.</summary>
    private sealed class FailingForm : Form
    {
        // Fields, not properties: a Form's public properties take designer serialization (WFO1000).
        public bool Fail = true;
        public int Attempts;

        protected override void Dispose(bool disposing)
        {
            if (disposing && Fail)
            {
                Attempts++;
                throw new InvalidOperationException("the window would not close");
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>The real environment's simulator reads; everything else (clock, speech, the session's jobs) a fake's.</summary>
    private sealed class SimulatorFrom : IBbs757DisplayEnvironment
    {
        private readonly Bbs757DisplayEnvironment _real;
        private readonly Bbs757DisplayMonitorTests.FakeEnvironment _fake;

        public SimulatorFrom(Bbs757DisplayEnvironment real, Bbs757DisplayMonitorTests.FakeEnvironment fake)
        {
            _real = real;
            _fake = fake;
        }

        public long NowMs => _fake.NowMs;
        public Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys) => _real.ReadSimulator(keys);
        public bool IsMuted(string muteKey) => _fake.IsMuted(muteKey);
        public Bbs757DisplayFilesNotice FilesNotice => _fake.FilesNotice;
        public void Speak(string text) => _fake.Speak(text);
        public void Log(string channel, string line) => _fake.Log(channel, line);
        public Task<ModuleAnalysis> AnalyseAsync() => _fake.AnalyseAsync();
        public Task<ModuleLocation> LocateAsync() => _fake.LocateAsync();
    }

    /// <summary>Starts the FMA without its WinForms timer (the tests tick it); any other monitor as the app would.</summary>
    private static void StartWithoutTimer(IBbs757DisplayMonitor monitor, IBbs757DisplayEnvironment env)
    {
        if (monitor is Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading> fma) fma.Start(env, startTimer: false);
        else monitor.Start(env);
    }

    /// <summary>A disposed session refuses before it returns a task.</summary>
    private static void StartSearch(ModuleSession session) => session.LocateAsync();

    private static ModuleSession NewSession() => new(MSFSBlindAssist.DisplayLayouts.Bbs757Module.Description, new FakeHost(), new NoLayouts());

    private static Bbs757DisplayEnvironment Connected(ModuleSession session, string? title, Dictionary<string, double> cache,
        Dictionary<string, string> cacheKeys) =>
        new(() => true, () => true, () => title, key => cache.TryGetValue(key, out double v) ? v : null, cacheKeys,
            () => new UserSettings(), new SpeechCapture(), session, new Bbs757DisplayFilesNotice());

    private sealed class FakeMonitor : IBbs757DisplayMonitor
    {
        public List<string> Calls { get; } = new();
        public CoherentBbs757TabletClient? TabletClient { get; init; }
        public IReadOnlyList<Bbs757SimInput> SimInputs { get; init; } = Array.Empty<Bbs757SimInput>();

        public void Start(IBbs757DisplayEnvironment env) => Calls.Add("start");
        public void Restart() => Calls.Add("restart");
        public void Stop() => Calls.Add(TabletClient is { IsDisposed: false } ? "stop while the tablet client lived" : "stop");
        public bool KeyFallsBackToAi() => true;
        public IReadOnlyList<string> View() => Array.Empty<string>();
        public void Dispose() => Stop();
    }

    /// <summary>The real environment, with the simulator up and every input read as 1.</summary>
    private sealed class SimulatorUp : IBbs757DisplayEnvironment
    {
        private readonly IBbs757DisplayEnvironment _inner;

        public SimulatorUp(IBbs757DisplayEnvironment inner) => _inner = inner;

        public long NowMs => _inner.NowMs;
        public Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys) => new Bbs757SimValues(keys.ToDictionary(k => k, _ => 1.0));
        public bool IsMuted(string muteKey) => false;
        public Bbs757DisplayFilesNotice FilesNotice => _inner.FilesNotice;
        public void Speak(string text) => _inner.Speak(text);
        public void Log(string channel, string line) => _inner.Log(channel, line);
        public Task<ModuleAnalysis> AnalyseAsync() => _inner.AnalyseAsync();
        public Task<ModuleLocation> LocateAsync() => _inner.LocateAsync();
    }

    /// <summary>No installed files and no simulator process; the work runs at once.</summary>
    private sealed class FakeHost : IModuleHost
    {
        public int FindCalls { get; private set; }
        public long NowMs => 0;
        public string SimulatorVersion => "FS2024";

        public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log)
        {
            FindCalls++;
            return Array.Empty<string>();
        }

        public byte[] ReadFile(string path) => throw new InvalidOperationException($"the session read {path}");

        public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
        {
            failure = null;
            return null;
        }

        public void Log(string line) { }

        public Task<T> RunInBackground<T>(string name, Func<T> work)
        {
            try
            {
                return Task.FromResult(work());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }

    private sealed class NoLayouts : ILayoutSource
    {
        public LayoutLookup Find(string wasmFile) => new(null, "no layout");
    }
}
