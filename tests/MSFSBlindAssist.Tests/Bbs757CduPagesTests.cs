using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// <see cref="Bbs757CduPages"/>, the background reader and typist on the first officer's CDU, against
/// a fake CDU (no tablet view): it reaches a page with one key or none, types an entry only into a
/// scratchpad it has wiped and presses the line key only when the scratchpad shows exactly what it
/// typed, refuses while the pilot's own keys are queued, and answers null or false, never an
/// exception, when the CDU cannot be reached. Waits go through an instant fake delay.
/// </summary>
public class Bbs757CduPagesTests
{
    private const int Fo = Bbs757CduPages.FirstOfficer;

    // ---- Pages ---------------------------------------------------------------------------------

    [Fact]
    public async Task ShowPage_presses_nothing_when_already_there()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var pages = Pages(cdu);

        var screen = await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal("NAV RADIO", Bbs757CduPages.Title(screen!));
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task ShowPage_presses_the_page_key_and_waits()
    {
        // The new page arrives two reads after the key, as a frame does on the Comm Bus.
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", PageLagReads = 2 };
        var delays = new List<int>();
        var pages = Pages(cdu, delays);

        var screen = await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal("NAV RADIO", Bbs757CduPages.Title(screen!));
        Assert.Equal(new[] { (Fo, "NAVRAD", 250) }, cdu.Presses);
        Assert.All(delays, ms => Assert.Equal(250, ms));
    }

    [Fact]
    public async Task ShowPage_brings_up_a_cdu_with_no_frame_yet()
    {
        // After an aircraft load the first officer's CDU has no frame until its screen first
        // changes: it reads null, and the page key is what brings its first frame.
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", NoFrameUntilAKey = true };
        var pages = Pages(cdu);

        var screen = await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal("NAV RADIO", Bbs757CduPages.Title(screen!));
        Assert.Equal(new[] { (Fo, "NAVRAD", 250) }, cdu.Presses);
    }

    [Fact]
    public async Task ShowPage_presses_nothing_on_a_cdu_with_no_frame_while_unreachable()
    {
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", NoFrameUntilAKey = true, IsReadable = false };
        var pages = Pages(cdu);

        Assert.Null(await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None));
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task ShowPage_gives_up_when_the_page_never_comes()
    {
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", IgnorePageKeys = true };
        var delays = new List<int>();
        var pages = Pages(cdu, delays);

        var screen = await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None);

