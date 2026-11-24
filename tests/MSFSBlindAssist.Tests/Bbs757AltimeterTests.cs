using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's altimeters, radio minimums and squawk, as far as they run without a sim: the
/// typed input, the stock-event parameters (measured: <c>K:KOHLSMAN_SET</c> takes 16 × hPa and sets
/// all three altimeters, <c>K:INCREASE/DECREASE_DECISION_HEIGHT</c> step 10 ft, <c>K:XPNDR_SET</c>
/// takes BCD), the phrases, and the set loops against a simulated aircraft. CI runs under en-US, so
/// the de-DE and sv-SE cases are the ones that catch a culture leak.
/// </summary>
public class Bbs757AltimeterTests
{
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

    private static int Count(IEnumerable<string> calls, string needle)
        => calls.Sum(c => c.Split(needle).Length - 1);

    // ---- Altimeter: the parameter -----------------------------------------------------------

    [Theory]
    [InlineData(1020.0, 16320u)]   // measured: 16320 set all three to 1020
    [InlineData(1013.0, 16212u)]   // standard, as the Standard button sets it
    [InlineData(1013.25, 16212u)]
    [InlineData(29.92, 16212u)]    // standard: 29.92 inches is 1013.21 hPa, never 16211
    [InlineData(29.916, 16212u)]
    [InlineData(1013.4, 16214u)]   // not standard
    [InlineData(1012.0, 16192u)]
    [InlineData(30.12, 16320u)]
    [InlineData(28.50, 15442u)]
    public void Kohlsman_takes_sixteen_times_the_hectopascals(double typed, uint parameter)
    {
        Assert.Equal(parameter, Bbs757Altimeters.KohlsmanParameter(typed));
    }

    [Fact]
    public void Standard_is_1013_25_hectopascals()
    {
        Assert.Equal(16212u, Bbs757Altimeters.StandardParameter);
        Assert.Equal(1013.25, Bbs757Altimeters.StandardParameter / 16.0);
    }

