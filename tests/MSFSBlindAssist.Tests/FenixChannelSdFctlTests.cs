// Characterization tests for the Fenix SD's F/CTL page (FenixChannelSdPages.FctlRows, seam ComposeFctl, wired by a later
// task). The pushes are built with SdPush (FenixChannelSdWindowTests.cs) and the composer is called directly, so every field
// number is written out here, never read from FenixChannelFields.
//
// The contract: the page reads 145 (the surfaces; each a nested message, position 1 a double and fault 2 a bool: 1 left
// aileron, 2-6 left spoilers 1-5, 7 right aileron, 8-12 right spoilers 1-5, 13 left elevator, 14 right elevator, 15 THS,
// 16 rudder), 146 (the rudder trim, 2 a double), 147 (3: the FCDC has data) and 148 (the computers, 1-2 ELAC, 3-5 SEC, bools).
// The signs were measured in flight on 2026-11-03: ailerons and elevators negative = up, the THS negative = nose
// up, the rudder and its trim positive = right.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdFctlTests
{
    // One SurfaceState: null leaves the message empty (the Fenix leaves a 0 position out).
    private static FenixChannelProtoWriter Surface(double? position = null, bool fault = false)
    {
        var surface = new FenixChannelProtoWriter();
        if (position is { } value) surface.Double(1, value);
        if (fault) surface.Bool(2, true);
        return surface;
    }

    private static SdPush FctlPush(
        Dictionary<int, FenixChannelProtoWriter>? surfaces = null, double rudderTrim = 0.0524, bool fcdc = true,
        int[]? computers = null, int[]? skip145 = null, bool include146 = true, bool include147 = true, bool include148 = true,
        bool include145 = true)
        => new SdPush { Page = 9 }.With(remote =>
        {
            if (include145)
            {
                var all = new FenixChannelProtoWriter();
                for (int field = 1; field <= 16; field++)
                {
                    if (skip145?.Contains(field) == true) continue;
                    all.Message(field, surfaces is not null && surfaces.TryGetValue(field, out FenixChannelProtoWriter? set) ? set : Surface());
                }

                remote.Message(145, all);
            }

            if (include146) remote.Message(146, new FenixChannelProtoWriter().Double(1, 4.2).Double(2, rudderTrim));
            if (include147)
            {
                var status = new FenixChannelProtoWriter();
                if (fcdc) status.Bool(3, true);
                remote.Message(147, status);
            }

            if (include148)
            {
                var fcc = new FenixChannelProtoWriter();
                foreach (int field in computers ?? [1, 2, 3, 4, 5]) fcc.Bool(field, true);
                remote.Message(148, fcc);
            }
        });

    private static IReadOnlyList<string> RowsOf(SdPush push, List<string>? reported = null)
    {
        reported ??= [];
        FenixChannelRows rows = FenixChannelSdPages.FctlRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        return rows.Rows!;
    }

    private static string RowOf(IReadOnlyList<string> rows, string label)
        => Assert.Single(rows, row => row.StartsWith(label + ":") || row.StartsWith(label + ","));

    [Fact]
    public void The_fctl_page_reads_spoilers_surfaces_and_computers()
    {
        // A real cruise push (2026-11-03, A321): the spoilers present and empty.
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(0.0519), [7] = Surface(-0.0519), [13] = Surface(0.0112), [14] = Surface(0.0112),
            [15] = Surface(-1.6117), [16] = Surface(0.0524)
        };
        var reported = new List<string>();

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces), reported);

        Assert.Equal(
            new[]
            {
                "SPD BRK left: all 0°", "SPD BRK right: all 0°", "L AIL: 0.1° down", "R AIL: 0.1° up",
                "ELAC 1: normal", "ELAC 2: normal", "SEC 1: normal", "SEC 2: normal", "SEC 3: normal",
                "PITCH TRIM: 1.6° UP", "L ELEV: 0.0°", "R ELEV: 0.0°", "RUD: 0.1° right", "RUD TRIM: 0.1° right"
            },
            rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void The_spoilers_read_in_the_contracts_order_as_whole_numbers()
    {
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [2] = Surface(1.4), [3] = Surface(2.5), [4] = Surface(3), [5] = Surface(4), [6] = Surface(5),
            [8] = Surface(6), [9] = Surface(7), [10] = Surface(8), [11] = Surface(9), [12] = Surface(10.6)
        };

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces));

        Assert.Equal("SPD BRK left, 1 to 5: 1°, 3°, 3°, 4°, 5°", RowOf(rows, "SPD BRK left"));
        Assert.Equal("SPD BRK right, 1 to 5: 6°, 7°, 8°, 9°, 11°", RowOf(rows, "SPD BRK right"));
    }

    [Fact]
    public void Spoilers_name_their_order_or_fold_when_equal()
    {
        // Spoiler 1 is the inboard one and 5 the outboard (the A320's numbering; the contract's order).
        var speedBrake = new Dictionary<int, FenixChannelProtoWriter>
        {
            [2] = Surface(0), [3] = Surface(25), [4] = Surface(25), [5] = Surface(25), [6] = Surface(0),
            [8] = Surface(12, fault: true), [9] = Surface(12, fault: true), [10] = Surface(12, fault: true),
            [11] = Surface(12, fault: true), [12] = Surface(12, fault: true)
        };

        IReadOnlyList<string> rows = RowsOf(FctlPush(speedBrake));

        Assert.Equal("SPD BRK left, 1 to 5: 0°, 25°, 25°, 25°, 0°", RowOf(rows, "SPD BRK left"));
        Assert.Equal("SPD BRK right: all 12° fault", RowOf(rows, "SPD BRK right"));

        // One differing fault is not folded.
        IReadOnlyList<string> oneFault = RowsOf(FctlPush(new() { [4] = Surface(0, fault: true) }));
        Assert.Equal("SPD BRK left, 1 to 5: 0°, 0°, 0° fault, 0°, 0°", RowOf(oneFault, "SPD BRK left"));
        Assert.Equal("SPD BRK right: all 0°", RowOf(oneFault, "SPD BRK right"));

        // No FCDC data: five crosses fold too.
        IReadOnlyList<string> crosses = RowsOf(FctlPush(fcdc: false));
        Assert.Equal("SPD BRK left: all XX", RowOf(crosses, "SPD BRK left"));
        Assert.Equal("SPD BRK right: all XX", RowOf(crosses, "SPD BRK right"));
    }

    [Fact]
    public void The_landing_push_reads_the_ground_spoilers()
    {
        // The real touchdown push of 22:47:25 (A321, 2026-11-03, CYYZ 24): all ten spoilers at their 50 degree maximum.
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(-25.0), [2] = Surface(50.0), [3] = Surface(50.0), [4] = Surface(50.0), [5] = Surface(50.0),
            [6] = Surface(50.0), [7] = Surface(-25.0), [8] = Surface(50.0), [9] = Surface(50.0), [10] = Surface(50.0),
            [11] = Surface(50.0), [12] = Surface(50.0), [13] = Surface(15.0), [14] = Surface(15.0), [15] = Surface(-0.0078),
            [16] = Surface(-0.9042)
        };
        var reported = new List<string>();

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces, rudderTrim: 0), reported);

        Assert.Equal(
            new[]
            {
                "SPD BRK left: all 50°", "SPD BRK right: all 50°", "L AIL: 25.0° up",
                "R AIL: 25.0° up", "ELAC 1: normal", "ELAC 2: normal", "SEC 1: normal", "SEC 2: normal", "SEC 3: normal",
                "PITCH TRIM: 0.0°", "L ELEV: 15.0° down", "R ELEV: 15.0° down", "RUD: 0.9° left", "RUD TRIM: 0.0°"
            },
            rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void Computers_read_normal_or_fault()
    {
        IReadOnlyList<string> rows = RowsOf(FctlPush(computers: [2, 4]));

        Assert.Equal("ELAC 1: fault", RowOf(rows, "ELAC 1"));
        Assert.Equal("ELAC 2: normal", RowOf(rows, "ELAC 2"));
        Assert.Equal("SEC 1: fault", RowOf(rows, "SEC 1"));
        Assert.Equal("SEC 2: normal", RowOf(rows, "SEC 2"));
        Assert.Equal("SEC 3: fault", RowOf(rows, "SEC 3"));
    }

    [Fact]
    public void Pitch_trim_negative_is_up()
    {
        IReadOnlyList<string> up = RowsOf(FctlPush(new() { [15] = Surface(-1.94) }));
        IReadOnlyList<string> down = RowsOf(FctlPush(new() { [15] = Surface(0.62) }));
        IReadOnlyList<string> zero = RowsOf(FctlPush(new() { [15] = Surface(-0.04) }));

        Assert.Equal("PITCH TRIM: 1.9° UP", RowOf(up, "PITCH TRIM"));
        Assert.Equal("PITCH TRIM: 0.6° DN", RowOf(down, "PITCH TRIM"));
        Assert.Equal("PITCH TRIM: 0.0°", RowOf(zero, "PITCH TRIM"));
    }

    [Fact]
    public void Surface_directions_follow_the_measured_signs()
    {
        // Measured in flight on 2026-11-03: surfaces negative = up; the rudder and its trim positive = right.
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(-2.34), [7] = Surface(2.34), [13] = Surface(-11.96), [14] = Surface(11.96), [16] = Surface(-3.25)
        };

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces, rudderTrim: -0.76));

        Assert.Equal("L AIL: 2.3° up", RowOf(rows, "L AIL"));
        Assert.Equal("R AIL: 2.3° down", RowOf(rows, "R AIL"));
        Assert.Equal("L ELEV: 12.0° up", RowOf(rows, "L ELEV"));
        Assert.Equal("R ELEV: 12.0° down", RowOf(rows, "R ELEV"));
        Assert.Equal("RUD: 3.3° left", RowOf(rows, "RUD"));
        Assert.Equal("RUD TRIM: 0.8° left", RowOf(rows, "RUD TRIM"));
    }

    [Fact]
    public void A_faulted_surface_says_fault()
    {
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(-2.34, fault: true), [3] = Surface(12, fault: true), [15] = Surface(-1.94, fault: true),
            [16] = Surface(0, fault: true)
        };

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces));

        Assert.Equal("L AIL: 2.3° up, fault", RowOf(rows, "L AIL"));
        Assert.Equal("SPD BRK left, 1 to 5: 0°, 12° fault, 0°, 0°, 0°", RowOf(rows, "SPD BRK left"));
        Assert.Equal("PITCH TRIM: 1.9° UP, fault", RowOf(rows, "PITCH TRIM"));
        Assert.Equal("RUD: 0.0°, fault", RowOf(rows, "RUD"));
        Assert.Equal("R AIL: 0.0°", RowOf(rows, "R AIL"));
    }

    [Fact]
    public void Without_fcdc_data_surfaces_read_XX()
    {
        var surfaces = new Dictionary<int, FenixChannelProtoWriter> { [1] = Surface(-2.34), [2] = Surface(4) };

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces, fcdc: false, computers: [1]));

        Assert.Equal(
            new[]
            {
                "SPD BRK left: all XX", "SPD BRK right: all XX", "L AIL: XX", "R AIL: XX",
                "ELAC 1: normal", "ELAC 2: fault", "SEC 1: fault", "SEC 2: fault", "SEC 3: fault",
                "PITCH TRIM: XX", "L ELEV: XX", "R ELEV: XX", "RUD: XX", "RUD TRIM: 0.1° right"
            },
            rows);
    }

    [Fact]
    public void A_nan_position_reads_XX()
    {
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(double.NaN), [4] = Surface(double.NaN), [15] = Surface(double.NaN)
        };

        IReadOnlyList<string> rows = RowsOf(FctlPush(surfaces, rudderTrim: double.NaN));

        Assert.Equal("L AIL: XX", RowOf(rows, "L AIL"));
        Assert.Equal("SPD BRK left, 1 to 5: 0°, 0°, XX, 0°, 0°", RowOf(rows, "SPD BRK left"));
        Assert.Equal("PITCH TRIM: XX", RowOf(rows, "PITCH TRIM"));
        Assert.Equal("RUD TRIM: XX", RowOf(rows, "RUD TRIM"));
    }

    [Theory]
    [InlineData(145, "no field 145")]
    [InlineData(146, "no field 146")]
    [InlineData(147, "no field 147")]
    [InlineData(148, "no field 148")]
    public void A_missing_message_makes_the_page_not_available(int message, string problem)
    {
        SdPush push = FctlPush(include145: message != 145, include146: message != 146, include147: message != 147,
            include148: message != 148);

        FenixChannelRows rows = FenixChannelSdPages.FctlRows(push.Context(_ => { }));

        Assert.Equal(problem, rows.Problem);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(16)]
    public void A_missing_surface_makes_the_page_not_available(int surface)
    {
        FenixChannelRows rows = FenixChannelSdPages.FctlRows(FctlPush(skip145: [surface]).Context(_ => { }));

        Assert.Equal($"no field 145.{surface}", rows.Problem);
    }

    [Fact]
    public void Fctl_rows_are_the_same_in_every_culture()
    {
        var surfaces = new Dictionary<int, FenixChannelProtoWriter>
        {
            [1] = Surface(-2.34), [3] = Surface(12.5), [13] = Surface(-0.04), [15] = Surface(-1.94), [16] = Surface(3.25)
        };
        IReadOnlyList<string> expected = RowsOf(FctlPush(surfaces));

        foreach (string name in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(expected, TestCulture.Run(name, () => RowsOf(FctlPush(surfaces))));
        }

        Assert.Equal("L AIL: 2.3° up", RowOf(expected, "L AIL"));
        Assert.Equal("L ELEV: 0.0°", RowOf(expected, "L ELEV"));
        Assert.Equal("RUD: 3.3° right", RowOf(expected, "RUD"));
    }
}
