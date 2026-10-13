using System.Globalization;
using System.Text;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Forms.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300 MCDU window driven without a sim or a screen: the screens it reads, the clock it keeps and the waits
/// it makes are the form's own seams, and the definition is faked as in <see cref="IniA300McduKeyTests"/>. The
/// window is never shown, so every test calls what the shown window's poll and its show would call.
/// </summary>
public sealed class A300McduFormTests : IDisposable
{
    private const string ExportWrite = "1 (>L:INI_MCDU_OPTION)";
    private const string CaptainClr = "1 (>L:INI_MCDU1_CLR)";
    private const string CaptainKeyDown = "1 (>L:INI_MCDU1_";
    private const string ClearFailed = "Could not clear the scratchpad";

    private static readonly DateTime T0 = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly A300McduUnit Captain = A300McduUnit.Captain;
    private static readonly A300McduUnit FirstOfficer = A300McduUnit.FirstOfficer;

    private readonly IniA300Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly SpeechCapture _speech = new();
    private readonly List<string> _sent = new();
    private readonly Dictionary<A300McduUnit, A300McduScreen?> _screens = new();
    private readonly A300McduForm _form;
    private DateTime _now = T0;
    private bool _canLand = true;

    /// <summary>What the aircraft does when the captain's CLR key goes down.</summary>
    private Action? _onCaptainClr;

    /// <summary>What the aircraft does when any captain key goes down, given the key's name ("K", "CLR", "NEXT").</summary>
    private Action<string>? _onCaptainKey;

    // The key queue and the aircraft can run on the window's fake clock (_clockedKeys): each wait the definition makes
    // (a key's hold and its gap) and each client-data delivery is a timer, fired when the window's own waits move the
    // clock past it. By default a key is pressed and released at once, and the queue is never seen to hold anything.
    private readonly List<(DateTime Due, Action Fire)> _timers = new();
    private bool _clockedKeys;
    private bool _keysStuck;

    /// <summary>What each of the pump's waits runs long by. The real ones do: Task.Delay(80) takes about 93 ms, so a key is
    /// about 187 ms and not the nominal 160 the read-back's hold is counted at.</summary>
    private int _keyWaitExtraMs;
    private string _pad = "";
    private int _clrOnEmptyPad;

