using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's MCP value entry: the pure RPN and phrase builders, the typed-input
/// validators, and the set loops run against a simulated MCP (<see cref="FakeMcp"/>) that moves the
/// speed target the way BlueBird's knob was measured to (session 7: 1, 2 and 5 kt tiers within a
/// burst). What is pinned here is the logic around the aircraft: how a speed change is cut into
/// bursts, when to correct, what is spoken (the value READ, never the request), and that no number
/// is formatted with the PC's culture (CI runs under en-US, so the de-DE and sv-SE tests are the
/// ones that can catch a comma or a U+2212 minus).
/// </summary>
public class Bbs757McpTests
{
    private static int Count(IEnumerable<string> calls, string needle)
        => calls.Sum(c => c.Split(needle).Length - 1);

    /// <summary>The events a speed burst loop fires: its count, the number it starts with.</summary>
    private static int EventsIn(string burst) => int.Parse(burst.Split(' ')[0], CultureInfo.InvariantCulture);

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- RPN builders -----------------------------------------------------------------------

    [Fact]
    public void Constants_name_the_measured_variables()
    {
        Assert.Equal("BBS_AP_TARGET_HEADING", Bbs757Mcp.HeadingVar);
        Assert.Equal("BBS_AP_TARGET_ALTITUDE", Bbs757Mcp.AltitudeVar);
        Assert.Equal("BBS757_ORANGE_AIRSPEED_BUGS", Bbs757Mcp.SpeedVar);
        Assert.Equal("BBS_AP_TARGET_VS", Bbs757Mcp.VsVar);
        Assert.Equal("BBS_AIRSPEED_IN_MACH", Bbs757Mcp.MachModeVar);
    }

    // ---- Speed bursts: BlueBird's knob tiers (session 7) ---------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]     // events 1-10 move 1 kt each (measured: 10 events +10)
    [InlineData(11, 12)]     // events 11-25 move 2 kt each
    [InlineData(20, 30)]     // measured: 20 events +30
    [InlineData(25, 40)]     // measured: 25 events +40
    [InlineData(26, 45)]     // from the 26th, 5 kt each
    [InlineData(30, 65)]     // measured: 30 events +65
    [InlineData(53, 180)]    // the first flight's burst: 53 events moved the target 180 kt
    public void A_burst_moves_the_speed_by_blueBirds_measured_tiers(int events, int knots)
    {
        Assert.Equal(knots, Bbs757Mcp.SpeedBurstKnots(events));
    }

    [Theory]
    [InlineData(7, 7, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(11, 10, 1)]
    [InlineData(13, 11, 1)]
    [InlineData(40, 25, 0)]
    [InlineData(41, 25, 1)]
    [InlineData(47, 26, 2)]     // 200 to 153: 26 events move 45 kt, 2 are left
    [InlineData(100, 37, 0)]
    [InlineData(180, 53, 0)]    // 100 to 280: the flight's 53 events, exactly
    [InlineData(299, 76, 4)]    // the whole 100 to 399 span
    [InlineData(0, 0, 0)]
    public void A_speed_change_is_one_planned_burst_and_the_knots_it_cannot_reach(int knots, int bulk, int rest)
    {
        Assert.Equal(new Bbs757SpeedBurstPlan(bulk, rest), Bbs757Mcp.SpeedBursts(knots));
    }

    [Fact]
    public void The_planned_burst_is_the_longest_that_does_not_pass_the_change()
    {
        for (int knots = 0; knots <= 500; knots++)
        {
            var (bulk, rest) = Bbs757Mcp.SpeedBursts(knots);

            Assert.Equal(knots, Bbs757Mcp.SpeedBurstKnots(bulk) + rest);
            Assert.True(Bbs757Mcp.SpeedBurstKnots(bulk + 1) > knots, $"{knots} kt: one more event would still fit");
            // The rest goes as a burst of its own, so every one of its events moves 1 kt.
            Assert.InRange(rest, 0, 4);
        }
    }

    [Theory]
    [InlineData(true, 26, "26 sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_INC) l0 1 - sp0 g1 }")]
    [InlineData(false, 2, "2 sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_DEC) l0 1 - sp0 g1 }")]
    [InlineData(true, 76, "76 sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_INC) l0 1 - sp0 g1 }")]
    public void A_burst_is_one_short_rpn_loop_for_any_count(bool up, int events, string code)
    {
        // No unique prefix: ExecuteCalculatorCodeUnique adds it on the way out.
        Assert.Equal(code, Bbs757Mcp.SpeedBurstCode(up, events));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_burst_loop_is_written_invariant_under_comma_decimal_cultures(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("1234 sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_DEC) l0 1 - sp0 g1 }", Bbs757Mcp.SpeedBurstCode(false, 1234));
            Assert.Equal("53 sp0 :1 l0 0 > if{ (>K:AP_SPD_VAR_INC) l0 1 - sp0 g1 }", Bbs757Mcp.SpeedBurstCode(true, 53));
        });
    }

