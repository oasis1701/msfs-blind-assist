using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's ILS control panel (plan 04, task 6a), counted. No variable holds the tuned
/// frequency, and the panel's two knobs are step requests BlueBird consumes within a frame, so the app
/// keeps its own count of the panel, from the state a flight loads in (PARK, fraction .10), and turns the
/// knobs one click per write, each after a fresh read and 250 ms apart. The knob model is round 1's
/// measurement (2026-10-26 on the ground; docs/b757.md, "The ILS control panel"), copied here as data; the
/// step timing is round 4's of the same day ([B757-59]). Task 6b reads the panel's
/// display (round 5): a set reads it, clicks and reads again, and says what it shows; the count stands in, and
/// says so, when the display cannot be read, which is how the rig's fake display answers unless a test shows it
/// the panel. CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch a culture leak.
/// </summary>
public class Bbs757IlsPanelTests
{
    private const Bbs757IlsKnob W = Bbs757IlsKnob.Whole;
    private const Bbs757IlsKnob F = Bbs757IlsKnob.Fraction;

    private const string Invalid = "ILS frequency must be 108.10 to 111.95, on an ILS channel.";
    private const string Empty = "Type a frequency first.";

    private const string WholeDown = "-1 (>L:B757_ILS_FREQ_WHOLE_INC)";
    private const string WholeUp = "1 (>L:B757_ILS_FREQ_WHOLE_INC)";
    private const string FractionDown = "-1 (>L:B757_ILS_FREQ_FRAC_INC)";
    private const string FractionUp = "1 (>L:B757_ILS_FREQ_FRAC_INC)";

    /// <summary>The fraction knob's ten channels in hundredths, in the order +1 turns them (measured).</summary>
    private static readonly int[] Hundredths = { 10, 15, 30, 35, 50, 55, 70, 75, 90, 95 };

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

    private static Bbs757IlsDial Dial(double mhz)
    {
        Assert.True(Bbs757IlsPanel.TryDialFor(mhz, out var dial), $"{mhz} is not an ILS channel");
        return dial;
    }

    /// <summary>What the panel's frequency window shows: the frequency with two decimals, or dashes when parked.</summary>
    private static string Shown(Bbs757IlsDial dial)
        => dial.Mhz is double mhz ? mhz.ToString("0.00", CultureInfo.InvariantCulture) : "-----";

    private static string[] Repeat(int times, params string[] cycle)
        => Enumerable.Range(0, times).SelectMany(_ => cycle).ToArray();

    // ---- The display's variable ----------------------------------------------------------------------

    [Theory]
    [InlineData(-1.0, 95.0, true, null, 95)]
    [InlineData(108.1, 95.0, false, 108.10, 95)]
    [InlineData(108.15, 95.0, false, 108.15, 95)]
    [InlineData(111.95, 95.0, false, 111.95, 95)]
    [InlineData(110.5, null, false, 110.50, null)]
    public void Reading_from_the_variable(double frequency, double? course, bool parked, double? mhz, int? shownCourse)
    {
        var reading = Bbs757IlsPanel.ReadingFrom(frequency, course);

        Assert.Equal(new Bbs757IlsReading(parked, mhz, shownCourse), reading);
    }

    [Fact]
    public void A_frequency_stored_as_a_32_bit_float_still_reads()
    {
        Assert.Equal(new Bbs757IlsReading(false, 108.15, 95), Bbs757IlsPanel.ReadingFrom((double)108.15f, 95));
        Assert.Equal(new Bbs757IlsReading(false, 111.95, 95), Bbs757IlsPanel.ReadingFrom((double)111.95f, 95));
        Assert.Equal(new Bbs757IlsReading(false, 110.10, 95), Bbs757IlsPanel.ReadingFrom((double)110.10f, 95));
        Assert.Null(Bbs757IlsPanel.ReadingFrom(110.52, 95));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(null)]
    [InlineData(107.95)]
    [InlineData(112.0)]
    [InlineData(110.52)]
    [InlineData(double.NaN)]
    public void Reading_from_an_absent_variable_is_none(double? frequency)
    {
        Assert.Null(Bbs757IlsPanel.ReadingFrom(frequency, 95));
    }

