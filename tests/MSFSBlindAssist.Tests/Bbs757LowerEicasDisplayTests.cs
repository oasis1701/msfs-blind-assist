// The 757 lower EICAS as one reader of the 757's display monitor: its names and keys, its spoken lines (page changes
// and STATUS messages, S11), its window rows, and the shared monitor settling its changes, never judging a reading by a
// simulator value (B757-30).

using System.Globalization;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757LowerEicasDisplayTests
{
    private const string Key = "MSFSBA_BBS757_STATUS_MESSAGES";

    /// <summary>A reading of <paramref name="page"/> (1 engine, 2 STATUS, 3 engine off, else blank) with its page word,
    /// <paramref name="rows"/> as drawn and <paramref name="messages"/> in the STATUS list.</summary>
    internal static Bbs757LowerEicasReading R(int page = 1, bool corrupt = false, string[]? messages = null, bool powered = true,
        string[]? rows = null)
    {
        string? word = !powered ? null : page switch { 1 => "engine", 2 => "STATUS", 3 => "engine off", _ => "blank" };
        return new Bbs757LowerEicasReading(page, word, rows ?? Array.Empty<string>(), messages ?? Array.Empty<string>(),
            powered, corrupt);
    }

    private static Bbs757LowerEicasReading Status(params string[] messages) => R(page: 2, messages: messages);

    private static IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757LowerEicasReading heard, Bbs757LowerEicasReading now, bool prefix = true) =>
        new Bbs757LowerEicasDisplay().Changes(heard, now, prefix);

    private static string[] Texts(IReadOnlyList<Bbs757SpokenLine> lines) => lines.Select(l => l.Text).ToArray();

    [Fact]
    public void The_reader_is_the_lower_eicas_on_the_status_messages_row()
    {
        Assert.Equal("Lower EICAS", Bbs757LowerEicasDisplay.Name);
        Assert.Equal(Key, Bbs757LowerEicasDisplay.MuteKey);
        Assert.Equal("bbs757-lower-eicas", Bbs757LowerEicasDisplay.LogChannel);
        Assert.Equal(250, Bbs757LowerEicasDisplay.TickMs);
        Assert.Equal("EICAS status messages", Bbs757LowerEicasDisplay.MonitorRowLabel);
        IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> reader = new Bbs757LowerEicasDisplay();
        Assert.Empty(reader.SimInputs);   // memory alone: its N2 and page gate is gone (B757-30)
        Assert.True(reader.Lit(R()));
        Assert.False(reader.Lit(R(powered: false)));
    }

    [Fact]
    public void A_page_change_is_spoken_with_the_page_words()
    {
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(Changes(R(1), R(2))));
        Assert.Equal(new[] { "Lower EICAS: engine page" }, Texts(Changes(R(2), R(1))));
        Assert.Equal(new[] { "Lower EICAS: engine page off" }, Texts(Changes(R(1), R(3))));
        Assert.Equal(new[] { "Lower EICAS: blank" }, Texts(Changes(R(1), R(0))));
        Assert.Equal(new[] { "STATUS page" }, Texts(Changes(R(1), R(2), prefix: false)));
        // Two values of one kind are the same page, and the page's rows (an N2 spooling) are never spoken.
        Assert.Empty(Changes(R(0), R(9)));
        Assert.Empty(Changes(R(1, rows: new[] { "N2: 70.0, 70.0" }), R(1, rows: new[] { "N2: 71.2, 70.0" })));
    }

    [Fact]
    public void Messages_present_when_status_opens_are_not_spoken()
    {
        var lines = Changes(R(1, messages: new[] { "WARN ELEX" }), Status("WARN ELEX"));
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(lines));
        // One that appeared under another page is never news, not even when STATUS then opens.
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, Texts(Changes(R(1), Status("WARN ELEX"))));
        // Leaving STATUS says only the page change.
        Assert.Equal(new[] { "Lower EICAS: engine page" }, Texts(Changes(Status("WARN ELEX"), R(1))));
    }

    [Fact]
    public void A_new_status_message_on_the_status_page_is_spoken()
    {
        Assert.Equal(new[] { "Status: L EICAS CMPTR" }, Texts(Changes(Status("WARN ELEX"), Status("L EICAS CMPTR", "WARN ELEX"))));
        Assert.Equal(new[] { "Status gone: WARN ELEX" }, Texts(Changes(Status("L EICAS CMPTR", "WARN ELEX"), Status("L EICAS CMPTR"))));
    }

    [Fact]
    public void Several_new_messages_share_one_line()
    {
        var lines = Changes(Status("A", "B"), Status("C", "D", "A"));
        Assert.Equal(new[] { "Status: C, D", "Status gone: B" }, Texts(lines));
        Assert.Equal(new[] { "Status gone: A, B" }, Texts(Changes(Status("A", "B", "C"), Status("C"))));
    }

    [Fact]
    public void A_status_message_off_the_status_page_is_not_spoken()
    {
        Assert.Empty(Changes(R(1), R(1, messages: new[] { "WARN ELEX" })));
        Assert.Empty(Changes(R(3), R(3, messages: new[] { "WARN ELEX" })));
    }

    [Fact]
    public void A_message_gone_off_the_status_page_is_not_spoken()
    {
        Assert.Empty(Changes(R(1, messages: new[] { "WARN ELEX" }), R(1)));
    }

    [Fact]
    public void A_new_message_with_the_status_page_on_both_sides_keeps_BlueBirds_words()
    {
        // Status lines need STATUS on both sides, so a page change and status lines never come together: here both
        // readings show STATUS and only the new message is said, in BlueBird's words exactly (spaces included).
        var lines = Changes(Status("Z"), Status("Y  X", "Z"));
        Assert.Equal(new[] { "Status: Y  X" }, Texts(lines));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        // Every line, page changes and messages alike, carries the one mute key the monitor checks.
        var lines = Changes(Status("A"), Status("B"));
        Assert.All(lines, l => Assert.Equal(Key, l.MuteKey));
        Assert.All(Changes(R(1), R(2)), l => Assert.Equal(Key, l.MuteKey));
    }

    [Fact]
    public void A_page_without_its_part_lists_not_read()
    {
        var layout = Bbs757LowerEicasLayoutTests.Sample() with { Status = null, StatusMissing = "no list" };
        var raw = new Bbs757LowerEicasRaw(2, true, false, true, null, null, Array.Empty<string>());
        var rows = Bbs757LowerEicasText.PageRows(layout, raw);
        var reading = new Bbs757LowerEicasReading(2, "STATUS", rows, Array.Empty<string>(), true, false);
        Assert.Equal(new[] { "Page: STATUS", "STATUS page: not read: no list" }, new Bbs757LowerEicasDisplay().Rows(reading));
        // It speaks nothing of its own: the same page again, and no message.
        Assert.Empty(Changes(reading, reading with { PageRows = rows.ToArray() }));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Numbers_format_as_drawn_under_de_DE_and_sv_SE(string culture)
    {
        var layout = Bbs757LowerEicasLayoutTests.Sample();
        var engine = new Bbs757LowerEicasEngineRaw(true, 81.99, 77.4, 3600, 91.9, 114.7, 0.58, -1.84, 0, 0, false, false, 77.4);
        var status = new Bbs757LowerEicasStatusRaw(new double[] { 1, 1, 1 }, new[] { 0.98, 0.97, 1.0 },
            new double[] { 3000, 2880, 2950 }, -55, 0, 0.75, true, true, 1850, 0, 1760, 0.123, 0, 0, 0, new uint[8]);
        var raw = new Bbs757LowerEicasRaw(2, true, false, false, new[] { engine, engine }, status, new[] { "WARN ELEX" });
        var reading = new Bbs757LowerEicasReading(2, "STATUS", Bbs757LowerEicasText.PageRows(layout, raw),
            new[] { "WARN ELEX" }, true, false);
        var display = new Bbs757LowerEicasDisplay();
        var rows = TestCulture.Run(culture, () => display.Rows(reading));
        Assert.Equal("Page: STATUS", rows[0]);
        Assert.Contains("HYD QTY: L 0.98, C 0.97, R 1.00", rows);
        Assert.Contains("FF: 1.6, 1.6", rows); // kilograms, one decimal, a point
        Assert.Contains("RUD: 12 % right", rows);
    }

    [Fact]
    public void Rows_of_a_blank_display_is_the_one_row()
    {
        var display = new Bbs757LowerEicasDisplay();
        Assert.Equal(new[] { "Lower EICAS: blank" }, display.Rows(R(0)));
        Assert.Equal(new[] { "Page: engine" }, display.Rows(R(1)));
    }

    // ---- through the monitor ------------------------------------------------------------------------------------

    /// <summary>The lower EICAS reader's words, power and (no) inputs with a scripted sampler in place of memory.</summary>
    private sealed class Scripted : IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>
    {
        private readonly IBbs757DisplayReader<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> _inner = new Bbs757LowerEicasDisplay();
        public Bbs757LowerEicasReading Current = R(1);

        public string Name => Bbs757LowerEicasDisplay.Name;
        public string MuteKey => Bbs757LowerEicasDisplay.MuteKey;
        public string LogChannel => Bbs757LowerEicasDisplay.LogChannel;
        public int TickMs => Bbs757LowerEicasDisplay.TickMs;
        public IReadOnlyList<Bbs757SimInput> SimInputs => _inner.SimInputs;
        public IReadOnlyList<Bbs757LowerEicasLayout> LayoutsIn(ModuleAnalysis analysis) => new[] { Bbs757LowerEicasLayoutTests.Sample() };
        public IBbs757DisplaySampler<Bbs757LowerEicasReading>? SamplerFor(LocatedModule module, Bbs757LowerEicasLayout layout) => new Sampler(this);
        public bool? Lit(Bbs757LowerEicasReading r) => _inner.Lit(r);
        public IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757LowerEicasReading a, Bbs757LowerEicasReading b, bool p) => _inner.Changes(a, b, p);
        public IReadOnlyList<string> Rows(Bbs757LowerEicasReading r) => _inner.Rows(r);

        private sealed class Sampler(Scripted owner) : IBbs757DisplaySampler<Bbs757LowerEicasReading>
        {
            public bool TryRead(out Bbs757LowerEicasReading reading)
            {
                reading = owner.Current;
                return true;
            }

            public void Dispose()
            {
            }
        }
    }

    private static (Scripted Reader, Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading> Monitor,
        Bbs757DisplayMonitorTests.FakeEnvironment Env, Action<int> Tick) Run()
    {
        var reader = new Scripted();
        var env = new Bbs757DisplayMonitorTests.FakeEnvironment();
        var monitor = new Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>(reader);
        monitor.Start(env, startTimer: false);
        void Tick(int n)
        {
            for (int i = 0; i < n; i++)
            {
                env.NowMs += 250;
                monitor.Tick();
            }
        }
        // The display is lit from the start (its power-up was not seen), so the first good reading is the silent baseline.
        Tick(8);
        return (reader, monitor, env, Tick);
    }

    [Fact]
    public void A_page_change_is_said_once_after_settling()
    {
        var (reader, monitor, env, tick) = Run();
        Assert.Equal("Running", monitor.StateName);
        Assert.Empty(env.Spoken);

        reader.Current = R(2);
        tick(1);
        Assert.Empty(env.Spoken); // not yet settled
        tick(6);
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, env.Spoken);
        tick(10);
        Assert.Single(env.Spoken);
        Assert.Equal("Page: STATUS", monitor.View()[0]);
    }

    [Fact]
    public void Fast_page_changes_say_only_the_last()
    {
        var (reader, monitor, env, tick) = Run();
        // The monitor's settle starts at the first change and does not restart on later ones. Here the middle step returns
        // to the heard page (engine), which closes the open settle window unspoken; engine off then opens a new one and
        // holds. So this proves: a page that came and went is never said, and the page that holds is said once.
        foreach (int page in new[] { 2, 1, 3 })
        {
            reader.Current = R(page);
            tick(1);
        }
        tick(6);
        Assert.Equal(new[] { "Lower EICAS: engine page off" }, env.Spoken);

        // A change and back inside the settle says nothing.
        reader.Current = R(1);
        tick(1);
        reader.Current = R(3);
        tick(8);
        Assert.Single(env.Spoken);
    }

    [Fact]
    public void A_muted_row_says_nothing_through_the_monitor()
    {
        var (reader, monitor, env, tick) = Run();
        env.Muted.Add(Key);
        reader.Current = R(2);
        tick(8);
        Assert.Empty(env.Spoken);
        Assert.Equal("Page: STATUS", monitor.View()[0]);
    }

    [Fact]
    public void A_message_while_status_shows_is_spoken_through_the_monitor()
    {
        var (reader, monitor, env, tick) = Run();
        reader.Current = Status("WARN ELEX");
        tick(8);
        Assert.Equal(new[] { "Lower EICAS: STATUS page" }, env.Spoken);

        reader.Current = Status("L EICAS CMPTR", "WARN ELEX");
        tick(8);
        Assert.Equal(new[] { "Lower EICAS: STATUS page", "Status: L EICAS CMPTR" }, env.Spoken);
    }

    // The owner's flight of 2026-11-10: at each engine start the N2 BlueBird draws spooled faster than the simulator's
    // TURB ENG N2 followed, and the gate of the time said "Lower EICAS announcements stopped: values do not match the
    // simulator" while the display was right. The reader reads memory alone now (B757-30): an engine page whose N2 rises
    // while the simulator's lags far behind, or reads anything at all, says nothing, and the reader runs throughout.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void An_engine_start_speaks_no_stop_line(string culture)
    {
        var (spoken, view, asked) = TestCulture.Run(culture, () =>
        {
            var (reader, monitor, env, tick) = Run();
            for (int i = 0; i <= 240; i++) // a minute of the start, a reading every 250 ms
            {
                double n2 = Math.Min(62.0, i * 0.5); // memory: the N2 the engine page draws, up to idle
                double lagging = Math.Max(0, n2 - 8); // the simulator's, well behind
                reader.Current = R(1, rows: new[] { "N2: " + n2.ToString("F1", CultureInfo.InvariantCulture) + ", 0.0" });
                env.Simulator = new Bbs757SimValues(new Dictionary<string, double>
                {
                    ["TURB ENG N2:1"] = lagging, ["TURB ENG N2:2"] = 0, ["BBS_ECAM_Mode"] = 3,
                });
                tick(1);
                Assert.Equal("Running", monitor.StateName);
            }
            return (env.Spoken, monitor.View(), env.KeysAsked.ToArray());
        });
        Assert.Empty(spoken); // no stop line, and the N2 itself is never spoken
        Assert.Equal(new[] { "Page: engine", "N2: 62.0, 0.0" }, view);
        Assert.Empty(asked); // nothing of the simulator's is even read
    }
}
