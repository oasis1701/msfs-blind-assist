// Characterization tests for the app-wide cabin-ready call: FenixCabinReadyEdge, the
// known false-to-true change after a silent baseline, and FenixCabinReadyMonitor, which speaks it once on
// the UI thread. The monitor is built with fakes: a source that captures the value callback and counts its
// disposal, and a SynchronizationContext that queues posts until RunAll(). No test builds a
// FenixDataService or a FenixDataPoller (a live start would open a socket to 127.0.0.1:8083), so the
// definition's wiring is checked by reading FenixA320Definition.Cabin.cs.
//
// The same monitor also says the doors, the slides and the cautions
// (FenixCabinEvents), each kind mutable by its Ctrl+M row and the doors left to the Cabin window's read-back
// while the pilot's own press is pending (FenixCabinOwnPresses). Those tests feed whole polls, every cabin
// name in order as the poller hands them on, with a fake clock, a fake mute list and fake settings.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixCabinReadyMonitorTests
{
    private const string CabinReady = "aircraft.communication.cids.cabinReady";
    private const string CabinReadyAvailable = "aircraft.communication.cids.cabinReady.available";

    private static readonly FenixDataValue True = new(FenixValueKind.Bool, 0, true, null, "true");
    private static readonly FenixDataValue False = new(FenixValueKind.Bool, 0, false, null, "false");
    private static readonly FenixDataValue Unknown = FenixDataValue.Unknown;
    private static readonly FenixDataValue JsonNull = new(FenixValueKind.Null, 0, false, null, "null");
    private static readonly FenixDataValue NumberOne = new(FenixValueKind.Number, 1, false, null, "1");

    // ---- Fakes ----------------------------------------------------------------------------------

    /// <summary>A UI thread that queues each post and runs the queue on <see cref="RunAll"/>.</summary>
    private sealed class QueueContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _posted = new();

        public int Pending { get { lock (_posted) return _posted.Count; } }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_posted) _posted.Add((d, state));
        }

        public void RunAll()
        {
            List<(SendOrPostCallback Callback, object? State)> batch;
            lock (_posted)
            {
                batch = new(_posted);
                _posted.Clear();
            }
            foreach (var (callback, state) in batch) callback(state);
        }
    }

    /// <summary>A UI thread that is gone: WinForms' BeginInvoke once the window handle is destroyed.</summary>
    private sealed class ClosedContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) =>
            throw new InvalidOperationException(
                "Invoke or BeginInvoke cannot be called on a control until the window handle has been created.");
    }

    /// <summary>Stands in for the poller: captures the value callback and counts disposals.</summary>
    private sealed class FakeSource
    {
        private int _disposed;

        public Action<string, FenixDataValue>? Callback { get; private set; }
        public int Disposed => Volatile.Read(ref _disposed);

        public IDisposable Subscribe(Action<string, FenixDataValue> onValue)
        {
            Callback = onValue;
            return new Handle(this);
        }

        public void Send(string name, FenixDataValue value) => Callback!(name, value);

        private sealed class Handle(FakeSource owner) : IDisposable
        {
            public void Dispose() => Interlocked.Increment(ref owner._disposed);
        }
    }

    private sealed class Spoken
    {
        private readonly List<string> _texts = new();

        public void Add(string text)
        {
            lock (_texts) _texts.Add(text);
        }

        public IReadOnlyList<string> Texts
        {
            get { lock (_texts) return _texts.ToArray(); }
        }
    }

    private static bool[] Run(params FenixDataValue[] values)
    {
        var edge = new FenixCabinReadyEdge();
        return values.Select(edge.Next).ToArray();
    }

    // ---- The edge -------------------------------------------------------------------------------

    [Fact]
    public void Edge_speaks_only_on_a_known_rise()
    {
        Assert.Equal([false, true], Run(False, True));
        // A cabin already ready at the first value is the silent baseline.
        Assert.Equal([false], Run(True));
        Assert.Equal([false, false, true], Run(True, False, True));
        // An unknown value between keeps the known false.
        Assert.Equal([false, false, true], Run(False, Unknown, True));
        // Unknown is no baseline: the first Bool is, and says nothing.
        Assert.Equal([false, false], Run(Unknown, True));
        Assert.Equal([false, false], Run(JsonNull, True));
        // A number is not a Bool: ignored, neither a rise nor a baseline.
        Assert.Equal([false, false, true], Run(False, NumberOne, True));
        Assert.Equal([false, false], Run(NumberOne, True));
        // Steady true never repeats.
        Assert.Equal([false, true, false, false], Run(False, True, True, True));
    }

    // ---- The monitor ----------------------------------------------------------------------------

    [Fact]
    public async Task Speaks_on_the_UI_context()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);

        // The first value is the silent baseline; the rise after it is the one that speaks.
        source.Send(CabinReady, False);
        await Task.Run(() => source.Send(CabinReady, True));

        Assert.Empty(spoken.Texts);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Speaks_once_per_rise_and_never_at_connect()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        // Already ready when the app connects: the first value is the silent baseline.
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);

        source.Send(CabinReady, True);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Ignores_other_names()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);

        source.Send(CabinReadyAvailable, False);
        source.Send(CabinReadyAvailable, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        // The other name's false set no baseline: the first cabin-ready value is the silent one.
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);
        Assert.Equal(0, ui.Pending);
    }

    [Fact]
    public void Dispose_stops_speech_and_disposes_the_subscription()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);

        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        Assert.Equal(1, ui.Pending);

        monitor.Dispose();
        Assert.Equal(1, source.Disposed);

        // The post queued before Dispose says nothing.
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        // A late value from the source (its own Dispose is best effort) says nothing either.
        source.Send(CabinReady, False);
        source.Send(CabinReady, True);
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        monitor.Dispose();
        Assert.Equal(1, source.Disposed);
    }

    [Fact]
    public void A_closed_context_does_not_throw()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, new ClosedContext(), spoken.Add);
        source.Send(CabinReady, False);

        var thrown = Record.Exception(() => source.Send(CabinReady, True));

        Assert.Null(thrown);
        Assert.Empty(spoken.Texts);
    }

    [Fact]
    public void Without_a_UI_context_it_speaks_inline()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, null, spoken.Add);

        source.Send(CabinReady, False);
        source.Send(CabinReady, True);

        Assert.Equal(["Cabin ready"], spoken.Texts);
    }

    [Fact]
    public void Spoken_text_is_cabin_ready()
    {
        Assert.Equal("Cabin ready", FenixCabinReadyMonitor.SpokenText);
    }

    // ---- The background announcements -----------------------------------------------------------

    private static readonly DateTime T0 = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeClock
    {
        public DateTime Now { get; set; } = T0;
    }

    // One poll as the poller hands it on: every cabin name in order, the given values, Unknown for the rest.
    private static void SendPoll(FakeSource source, params (string Name, FenixDataValue Value)[] values)
    {
        var given = values.ToDictionary(v => v.Name, v => v.Value);
        foreach (string name in FenixCabinNames.All)
            source.Send(name, given.TryGetValue(name, out FenixDataValue value) ? value : Unknown);
    }

    private static (string, FenixDataValue)[] Slides(FenixDataValue value, params string[] keys)
        => (keys.Length > 0 ? keys : new[] { "d1l", "d1r", "d4l", "d4r" }).Select(key => (FenixCabinNames.Slide(key), value)).ToArray();

    private static (string, FenixDataValue)[] With(params (string, FenixDataValue)[][] parts) => parts.SelectMany(p => p).ToArray();

    private static FenixSetting Setting(string key, string value)
        => new(key, "Aircraft", value, Array.Empty<string>(), null, null, true);

    [Fact]
    public void Doors_slides_and_cautions_are_said_on_the_UI_context_once_per_poll()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);

        // The first poll is the silent baseline.
        SendPoll(source, With(Slides(True), new[]
        {
            (CabinReady, False), (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.WaterServicePanelOpen, False)
        }));
        ui.RunAll();
        Assert.Empty(spoken.Texts);

        // A poll is judged once, when its last name has come: nothing before that.
        var changed = With(Slides(False), new[]
        {
            (CabinReady, True), (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.WaterServicePanelOpen, True)
        }).ToDictionary(v => v.Item1, v => v.Item2);
        IReadOnlyList<string> names = FenixCabinNames.All;
        for (int i = 0; i < names.Count - 1; i++)
            source.Send(names[i], changed.TryGetValue(names[i], out FenixDataValue value) ? value : Unknown);
        ui.RunAll();
        Assert.Equal(["Cabin ready"], spoken.Texts);

        source.Send(names[^1], Unknown);
        Assert.Equal(["Cabin ready"], spoken.Texts);
        ui.RunAll();
        Assert.Equal(["Cabin ready", "Door 1 left open", "Slides disarmed", "Cabin caution: Water service door open"], spoken.Texts);

        // The same values again: nothing more.
        SendPoll(source, changed.Select(kv => (kv.Key, kv.Value)).ToArray());
        ui.RunAll();
        Assert.Equal(4, spoken.Texts.Count);
    }

    [Fact]
    public void A_muted_kind_says_nothing_and_the_others_still_speak()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var muted = new HashSet<string> { FenixCabinAnnouncements.DoorsKey, FenixCabinAnnouncements.CabinReadyKey };
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add, isMuted: muted.Contains);

        SendPoll(source, With(Slides(True), new[] { (CabinReady, False), (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.WasteTankFull, False) }));
        SendPoll(source, With(Slides(False), new[] { (CabinReady, True), (FenixCabinNames.Door("d1l"), True), (FenixCabinNames.WasteTankFull, True) }));
        ui.RunAll();
        Assert.Equal(["Slides disarmed", "Cabin caution: Vacuum system disabled: waste tank full"], spoken.Texts);

        // The mute list is read when the words are said: a row muted or unmuted after the poll counts at once.
        SendPoll(source, With(Slides(True), new[] { (CabinReady, False), (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.WasteTankFull, False) }));
        SendPoll(source, With(Slides(True), new[] { (CabinReady, True), (FenixCabinNames.Door("d1l"), False), (FenixCabinNames.WasteTankFull, False) }));
        muted.Remove(FenixCabinAnnouncements.DoorsKey);
        muted.Remove(FenixCabinAnnouncements.CabinReadyKey);
        muted.Add(FenixCabinAnnouncements.SlidesKey);
        ui.RunAll();
        Assert.Equal(
            ["Slides disarmed", "Cabin caution: Vacuum system disabled: waste tank full", "Door 1 left closed", "Cabin ready"],
            spoken.Texts);
    }

    [Fact]
    public void A_door_the_pilot_pressed_is_left_to_the_windows_read_back_until_its_window_ends()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var presses = new FenixCabinOwnPresses();
        var clock = new FakeClock();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add,
            ownPresses: presses, clock: () => clock.Now);

        (string, FenixDataValue)[] Doors(bool d1l, bool d4r, bool forward) => new[]
        {
            (FenixCabinNames.Door("d1l"), d1l ? True : False), (FenixCabinNames.Door("d4r"), d4r ? True : False),
            (FenixCabinNames.CargoForward, forward ? True : False), (FenixCabinNames.CargoFwdMoving, False)
        };
        SendPoll(source, Doors(false, false, false));

        // The pilot opens door 1 left and the forward cargo door from the Cabin window; door 4 right opens by
        // itself. Only door 4 right is said here: the window reads its own presses back.
        presses.Mark("door.d1l", T0, TimeSpan.FromSeconds(30));
        presses.Mark("cargo.forward", T0, TimeSpan.FromSeconds(30));
        clock.Now = T0 + TimeSpan.FromSeconds(5);
        SendPoll(source, Doors(true, true, true));
        ui.RunAll();
        Assert.Equal(["Door 4 right open"], spoken.Texts);

        // After the window: door 1 left closing is said, and the opening the window spoke is not said again.
        clock.Now = T0 + TimeSpan.FromSeconds(31);
        SendPoll(source, Doors(false, true, true));
        ui.RunAll();
        Assert.Equal(["Door 4 right open", "Door 1 left closed"], spoken.Texts);
    }

    [Fact]
    public void A_door_change_after_a_press_window_has_ended_is_said()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var presses = new FenixCabinOwnPresses();
        var clock = new FakeClock();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add,
            ownPresses: presses, clock: () => clock.Now);
        SendPoll(source, (FenixCabinNames.Door("d1l"), False));

        // The read-back waited its 30 s and the door had not moved: when it does, it is said.
        presses.Mark("door.d1l", T0, TimeSpan.FromSeconds(30));
        clock.Now = T0 + TimeSpan.FromSeconds(30);
        SendPoll(source, (FenixCabinNames.Door("d1l"), True));
        ui.RunAll();
        Assert.Equal(["Door 1 left open"], spoken.Texts);
    }

    [Fact]
    public void The_variant_lays_the_cabin_out()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, null, spoken.Add);

        // Until the variant is known the cabin is the FAP's A320, which has no door 2.
        SendPoll(source, (FenixCabinNames.Door("d2l"), False));
        SendPoll(source, (FenixCabinNames.Door("d2l"), True));
        Assert.Empty(spoken.Texts);

        monitor.OnVariantChanged(FenixAircraftType.A321);
        SendPoll(source, (FenixCabinNames.Door("d2l"), False));
        Assert.Equal(["Door 2 left closed"], spoken.Texts);

        // A monitor started on an A321 has door 2 from the start.
        var a321Source = new FakeSource();
        var a321Spoken = new Spoken();
        using var a321 = new FenixCabinReadyMonitor(a321Source.Subscribe, null, a321Spoken.Add, type: FenixAircraftType.A321);
        SendPoll(a321Source, (FenixCabinNames.Door("d3r"), False));
        SendPoll(a321Source, (FenixCabinNames.Door("d3r"), True));
        Assert.Equal(["Door 3 right open"], a321Spoken.Texts);
    }

    [Fact]
    public void The_settings_are_read_at_start_when_the_Fenix_starts_answering_and_on_a_variant_change()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        int reads = 0;
        IReadOnlyList<FenixSetting> answer = Array.Empty<FenixSetting>();
        Task<IReadOnlyList<FenixSetting>> ReadSettings()
        {
            Interlocked.Increment(ref reads);
            return Task.FromResult(answer);
        }
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, null, spoken.Add, readSettings: ReadSettings);

        // At start, before the Fenix answers: an empty answer keeps the A320's own layout.
        Assert.Equal(1, reads);
        SendPoll(source);
        SendPoll(source);
        Assert.Equal(1, reads);

        // The Fenix starts answering: the settings are read again, once. This cabin has every door full size.
        answer = new[] { Setting("emergency_exits_type", "DoubleFullsize") };
        SendPoll(source, (FenixCabinNames.Door("d2l"), False));
        Assert.Equal(2, reads);
        SendPoll(source, (FenixCabinNames.Door("d2l"), False));
        Assert.Equal(2, reads);
        SendPoll(source, (FenixCabinNames.Door("d2l"), True));
        Assert.Equal(["Door 2 left open"], spoken.Texts);

        // Down and back: read again. A variant change: read again.
        SendPoll(source);
        SendPoll(source, (FenixCabinNames.Door("d2l"), True));
        Assert.Equal(3, reads);
        monitor.OnVariantChanged(FenixAircraftType.A320);
        Assert.Equal(4, reads);

        // An empty answer keeps what an earlier read found: door 2 is still there.
        answer = Array.Empty<FenixSetting>();
        monitor.OnVariantChanged(FenixAircraftType.A320);
        SendPoll(source, (FenixCabinNames.Door("d2l"), False));
        Assert.Equal(["Door 2 left open", "Door 2 left closed"], spoken.Texts);
    }

    // ---- A long outage: a new silent baseline ---------------------------------------------------

    private static (string, FenixDataValue)[] Cabin(bool ready, bool doorOpen, bool armed, bool panelOpen)
        => With(Slides(armed ? True : False), new[]
        {
            (CabinReady, ready ? True : False), (FenixCabinNames.Door("d1l"), doorOpen ? True : False),
            (FenixCabinNames.WaterServicePanelOpen, panelOpen ? True : False)
        });

    private static (FenixCabinReadyMonitor Monitor, FakeSource Source, Spoken Spoken, FakeClock Clock) Outage()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        var clock = new FakeClock();
        var monitor = new FenixCabinReadyMonitor(source.Subscribe, null, spoken.Add, clock: () => clock.Now);
        return (monitor, source, spoken, clock);
    }

    [Fact]
    public void After_more_than_10_s_without_an_answer_the_first_answer_is_a_new_silent_baseline()
    {
        // A second flight loaded in the same session: everything differs, nothing of it is said.
        var (monitor, source, spoken, clock) = Outage();
        using (monitor)
        {
            SendPoll(source, Cabin(ready: false, doorOpen: false, armed: true, panelOpen: false));
            clock.Now = T0 + TimeSpan.FromSeconds(1);
            SendPoll(source);
            clock.Now = T0 + TimeSpan.FromSeconds(6);
            SendPoll(source);
            clock.Now = T0 + TimeSpan.FromSeconds(10.5);
            SendPoll(source, Cabin(ready: true, doorOpen: true, armed: false, panelOpen: true));
            Assert.Empty(spoken.Texts);

            // From the new baseline on, a change is said again, of every kind.
            clock.Now = T0 + TimeSpan.FromSeconds(11.5);
            SendPoll(source, Cabin(ready: false, doorOpen: false, armed: true, panelOpen: false));
            clock.Now = T0 + TimeSpan.FromSeconds(12.5);
            SendPoll(source, Cabin(ready: true, doorOpen: false, armed: true, panelOpen: true));
            Assert.Equal(["Door 1 left closed", "Slides armed", "Cabin ready", "Cabin caution: Water service door open"], spoken.Texts);
        }
    }

    [Fact]
    public void An_outage_of_10_s_or_less_keeps_the_baseline()
    {
        var (monitor, source, spoken, clock) = Outage();
        using (monitor)
        {
            SendPoll(source, Cabin(ready: false, doorOpen: false, armed: true, panelOpen: false));
            clock.Now = T0 + TimeSpan.FromSeconds(1);
            SendPoll(source);
            clock.Now = T0 + TimeSpan.FromSeconds(10);
            SendPoll(source, Cabin(ready: true, doorOpen: true, armed: false, panelOpen: true));

            Assert.Equal(["Cabin ready", "Door 1 left open", "Slides disarmed", "Cabin caution: Water service door open"], spoken.Texts);
        }
    }

    [Fact]
    public void Polls_that_answer_anything_are_no_outage_however_long()
    {
        // 30 s of polls in which only one unrelated value answers: the door's baseline holds, its change is said.
        var (monitor, source, spoken, clock) = Outage();
        using (monitor)
        {
            SendPoll(source, Cabin(ready: false, doorOpen: false, armed: true, panelOpen: false));
            for (int second = 5; second <= 30; second += 5)
            {
                clock.Now = T0 + TimeSpan.FromSeconds(second);
                SendPoll(source, (FenixCabinNames.FapOperational, True));
            }
            clock.Now = T0 + TimeSpan.FromSeconds(31);
            SendPoll(source, Cabin(ready: true, doorOpen: true, armed: true, panelOpen: false));

            Assert.Equal(["Cabin ready", "Door 1 left open"], spoken.Texts);
        }
    }

    [Fact]
    public void Dispose_stops_the_background_announcements_too()
    {
        var ui = new QueueContext();
        var source = new FakeSource();
        var spoken = new Spoken();
        var monitor = new FenixCabinReadyMonitor(source.Subscribe, ui, spoken.Add);
        SendPoll(source, (FenixCabinNames.Door("d1l"), False));
        SendPoll(source, (FenixCabinNames.Door("d1l"), True));
        Assert.Equal(1, ui.Pending);

        monitor.Dispose();
        ui.RunAll();
        SendPoll(source, (FenixCabinNames.Door("d1l"), False));
        ui.RunAll();

        Assert.Empty(spoken.Texts);
        Assert.Equal(0, ui.Pending);
    }

    [Fact]
    public void A_closed_context_drops_the_background_announcements_without_throwing()
    {
        var source = new FakeSource();
        var spoken = new Spoken();
        using var monitor = new FenixCabinReadyMonitor(source.Subscribe, new ClosedContext(), spoken.Add);
        SendPoll(source, (FenixCabinNames.Door("d1l"), False));

        var thrown = Record.Exception(() => SendPoll(source, (FenixCabinNames.Door("d1l"), True)));

        Assert.Null(thrown);
        Assert.Empty(spoken.Texts);
    }

    // ---- The definition's seams -----------------------------------------------------------------

    private static string AppFile(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist" }.Concat(parts).ToArray()));

    [Fact]
    public void The_definition_starts_and_stops_the_monitor()
    {
        string source = AppFile("Aircraft", "FenixA320Definition.Cabin.cs");

        // The definition is passed for its variant (the layout) and its VariantChanged.
        string start = MethodBody(source, "partial void StartCabinBackground(");
        Assert.Contains("FenixCabinReadyMonitor.Start(DataService, announcer, this)", start);

        string stop = MethodBody(source, "partial void StopCabinBackground(");
        Assert.Contains("_cabinReadyMonitor?.Dispose()", stop);
        Assert.Contains("_cabinReadyMonitor = null", stop);
    }

    [Fact]
    public void The_app_wide_monitor_polls_every_cabin_name_and_reads_the_fenix_mute_list_and_the_shared_presses()
    {
        string start = MethodBody(AppFile("Services", "FenixCabinReadyMonitor.cs"), "public static FenixCabinReadyMonitor Start(");

        // One poll for every kind: every cabin name, through the data service's batched read.
        Assert.Contains("new FenixDataPoller(FenixCabinNames.All, data.ReadManyAsync", start);
        // The Ctrl+M mute list, as current right after a toggle (SettingsManager.Save rebuilds the set).
        Assert.Contains("SettingsManager.Current.FenixDisabledMonitorVariablesSet.Contains(", start);
        // The one registry the Cabin window marks its door presses in.
        Assert.Contains("FenixCabinOwnPresses.Shared", start);
        Assert.Contains("definition.VariantChanged +=", start);
        Assert.Contains("data.GetSettingsAsync()", start);

        // The window marks its door presses in that registry unless a test gives it its own.
        string window = AppFile("Forms", "Fenix", "FenixCabinForm.Controls.cs");
        Assert.Contains("OwnPresses { get; set; } = FenixCabinOwnPresses.Shared;", window);
    }

    // The braces of the method whose declaration starts with `declaration`, inclusive.
    private static string MethodBody(string source, string declaration)
    {
        int at = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(at >= 0, $"`{declaration}` is not in the file");
        int open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"`{declaration}` has no body");
        Assert.DoesNotContain(";", source[at..open]);

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"`{declaration}` has no closing brace");
        return "";
    }
}