    [Fact]
    public void HeadingWrite_sets_the_target_lvar_and_wraps_360_to_north()
    {
        Assert.Equal("0 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(360));
        Assert.Equal("90 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(90));
        Assert.Equal("359 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(359));
        Assert.Equal("359 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(-1));
    }

    [Fact]
    public void AltitudeWrite_rounds_to_the_hundred()
    {
        Assert.Equal("12000 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(12049));
        Assert.Equal("12100 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(12050));
        Assert.Equal("35000 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(35000));
    }

    [Fact]
    public void VsWrite_sets_the_target_lvar_signed()
    {
        Assert.Equal("-1500 (>L:BBS_AP_TARGET_VS)", Bbs757Mcp.VsWrite(-1500));
        Assert.Equal("0 (>L:BBS_AP_TARGET_VS)", Bbs757Mcp.VsWrite(0));
    }

    [Fact]
    public void VsSteps_step_the_difference_in_hundreds()
    {
        Assert.Equal(15, Count(Bbs757Mcp.VsSteps(0, -1500), "(>K:AP_VS_VAR_DEC)"));
        Assert.Equal(15, Count(Bbs757Mcp.VsSteps(-1500, 0), "(>K:AP_VS_VAR_INC)"));
        Assert.Equal(1, Count(Bbs757Mcp.VsSteps(1450, 1500), "(>K:AP_VS_VAR_INC)"));   // 50 fpm rounds up to one step
        Assert.Empty(Bbs757Mcp.VsSteps(1500, 1500));
        Assert.Empty(Bbs757Mcp.VsSteps(1500, 1530));   // less than half a step
        Assert.Empty(Bbs757Mcp.VsSteps(double.NaN, 1500));
    }

    // ---- Typed input ------------------------------------------------------------------------

    [Fact]
    public void A_typed_speed_under_a_comma_decimal_culture_is_accepted()
    {
        InCulture("de-DE", () =>
        {
            Assert.True(NumberParsing.TryParseTyped("250", out double value));
            Assert.True(FcuValueEntry.TrySpeed(value, out int speed, out _));
            Assert.Equal(250, speed);

            Assert.True(Bbs757Mach.TryParse("250", out var entry, out string error), error);
            Assert.Equal(250, entry.Knots);
        });
    }

    [Fact]
    public void Output_carries_no_comma_under_comma_decimal_cultures()
    {
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                var all = new List<string>
                {
                    Bbs757Mcp.HeadingWrite(90), Bbs757Mcp.AltitudeWrite(12050), Bbs757Mcp.VsWrite(-1500),
                    Bbs757Mcp.HeadingPhrase(90.4), Bbs757Mcp.SpeedPhrase(250.4),
                    Bbs757Mcp.AltitudePhrase(12000), Bbs757Mcp.AltitudePhrase(35000.2),
                    Bbs757Mcp.VsPhrase(-1500), Bbs757Mcp.VsPhrase(1500), Bbs757Mcp.VsPhrase(0),
                };
                all.Add(Bbs757Mcp.SpeedBurstCode(true, 27));
                all.Add(Bbs757Mcp.SpeedBurstCode(false, 4));
                all.AddRange(Bbs757Mcp.VsSteps(0, -1500));

                Assert.All(all, s => Assert.DoesNotContain(",", s));
                Assert.All(all, s => Assert.DoesNotContain("−", s));   // sv-SE's minus sign
            });
        }
    }

    [Fact]
    public void A_typed_heading_is_zero_to_360_and_360_is_north()
    {
        Assert.True(Bbs757Mcp.TryHeadingInput("90", out int hdg, out _));
        Assert.Equal(90, hdg);
        Assert.True(Bbs757Mcp.TryHeadingInput("360", out int north, out _));
        Assert.Equal(0, north);

        Assert.True(Bbs757Mcp.TryHeadingInput("090", out int padded, out _));   // the way a pilot types a heading
        Assert.Equal(90, padded);

        Assert.False(Bbs757Mcp.TryHeadingInput("361", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryHeadingInput("-5", out _, out _));
        Assert.False(Bbs757Mcp.TryHeadingInput("NaN", out _, out _));
        Assert.False(Bbs757Mcp.TryHeadingInput(null, out _, out _));
    }

    [Fact]
    public void A_typed_altitude_is_rounded_to_the_hundred()
    {
        Assert.True(Bbs757Mcp.TryAltitudeInput("12049", out int ft, out _));
        Assert.Equal(12000, ft);
        Assert.True(Bbs757Mcp.TryAltitudeInput("12050", out int up, out _));
        Assert.Equal(12100, up);
        Assert.True(Bbs757Mcp.TryAltitudeInput("100", out _, out _));
        Assert.True(Bbs757Mcp.TryAltitudeInput("49000", out _, out _));

        Assert.False(Bbs757Mcp.TryAltitudeInput("99", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryAltitudeInput("49001", out _, out _));
        Assert.False(Bbs757Mcp.TryAltitudeInput("high", out _, out _));
    }

    [Fact]
    public void A_typed_vertical_speed_is_minus_8000_to_6000_in_hundreds()
    {
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-1500", out int fpm, out _));
        Assert.Equal(-1500, fpm);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("0", out int level, out _));
        Assert.Equal(0, level);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-8000", out _, out _));
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("6000", out _, out _));

        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("1549", out int down, out _));
        Assert.Equal(1500, down);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("1550", out int up, out _));
        Assert.Equal(1600, up);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-1550", out int negUp, out _));
        Assert.Equal(-1600, negUp);

        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("6001", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("-8001", out _, out _));
        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("up", out _, out _));
    }

    // ---- Spoken phrases ---------------------------------------------------------------------

    [Fact]
    public void The_phrases_say_the_window_value()
    {
        Assert.Equal("Heading 090", Bbs757Mcp.HeadingPhrase(90));
        Assert.Equal("Heading 005", Bbs757Mcp.HeadingPhrase(4.6));
        Assert.Equal("Heading 000", Bbs757Mcp.HeadingPhrase(0));
        Assert.Equal("Heading 000", Bbs757Mcp.HeadingPhrase(359.6));   // rounds to 360, which is north
        Assert.Equal("Speed 250", Bbs757Mcp.SpeedPhrase(250.2));
        Assert.Equal("Altitude 12000", Bbs757Mcp.AltitudePhrase(12000));
        Assert.Equal("Altitude 35000", Bbs757Mcp.AltitudePhrase(35000));
        Assert.Equal("Vertical speed minus 1500", Bbs757Mcp.VsPhrase(-1500));
        Assert.Equal("Vertical speed 1500", Bbs757Mcp.VsPhrase(1500));
        Assert.Equal("Vertical speed 0", Bbs757Mcp.VsPhrase(0));
        Assert.Equal("Vertical speed 0", Bbs757Mcp.VsPhrase(-0.2));   // never "minus 0"
    }

    [Fact]
    public void An_unreadable_value_is_unavailable_never_a_zero()
    {
        Assert.Equal("Heading unavailable", Bbs757Mcp.HeadingPhrase(null));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(null));
        Assert.Equal("Altitude unavailable", Bbs757Mcp.AltitudePhrase(null));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(null));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(double.NaN));
    }

    // ---- Set loops against a simulated MCP ---------------------------------------------------

    /// <summary>
    /// A simulated MCP behind <see cref="McpIo"/>, with a virtual clock (<see cref="Now"/>, advanced
    /// only by the loop's waits).
    /// <list type="bullet">
    /// <item>The speed target moves as BlueBird's knob was measured live to move it,
    /// modeled here on its own, never through the code under test: a speed write fires its events
    /// (a burst loop's count, or each <c>(&gt;K:AP_SPD_VAR_…)</c> written out), and the writes with
    /// less than <see cref="BurstResetMs"/> between them are ONE burst whose count runs across them.
    /// <see cref="Tiered"/> (the default) moves a burst's events 1-10 1 kt each, 11-25 2 kt and every
    /// later one 5 kt; without it every event moves 1 kt. The target clamps at 100 and 399.</item>
    /// <item><see cref="IgnoresSpeedEvents"/> drops every speed event (the variable is overwritten),
    /// <see cref="EventsCountedPerBurst"/> drops a burst's events past that many, <see cref="LagMs"/>
    /// lands half of a write's move at once and the other half that long after, <see cref="DriftPerRead"/>
    /// keeps the speed moving on every read once a burst was sent (it never settles) and
    /// <see cref="SpeedAfterSend"/> makes it read wild once one was.</item>
    /// <item>V/S events move 100 fpm each, counted per calculator call (<see cref="EventsCountedPerCall"/>)
    /// and between two waits (<see cref="EventsCountedPerWindow"/>); <see cref="DirectWritesAdopted"/>
    /// says whether a target L:var takes a direct write.</item>
    /// </list>
    /// </summary>
    private sealed class FakeMcp
    {
        /// <summary>A pause this long or longer between two speed writes starts a new burst (measured: about 0.3 s).</summary>
        public const int BurstResetMs = 300;

        private static readonly Regex BurstLoop =
            new(@"^(\d+) sp0 :1 l0 0 > if\{ \(>K:AP_SPD_VAR_(INC|DEC)\) l0 1 - sp0 g1 \}$");

        public double? Speed = 200, Heading = 0, Altitude = 10000, Vs = 0, Mach = 0;
        public double? Blanked = 0, Drum100, Drum10;   // the speed window's blank flag; its tenths and hundredths wheels
        public readonly Dictionary<string, double?> Lights = new();   // MCP button lights by key; a missing one reads null

        /// <summary>Lights the MCP buttons with these labels.</summary>
        public FakeMcp Lit(params string[] labels)
        {
            foreach (string label in labels) Lights[Bbs757McpLights.All.First(l => l.Label == label).Key] = 1;
            return this;
        }
        public bool Tiered = true;
        public bool IgnoresSpeedEvents;
        public int EventsCountedPerBurst = int.MaxValue;
        public int EventsCountedPerCall = int.MaxValue;     // V/S
        public int EventsCountedPerWindow = int.MaxValue;   // V/S
        public bool DirectWritesAdopted = true;
        public readonly List<string> Writes = new();
        public readonly List<long> WriteTimes = new();      // virtual ms of each write
        public int SpeedReads;                              // reads of the speed target so far
        public readonly List<int> SpeedReadsAtWrite = new();   // SpeedReads when each write went out
        public int Delays;
        public long Now;                 // virtual ms
        public int LagMs;                // > 0: a speed write lands half now, half this long later
        public double DriftPerRead;      // added to the speed on every read once a burst was sent
        public double? SpeedAfterSend;   // when set, the speed reads this once a burst was sent (a wild read)
        public double Peak;              // highest speed the target ever held after a burst landed
        public bool CancelNextDelay;     // the next wait throws as a superseded set's does, before any time passes
        public Action? OnSpeedRead;      // runs inside each read of the speed target, after its cancellation check
        private readonly List<(long Due, double Delta)> _pending = new();
        private bool _sent;
        private int _used;               // V/S events counted since the last wait
        private int _burstEvents;        // speed events in the burst under way
        private long? _lastSpeedWrite;   // virtual ms of the last speed write

        private void MoveSpeed(double delta)
        {
            if (Speed == null) return;
            Speed = Math.Clamp(Speed.Value + delta, 100, 399);
            Peak = Math.Max(Peak, Speed.Value);
        }

        private void Land()
        {
            foreach (var due in _pending.Where(p => p.Due <= Now).ToList())
            {
                MoveSpeed(due.Delta);
                _pending.Remove(due);
            }
        }

        private int Counted(string rpn, string kEvent)
        {
            int n = Math.Min(Count(new[] { rpn }, kEvent), EventsCountedPerCall);
            n = Math.Min(n, Math.Max(0, EventsCountedPerWindow - _used));
            _used += n;
            return n;
        }

        /// <summary>One speed write: its events join the burst under way, or start a new one after a pause.</summary>
        private void SpeedWrite(string rpn)
        {
            var loop = BurstLoop.Match(rpn);
            int events = loop.Success
                ? int.Parse(loop.Groups[1].Value, CultureInfo.InvariantCulture)
                : Count(new[] { rpn }, "(>K:AP_SPD_VAR_INC)") + Count(new[] { rpn }, "(>K:AP_SPD_VAR_DEC)");
            int sign = rpn.Contains("AP_SPD_VAR_INC") ? 1 : -1;

            if (_lastSpeedWrite is not long last || Now - last >= BurstResetMs) _burstEvents = 0;
            _lastSpeedWrite = Now;
            _sent = true;

            double moved = 0;
            for (int i = 0; i < events; i++)
            {
                _burstEvents++;
                if (IgnoresSpeedEvents || _burstEvents > EventsCountedPerBurst) continue;
                moved += !Tiered ? 1 : _burstEvents <= 10 ? 1 : _burstEvents <= 25 ? 2 : 5;
            }
            if (LagMs > 0)
            {
                double first = Math.Truncate(moved / 2);
                MoveSpeed(sign * first);
                _pending.Add((Now + LagMs, sign * (moved - first)));
            }
            else MoveSpeed(sign * moved);
            if (SpeedAfterSend != null) Speed = SpeedAfterSend;
        }

        public McpIo Io => new()
        {
            Read = (key, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Land();
                if (key == Bbs757Mcp.SpeedVar)
                {
                    SpeedReads++;
                    OnSpeedRead?.Invoke();
                }
                if (key == Bbs757Mcp.SpeedVar && _sent && DriftPerRead != 0) MoveSpeed(DriftPerRead);
                return Task.FromResult(key switch
                {
                    Bbs757Mcp.SpeedVar => Speed,
                    Bbs757Mcp.HeadingVar => Heading,
                    Bbs757Mcp.AltitudeVar => Altitude,
                    Bbs757Mcp.VsVar => Vs,
                    Bbs757Mcp.MachModeVar => Mach,
                    Bbs757Mcp.IasBlankedVar => Blanked,
                    Bbs757Mcp.MachTenthsDrumVar => Drum100,
                    Bbs757Mcp.MachHundredthsDrumVar => Drum10,
                    _ => Lights.TryGetValue(key, out double? light) ? light : (double?)null,
                });
            },
            Write = rpn =>
            {
                Writes.Add(rpn);
                WriteTimes.Add(Now);
                SpeedReadsAtWrite.Add(SpeedReads);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_HEADING)") && DirectWritesAdopted) Heading = Value(rpn);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_ALTITUDE)") && DirectWritesAdopted) Altitude = Value(rpn);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_VS)") && DirectWritesAdopted) Vs = Value(rpn);

                if (rpn.Contains("AP_SPD_VAR_")) SpeedWrite(rpn);

                int vsInc = Counted(rpn, "(>K:AP_VS_VAR_INC)");
                int vsDec = Counted(rpn, "(>K:AP_VS_VAR_DEC)");
                if (Vs != null) Vs += 100 * (vsInc - vsDec);
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                if (CancelNextDelay)
                {
                    CancelNextDelay = false;
                    throw new OperationCanceledException();
                }
                Delays++;
                Now += ms;
                _used = 0;
                Land();
                return Task.CompletedTask;
            },
        };

        private static double Value(string rpn) => double.Parse(rpn.Split(' ')[0], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task A_set_superseded_during_its_speed_read_sends_nothing()
    {
        // The final review's minor 1: a newer set cancels this one while its read is in flight, and the
        // read still answers. Its burst must not go out after the newer set made its spacing check, or
        // the two bursts merge into one accelerated burst.
        var mcp = new FakeMcp();
        using var cts = new CancellationTokenSource();
        mcp.OnSpeedRead = () => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, cts.Token));

        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task Setting_the_heading_writes_directly_and_speaks_the_window_value()
    {
        var mcp = new FakeMcp();

        string said = await Bbs757Mcp.SetHeadingAsync(mcp.Io, 90, default);

        Assert.Equal(new[] { "90 (>L:BBS_AP_TARGET_HEADING)" }, mcp.Writes);
        Assert.Equal("Heading 090", said);
    }

    [Fact]
    public async Task A_heading_the_window_did_not_take_is_spoken_as_the_window_shows_it()
    {
        var mcp = new FakeMcp { DirectWritesAdopted = false, Heading = 45 };

        Assert.Equal("Heading 045", await Bbs757Mcp.SetHeadingAsync(mcp.Io, 90, default));
    }

    [Fact]
    public async Task Setting_the_altitude_speaks_the_window_value()
    {
        var mcp = new FakeMcp();

        string said = await Bbs757Mcp.SetAltitudeAsync(mcp.Io, 12000, default);

        Assert.Equal(new[] { "12000 (>L:BBS_AP_TARGET_ALTITUDE)" }, mcp.Writes);
        Assert.Equal("Altitude 12000", said);
    }

    [Fact]
    public async Task A_speed_set_sends_one_planned_burst_and_speaks_what_it_reads()
    {
        var mcp = new FakeMcp { Speed = 200 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(true, 27) }, mcp.Writes);   // 27 events: 10 + 30 + 10 kt, nothing to correct
        Assert.DoesNotContain(mcp.Writes, w => w.Contains("L:BBS757_ORANGE_AIRSPEED_BUGS"));   // read-only: never written directly
    }

    [Fact]
    public async Task Two_hundred_to_153_lands_on_blueBirds_tiers_with_no_correction()
    {
        var mcp = new FakeMcp { Speed = 200 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 153, default);

        Assert.Equal("Speed 153", said);
        Assert.Equal(153, mcp.Speed);
        // 26 events move 45 kt; the 2 knots left go as a burst of their own, after the pause that
        // restarts BlueBird's count, so they move 1 kt each and not 5.
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(false, 26), Bbs757Mcp.SpeedBurstCode(false, 2) }, mcp.Writes);
        Assert.True(mcp.WriteTimes[1] - mcp.WriteTimes[0] >= Bbs757Mcp.SpeedBurstGapMs);
        Assert.Equal(new[] { 1, 1 }, mcp.SpeedReadsAtWrite);   // both before any settle read: the 2 are the plan's, not a correction
    }

    [Fact]
    public async Task One_hundred_to_280_lands_on_blueBirds_tiers_in_one_burst()
    {
        var mcp = new FakeMcp { Speed = 100 };

        Assert.Equal("Speed 280", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 280, default));
        Assert.Equal(280, mcp.Speed);
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(true, 53) }, mcp.Writes);   // the flight's 53 events: 180 kt
    }

    [Theory]
    [InlineData(100)]   // 100 to 280 among them
    [InlineData(153)]
    [InlineData(200)]   // 200 to 153 and 200 to 250 among them
    [InlineData(250)]
    [InlineData(399)]
    public async Task Every_speed_from_100_to_399_lands_with_no_correction(double from)
    {
        for (int to = 100; to <= 399; to++)
        {
            var mcp = new FakeMcp { Speed = from };

            Assert.Equal($"Speed {to}", await Bbs757Mcp.SetSpeedAsync(mcp.Io, to, default));
            // Exactly the planned burst and its rest, both sent on the one read that planned them. A
            // correction comes only after a settle read, and a correction of the rest would be the very
            // same string as the planned rest, so the reads are what tell them apart.
            Assert.Equal(PlannedBursts(from, to), mcp.Writes);
            Assert.All(mcp.SpeedReadsAtWrite, reads => Assert.Equal(1, reads));
        }
    }

    /// <summary>What a set from <paramref name="from"/> to <paramref name="to"/> plans: its bulk burst, then its rest when there is one.</summary>
    private static List<string> PlannedBursts(double from, int to)
    {
        int diff = to - (int)from;
        var (bulk, rest) = Bbs757Mcp.SpeedBursts(Math.Abs(diff));
        var writes = new List<string>();
        if (bulk > 0) writes.Add(Bbs757Mcp.SpeedBurstCode(diff > 0, bulk));
        if (rest > 0) writes.Add(Bbs757Mcp.SpeedBurstCode(diff > 0, rest));
        return writes;
    }

    [Fact]
    public async Task Plain_one_knot_steps_converge_through_the_corrections()
    {
        // An MCP that does not speed up (every event 1 kt): the planned burst falls short, and the
        // corrections, 1 kt an event, bring the speed the rest of the way.
        var down = new FakeMcp { Speed = 200, Tiered = false };
        Assert.Equal("Speed 153", await Bbs757Mcp.SetSpeedAsync(down.Io, 153, default));
        Assert.True(down.Writes.Count > 2, "corrected");

        var up = new FakeMcp { Speed = 100, Tiered = false };
        Assert.Equal("Speed 280", await Bbs757Mcp.SetSpeedAsync(up.Io, 280, default));
        Assert.True(up.Writes.Count > 1, "corrected");
    }

    [Fact]
    public async Task A_correction_sends_bursts_of_at_most_ten_a_pause_apart()
    {
        // The planned burst (53 events) moves only 53 kt on an MCP that does not speed up: 127 are left.
        var mcp = new FakeMcp { Speed = 100, Tiered = false };

        await Bbs757Mcp.SetSpeedAsync(mcp.Io, 280, default);

        var corrections = mcp.Writes.Skip(1).ToList();
        Assert.Equal(127, corrections.Sum(EventsIn));
        Assert.All(corrections, w => Assert.InRange(EventsIn(w), 1, Bbs757Mcp.LinearBurstMax));
        for (int i = 2; i < mcp.WriteTimes.Count; i++)
            Assert.True(mcp.WriteTimes[i] - mcp.WriteTimes[i - 1] >= Bbs757Mcp.SpeedBurstGapMs, $"write {i} came too soon");
    }

    [Fact]
    public async Task A_speed_already_set_sends_nothing()
    {
        var mcp = new FakeMcp { Speed = 250 };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_speed_set_in_mach_mode_writes_nothing_and_says_so()
    {
        // The definition changes the window to knots first; Mach here means that did not hold.
        var mcp = new FakeMcp { Mach = 1 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("IAS/MACH changeover is not available now", said);
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task An_unreadable_mach_flag_does_not_stop_a_knots_set()
    {
        var mcp = new FakeMcp { Mach = null };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
    }

    [Fact]
    public async Task A_speed_set_with_the_target_unreadable_writes_nothing()
    {
        var mcp = new FakeMcp { Speed = null };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_burst_the_aircraft_only_partly_counts_is_corrected_until_it_lands()
    {
        // Only a burst's first 10 events count: the planned 27 move 10 kt. The correction's bursts of
        // at most ten each count whole and stay on the 1 kt tier, so it lands exactly.
        var mcp = new FakeMcp { Speed = 200, EventsCountedPerBurst = 10 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.True(mcp.Writes.Count > 1, "the remainder was sent again");
        Assert.Equal(250, mcp.Peak);   // never past the target
    }

    [Fact]
    public async Task A_speed_that_does_not_move_is_not_chased()
    {
        // The variable is overwritten by the aircraft: after the first round the difference has not
        // shrunk, so nothing more is sent, and the pilot hears the value the window shows.
        var mcp = new FakeMcp { Speed = 200, IgnoresSpeedEvents = true };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 153, default);

        Assert.Equal("Speed 200", said);
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(false, 26), Bbs757Mcp.SpeedBurstCode(false, 2) }, mcp.Writes);   // the first round only
    }

    [Fact]
    public async Task Corrections_stop_after_three_rounds()
    {
        // Only 5 events of a burst count: the difference shrinks every round, forever.
        var mcp = new FakeMcp { Speed = 200, EventsCountedPerBurst = 5 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        // The planned burst 205; then 45 left in five bursts, 230; 20 in two, 240; 10 in one, 245; and no more.
        Assert.Equal("Speed 245", said);
    }

    [Fact]
    public async Task An_overshoot_is_brought_back()
    {
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var overshooting = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 4; }   // 254 after the first call
            },
        };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(overshooting, 250, default));
    }

    [Fact]
    public async Task A_speed_set_reads_again_after_it_waits()
    {
        var mcp = new FakeMcp { Speed = 200 };

        await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.True(mcp.Delays >= 1);
    }

    [Fact]
    public async Task A_burst_that_lands_in_two_parts_is_judged_once_both_have_landed()
    {
        // Half of each call lands at once, the other half 700 ms later. Judged 600 ms after the send
        // the speed reads 225 and the "remaining 25" would be sent again, overshooting to 275.
        var mcp = new FakeMcp { Speed = 200, LagMs = 700 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.Equal(250, mcp.Speed);
        Assert.Equal(250, mcp.Peak);          // never above the target, even for a moment
        Assert.Single(mcp.Writes);            // the first send only: nothing was left to correct
    }

    [Fact]
    public async Task The_settle_read_gives_up_after_about_a_second_and_a_half_with_the_last_read()
    {
        // The speed keeps moving after the burst (5 kt a read), so two reads never agree.
        var mcp = new FakeMcp { Speed = 200, DriftPerRead = 5 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        // 600 ms settle + ten 150 ms polls; the last of eleven reads is 250 + 5 * 11 = 305, which is
        // further from 250 than the 50 started with, so nothing is chased.
        Assert.Equal(600 + Bbs757Mcp.SettleGiveUpMs, mcp.Now);
        Assert.Equal("Speed 305", said);
        Assert.Single(mcp.Writes);
    }

    [Fact]
    public async Task An_overshoot_nearer_than_the_start_counts_as_progress()
    {
        // 200 to 250 lands on 256 (the remaining difference changed sign, 6 against 50): brought back.
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var overshooting = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 6; }
            },
        };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(overshooting, 250, default));
        Assert.Contains(mcp.Writes, w => w.Contains("(>K:AP_SPD_VAR_DEC)"));
    }

    [Fact]
    public async Task An_overshoot_no_nearer_than_the_start_is_not_chased()
    {
        // 200 to 250 lands on 310 (60 off against 50): another round would only widen it.
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var wild = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 60; }
            },
        };

        Assert.Equal("Speed 310", await Bbs757Mcp.SetSpeedAsync(wild, 250, default));
        Assert.DoesNotContain(mcp.Writes, w => w.Contains("(>K:AP_SPD_VAR_DEC)"));
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(-5)]
    [InlineData(500.5)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_speed_target_read_outside_zero_to_500_is_unreadable_and_sends_nothing(double wild)
    {
        var mcp = new FakeMcp { Speed = wild };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_speed_target_that_reads_wild_after_the_burst_is_not_corrected()
    {
        var mcp = new FakeMcp { Speed = 200, SpeedAfterSend = 4000 };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Single(mcp.Writes);   // the first send only
    }

    [Fact]
    public void A_round_sends_at_most_140_vertical_speed_steps()
    {
        Assert.Equal(140, Count(Bbs757Mcp.VsSteps(-8000, 6000), "(>K:AP_VS_VAR_INC)"));
        Assert.Equal(140, Count(Bbs757Mcp.VsSteps(-1_000_000, 6000), "(>K:AP_VS_VAR_INC)"));
    }

    [Fact]
    public void The_phrases_refuse_a_value_that_is_not_a_speed_or_a_vertical_speed()
    {
        Assert.Equal("Speed 500", Bbs757Mcp.SpeedPhrase(500));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(501));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(-1));
        Assert.Equal("Vertical speed 6000", Bbs757Mcp.VsPhrase(6000));
        Assert.Equal("Vertical speed minus 8000", Bbs757Mcp.VsPhrase(-8000));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(6001));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(-8001));
    }

    [Fact]
    public async Task A_vertical_speed_read_outside_the_range_is_unreadable_and_not_stepped()
    {
        var mcp = new FakeMcp { Vs = 1_000_000, DirectWritesAdopted = false };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal("Vertical speed unavailable", said);
        Assert.Equal(new[] { "-1500 (>L:BBS_AP_TARGET_VS)" }, mcp.Writes);   // the direct write only
    }

    [Fact]
    public async Task A_canceled_speed_set_ends_without_speaking()
    {
        var mcp = new FakeMcp { Speed = 200 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, cts.Token));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_vertical_speed_the_window_takes_is_not_stepped()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = true };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal(new[] { "-1500 (>L:BBS_AP_TARGET_VS)" }, mcp.Writes);
        Assert.Equal("Vertical speed minus 1500", said);
    }

    [Fact]
    public async Task A_vertical_speed_write_the_window_ignores_is_stepped_by_hundreds()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = false };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal(15, Count(mcp.Writes, "(>K:AP_VS_VAR_DEC)"));
        Assert.Equal("Vertical speed minus 1500", said);
    }

    [Fact]
    public async Task A_vertical_speed_still_off_after_stepping_is_spoken_as_read()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = false, EventsCountedPerCall = 0 };

        Assert.Equal("Vertical speed 0", await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default));
    }

    [Fact]
    public async Task Readouts_speak_fresh_values()
    {
        var mcp = new FakeMcp { Heading = 90, Altitude = 12000, Speed = 250, Vs = -1500 };

        Assert.Equal("Heading 090", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude 12000", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
        Assert.Equal("Speed 250", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Vertical speed minus 1500", await Bbs757Mcp.ReadVerticalSpeedAsync(mcp.Io, default));
        Assert.Empty(mcp.Writes);
    }

    // ---- The lit mode buttons after a readout (Declan's request, 2026-10-30) ----------------

    [Fact]
    public async Task Readouts_name_the_lit_mode_buttons_after_the_value()
    {
        var mcp = new FakeMcp { Heading = 90, Altitude = 12000, Speed = 250, Vs = -1500 }
            .Lit("LNAV", "SPD", "V/S", "CMD L");

        Assert.Equal("Heading 090, LNAV", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude 12000, V/S", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
        Assert.Equal("Speed 250, SPD", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Vertical speed minus 1500, V/S", await Bbs757Mcp.ReadVerticalSpeedAsync(mcp.Io, default));
    }

    [Fact]
    public async Task Several_lit_buttons_are_named_in_panel_order()
    {
        var mcp = new FakeMcp { Heading = 270, Altitude = 3000 }.Lit("APP", "LOC", "ALT HOLD");

        Assert.Equal("Heading 270, LOC, APP", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude 3000, ALT HOLD", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
    }

    [Fact]
    public async Task The_blank_and_mach_windows_name_their_lit_buttons_too()
    {
        var blank = new FakeMcp { Blanked = 1, Speed = 250 }.Lit("VNAV", "THR");
        var mach = new FakeMcp { Mach = 1, Drum100 = 70, Drum10 = 90 }.Lit("FLCH");

        Assert.Equal("MCP speed window blank, THR, VNAV. Speed Intervene opens it.", await Bbs757Mcp.ReadSpeedAsync(blank.Io, default));
        Assert.Equal("MCP Mach 0.78, FLCH", await Bbs757Mcp.ReadSpeedAsync(mach.Io, default));
        InCulture("de-DE", () => Assert.Equal("MCP Mach 0.78, FLCH",
            Bbs757Mcp.SpeedReadout(0, 1, null, 0.78, new[] { "FLCH" })));
    }

    [Fact]
    public async Task An_unread_value_names_no_mode_and_an_unread_light_is_not_lit()
    {
        var mcp = new FakeMcp { Heading = null, Speed = null, Altitude = 5000 }.Lit("LNAV", "SPD");
        mcp.Lights[Bbs757McpLights.All.First(l => l.Label == "ALT HOLD").Key] = double.NaN;

        Assert.Equal("Heading unavailable", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Speed unavailable", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Altitude 5000", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
    }

    [Fact]
    public async Task The_lights_come_from_the_cache_when_there_is_one()
    {
        var mcp = new FakeMcp { Heading = 90 };   // nothing lit when read fresh
        var reads = new List<string>();
        var inner = mcp.Io;
        string lnav = Bbs757McpLights.All.First(l => l.Label == "LNAV").Key;
        var io = new McpIo
        {
            Read = (key, ct) => { reads.Add(key); return inner.Read(key, ct); },
            Write = inner.Write,
            Delay = inner.Delay,
            Cached = key => key == lnav ? 1 : 0,
        };

        Assert.Equal("Heading 090, LNAV", await Bbs757Mcp.ReadHeadingAsync(io, default));
        Assert.Equal(new[] { Bbs757Mcp.HeadingVar }, reads);
    }

    [Fact]
    public void Every_mode_light_named_is_an_mcp_light()
    {
        var labels = Bbs757McpLights.All.Select(l => l.Label).ToHashSet();
        foreach (string label in Bbs757Mcp.HeadingModeLights.Concat(Bbs757Mcp.SpeedModeLights)
                     .Concat(Bbs757Mcp.AltitudeModeLights).Concat(Bbs757Mcp.VsModeLights))
            Assert.Contains(label, labels);
    }

    [Fact]
    public async Task The_speed_readout_does_not_pass_a_knots_target_off_as_the_mach_window()
    {
        var mcp = new FakeMcp { Speed = 250, Mach = 1 };

        Assert.Equal(Bbs757Mcp.MachModeReadNote, await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
    }

    // ---- The speed window while blank, and Mach (plan 05, Task 2) ----------------------------

    [Theory]
    [InlineData(1.0, 0.0, 250.0, null, "MCP speed window blank. Speed Intervene opens it.")]
    [InlineData(1.0, 1.0, 250.0, 0.78, "MCP speed window blank. Speed Intervene opens it.")]   // blank wins
    [InlineData(0.0, 1.0, 281.1, 0.78, "MCP Mach 0.78")]
    [InlineData(0.0, 1.0, 281.1, null, "MCP is in Mach mode. Mach is not read yet.")]
    [InlineData(0.0, 0.0, 250.0, null, "Speed 250")]
    [InlineData(null, null, 250.0, null, "Speed 250")]                                         // nothing known: today's words
    [InlineData(0.0, 0.0, null, null, "Speed unavailable")]
    public void Speed_readout_cases(double? blanked, double? machFlag, double? knots, double? mach, string said)
    {
        Assert.Equal(said, Bbs757Mcp.SpeedReadout(blanked, machFlag, knots, mach));
        InCulture("de-DE", () => Assert.Equal(said, Bbs757Mcp.SpeedReadout(blanked, machFlag, knots, mach)));
    }

    [Theory]
    // Measured at FL290 on 2026-10-26 ([B757-50]): the tenths wheel reads 10·d, the hundredths wheel 10·(d+1).
    [InlineData(70.0, 40.0, 0.73)]
    [InlineData(70.0, 50.0, 0.74)]
    [InlineData(70.0, 100.0, 0.79)]
    [InlineData(60.0, 100.0, 0.69)]
    [InlineData(40.0, 10.0, 0.40)]
    [InlineData(80.0, 60.0, 0.85)]
    [InlineData(90.0, 60.0, 0.95)]
    public void Mach_from_the_window_wheels(double tenths, double hundredths, double mach)
    {
        Assert.Equal(mach, Bbs757Mcp.MachFromDrums(tenths, hundredths)!.Value, 3);
    }

    [Theory]
    [InlineData(null, 40.0)]
    [InlineData(70.0, null)]
    [InlineData(70.0, 45.0)]     // a wheel caught between digits: not a reading
    [InlineData(74.0, 40.0)]
    [InlineData(70.0, 0.0)]      // the hundredths wheel never reads below 10
    [InlineData(70.0, 110.0)]
    [InlineData(100.0, 40.0)]
    [InlineData(double.NaN, 40.0)]
    [InlineData(20.0, 90.0)]     // 283 kt in IAS reads ".28": knots, never a Mach the window takes (review finding 1)
    [InlineData(30.0, 10.0)]     // .30: below the .40 floor
    public void Wheels_that_are_not_a_mach_read_as_unread(double? tenths, double? hundredths)
    {
        Assert.Null(Bbs757Mcp.MachFromDrums(tenths, hundredths));
    }

    [Fact]
    public async Task Shift_s_says_the_window_is_blank_rather_than_the_held_speed()
    {
        // Measured in the VNAV climb: the window blank, the bug still 250 behind it.
        var mcp = new FakeMcp { Blanked = 1, Speed = 250 };

        Assert.Equal("MCP speed window blank. Speed Intervene opens it.", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
    }

    [Fact]
    public async Task Shift_s_reads_mach_from_the_wheels()
    {
        var mcp = new FakeMcp { Blanked = 0, Mach = 1, Speed = 281.1, Drum100 = 70, Drum10 = 40 };

        Assert.Equal("MCP Mach 0.73", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
    }

    [Theory]
    [InlineData(1.0, 0.0, "MCP speed window blank. Speed Intervene opens it.")]
    [InlineData(0.0, 1.0, "Speed 250")]
    public async Task Shift_s_takes_the_blank_flag_from_the_cache_not_a_fresh_read(double cached, double fresh, string expected)
    {
        // The flag is batched: a fresh read of it waited up to a second for the next batch (2026-10-27).
        var mcp = new FakeMcp { Blanked = fresh, Speed = 250 };
        var reads = new List<string>();
        var inner = mcp.Io;
        var io = new McpIo
        {
            Read = (key, ct) => { reads.Add(key); return inner.Read(key, ct); },
            Write = inner.Write,
            Delay = inner.Delay,
            Cached = key => key == Bbs757Mcp.IasBlankedVar ? cached : null,
        };

        Assert.Equal(expected, await Bbs757Mcp.ReadSpeedAsync(io, default));
        Assert.DoesNotContain(Bbs757Mcp.IasBlankedVar, reads);
    }

    [Fact]
    public async Task Shift_s_reads_the_blank_flag_fresh_when_nothing_is_cached()
    {
        var mcp = new FakeMcp { Blanked = 1, Speed = 250 };
        var inner = mcp.Io;
        var io = new McpIo { Read = inner.Read, Write = inner.Write, Delay = inner.Delay, Cached = _ => null };

        Assert.Equal("MCP speed window blank. Speed Intervene opens it.", await Bbs757Mcp.ReadSpeedAsync(io, default));
    }

    [Fact]
    public void The_mach_wheels_are_read_on_request_for_the_readout()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (string key in new[] { Bbs757Mcp.MachTenthsDrumVar, Bbs757Mcp.MachHundredthsDrumVar })
        {
            Assert.Equal(key, vars[key].Name);
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.True(vars[key].ExcludeFromMonitorManager);
        }
        Assert.Equal("B757_MCP_Gau_SPD_AUTO_100", Bbs757Mcp.MachTenthsDrumVar);
        Assert.Equal("B757_MCP_Gau_SPD_AUTO_10", Bbs757Mcp.MachHundredthsDrumVar);
    }

    [Theory]
    [InlineData(1.0, false)]
    [InlineData(0.0, true)]
    [InlineData(null, true)]     // nothing delivered yet: the box stays open, as before
    public void Speed_box_closed_while_blank(double? blanked, bool open)
    {
        var cache = new Dictionary<string, double?>(StringComparer.Ordinal) { [Bbs757Mcp.IasBlankedVar] = blanked };
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            WriteIo = _ => new Bbs757SimIo
            {
                CanWrite = () => true,
                ReadFresh = (_, _) => Task.FromResult<double?>(null),
                Cached = key => cache.GetValueOrDefault(key),
                Write = _ => { },
                RequestRead = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        var sim = new SimConnectManager(IntPtr.Zero);

        Assert.Equal(open, def.McpInputEnabled(HotkeyAction.FCUSetSpeed, sim)!());
        Assert.Null(def.McpInputEnabled(HotkeyAction.FCUSetHeading, sim));   // the other windows never close
    }

    [Fact]
    public async Task Unreadable_values_are_spoken_unavailable()
    {
        var mcp = new FakeMcp { Heading = null, Altitude = null, Speed = null, Vs = null };

        Assert.Equal("Heading unavailable", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude unavailable", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
        Assert.Equal("Speed unavailable", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Vertical speed unavailable", await Bbs757Mcp.ReadVerticalSpeedAsync(mcp.Io, default));
    }

    // ---- The definition ---------------------------------------------------------------------

    [Fact]
    public void The_mcp_variables_are_registered_on_request_so_a_fresh_read_can_deliver_them()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (string key in new[]
                 {
                     Bbs757Mcp.HeadingVar, Bbs757Mcp.AltitudeVar, Bbs757Mcp.SpeedVar,
                     Bbs757Mcp.VsVar, Bbs757Mcp.MachModeVar,
                 })
        {
            Assert.True(vars.ContainsKey(key), key);
            Assert.Equal(key, vars[key].Name);
            Assert.Equal(SimVarType.LVar, vars[key].Type);
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.False(vars[key].IsAnnounced);
            Assert.True(vars[key].ExcludeFromMonitorManager);   // read for the readouts only; a Ctrl+M row would mute nothing
        }
    }

    [Fact]
    public void The_speed_window_blank_flag_is_batched_for_the_windows_and_never_spoken()
    {
        // One batch slot: the windows' 400 ms button refresh reads the cache (plan 05, Task 1). Only a
        // Continuous AND IsAnnounced var joins the batch; one that is not announced gets no subscription
        // at all and was never delivered (measured 2026-10-26: Speed Intervene's caption stayed blank).
        var flag = new BBS757Definition().GetVariables()[Bbs757Mcp.IasBlankedVar];

        Assert.Equal("BBS_IAS_BLANKED", flag.Name);
        Assert.Equal(SimVarType.LVar, flag.Type);
        Assert.Equal(UpdateFrequency.Continuous, flag.UpdateFrequency);
        Assert.True(flag.IsAnnounced);
        Assert.False(flag.ExcludeFromBatch);
        Assert.True(flag.ExcludeFromMonitorManager);   // a window's cache, never spoken: no Ctrl+M row ([VAR-9])
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void The_speed_window_blank_flag_is_consumed_silently(double value)
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        Assert.True(def.ProcessSimVarUpdate(Bbs757Mcp.IasBlankedVar, value, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_mcp_variables_are_on_no_panel()
    {
        var onPanels = new BBS757Definition().GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Bbs757Mcp.HeadingVar, onPanels);
        Assert.DoesNotContain(Bbs757Mcp.SpeedVar, onPanels);
    }

    [Fact]
    public async Task Every_set_that_cannot_land_is_refused_aloud_and_writes_nothing()
    {
        // Never connected: CalcWriteCanLand is false ([MD11-16]).
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetMcpHeadingAsync(90, sim, speech);
        await def.SetMcpSpeedAsync(250, sim, speech);
        await def.SetMcpAltitudeAsync(12000, sim, speech);
        await def.SetMcpVerticalSpeedAsync(-1500, sim, speech);

        Assert.Equal(new[]
        {
            "Heading unavailable", "Speed unavailable", "Altitude unavailable", "Vertical speed unavailable",
        }, speech.All);
    }

    /// <summary>A set held in flight until released, counting how many started and how many were canceled.</summary>
    private sealed class HeldSet
    {
        public readonly TaskCompletionSource<string> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public int Canceled;

        public Func<McpIo, CancellationToken, Task<string>> Set => async (_, ct) =>
        {
            Started++;
            try { return await Release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { Canceled++; throw; }
        };
    }

    [Fact]
    public async Task A_repeat_of_the_speed_being_set_is_ignored_but_a_different_speed_supersedes_it()
    {
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();

        Task first = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        Task repeat = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        Assert.True(repeat.IsCompleted);
        Assert.Equal(1, held.Started);   // the repeat started nothing
        Assert.Equal(0, held.Canceled);  // and did not cancel the set in flight

        Task other = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 240, true, "Speed unavailable", sim, speech, held.Set);
        Assert.Equal(2, held.Started);

        held.Release.SetResult("Speed 240");
        await Task.WhenAll(first, other);

        Assert.Equal(1, held.Canceled);                    // 250 was superseded
        Assert.Equal(new[] { "Speed 240" }, speech.All);   // and said nothing
    }

    [Fact]
    public async Task The_same_speed_can_be_set_again_once_the_first_set_has_finished()
    {
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();
        held.Release.SetResult("Speed 250");

        await def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        await def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);

        Assert.Equal(2, held.Started);
        Assert.Equal(new[] { "Speed 250", "Speed 250" }, speech.All);
    }

    [Fact]
    public async Task A_speed_set_right_after_a_superseded_one_waits_out_blueBirds_burst_gap()
    {
        // The first set is cut off just after its burst, as a newer set supersedes it. Sent at once, the
        // newer set's events would join BlueBird's count (its 28th on move 5 kt each) and overshoot.
        var mcp = new FakeMcp { Speed = 200, CancelNextDelay = true };
        var def = new BBS757Definition { CanWrite = _ => true, NowMs = () => mcp.Now, McpSetIo = _ => mcp.Io };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetMcpSpeedAsync(250, sim, speech);   // its 27 events land on 250, then it is cut off
        await def.SetMcpSpeedAsync(260, sim, speech);

        Assert.Equal(new[] { "Speed 260" }, speech.All);   // the superseded set says nothing
        Assert.Equal(260, mcp.Speed);
        Assert.Equal(2, mcp.Writes.Count);
        Assert.True(mcp.WriteTimes[1] - mcp.WriteTimes[0] >= Bbs757Mcp.SpeedBurstGapMs, "the newer burst went out inside BlueBird's window");
    }

    [Fact]
    public async Task A_speed_set_long_after_the_last_burst_does_not_wait()
    {
        var mcp = new FakeMcp { Speed = 200 };
        var def = new BBS757Definition { CanWrite = _ => true, NowMs = () => mcp.Now, McpSetIo = _ => mcp.Io };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetMcpSpeedAsync(250, sim, speech);   // its settle reads take it well past the gap
        long finished = mcp.Now;
        await def.SetMcpSpeedAsync(260, sim, speech);

        Assert.Equal(new[] { "Speed 250", "Speed 260" }, speech.All);
        Assert.Equal(finished, mcp.WriteTimes[1]);   // read and sent at once, with no wait first
    }

    [Fact]
    public async Task A_repeat_of_a_heading_is_not_ignored()
    {
        // Only the slow speed loop is protected; a heading write is instant and idempotent.
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();

        Task first = def.RunMcpSetAsync(Bbs757Mcp.HeadingVar, 90, false, "Heading unavailable", sim, speech, held.Set);
        Task repeat = def.RunMcpSetAsync(Bbs757Mcp.HeadingVar, 90, false, "Heading unavailable", sim, speech, held.Set);

        Assert.Equal(2, held.Started);
        held.Release.SetResult("Heading 090");
        await Task.WhenAll(first, repeat);
        Assert.Equal(new[] { "Heading 090" }, speech.All);
    }

    [Fact]
    public void The_heading_window_and_the_guide_agree_on_the_range()
    {
        // Both say 360 is north: the validator takes it, and the window's label says so.
        Assert.True(Bbs757Mcp.TryHeadingInput("360", out int north, out _));
        Assert.Equal(0, north);
        string guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", "BBS757_Hotkeys.txt"));
        Assert.Contains("Set MCP Heading (0-360, 360 is north)", guide);
    }

    [Fact]
    public async Task A_readout_with_no_connection_says_unavailable()
    {
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.ReadMcpAsync(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadHeading, sim, speech);

        Assert.Equal(new[] { "Heading unavailable" }, speech.All);
    }
}
