// Characterization tests for FenixChannelEwdRows, which turns one snapshot of the Fenix display channel into the rows of
// the E/WD window. Pure logic: every snapshot is built here with FenixChannelProtoWriter, the field numbers written out
// literally and never read from FenixChannelFields or a capture, so a wrong number in the app's table fails a test.
//
// The contract: the rows run the engine block, the thrust limit, the FOB line, the flap text (labeled "Flaps: "), the
// upper ECAM's left lines and then its right lines, and a text with nothing to say gives no row. The engine block is one
// row for each gauge, "<label>: <engine 1>, <engine 2>", in the order the E/WD shows them (measured on the installed
// build 3.0.0.7613): N1, EGT, N2 and fuel flow for the CFM56, and EPR, fuel flow, EGT, N2 and N1 for the IAE V2500. The
// LEAP and the GTF get the CFM56's block unmeasured, since no NEO exists on 3.0.0.7613; an engine family not read yet
// gets one status row. N1, N2 and EPR are truncated to 1, 1 and 3 decimals, EGT and fuel flow are rounded to none, a
// value the display cannot draw reads "XX", and a minus is ASCII whatever the PC's culture is. A problem makes the
// window not available and is reported.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelEwdRowsTests
{
    private const FenixEngineFamily Iae = FenixEngineFamily.IaeV2500;

    // The rows of the engine block of a CFM56, which AfterBlock leaves out.
    private const int TurbofanBlockRows = 4;

    // One engine: only the values given are sent, so a value left out reads as the protobuf leaves it, 0.
    private static FenixChannelProtoWriter Engine(
        double? n1 = null, double? n2 = null, double? ff = null, double? epr = null, double? egt = null)
    {
        var engine = new FenixChannelProtoWriter();
        if (n1 is { } n1Value) engine.Double(2, n1Value);
        if (n2 is { } n2Value) engine.Double(3, n2Value);
        if (ff is { } ffValue) engine.Double(4, ffValue);
        if (epr is { } eprValue) engine.Double(5, eprValue);
        if (egt is { } egtValue) engine.Double(6, egtValue);
        return engine;
    }

    // The cruise values of the two engines: N1 near 81, N2 near 86, a fuel flow of 1320, an EPR near 1.41 and an EGT near
    // 377 and 378.
    private static FenixChannelProtoWriter LeftCruise() => Engine(81.0801, 86.1461, 1320, 1.4082, 376.7972);

    private static FenixChannelProtoWriter RightCruise() => Engine(81.2561, 86.3052, 1320, 1.407, 378.4421);

    // One element of the thrust limit: a message whose field 1 is the string, or one without it for a null string.
    private static FenixChannelProtoWriter Limit(string? text)
        => text is null ? new FenixChannelProtoWriter() : new FenixChannelProtoWriter().String(1, text);

    private static readonly string?[] CruiseLimit = ["cCLB", "g1.618", null];

    // The RemoteData message of a push: both engines, the thrust limit, the flap text when there is one and the weight
    // unit (154) when it is kilograms.
    private static FenixChannelProtoWriter Remote(
        FenixChannelProtoWriter left, FenixChannelProtoWriter right, string?[] limit, bool kg = true, string? flap = null)
    {
        var engines = new FenixChannelProtoWriter().Message(1, left).Message(2, right);
        foreach (string? element in limit)
        {
            engines.Message(8, Limit(element));
        }

        var remote = new FenixChannelProtoWriter().Message(129, engines);
        if (kg) remote.Bool(154, true);
        if (flap is not null) remote.String(152, flap);
        return remote;
    }

    private static FenixChannelSnapshot SnapshotOf(
        FenixChannelProtoWriter remote, string fob = "", string[]? upperLeft = null, string[]? upperRight = null)
    {
        FenixChannelPush push = FenixChannelPush.From(remote.ToArray())
            ?? throw new InvalidOperationException("the test's RemoteData is not valid protobuf");
        var frame = new FenixChannelFrame(false, [], FenixFdState.Default, [], upperLeft ?? [], upperRight ?? [], 0, 0, 0, fob)
        {
            EwdLive = true
        };

        return new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), frame, push, null, 0, 1);
    }

    // The cruise, with the parts a test varies. The FOB line and the right upper ECAM line are the ones measured.
    private static FenixChannelSnapshot Cruise(
        FenixChannelProtoWriter? left = null, FenixChannelProtoWriter? right = null, string?[]? limit = null,
        bool kg = true, string? flap = null, string fob = "wFOB:   g8140 cs KG", string[]? upperLeft = null,
        string[]? upperRight = null)
        => SnapshotOf(Remote(left ?? LeftCruise(), right ?? RightCruise(), limit ?? CruiseLimit, kg, flap), fob, upperLeft,
            upperRight ?? ["gCOMPANY MSG"]);

    private static IReadOnlyList<string> RowsOf(FenixChannelSnapshot snapshot, FenixEngineFamily family = Iae)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelEwdRows.Compose(snapshot, family, reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // The rows after a CFM56's engine block: the thrust limit, the FOB line, the flap text and the upper ECAM.
    private static IReadOnlyList<string> AfterBlock(FenixChannelSnapshot snapshot)
        => RowsOf(snapshot, FenixEngineFamily.Cfm56).Skip(TurbofanBlockRows).ToList();

    // The one problem a snapshot gives: the status row's, and the single line the report got.
    private static string ProblemOf(FenixChannelSnapshot snapshot, FenixEngineFamily family = Iae)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelEwdRows.Compose(snapshot, family, reported.Add);

        Assert.Null(rows.Rows);
        Assert.NotNull(rows.Problem);
        Assert.Equal(new[] { rows.Problem }, reported);
        return rows.Problem!;
    }

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The cruise ---------------------------------------------------------------------------

    private static readonly string[] CruiseRows =
    [
        "EPR: 1.408, 1.407", "FF KG/H: 1320, 1320", "EGT °C: 377, 378", "N2 %: 86.1, 86.3", "N1 %: 81.0, 81.2",
        "CLB cyan, 1.618 green", "FOB: white, 8140 green, KG cyan", "COMPANY MSG, green"
    ];

    [Fact]
    public void The_cruise_rows_read_as_the_display_draws_them()
    {
        Assert.Equal(CruiseRows, RowsOf(Cruise()));
    }

    [Fact]
    public void The_window_is_called_the_ewd()
    {
        Assert.Equal("E/WD", FenixChannelEwdRows.DisplayName);
    }

    [Theory]
    [InlineData(FenixEngineFamily.Cfm56, "N1 %|EGT °C|N2 %|FF KG/H")]
    [InlineData(FenixEngineFamily.CfmLeap, "N1 %|EGT °C|N2 %|FF KG/H")]
    [InlineData(FenixEngineFamily.PwGtf, "N1 %|EGT °C|N2 %|FF KG/H")]
    [InlineData(FenixEngineFamily.IaeV2500, "EPR|FF KG/H|EGT °C|N2 %|N1 %")]
    public void Each_family_has_its_block(FenixEngineFamily family, string labels)
    {
        // Nothing but the block: no text anywhere, so every row is a gauge.
        FenixChannelSnapshot snapshot = SnapshotOf(Remote(LeftCruise(), RightCruise(), []));

        string[] drawn = RowsOf(snapshot, family).Select(row => row[..row.IndexOf(':')]).ToArray();

        Assert.Equal(labels.Split('|'), drawn);
    }

    [Fact]
    public void The_engine_values_follow_the_engines()
    {
        // Engine 1 is the left engine (129.1) and engine 2 the right one (129.2), in every gauge and on both blocks.
        FenixChannelSnapshot snapshot = SnapshotOf(Remote(
            Engine(n1: 21.5, n2: 58.7, ff: 900, epr: 0.996, egt: 423),
            Engine(n1: 22.5, n2: 59.7, ff: 910, epr: 1.006, egt: 433), []));

        Assert.Equal(
            new[] { "N1 %: 21.5, 22.5", "EGT °C: 423, 433", "N2 %: 58.7, 59.7", "FF KG/H: 900, 910" },
            RowsOf(snapshot, FenixEngineFamily.Cfm56));
        Assert.Equal(
            new[] { "EPR: 0.996, 1.006", "FF KG/H: 900, 910", "EGT °C: 423, 433", "N2 %: 58.7, 59.7", "N1 %: 21.5, 22.5" },
            RowsOf(snapshot, Iae));
    }

    [Theory]
    [InlineData(FenixEngineFamily.Unknown)]
    [InlineData((FenixEngineFamily)99)]
    public void An_unread_engine_type_shows_no_block(FenixEngineFamily family)
    {
        // The status row stands in the block's place and the rest of the window reads as it does with an engine type.
        // It is not a problem of the window: nothing is reported, and the other rows are still there.
        string[] expected = ["Engines: not available, engine type not read yet", .. CruiseRows.Skip(5)];

        Assert.Equal(expected, RowsOf(Cruise(), family));
    }

    // ---- The text rows ------------------------------------------------------------------------

    [Fact]
    public void The_rows_after_the_block_run_thrust_limit_fob_flap_then_the_upper_ecam()
    {
        FenixChannelSnapshot snapshot = Cruise(
            flap: "c1+F", upperLeft: ["urENG 1 FIREu ", "c -GEN 1......OFF THEN ON"], upperRight: ["mT.O INHIBIT", "a*AIR BLEED"]);

        Assert.Equal(
            new[]
            {
                "CLB cyan, 1.618 green", "FOB: white, 8140 green, KG cyan", "Flaps: 1+F, cyan", "ENG 1 FIRE, red",
                "-GEN 1......OFF THEN ON, cyan", "T.O INHIBIT, magenta", "*AIR BLEED, amber"
            },
            AfterBlock(snapshot));
    }

    [Fact]
    public void The_measured_lines_read_with_their_colors()
    {
        // Each upper ECAM line as the display sent it; a blank line gives no row, and the rest keep their order.
        FenixChannelSnapshot lines = Cruise(
            fob: "",
            upperLeft:
            [
                "urENG 1 FIREu ", "c -GEN 1......OFF THEN ON", "", "uaENG 1u afSHUT DOWNf",
                "guT.Ou AUTO BRKc.....MAX    ", "uaNAVu aGPWS TERR DET FAULT", "c -GPWS TERR..........OFF"
            ],
            upperRight: ["mT.O INHIBIT", "a*AIR BLEED", ""],
            limit: [null, null, null]);

        Assert.Equal(
            new[]
            {
                "ENG 1 FIRE, red", "-GEN 1......OFF THEN ON, cyan", "ENG 1 SHUT DOWN, amber",
                "T.O AUTO BRK green, .....MAX cyan", "NAV GPWS TERR DET FAULT, amber", "-GPWS TERR..........OFF, cyan",
                "T.O INHIBIT, magenta", "*AIR BLEED, amber"
            },
            AfterBlock(lines));
    }

    [Fact]
    public void The_thrust_limit_reads_each_element_on_its_own()
    {
        // Each element is parsed alone, so a color never carries from one to the next, and an element without a color
        // letter stays colorless. No row when every element is blank.
        Assert.Equal(new[] { "N1 MODE, TOGA cyan, 95.4 green, % cyan" },
            AfterBlock(Cruise(limit: ["N1 MODE", "cTOGA", "g95.4c%"], fob: "", upperRight: [])));
        Assert.Equal(new[] { "XX XX, amber" },
            AfterBlock(Cruise(limit: ["aXX", "aXX", null], fob: "", upperRight: [])));
        Assert.Empty(AfterBlock(Cruise(limit: [null, "", null], fob: "", upperRight: [])));
    }

    [Fact]
    public void The_flap_text_reads_with_its_label_and_color_and_an_absent_one_gives_no_row()
    {
        // The row is labeled "Flaps: ", since a bare "1+F" says little by ear. A blank text has no row.
        FenixChannelSnapshot Only(string? flap) => Cruise(flap: flap, limit: [], fob: "", upperRight: []);

        Assert.Equal(new[] { "Flaps: 1+F, cyan" }, AfterBlock(Only("c1+F")));
        Assert.Equal(new[] { "Flaps: 2, cyan" }, AfterBlock(Only("c2")));
        Assert.Equal(new[] { "Flaps: FULL, green" }, AfterBlock(Only("gFULL")));
        Assert.Empty(AfterBlock(Only(null)));
        Assert.Empty(AfterBlock(Only("")));
    }

    // ---- Numbers ------------------------------------------------------------------------------

    [Theory]
    [InlineData("truncated", 21.567, 1, "21.5")]
    [InlineData("truncated", 18.2898, 1, "18.2")]
    [InlineData("truncated", 58.7576, 1, "58.7")]
    [InlineData("truncated", 81.25, 1, "81.2")]
    [InlineData("truncated", 0.99693, 3, "0.996")]
    [InlineData("truncated", 1.00978, 3, "1.009")]
    [InlineData("truncated", 1.0625, 3, "1.062")]
    [InlineData("truncated", 1.407, 3, "1.407")]
    [InlineData("rounded", 7.98, 0, "8")]
    [InlineData("rounded", 422.958, 0, "423")]
    [InlineData("rounded", 376.5, 0, "377")]
    [InlineData("rounded", -48.68, 0, "-49")]
    [InlineData("rounded", 900, 0, "900")]
    [InlineData("rounded", -0.4, 0, "0")]
    [InlineData("truncated", -0.04, 1, "0.0")]
    [InlineData("truncated", -0.0004, 3, "0.000")]
    public void Each_value_rounds_or_truncates_as_the_display_draws_it(string how, double value, int decimals, string expected)
    {
        string shown = how == "truncated"
            ? FenixChannelEwdRows.Truncated(value, decimals)
            : FenixChannelEwdRows.Number(value, decimals);

        Assert.Equal(expected, shown);
    }

    [Fact]
    public void The_rows_truncate_the_n2_and_round_the_fuel_flow()
    {
        // The two values nothing else separates, through the rows: the N2 58.7576 reads 58.7 (rounding it would read 58.8)
        // and the fuel flow 899.6 reads 900 (truncating it would read 899).
        FenixChannelSnapshot snapshot = SnapshotOf(Remote(
            Engine(n2: 58.7576, ff: 899.6), Engine(n2: 58.7576, ff: 899.6), []));

        Assert.Equal(
            new[] { "N1 %: 0.0, 0.0", "EGT °C: 0, 0", "N2 %: 58.7, 58.7", "FF KG/H: 900, 900" },
            RowsOf(snapshot, FenixEngineFamily.Cfm56));
    }

    [Fact]
    public void A_negative_value_truncates_toward_zero_and_rounds_away_from_it()
    {
        Assert.Equal("-21.5", FenixChannelEwdRows.Truncated(-21.567, 1));
        Assert.Equal("-1.062", FenixChannelEwdRows.Truncated(-1.0625, 3));
        Assert.Equal("-377", FenixChannelEwdRows.Number(-376.5, 0));
    }

    [Fact]
    public void A_value_the_display_cannot_draw_reads_XX()
    {
        // N1 and N2 are NaN, the EPR and EGT are there and the fuel flow was left out (it reads 0).
        FenixChannelSnapshot left = SnapshotOf(Remote(
            Engine(n1: double.NaN, n2: double.NaN, epr: 0.99698, egt: 344.97),
            Engine(n1: double.NaN, n2: double.NaN, epr: 0.99698, egt: 344.97), [], kg: false));

        Assert.Equal(
            new[] { "EPR: 0.996, 0.996", "FF LBS/H: 0, 0", "EGT °C: 345, 345", "N2 %: XX, XX", "N1 %: XX, XX" },
            RowsOf(left));

        // Every gauge, infinity as well as NaN, on engine 1 and engine 2 alike.
        FenixChannelSnapshot every = SnapshotOf(Remote(
            Engine(double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.NaN, double.PositiveInfinity),
            Engine(double.PositiveInfinity, double.NaN, double.NaN, double.NegativeInfinity, double.NaN), []));

        Assert.Equal(
            new[] { "EPR: XX, XX", "FF KG/H: XX, XX", "EGT °C: XX, XX", "N2 %: XX, XX", "N1 %: XX, XX" },
            RowsOf(every));
        Assert.Equal(
            new[] { "N1 %: XX, XX", "EGT °C: XX, XX", "N2 %: XX, XX", "FF KG/H: XX, XX" },
            RowsOf(every, FenixEngineFamily.PwGtf));
    }

    [Fact]
    public void An_absent_value_reads_as_drawn()
    {
        // An N1 the push left out is 0, which the display draws as it is.
        FenixChannelSnapshot snapshot = SnapshotOf(Remote(Engine(n2: 58.7), Engine(n2: 58.7), []));

        Assert.Equal(
            new[] { "N1 %: 0.0, 0.0", "EGT °C: 0, 0", "N2 %: 58.7, 58.7", "FF KG/H: 0, 0" },
            RowsOf(snapshot, FenixEngineFamily.Cfm56));
    }

    [Fact]
    public void A_value_no_engine_has_reads_XX_and_never_throws()
    {
        Assert.Equal("XX", FenixChannelEwdRows.Truncated(1e15, 1));
        Assert.Equal("XX", FenixChannelEwdRows.Truncated(-1e300, 3));
        Assert.Equal("XX", FenixChannelEwdRows.Truncated(double.MaxValue, 1));
        Assert.Equal("123456789012345.0", FenixChannelEwdRows.Truncated(123456789012345, 1));

        // Number has the same bound: a garbage EGT of 1e300 reads "XX", not 301 digits.
        Assert.Equal("XX", FenixChannelEwdRows.Number(1e300, 0));
    }

    [Fact]
    public void Fuel_flow_follows_the_weight_unit()
    {
        // The channel sends the number in the unit it names, so the number is the same whichever the label is.
        Assert.Equal("FF KG/H: 1320, 1320", RowsOf(Cruise(kg: true))[1]);
        Assert.Equal("FF LBS/H: 1320, 1320", RowsOf(Cruise(kg: false))[1]);

        // Sent as false it is pounds too.
        var engines = new FenixChannelProtoWriter().Message(1, LeftCruise()).Message(2, RightCruise());
        FenixChannelSnapshot sentFalse = SnapshotOf(new FenixChannelProtoWriter().Message(129, engines).Bool(154, false));
        Assert.Equal("FF LBS/H: 1320, 1320", RowsOf(sentFalse)[1]);
    }

    // ---- Problems -----------------------------------------------------------------------------

    [Fact]
    public void An_unknown_letter_outside_the_gate_makes_the_window_not_available()
    {
        // "x" with the digit 1 is a known code, so the thrust limit case uses q. The decoder finds the thrust limit and
        // the flap text; the composer finds the FOB line and the upper ECAM. None gives a row with the letter dropped.
        Assert.Equal("unknown code 'q' in the thrust limit", ProblemOf(Cruise(limit: ["cCLB", "q1.618"])));
        Assert.Equal("unknown code 'x' in the flap text", ProblemOf(Cruise(flap: "xCONF")));
        Assert.Equal("unknown code 'q' in the FOB line", ProblemOf(Cruise(fob: "wFOB: qg8140")));
        Assert.Equal("unknown code 'q' in the upper ECAM", ProblemOf(Cruise(upperLeft: ["urENG 1 FIREu ", "qENG 2"])));
        Assert.Equal("unknown code 'k' in the upper ECAM", ProblemOf(Cruise(upperRight: ["gCOMPANY MSG", "kBLEED"])));
    }

    [Fact]
    public void A_problem_goes_to_the_report()
    {
        // No engine message at all: the decoder's "no engine data".
        FenixChannelSnapshot noEngines = SnapshotOf(new FenixChannelProtoWriter().Bool(154, true));
        Assert.Equal("no engine data", ProblemOf(noEngines));

        // A field in another wire shape names its path.
        var shaped = new FenixChannelProtoWriter().Message(129, new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Varint(4, 1)).Message(2, RightCruise()));
        Assert.Equal("field 129.1.4 has the wrong shape", ProblemOf(SnapshotOf(shaped)));

        // No message or no frame is "no data".
        FenixChannelSnapshot whole = Cruise();
        Assert.Equal("no data", ProblemOf(whole with { Push = null }));
        Assert.Equal("no data", ProblemOf(whole with { Frame = null }));
    }

    [Fact]
    public void The_report_is_optional()
    {
        FenixChannelRows problem = FenixChannelEwdRows.Compose(
            SnapshotOf(new FenixChannelProtoWriter().Bool(154, true)), Iae);
        FenixChannelRows rows = FenixChannelEwdRows.Compose(Cruise(), Iae);

        Assert.Equal("no engine data", problem.Problem);
        Assert.Equal(CruiseRows, rows.Rows);
    }

    // ---- Culture ------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_are_the_same_in_every_culture(string culture)
    {
        // A comma-decimal culture would write 1,407, and sv-SE writes its minus as U+2212. The rows are the invariant
        // ones with an ASCII hyphen.
        FenixChannelSnapshot coldSoaked = SnapshotOf(Remote(
            Engine(n1: 21.5, n2: 58.7, ff: 900, epr: 0.996, egt: -12),
            Engine(n1: 22.5, n2: 59.7, ff: 910, epr: 1.006, egt: -12.4), []));

        InCulture(culture, () =>
        {
            Assert.Equal(CruiseRows, RowsOf(Cruise()));
            Assert.Equal(
                new[] { "EPR: 0.996, 1.006", "FF KG/H: 900, 910", "EGT °C: -12, -12", "N2 %: 58.7, 59.7", "N1 %: 21.5, 22.5" },
                RowsOf(coldSoaked));
            Assert.Equal("-12", FenixChannelEwdRows.Number(-12, 0));
            Assert.Equal("-1.062", FenixChannelEwdRows.Truncated(-1.0625, 3));
        });
    }
}
