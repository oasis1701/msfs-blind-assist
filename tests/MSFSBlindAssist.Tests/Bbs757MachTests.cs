using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's Mach entry through the speed window (gap plan 05, Task 3): the typed entry, the IAS/MACH
/// changeover before a set in the other units, and the Mach set against a simulated MCP that behaves as
/// measured at FL290 on 2026-10-26 ([B757-51]): one knob event moves 0.01 Mach, bursts speed up in BlueBird's knot
/// tiers, the target stops at .40 but runs past .86 (to at least .95), and the Mach shows only on the
/// window's wheels (<c>L:BBS_TARGET_MACH</c> stays 0). Whether the aircraft flies it is checked in the sim.
/// </summary>
public class Bbs757MachTests
{
    private const string RangeError = "Enter a speed from 100 to 399 knots, or a Mach number from 0.40 to 0.86.";

    private static readonly Bbs757Press Changeover =
        new("1 (>L:B757_MCP_AP_SPD_MACH_SW, bool)", "0 (>L:B757_MCP_AP_SPD_MACH_SW, bool)", 0);

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

    // ---- Typed entry ------------------------------------------------------------------------

    [Theory]
    [InlineData("250", 250, null)]
    [InlineData("100", 100, null)]
    [InlineData("399", 399, null)]
    [InlineData("0.78", null, 0.78)]
    [InlineData(".78", null, 0.78)]
    [InlineData("M.78", null, 0.78)]
    [InlineData("m0.78", null, 0.78)]
    [InlineData(" M 0.40 ", null, 0.40)]
    [InlineData("0.86", null, 0.86)]
    public void Parses_knots_and_mach(string text, int? knots, double? mach)
    {
        Assert.True(Bbs757Mach.TryParse(text, out var entry, out string error), error);
        Assert.Equal(knots, entry.Knots);
        Assert.Equal(mach, entry.Mach);
    }

    [Fact]
    public void Parses_a_comma_mach_under_de_DE()
    {
        InCulture("de-DE", () =>
        {
            Assert.True(Bbs757Mach.TryParse("0,78", out var entry, out _));
            Assert.Equal(0.78, entry.Mach);
            Assert.True(Bbs757Mach.TryParse("M,78", out entry, out _));
            Assert.Equal(0.78, entry.Mach);
        });
    }

    [Theory]
    [InlineData("0.39")]
    [InlineData("0.87")]
    [InlineData("0.95")]       // BlueBird's target runs to .95; the app never sets past .86
    [InlineData("0.785")]      // the window shows hundredths only
    [InlineData("99")]
    [InlineData("400")]
    [InlineData("250.5")]
    [InlineData("1.5")]
    [InlineData("M250")]
    [InlineData("")]
    [InlineData("fast")]
    [InlineData(null)]
    public void Refuses_out_of_range(string? text)
    {
        Assert.False(Bbs757Mach.TryParse(text, out _, out string error));
        Assert.Equal(RangeError, error);
    }

    [Theory]
    [InlineData(0.78, "Mach 0.78")]
    [InlineData(0.4, "Mach 0.40")]
    [InlineData(null, "Mach unavailable")]
    public void Phrase_is_two_decimals_and_invariant(double? mach, string said)
    {
        Assert.Equal(said, Bbs757Mach.Phrase(mach));
        InCulture("de-DE", () => Assert.Equal(said, Bbs757Mach.Phrase(mach)));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(8, 8, 0)]
    [InlineData(10, 10, 0)]
    [InlineData(13, 11, 1)]    // 11 events move 12 hundredths; the last one goes alone
    [InlineData(40, 25, 0)]
    [InlineData(46, 25, 6)]    // .40 to .86: never a burst into the unmeasured third tier
    public void Bursts_use_the_knot_tiers_and_stop_at_the_measured_ones(int hundredths, int bulk, int rest)
    {
        Assert.Equal(new Bbs757SpeedBurstPlan(bulk, rest), Bbs757Mach.Bursts(hundredths));
    }

    // ---- The set against a simulated MCP ----------------------------------------------------