    public A300McduFormTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => _canLand,
            Send = (_, rpn) =>
            {
                _sent.Add(rpn);
                if (rpn == CaptainClr)
                    _onCaptainClr?.Invoke();
                if (rpn.StartsWith(CaptainKeyDown, StringComparison.Ordinal))
                    _onCaptainKey?.Invoke(rpn[CaptainKeyDown.Length..^1]);
            },
            Delay = KeyWait,
        };
        _def.Attach(_sim);
        _form = new A300McduForm(_def, _sim, _speech)
        {
            Screens = unit => _screens.GetValueOrDefault(unit),
            HasFeed = () => true,
            UtcNow = () => _now,
            Delay = ms =>
            {
                Advance(ms);
                return Task.CompletedTask;
            },
        };
    }

    public void Dispose() => _form.Dispose();

    // ---- The fake clock ----

    /// <summary>The definition's waits: over at once by default, on the fake clock for <see cref="_clockedKeys"/>,
    /// never over for <see cref="_keysStuck"/>.</summary>
    private Task KeyWait(int ms)
    {
        if (_keysStuck)
            return new TaskCompletionSource().Task;
        if (!_clockedKeys)
            return Task.CompletedTask;
        var done = new TaskCompletionSource();
        After(ms + _keyWaitExtraMs, () => done.SetResult());
        return done.Task;
    }

    private void After(int ms, Action fire) => _timers.Add((_now.AddMilliseconds(ms), fire));

    /// <summary>Moves the clock on, firing each timer that falls due on the way at its own time, since a key pump's
    /// step starts the next.</summary>
    private void Advance(int ms)
    {
        var target = _now.AddMilliseconds(ms);
        while (true)
        {
            int next = -1;
            for (int i = 0; i < _timers.Count; i++)
                if (_timers[i].Due <= target && (next < 0 || _timers[i].Due < _timers[next].Due))
                    next = i;
            if (next < 0)
                break;
            var (due, fire) = _timers[next];
            _timers.RemoveAt(next);
            if (due > _now)
                _now = due;
            fire();
        }
        _now = target;
    }

    /// <summary>The captain's scratchpad as the A300 drives it from the key L:vars: a letter or digit key types its
    /// character, CLR deletes one, and CLR on an empty scratchpad types "CLR" into it ([A300-4]). The screen shows each
    /// result after a client-data delivery of <paramref name="deliveryMs"/>.</summary>
    private void AircraftHolds(string pad, int deliveryMs = 100)
    {
        _pad = pad;
        _screens[Captain] = Page(scratchpad: pad);
        _onCaptainKey = key =>
        {
            if (key == "CLR")
            {
                if (_pad.Length == 0)
                {
                    _clrOnEmptyPad++;
                    _pad = "CLR";
                }
                else
                {
                    _pad = _pad[..^1];
                }
            }
            else if (key.Length == 1)
            {
                _pad += key;
            }
            else
            {
                return;
            }
            var shown = _pad;
            After(deliveryMs, () => _screens[Captain] = Page(scratchpad: shown));
        };
    }

    // ---- Screens ----

    /// <summary>A 1008-byte area holding <paramref name="rows"/> (index = screen row), green, large font.</summary>
    private static byte[] Area(IEnumerable<(int Row, string Text)> rows)
    {
        var bytes = new byte[A300McduText.DataSize];
        foreach (var (row, text) in rows)
        {
            var ascii = Encoding.ASCII.GetBytes(text);
            for (int col = 0; col < ascii.Length && col < A300McduText.Cols; col++)
            {
                int at = (row * A300McduText.Cols + col) * A300McduText.CellBytes;
                bytes[at] = ascii[col];
                bytes[at + 1] = 3;
            }
        }
        return bytes;
    }

    private const string InitTitle = "          INIT      b   ";

    /// <summary>The INIT page as the captain's MCDU showed it at the gate; <paramref name="more"/> replaces whole rows.</summary>
    private static A300McduScreen Page(A300McduUnit unit = A300McduUnit.Captain, string title = InitTitle,
        string scratchpad = "NAV ACCUR DOWNGRADED", params (int Row, string Text)[] more)
    {
        var rows = new Dictionary<int, string>
        {
            [0] = title,
            [1] = " CO RTE          FROM/TO",
            [2] = "!!!!!!!/!!!!   !!!!/!!!!",
            [9] = "CRZ FL        TEMP/TROPO",
            [10] = "!!!           -56^/36090",
            [13] = scratchpad,
        };
        foreach (var (row, text) in more)
            rows[row] = text;
        return A300McduText.Decode(unit, Area(rows.Select(r => (r.Key, r.Value))))!;
    }

    private static A300McduScreen Blank(A300McduUnit unit = A300McduUnit.Captain) =>
        A300McduText.Decode(unit, new byte[A300McduText.DataSize])!;

    private int IndexOf(string startsWith) => _form.Lines.ToList().FindIndex(l => l.StartsWith(startsWith, StringComparison.Ordinal));

    private void PollAt(int msAfterStart)
    {
        _now = T0.AddMilliseconds(msAfterStart);
        _form.Tick();
    }

    // ---- The export ----

    [Fact]
    public void A_poll_switches_the_export_on_until_it_is_confirmed()
    {
        _canLand = false;
        _form.Tick();
        Assert.DoesNotContain(_sent, s => s.Contains("INI_MCDU_OPTION"));
        _canLand = true;
        _form.Tick();
        _form.Tick();
        Assert.Single(_sent, s => s.Contains("INI_MCDU_OPTION"));
    }

    [Fact]
    public void A_poll_sends_the_export_again_after_a_flight_load()
    {
        _form.Tick();
        _form.Tick();
        Assert.Equal(new[] { ExportWrite }, _sent);
        _def.OnSimContextReset();   // the flight load switches the option off while the window stays open
        _form.Tick();
        _form.Tick();
        Assert.Equal(new[] { ExportWrite, ExportWrite }, _sent);
    }

    // ---- The row ----

    [Fact]
    public void A_lost_feed_replaces_the_page_with_the_waiting_row()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        Assert.Contains(_form.Lines, l => l.StartsWith("Title: INIT", StringComparison.Ordinal));
        _screens[Captain] = null;   // a reconnect's new data manager has delivered nothing yet
        _form.Tick();
        Assert.Equal(new[] { "Captain MCDU: waiting for data" }, _form.Lines);
        Assert.Equal("Captain MCDU: waiting for data", _form.Status);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_page_that_returns_after_the_waiting_row_is_drawn_again()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        _screens[Captain] = null;
        _form.Tick();
        _screens[Captain] = Page(title: "          F-PLN       ");
        _form.Tick();
        Assert.Contains(_form.Lines, l => l.StartsWith("Title: F-PLN", StringComparison.Ordinal));
        Assert.Equal(new[] { "F-PLN" }, _speech.All);   // a new page is named once
    }

    [Fact]
    public void Without_a_data_manager_the_status_says_so_and_the_page_stays()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        var before = _form.Lines.ToList();
        _form.HasFeed = () => false;
        _form.Tick();
        Assert.Equal("Captain MCDU: not connected", _form.Status);
        Assert.Equal(before, _form.Lines);
        Assert.Empty(_speech.All);
    }

    // ---- The scratchpad read-back ----

    [Fact]
    public void Opening_on_a_blank_frame_does_not_seed_the_scratchpad()
    {
        _screens[Captain] = Blank();
        _form.PrepareToShow();
        _screens[Captain] = Page();   // the MCDU already held NAV ACCUR DOWNGRADED when the window opened
        for (int poll = 1; poll <= 3; poll++)
            PollAt(250 * poll);
        Assert.DoesNotContain("NAV ACCUR DOWNGRADED", _speech.All);
    }

    [Fact]
    public void A_scratchpad_change_after_the_first_page_is_still_read_back()
    {
        _screens[Captain] = Blank();
        _form.PrepareToShow();
        _screens[Captain] = Page();
        for (int poll = 1; poll <= 3; poll++)
            PollAt(250 * poll);
        _screens[Captain] = Page(scratchpad: "FPLN DISCONTINUITY");
        for (int poll = 4; poll <= 6; poll++)
            PollAt(250 * poll);
        Assert.Contains("FPLN DISCONTINUITY", _speech.All);
    }

    [Fact]
    public void Opening_on_a_page_seeds_the_scratchpad_silently()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        for (int poll = 1; poll <= 3; poll++)
            PollAt(250 * poll);
        Assert.Empty(_speech.All);
    }

    // ---- Showing the window again ----

    [Fact]
    public void A_first_show_lands_on_the_title()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        Assert.Equal(0, _form.SelectedRow);
        Assert.StartsWith("Title:", _form.Lines[0], StringComparison.Ordinal);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_reshow_on_the_same_page_keeps_the_line()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        int line4 = IndexOf("4:");
        Assert.True(line4 > 0);
        _form.SelectedRow = line4;
        _form.PrepareToShow();
        Assert.Equal(line4, _form.SelectedRow);
    }

    [Fact]
    public void A_reshow_on_the_same_page_finds_the_line_by_its_row_not_its_index()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        int line5 = IndexOf("5:");
        _form.SelectedRow = line5;
        // While the window was hidden the pilot's entry changed line 5 and a label appeared above line 2: every row
        // from there on moves down one, and the line's old text is gone. The title is the same, so the page is.
        _screens[Captain] = Page(more: new[] { (3, " ALTN"), (10, "FL350          -56^/36090") });
        _form.PrepareToShow();
        Assert.StartsWith("5: FL350", _form.Lines[_form.SelectedRow], StringComparison.Ordinal);
    }

    [Fact]
    public void A_reshow_on_a_new_page_lands_on_the_title()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        _form.SelectedRow = IndexOf("4:");
        _screens[Captain] = Page(title: "          F-PLN       ");
        _form.PrepareToShow();
        Assert.Equal(0, _form.SelectedRow);
        Assert.Empty(_speech.All);   // the title is read by the focused row, never announced
    }

    // ---- The clear-all ----

    /// <summary>The captain's scratchpad loses its last character each time his CLR goes down.</summary>
    private void CaptainClrDeletesOneCharacter() => _onCaptainClr = () =>
    {
        var pad = _screens[Captain]!.Scratchpad;
        _screens[Captain] = Page(scratchpad: pad.Length > 0 ? pad[..^1] : pad);
    };

    private int CaptainClrPresses => _sent.Count(s => s == CaptainClr);

    [Fact]
    public async Task The_clear_presses_the_unit_it_started_on()
    {
        _screens[Captain] = Page(scratchpad: "AB");
        _screens[FirstOfficer] = Page(FirstOfficer, scratchpad: "XY");
        CaptainClrDeletesOneCharacter();
        bool switched = false;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (!switched)
            {
                switched = true;
                _form.SelectUnit(FirstOfficer);   // the pilot moves to the other MCDU while the clear runs
            }
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();

        Assert.True(switched);
        Assert.Equal(2, CaptainClrPresses);
        Assert.DoesNotContain(_sent, s => s.Contains("INI_MCDU2"));
        Assert.DoesNotContain(ClearFailed, _speech.All);
        Assert.Equal("", _screens[Captain]!.Scratchpad);
        Assert.Equal("XY", _screens[FirstOfficer]!.Scratchpad);
    }

    [Fact]
    public async Task The_clear_waits_for_the_scratchpad_to_change_before_the_next_press()
    {
        _screens[Captain] = Page(scratchpad: "AB");   // the aircraft ignores CLR: the pad never changes
        await _form.ClearScratchpadAsync();
        Assert.Equal(1, CaptainClrPresses);
        Assert.Equal(new[] { ClearFailed }, _speech.All);
        var waited = _now - T0;
        Assert.InRange(waited.TotalMilliseconds, A300McduForm.ClearConfirmMs, A300McduForm.ClearConfirmMs + 100);
    }

    [Fact]
    public async Task The_clear_presses_again_as_soon_as_the_pad_has_changed()
    {
        _screens[Captain] = Page(scratchpad: "ABC");
        int polls = 0;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            polls++;
            if (polls == 3)   // the aircraft took three polls (150 ms) to show the first deletion
                _screens[Captain] = Page(scratchpad: "AB");
            return wait(ms);
        };

        // 150 ms in, the pad reads AB: the next CLR goes out, and the aircraft ignores that one.
        await _form.ClearScratchpadAsync();

        Assert.Equal(2, CaptainClrPresses);
        Assert.Equal(new[] { ClearFailed }, _speech.All);
        Assert.InRange((_now - T0).TotalMilliseconds, 150 + A300McduForm.ClearConfirmMs, 150 + A300McduForm.ClearConfirmMs + 100);
    }

    [Fact]
    public async Task A_blank_frame_after_a_press_is_no_change()
    {
        _screens[Captain] = Page(scratchpad: "AB");
        _onCaptainClr = () => _screens[Captain] = Blank();   // the page is being redrawn and stays blank
        await _form.ClearScratchpadAsync();
        Assert.Equal(1, CaptainClrPresses);
        Assert.Equal(new[] { ClearFailed }, _speech.All);
    }

    [Fact]
    public async Task A_blank_frame_between_a_press_and_the_redrawn_page_is_waited_out()
    {
        // Each CLR redraws the page: two blank frames (100 ms), then the page with one character fewer.
        _screens[Captain] = Page(scratchpad: "AB");
        var redraw = new Queue<A300McduScreen>();
        var pads = new Queue<string>(new[] { "A", "" });
        _onCaptainClr = () =>
        {
            redraw.Enqueue(Blank());
            redraw.Enqueue(Blank());
            redraw.Enqueue(Page(scratchpad: pads.Dequeue()));
        };
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (redraw.Count > 0)
                _screens[Captain] = redraw.Dequeue();
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();

        Assert.Equal(2, CaptainClrPresses);
        Assert.Empty(_speech.All);
        Assert.Equal("", _screens[Captain]!.Scratchpad);
    }

    [Fact]
    public async Task A_clear_on_an_empty_scratchpad_presses_nothing_and_says_so()
    {
        _screens[Captain] = Page(scratchpad: "");
        await _form.ClearScratchpadAsync();
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Scratchpad already empty" }, _speech.All);
    }

    [Fact]
    public async Task A_clear_on_a_blank_unit_presses_nothing_and_says_it_could_not()
    {
        _screens[Captain] = Blank();
        await _form.ClearScratchpadAsync();
        Assert.Empty(_sent);
        Assert.Equal(new[] { ClearFailed }, _speech.All);
    }

    [Fact]
    public async Task A_clear_that_cannot_reach_the_aircraft_says_so_once()
    {
        _screens[Captain] = Page(scratchpad: "AB");
        _canLand = false;
        await _form.ClearScratchpadAsync();
        Assert.Empty(_sent);
        Assert.Equal(new[] { "MCDU unavailable" }, _speech.All);
    }

    [Fact]
    public async Task A_clear_that_works_is_silent_and_stops_when_the_pad_is_empty()
    {
        _screens[Captain] = Page(scratchpad: "AB");
        CaptainClrDeletesOneCharacter();
        await _form.ClearScratchpadAsync();
        Assert.Equal(2, CaptainClrPresses);   // never a CLR on an empty pad: the A300 would type CLR into it
        Assert.Empty(_speech.All);
    }

    // ---- The clear-all and the key queue ----

    [Theory]
    [InlineData(100)]
    [InlineData(350)]   // an aircraft slow to show a key: the hold the entry set covers it, the empty queue alone does not
    public async Task Keys_still_in_the_queue_land_before_the_clear_reads_the_scratchpad(int deliveryMs)
    {
        // KLAX was just sent from the input box (four keys, 160 ms each) and Delete follows at once, with KJFK/ on the
        // screen. A change of the scratchpad is the only sign that a CLR landed, so a key still ahead of the CLR reads as
        // one, and each such change queued one more CLR: the extras land on an empty scratchpad, where the A300 types CLR
        // into it ([A300-4]).
        _clockedKeys = true;
        AircraftHolds("KJFK/", deliveryMs);
        _form.PrepareToShow();
        Assert.True(_form.Send(Captain, new[] { "K", "L", "A", "X" }));
        Assert.Equal(4, _def.McduKeysPending);

        await _form.ClearScratchpadAsync();
        Advance(5000);   // whatever the clear left in the queue lands now

        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal(9, CaptainClrPresses);   // KJFK/KLAX is nine characters: one CLR each, not one more
        Assert.Equal("", _pad);
        Assert.DoesNotContain(ClearFailed, _speech.All);
        for (int poll = 0; poll < 8; poll++)   // the window's poll then reads the empty scratchpad back, once
        {
            Advance(250);
            _form.Tick();
        }
        Assert.Equal(new[] { "Scratchpad cleared" }, _speech.All);
    }

    [Fact]
    public async Task Keys_ahead_of_the_clear_do_not_use_up_the_wait_for_its_effect()
    {
        // Twelve page keys are 1.9 s of queue, more than the 1.5 s the aircraft gets to answer a CLR. That time is
        // counted from the CLR's release, not from its enqueue.
        _clockedKeys = true;
        AircraftHolds("AB");
        _form.PrepareToShow();
        Assert.True(_form.Send(Captain, Enumerable.Repeat("NEXT", 12).ToList()));

        await _form.ClearScratchpadAsync();
        Advance(5000);

        Assert.DoesNotContain(ClearFailed, _speech.All);
        Assert.Equal(2, CaptainClrPresses);
        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal("", _pad);
    }

    [Fact]
    public async Task A_backspace_pressed_during_the_clear_lands_before_the_next_read()
    {
        _clockedKeys = true;
        AircraftHolds("ABC");
        _form.PrepareToShow();
        bool pressed = false;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (!pressed)
            {
                pressed = true;
                _form.Send(Captain, new[] { "CLR" });   // Backspace on the display, behind the clear's first CLR
            }
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();
        Advance(5000);

        Assert.True(pressed);
        Assert.Equal(3, CaptainClrPresses);   // three characters: the clear's CLRs and the Backspace share them
        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal("", _pad);
        Assert.DoesNotContain(ClearFailed, _speech.All);
    }

    [Fact]
    public async Task A_key_queue_that_never_moves_ends_the_clear_with_one_sentence()
    {
        _keysStuck = true;
        AircraftHolds("AB");
        _form.PrepareToShow();
        Assert.True(_form.Send(Captain, new[] { "K", "L" }));   // the first goes down, and the pump never gets on
        await _form.ClearScratchpadAsync();
        Assert.DoesNotContain(_sent, s => s == CaptainClr);
        Assert.Equal(new[] { ClearFailed }, _speech.All);
        // the queue has not shrunk for the 1.5 s the clear allows it
        Assert.InRange((_now - T0).TotalMilliseconds, A300McduForm.ClearConfirmMs, A300McduForm.ClearConfirmMs + 100);
    }

    // ---- The clear-all at the pump's real pace ----

    [Theory]
    [InlineData(9)]
    [InlineData(19)]
    public async Task A_pump_slower_than_its_nominal_pace_still_gets_its_margin_after_the_last_key(int keys)
    {
        // The real pump waits two real timers a key and Task.Delay(80) takes about 93 ms: about 187 ms a key, not the 160
        // the send's hold is counted at, so the margin that hold leaves after the last key's actual release is 0.25 s for
        // nine keys and nothing from about nineteen. Delete follows Enter at once, and the aircraft shows a key 350 ms
        // after it goes down.
        _clockedKeys = true;
        _keyWaitExtraMs = 15;   // 95 ms a wait, 190 ms a key
        AircraftHolds("", 350);
        _form.PrepareToShow();
        var entry = Enumerable.Range(0, keys).Select(i => ((char)('A' + i % 26)).ToString()).ToList();
        Assert.True(_form.Send(Captain, entry));

        await _form.ClearScratchpadAsync();
        Advance(8000);

        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal(keys, CaptainClrPresses);
        Assert.Equal("", _pad);
        Assert.DoesNotContain(ClearFailed, _speech.All);
    }

    [Fact]
    public async Task An_entry_typed_on_the_other_unit_after_the_clear_starts_does_not_end_it()
    {
        // Ctrl+Shift+R and nine keys on the first officer's MCDU while the captain's clear waits for its first CLR to go. The
        // keys share the one queue, so the clear waits for them; nothing is stuck, but they were not there when the wait
        // began, so a bound fixed from the queue at that moment ran out and the clear left the pad half cleared.
        _clockedKeys = true;
        _keyWaitExtraMs = 15;
        AircraftHolds("AB");
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        bool typed = false;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (!typed)
            {
                typed = true;
                _form.SelectUnit(FirstOfficer);
                _form.Send(FirstOfficer, "ABCDEFGHI".Select(c => c.ToString()).ToList());
            }
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();
        Advance(8000);

        Assert.True(typed);
        Assert.DoesNotContain(ClearFailed, _speech.All);
        Assert.Equal(2, CaptainClrPresses);
        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal("", _pad);
    }

    // ---- The clear-all after the pilot switches units ----

    [Theory]
    [InlineData(200)]
    [InlineData(350)]
    public async Task A_unit_switch_at_the_clears_first_wait_does_not_cut_the_wait_for_the_keys(int deliveryMs)
    {
        // The pilot moves to the other MCDU (Ctrl+Shift+R) while the clear waits for the four keys of the entry. The clear
        // goes on with the captain's unit, whose keys still need the settle margin to show on ITS screen: the empty queue
        // alone is not enough (the 350 ms case of the test above), so the hold is kept per unit and not only for the unit
        // shown.
        _clockedKeys = true;
        AircraftHolds("KJFK/", deliveryMs);
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        Assert.True(_form.Send(Captain, new[] { "K", "L", "A", "X" }));
        bool switched = false;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (!switched)
            {
                switched = true;
                _form.SelectUnit(FirstOfficer);
            }
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();
        Advance(5000);

        Assert.True(switched);
        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal(9, CaptainClrPresses);
        Assert.Equal("", _pad);
        Assert.Empty(_speech.All);   // silent on success; the first officer is the unit shown, so no read-back either

        // Back on the captain's MCDU (Ctrl+Shift+L), Delete finds the scratchpad empty and presses nothing.
        _form.SelectUnit(Captain);
        await _form.ClearScratchpadAsync();
        Assert.Equal(new[] { "Scratchpad already empty" }, _speech.All);
        Assert.Equal(9, CaptainClrPresses);
        Assert.Equal(0, _clrOnEmptyPad);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(350)]
    public async Task A_unit_switch_after_the_queue_has_drained_does_not_cut_the_hold(int deliveryMs)
    {
        // The last key of the entry is released but its effect is not on the screen yet when the pilot switches units.
        _clockedKeys = true;
        AircraftHolds("KJFK/", deliveryMs);
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        Assert.True(_form.Send(Captain, new[] { "K", "L", "A", "X" }));
        bool switched = false;
        bool lastKeyNotShownYet = false;
        var wait = _form.Delay;
        _form.Delay = ms =>
        {
            if (!switched && _def.McduKeysPending == 0)
            {
                switched = true;
                lastKeyNotShownYet = _now < T0.AddMilliseconds(480 + deliveryMs);   // X goes down at 480 ms
                _form.SelectUnit(FirstOfficer);
            }
            return wait(ms);
        };

        await _form.ClearScratchpadAsync();
        Advance(5000);

        Assert.True(switched);
        Assert.True(lastKeyNotShownYet);   // the premise: the switch comes before the last key shows
        Assert.Equal(0, _clrOnEmptyPad);
        Assert.Equal(9, CaptainClrPresses);
        Assert.Equal("", _pad);
        Assert.Empty(_speech.All);
    }

    // ---- The blank clocks ----

    [Fact]
    public void Switching_to_a_unit_blank_for_a_while_says_blank_at_once()
    {
        _screens[Captain] = Page();
        _screens[FirstOfficer] = Blank(FirstOfficer);
        PollAt(0);
        PollAt(2000);   // the window sat on the captain's MCDU while the first officer's stayed blank
        _form.SelectUnit(FirstOfficer);
        Assert.Equal(new[] { "First Officer MCDU is blank. It may be unpowered." }, _form.Lines);
    }

    [Fact]
    public void A_unit_that_goes_blank_while_the_other_is_shown_is_timed_from_then()
    {
        _screens[Captain] = Page();
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        PollAt(0);
        _screens[FirstOfficer] = Blank(FirstOfficer);
        PollAt(250);    // the first officer's clock starts here, though the window shows the captain's MCDU
        PollAt(1500);   // 1.25 s blank
        _form.SelectUnit(FirstOfficer);
        Assert.Equal(new[] { "First Officer MCDU is blank. It may be unpowered." }, _form.Lines);
    }

    [Fact]
    public void Switching_to_a_unit_that_has_only_just_gone_blank_waits()
    {
        _screens[Captain] = Page();
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        PollAt(0);
        _screens[FirstOfficer] = Blank(FirstOfficer);
        PollAt(1500);
        PollAt(2000);   // blank for half a second: perhaps a page change
        _form.SelectUnit(FirstOfficer);
        Assert.Equal(new[] { "First Officer MCDU: waiting for data" }, _form.Lines);
    }

    [Fact]
    public void A_blank_on_the_shown_unit_holds_the_page_for_a_second_then_says_blank()
    {
        _screens[Captain] = Page();
        _form.PrepareToShow();
        PollAt(0);
        _screens[Captain] = Blank();
        PollAt(250);
        PollAt(900);
        Assert.Contains(_form.Lines, l => l.StartsWith("Title:", StringComparison.Ordinal));   // the FMS between pages
        PollAt(1250);
        Assert.Equal(new[] { "Captain MCDU is blank. It may be unpowered." }, _form.Lines);
    }

    [Fact]
    public void A_reshow_does_not_believe_a_blank_it_did_not_watch()
    {
        _screens[Captain] = Page();
        _screens[FirstOfficer] = Blank(FirstOfficer);
        _form.PrepareToShow();
        PollAt(0);
        PollAt(2000);
        _now = T0.AddSeconds(60);   // the window was hidden; nothing polled meanwhile
        _form.PrepareToShow();
        _form.SelectUnit(FirstOfficer);
        Assert.Equal(new[] { "First Officer MCDU: waiting for data" }, _form.Lines);
    }

    [Fact]
    public void A_unit_switch_does_not_start_the_clocks_again()
    {
        _screens[Captain] = Blank();
        _screens[FirstOfficer] = Page(FirstOfficer);
        _form.PrepareToShow();
        PollAt(0);
        PollAt(1500);   // the captain's MCDU has been blank for 1.5 s
        _form.SelectUnit(FirstOfficer);
        _form.SelectUnit(Captain);
        Assert.Equal(new[] { "Captain MCDU is blank. It may be unpowered." }, _form.Lines);
    }

    // ---- Culture ----

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_window_reads_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            _screens[Captain] = Page();
            _screens[FirstOfficer] = Blank(FirstOfficer);
            _form.PrepareToShow();
            PollAt(0);
            PollAt(2000);
            _form.SelectUnit(FirstOfficer);
            Assert.Equal(new[] { "First Officer MCDU is blank. It may be unpowered." }, _form.Lines);
            _screens[FirstOfficer] = null;
            _form.Tick();
            Assert.Equal(new[] { "First Officer MCDU: waiting for data" }, _form.Lines);
            _form.SelectUnit(Captain);
            await _form.ClearScratchpadAsync();   // the pad never changes: the wait times out
            Assert.Equal(new[] { ClearFailed }, _speech.All);
            Assert.Equal(1, CaptainClrPresses);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