    // ---- The typed frequency --------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Ils_entry_parsing(string culture)
    {
        InCulture(culture, () =>
        {
            foreach (var (typed, mhz) in new[]
            {
                ("110.50", 110.5), ("110.5", 110.5), ("110,55", 110.55), ("110,5", 110.5), ("110.55", 110.55),
                ("108.10", 108.1), ("108.1", 108.1), ("111.95", 111.95), (" 109.30 ", 109.3), ("110.550", 110.55),
                ("109,90", 109.9),
            })
            {
                Assert.True(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error), $"\"{typed}\" refused: {error}");
                Assert.Equal(mhz, parsed);
                Assert.Equal("", error);
            }

            foreach (string typed in new[]
            {
                "108.00", "108.05", "108.20", "110.45", "110.52", "110.501", "110.555", "107.95", "112.10", "112", "110",
                "108", "1105", "11050", "-110.5", "+110.5", "110.5 MHz", "1.105e2", "abc", "NaN", "Infinity", "110.5.5",
                "1,110.50", "110.50.",
            })
            {
                Assert.False(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error), $"\"{typed}\" accepted");
                Assert.Equal(0, parsed);
                Assert.Equal(Invalid, error);
            }

            foreach (string? typed in new[] { null, "", "   " })
            {
                Assert.False(Bbs757IlsPanel.TryParseIls(typed, out double parsed, out string error));
                Assert.Equal(0, parsed);
                Assert.Equal(Empty, error);
            }
        });
    }

    [Fact]
    public void Every_ils_channel_is_accepted_and_every_other_hundredth_refused()
    {
        int accepted = 0;
        for (int h = 10800; h <= 11199; h++)
        {
            string typed = $"{h / 100}.{h % 100:00}";
            bool channel = Hundredths.Contains(h % 100);

            Assert.Equal(channel, Bbs757IlsPanel.TryParseIls(typed, out double mhz, out _));
            Assert.Equal(channel, Bbs757IlsPanel.TryDialFor(h / 100.0, out var dial));
            if (!channel) continue;
            accepted++;
            Assert.Equal(h / 100.0, mhz);
            Assert.Equal(h / 100.0, dial.Mhz);
            Assert.Equal((h / 100 - 108, Array.IndexOf(Hundredths, h % 100)), (dial.Whole, dial.Fraction));
        }
        Assert.Equal(40, accepted);   // 108.10 to 111.95: four MHz, ten channels each

        foreach (double off in new[] { 107.95, 112.10, 110.52, 110.505, double.NaN, double.PositiveInfinity })
            Assert.False(Bbs757IlsPanel.TryDialFor(off, out _), $"{off} is no channel");
    }

    // ---- The dial and its phrases -----------------------------------------------------------------------

    [Fact]
    public void A_flight_loads_with_the_ils_parked_on_10()
    {
        // The owner's flight was in its starting panel state: PARK, and the first step proved the
        // fraction PARK remembered was .10 (PARK -1 gave 111.10).
        Assert.Equal(new Bbs757IlsDial(4, 0), Bbs757IlsDial.LoadState);
        Assert.True(Bbs757IlsDial.LoadState.IsParked);
        Assert.Null(Bbs757IlsDial.LoadState.Mhz);

        Assert.Equal(110.5, new Bbs757IlsDial(2, 4).Mhz);
        Assert.Equal(108.1, new Bbs757IlsDial(0, 0).Mhz);
        Assert.Equal(111.95, new Bbs757IlsDial(3, 9).Mhz);
        Assert.False(new Bbs757IlsDial(3, 9).IsParked);

        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(5, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Bbs757IlsDial(0, -1));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Phrases_say_what_the_count_says(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("ILS 110.50", Bbs757IlsPanel.Phrase(Dial(110.50)));
            Assert.Equal("ILS 108.10", Bbs757IlsPanel.Phrase(Dial(108.10)));   // two decimals and a dot, whatever the PC's culture
            Assert.Equal("ILS parked", Bbs757IlsPanel.Phrase(Bbs757IlsDial.LoadState));
            Assert.Equal("ILS parked", Bbs757IlsPanel.Phrase(new Bbs757IlsDial(4, 7)));   // whatever fraction PARK remembers
            Assert.Equal("ILS tuning unknown", Bbs757IlsPanel.Phrase(null));

            // N and the Ctrl+N window say the count with its qualifier, so it never sounds like a reading
            // (a read is said bare: ReadPhrase); no count is still "unknown".
            Assert.Equal("ILS 110.50 by count", Bbs757IlsPanel.CountPhrase(Dial(110.50)));
            Assert.Equal("ILS 108.10 by count", Bbs757IlsPanel.CountPhrase(Dial(108.10)));
            Assert.Equal("ILS parked by count", Bbs757IlsPanel.CountPhrase(Bbs757IlsDial.LoadState));
            Assert.Equal("ILS tuning unknown", Bbs757IlsPanel.CountPhrase(null));
            Assert.NotEqual(Bbs757IlsPanel.ReadPhrase(new Bbs757IlsReading(false, 110.50, 95)), Bbs757IlsPanel.CountPhrase(Dial(110.50)));
            Assert.NotEqual(Bbs757IlsPanel.ReadPhrase(new Bbs757IlsReading(true, null, 95)), Bbs757IlsPanel.CountPhrase(Bbs757IlsDial.LoadState));
        });
    }

    [Fact]
    public void A_set_that_stops_part_way_says_how_far_it_got()
    {
        // The clicks sent and the clicks planned; before any click it is the plain refusal.
        Assert.Equal("ILS unavailable", Bbs757IlsPanel.UnavailableAfterPhrase(0, 6));
        Assert.Equal("ILS unavailable after 3 of 5 clicks.", Bbs757IlsPanel.UnavailableAfterPhrase(3, 5));
        Assert.Equal("ILS unavailable after 6 of 6 clicks.", Bbs757IlsPanel.UnavailableAfterPhrase(6, 6));
        Assert.Equal("ILS unavailable after 1 of 1 click.", Bbs757IlsPanel.UnavailableAfterPhrase(1, 1));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_failed_read_after_the_learning_click_says_the_ils_left_park(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("ILS display could not be read: the ILS left PARK, now 108.75 by count.",
                Bbs757IlsPanel.LeftParkUnreadPhrase(Dial(108.75)));
            Assert.Equal("ILS display could not be read: the ILS left PARK.", Bbs757IlsPanel.LeftParkUnreadPhrase(null));
        });
    }

    // ---- The knob model -----------------------------------------------------------------------------------

    /// <summary>
    /// Round 1 (2026-10-26 on the ground, 11:40-11:49), in the order the knobs were turned
    /// from the panel as the flight loaded (PARK, .10): each row's knob and direction, and what the panel's
    /// frequency window showed after each of its clicks.
    /// </summary>
    private static readonly (Bbs757IlsKnob Knob, int Direction, string[] Shown)[] RoundOne =
    {
        (W, -1, Repeat(4, "111.10", "110.10", "109.10", "108.10", "-----")),   // WHOLE -1 x20 from PARK: a ring with PARK in it
        (W, +1, new[] { "108.10", "109.10", "110.10", "111.10", "-----", "108.10" }),   // WHOLE +1 x6
        (F, +1, Repeat(2, "108.15", "108.30", "108.35", "108.50", "108.55",
                          "108.70", "108.75", "108.90", "108.95", "108.10")),           // FRAC +1 x20: ILS channels only, no carry
        (F, -1, new[] { "108.95", "108.90", "108.75" }),                                // FRAC -1 x3
        (W, +1, new[] { "109.75", "110.75", "111.75", "-----" }),                       // WHOLE +1 x4 keeps the fraction
        (F, +1, new[] { "-----" }),                                                     // FRAC +1 at PARK: nothing
        (W, +1, new[] { "108.75" }),                                                    // PARK remembered .75
        (F, -1, new[] { "108.70" }),
        (F, +1, new[] { "108.75", "108.90", "108.95", "108.10" }),                      // the restore: FRAC +1 x4,
        (W, -1, new[] { "-----", "111.10" }),                                           // WHOLE -1, then -1 and +1 to prove it
        (W, +1, new[] { "-----" }),
    };

    [Fact]
    public void Dial_model_matches_the_measurement()
    {
        var dial = Bbs757IlsDial.LoadState;
        int clicks = 0;
        foreach (var (knob, direction, shown) in RoundOne)
        {
            foreach (string expected in shown)
            {
                dial = Bbs757IlsPanel.Step(dial, knob, direction);
                clicks++;
                Assert.True(expected == Shown(dial),
                    $"click {clicks} ({knob} {(direction > 0 ? "+1" : "-1")}): the panel showed {expected}, the model {Shown(dial)}");
            }
        }

        Assert.Equal(63, clicks);
        Assert.Equal(Bbs757IlsDial.LoadState, dial);   // round 1 ended as it began: PARK with .10
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]    // a write of 2 moved one channel (measured): one write is one click, so a click is +1 or -1
    [InlineData(-2)]
    public void A_click_is_one_step_either_way(int direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757IlsPanel.Step(Bbs757IlsDial.LoadState, W, direction));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757IlsPanel.StepCode(F, direction));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]   // .NET writes -1 as "−1" (U+2212) under sv-SE: the RPN parser would not read it
    public void A_click_is_one_calculator_write_with_no_culture_in_it(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal(WholeDown, Bbs757IlsPanel.StepCode(W, -1));
            Assert.Equal(WholeUp, Bbs757IlsPanel.StepCode(W, +1));
            Assert.Equal(FractionDown, Bbs757IlsPanel.StepCode(F, -1));
            Assert.Equal(FractionUp, Bbs757IlsPanel.StepCode(F, +1));
        });
        Assert.Equal("B757_ILS_FREQ_WHOLE_INC", Bbs757IlsPanel.WholeVar);
        Assert.Equal("B757_ILS_FREQ_FRAC_INC", Bbs757IlsPanel.FractionVar);
    }

    // ---- The plan -----------------------------------------------------------------------------------------

    /// <summary>Every state the panel can be in: the 40 channels, and PARK with each of the ten fractions it may remember.</summary>
    private static IEnumerable<Bbs757IlsDial> EveryState()
        => from whole in Enumerable.Range(0, 5) from fraction in Enumerable.Range(0, 10) select new Bbs757IlsDial(whole, fraction);

    /// <summary>Every target: the 40 channels, and PARK (null).</summary>
    private static IEnumerable<Bbs757IlsDial?> EveryTarget()
        => EveryState().Where(d => !d.IsParked).Select(d => (Bbs757IlsDial?)d).Append(null);

    /// <summary>
    /// The fewest clicks from <paramref name="from"/> to <paramref name="to"/> (null: PARK) by the knob model
    /// (<see cref="Bbs757IlsPanel.Step"/>, checked against the measurement above), never passing PARK on the
    /// way: a breadth-first search over the panel's 50 states.
    /// </summary>
    private static int FewestClicks(Bbs757IlsDial from, Bbs757IlsDial? to)
    {
        bool Arrived(Bbs757IlsDial d) => to is Bbs757IlsDial target ? d == target : d.IsParked;
        var seen = new HashSet<Bbs757IlsDial> { from };
        var frontier = new List<Bbs757IlsDial> { from };
        for (int clicks = 0; frontier.Count > 0; clicks++)
        {
            if (frontier.Any(Arrived)) return clicks;
            var next = new List<Bbs757IlsDial>();
            foreach (var d in frontier)
                foreach (var knob in new[] { W, F })
                    foreach (int direction in new[] { +1, -1 })
                    {
                        var n = Bbs757IlsPanel.Step(d, knob, direction);
                        if (n.IsParked && !Arrived(n)) continue;   // never through PARK
                        if (seen.Add(n)) next.Add(n);
                    }
            frontier = next;
        }
        throw new InvalidOperationException("no way there");
    }

    [Fact]
    public void Plan_reaches_every_target()
    {
        int pairs = 0;
        foreach (var from in EveryState())
        {
            foreach (var to in EveryTarget())
            {
                var plan = Bbs757IlsPanel.PlanSteps(from, to);
                var path = new List<Bbs757IlsDial> { from };
                foreach (var (knob, direction) in plan) path.Add(Bbs757IlsPanel.Step(path[^1], knob, direction));
                var reached = path[^1];
                string pair = $"{Shown(from)} (fraction {from.Fraction}) to {(to is Bbs757IlsDial t ? Shown(t) : "PARK")}";

                // It gets there...
                Assert.True(to is Bbs757IlsDial target ? reached == target : reached.IsParked, $"{pair}: reached {Shown(reached)}");
                // ...with the ILS parked only where it starts or ends, never on the way...
                Assert.True(path.Skip(1).SkipLast(1).All(d => !d.IsParked), $"{pair}: passes PARK");
                // ...in the fewest clicks that never pass PARK...
                Assert.True(FewestClicks(from, to) == plan.Count, $"{pair}: {plan.Count} clicks, {FewestClicks(from, to)} would do");
                // ...in the stated order: to PARK the whole knob only; out of PARK the whole knob first (the
                // fraction knob does nothing while parked); between two frequencies the fraction first.
                var knobs = plan.Select(s => s.Knob).ToList();
                if (to is null) Assert.All(knobs, k => Assert.Equal(W, k));
                else if (from.IsParked) Assert.True(knobs.SkipWhile(k => k == W).All(k => k == F), $"{pair}: {string.Join(",", knobs)}");
                else Assert.True(knobs.SkipWhile(k => k == F).All(k => k == W), $"{pair}: {string.Join(",", knobs)}");
                pairs++;
            }
        }

        // The 41 x 41 pairs (PARK as a flight loads it among them), and PARK with its other nine fractions too.
        Assert.Equal(50 * 41, pairs);
    }

    [Fact]
    public void Between_108_and_111_the_whole_knob_never_passes_park()
    {
        // The way round through PARK is one click shorter here (two against three), and the only place it
        // is: one click is not worth parking the ILS on the way.
        Assert.Equal(new[] { (F, +1), (W, +1), (W, +1), (W, +1) }, Bbs757IlsPanel.PlanSteps(Dial(108.10), Dial(111.15)));
        Assert.Equal(new[] { (W, -1), (W, -1), (W, -1) }, Bbs757IlsPanel.PlanSteps(Dial(111.50), Dial(108.50)));
    }

    [Fact]
    public void Out_of_park_the_whole_knob_turns_first_and_the_load_state_to_110_50_is_what_round_3_turned()
    {
        // Round 3 (2026-10-26, the FMC as an ILS comparator, on the ground) tuned 110.50 by hand from
        // the load state: WHOLE -1 x2, FRAC +1 x4.
        Assert.Equal(new[] { (W, -1), (W, -1), (F, +1), (F, +1), (F, +1), (F, +1) },
            Bbs757IlsPanel.PlanSteps(Bbs757IlsDial.LoadState, Dial(110.50)));
        // A tie round the fraction ring (five either way) goes up.
        Assert.Equal(new[] { (W, +1), (F, +1), (F, +1), (F, +1), (F, +1), (F, +1) },
            Bbs757IlsPanel.PlanSteps(Bbs757IlsDial.LoadState, Dial(108.55)));
        // Already there: no click.
        Assert.Empty(Bbs757IlsPanel.PlanSteps(Dial(110.50), Dial(110.50)));
    }

    [Theory]
    [InlineData(108.10, -1, 1)]   // 108's -1 is PARK
    [InlineData(109.50, -1, 2)]   // 109, 108, PARK
    [InlineData(110.50, +1, 2)]   // 110, 111, PARK: how round 3 parked 110.50 by hand (WHOLE +1 x2)
    [InlineData(111.95, +1, 1)]   // 111's +1 is PARK
    public void Park_steps(double mhz, int direction, int clicks)
    {
        var from = Dial(mhz);

        var plan = Bbs757IlsPanel.PlanSteps(from, null);

        // The whole knob only, the shorter way round its ring of five...
        Assert.Equal(Enumerable.Repeat((W, direction), clicks), plan);
        Assert.All(plan, s => Assert.Equal(direction > 0 ? WholeUp : WholeDown, Bbs757IlsPanel.StepCode(s.Knob, s.Direction)));
        // ...and PARK remembers the fraction.
        var parked = plan.Aggregate(from, (d, s) => Bbs757IlsPanel.Step(d, s.Knob, s.Direction));
        Assert.True(parked.IsParked);
        Assert.Equal(from.Fraction, parked.Fraction);
        // A parked panel as the target is PARK too, whatever fraction it names.
        Assert.Equal(plan, Bbs757IlsPanel.PlanSteps(from, Bbs757IlsDial.LoadState));
        // Parked already, with any fraction: no click.
        Assert.All(Enumerable.Range(0, 10), f => Assert.Empty(Bbs757IlsPanel.PlanSteps(new Bbs757IlsDial(4, f), null)));
    }

    // ==== The definition: the count and the set ===========================================================

    /// <summary>The fresh read made before every click: the ILS course, the variable the course set reads back.</summary>
    private const string ReadBefore = "read BBS757_ILS_COURSE";

    /// <summary>A read of the ILS panel's display (task 6b), in the sim's log.</summary>
    private const string ReadDisplay = "read display";

    /// <summary>A set or park refused: no count, and the display could not be read.</summary>
    private const string UnknownRefusal = "ILS tuning unknown: the ILS display could not be read.";

    /// <summary>What a set says when it went by the count alone (the display could not be read).</summary>
    private static string ByCount(string phrase) => $"{phrase} by count: the ILS display could not be read.";

    /// <summary>What the panel's display shows for a dial (the course window as 095).</summary>
    private static Bbs757IlsReading ReadingOf(Bbs757IlsDial panel)
        => panel.Mhz is double mhz ? new Bbs757IlsReading(false, mhz, 95) : new Bbs757IlsReading(true, null, 95);

    /// <summary>The click a calculator write makes, or null for any other write.</summary>
    private static (Bbs757IlsKnob Knob, int Direction)? ClickOf(string rpn) => rpn switch
    {
        WholeUp => (W, +1),
        WholeDown => (W, -1),
        FractionUp => (F, +1),
        FractionDown => (F, -1),
        _ => null,
    };

    /// <summary>
    /// The sim behind the definition's ILS set, as the set sees it: whether a write can land, the answer to a
    /// fresh read, a clock the waits move, and, in order, every read, wait and write it makes.
    /// </summary>
    private sealed class PanelSim
    {
        public bool CanWrite = true;
        public long Now = 1_000_000;

        /// <summary>What a fresh read answers (null: the sim did not answer it in time).</summary>
        public double? ReadAnswer = 95;

        public readonly List<string> Writes = new();
        public readonly List<string> Log = new();

        /// <summary>Runs at each wait, with the wait's number (1 for the first), before the wait ends.</summary>
        public Action<int>? OnWait;

        /// <summary>Runs at each write, with the write's number (1 for the first), before it is recorded.</summary>
        public Action<int>? OnWrite;

        /// <summary>The ILS control panel itself, when a test gives one: each click written turns it by the knob model.</summary>
        public Bbs757IlsDial? Panel;

        /// <summary>Whether the click of this write's number (1 for the first) goes nowhere on the panel.</summary>
        public Func<int, bool>? LoseClick;

        private int _waits;

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, ct) =>
            {
                Log.Add($"read {key}");
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(ReadAnswer);
            },
            Cached = key => throw new InvalidOperationException($"the ILS set read the cache of {key}"),
            Write = rpn =>
            {
                OnWrite?.Invoke(Writes.Count + 1);
                Writes.Add(rpn);
                Log.Add(rpn);
                if (Panel is Bbs757IlsDial panel && ClickOf(rpn) is (var knob, var direction) && LoseClick?.Invoke(Writes.Count) != true)
                    Panel = Bbs757IlsPanel.Step(panel, knob, direction);
            },
            RequestRead = key => throw new InvalidOperationException($"the ILS set re-read {key}"),
            Delay = (ms, ct) =>
            {
                Log.Add($"wait {ms}");
                Now += ms;
                OnWait?.Invoke(++_waits);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// A definition whose writes go to <see cref="Sim"/> on its clock and whose ILS display is <see cref="Display"/>
    /// (unreadable unless a test shows it the panel: 6a's tests are the count's), and what it said and gave the
    /// window's status box.
    /// </summary>
    private sealed class Rig
    {
        public readonly PanelSim Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly List<string> Status = new();
        public readonly FakeIlsDisplay Display;
        public readonly BBS757Definition Def;

        public Rig()
        {
            Display = new FakeIlsDisplay(Sim.Log);
            Def = new BBS757Definition
            {
                NestRunning = () => true, WriteIo = _ => Sim.Io, NowMs = () => Sim.Now, IlsDisplayFor = _ => Display,
            };
        }

        /// <summary>The panel is <paramref name="panel"/>, the clicks turn it, and the display shows it.</summary>
        public void ShowPanel(Bbs757IlsDial panel)
        {
            Sim.Panel = panel;
            Display.Answer = () => ReadingOf(Sim.Panel!.Value);
        }

        /// <summary>A set from the window: a frequency, or PARK (null).</summary>
        public Task Set(double? mhz) => Def.SetIlsAsync(mhz, new SimConnectManager(IntPtr.Zero), Speech, Status.Add);

        /// <summary>MainForm's AircraftLoaded: a flight is loading, and BlueBird's panel may not run yet.</summary>
        public void LoadBegins() => Def.OnSimContextReset();

        /// <summary>A COM row delivered (the 1 Hz batch): the flight runs.</summary>
        public void ComDelivered() => Def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());

        /// <summary>A flight load, then its first COM delivery.</summary>
        public void FlightLoad()
        {
            LoadBegins();
            ComDelivered();
        }

        /// <summary>A dropped connection: MainForm.OnConnectionLost calls both, in this order.</summary>
        public void ConnectionDrop()
        {
            Def.OnSimContextReset();
            Def.OnVariableCacheCleared();
        }
    }

    /// <summary>The clicks from the load state to 110.50: WHOLE -1 x2, FRAC +1 x4 (as round 3 turned them by hand).</summary>
    private static readonly string[] LoadStateTo11050 = { WholeDown, WholeDown, FractionUp, FractionUp, FractionUp, FractionUp };

    [Fact]
    public async Task Count_after_a_flight_load_and_unknown_after_a_dropped_connection()
    {
        var rig = new Rig();

        // A new definition (MSFSBA started, or the 757 profile picked, after the flight loaded): the app
        // has not seen the panel as it loaded.
        Assert.Null(rig.Def.IlsDial);

        rig.LoadBegins();
        Assert.Null(rig.Def.IlsDial);   // loading: BlueBird's panel may not run yet
        rig.ComDelivered();
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);   // the flight runs: the panel as it loads
        rig.ComDelivered();
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);

        await rig.Set(110.50);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);   // the count follows the clicks sent
        rig.ComDelivered();
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);   // a later COM delivery is no flight load

        rig.FlightLoad();   // the next flight's panel loads parked again
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);

        await rig.Set(109.30);
        rig.ConnectionDrop();   // whatever happens to the panel until the app is back is unseen
        Assert.Null(rig.Def.IlsDial);
        rig.ComDelivered();     // the reconnect's re-fire: still no flight load seen
        Assert.Null(rig.Def.IlsDial);

        rig.FlightLoad();   // reconnected, and a flight loaded: counted again
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_set_between_a_flight_load_and_the_first_com_delivery_is_refused()
    {
        // AircraftLoaded fires during the loading screen, before BlueBird's panel runs: a click then could be
        // lost while the count moved. The count starts once the flight runs (a COM row delivered).
        var rig = new Rig();
        rig.LoadBegins();

        await rig.Set(110.50);

        Assert.Equal(new[] { UnknownRefusal }, rig.Speech.Interrupts);
        Assert.Equal(new[] { ReadDisplay }, rig.Sim.Log);   // the display tried: nothing written, nothing waited

        rig.ComDelivered();
        await rig.Set(110.50);

        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);
        Assert.Equal(ByCount("ILS 110.50 set"), rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task Each_click_follows_a_fresh_read_and_back_to_back_sets_keep_the_gap()
    {
        // A wall-clock gap alone cannot keep two clicks in separate sim frames across a stall: each click
        // waits for a fresh read (answered by the sim in a frame after the last click went out), then the gap.
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(108.10);   // PARK to 108: one click
        await rig.Set(null);     // at once: its first click waits out the gap since the last one
        rig.Sim.Now += 100;
        await rig.Set(108.10);   // 100 ms later: the rest of the gap
        rig.Sim.Now += 400;
        await rig.Set(null);     // 400 ms later: no wait

        // Each set first tries the display (unreadable here: the count's clicks follow).
        Assert.Equal(new[]
        {
            ReadDisplay, ReadBefore, WholeUp,
            ReadDisplay, ReadBefore, "wait 250", WholeDown,
            ReadDisplay, ReadBefore, "wait 150", WholeUp,
            ReadDisplay, ReadBefore, WholeDown,
        }, rig.Sim.Log);
    }

    [Fact]
    public async Task A_click_whose_read_the_sim_does_not_answer_is_not_sent()
    {
        // No answer: nothing shows the sim moved on from the last click, so the next could share its frame.
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWrite = n =>
        {
            if (n == 1) rig.Sim.ReadAnswer = null;   // the sim stops answering after the first click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { ReadDisplay, ReadBefore, WholeDown, ReadBefore }, rig.Sim.Log);
        Assert.Equal(new[] { WholeDown }, rig.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable after 1 of 6 clicks." }, rig.Speech.Interrupts);   // how far it got
        Assert.Equal(rig.Speech.Interrupts, rig.Status);
        Assert.Equal(new Bbs757IlsDial(3, 0), rig.Def.IlsDial);   // the click sent is counted: 111.10

        // Not answered before the first click: nothing went out, the plain words.
        var silent = new Rig();
        silent.FlightLoad();
        silent.Sim.ReadAnswer = null;
        await silent.Set(110.50);
        Assert.Empty(silent.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable" }, silent.Speech.Interrupts);

        // A set of two plans (the learning click out of PARK, then five): the clicks of both.
        var learning = new Rig();
        learning.ShowPanel(new Bbs757IlsDial(Bbs757IlsDial.Park, 7));   // PARK remembering .75, no count
        learning.Sim.OnWrite = n =>
        {
            if (n == 2) learning.Sim.ReadAnswer = null;   // the sim stops answering after the second click
        };
        await learning.Set(110.50);
        Assert.Equal(new[] { WholeUp, FractionDown }, learning.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable after 2 of 6 clicks." }, learning.Speech.Interrupts);
    }

    [Theory]
    [InlineData("flight load")]
    [InlineData("dropped connection")]
    public async Task A_set_stopped_by_a_flight_load_or_a_drop_shows_the_count_without_saying_it(string reset)
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n != 2) return;
            if (reset == "flight load") rig.LoadBegins();
            else rig.ConnectionDrop();
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);                                // nothing said
        Assert.Equal(new[] { "ILS tuning unknown" }, rig.Status);    // the window no longer says it is setting
    }

    [Fact]
    public async Task Set_refused_while_unknown()
    {
        var rig = new Rig();

        await rig.Set(110.50);
        await rig.Set(null);

        Assert.Equal(new[] { UnknownRefusal, UnknownRefusal }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(new[] { UnknownRefusal, UnknownRefusal }, rig.Status);   // the window's status says it too
        Assert.Equal(new[] { ReadDisplay, ReadDisplay }, rig.Sim.Log);        // the display tried: nothing written or waited
        Assert.Null(rig.Def.IlsDial);
    }

    [Fact]
    public async Task Steps_are_sent_250_ms_apart()
    {
        // BlueBird takes a click within a frame, and two landing in one frame count once (20 ms apart lost
        // steps; 50 and 150 ms none; a 192 ms frame was seen): one write per click, 250 ms between them.
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(110.50);

        // Each click after a fresh read; from the second on, the gap after it. (The display, tried first,
        // could not be read: the clicks are the count's.)
        Assert.Equal(new[]
        {
            ReadDisplay,
            ReadBefore, WholeDown,
            ReadBefore, "wait 250", WholeDown,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
            ReadBefore, "wait 250", FractionUp,
        }, rig.Sim.Log);
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);
        Assert.Equal(250, BBS757Definition.IlsStepGapMs);
        // Said once, from the count, and saying so: never "tuned".
        Assert.Equal(new[] { ByCount("ILS 110.50 set") }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(new[] { ByCount("ILS 110.50 set") }, rig.Status);
    }

    [Fact]
    public async Task Park_is_the_whole_knob_and_says_parked()
    {
        var rig = new Rig();
        rig.FlightLoad();
        await rig.Set(110.50);
        rig.Sim.Log.Clear();
        rig.Speech.Interrupts.Clear();

        await rig.Set(null);

        // 110, 111, PARK; pressed right after the set, so its first click waits out the gap too.
        Assert.Equal(new[] { ReadDisplay, ReadBefore, "wait 250", WholeUp, ReadBefore, "wait 250", WholeUp }, rig.Sim.Log);
        Assert.Equal(new[] { ByCount("ILS parked") }, rig.Speech.Interrupts);
        Assert.Equal(new Bbs757IlsDial(4, 4), rig.Def.IlsDial);              // PARK, remembering .50
    }

    [Fact]
    public async Task Already_there_sends_nothing()
    {
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(null);   // parked as the flight loaded
        await rig.Set(110.50);
        rig.Sim.Writes.Clear();
        await rig.Set(110.50);

        Assert.Empty(rig.Sim.Writes);
        Assert.Equal(new[] { ByCount("ILS already parked"), ByCount("ILS 110.50 set"), ByCount("ILS already 110.50") },
            rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Status);
    }

    [Fact]
    public async Task A_set_that_cannot_land_is_refused_aloud_and_nothing_is_written()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.CanWrite = false;

        await rig.Set(110.50);
        await rig.Set(null);

        Assert.Equal(new[] { "ILS unavailable", "ILS unavailable" }, rig.Speech.Interrupts);
        Assert.Equal(rig.Speech.Interrupts, rig.Status);
        Assert.Empty(rig.Sim.Log);
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_frequency_that_is_no_channel_is_refused_aloud()
    {
        var rig = new Rig();
        rig.FlightLoad();

        await rig.Set(110.52);
        await rig.Set(118.0);

        Assert.Equal(new[] { Invalid, Invalid }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Log);
    }

    [Fact]
    public async Task A_set_while_another_turns_the_knobs_is_refused()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 1) _ = rig.Set(null);   // the park button, pressed between the first two clicks
        };

        await rig.Set(110.50);

        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);   // the park sent nothing
        Assert.Equal(new[] { "ILS tuning in progress", ByCount("ILS 110.50 set") }, rig.Speech.Interrupts);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);

        await rig.Set(null);   // finished: the next set runs
        Assert.Equal(ByCount("ILS parked"), rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task A_flight_load_stops_a_set_between_clicks_and_none_turns_the_next_flights_knobs()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 2) rig.LoadBegins();   // after the second click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
        Assert.Null(rig.Def.IlsDial);   // loading

        rig.ComDelivered();             // the new flight runs: its panel as it loads
        Assert.Equal(Bbs757IlsDial.LoadState, rig.Def.IlsDial);
        rig.Sim.OnWait = null;
        await rig.Set(110.50);          // counted from there
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes.Skip(2));
    }

    [Fact]
    public async Task A_profile_switch_stops_a_set_between_clicks_and_the_count_keeps_the_clicks_sent()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWait = n =>
        {
            if (n == 3) rig.Def.OnSwitchedAway();   // after the third click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown, FractionUp }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);   // nothing said into the next aircraft
        Assert.Empty(rig.Status);       // nor shown: the window is closed
        Assert.Equal(Dial(110.15), rig.Def.IlsDial);
    }

    [Fact]
    public async Task A_click_that_throws_says_unavailable_and_leaves_the_count_unknown()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Sim.OnWrite = n =>
        {
            if (n == 3) throw new InvalidOperationException("no link");
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeDown, WholeDown }, rig.Sim.Writes);
        Assert.Equal(new[] { "ILS unavailable after 2 of 6 clicks." }, rig.Speech.Interrupts);   // the two known sent
        Assert.Null(rig.Def.IlsDial);   // whether the third click went out cannot be known

        rig.Sim.OnWrite = null;
        await rig.Set(110.50);           // and a set from an unknown count is refused
        Assert.Equal(UnknownRefusal, rig.Speech.Interrupts[^1]);
    }

    [Fact]
    public async Task A_set_after_a_switch_away_does_nothing()
    {
        var rig = new Rig();
        rig.FlightLoad();
        rig.Def.OnSwitchedAway();

        await rig.Set(110.50);
        await rig.Set(null);

        Assert.Empty(rig.Sim.Log);
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_set_writes_and_says_no_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var rig = new Rig();
            rig.FlightLoad();

            await rig.Set(110.50);
            await rig.Set(110.50);

            Assert.Equal(LoadStateTo11050, rig.Sim.Writes);   // "-1", never "−1"
            Assert.Equal(new[] { ByCount("ILS 110.50 set"), ByCount("ILS already 110.50") }, rig.Speech.Interrupts);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ==== Reading the panel's display (task 6b) =============================================================

    [Fact]
    public async Task Set_reads_clicks_and_verifies()
    {
        // MSFSBA started over a running flight: no count. The display says where the panel is, the clicks
        // are planned from that, and the display read after them is what is said.
        var rig = new Rig();
        rig.ShowPanel(Dial(109.30));

        await rig.Set(110.50);

        Assert.Equal(new[]
        {
            ReadDisplay,                           // 109.30
            ReadBefore, FractionUp,                // .35
            ReadBefore, "wait 250", FractionUp,    // .50
            ReadBefore, "wait 250", WholeUp,       // 110.50
            ReadBefore, "wait 250", ReadDisplay,   // the panel catches up (a fresh read, the gap), then shows the target
        }, rig.Sim.Log);
        Assert.Equal(new[] { "ILS 110.50 set" }, rig.Speech.Interrupts);   // said once, from the read
        Assert.Equal(rig.Speech.Interrupts, rig.Speech.All);
        Assert.Equal(new[] { "ILS 110.50 set" }, rig.Status);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);   // the read is the count now

        // Already there by the read: nothing turned.
        rig.Sim.Log.Clear();
        await rig.Set(110.50);
        Assert.Equal(new[] { ReadDisplay }, rig.Sim.Log);
        Assert.Equal("ILS already 110.50", rig.Speech.Interrupts[^1]);

        // PARK the same way: the whole knob, then the read.
        rig.Sim.Log.Clear();
        rig.Sim.Now += 1000;
        await rig.Set(null);
        Assert.Equal(new[] { ReadDisplay, ReadBefore, WholeUp, ReadBefore, "wait 250", WholeUp, ReadBefore, "wait 250", ReadDisplay },
            rig.Sim.Log);
        Assert.Equal("ILS parked", rig.Speech.Interrupts[^1]);
        Assert.Equal(new Bbs757IlsDial(Bbs757IlsDial.Park, 4), rig.Def.IlsDial);   // PARK keeps the fraction the count had (.50)
    }

    [Fact]
    public async Task Set_from_park_learns_the_fraction()
    {
        // Parked, the display shows "-----": the fraction PARK remembers is hidden, and the fraction knob does
        // nothing while parked. With no count to know it, the whole knob takes the panel out of PARK once (+1
        // gives 108.xx) and the display shows the fraction.
        var rig = new Rig();
        rig.ShowPanel(new Bbs757IlsDial(Bbs757IlsDial.Park, 7));   // PARK, remembering .75

        await rig.Set(null);   // PARK wanted: the read is enough
        Assert.Equal(new[] { ReadDisplay }, rig.Sim.Log);
        Assert.Equal(new[] { "ILS already parked" }, rig.Speech.Interrupts);
        Assert.Null(rig.Def.IlsDial);   // parked, fraction unknown: still no count

        rig.Sim.Log.Clear();
        await rig.Set(110.50);

        Assert.Equal(new[]
        {
            ReadDisplay,                           // -----
            ReadBefore, WholeUp,                   // out of PARK
            ReadBefore, "wait 250", ReadDisplay,   // the panel catches up, then 108.75: the fraction
            ReadBefore, FractionDown,              // .70 (the gap since the last click is already waited)
            ReadBefore, "wait 250", FractionDown,  // .55
            ReadBefore, "wait 250", FractionDown,  // .50
            ReadBefore, "wait 250", WholeUp,       // 109.50
            ReadBefore, "wait 250", WholeUp,       // 110.50
            ReadBefore, "wait 250", ReadDisplay,
        }, rig.Sim.Log);
        Assert.Equal("ILS 110.50 set", rig.Speech.Interrupts[^1]);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);

        // With the count knowing the fraction (a flight load: PARK, .10), the read of PARK needs no learning click.
        var loaded = new Rig();
        loaded.FlightLoad();
        loaded.ShowPanel(Bbs757IlsDial.LoadState);
        await loaded.Set(110.50);
        Assert.Equal(LoadStateTo11050, loaded.Sim.Writes);
        Assert.Equal(new[] { "ILS 110.50 set" }, loaded.Speech.Interrupts);
        Assert.Equal(2, loaded.Display.Reads);
    }

    [Fact]
    public async Task A_display_lost_after_the_learning_click_says_the_ils_left_park()
    {
        // The learning click took the panel out of PARK (108 and the hidden fraction), then the display could
        // not be read: the pilot hears that the ILS is no longer parked, not the plain "could not be read".
        var rig = new Rig();
        rig.ShowPanel(new Bbs757IlsDial(Bbs757IlsDial.Park, 7));   // PARK, remembering .75; no count
        rig.Sim.OnWrite = n =>
        {
            if (n == 1) rig.Display.Answer = () => null;   // the display goes with the learning click
        };

        await rig.Set(110.50);

        Assert.Equal(new[] { WholeUp }, rig.Sim.Writes);   // out of PARK, nothing more
        const string said = "ILS display could not be read: the ILS left PARK.";   // the count never knew the fraction
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);
        Assert.Equal(new[] { said }, rig.Status);
        Assert.Null(rig.Def.IlsDial);
        Assert.Equal(new[] { ReadDisplay, ReadBefore, WholeUp, ReadBefore, "wait 250", ReadDisplay }, rig.Sim.Log);
    }

    [Fact]
    public async Task Set_retries_once_then_reports()
    {
        // A click the panel lost: the read after the clicks shows where it is, and the set plans once more from there.
        var rig = new Rig();
        rig.ShowPanel(Dial(110.10));
        rig.Sim.LoseClick = n => n == 2;

        await rig.Set(110.50);

        // .15 .30 (lost) .35 .50 counted; the panel shows 110.35, one more click. Each check read waits for the
        // panel to catch up: after the first plan's clicks and after the retry's.
        Assert.Equal(new[]
        {
            ReadDisplay,                           // 110.10
            ReadBefore, FractionUp,                // .15
            ReadBefore, "wait 250", FractionUp,    // lost
            ReadBefore, "wait 250", FractionUp,    // .30
            ReadBefore, "wait 250", FractionUp,    // .35
            ReadBefore, "wait 250", ReadDisplay,   // shows 110.35
            ReadBefore, FractionUp,                // .50, the retry (the gap already waited)
            ReadBefore, "wait 250", ReadDisplay,   // shows 110.50
        }, rig.Sim.Log);
        Assert.Equal(Enumerable.Repeat(FractionUp, 5), rig.Sim.Writes);
        Assert.Equal(3, rig.Display.Reads);
        Assert.Equal(new[] { "ILS 110.50 set" }, rig.Speech.Interrupts);
        Assert.Equal(Dial(110.50), rig.Def.IlsDial);

        // A knob that turns nothing: after the second try the set says what the panel shows, never "set".
        var stuck = new Rig();
        stuck.ShowPanel(Dial(110.55));
        stuck.Sim.LoseClick = _ => true;

        await stuck.Set(110.50);

        Assert.Equal(new[] { FractionDown, FractionDown }, stuck.Sim.Writes);   // the plan, then the plan once more
        Assert.Equal(3, stuck.Display.Reads);
        Assert.Equal(new[] { "ILS shows 110.55, not 110.50" }, stuck.Speech.Interrupts);
        Assert.Equal(new[] { "ILS shows 110.55, not 110.50" }, stuck.Status);
        Assert.Equal(Dial(110.55), stuck.Def.IlsDial);   // the count is what the panel shows
    }

    [Fact]
    public async Task Unreadable_display_falls_back_to_the_count()
    {
        var rig = new Rig();   // its display cannot be read

        // No count either: nothing to go on, nothing turned.
        await rig.Set(110.50);
        Assert.Equal(new[] { UnknownRefusal }, rig.Speech.Interrupts);
        Assert.Empty(rig.Sim.Writes);

        // A flight load gives the count: 6a's clicks, and the words say it was by the count, once.
        rig.FlightLoad();
        await rig.Set(110.50);
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);
        Assert.Equal(new[] { UnknownRefusal, "ILS 110.50 set by count: the ILS display could not be read." }, rig.Speech.Interrupts);
        Assert.Equal(2, rig.Display.Reads);   // one per set: a display that failed is not read again after the clicks

        rig.Sim.Now += 1000;
        await rig.Set(null);
        Assert.Equal("ILS parked by count: the ILS display could not be read.", rig.Speech.Interrupts[^1]);

        // Read before the clicks but not after: the set ends on its count, and says so.
        var late = new Rig();
        late.ShowPanel(Dial(109.30));
        late.Sim.OnWrite = n =>
        {
            if (n == 1) late.Display.Answer = () => null;
        };

        await late.Set(110.50);

        Assert.Equal(new[] { FractionUp, FractionUp, WholeUp }, late.Sim.Writes);
        Assert.Equal(new[] { ReadBefore, "wait 250", ReadDisplay }, late.Sim.Log.TakeLast(3));   // the check read was paced too
        Assert.Equal(new[] { "ILS 110.50 set by count: the ILS display could not be read." }, late.Speech.Interrupts);
        Assert.Equal(Dial(110.50), late.Def.IlsDial);

        // The words never depend on the PC's culture.
        var german = new Rig();
        german.FlightLoad();
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            await german.Set(109.35);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
        Assert.Equal("ILS 109.35 set by count: the ILS display could not be read.", german.Speech.Interrupts[^1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task What_the_display_shows_is_said_with_no_culture(string culture)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var stuck = new Rig();
            stuck.ShowPanel(Dial(109.35));
            stuck.Sim.LoseClick = _ => true;

            await stuck.Set(110.50);

            Assert.Equal(new[] { "ILS shows 109.35, not 110.50" }, stuck.Speech.Interrupts);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