        Assert.Null(screen);
        Assert.Single(cdu.Presses);
        Assert.Equal(3000, delays.Sum());   // read every 250 ms for 3 s
    }

    [Fact]
    public async Task ShowPage_gives_up_after_three_seconds_of_slow_reads()
    {
        // While the agent is gone a read can wait about 4 s for it. The 3 s are the clock's, not the
        // sum of the 250 ms sleeps, so the call ends after one such read instead of twelve.
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", IgnorePageKeys = true, ReadTakesMs = 4000 };
        var pages = Pages(cdu);

        var screen = await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None);

        Assert.Null(screen);
        Assert.Single(cdu.Presses);
        Assert.Equal(1, cdu.ReadsAfterAKey);
    }

    [Fact]
    public async Task Presses_keys_only_on_the_first_officers_cdu()
    {
        // R52: the captain's CDU, the one the pilot works, never takes the app's keys; it can still be read.
        const int captain = 1;
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", Scratchpad = "KJFK" };
        var pages = Pages(cdu);
        var ct = CancellationToken.None;

        Assert.Null(await pages.ShowPageAsync(captain, Bbs757CduKeys.NavRad, IsNavRadio, ct));
        Assert.False(await pages.WipeScratchpadAsync(captain, ct));
        Assert.Null(await pages.EnterAsync(captain, "113.90", "L1", ct));
        Assert.Null(await pages.PressKeysAsync(captain, new[] { Bbs757CduKeys.Del, "L1" }, ct));
        Assert.Empty(cdu.Presses);

        Assert.NotNull(await pages.ReadAsync(captain, ct));
        var legs = await pages.ShowPageAsync(captain, Bbs757CduKeys.Legs, s => Bbs757CduPages.Title(s) == "ACT RTE 1 LEGS", ct);
        Assert.NotNull(legs);   // already up: nothing to press
        Assert.Empty(cdu.Presses);
    }

    // ---- Entries -------------------------------------------------------------------------------

    [Fact]
    public async Task Enter_types_wipes_and_presses_the_line_key()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO", Scratchpad = "KJFK" };
        var delays = new List<int>();
        var pages = Pages(cdu, delays);

        var screen = await pages.EnterAsync(Fo, "113.90", "L1", CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal(new[]
        {
            (Fo, "KEYCLR", 1300),
            (Fo, "NUM1", 250), (Fo, "NUM1", 250), (Fo, "NUM3", 250), (Fo, "NUMPER", 250), (Fo, "NUM9", 250), (Fo, "NUM0", 250),
            (Fo, "L1", 250),
        }, cdu.Presses);
        Assert.Equal(new[] { "113.90" }, cdu.LineEntries);
        Assert.Equal("", Bbs757CduPages.Scratchpad(screen!));
        Assert.Equal(1800, delays[0]);    // the long CLR's settle
        Assert.Equal(1200, delays[^1]);   // after the line key
    }

    [Fact]
    public async Task Enter_presses_no_line_key_when_a_message_covers_the_typing()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO", MessageAfterTyping = "INVALID ENTRY" };
        var pages = Pages(cdu);

        var screen = await pages.EnterAsync(Fo, "113.90", "L1", CancellationToken.None);

        Assert.Null(screen);
        Assert.DoesNotContain(cdu.Presses, p => p.Key == "L1");
        Assert.Equal(2, cdu.Presses.Count(p => p == (Fo, "KEYCLR", 1300)));   // the wipe before, and the one after
        Assert.Equal((Fo, "KEYCLR", 1300), cdu.Presses[^1]);
        Assert.Empty(cdu.LineEntries);
        Assert.Equal("", cdu.Scratchpad);
    }

    [Fact]
    public async Task Enter_refuses_while_the_pilot_types()
    {
        // The pilot is typing on CDU 2 in the CDU window: nothing is interleaved with his keys.
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", PendingKeyCount = 3 };
        var pages = Pages(cdu);

        Assert.Null(await pages.EnterAsync(Fo, "113.90", "L1", CancellationToken.None));
        Assert.Null(await pages.PressKeysAsync(Fo, new[] { "KEYDEL", "L1" }, CancellationToken.None));
        Assert.Null(await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, CancellationToken.None));
        Assert.False(await pages.WipeScratchpadAsync(Fo, CancellationToken.None));
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task Enter_types_the_entry_without_its_surrounding_spaces()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var pages = Pages(cdu);

        Assert.NotNull(await pages.EnterAsync(Fo, " 113.90 ", "L1", CancellationToken.None));
        Assert.DoesNotContain(cdu.Presses, p => p.Key == Bbs757CduKeys.Space);
        Assert.Equal(new[] { "113.90" }, cdu.LineEntries);

        int pressed = cdu.Presses.Count;
        Assert.Null(await pages.EnterAsync(Fo, "   ", "L1", CancellationToken.None));   // nothing to type
        Assert.Equal(pressed, cdu.Presses.Count);
    }

    [Fact]
    public async Task Enter_and_PressKeys_read_only_once_their_keys_have_gone()
    {
        // The keys go out one at a time through the client's queue: every read comes after it has emptied.
        var cdu = new FakeCdu { Title = "NAV RADIO", QueuesKeys = true };
        var pages = new Bbs757CduPages(cdu, (ms, ct) => { cdu.OneKeyGoes(); return Task.CompletedTask; }, () => cdu.NowMs);

        Assert.NotNull(await pages.EnterAsync(Fo, "113.90", "L1", CancellationToken.None));
        Assert.NotNull(await pages.PressKeysAsync(Fo, new[] { Bbs757CduKeys.Del, "L1" }, CancellationToken.None));

        Assert.Equal(new[] { "113.90" }, cdu.LineEntries);
        Assert.NotEmpty(cdu.PendingAtReads);
        Assert.All(cdu.PendingAtReads, pending => Assert.Equal(0, pending));
    }

    [Fact]
    public async Task No_screen_and_no_line_key_when_the_keys_never_go_out()
    {
        // The queue does not empty within its time: the keys have not taken effect, so a read now
        // would show the screen before them, and a line key would land on unfinished typing.
        var keys = new FakeCdu { Title = "NAV RADIO", QueuesKeys = true };
        Assert.Null(await Pages(keys).PressKeysAsync(Fo, new[] { Bbs757CduKeys.Del, "L1" }, CancellationToken.None));

        var entry = new FakeCdu { Title = "NAV RADIO", QueuesKeys = true };
        Assert.Null(await Pages(entry).EnterAsync(Fo, "113.90", "L1", CancellationToken.None));
        Assert.DoesNotContain(entry.Presses, p => p.Key == "L1");
        Assert.Empty(entry.LineEntries);
    }

    [Fact]
    public async Task Enter_refuses_what_it_cannot_type_or_press()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var pages = Pages(cdu);

        Assert.Null(await pages.EnterAsync(Fo, "113*90", "L1", CancellationToken.None));   // no key for '*'
        Assert.Null(await pages.EnterAsync(Fo, "113.90", "L7", CancellationToken.None));   // no such line key
        Assert.Null(await pages.EnterAsync(Fo, "", "L1", CancellationToken.None));
        Assert.Null(await pages.PressKeysAsync(Fo, new[] { "KEYDEL", "LSK1" }, CancellationToken.None));
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task Wipe_holds_clr_again_when_a_long_clr_removes_one_character()
    {
        // Measured 2026-10-26 (docs/b757.md, "The page service"): a CLR held 1.3 s cleared the scratchpad 45 times in
        // 60 and removed only its last character the other 15; a second long CLR always cleared the rest.
        var cdu = new FakeCdu { Title = "NAV RADIO", Scratchpad = "KATL1100.5", LongClrMisses = 1 };
        var delays = new List<int>();
        var pages = Pages(cdu, delays);

        Assert.True(await pages.WipeScratchpadAsync(Fo, CancellationToken.None));
        Assert.Equal(new[] { (Fo, "KEYCLR", 1300), (Fo, "KEYCLR", 1300) }, cdu.Presses);
        Assert.Equal(new[] { 1800, 1800 }, delays);
    }

    [Fact]
    public async Task Wipe_answers_false_when_two_long_clrs_leave_text()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO", Scratchpad = "KATL1100.5", LongClrMisses = 2 };
        var pages = Pages(cdu);

        Assert.False(await pages.WipeScratchpadAsync(Fo, CancellationToken.None));
        Assert.Equal(new[] { (Fo, "KEYCLR", 1300), (Fo, "KEYCLR", 1300) }, cdu.Presses);
    }

    [Fact]
    public async Task Enter_types_after_a_long_clr_that_needed_a_second_hold()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO", Scratchpad = "KJFK", LongClrMisses = 1 };
        var pages = Pages(cdu);

        var screen = await pages.EnterAsync(Fo, "113.90", "L1", CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal(new[] { "113.90" }, cdu.LineEntries);
    }

    [Fact]
    public async Task PressKeys_sends_the_keys_in_order()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var delays = new List<int>();
        var pages = Pages(cdu, delays);

        var screen = await pages.PressKeysAsync(Fo, new[] { "KEYDEL", "L1" }, CancellationToken.None);

        Assert.NotNull(screen);
        Assert.Equal(new[] { (Fo, "KEYDEL", 250), (Fo, "L1", 250) }, cdu.Presses);
        Assert.Equal(1200, delays[^1]);
    }

    [Fact]
    public async Task One_call_at_a_time()
    {
        // A read asked for during a wipe waits for the wipe: no call's keys or reads interleave with another's.
        var cdu = new FakeCdu { Title = "NAV RADIO", Scratchpad = "KJFK" };
        var release = new TaskCompletionSource();
        var pages = new Bbs757CduPages(cdu, (ms, ct) => release.Task);

        var wipe = pages.WipeScratchpadAsync(Fo, CancellationToken.None);   // waiting out its long CLR
        var read = pages.ReadAsync(Fo, CancellationToken.None);

        Assert.False(read.IsCompleted);
        release.SetResult();
        Assert.True(await wipe);
        Assert.NotNull(await read);
    }

    [Fact]
    public async Task A_walk_keeps_other_calls_out_until_its_last_step()
    {
        // Ctrl+N's read of APPROACH REF is several pages: a key another reader presses between two of its steps
        // would land on the wrong page. The walk holds the gate from its first step to its last.
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var release = new TaskCompletionSource();
        var pages = new Bbs757CduPages(cdu, (ms, ct) => release.Task);

        var walk = pages.WalkAsync<Bbs757CduScreen?>("test walk", null, CancellationToken.None, async w =>
        {
            await w.PressKeysAsync(Fo, new[] { "L6" }, CancellationToken.None);
            return await w.PressKeysAsync(Fo, new[] { "L5" }, CancellationToken.None);
        });
        var other = pages.PressKeysAsync(Fo, new[] { "KEYDEL" }, CancellationToken.None);

        Assert.False(other.IsCompleted);
        Assert.Equal(new[] { "L6" }, cdu.Presses.Select(p => p.Key));
        release.SetResult();
        Assert.NotNull(await walk);
        Assert.NotNull(await other);
        Assert.Equal(new[] { "L6", "L5", "KEYDEL" }, cdu.Presses.Select(p => p.Key));
    }

    [Fact]
    public async Task A_walk_that_is_canceled_or_throws_answers_its_failed_value()
    {
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        var pages = Pages(cdu);

        Assert.Equal("failed", await pages.WalkAsync("test walk", "failed", CancellationToken.None,
            _ => throw new OperationCanceledException()));
        Assert.Equal("failed", await pages.WalkAsync("test walk", "failed", CancellationToken.None,
            _ => throw new InvalidOperationException("boom")));
        Assert.NotNull(await pages.ReadAsync(Fo, CancellationToken.None));   // the gate was let go
    }

    // ---- Failures ------------------------------------------------------------------------------

    [Fact]
    public async Task Calls_never_throw()
    {
        // A link that throws on every member.
        var broken = new Bbs757CduPages(new ThrowingLink(), InstantDelay(null));
        await AssertAllFail(broken, CancellationToken.None);

        // A canceled caller.
        var cdu = new FakeCdu { Title = "NAV RADIO" };
        await AssertAllFail(Pages(cdu), new CancellationToken(canceled: true));
        Assert.Empty(cdu.Presses);

        // The tablet view unreachable (aircraft reloading, debugger port held): a client that never
        // connected, and one disposed by a switch away. Neither is ever started here.
        using var unreachable = new CoherentBbs757TabletClient(() => "");
        await AssertAllFail(Bbs757CduPages.For(unreachable), CancellationToken.None);
        var disposed = new CoherentBbs757TabletClient(() => "");
        disposed.Dispose();
        await AssertAllFail(Bbs757CduPages.For(disposed), CancellationToken.None);
        Assert.Equal(0, unreachable.PendingKeyCount);
    }

    [Theory]
    [InlineData("page")]
    [InlineData("wipe")]
    [InlineData("entry")]
    [InlineData("keys")]
    public async Task Calls_stop_reading_once_the_tablet_view_is_lost(string call)
    {
        // The link drops with a call's first key: the call stops there and answers null (false), with
        // no further read, rather than wait out reads that cannot answer.
        var cdu = new FakeCdu { Title = "ACT RTE 1 LEGS     1/7", Scratchpad = "KJFK", IgnorePageKeys = true, UnreadableAfterAKey = true };
        var pages = Pages(cdu);
        var ct = CancellationToken.None;

        bool failed = call switch
        {
            "page" => await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, ct) == null,
            "wipe" => !await pages.WipeScratchpadAsync(Fo, ct),
            "entry" => await pages.EnterAsync(Fo, "113.90", "L1", ct) == null,
            _ => await pages.PressKeysAsync(Fo, new[] { Bbs757CduKeys.Del, "L1" }, ct) == null,
        };

        Assert.True(failed);
        Assert.Equal(0, cdu.ReadsAfterAKey);
        if (call == "entry") Assert.Equal(new[] { (Fo, Bbs757CduKeys.Clr, 1300) }, cdu.Presses);   // the wipe's CLR, then nothing
    }

    private static async Task AssertAllFail(Bbs757CduPages pages, CancellationToken ct)
    {
        Assert.Null(await pages.ReadAsync(Fo, ct));
        Assert.Null(await pages.ShowPageAsync(Fo, Bbs757CduKeys.NavRad, IsNavRadio, ct));
        Assert.False(await pages.WipeScratchpadAsync(Fo, ct));
        Assert.Null(await pages.EnterAsync(Fo, "113.90", "L1", ct));
        Assert.Null(await pages.PressKeysAsync(Fo, new[] { "KEYDEL", "L1" }, ct));
    }

    // ---- Titles --------------------------------------------------------------------------------

    [Fact]
    public void Title_and_page_count()
    {
        const string legs = "  ACT RTE 1 LEGS     1/7";
        Assert.Equal("ACT RTE 1 LEGS", Bbs757CduTitle.PageName(legs));
        Assert.Equal((1, 7), Bbs757CduTitle.Counter(legs));
        Assert.Equal("RTE 1 LEGS", Bbs757CduTitle.Identity(legs));
        Assert.Equal("ACT RTE 1 LEGS 1/7", Bbs757CduTitle.Spoken(legs));

        Assert.Equal("RTE 1 LEGS", Bbs757CduTitle.Identity("  MOD RTE 1 LEGS     1/7"));

        const string navRadio = "        NAV RADIO       ";
        Assert.Equal("NAV RADIO", Bbs757CduTitle.PageName(navRadio));
        Assert.Null(Bbs757CduTitle.Counter(navRadio));
        Assert.Equal("NAV RADIO", Bbs757CduTitle.Identity(navRadio));
        Assert.Equal("NAV RADIO", Bbs757CduTitle.Spoken(navRadio));

        var screen = FakeCdu.ScreenOf(legs, "");
        Assert.Equal("ACT RTE 1 LEGS", Bbs757CduPages.Title(screen));
        Assert.Equal((1, 7), Bbs757CduPages.PageCount(screen));
        Assert.Null(Bbs757CduPages.PageCount(FakeCdu.ScreenOf(navRadio, "")));
    }

    [Theory]
    [InlineData("", "", "", "")]
    [InlineData("1/7", "", "", "1/7")]   // a counter alone
    [InlineData("  RTE 1  2/3", "RTE 1", "RTE 1", "RTE 1 2/3")]
    [InlineData("  ACT PERF INIT        ", "ACT PERF INIT", "PERF INIT", "ACT PERF INIT")]
    [InlineData("  ACTIVE   ", "ACTIVE", "ACTIVE", "ACTIVE")]   // "ACT" only as its own word
    [InlineData("  POS REF 10/12", "POS REF", "POS REF", "POS REF 10/12")]
    public void Titles_read_the_same_whatever_the_spacing(string row, string pageName, string identity, string spoken)
    {
        Assert.Equal(pageName, Bbs757CduTitle.PageName(row));
        Assert.Equal(identity, Bbs757CduTitle.Identity(row));
        Assert.Equal(spoken, Bbs757CduTitle.Spoken(row));
    }

    // ---- The definition ------------------------------------------------------------------------

    [Fact]
    public void The_definition_hands_out_one_page_service_over_its_tablet_client()
    {
        int made = 0;
        var def = new BBS757Definition { TabletClientFactory = () => { made++; return new CoherentBbs757TabletClient(() => ""); } };

        var first = def.CduPages;
        var second = def.CduPages;

        Assert.Same(first, second);
        Assert.Equal(1, made);
        def.OnSwitchedAway();
    }

    // ---- Fakes ---------------------------------------------------------------------------------

    private static bool IsNavRadio(Bbs757CduScreen s) => Bbs757CduPages.Title(s) == "NAV RADIO";

    /// <summary>The page service over a fake CDU, on the fake's clock (still unless its reads move it).</summary>
    private static Bbs757CduPages Pages(FakeCdu cdu, List<int>? delays = null) => new(cdu, InstantDelay(delays), () => cdu.NowMs);

    /// <summary>A delay that returns at once, noting how long it was asked to wait.</summary>
    private static Func<int, CancellationToken, Task> InstantDelay(List<int>? delays) => (ms, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        delays?.Add(ms);
        return Task.CompletedTask;
    };

    /// <summary>
    /// One CDU as the tablet client shows it: a title, a scratchpad, page keys that change the title,
    /// typing that fills the scratchpad, CLR that clears (held a second or more: the whole of it), and
    /// line keys that take the scratchpad's entry.
    /// </summary>
    private sealed class FakeCdu : IBbs757CduLink
    {
        private static readonly Dictionary<string, string> PageTitles = new()
        {
            ["NAVRAD"] = "        NAV RADIO       ",
            ["LEGS"] = "  ACT RTE 1 LEGS     1/7",
        };

        private string? _pendingTitle;
        private int _lagLeft;

        public string Title { get; set; } = "";
        public string Scratchpad { get; set; } = "";
        public bool IsReadable { get; set; } = true;
        public int PendingKeyCount { get; set; }

        /// <summary>Reads that still show the old page after a page key.</summary>
        public int PageLagReads { get; init; }
        public bool IgnorePageKeys { get; init; }
        /// <summary>A CDU message that replaces the scratchpad once a character is typed.</summary>
        public string? MessageAfterTyping { get; init; }
        /// <summary>
        /// How many long CLRs, from the first, remove only the last character, as a short CLR does (none,
        /// normally; measured 2026-10-26: about one long CLR in four).
        /// </summary>
        public int LongClrMisses { get; set; }
        /// <summary>Reads null until a key is pressed, as the first officer's CDU does after a load.</summary>
        public bool NoFrameUntilAKey { get; init; }
        /// <summary>The tablet view is lost the moment a key is pressed.</summary>
        public bool UnreadableAfterAKey { get; init; }
        /// <summary>Every key pressed stays queued until <see cref="OneKeyGoes"/> lets it out.</summary>
        public bool QueuesKeys { get; init; }
        /// <summary>How far each read moves the clock (<see cref="NowMs"/>), as a read that waits for a lost agent does.</summary>
        public int ReadTakesMs { get; init; }
        /// <summary>The clock the page service is given.</summary>
        public long NowMs { get; private set; }

        public List<(int Cdu, string Key, int HoldMs)> Presses { get; } = new();
        public List<string> LineEntries { get; } = new();
        /// <summary>Reads made once a key had been pressed.</summary>
        public int ReadsAfterAKey { get; private set; }
        /// <summary>The keys still queued at each read.</summary>
        public List<int> PendingAtReads { get; } = new();

        /// <summary>One queued key goes out.</summary>
        public void OneKeyGoes()
        {
            if (PendingKeyCount > 0) PendingKeyCount--;
        }

        public void PressKey(int cdu, string key, int holdMs)
        {
            Presses.Add((cdu, key, holdMs));
            if (UnreadableAfterAKey) IsReadable = false;
            if (QueuesKeys) PendingKeyCount++;
            if (PageTitles.TryGetValue(key, out var title))
            {
                if (IgnorePageKeys) return;
                _pendingTitle = title;
                _lagLeft = PageLagReads;
                return;
            }
            if (key == "KEYCLR")
            {
                bool clearsAll = holdMs >= 1000 && LongClrMisses-- <= 0;
                Scratchpad = clearsAll ? "" : Scratchpad.Length > 0 ? Scratchpad[..^1] : "";
                return;
            }
            if (key.Length == 2 && (key[0] == 'L' || key[0] == 'R'))
            {
                if (Scratchpad.Length > 0) LineEntries.Add(Scratchpad);
                Scratchpad = "";
                return;
            }
            char? c = key switch
            {
                "NUMPER" => '.',
                "KEYSP" => ' ',
                "KEYSLASH" => '/',
                _ when key.StartsWith("NUM", StringComparison.Ordinal) && key.Length == 4 => key[3],
                _ when key.StartsWith("KEY", StringComparison.Ordinal) && key.Length == 4 => key[3],
                _ => null,
            };
            if (c is { } typed) Scratchpad = MessageAfterTyping ?? Scratchpad + typed;
        }

        public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NowMs += ReadTakesMs;
            if (Presses.Count > 0) ReadsAfterAKey++;
            PendingAtReads.Add(PendingKeyCount);
            if (NoFrameUntilAKey && Presses.Count == 0) return Task.FromResult<Bbs757CduScreen?>(null);
            if (_pendingTitle != null)
            {
                if (_lagLeft > 0) _lagLeft--;
                else { Title = _pendingTitle; _pendingTitle = null; }
            }
            return Task.FromResult<Bbs757CduScreen?>(ScreenOf(Title, Scratchpad));
        }

        public static Bbs757CduScreen ScreenOf(string title, string scratchpad)
        {
            var lines = Enumerable.Repeat(new string(' ', Bbs757CduFrame.Columns), Bbs757CduFrame.Rows).ToArray();
            lines[0] = Row(title);
            lines[Bbs757CduFrame.ScratchpadRow] = Row(scratchpad);
            return new Bbs757CduScreen(lines);
        }

        private static string Row(string text)
        {
            string row = text.Length > Bbs757CduFrame.Columns ? text[..Bbs757CduFrame.Columns] : text;
            return row.PadRight(Bbs757CduFrame.Columns);
        }
    }

    /// <summary>A link whose every member throws.</summary>
    private sealed class ThrowingLink : IBbs757CduLink
    {
        public bool IsReadable => throw new InvalidOperationException("link");
        public int PendingKeyCount => throw new InvalidOperationException("link");
        public void PressKey(int cdu, string key, int holdMs) => throw new InvalidOperationException("link");
        public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct) => throw new InvalidOperationException("link");
    }
}
