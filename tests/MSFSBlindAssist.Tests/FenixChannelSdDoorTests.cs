// Characterization tests for the Fenix SD's DOOR page (FenixChannelSdPages.DoorRows, seam ComposeDoor, wired by a later task).
// The pushes are built with SdPush (FenixChannelSdWindowTests.cs) and the composer is called directly, so every field
// number is written out here, never read from FenixChannelFields.
//
// The contract: the page reads 179 (the doors, all varint bools: a set door field is closed, a clear one open, a set slide
// field armed) and 180 (crew oxygen: 180.2 SDAC has data, 180.5 pressure). OXY: n PSI while 180.2, else OXY: XX PSI. The
// airframe is the variant's Type, never a field: A319, A320 and A321 have door tables (AVIONIC 1-3 = 179.1-3, Door 1 left
// .4 slide .16, Door 1 right .5 slide .17, AVIONIC 4 .6, Forward cargo door .7, the exits, Aft cargo door .12, Bulk cargo
// door .13 only while 179.27, Door 4 left .14 slide .20, Door 4 right .15 slide .21; named as the cabin window names
// them); the exits are inferred (Task 1 measures them) and read no slide.
// An unknown airframe and the A318 are "airframe not known", before any field is read.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdDoorTests
{
    private static readonly int[] AllDoorFields = Enumerable.Range(1, 25).ToArray();

    // The doors (179): each listed field written set; 26 (double overwing) and 27 (bulk door) by flag.
    private static FenixChannelProtoWriter Doors(IEnumerable<int> set, bool doubleOverwing = false, bool bulkDoor = false)
    {
        var doors = new FenixChannelProtoWriter();
        foreach (int field in set.OrderBy(f => f)) doors.Bool(field, true);
        if (doubleOverwing) doors.Bool(26, true);
        if (bulkDoor) doors.Bool(27, true);
        return doors;
    }

    private static FenixChannelProtoWriter Oxygen(bool data = true, double pressure = 1800.0)
        => new FenixChannelProtoWriter().Bool(2, data).Double(5, pressure);

    private static SdPush DoorPush(FenixChannelProtoWriter? doors, FenixChannelProtoWriter? oxygen,
        FenixAircraftType type = FenixAircraftType.A320)
        => new SdPush { Page = 6, Variant = FenixVariant.Unknown with { Type = type, Engine = FenixEngineFamily.Cfm56 } }.With(remote =>
        {
            if (doors is not null) remote.Message(179, doors);
            if (oxygen is not null) remote.Message(180, oxygen);
        });

    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.DoorRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    [Fact]
    public void The_a320_door_page_lists_its_doors_nose_to_tail()
    {
        Assert.Equal(
            new[]
            {
                "OXY: 1800 PSI", "AVIONIC 1: closed", "AVIONIC 2: closed", "AVIONIC 3: closed",
                "Door 1 left: closed, SLIDE armed", "Door 1 right: closed, SLIDE armed", "AVIONIC 4: closed",
                "Forward cargo door: closed",
                "Overwing exit left: closed", "Overwing exit right: closed", "Aft cargo door: closed",
                "Door 4 left: closed, SLIDE armed", "Door 4 right: closed, SLIDE armed"
            },
            RowsOf(DoorPush(Doors(AllDoorFields), Oxygen())));
    }

    [Fact]
    public void Only_the_airframes_own_doors_are_listed()
    {
        // The measured hazard: every field set, every slide armed, overwing slides included.
        IReadOnlyList<string> a321 = RowsOf(DoorPush(Doors(AllDoorFields), Oxygen(), FenixAircraftType.A321));
        Assert.Contains("Door 2 left: closed", a321);
        Assert.Contains("Door 2 right: closed", a321);
        Assert.Contains("Door 3 left: closed", a321);
        Assert.Contains("Door 3 right: closed", a321);
        Assert.DoesNotContain(a321, row => row.StartsWith("Overwing exit"));
        Assert.Equal(15, a321.Count);

        IReadOnlyList<string> a319 = RowsOf(DoorPush(Doors(AllDoorFields), Oxygen(), FenixAircraftType.A319));
        Assert.Equal(new[] { "Overwing exit left: closed", "Overwing exit right: closed" },
            a319.Where(r => r.StartsWith("Overwing exit")));

        IReadOnlyList<string> doubleExits = RowsOf(DoorPush(Doors(AllDoorFields, doubleOverwing: true), Oxygen(), FenixAircraftType.A319));
        Assert.Equal(
            new[]
            {
                "Overwing exit 1 left: closed", "Overwing exit 1 right: closed",
                "Overwing exit 2 left: closed", "Overwing exit 2 right: closed"
            },
            doubleExits.Where(r => r.StartsWith("Overwing exit")));

        // The overwing slides (22 to 25) never appear, and no exit reads a slide.
        foreach (IReadOnlyList<string> rows in new[] { a321, a319, doubleExits })
        {
            Assert.DoesNotContain(rows, row => (row.StartsWith("Overwing exit") || row.StartsWith("Door 2") || row.StartsWith("Door 3"))
                && row.Contains("SLIDE"));
        }
    }

    [Fact]
    public void Open_doors_and_disarmed_slides_read_so()
    {
        // Everything set except the forward left entry (4), the forward cargo (7) and the aft right slide (21).
        IEnumerable<int> set = AllDoorFields.Except([4, 7, 21]);

        IReadOnlyList<string> rows = RowsOf(DoorPush(Doors(set), Oxygen()));

        Assert.Contains("Door 1 left: open, SLIDE armed", rows);
        Assert.Contains("Forward cargo door: open", rows);
        Assert.Contains("Door 4 right: closed, SLIDE disarmed", rows);
        Assert.Contains("Door 1 right: closed, SLIDE armed", rows);
    }

    [Fact]
    public void Bulk_shows_only_on_an_airframe_with_one()
    {
        IReadOnlyList<string> without = RowsOf(DoorPush(Doors(AllDoorFields), Oxygen()));
        Assert.DoesNotContain(without, row => row.StartsWith("Bulk"));

        IReadOnlyList<string> closed = RowsOf(DoorPush(Doors(AllDoorFields, bulkDoor: true), Oxygen()));
        Assert.Equal(
            new[] { "Aft cargo door: closed", "Bulk cargo door: closed", "Door 4 left: closed, SLIDE armed" },
            closed.SkipWhile(r => !r.StartsWith("Aft cargo")).Take(3));

        IReadOnlyList<string> open = RowsOf(DoorPush(Doors(AllDoorFields.Except([13]), bulkDoor: true), Oxygen()));
        Assert.Contains("Bulk cargo door: open", open);
    }

    [Fact]
    public void Oxy_reads_XX_without_sdac_data()
    {
        Assert.Equal("OXY: XX PSI", RowsOf(DoorPush(Doors(AllDoorFields), Oxygen(data: false)))[0]);
        // A NaN pressure with data reads XX as every number does (Whole), keeping the unit.
        Assert.Equal("OXY: XX PSI", RowsOf(DoorPush(Doors(AllDoorFields), Oxygen(pressure: double.NaN)))[0]);
        Assert.Equal("OXY: 1799 PSI", RowsOf(DoorPush(Doors(AllDoorFields), Oxygen(pressure: 1798.6)))[0]);
    }

    [Fact]
    public void A_missing_message_is_not_available()
    {
        var reported = new List<string>();

        Assert.Equal("no field 179",
            FenixChannelSdPages.DoorRows(DoorPush(null, Oxygen()).Context(reported.Add)).Problem);
        Assert.Equal("no field 180",
            FenixChannelSdPages.DoorRows(DoorPush(Doors(AllDoorFields), null).Context(reported.Add)).Problem);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData(FenixAircraftType.Unknown)]
    [InlineData(FenixAircraftType.A318)]
    public void An_unknown_airframe_is_not_available(FenixAircraftType type)
    {
        // The airframe is checked first: even a push without its messages reads so.
        var reported = new List<string>();

        FenixChannelRows full = FenixChannelSdPages.DoorRows(DoorPush(Doors(AllDoorFields), Oxygen(), type).Context(reported.Add));
        FenixChannelRows empty = FenixChannelSdPages.DoorRows(DoorPush(null, null, type).Context(reported.Add));

        Assert.Equal("airframe not known", full.Problem);
        Assert.Equal("airframe not known", empty.Problem);
        Assert.Null(full.Rows);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Door_rows_are_the_same_in_every_culture(string culture)
    {
        SdPush push = DoorPush(Doors(AllDoorFields), Oxygen(pressure: 1234.5));

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => RowsOf(push));

        Assert.Equal("OXY: 1235 PSI", rows[0]);
        Assert.DoesNotContain(rows, row => row.Contains('−'));
    }
}