    /// <summary>
    /// The MCP as measured at FL290 on 2026-10-26: the IAS/MACH flag, the window's wheels in Mach, the knob
    /// events in tiers (1 hundredth each for events 1-10, 2 for 11-25, 3 after), the .40 floor and no
    /// ceiling before .95. A changeover press flips the flag unless <see cref="ChangeoverWorks"/> is false
    /// (on the ground, measured in session 8).
    /// </summary>
    private sealed class FakeMcp
    {
        private static readonly Regex BurstLoop =
            new(@"^(\d+) sp0 :1 l0 0 > if\{ \(>K:AP_SPD_VAR_(INC|DEC)\) l0 1 - sp0 g1 \}$");

        public double? MachFlag = 1;
        public int Hundredths = 73;     // the Mach target, in hundredths
        public int Knots = 283;         // the IAS target the wheels show in IAS (measured: 20/90/40 at 283)
        public bool WheelsReadable = true;
        public bool ChangeoverWorks = true;
        public int RollDelayMs;         // the wheels show a change this long after it (measured: 1-2 s)
        public int Peak;
        public readonly List<string> Writes = new();
        public long Now;

        private long? _lastBurst;
        private int _burstEvents;
        private int _shownDuringRoll;   // what the wheels show until _rollUntil
        private long _rollUntil;
        private long _machShownAt;      // the wheels show Mach, not knots, from this time

        private int Displayed() => Now < _rollUntil ? _shownDuringRoll : Hundredths;

        private (double Tenths, double Hundredths) Wheels()
        {
            int shown = Displayed();
            bool mach = MachFlag is >= 0.5 && Now >= _machShownAt;
            return mach
                ? (shown / 10 * 10, (shown % 10 + 1) * 10)
                : (Knots / 100 * 10, (Knots / 10 % 10 + 1) * 10);
        }

        public McpIo Io => new()
        {
            Read = (key, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                double? value = key switch
                {
                    Bbs757Mcp.MachModeVar => MachFlag,
                    Bbs757Mcp.MachTenthsDrumVar => WheelsReadable ? Wheels().Tenths : null,
                    Bbs757Mcp.MachHundredthsDrumVar => WheelsReadable ? Wheels().Hundredths : null,
                    "BBS_TARGET_MACH" => 0,
                    _ => null,
                };
                return Task.FromResult(value);
            },
            Write = rpn =>
            {
                Writes.Add(rpn);
                if (rpn == Changeover.Down && ChangeoverWorks && MachFlag is double f)
                {
                    MachFlag = f > 0.5 ? 0 : 1;
                    _machShownAt = Now + RollDelayMs;
                }
                var loop = BurstLoop.Match(rpn);
                if (!loop.Success) return;
                int before = Displayed();
                if (_lastBurst is not long last || Now - last >= 300) _burstEvents = 0;
                _lastBurst = Now;
                int sign = loop.Groups[2].Value == "INC" ? 1 : -1;
                for (int i = int.Parse(loop.Groups[1].Value, CultureInfo.InvariantCulture); i > 0; i--)
                {
                    _burstEvents++;
                    int step = _burstEvents <= 10 ? 1 : _burstEvents <= 25 ? 2 : 3;
                    Hundredths = Math.Clamp(Hundredths + sign * step, 40, 95);
                    Peak = Math.Max(Peak, Hundredths);
                }
                if (RollDelayMs > 0)
                {
                    _shownDuringRoll = before;
                    _rollUntil = Now + RollDelayMs;
                }
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Now += ms;
                return Task.CompletedTask;
            },
        };

        public IEnumerable<string> Bursts => Writes.Where(w => w.Contains("AP_SPD_VAR_"));
    }

