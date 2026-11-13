// Pins [FNX-20]: a typed Fenix FCU set (heading, speed, altitude, vertical speed) lands on the value typed
// with the Fenix's Encoder Acceleration option on. Measured 2026-10-24 (3.0.0.7454, A321 IAE): one -80
// write took the altitude to the 100 ft floor and the old 46 ms "batched retry" then ran it up to 30600;
// one +40 write hit the 49000 ceiling; single writes of 20 and 5 landed exactly, and so did five +1 writes
// about 150 ms apart. So with acceleration on or unread a set writes at most 20 counts at a time, reads the
// knob back after each write and steps from what it reads, and never writes again on a knob that has not
// moved. The loop is pure (FenixA320FcuKnobSet.RunAsync over FenixFcuKnobIo), so it runs here against a
// fake knob that multiplies a write of more than 20 counts, or one within 100 ms of the previous, by 3.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FenixA320FcuKnobSetTests
{
    // ---- The fake knob -------------------------------------------------------------------------------

    /// <summary>
    /// An FCU knob on a fake clock. A write is the counter step the Fenix sees: it lands <see cref="LatencyMs"/>
    /// later, multiplied by <see cref="Factor"/> when it carries more than <see cref="BigWrite"/> counts or comes
    /// within <see cref="QuickMs"/> of the previous write (the measured acceleration, modelled), clamped to the
    /// knob's range or wrapped at 360. A read costs one frame (<see cref="ReadMs"/>).
    /// </summary>
    private sealed class FakeKnob
    {
        private readonly FenixFcuKnob _knob;
        private readonly double _min, _max;
        private readonly List<(long At, double Value)> _pending = new();
        private long? _lastWriteAt;
        private double _scheduled;   // the value once every pending write has landed

        public FakeKnob(FenixFcuKnob knob, double value, double min, double max)
        {
            _knob = knob; _min = min; _max = max;
            Value = _scheduled = value;
        }

        public double Value { get; private set; }
        public long Now { get; private set; }
        public int Factor { get; set; } = 3;
        public int BigWrite { get; set; } = 20;
        public int QuickMs { get; set; } = 100;
        public int LatencyMs { get; set; } = 40;
        public int ReadMs { get; set; } = 30;
        public bool Stuck { get; set; }
        public bool Unreadable { get; set; }
        /// <summary>Moves this many counts per write whatever was written (null: as written).</summary>
        public int? FixedMove { get; set; }
        /// <summary>The V/S window's dashes: while dashed a read gives <see cref="LiveValue"/>; the first write
        /// shows the live value rounded to the step, plus the write, unless <see cref="StaysDashed"/>.</summary>
        public bool Dashed { get; set; }
        public double LiveValue { get; set; }
        public bool StaysDashed { get; set; }

        public List<(long At, int Counts)> Writes { get; } = new();
        public List<long> Reads { get; } = new();

        public void Write(int counts)
        {
            int applied = counts;
            if (Math.Abs(counts) > BigWrite || (_lastWriteAt is long last && Now - last < QuickMs)) applied *= Factor;
            if (FixedMove is int fixedMove) applied = fixedMove;
            _lastWriteAt = Now;
            Writes.Add((Now, counts));
            if (Stuck) return;

            if (Dashed)
            {
                if (StaysDashed) return;
                Dashed = false;
                _scheduled = Math.Round(LiveValue / _knob.Step) * _knob.Step;
            }
            double next = _scheduled + applied * _knob.Step;
            next = _knob.Wraps ? ((next % 360) + 360) % 360 : Math.Clamp(next, _min, _max);
            _scheduled = next;
            _pending.Add((Now + LatencyMs, next));
        }

        public Task<FenixFcuReading> Read()
        {
            Now += ReadMs;
            Reads.Add(Now);
            foreach (var p in _pending.Where(p => p.At <= Now).ToList())
            {
                Value = p.Value;
                _pending.Remove(p);
            }
            if (Unreadable) return Task.FromResult(new FenixFcuReading(null));
            return Task.FromResult(Dashed ? new FenixFcuReading(LiveValue, Dashed: true) : new FenixFcuReading(Value));
        }

        public Task Delay(int ms)
        {
            Now += ms;
            return Task.CompletedTask;
        }

        public FenixFcuKnobIo Io(Func<bool>? stillWanted = null) => new()
        {
            Write = Write,
            Read = Read,
            Delay = Delay,
            NowMs = () => Now,
            StillWanted = stillWanted ?? (() => true),
        };
    }

    private static FakeKnob Altitude(double value) => new(FenixFcuKnob.Altitude, value, 100, 49000);
    private static FakeKnob Speed(double value) => new(FenixFcuKnob.Speed, value, 100, 399);
    private static FakeKnob Heading(double value) => new(FenixFcuKnob.Heading, value, 0, 359);
    private static FakeKnob VerticalSpeed(double value) => new(FenixFcuKnob.VerticalSpeed, value, -6000, 6000);

    private static Task<FenixFcuSetResult> Run(FakeKnob fake, FenixFcuKnob knob, double target, bool? acceleration,
        long? lastWriteMs = null, Func<bool>? stillWanted = null)
        => FenixA320FcuKnobSet.RunAsync(knob, target, acceleration, lastWriteMs, fake.Io(stillWanted));

    private static void NoWriteOverTwentyOrQuick(FakeKnob fake)
    {
        Assert.All(fake.Writes, w => Assert.InRange(Math.Abs(w.Counts), 1, FenixA320FcuKnobSet.AcceleratedChunkLimit));
        for (int i = 1; i < fake.Writes.Count; i++)
            Assert.True(fake.Writes[i].At - fake.Writes[i - 1].At >= FenixA320FcuKnobSet.AcceleratedSettleMs,
                $"writes {i - 1} and {i} only {fake.Writes[i].At - fake.Writes[i - 1].At} ms apart");
    }

    // ---- The pure steps ------------------------------------------------------------------------------

    [Theory]
    [InlineData(340, 20, 40)] [InlineData(20, 340, -40)] [InlineData(0, 360, 0)] [InlineData(359, 0, 1)]
    [InlineData(10, 190, 180)] [InlineData(190, 10, -180)] [InlineData(90, 90, 0)]
    public void Heading_goes_the_short_way_round(int from, int to, int counts)
    {
        Assert.Equal(counts, FenixA320FcuKnobSet.HeadingDelta(from, to));
        Assert.Equal(counts, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.Heading, from, to));
    }

    [Fact]
    public void Counts_are_whole_clicks_of_the_knob()
    {
        Assert.Equal(-80, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.Altitude, 15000, 7000));
        Assert.Equal(150, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.Speed, 100, 250));
        Assert.Equal(-3, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.Speed, 81, 78));       // Mach x 100
        Assert.Equal(12, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.VerticalSpeed, -1200, 0));
        Assert.Equal(2, FenixA320FcuKnobSet.CountsToGo(FenixFcuKnob.VerticalSpeed, -1234, -1000));   // a live V/S
    }

    [Theory]
    [InlineData(-80, 20, -20)] [InlineData(80, 20, 20)] [InlineData(7, 20, 7)] [InlineData(-3, 1, -1)]
    [InlineData(-80, int.MaxValue, -80)] [InlineData(0, 20, 0)]
    public void The_next_write_never_carries_more_than_the_limit(int countsToGo, int limit, int write)
        => Assert.Equal(write, FenixA320FcuKnobSet.NextWrite(countsToGo, limit));

    [Fact]
    public void The_limit_is_the_largest_write_measured_exact()
        => Assert.Equal(20, FenixA320FcuKnobSet.AcceleratedChunkLimit);

    // ---- Acceleration on: closed loop in chunks ------------------------------------------------------

    [Fact]
    public async Task Altitude_15000_to_7000_lands_with_acceleration_on()
    {
        var fake = Altitude(15000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(7000, result.Value);
        Assert.Equal(7000, fake.Value);
        Assert.Equal(new[] { -20, -20, -20, -20 }, fake.Writes.Select(w => w.Counts));
        NoWriteOverTwentyOrQuick(fake);
        Assert.InRange(result.ElapsedMs, 2000, 3000);   // four settles of 500 ms plus a frame per read
    }

    [Fact]
    public async Task An_unread_setting_takes_the_safe_path()
    {
        var fake = Altitude(15000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: null);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(7000, fake.Value);
        NoWriteOverTwentyOrQuick(fake);
    }

    [Fact]
    public async Task Speed_100_to_250_lands()
    {
        var fake = Speed(100);
        var result = await Run(fake, FenixFcuKnob.Speed, 250, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(250, fake.Value);
        Assert.Equal(150, fake.Writes.Sum(w => w.Counts));
        Assert.Equal(8, fake.Writes.Count);
        NoWriteOverTwentyOrQuick(fake);
    }

    [Theory]
    [InlineData(340, 20, 1)] [InlineData(20, 340, -1)] [InlineData(350, 170, -1)] [InlineData(5, 0, -1)]
    public async Task Heading_wraps_the_short_way(int from, int to, int sign)
    {
        var fake = Heading(from);
        var result = await Run(fake, FenixFcuKnob.Heading, to, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(to % 360, fake.Value);
        Assert.All(fake.Writes, w => Assert.Equal(sign, Math.Sign(w.Counts)));
        NoWriteOverTwentyOrQuick(fake);
    }

    [Fact]
    public async Task Heading_360_lands_on_north()
    {
        var fake = Heading(350);
        var result = await Run(fake, FenixFcuKnob.Heading, 360, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(0, fake.Value);
        Assert.Equal(new[] { 10 }, fake.Writes.Select(w => w.Counts));
    }

    [Fact]
    public async Task A_knob_whose_acceleration_outlasts_the_settle_still_lands()
    {
        // Whatever the Fenix's real time window is, a write seen multiplied halves the chunk and doubles the wait.
        var fake = Altitude(15000);
        fake.QuickMs = 900;
        var result = await Run(fake, FenixFcuKnob.Altitude, 5000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(5000, fake.Value);
        Assert.Equal(new[] { -20, -20 }, fake.Writes.Take(2).Select(w => w.Counts));   // the second was multiplied
        Assert.All(fake.Writes.Skip(2), w => Assert.InRange(Math.Abs(w.Counts), 1, 10));
        Assert.InRange(fake.Writes.Count, 3, 8);
    }

    [Fact]
    public async Task A_knob_stricter_than_measured_still_lands()
    {
        var fake = Altitude(15000);
        fake.BigWrite = 10;   // multiplies anything over 10
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(7000, fake.Value);
        Assert.All(fake.Writes.Skip(1), w => Assert.InRange(Math.Abs(w.Counts), 1, 10));
    }

    // ---- Acceleration off: one write of the whole difference -----------------------------------------

    [Fact]
    public async Task Acceleration_off_turns_the_whole_difference_in_one_write()
    {
        var fake = Altitude(15000);
        fake.Factor = 1;
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: false);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(new[] { -80 }, fake.Writes.Select(w => w.Counts));
        Assert.InRange(result.ElapsedMs, FenixA320FcuKnobSet.PlainSettleMs, 500);
    }

    [Fact]
    public async Task A_stale_off_still_lands_and_then_turns_in_chunks()
    {
        // The pilot turned acceleration on a moment ago: the first whole-difference write is multiplied (to
        // the 100 ft floor, as measured), and the set carries on from there in checked chunks.
        var fake = Altitude(15000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: false);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(7000, fake.Value);
        Assert.Equal(-80, fake.Writes[0].Counts);
        Assert.All(fake.Writes.Skip(1), w => Assert.InRange(Math.Abs(w.Counts), 1, 20));
        for (int i = 2; i < fake.Writes.Count; i++)
            Assert.True(fake.Writes[i].At - fake.Writes[i - 1].At >= FenixA320FcuKnobSet.AcceleratedSettleMs);
    }

    // ---- Never a second write on a knob that has not moved -------------------------------------------

    [Fact]
    public async Task A_knob_that_never_moves_gives_up_after_one_write()
    {
        var fake = Altitude(15000);
        fake.Stuck = true;
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Missed, result.End);
        Assert.Equal(15000, result.Value);
        Assert.Single(fake.Writes);
        Assert.InRange(result.ElapsedMs, FenixA320FcuKnobSet.NoMoveWaitMs, FenixA320FcuKnobSet.NoMoveWaitMs + 300);
    }

    [Fact]
    public async Task A_write_that_shows_late_is_never_sent_twice()
    {
        var fake = Altitude(15000);
        fake.LatencyMs = 1100;   // longer than the settle: the first read still sees 15000
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(7000, fake.Value);
        Assert.Equal(new[] { -20, -20, -20, -20 }, fake.Writes.Select(w => w.Counts));
    }

    [Fact]
    public async Task A_late_plain_write_is_never_sent_twice()
    {
        var fake = Altitude(15000);
        fake.Factor = 1;
        fake.LatencyMs = 700;
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: false);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(new[] { -80 }, fake.Writes.Select(w => w.Counts));
    }

    // ---- Bounded --------------------------------------------------------------------------------------

    [Fact]
    public async Task A_knob_that_moves_the_wrong_way_stops_within_the_stall_bound()
    {
        var fake = Altitude(15000);
        fake.FixedMove = 1;   // up one click whatever is written
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Missed, result.End);
        // Each wrong-way move backs off once (chunk 20, 10, 5, 2, 1: four changes, each a fresh count), then
        // StallWrites writes that bring it no closer end the set.
        Assert.InRange(fake.Writes.Count, 1, FenixA320FcuKnobSet.StallWrites + 4);
        Assert.True(result.ElapsedMs < FenixA320FcuKnobSet.MaxSetMs);
    }

    [Fact]
    public async Task A_knob_that_crawls_stops_within_the_time_bound()
    {
        var fake = Altitude(100);
        fake.FixedMove = 1;   // the right way, but one click per write
        var result = await Run(fake, FenixFcuKnob.Altitude, 49000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Missed, result.End);
        Assert.True(fake.Writes.Count <= FenixA320FcuKnobSet.MaxWrites);
        Assert.InRange(result.ElapsedMs, 1, FenixA320FcuKnobSet.MaxSetMs + 3000);
    }

    // ---- Unread, superseded, a recent write ----------------------------------------------------------

    [Fact]
    public async Task An_unread_knob_writes_nothing()
    {
        var fake = Altitude(15000);
        fake.Unreadable = true;
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Unread, result.End);
        Assert.Empty(fake.Writes);
    }

    [Fact]
    public async Task A_read_lost_midway_stops_writing()
    {
        var fake = Altitude(15000);
        var io = fake.Io();
        int reads = 0;
        var lossy = new FenixFcuKnobIo
        {
            Write = io.Write, Delay = io.Delay, NowMs = io.NowMs, StillWanted = io.StillWanted,
            Read = async () => ++reads == 1 ? await fake.Read() : new FenixFcuReading(null),
        };
        var result = await FenixA320FcuKnobSet.RunAsync(FenixFcuKnob.Altitude, 7000, true, null, lossy);

        Assert.Equal(FenixFcuSetEnd.Unread, result.End);
        Assert.Equal(15000, result.Value);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task A_newer_set_takes_over_after_the_write_in_flight()
    {
        var fake = Altitude(15000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true,
            stillWanted: () => fake.Writes.Count == 0);

        Assert.Equal(FenixFcuSetEnd.Superseded, result.End);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task A_set_no_longer_wanted_writes_nothing()
    {
        var fake = Altitude(15000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true, stillWanted: () => false);

        Assert.Equal(FenixFcuSetEnd.Superseded, result.End);
        Assert.Empty(fake.Writes);
    }

    [Fact]
    public async Task A_recent_write_to_the_knob_is_waited_out_before_the_first_read()
    {
        // The set this one replaced wrote 100 ms ago: reading now could see the value before that write lands,
        // and a write now would come within the acceleration window.
        var fake = Altitude(15000);
        await fake.Delay(1000);
        var result = await Run(fake, FenixFcuKnob.Altitude, 7000, acceleration: true, lastWriteMs: fake.Now - 100);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.True(fake.Reads[0] >= 900 + FenixA320FcuKnobSet.AcceleratedSettleMs, $"first read at {fake.Reads[0]}");
    }

    // ---- The V/S window's dashes ---------------------------------------------------------------------

    [Fact]
    public async Task A_dashed_vertical_speed_steps_from_the_value_it_shows_after_the_first_write()
    {
        var fake = VerticalSpeed(0);
        fake.Dashed = true;
        fake.LiveValue = -1234;   // v3 sends the live V/S while dashed
        var result = await Run(fake, FenixFcuKnob.VerticalSpeed, -500, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(-500, fake.Value);
        NoWriteOverTwentyOrQuick(fake);
    }

    [Fact]
    public async Task A_vertical_speed_still_dashed_after_a_write_stops()
    {
        var fake = VerticalSpeed(0);
        fake.Dashed = true;
        fake.StaysDashed = true;
        fake.LiveValue = -1234;
        var result = await Run(fake, FenixFcuKnob.VerticalSpeed, -500, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.StillDashed, result.End);
        Assert.Single(fake.Writes);
    }

    // ---- What is spoken ------------------------------------------------------------------------------

    private static FenixFcuSetResult R(FenixFcuSetEnd end, double? value) => new(end, value, 1, 1000);

    [Theory] [InlineData("en-US")] [InlineData("de-DE")] [InlineData("sv-SE")]
    public void A_miss_is_spoken_once_in_the_pilots_terms(string culture)
    {
        string Say(FenixFcuKnob knob, FenixFcuSetEnd end, double? value, double target)
            => TestCulture.Run(culture, () => FenixA320FcuKnobSet.Spoken(knob, R(end, value), target))!;

        Assert.Equal("Altitude set to 7100, not 7000.", Say(FenixFcuKnob.Altitude, FenixFcuSetEnd.Missed, 7100, 7000));
        Assert.Equal("Altitude set to 30600, not 7000.", Say(FenixFcuKnob.Altitude, FenixFcuSetEnd.Missed, 30600, 7000));
        Assert.Equal("Speed set to 255, not 250.", Say(FenixFcuKnob.Speed, FenixFcuSetEnd.Missed, 255, 250));
        Assert.Equal("Speed set to 0.80, not 0.78.", Say(FenixFcuKnob.Speed, FenixFcuSetEnd.Missed, 80, 78));
        Assert.Equal("Heading set to 005, not 010.", Say(FenixFcuKnob.Heading, FenixFcuSetEnd.Missed, 5, 10));
        Assert.Equal("Vertical speed set to -1200, not -1000.", Say(FenixFcuKnob.VerticalSpeed, FenixFcuSetEnd.Missed, -1200, -1000));
        Assert.Equal("Vertical speed set to +300, not +500.", Say(FenixFcuKnob.VerticalSpeed, FenixFcuSetEnd.Missed, 300, 500));
        Assert.Equal("Vertical speed set to 0, not -100.", Say(FenixFcuKnob.VerticalSpeed, FenixFcuSetEnd.Missed, 0, -100));
        Assert.Equal("Vertical speed still dashed, not -1000.", Say(FenixFcuKnob.VerticalSpeed, FenixFcuSetEnd.StillDashed, -1234, -1000));
        Assert.Equal("Altitude not confirmed.", Say(FenixFcuKnob.Altitude, FenixFcuSetEnd.Unread, null, 7000));
        Assert.Equal("Altitude not confirmed.", Say(FenixFcuKnob.Altitude, FenixFcuSetEnd.Missed, null, 7000));
        // The first read failed, so nothing was written.
        Assert.Equal("Speed not set: no read-back.", TestCulture.Run(culture,
            () => FenixA320FcuKnobSet.Spoken(FenixFcuKnob.Speed, new FenixFcuSetResult(FenixFcuSetEnd.Unread, null, 0, 1000), 250)));
    }

    [Fact]
    public void A_landed_or_superseded_set_says_nothing()
    {
        // CORE-7: the screen reader already read what was typed, and the FCU's own value speaks as it changes.
        Assert.Null(FenixA320FcuKnobSet.Spoken(FenixFcuKnob.Altitude, R(FenixFcuSetEnd.Landed, 7000), 7000));
        Assert.Null(FenixA320FcuKnobSet.Spoken(FenixFcuKnob.Altitude, R(FenixFcuSetEnd.Superseded, 9000), 7000));
    }

    // ---- Per-knob turns ------------------------------------------------------------------------------

    [Fact]
    public void A_newer_set_of_the_same_knob_replaces_the_older_and_other_knobs_are_left_alone()
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int first = turns.Begin(FenixFcuKnob.Altitude, 7000);
        int heading = turns.Begin(FenixFcuKnob.Heading, 90);
        Assert.True(turns.IsLatest("E_FCU_ALTITUDE", first));

        int second = turns.Begin(FenixFcuKnob.Altitude, 9000);
        Assert.False(turns.IsLatest("E_FCU_ALTITUDE", first));
        Assert.True(turns.IsLatest("E_FCU_ALTITUDE", second));
        Assert.True(turns.IsLatest("E_FCU_HEADING", heading));

        Assert.Null(turns.LastWriteMs("E_FCU_ALTITUDE"));
        turns.Wrote("E_FCU_ALTITUDE", 1234);
        Assert.Equal(1234, turns.LastWriteMs("E_FCU_ALTITUDE"));
        Assert.Null(turns.LastWriteMs("E_FCU_HEADING"));
    }

    [Fact]
    public void An_aircraft_switch_ends_every_set()
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int altitude = turns.Begin(FenixFcuKnob.Altitude, 7000);
        turns.Close();
        Assert.False(turns.IsLatest("E_FCU_ALTITUDE", altitude));
        Assert.False(turns.IsLatest("E_FCU_ALTITUDE", turns.Begin(FenixFcuKnob.Altitude, 7000)));
        Assert.False(turns.IsRunning("E_FCU_ALTITUDE"));
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 13000, 0));
    }

    // ---- The steps a set passes through are not announced (CORE-7) -----------------------------------

    [Fact]
    public void A_running_set_swallows_every_value_but_its_target()
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int turn = turns.Begin(FenixFcuKnob.Altitude, 7000);
        Assert.True(turns.IsRunning("E_FCU_ALTITUDE"));

        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 13000, 0));
        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 7100, 0));
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 7000, 0));     // the target speaks, once, as before
        Assert.False(turns.Swallows(FenixFcuKnob.Heading, 120, 0));       // another knob is untouched

        turns.Finish("E_FCU_ALTITUDE", turn, FenixFcuSetEnd.Landed, 1000);
        Assert.False(turns.IsRunning("E_FCU_ALTITUDE"));
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 9000, 1001));   // a later cockpit turn speaks
    }

    [Fact]
    public void A_typed_heading_of_360_lets_north_through()
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        turns.Begin(FenixFcuKnob.Heading, 360);
        Assert.False(turns.Swallows(FenixFcuKnob.Heading, 0, 0));
        Assert.True(turns.Swallows(FenixFcuKnob.Heading, 350, 0));
    }

    [Theory] [InlineData("Missed")] [InlineData("StillDashed")]
    public void After_a_miss_the_knob_stays_quiet_for_one_delivery_period(string end)
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int turn = turns.Begin(FenixFcuKnob.Altitude, 7000);
        turns.Finish("E_FCU_ALTITUDE", turn, Enum.Parse<FenixFcuSetEnd>(end), 5000);

        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 7100, 5000 + FenixA320FcuKnobSet.QuietAfterMissMs - 1));
        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 7000, 5500));    // the miss sentence alone
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 7100, 5000 + FenixA320FcuKnobSet.QuietAfterMissMs));
        Assert.True(FenixA320FcuKnobSet.QuietAfterMissMs > 1000);          // past the 1 Hz delivery period
    }

    [Theory] [InlineData("Landed")] [InlineData("Superseded")] [InlineData("Unread")]
    public void Only_a_miss_brings_the_quiet(string end)
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int turn = turns.Begin(FenixFcuKnob.Altitude, 7000);
        turns.Finish("E_FCU_ALTITUDE", turn, Enum.Parse<FenixFcuSetEnd>(end), 5000);
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 7100, 5001));
    }

    [Fact]
    public void An_older_set_finishing_leaves_the_newer_running_and_a_new_set_ends_the_quiet()
    {
        var turns = new FenixA320FcuKnobSet.Turns();
        int older = turns.Begin(FenixFcuKnob.Altitude, 7000);
        int newer = turns.Begin(FenixFcuKnob.Altitude, 9000);
        turns.Finish("E_FCU_ALTITUDE", older, FenixFcuSetEnd.Superseded, 0);
        Assert.True(turns.IsRunning("E_FCU_ALTITUDE"));
        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 7000, 0));       // the newer target is 9000
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 9000, 0));

        turns.Finish("E_FCU_ALTITUDE", newer, FenixFcuSetEnd.Missed, 0);
        turns.Finish("E_FCU_ALTITUDE", newer, FenixFcuSetEnd.Superseded, 0);   // the finally's second call
        Assert.True(turns.Swallows(FenixFcuKnob.Altitude, 8000, 100));     // the quiet holds
        turns.Begin(FenixFcuKnob.Altitude, 8000);
        Assert.False(turns.Swallows(FenixFcuKnob.Altitude, 8000, 100));    // a new set's own target speaks
    }

    [Fact]
    public void The_definition_consumes_the_steps_and_lets_the_target_through()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();
        int turn = def.FcuSetTurns.Begin(FenixFcuKnob.Altitude, 7000);

        Assert.True(def.ProcessSimVarUpdate("N_FCU_ALTITUDE", 13000, speech));    // consumed silently
        Assert.True(def.ProcessSimVarUpdate("N_FCU_ALTITUDE", 9000, speech));
        Assert.False(def.ProcessSimVarUpdate("N_FCU_ALTITUDE", 7000, speech));    // the generic announcement
        Assert.False(def.ProcessSimVarUpdate("N_FCU_HEADING", 120, speech));      // no set of that knob
        Assert.Empty(speech.All);

        def.FcuSetTurns.Finish("E_FCU_ALTITUDE", turn, FenixFcuSetEnd.Landed, Environment.TickCount64);
        Assert.False(def.ProcessSimVarUpdate("N_FCU_ALTITUDE", 9000, speech));    // a cockpit turn after the set
    }

    [Fact]
    public void The_definition_keeps_the_knob_quiet_after_a_miss()
    {
        var def = new FenixA320Definition();
        var speech = new SpeechCapture();
        foreach (var knob in FenixFcuKnob.All)
        {
            int turn = def.FcuSetTurns.Begin(knob, 100);
            def.FcuSetTurns.Finish(knob.CounterVar, turn, FenixFcuSetEnd.Missed, Environment.TickCount64);
            Assert.True(def.ProcessSimVarUpdate(knob.ValueVar, 300, speech), knob.ValueVar);
        }
        Assert.Empty(speech.All);
    }

    // ---- A multiplied correction is not a stall ------------------------------------------------------

    [Fact]
    public async Task A_multiplied_correction_after_an_overshoot_still_lands()
    {
        // Factor 5 and a 900 ms window: the second write overshoots to 3000 and the first correction is
        // multiplied too; the halved, slower writes that follow are a fresh count, not a stall. Before the
        // reset this ended Missed at 9000 (the review's trace).
        var fake = Altitude(15000);
        fake.Factor = 5;
        fake.QuickMs = 900;
        var result = await Run(fake, FenixFcuKnob.Altitude, 11000, acceleration: true);

        Assert.Equal(FenixFcuSetEnd.Landed, result.End);
        Assert.Equal(11000, fake.Value);
        Assert.Equal(new[] { -20, -20, 10 }, fake.Writes.Take(3).Select(w => w.Counts));   // the 10 went in as 50
        Assert.All(fake.Writes.Skip(3), w => Assert.InRange(Math.Abs(w.Counts), 1, 5));
        Assert.True(result.ElapsedMs < FenixA320FcuKnobSet.MaxSetMs);
    }

    // ---- "Already at" ---------------------------------------------------------------------------------

    [Theory] [InlineData("sv-SE")] [InlineData("de-DE")] [InlineData("en-US")]
    public void Already_at_is_written_invariant(string culture)
    {
        Assert.Equal("Already at vertical speed -1000", TestCulture.Run(culture, () => FenixA320FcuKnobSet.AlreadyAt("vertical speed", -1000)));
        Assert.Equal("Already at altitude 7000", TestCulture.Run(culture, () => FenixA320FcuKnobSet.AlreadyAt("altitude", 7000)));
        Assert.DoesNotContain('−', TestCulture.Run(culture, () => FenixA320FcuKnobSet.AlreadyAt("vertical speed", -500)));
    }

    [Theory]
    [InlineData("SetFCUHeading", "Heading", "heading")]
    [InlineData("SetFCUSpeed", "Speed", "speed")]
    [InlineData("SetFCUAltitude", "Altitude", "altitude")]
    [InlineData("SetFCUVS", "VerticalSpeed", "vertical speed")]
    public void Already_at_is_skipped_while_a_set_of_that_knob_is_live(string method, string knob, string what)
    {
        // The 1 Hz cache can show a step of the set still running: "Already at 9000" while it turns on to 7000.
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, method);
        Assert.Contains($"!_fcuSetTurns.IsRunning(FenixFcuKnob.{knob}.CounterVar)", body);
        Assert.Contains($"FenixA320FcuKnobSet.AlreadyAt(\"{what}\",", body);
        Assert.DoesNotContain("$\"Already at", body);
    }

    [Fact]
    public void A_superseded_altitude_set_leaves_the_scale_to_the_newer_set()
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "SetFCUAltitude");
        int set = body.IndexOf("await SetFcuKnobAsync(FenixFcuKnob.Altitude", StringComparison.Ordinal);
        int guard = body.IndexOf("if (end != FenixFcuSetEnd.Superseded)", StringComparison.Ordinal);
        int restore = body.IndexOf("SetLVar(\"S_FCU_ALTITUDE_SCALE\", userPreferredScaleMode)", StringComparison.Ordinal);
        Assert.True(set > 0 && guard > set && restore > guard, "the scale restore must follow the set, behind the superseded check");
    }

    // ---- Wiring --------------------------------------------------------------------------------------

    private static readonly Dictionary<string, SimVarDefinition> Vars = new FenixA320Definition().GetVariables();

    private static string DefinitionPath => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Aircraft", "FenixA320Definition.cs");

    private static string HooksPath => Path.Combine(ClaudeContextBudgetTests.RepoRoot(),
        "MSFSBlindAssist", "Aircraft", "FenixA320Definition.Hooks.cs");

    [Fact]
    public void Each_knob_reads_back_through_an_on_request_twin()
    {
        var knobs = new[] { FenixFcuKnob.Heading, FenixFcuKnob.Speed, FenixFcuKnob.Altitude, FenixFcuKnob.VerticalSpeed };
        Assert.Equal(new[] { "E_FCU_HEADING", "E_FCU_SPEED", "E_FCU_ALTITUDE", "E_FCU_VS" }, knobs.Select(k => k.CounterVar));
        foreach (var knob in knobs)
        {
            Assert.Contains(knob.ReadoutVar, FenixA320Definition.FcuReadoutVariables);
            Assert.Equal(UpdateFrequency.OnRequest, Vars[knob.ReadoutVar].UpdateFrequency);
        }
        Assert.Equal("B_FCU_VERTICALSPEED_DASHED_READOUT", FenixFcuKnob.VerticalSpeed.DashedReadoutVar);
        Assert.Null(FenixFcuKnob.Altitude.DashedReadoutVar);
    }

    [Theory]
    [InlineData("SetFCUHeading", "FenixFcuKnob.Heading")]
    [InlineData("SetFCUSpeed", "FenixFcuKnob.Speed")]
    [InlineData("SetFCUAltitude", "FenixFcuKnob.Altitude")]
    [InlineData("SetFCUVS", "FenixFcuKnob.VerticalSpeed")]
    public void Every_typed_set_runs_the_closed_loop(string method, string knob)
    {
        string body = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, method);
        Assert.Contains($"SetFcuKnobAsync({knob},", body);
        Assert.DoesNotContain("JumpCounter(", body);
    }

    [Fact]
    public void The_set_reads_the_option_and_the_twins_and_the_batched_retry_is_gone()
    {
        string src = File.ReadAllText(DefinitionPath);
        Assert.DoesNotContain("SetFCUValueWithVerification", src);
        Assert.DoesNotContain("Task.Delay(33)", src);   // the old 10-count burst

        string set = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "SetFcuKnobAsync");
        Assert.Contains("EncoderAcceleration.ReadAsync(", set);
        Assert.Contains("FenixA320FcuKnobSet.RunAsync(", set);
        Assert.Contains("JumpCounter(", set);
        Assert.Contains("FenixA320FcuKnobSet.Spoken(", set);

        string read = FoFbwUnclaimedEventKeyTests.MethodBody(DefinitionPath, "ReadFcuKnobAsync");
        Assert.Contains("ReadFreshAsync(", read);
        Assert.DoesNotContain("GetCachedVariableValue(", read);

        string switchedAway = FoFbwUnclaimedEventKeyTests.MethodBody(HooksPath, "OnSwitchedAway");
        Assert.Contains("_fcuSetTurns.Close()", switchedAway);
    }
}
