// Characterization tests for the Fenix SD's WHEEL page (FenixChannelSdPages.WheelRows, seam ComposeWheel, wired by a later
// task). The pushes are built with SdPush (FenixChannelSdWindowTests.cs) and the composer is called directly, so every field
// number is written out here, never read from FenixChannelFields.
//
// The contract: the page reads 137 (the labels, varint bools: 12 L/G CTL, 9 N/W STEERING, 16/17 ANTI SKID, 7/8 NORM BRK,
// 2/15 ALTN BRK (each with its color: 16, 7 and 2 green; 17, 8 and 15 amber), 10/11 BSCU 1/2 amber, 13 AUTO BRK amber, 18 ACCU ONLY, 4/5/6 AUTO BRK LO/MED/MAX, 19 the tire pressure indicators are fitted), 138 (the brake
// temperatures 1-4, doubles), 139 (the tire pressures 1-6, doubles, read only while 137.19), 141 (the released brakes 1-4,
// bools), 140 (the brakes drawn amber 1-4, bools), 143 (the gears: 143.1 nose, .2 left, .3 right; each .1 and .2 the LGCIU 1 and 2 position, doubles: measured on the
// 2026-11-03 gear extension, 2 is up, 1 in transit and the absent field (0) down) and 144 (the gear doors 1-3, doubles,
// measured: 1 closed, 0 opening, 2 open). 135, 136 and 142 are not read. A gear or door value with no word reads
// "not available" and is reported, never guessed.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdWheelTests
{
    private static FenixChannelProtoWriter Flags(params int[] set)
    {
        var flags = new FenixChannelProtoWriter();
        foreach (int field in set.OrderBy(f => f)) flags.Bool(field, true);
        return flags;
    }

    private static FenixChannelProtoWriter Doubles(params double[] values)
    {
        var message = new FenixChannelProtoWriter();
        for (int i = 0; i < values.Length; i++) message.Double(i + 1, values[i]);
        return message;
    }

    // 143: each gear a message of its two LGCIU doubles.
    private static FenixChannelProtoWriter Gears(double nose1 = 2, double nose2 = 2, double left1 = 2, double left2 = 2,
        double right1 = 2, double right2 = 2)
        => new FenixChannelProtoWriter()
            .Message(1, Doubles(nose1, nose2))
            .Message(2, Doubles(left1, left2))
            .Message(3, Doubles(right1, right2));

    // 143 with the gear down: each gear a present, empty message (every LGCIU value is the absent field, 0).
    private static FenixChannelProtoWriter GearsDown()
        => new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter())
            .Message(2, new FenixChannelProtoWriter())
            .Message(3, new FenixChannelProtoWriter());

    private static SdPush WheelPush(
        FenixChannelProtoWriter? flags = null, FenixChannelProtoWriter? temps = null, FenixChannelProtoWriter? tires = null,
        FenixChannelProtoWriter? released = null, FenixChannelProtoWriter? gears = null, FenixChannelProtoWriter? doors = null,
        bool includeTires = true, FenixChannelProtoWriter? amber = null, bool includeAmber = true)
        => new SdPush { Page = 12 }.With(remote =>
        {
            remote.Message(137, flags ?? Flags());
            remote.Message(138, temps ?? Doubles(19.8114, 20.9959, 19.3692, 19.7807));
            if (includeTires) remote.Message(139, tires ?? Doubles(165, 165, 210, 205, 210, 205));
            if (includeAmber) remote.Message(140, amber ?? new FenixChannelProtoWriter());
            remote.Message(141, released ?? new FenixChannelProtoWriter());
            remote.Message(143, gears ?? Gears());
            remote.Message(144, doors ?? Doubles(1, 1, 1));
        });

    private static IReadOnlyList<string> RowsOf(SdPush push, List<string>? reported = null)
    {
        reported ??= [];
        FenixChannelRows rows = FenixChannelSdPages.WheelRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        return rows.Rows!;
    }

    [Fact]
    public void The_wheel_page_reads_gear_brakes_and_labels()
    {
        // A real cruise push (2026-11-03, A321, gear up): 137 had only .3 and .19 set.
        var reported = new List<string>();

        IReadOnlyList<string> rows = RowsOf(WheelPush(Flags(3, 19)), reported);

        Assert.Equal(
            new[]
            {
                "nose gear: up", "nose gear door: closed", "left gear: up", "left gear door: closed",
                "right gear: up", "right gear door: closed", "nose tires: left 165 PSI, right 165 PSI",
                "wheel 1: brake 20 °C, tire 210 PSI", "wheel 2: brake 21 °C, tire 205 PSI",
                "wheel 3: brake 19 °C, tire 210 PSI", "wheel 4: brake 20 °C, tire 205 PSI"
            },
            rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void Labels_show_only_while_drawn()
    {
        Assert.Equal(
            new[]
            {
                "L/G CTL", "nose gear: up", "nose gear door: closed", "N/W STEERING", "ANTI SKID, green",
                "NORM BRK, green", "ALTN BRK, amber", "ACCU ONLY", "AUTO BRK: LO"
            },
            RowsOf(WheelPush(Flags(12, 9, 16, 7, 15, 18, 4))).Take(9));

        // Each label's other color field draws it too.
        IReadOnlyList<string> alternatives = RowsOf(WheelPush(Flags(17, 8, 2, 5)));
        Assert.Contains("ANTI SKID, amber", alternatives);
        Assert.Contains("NORM BRK, amber", alternatives);
        Assert.Contains("ALTN BRK, green", alternatives);
        Assert.Contains("AUTO BRK: MED", alternatives);

        Assert.Contains("AUTO BRK: MAX", RowsOf(WheelPush(Flags(6))));

        // More than one auto brake field: the first of LO, MED, MAX.
        Assert.Contains("AUTO BRK: MED", RowsOf(WheelPush(Flags(5, 6))));
        Assert.Contains("AUTO BRK: LO", RowsOf(WheelPush(Flags(4, 5, 6))));

        // Nothing set: no label rows at all.
        IReadOnlyList<string> none = RowsOf(WheelPush(Flags()));
        foreach (string label in new[] { "L/G CTL", "N/W STEERING", "ACCU ONLY" })
        {
            Assert.DoesNotContain(label, none);
        }
        Assert.DoesNotContain(none, row => row.StartsWith("ANTI SKID") || row.StartsWith("NORM BRK")
            || row.StartsWith("ALTN BRK") || row.StartsWith("BSCU"));
        Assert.DoesNotContain(none, row => row.StartsWith("AUTO BRK"));
    }

    [Fact]
    public void Each_label_speaks_the_color_the_fenix_sends()
    {
        // Amber wins when both colors are set.
        IReadOnlyList<string> both = RowsOf(WheelPush(Flags(16, 17, 7, 8, 2, 15)));
        Assert.Contains("ANTI SKID, amber", both);
        Assert.Contains("NORM BRK, amber", both);
        Assert.Contains("ALTN BRK, amber", both);
        Assert.DoesNotContain(both, row => row.Contains("green"));

        IReadOnlyList<string> green = RowsOf(WheelPush(Flags(16, 7, 2)));
        Assert.Contains("ANTI SKID, green", green);
        Assert.Contains("NORM BRK, green", green);
        Assert.Contains("ALTN BRK, green", green);
    }

    [Fact]
    public void The_bscu_amber_labels_follow_the_anti_skid_place()
    {
        List<string> list = RowsOf(WheelPush(Flags(9, 16, 10, 11, 7))).ToList();

        Assert.Equal(new[] { "ANTI SKID, green", "BSCU 1, amber", "BSCU 2, amber", "NORM BRK, green" },
            list.GetRange(list.IndexOf("ANTI SKID, green"), 4));
        Assert.Contains("BSCU 1, amber", RowsOf(WheelPush(Flags(10))));
        Assert.DoesNotContain("BSCU 2, amber", RowsOf(WheelPush(Flags(10))));
        Assert.DoesNotContain("BSCU 1, amber", RowsOf(WheelPush(Flags(11))));
    }

    [Fact]
    public void Auto_brake_gains_amber_with_or_without_a_mode()
    {
        Assert.Contains("AUTO BRK: LO, amber", RowsOf(WheelPush(Flags(4, 13))));
        Assert.Contains("AUTO BRK: MED, amber", RowsOf(WheelPush(Flags(5, 13))));
        Assert.Contains("AUTO BRK: MAX, amber", RowsOf(WheelPush(Flags(6, 13))));
        Assert.Contains("AUTO BRK, amber", RowsOf(WheelPush(Flags(13))));
        Assert.Contains("AUTO BRK: MED", RowsOf(WheelPush(Flags(5))));
    }

    [Fact]
    public void The_gate_push_reads_its_brake_labels_with_colors()
    {
        // The gate push of 2026-11-03: 137 .2, .3, .8, .9, .13, .17, .18 and .19 set; 140 empty.
        IReadOnlyList<string> rows = RowsOf(WheelPush(Flags(2, 3, 8, 9, 13, 17, 18, 19)));

        Assert.Equal(
            new[] { "N/W STEERING", "ANTI SKID, amber", "NORM BRK, amber", "ALTN BRK, green", "ACCU ONLY", "AUTO BRK, amber" },
            rows.Where(row => row.StartsWith("N/W") || row.StartsWith("ANTI") || row.StartsWith("NORM")
                || row.StartsWith("ALTN") || row.StartsWith("ACCU") || row.StartsWith("AUTO")));
    }

    [Fact]
    public void An_amber_brake_reads_amber_after_its_temperature()
    {
        var amber = new FenixChannelProtoWriter().Bool(2, true);
        IReadOnlyList<string> rows = RowsOf(WheelPush(Flags(19), amber: amber));

        Assert.Contains("wheel 1: brake 20 °C, tire 210 PSI", rows);
        Assert.Contains("wheel 2: brake 21 °C amber, tire 205 PSI", rows);
        Assert.Contains("wheel 3: brake 19 °C, tire 210 PSI", rows);

        Assert.Contains("wheel 2: brake 21 °C amber, released", RowsOf(WheelPush(Flags(), released: Flags(2), amber: amber)));
        Assert.Contains("wheel 2: brake XX °C amber",
            RowsOf(WheelPush(Flags(), temps: Doubles(1, double.NaN, 3, 4), amber: amber)));
    }

    [Fact]
    public void A_push_without_the_amber_brakes_is_not_available()
    {
        var reported = new List<string>();
        FenixChannelRows missing = FenixChannelSdPages.WheelRows(WheelPush(includeAmber: false).Context(reported.Add));

        Assert.Equal("no field 140", missing.Problem);
        Assert.Empty(reported);
    }

    [Fact]
    public void Each_gear_reads_both_lgcius()
    {
        var reported = new List<string>();

        // The second LGCIU of each gear is read, not mirrored from the first.
        IReadOnlyList<string> rows = RowsOf(WheelPush(gears: Gears(2, 7, 7, 2, 2, 7)), reported);

        Assert.Contains("nose gear: LGCIU 1 up, LGCIU 2 not available", rows);
        Assert.Contains("left gear: LGCIU 1 not available, LGCIU 2 up", rows);
        Assert.Contains("right gear: LGCIU 1 up, LGCIU 2 not available", rows);
        Assert.Equal(
            new[] { "field 143.1.2 has an unknown value 7", "field 143.2.1 has an unknown value 7", "field 143.3.2 has an unknown value 7" },
            reported);
    }

    [Fact]
    public void A_gear_is_said_once_while_both_lgcius_agree()
    {
        var reported = new List<string>();

        // Both agree, for each of up, in transit and down (down is the absent field).
        Assert.Contains("nose gear: up", RowsOf(WheelPush(gears: Gears(2, 2, 1, 1, 0, 0)), reported));
        IReadOnlyList<string> mixed = RowsOf(WheelPush(gears: Gears(2, 2, 1, 1, 0, 0)), reported);
        Assert.Contains("left gear: in transit", mixed);
        Assert.Contains("right gear: down", mixed);
        Assert.Empty(reported);

        // They differ: each is named.
        Assert.Contains("nose gear: LGCIU 1 down, LGCIU 2 in transit",
            RowsOf(WheelPush(gears: Gears(0, 1)), reported));
        Assert.Empty(reported);

        // One unknown: named, reported once.
        Assert.Contains("nose gear: LGCIU 1 up, LGCIU 2 not available", RowsOf(WheelPush(gears: Gears(2, 7)), reported));
        Assert.Equal(new[] { "field 143.1.2 has an unknown value 7" }, reported);

        // Both unknown: one phrase, both fields reported.
        var both = new List<string>();
        Assert.Contains("nose gear: not available", RowsOf(WheelPush(gears: Gears(7, 9)), both));
        Assert.Equal(new[] { "field 143.1.1 has an unknown value 7", "field 143.1.2 has an unknown value 9" }, both);
    }

    [Fact]
    public void An_unmeasured_gear_value_reads_not_available()
    {
        var reported = new List<string>();

        IReadOnlyList<string> rows = RowsOf(WheelPush(gears: Gears(nose1: 7)), reported);

        Assert.Contains("nose gear: LGCIU 1 not available, LGCIU 2 up", rows);
        Assert.Equal(new[] { "field 143.1.1 has an unknown value 7" }, reported);

        // A decimal value is written as sent, without needless decimals otherwise; the page goes on.
        var more = new List<string>();
        IReadOnlyList<string> withDoor = RowsOf(WheelPush(gears: Gears(left2: 2.5), doors: Doubles(7, 1, 3)), more);
        Assert.Contains("left gear: LGCIU 1 up, LGCIU 2 not available", withDoor);
        Assert.Contains("nose gear door: not available", withDoor);
        Assert.Contains("left gear door: closed", withDoor);
        Assert.Contains("right gear door: not available", withDoor);
        Assert.Equal(
            new[] { "field 144.1 has an unknown value 7", "field 143.2.2 has an unknown value 2.5", "field 144.3 has an unknown value 3" },
            more);
        Assert.Contains("wheel 1: brake 20 °C", withDoor);
    }

    [Fact]
    public void The_gear_extension_reads_in_transit_then_down()
    {
        // The 22:44:48 push shape (gear extension, 2026-11-03, A321): every LGCIU 1.0, the doors an EMPTY message (all 0).
        var reported = new List<string>();
        IReadOnlyList<string> transit = RowsOf(
            WheelPush(gears: Gears(1, 1, 1, 1, 1, 1), doors: new FenixChannelProtoWriter()), reported);

        Assert.Contains("nose gear: in transit", transit);
        Assert.Contains("left gear: in transit", transit);
        Assert.Contains("right gear: in transit", transit);
        Assert.Contains("nose gear door: open", transit);
        Assert.Contains("left gear door: open", transit);
        Assert.Contains("right gear door: open", transit);

        // The 22:45:05 push shape: 143.1 to .3 EMPTY messages (down is the absent field), the doors closed again.
        IReadOnlyList<string> down = RowsOf(WheelPush(gears: GearsDown(), doors: Doubles(1, 1, 1)), reported);

        Assert.Contains("nose gear: down", down);
        Assert.Contains("left gear: down", down);
        Assert.Contains("right gear: down", down);
        Assert.Contains("nose gear door: closed", down);
        Assert.Contains("left gear door: closed", down);
        Assert.Contains("right gear door: closed", down);
        Assert.Empty(reported);
    }

    [Fact]
    public void A_door_value_of_two_reads_open()
    {
        var reported = new List<string>();

        IReadOnlyList<string> rows = RowsOf(WheelPush(doors: Doubles(2, 2, 2)), reported);

        Assert.Contains("nose gear door: open", rows);
        Assert.Contains("left gear door: open", rows);
        Assert.Contains("right gear door: open", rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void Tire_pressures_show_only_with_tpis()
    {
        IReadOnlyList<string> without = RowsOf(WheelPush(Flags(), includeTires: false));
        Assert.DoesNotContain(without, row => row.Contains("tire"));
        Assert.Contains("wheel 1: brake 20 °C", without);

        // With the indicators fitted, 139 is required.
        var reported = new List<string>();
        FenixChannelRows missing = FenixChannelSdPages.WheelRows(WheelPush(Flags(19), includeTires: false).Context(reported.Add));
        Assert.Equal("no field 139", missing.Problem);

        IReadOnlyList<string> with = RowsOf(WheelPush(Flags(19), tires: Doubles(164.6, 165.4, 210.5, 205, 0, 99.5)));
        Assert.Equal(
            new[]
            {
                "nose tires: left 165 PSI, right 165 PSI", "wheel 1: brake 20 °C, tire 211 PSI",
                "wheel 2: brake 21 °C, tire 205 PSI", "wheel 3: brake 19 °C, tire 0 PSI", "wheel 4: brake 20 °C, tire 100 PSI"
            },
            with.Where(row => row.Contains("tire")));
    }

    [Fact]
    public void A_released_brake_ends_its_wheel_row()
    {
        IReadOnlyList<string> none = RowsOf(WheelPush());
        Assert.DoesNotContain(none, row => row.Contains("released") || row.StartsWith("REL"));

        Assert.Contains("wheel 1: brake 20 °C, tire 210 PSI, released", RowsOf(WheelPush(Flags(19), released: Flags(1, 2))));
        Assert.Contains("wheel 2: brake 21 °C, tire 205 PSI, released", RowsOf(WheelPush(Flags(19), released: Flags(1, 2))));
        Assert.Equal(2, RowsOf(WheelPush(released: Flags(4, 2))).Count(row => row.EndsWith(", released")));
        Assert.Equal(4, RowsOf(WheelPush(released: Flags(1, 2, 3, 4))).Count(row => row.EndsWith(", released")));
        Assert.Contains("wheel 3: brake 19 °C, released", RowsOf(WheelPush(released: Flags(3))));
    }

    [Fact]
    public void Each_wheel_reads_its_own_brake_tire_and_release()
    {
        var expected = new[]
        {
            "nose tires: left 165 PSI, right 166 PSI", "wheel 1: brake 179 °C, tire 210 PSI",
            "wheel 2: brake 204 °C, tire 205 PSI, released", "wheel 3: brake 184 °C, tire 211 PSI, released",
            "wheel 4: brake 193 °C, tire 206 PSI"
        };
        IReadOnlyList<string> rows = RowsOf(WheelPush(Flags(19), Doubles(179, 204, 184, 193),
            Doubles(165, 166, 210, 205, 211, 206), Flags(2, 3)));

        List<string> list = rows.ToList();
        Assert.Equal(expected, list.GetRange(list.IndexOf(expected[0]), 5));
        Assert.DoesNotContain(rows, row => row.StartsWith("brakes") || row.StartsWith("REL") || row.EndsWith("tires PSI"));

        IReadOnlyList<string> bare = RowsOf(WheelPush(Flags(), Doubles(179, 204, 184, 193), released: Flags(2, 3), includeTires: false));
        Assert.Equal(
            new[]
            {
                "wheel 1: brake 179 °C", "wheel 2: brake 204 °C, released", "wheel 3: brake 184 °C, released", "wheel 4: brake 193 °C"
            },
            bare.Where(row => row.StartsWith("wheel")));
        Assert.DoesNotContain(bare, row => row.StartsWith("nose tires"));
    }

    [Fact]
    public void Missing_messages_are_not_available()
    {
        foreach (int field in new[] { 137, 138, 141, 143, 144 })
        {
            var reported = new List<string>();
            SdPush push = new SdPush { Page = 12 }.With(remote =>
            {
                if (field != 137) remote.Message(137, Flags());
                if (field != 138) remote.Message(138, Doubles(1, 2, 3, 4));
                remote.Message(140, new FenixChannelProtoWriter());
                if (field != 141) remote.Message(141, new FenixChannelProtoWriter());
                if (field != 143) remote.Message(143, Gears());
                if (field != 144) remote.Message(144, Doubles(1, 1, 1));
            });

            Assert.Equal($"no field {field}", FenixChannelSdPages.WheelRows(push.Context(reported.Add)).Problem);
            Assert.Empty(reported);
        }

        foreach (int gear in new[] { 1, 2, 3 })
        {
            var partial = new FenixChannelProtoWriter();
            foreach (int other in new[] { 1, 2, 3 }.Where(g => g != gear)) partial.Message(other, Doubles(2, 2));
            SdPush push = WheelPush(gears: partial);

            Assert.Equal($"no field 143.{gear}", FenixChannelSdPages.WheelRows(push.Context(_ => { })).Problem);
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Wheel_rows_are_the_same_in_every_culture(string culture)
    {
        var reported = new List<string>();
        SdPush push = WheelPush(Flags(19), temps: Doubles(-0.4, 20.5, 1234.5, double.NaN), gears: Gears(left2: 2.5));

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => RowsOf(push, reported));

        Assert.Contains("nose tires: left 165 PSI, right 165 PSI", rows);
        Assert.Contains("wheel 1: brake 0 °C, tire 210 PSI", rows);
        Assert.Contains("wheel 2: brake 21 °C, tire 205 PSI", rows);
        Assert.Contains("wheel 3: brake 1235 °C, tire 210 PSI", rows);
        Assert.Contains("wheel 4: brake XX °C, tire 205 PSI", rows);
        Assert.Contains("left gear: LGCIU 1 up, LGCIU 2 not available", rows);
        Assert.Equal(new[] { "field 143.2.2 has an unknown value 2.5" }, reported);
        Assert.DoesNotContain(rows, row => row.Contains('−'));
    }
}