    [Fact]
    public async Task Mach_set_against_a_fake_mcp_up()
    {
        var mcp = new FakeMcp { Hundredths = 70 };

        string said = await Bbs757Mach.SetMachAsync(mcp.Io, 78, default);

        Assert.Equal("Mach 0.78", said);
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(true, 8) }, mcp.Bursts);   // one burst, no correction
    }

    [Fact]
    public async Task Mach_set_against_a_fake_mcp_down()
    {
        var mcp = new FakeMcp { Hundredths = 80 };

        string said = await Bbs757Mach.SetMachAsync(mcp.Io, 74, default);

        Assert.Equal("Mach 0.74", said);
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(false, 6) }, mcp.Bursts);
    }

    [Fact]
    public async Task The_whole_range_in_one_set_never_passes_the_target()
    {
        var mcp = new FakeMcp { Hundredths = 40 };

        string said = await Bbs757Mach.SetMachAsync(mcp.Io, 86, default);

        Assert.Equal("Mach 0.86", said);
        Assert.Equal(86, mcp.Peak);   // a burst of 30 would have run to .95 (measured)
    }

    /// <summary>The fake's MCP with every speed write passed through <paramref name="after"/> once it has landed.</summary>
    private static McpIo Tampered(FakeMcp mcp, Action<string> after)
    {
        var io = mcp.Io;
        return new McpIo { Read = io.Read, Delay = io.Delay, Write = rpn => { io.Write(rpn); after(rpn); } };
    }

    [Fact]
    public async Task An_overshoot_is_brought_back_in_a_linear_burst()
    {
        // .41 to .60 with one hundredth too many landing: the correction goes the other way, 1 an event.
        var mcp = new FakeMcp { Hundredths = 41 };
        bool extra = false;
        var io = Tampered(mcp, rpn => { if (!extra && rpn.Contains("AP_SPD_VAR_INC")) { extra = true; mcp.Hundredths++; } });

        Assert.Equal("Mach 0.60", await Bbs757Mach.SetMachAsync(io, 60, default));
        Assert.Contains(Bbs757Mcp.SpeedBurstCode(false, 1), mcp.Bursts);
    }

    [Fact]
    public async Task A_short_landing_is_never_chased_past_the_planned_count()
    {
        // A burst that seems to land short may still be landing: sending more than the planned difference in
        // its direction could carry the target past what was typed (and near .86, past the aircraft's limit).
        var mcp = new FakeMcp { Hundredths = 41 };
        bool lost = false;
        var io = Tampered(mcp, rpn => { if (!lost && rpn.Contains("AP_SPD_VAR_INC")) { lost = true; mcp.Hundredths--; } });

        Assert.Equal("Mach 0.59", await Bbs757Mach.SetMachAsync(io, 60, default));   // the honest read-back
        Assert.True(mcp.Peak <= 60);
        // Only the plan for 19: a bulk of 14 events (18 hundredths) and its rest of 1; no correction after.
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(true, 14), Bbs757Mcp.SpeedBurstCode(true, 1) }, mcp.Bursts);
    }

    [Fact]
    public async Task A_mach_typed_in_knots_waits_for_the_wheels_to_show_mach()
    {
        // Review finding 1: in IAS at 283 kt the wheels read 20/90 (".28"); they roll to the Mach 1-2 s after
        // the changeover (measured 2026-10-26). Planned from ".28", .78 would send +50 and run the target to .95.
        var mcp = new FakeMcp { MachFlag = 0, Hundredths = 73, Knots = 283, RollDelayMs = 1500 };
        Assert.Null(await Bbs757Mach.EnsureUnitsAsync(mcp.Io, wantMach: true, Changeover, () => { }, default));

        string said = await Bbs757Mach.SetMachAsync(mcp.Io, 78, default);

        Assert.Equal("Mach 0.78", said);
        Assert.True(mcp.Peak <= 78, $"the target reached .{mcp.Peak}");
        Assert.Equal(new[] { Bbs757Mcp.SpeedBurstCode(true, 5) }, mcp.Bursts);
    }

    [Fact]
    public async Task A_burst_is_judged_only_once_the_wheels_have_rolled()
    {
        // Review finding 4: the wheels show a burst 1-2 s after it; two reads of the old digits must not
        // count as "it did not move", nor send a correction on top of the burst still rolling in.
        var mcp = new FakeMcp { Hundredths = 70, RollDelayMs = 2000 };

        string said = await Bbs757Mach.SetMachAsync(mcp.Io, 78, default);

        Assert.Equal("Mach 0.78", said);
        Assert.Single(mcp.Bursts);
        Assert.Equal(78, mcp.Peak);
    }

    [Fact]
    public async Task Wheels_that_never_show_a_mach_send_nothing()
    {
        var mcp = new FakeMcp { MachFlag = 1, Hundredths = 73, RollDelayMs = 60_000 };
        mcp.Io.Write(Changeover.Down);   // flag to 0
        mcp.Io.Write(Changeover.Down);   // and back to 1: the wheels keep showing knots for a minute

        Assert.Equal("Mach unavailable", await Bbs757Mach.SetMachAsync(mcp.Io, 78, default));
        Assert.Empty(mcp.Bursts);
    }

    [Fact]
    public async Task Unread_wheels_send_nothing()
    {
        var mcp = new FakeMcp { WheelsReadable = false };

        Assert.Equal("Mach unavailable", await Bbs757Mach.SetMachAsync(mcp.Io, 78, default));
        Assert.Empty(mcp.Writes);
    }

    // ---- The IAS/MACH changeover before a set (ruling R59) ----------------------------------

    [Fact]
    public async Task Changeover_refused_on_the_ground()
    {
        var mcp = new FakeMcp { MachFlag = 0, ChangeoverWorks = false };
        int presses = 0;

        string? refused = await Bbs757Mach.EnsureUnitsAsync(mcp.Io, wantMach: true, Changeover, () => presses++, default);

        Assert.Equal("IAS/MACH changeover is not available now", refused);
        Assert.Equal(1, presses);
        Assert.Equal(new[] { Changeover.Down, Changeover.Up }, mcp.Writes);   // the press and its release, nothing else
        Assert.InRange(mcp.Now, 2000, 2500);                                   // waited out the 2 s for the flag
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(1.0, false)]
    public async Task A_set_in_the_other_units_presses_the_changeover_first(double flag, bool wantMach)
    {
        var mcp = new FakeMcp { MachFlag = flag };
        int presses = 0;

        Assert.Null(await Bbs757Mach.EnsureUnitsAsync(mcp.Io, wantMach, Changeover, () => presses++, default));
        Assert.Equal(1, presses);
        Assert.Equal(wantMach ? 1 : 0, mcp.MachFlag);
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    [InlineData(null, false)]   // an unread flag never stops a knots set (as before)
    public async Task A_set_in_the_shown_units_presses_nothing(double? flag, bool wantMach)
    {
        var mcp = new FakeMcp { MachFlag = flag };

        Assert.Null(await Bbs757Mach.EnsureUnitsAsync(mcp.Io, wantMach, Changeover, () => { }, default));
        Assert.Empty(mcp.Writes);
    }

    // ---- Through the speed window (the definition) --------------------------------------------

    private static (BBS757Definition Def, SpeechCapture Speech, SimConnectManager Sim) Window(FakeMcp mcp)
        => (new BBS757Definition { CanWrite = _ => true, NowMs = () => mcp.Now, McpSetIo = _ => mcp.Io },
            new SpeechCapture(), new SimConnectManager(IntPtr.Zero));

    [Fact]
    public async Task A_typed_mach_in_knots_presses_the_changeover_then_sets_and_says_the_mach()
    {
        var mcp = new FakeMcp { MachFlag = 0, Hundredths = 73 };
        var (def, speech, sim) = Window(mcp);

        await def.SetMcpSpeedAsync(new Bbs757SpeedEntry(null, 0.78), sim, speech);

        Assert.Equal(new[] { "Mach 0.78" }, speech.All);   // the changeover itself says nothing (R59)
        Assert.Equal(1, mcp.MachFlag);
        Assert.Equal(Changeover.Down, mcp.Writes[0]);      // the map's own press, before any burst
    }

    [Fact]
    public async Task A_typed_mach_the_aircraft_cannot_change_to_is_refused_once_and_sends_no_burst()
    {
        var mcp = new FakeMcp { MachFlag = 0, ChangeoverWorks = false };
        var (def, speech, sim) = Window(mcp);

        await def.SetMcpSpeedAsync(new Bbs757SpeedEntry(null, 0.78), sim, speech);

        Assert.Equal(new[] { "IAS/MACH changeover is not available now" }, speech.All);
        Assert.Empty(mcp.Bursts);
    }

    [Fact]
    public void The_speed_window_takes_mach_and_says_the_range()
    {
        Assert.True(Bbs757Mach.TryParse("0.78", out var entry, out _));
        Assert.Equal(78, entry.Hundredths);
        Assert.Null(new Bbs757SpeedEntry(250, null).Hundredths);
    }

    [Fact]
    public async Task A_mach_set_with_the_flag_unread_presses_nothing()
    {
        // The changeover is a toggle: pressed on an unknown state it could leave the window in knots.
        var mcp = new FakeMcp { MachFlag = null };

        Assert.Equal("Mach unavailable", await Bbs757Mach.EnsureUnitsAsync(mcp.Io, wantMach: true, Changeover, () => { }, default));
        Assert.Empty(mcp.Writes);
    }
}
