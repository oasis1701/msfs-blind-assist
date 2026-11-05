// Characterization tests for the Fenix SD's APU page (FenixChannelSdPages.ApuRows, seam ComposeApu, wired by a later task).
// The pushes are built with SdPush (FenixChannelSdWindowTests.cs) and the composer is called directly, so every field
// number is written out here, never read from FenixChannelFields: a wrong number in the app fails a test.
//
// The contract: the page reads 24 (the APU), 151.1 (the bleed valves) and 151.2 (the bleed pressures), all whole numbers.
// APU GEN (24.1 load %, 24.2 V, 24.3 HZ while 24.12 and not 24.14; "APU GEN: OFF" while 24.12 and 24.14; else "APU GEN"
// alone, the row plan 05's ELEC page has), AVAIL (only while 24.8), BLEED (151.2.3, PSI), BLEED valve (151.1.5, a valve
// word), N (24.4, %), EGT (24.5, °C), FLAP OPEN (only while 24.10), FUEL LO PR (only while 24.9), LOW OIL LEVEL (only while
// 24.11). A part the push leaves out stops the page ("no field 24"), a field of another wire shape is that field's wrong
// shape, the first problem winning; a NaN reads "XX".

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdApuTests
{
    // ---- Building the messages ------------------------------------------------------------------

    // The APU (24): load, volts, frequency, N1, EGT, then each flag written only while set.
    private static FenixChannelProtoWriter Apu(
        double load = 38.4, double volts = 115.4, double frequency = 400.4, double n1 = 100.2, double egt = 411.6,
        bool avail = false, bool fuelLowPressure = false, bool flapOpen = false, bool lowOil = false,
        bool master = true, bool off = false)
    {
        var apu = new FenixChannelProtoWriter().Double(1, load).Double(2, volts).Double(3, frequency).Double(4, n1).Double(5, egt);
        if (avail) apu.Bool(8, true);
        if (fuelLowPressure) apu.Bool(9, true);
        if (flapOpen) apu.Bool(10, true);
        if (lowOil) apu.Bool(11, true);
        if (master) apu.Bool(12, true);
        if (off) apu.Bool(14, true);
        return apu;
    }

    // The pneumatic state (151): the valves (1) with the APU bleed (1.5, written only when not Open, which the Fenix
    // leaves out) and the bleed pressures (2) with the APU's (2.3).
    private static FenixChannelProtoWriter Pneumatic(int apuBleedValve = 0, double apuPressure = 35.4)
    {
        var valves = new FenixChannelProtoWriter();
        if (apuBleedValve != 0) valves.Varint(5, (ulong)apuBleedValve);
        var pressures = new FenixChannelProtoWriter().Double(1, 40).Double(2, 41).Double(3, apuPressure);
        return new FenixChannelProtoWriter().Message(1, valves).Message(2, pressures);
    }

    private static SdPush ApuPush(
        FenixChannelProtoWriter? apu, FenixChannelProtoWriter? pneumatic)
        => new SdPush { Page = 1 }.With(remote =>
        {
            if (apu is not null) remote.Message(24, apu);
            if (pneumatic is not null) remote.Message(151, pneumatic);
        });

    private static SdPush Running() => ApuPush(Apu(avail: true), Pneumatic());

    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.ApuRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // ---- The bleed valve's transit -----------------------------------------------------------------

    private const int FailedOpen = 3;
    private const int Closed = 1;

    private static string BleedValveRow(int valve, FenixChannelApuBleedHold hold, long atMs)
    {
        SdPush push = ApuPush(Apu(avail: true), Pneumatic(apuBleedValve: valve));
        FenixChannelRows rows = FenixChannelSdPages.ApuRows(push.Context(_ => { }) with { ApuBleedHold = hold, AtMs = atMs });
        return Assert.Single(rows.Rows!, row => row.StartsWith("BLEED valve", StringComparison.Ordinal));
    }

    [Fact]
    public void The_bleed_valve_is_in_transit_for_its_three_seconds_of_failed_open()
    {
        // Measured 2026-11-03: the Fenix sends FailedOpen for the APU bleed valve's 3 s in transit, both ways.
        var hold = new FenixChannelApuBleedHold();

        Assert.Equal("BLEED valve: in transit", BleedValveRow(FailedOpen, hold, 10_000));
        Assert.Equal("BLEED valve: in transit", BleedValveRow(FailedOpen, hold, 13_000));
        Assert.Equal("BLEED valve: closed", BleedValveRow(Closed, hold, 14_000));
    }

    [Fact]
    public void A_bleed_valve_that_stays_failed_open_reads_failed_open()
    {
        var hold = new FenixChannelApuBleedHold();
        long at = 10_000;

        Assert.Equal("BLEED valve: in transit", BleedValveRow(FailedOpen, hold, at));
        for (int second = 1; second < FenixChannelApuBleedHold.TransitMs / 1000; second++)
        {
            BleedValveRow(FailedOpen, hold, at + (second * 1000));
        }

        Assert.Equal("BLEED valve: failed open", BleedValveRow(FailedOpen, hold, at + FenixChannelApuBleedHold.TransitMs));
    }

    [Fact]
    public void A_new_transit_starts_its_own_three_seconds()
    {
        var hold = new FenixChannelApuBleedHold();

        BleedValveRow(FailedOpen, hold, 10_000);
        BleedValveRow(Closed, hold, 14_000);
        Assert.Equal("BLEED valve: in transit", BleedValveRow(FailedOpen, hold, 15_000));
    }

    [Fact]
    public void A_transit_seen_after_the_page_was_away_starts_its_own_three_seconds()
    {
        // The page is composed only while the SD shows it: a value seen long ago says nothing about this one.
        var hold = new FenixChannelApuBleedHold();

        BleedValveRow(FailedOpen, hold, 10_000);
        Assert.Equal("BLEED valve: in transit", BleedValveRow(FailedOpen, hold, 100_000));
    }

    [Fact]
    public void With_no_hold_the_valve_reads_as_sent()
    {
        Assert.Contains("BLEED valve: failed open", RowsOf(ApuPush(Apu(avail: true), Pneumatic(apuBleedValve: FailedOpen))));
    }

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void The_apu_page_reads_gen_bleed_n_and_egt()
    {
        Assert.Equal(
            new[]
            {
                "APU GEN: 38 %, 115 V, 400 HZ", "AVAIL", "BLEED: 35 PSI", "BLEED valve: open", "N: 100 %", "EGT: 412 °C"
            },
            RowsOf(Running()));
    }

    [Fact]
    public void The_amber_title_is_the_first_row_while_set()
    {
        // 24.13: measured on 2026-11-03, set from APU master on until AVAIL in a start, and from the end of the cooldown
        // until the APU stopped.
        IReadOnlyList<string> amber = RowsOf(ApuPush(Apu(master: true).Bool(13, true), Pneumatic()));
        Assert.Equal("APU title, amber", amber[0]);
        Assert.StartsWith("APU GEN", amber[1]);

        IReadOnlyList<string> clear = RowsOf(ApuPush(Apu(master: true), Pneumatic()));
        Assert.DoesNotContain("APU title, amber", clear);
        Assert.StartsWith("APU GEN", clear[0]);
    }

    [Fact]
    public void Memos_show_only_while_drawn()
    {
        // AVAIL (24.8), FLAP OPEN (24.10), FUEL LO PR (24.9) and LOW OIL LEVEL (24.11) each show only while their own flag
        // is set, and in the SD's order.
        Assert.Equal(
            new[] { "APU GEN: 38 %, 115 V, 400 HZ", "BLEED: 35 PSI", "BLEED valve: open", "N: 100 %", "EGT: 412 °C" },
            RowsOf(ApuPush(Apu(), Pneumatic())));
        Assert.Equal(
            new[]
            {
                "APU GEN: 38 %, 115 V, 400 HZ", "BLEED: 35 PSI", "BLEED valve: open", "N: 100 %", "EGT: 412 °C",
                "FLAP OPEN", "FUEL LO PR", "LOW OIL LEVEL"
            },
            RowsOf(ApuPush(Apu(flapOpen: true, fuelLowPressure: true, lowOil: true), Pneumatic())));
        Assert.Contains("FLAP OPEN", RowsOf(ApuPush(Apu(flapOpen: true), Pneumatic())));
        Assert.DoesNotContain("FUEL LO PR", RowsOf(ApuPush(Apu(flapOpen: true), Pneumatic())));
        Assert.Contains("FUEL LO PR", RowsOf(ApuPush(Apu(fuelLowPressure: true), Pneumatic())));
        Assert.Contains("LOW OIL LEVEL", RowsOf(ApuPush(Apu(lowOil: true), Pneumatic())));
        Assert.Contains("AVAIL", RowsOf(ApuPush(Apu(avail: true), Pneumatic())));
    }

    [Fact]
    public void Apu_gen_shows_values_only_with_the_master_on()
    {
        // 24.12 clear: the legend alone, though the push carries the figures; the master off with 24.14 set is the legend
        // too (24.14 is not read).
        Assert.Equal("APU GEN", RowsOf(ApuPush(Apu(master: false), Pneumatic()))[0]);
        Assert.Equal("APU GEN", RowsOf(ApuPush(Apu(master: false, off: true), Pneumatic()))[0]);
        Assert.Equal("APU GEN", RowsOf(ApuPush(new FenixChannelProtoWriter().Double(4, 100).Double(5, 412), Pneumatic()))[0]);
    }

    [Fact]
    public void Apu_gen_reads_off_while_its_pushbutton_is_off()
    {
        // The pushbutton off (24.14) with the APU running: "APU GEN: OFF", as the ELEC page's row, though the push still
        // carries figures; no other row changes.
        IReadOnlyList<string> rows = RowsOf(ApuPush(Apu(avail: true, off: true), Pneumatic()));

        Assert.Equal(
            new[] { "APU GEN: OFF", "AVAIL", "BLEED: 35 PSI", "BLEED valve: open", "N: 100 %", "EGT: 412 °C" }, rows);
    }

    [Fact]
    public void Bleed_valve_follows_the_apu_bleed_state()
    {
        // 151.1.5: Open is left out on the wire; Closed is 1.
        Assert.Contains("BLEED valve: closed", RowsOf(ApuPush(Apu(), Pneumatic(apuBleedValve: 1))));
        Assert.Contains("BLEED valve: open", RowsOf(ApuPush(Apu(), Pneumatic(apuBleedValve: 0))));
    }

    [Fact]
    public void A_nan_reads_XX()
    {
        // The APU's bleed pressure (151.2.3) is NaN with the APU off: crosses, never 0 or "NaN" (Q5).
        IReadOnlyList<string> rows = RowsOf(ApuPush(Apu(master: false), Pneumatic(apuPressure: double.NaN)));

        Assert.Contains("BLEED: XX PSI", rows);
    }

    // ---- What stops the page --------------------------------------------------------------------

    [Fact]
    public void A_missing_apu_message_is_not_available()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.ApuRows(ApuPush(null, Pneumatic()).Context(reported.Add));

        Assert.Equal("no field 24", rows.Problem);
        Assert.Null(rows.Rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void A_missing_pneumatic_message_is_not_available()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.ApuRows(ApuPush(Apu(), null).Context(reported.Add));

        Assert.Equal("no field 151", rows.Problem);
        Assert.Null(rows.Rows);
    }

    [Fact]
    public void The_reader_names_the_first_problem_by_path()
    {
        // A varint where 151.2.3 is a double: the page's problem, not reported by the page.
        var pressures = new FenixChannelProtoWriter().Double(1, 40).Double(2, 41).Varint(3, 1);
        var pneumatic = new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).Message(2, pressures);
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.ApuRows(ApuPush(Apu(), pneumatic).Context(reported.Add));

        Assert.Equal("field 151.2.3 has the wrong shape", rows.Problem);
        Assert.Empty(reported);

        // A second, later problem (the EGT is read before the bleed pressure) never replaces the first.
        var badApu = new FenixChannelProtoWriter().Double(1, 38).Varint(5, 412).Bool(12, true);
        FenixChannelRows first = FenixChannelSdPages.ApuRows(ApuPush(badApu, pneumatic).Context(reported.Add));

        Assert.Equal("field 24.5 has the wrong shape", first.Problem);
    }

    [Fact]
    public void An_unknown_valve_value_reads_not_available_and_is_reported()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.ApuRows(ApuPush(Apu(), Pneumatic(apuBleedValve: 99)).Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Contains("BLEED valve: not available", rows.Rows!);
        Assert.Equal(new[] { "field 151.1.5 has an unknown value 99" }, reported);
    }

    // ---- Culture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Apu_rows_are_the_same_in_every_culture(string culture)
    {
        // EGT -52.5 rounds away from zero to -53 with an ASCII minus, whatever the PC's culture.
        SdPush push = ApuPush(Apu(egt: -52.5), Pneumatic());

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => RowsOf(push));

        Assert.Contains("EGT: -53 °C", rows);
        Assert.Contains("APU GEN: 38 %, 115 V, 400 HZ", rows);
        Assert.DoesNotContain(rows, row => row.Contains('−'));
    }

    // ---- The seams ------------------------------------------------------------------------------

    // A push whose only field 151 content is the valves message 151.1, with 151.1.1 set and nothing else under 151.
    private static SdPush ValvesOnlyPush(int page)
        => new SdPush { Page = page }.With(remote =>
            remote.Message(151, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Bool(1, true))));

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(12)]
    public void Each_ground_page_seam_gives_its_composers_rows(int page)
    {
        // A push that composes for the page (a problem compares just as well as rows: the test compares the results).
        // Field 151 is present on every page, so BLEED, PRESS and COND each give their own problem, not "no field 151".
        var push = ValvesOnlyPush(page);

        FenixChannelRows? composed = FenixChannelSdPages.Compose(page, push.Context(_ => { }));
        FenixChannelRows expected = page switch
        {
            1 => FenixChannelSdPages.ApuRows(push.Context(_ => { })),
            2 => FenixChannelSdPages.BleedRows(push.Context(_ => { })),
            3 => FenixChannelSdPages.PressRows(push.Context(_ => { })),
            4 => FenixChannelSdPages.CondRows(push.Context(_ => { })),
            6 => FenixChannelSdPages.DoorRows(push.Context(_ => { })),
            9 => FenixChannelSdPages.FctlRows(push.Context(_ => { })),
            _ => FenixChannelSdPages.WheelRows(push.Context(_ => { })),
        };

        Assert.NotNull(composed);
        Assert.Equal(expected.Problem, composed.Value.Problem);
        Assert.Equal(expected.Rows?.ToArray(), composed.Value.Rows?.ToArray());
    }

    [Fact]
    public void The_air_pages_give_three_different_problems_on_a_valves_only_push()
    {
        // BLEED, PRESS and COND each read 151.1 first and then their own part of 151, which this push leaves out: each
        // page's problem must name its own part, so no two of the three can be the same string.
        SdPush push = ValvesOnlyPush(2);

        FenixChannelRows bleed = FenixChannelSdPages.BleedRows(push.Context(_ => { }));
        FenixChannelRows press = FenixChannelSdPages.PressRows(push.Context(_ => { }));
        FenixChannelRows cond = FenixChannelSdPages.CondRows(push.Context(_ => { }));

        Assert.Null(bleed.Rows);
        Assert.Null(press.Rows);
        Assert.Null(cond.Rows);
        Assert.NotNull(bleed.Problem);
        Assert.NotNull(press.Problem);
        Assert.NotNull(cond.Problem);
        Assert.Equal(3, new[] { bleed.Problem, press.Problem, cond.Problem }.Distinct().Count());
    }
}