    [Theory]
    [InlineData("de-DE", "29,92", 16212u)]    // typing standard equals the Standard button
    [InlineData("de-DE", "1013", 16212u)]
    [InlineData("sv-SE", "29.92", 16212u)]
    [InlineData("en-US", "1013.25", 16212u)]
    [InlineData("de-DE", "1012", 16192u)]
    [InlineData("en-US", "30.12", 16320u)]
    [InlineData("en-US", "1020", 16320u)]
    public void A_typed_altimeter_is_read_in_any_culture(string culture, string typed, uint parameter)
    {
        InCulture(culture, () =>
        {
            Assert.True(Bbs757Altimeters.TryAltimeterInput(typed, out uint p, out string? error));
            Assert.Equal(parameter, p);
            Assert.Null(error);
        });
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("500")]
    [InlineData("35")]
    [InlineData("1100")]
    [InlineData("26.5")]
    [InlineData("1,013")]    // a thousands group is not a number the pilot means (CORE-17)
    public void A_typed_altimeter_out_of_range_or_unreadable_is_refused(string typed)
    {
        Assert.False(Bbs757Altimeters.TryAltimeterInput(typed, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void The_kohlsman_write_is_one_stock_event_written_invariant()
    {
        InCulture("de-DE", () =>
            Assert.Equal("16320 (>K:KOHLSMAN_SET)", Bbs757Altimeters.KohlsmanWrite(16320)));
    }

    // ---- Altimeter: the phrase --------------------------------------------------------------

    [Fact]
    public void Three_altimeters_that_agree_are_said_once()
    {
        Assert.Equal("Altimeter: 1020, 30.12", Bbs757Altimeters.AltimetersPhrase(1020, 1020, 1020));
        Assert.Equal("Altimeter standard", Bbs757Altimeters.AltimetersPhrase(1013.25, 1013.25, 1013.25));
        Assert.Equal("Altimeter standard", Bbs757Altimeters.AltimetersPhrase(16211 / 16.0, 16211 / 16.0, 16211 / 16.0));
    }

    [Fact]
    public void Altimeters_that_differ_are_said_one_by_one()
    {
        Assert.Equal("Altimeter 1: 1020, 30.12. Altimeter 2: standard. Altimeter 3: 1020, 30.12",
            Bbs757Altimeters.AltimetersPhrase(1020, 1013.25, 1020));
        Assert.Equal("Altimeter 1: 1020, 30.12. Altimeter 2: unavailable. Altimeter 3: 1020, 30.12",
            Bbs757Altimeters.AltimetersPhrase(1020, null, 1020));
    }

    [Fact]
    public void No_altimeter_read_is_unavailable()
    {
        Assert.Equal("Altimeters unavailable", Bbs757Altimeters.AltimetersPhrase(null, null, null));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_altimeter_phrase_is_invariant(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("Altimeter: 1020, 30.12", Bbs757Altimeters.AltimetersPhrase(1020, 1020, 1020));
            Assert.Equal("Altimeter 1: 1020, 30.12. Altimeter 2: standard. Altimeter 3: 1020, 30.12",
                Bbs757Altimeters.AltimetersPhrase(1020, 1013.25, 1020));
        });
    }

    [Fact]
    public void B_reads_the_altimeters_then_the_radio_minimums()
    {
        Assert.Equal("Altimeter: 1020, 30.12. DH 200", Bbs757Altimeters.Readout(1020, 1020, 1020, 200));
        Assert.Equal("Altimeter standard. DH unavailable", Bbs757Altimeters.Readout(1013.25, 1013.25, 1013.25, null));
    }

    // ---- Altimeter: the set -----------------------------------------------------------------

    [Fact]
    public async Task An_altimeter_set_writes_once_and_speaks_what_the_three_read_once_they_follow()
    {
        var sim = new FakeAircraft { Altimeters = new double?[] { 1013.25, 1013.25, 1013.25 }, AltimeterLagReads = 2 };

        string said = await Bbs757Altimeters.SetAltimetersAsync(sim.Io, 16320, CancellationToken.None);

        Assert.Equal(new[] { "16320 (>K:KOHLSMAN_SET)" }, sim.Writes);
        Assert.Equal("Altimeter: 1020, 30.12", said);
    }

    [Fact]
    public async Task An_altimeter_set_the_aircraft_ignores_says_what_it_reads_after_the_wait()
    {
        var sim = new FakeAircraft { Altimeters = new double?[] { 1013.25, 1013.25, 1013.25 }, AltimetersFollow = false };

        string said = await Bbs757Altimeters.SetAltimetersAsync(sim.Io, 16320, CancellationToken.None);

        Assert.Equal("Altimeter standard", said);
        Assert.True(sim.WaitedMs >= Bbs757Altimeters.AltimeterReadBackMaxMs);
        Assert.Single(sim.Writes);
    }

    // ---- Radio minimums ---------------------------------------------------------------------

    [Fact]
    public void Minimums_step_ten_feet_an_event()
    {
        Assert.Equal(5, Count(Bbs757Altimeters.DhSteps(200, 250), "(>K:INCREASE_DECISION_HEIGHT)"));
        Assert.Equal(5, Count(Bbs757Altimeters.DhSteps(250, 200), "(>K:DECREASE_DECISION_HEIGHT)"));
        Assert.Equal(0, Count(Bbs757Altimeters.DhSteps(250, 200), "INCREASE"));
        Assert.Empty(Bbs757Altimeters.DhSteps(200, 204));      // less than half a step
        Assert.Single(Bbs757Altimeters.DhSteps(200, 206));
        Assert.Empty(Bbs757Altimeters.DhSteps(double.NaN, 200));
    }

    [Fact]
    public void A_far_minimums_set_is_bounded_and_split_into_calls()
    {
        var calls = Bbs757Altimeters.DhSteps(0, 2500);

        Assert.Equal(250, Count(calls, "(>K:INCREASE_DECISION_HEIGHT)"));
        Assert.All(calls, c => Assert.True(c.Length < Bbs757Rpn.MaxCallChars));
        Assert.Equal(Bbs757Altimeters.MaxDhStepsPerRound, Count(Bbs757Altimeters.DhSteps(0, 1e9), "(>K:"));
    }

    [Theory]
    [InlineData("en-US", "200", 200)]
    [InlineData("en-US", "205", 210)]
    [InlineData("en-US", "0", 0)]
    [InlineData("en-US", "2500", 2500)]
    [InlineData("de-DE", "200,0", 200)]
    public void Typed_minimums_are_whole_tens_of_feet(string culture, string typed, int feet)
    {
        InCulture(culture, () =>
        {
            Assert.True(Bbs757Altimeters.TryMinimumsInput(typed, out int ft, out string? error));
            Assert.Equal(feet, ft);
            Assert.Null(error);
        });
    }

    [Theory]
    [InlineData("-10")]
    [InlineData("2600")]
    [InlineData("abc")]
    [InlineData("")]
    public void Typed_minimums_outside_0_to_2500_are_refused(string typed)
    {
        Assert.False(Bbs757Altimeters.TryMinimumsInput(typed, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData(200.0, "DH 200")]
    [InlineData(0.0, "DH 0")]
    [InlineData(null, "DH unavailable")]
    [InlineData(3000.0, "DH unavailable")]   // no sane decision height
    [InlineData(-50.0, "DH unavailable")]
    public void The_decision_height_is_said_as_the_adi_shows_it(double? feet, string phrase)
    {
        Assert.Equal(phrase, Bbs757Altimeters.DhPhrase(feet));
    }

    [Fact]
    public async Task A_minimums_set_steps_the_difference_and_speaks_the_value_read()
    {
        var sim = new FakeAircraft { Dh = 200 };

        string said = await Bbs757Altimeters.SetDecisionHeightAsync(sim.Io, 250, CancellationToken.None);

        Assert.Equal("DH 250", said);
        Assert.Equal(5, Count(sim.Writes, "(>K:INCREASE_DECISION_HEIGHT)"));
    }

    [Fact]
    public async Task A_minimums_set_already_there_sends_nothing()
    {
        var sim = new FakeAircraft { Dh = 200 };

        Assert.Equal("DH 200", await Bbs757Altimeters.SetDecisionHeightAsync(sim.Io, 200, CancellationToken.None));
        Assert.Empty(sim.Writes);
    }

    [Fact]
    public async Task A_minimums_set_corrects_steps_that_were_lost()
    {
        var sim = new FakeAircraft { Dh = 200, DhEventsCountedPerCall = 3 };

        string said = await Bbs757Altimeters.SetDecisionHeightAsync(sim.Io, 300, CancellationToken.None);

        Assert.Equal("DH 300", said);   // 3 + 3 + 3 + 1: three corrections reach it
    }

    [Fact]
    public async Task A_minimums_set_the_aircraft_does_not_take_stops_after_one_round()
    {
        var sim = new FakeAircraft { Dh = 200, DhEventsCountedPerCall = 0 };

        string said = await Bbs757Altimeters.SetDecisionHeightAsync(sim.Io, 300, CancellationToken.None);

        Assert.Equal("DH 200", said);
        Assert.Equal(10, Count(sim.Writes, "(>K:"));   // one round, never chased
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-5000.0)]
    [InlineData(9000.0)]
    public async Task A_minimums_set_from_an_unreadable_value_sends_nothing(double? dh)
    {
        var sim = new FakeAircraft { Dh = dh };

        string said = await Bbs757Altimeters.SetDecisionHeightAsync(sim.Io, 200, CancellationToken.None);

        Assert.Equal("DH unavailable", said);
        Assert.Empty(sim.Writes);
    }

    // ---- Squawk -----------------------------------------------------------------------------

    [Theory]
    [InlineData("4743", "4743")]
    [InlineData("421", "0421")]
    [InlineData(" 7700 ", "7700")]
    [InlineData("0000", "0000")]
    public void A_typed_squawk_text_is_up_to_four_octal_digits(string typed, string code)
    {
        Assert.True(Bbs757Squawk.TryParseText(typed, out string c, out string error));
        Assert.Equal(code, c);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("12a4")]       // letters are a wrong code, not an empty box
    [InlineData("4783")]
    [InlineData("12345")]
    [InlineData("-123")]
    [InlineData("47.4")]
    [InlineData("4 74")]
    [InlineData("４７４３")]   // full-width digits
    public void Squawk_text_that_is_not_octal_digits_gets_the_digits_message(string typed)
    {
        Assert.False(Bbs757Squawk.TryParseText(typed, out _, out string error));
        Assert.Equal(Bbs757Squawk.InvalidMessage, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_squawk_text_says_to_type_one_first(string? typed)
    {
        Assert.False(Bbs757Squawk.TryParseText(typed, out _, out string error));
        Assert.Equal(Bbs757Squawk.EmptyMessage, error);
    }

    [Fact]
    public void The_squawk_goes_out_as_bcd()
    {
        // Measured: XPNDR_SET 0x4743 gave 4743; a decimal 4743 gave 1207.
        Assert.Equal(0x4743, Bbs757Squawk.Bcd("4743"));
        Assert.Equal(0x0421, Bbs757Squawk.Bcd("0421"));
        InCulture("de-DE", () => Assert.Equal("18243 (>K:XPNDR_SET)", Bbs757Squawk.Write("4743")));
    }

    [Fact]
    public void The_transponder_code_reads_back_from_bco16()
    {
        Assert.Equal("4743", Bbs757Squawk.Decode(0x4743));
        Assert.Equal("1200", Bbs757Squawk.Decode(0x1200));
        Assert.Equal("0421", Bbs757Squawk.Decode(0x0421));
    }

    [Fact]
    public void The_squawk_is_spoken_as_one_number()
    {
        Assert.Equal("Squawk 4743", Bbs757Squawk.Confirmation("4743", 0x4743));
    }

    [Fact]
    public void A_squawk_the_transponder_did_not_take_says_what_it_reads()
    {
        Assert.Equal("Squawk 4743 entered, the transponder reads 1200", Bbs757Squawk.Confirmation("4743", 0x1200));
        Assert.Equal("Squawk 4743 sent, the transponder did not report back", Bbs757Squawk.Confirmation("4743", null));
    }

    // ---- A simulated 757 -----------------------------------------------------------------------

    private sealed class FakeAircraft
    {
        public double?[] Altimeters = { 1013.25, 1013.25, 1013.25 };
        public bool AltimetersFollow = true;
        public int AltimeterLagReads;           // reads of altimeter 1 before a KOHLSMAN_SET shows
        public double? Dh = 200;
        public int DhEventsCountedPerCall = int.MaxValue;
        public readonly List<string> Writes = new();
        public int WaitedMs;
        private double? _pendingMb;
        private int _readsSinceSet;

        public McpIo Io => new()
        {
            Read = (key, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                int index = Array.IndexOf(Bbs757Altimeters.KohlsmanKeys.ToArray(), key);
                if (index >= 0)
                {
                    if (index == 0 && _pendingMb is double mb && ++_readsSinceSet > AltimeterLagReads)
                    {
                        for (int i = 0; i < 3; i++) Altimeters[i] = mb;
                        _pendingMb = null;
                    }
                    return Task.FromResult(Altimeters[index]);
                }
                return Task.FromResult(key == Bbs757Altimeters.DecisionHeightKey ? Dh : null);
            },
            Write = rpn =>
            {
                Writes.Add(rpn);
                if (rpn.EndsWith("(>K:KOHLSMAN_SET)", StringComparison.Ordinal))
                {
                    if (AltimetersFollow) _pendingMb = double.Parse(rpn.Split(' ')[0], CultureInfo.InvariantCulture) / 16.0;
                    _readsSinceSet = 0;
                    return;
                }
                int up = Math.Min(Count(new[] { rpn }, "(>K:INCREASE_DECISION_HEIGHT)"), DhEventsCountedPerCall);
                int down = Math.Min(Count(new[] { rpn }, "(>K:DECREASE_DECISION_HEIGHT)"), DhEventsCountedPerCall);
                if (Dh is double dh) Dh = dh + 10 * up - 10 * down;
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                WaitedMs += ms;
                return Task.CompletedTask;
            },
        };
    }
}
