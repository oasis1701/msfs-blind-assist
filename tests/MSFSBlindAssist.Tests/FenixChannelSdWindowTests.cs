// Characterization tests for the Fenix SD window: its rows (FenixChannelSd.Compose), the field reader every SD page
// reads with (FenixChannelSdReader), the bottom line and the check for format letters the app does not know
// (FenixChannelSdFrame), the window (Forms/Fenix/FenixChannelSdForm) and the definition's factory for it
// (FenixA320Definition.CreateChannelSdForm), and the two seams that let a pilot reach it: the entry in the Fenix
// windows list and Alt+S (ChannelSdKey, pinned by its source). Pure logic, except the window tests, which build the
// window without showing it, as the other display windows are tested (FenixChannelPfdWindowTests). Every field number
// is written out here, never read from FenixChannelFields, so that a wrong number in the app fails a test. SdPush, at
// the end of this file, builds the pushes; the page tests of plan 05 (FenixChannelSdEngTests and the others) build
// theirs with it too.
//
// The contract: a snapshot with no message or no frame is "no data", which is not reported. Then the first format
// letter the app does not know, in the bottom line and, on the STATUS page (14), in the STATUS text, is the whole
// result. Then the bottom line: none is "no bottom line", and one of another length than nine is "field 178 has the
// wrong shape". A weight unit (154) of another shape is a wrong shape too. Each of these is reported once. Otherwise
// the rows are the page's name, the page's own rows (or one row "<PAGE>: not available, <problem>", or none), and the
// bottom line: TAT, SAT, UTC and GW, each element spoken with its colors and an empty one "not available", the UTC row
// one coded line of its hour and minute, "not available" unless it has both, GW in KG while 154 is set and LBS
// otherwise. The STATUS page's rows are the lines of its text, 175 then 176, each spoken with its colors and a blank
// one skipped; none at all is "no STATUS text", a page problem. The reader
// gives a field left out as its default, and the first problem it finds, named by the field's path, is kept for every
// reader it hands out; a valve value outside the enum reads "not available" and is reported without stopping the page.
// Numbers are written in the invariant culture, rounded away from zero, never "-0", and "XX" when not a number.

using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.Fenix;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdWindowTests
{
    // The bottom line of SdPush's default push, as the window reads it.
    private static readonly string[] BottomRows =
        ["TAT °C: +15, green", "SAT °C: +15, green", "UTC: 15:06", "GW KG: --, cyan"];

    // The rows of a push that composes, and nothing reported.
    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // The problem of a push that does not compose, which must also have been reported, once and in the same words.
    private static string ProblemOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Rows);
        Assert.NotNull(rows.Problem);
        Assert.Equal(new[] { rows.Problem }, reported);
        return rows.Problem!;
    }

    private static FenixChannelPush PushOf(FenixChannelProtoWriter remote)
        => FenixChannelPush.From(remote.ToArray()) ?? throw new InvalidOperationException("not valid protobuf");

    // ---- No data --------------------------------------------------------------------------------

    [Fact]
    public void A_snapshot_with_no_message_is_no_data_and_is_not_reported()
    {
        // Before the first good frame of a connection the snapshot has neither: the window shows "no data", and the log
        // gets nothing, since nothing is wrong.
        var trusted = new FenixDisplayVerdict(true, "trusted");
        SdPush push = new();
        var reported = new List<string>();

        foreach (FenixChannelSnapshot snapshot in new[]
                 {
                     new FenixChannelSnapshot(trusted, null, null, null, 0, 1),
                     new FenixChannelSnapshot(trusted, push.Frame(), null, null, 0, 1),
                     new FenixChannelSnapshot(trusted, null, push.Remote(), null, 0, 1),
                 })
        {
            Assert.Equal(FenixChannelRows.NotAvailable("no data"), FenixChannelSd.Compose(snapshot, push.Variant, reported.Add));
        }

        Assert.Empty(reported);
    }

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_page_shows_its_number_then_the_bottom_line()
    {
        // Page 42 is no page of the SD, so it has no composer: its name, then the bottom line. Elements 2, 3, 7 and 8
        // are not read. The UTC line has no color letter, so it speaks no color.
        Assert.Equal(new[] { "page 42" }.Concat(BottomRows), RowsOf(new SdPush { Page = 42 }));
    }

    [Fact]
    public void The_gw_unit_follows_field_154()
    {
        // The Fenix leaves 154 out for pounds, so an absent field is LBS; every weight would otherwise be 2.2 times off.
        SdPush pounds = new() { Kg = false };
        pounds.BottomLine[6] = "g63400";
        SdPush kilograms = new() { Kg = true };
        kilograms.BottomLine[6] = "g63400";

        Assert.Equal("GW LBS: 63400, green", RowsOf(pounds)[^1]);
        Assert.Equal("GW KG: 63400, green", RowsOf(kilograms)[^1]);
    }

    [Fact]
    public void The_weight_unit_reads_the_remote_data()
    {
        Assert.Equal("KG", FenixChannelSd.WeightUnit(FenixChannelSdReader.Of(new SdPush { Kg = true }.Remote(), _ => { })));
        Assert.Equal("LBS", FenixChannelSd.WeightUnit(FenixChannelSdReader.Of(new SdPush { Kg = false }.Remote(), _ => { })));
    }

    [Fact]
    public void An_empty_element_reads_not_available()
    {
        // An element with an empty string, one with no string at all (a null the contract wraps), and one with a color
        // letter but nothing to say are all empty.
        SdPush push = new();
        push.BottomLine[0] = "";
        push.BottomLine[1] = null;
        push.BottomLine[6] = "g ";

        Assert.Equal(
            new[] { "page 42", "TAT °C: not available", "SAT °C: not available", "UTC: 15:06", "GW KG: not available" },
            RowsOf(push));
    }

    [Theory]
    [InlineData("", "06")]
    [InlineData("15", "")]
    [InlineData(null, "06")]
    [InlineData("15", null)]
    [InlineData("", "")]
    public void The_utc_row_needs_both_its_hour_and_its_minute(string? hour, string? minute)
    {
        SdPush push = new();
        push.BottomLine[4] = hour;
        push.BottomLine[5] = minute;

        Assert.Equal("UTC: not available", RowsOf(push)[3]);
    }

    [Fact]
    public void The_utc_row_is_spoken_as_one_coded_line()
    {
        // The hour, a colon and the minute make one coded line, "<hour>:<minute>", spoken once: one color at its end
        // where the line has one, never a color after each element.
        SdPush push = new();
        push.BottomLine[4] = "g15";
        push.BottomLine[5] = "g06";

        Assert.Equal("UTC: 15:06, green", RowsOf(push)[3]);
    }

    [Fact]
    public void The_utc_row_reads_hours_and_minutes_with_a_colon_never_an_h()
    {
        // The SD draws "18 H 16", which a screen reader reads as "18 H 16"; the app speaks every other time as
        // hours:minutes (Output Shift+Z: "18:16Z"), so the row does too. Measured live: "UTC: 18:16".
        SdPush push = new();
        push.BottomLine[4] = "18";
        push.BottomLine[5] = "16";

        Assert.Equal("UTC: 18:16", RowsOf(push)[3]);
    }

    // ---- What takes the window to one status row ------------------------------------------------

    [Fact]
    public void A_missing_bottom_line_makes_the_window_not_available()
    {
        Assert.Equal("no bottom line", ProblemOf(new SdPush { BottomLine = [] }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(10)]
    public void A_bottom_line_of_another_length_is_a_wrong_shape(int count)
    {
        SdPush push = new() { BottomLine = Enumerable.Repeat<string?>("g1", count).ToArray() };

        Assert.Equal("field 178 has the wrong shape", ProblemOf(push));
    }

    [Fact]
    public void A_bottom_line_element_of_another_shape_is_a_wrong_shape()
    {
        // An element that is no message, and an element whose string is a number, each named by its path.
        SdPush notAMessage = new() { BottomLine = [] };
        notAMessage.With(remote => remote.Varint(178, 1));
        SdPush notAString = new() { BottomLine = [] };
        notAString.With(remote => remote.Message(178, new FenixChannelProtoWriter().Varint(1, 15)));

        Assert.Equal("field 178 has the wrong shape", ProblemOf(notAMessage));
        Assert.Equal("field 178.1 has the wrong shape", ProblemOf(notAString));
    }

    [Fact]
    public void A_weight_unit_of_another_shape_makes_the_window_not_available()
    {
        // The weight unit is the window's own, as the bottom line is: a reshaped 154 stops the window, never a GW in a
        // unit it guessed.
        SdPush push = new() { Kg = false };
        push.With(remote => remote.Double(154, 1));

        Assert.Equal("field 154 has the wrong shape", ProblemOf(push));
    }

    [Theory]
    [InlineData(0, "k+15", 'k')]    // the TAT
    [InlineData(7, "kX", 'k')]      // an element the window does not read: its display is drawn in a way the app cannot tell
    [InlineData(6, "qc--", 'q')]
    public void An_unknown_letter_in_the_bottom_line_untrusts_the_window(int element, string text, char code)
    {
        SdPush push = new();
        push.BottomLine[element] = text;

        Assert.Equal($"unknown code '{code}' in the bottom line", ProblemOf(push));
    }

    [Fact]
    public void The_first_unknown_letter_decides_before_the_bottom_lines_shape()
    {
        // The first letter in the order the elements come, and before the length of the bottom line is looked at.
        SdPush push = new() { BottomLine = ["g+15", "j+15", "", "", "15", "06", "k--", ""] };

        Assert.Equal("unknown code 'j' in the bottom line", ProblemOf(push));
    }

    [Fact]
    public void The_status_text_is_checked_on_the_status_page_only()
    {
        // The STATUS text (175, then 176) is drawn only on the STATUS page (14), so only there can a letter in it
        // untrust the window. The bottom line is looked at first.
        SdPush left = new() { StatusLeft = ["wSTATUS", "kX"] };
        SdPush right = new() { StatusLeft = ["wSTATUS"], StatusRight = ["jX"] };
        SdPush both = new() { StatusLeft = ["kX"] };
        both.BottomLine[0] = "q+15";

        Assert.Equal("unknown code 'k' in the STATUS text", FenixChannelSdFrame.UnknownCode(left.Remote(), 14));
        Assert.Equal("unknown code 'j' in the STATUS text", FenixChannelSdFrame.UnknownCode(right.Remote(), 14));
        Assert.Equal("unknown code 'q' in the bottom line", FenixChannelSdFrame.UnknownCode(both.Remote(), 14));
        Assert.Null(FenixChannelSdFrame.UnknownCode(left.Remote(), 8));
        Assert.Null(FenixChannelSdFrame.UnknownCode(new SdPush().Remote(), 14));
    }

    [Fact]
    public void An_element_that_is_not_text_is_skipped_by_the_letter_check()
    {
        // Its shape is the bottom line's problem, found when the bottom line is read; only a letter decides here.
        SdPush push = new() { BottomLine = [] };
        push.With(remote => remote.Varint(178, 1).Message(178, new FenixChannelProtoWriter().String(1, "kX")));

        Assert.Equal("unknown code 'k' in the bottom line", FenixChannelSdFrame.UnknownCode(push.Remote(), 8));
    }

    [Fact]
    public void The_bottom_line_is_its_nine_elements_in_order()
    {
        FenixChannelSection<IReadOnlyList<string>> bottom = FenixChannelSdFrame.BottomLine(new SdPush().Remote());

        Assert.True(bottom.IsOk);
        Assert.Equal(new[] { "g+15", "g+15", "", "", "15", "06", "c--", "", "" }, bottom.Value);
    }

    // ---- The STATUS page ------------------------------------------------------------------------

    [Fact]
    public void The_status_page_reads_the_left_column_then_the_right()
    {
        // Page 14. The empty first line and the spaces before NORMAL's color letter say nothing; every line of 175 comes
        // before every line of 176, each with its color.
        SdPush push = new() { Page = 14, StatusLeft = ["", "            gNORMAL"], StatusRight = ["aGEN 1"] };

        Assert.Equal(new[] { "STATUS", "NORMAL, green", "GEN 1, amber" }.Concat(BottomRows), RowsOf(push));
    }

    [Fact]
    public void Blank_status_lines_are_skipped()
    {
        // An empty string, an element with no string at all (a null the contract wraps), spaces, and a color letter with
        // nothing after it but a space: none is a row, and the others keep their order, 175 then 176.
        SdPush push = new()
        {
            Page = 14,
            StatusLeft = ["wL1", null, "", "   ", "gL2"],
            StatusRight = ["", "cR1", "w "]
        };

        Assert.Equal(new[] { "STATUS", "L1, white", "L2, green", "R1, cyan" }.Concat(BottomRows), RowsOf(push));
    }

    [Fact]
    public void An_unknown_letter_in_the_status_text_untrusts_the_window()
    {
        // The STATUS text is read on the STATUS page, so a letter the app does not know in either column is the whole
        // result there ([FNX-24]), reported once; the page's rows are never built.
        SdPush left = new() { Page = 14, StatusLeft = ["wSTATUS", "kX"] };
        SdPush right = new() { Page = 14, StatusLeft = ["wSTATUS"], StatusRight = ["jX"] };

        Assert.Equal("unknown code 'k' in the STATUS text", ProblemOf(left));
        Assert.Equal("unknown code 'j' in the STATUS text", ProblemOf(right));
    }

    [Fact]
    public void The_status_text_is_not_checked_on_other_pages()
    {
        // Page 13 draws no STATUS text, so a letter in it decides nothing: the page's name, then the bottom line.
        SdPush push = new() { Page = 13, StatusLeft = ["kX"] };

        Assert.Equal(new[] { "page 13" }.Concat(BottomRows), RowsOf(push));
    }

    [Fact]
    public void A_status_page_with_no_text_is_one_row_and_is_reported()
    {
        // No STATUS text is no content, not a wrong shape: the page is one row, its name and the bottom line stay, and
        // the window reports it once, under the page's name.
        SdPush push = new() { Page = 14, StatusLeft = ["", "   "], StatusRight = [null] };
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "STATUS", "STATUS: not available, no STATUS text" }.Concat(BottomRows),
            rows.Rows);
        Assert.Equal(new[] { "STATUS: no STATUS text" }, reported);
    }

    [Fact]
    public void A_status_text_of_another_shape_stops_the_page_only()
    {
        // 176 arrives as a varint: the page's one row names the field, the page's name and the bottom line stay, and the
        // problem is reported once, under the page's name.
        SdPush push = new() { Page = 14, StatusLeft = ["gNORMAL"] };
        push.With(remote => remote.Varint(176, 1));
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "STATUS", "STATUS: not available, field 176 has the wrong shape" }.Concat(BottomRows),
            rows.Rows);
        Assert.Equal(new[] { "STATUS: field 176 has the wrong shape" }, reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Status_speech_is_the_same_in_every_culture(string culture)
    {
        // Channel text is words and digits as the SD draws them: a decimal comma in it stays, and a decimal point stays.
        SdPush push = new() { Page = 14, StatusLeft = ["gNORMAL 1.5", "a12,5 HZ"], StatusRight = ["w-4"] };

        Assert.Equal(
            new[] { "STATUS", "NORMAL 1.5, green", "12,5 HZ, amber", "-4, white" }.Concat(BottomRows),
            TestCulture.Run(culture, () => RowsOf(push)));
    }

    // ---- The reader -----------------------------------------------------------------------------

    [Fact]
    public void The_reader_names_the_first_problem_by_path()
    {
        // Field 1 of the green system (133.1.1) arrives as a varint: the path names every level, and every reader
        // handed out shares the problem.
        FenixChannelPush deep = PushOf(new FenixChannelProtoWriter()
            .Message(133, new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Varint(1, 3000)))
            .Bool(154, true));
        var reported = new List<string>();
        FenixChannelSdReader remote = FenixChannelSdReader.Of(deep, reported.Add);
        FenixChannelSdReader green = remote.Message(133).Message(1);

        Assert.Null(remote.Problem);
        Assert.Equal(0, green.Double(1));
        Assert.Equal("field 133.1.1 has the wrong shape", remote.Problem);
        Assert.Equal(remote.Problem, green.Problem);

        // A later problem never replaces the first, and every read after it gives its default: 154 is set, yet false.
        remote.Message(129);
        remote.Fail("no engines");
        Assert.False(remote.Bool(154));
        Assert.Equal("field 133.1.1 has the wrong shape", green.Problem);

        // A stopping problem is the page's to report, through its rows; the reader reports none.
        Assert.Empty(reported);

        // A nested message left out is "no field <path>", at whatever level.
        FenixChannelSdReader none = FenixChannelSdReader.Of(PushOf(new FenixChannelProtoWriter().Bool(154, true)), _ => { });
        none.Message(133).Message(1).Double(1);
        Assert.Equal("no field 133", none.Problem);

        FenixChannelSdReader noGreen = FenixChannelSdReader.Of(
            PushOf(new FenixChannelProtoWriter().Message(133, new FenixChannelProtoWriter().Bool(12, true))), _ => { });
        noGreen.Message(133).Message(1);
        Assert.Equal("no field 133.1", noGreen.Problem);
    }

    [Fact]
    public void The_reader_reads_each_shape_and_a_field_left_out_as_its_default()
    {
        FenixChannelPush push = PushOf(new FenixChannelProtoWriter()
            .Double(1, 13.94)
            .Double(2, double.NaN)
            .Bool(3, true)
            .Varint(4, 0x1_0000_0005)                   // an int32 is its low 32 bits: 5
            .Varint(5, unchecked((ulong)(long)-1))      // an int32 of -1, sign-extended to ten bytes on the wire
            .Varint(6, 1)                               // a valve, closed
            .Message(7, new FenixChannelProtoWriter().String(1, "gA"))
            .Message(7, new FenixChannelProtoWriter())  // a null the contract wraps: no field 1
            .Message(7, new FenixChannelProtoWriter().String(1, "")));
        var reported = new List<string>();
        FenixChannelSdReader reader = FenixChannelSdReader.Of(push, reported.Add);

        Assert.Equal(13.94, reader.Double(1));
        Assert.True(double.IsNaN(reader.Double(2)));
        Assert.True(reader.Bool(3));
        Assert.Equal(5, reader.Int(4));
        Assert.Equal(-1, reader.Int(5));
        Assert.Equal("closed", reader.Valve(6));
        Assert.Equal(new[] { "gA", "", "" }, reader.Strings(7));

        // Left out: 0, false, 0, open (the Fenix leaves an open valve out) and no strings.
        Assert.Equal(0, reader.Double(20));
        Assert.False(reader.Bool(20));
        Assert.Equal(0, reader.Int(20));
        Assert.Equal("open", reader.Valve(20));
        Assert.Empty(reader.Strings(20));

        Assert.Null(reader.Problem);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData("double", "field 7 has the wrong shape")]        // a varint
    [InlineData("bool", "field 8 has the wrong shape")]          // a fixed64
    [InlineData("int", "field 9 has the wrong shape")]           // a string
    [InlineData("valve", "field 8 has the wrong shape")]         // a fixed64
    [InlineData("message", "field 7 has the wrong shape")]       // a varint
    [InlineData("strings", "field 7 has the wrong shape")]       // an element that is a varint
    [InlineData("strings value", "field 10.1 has the wrong shape")] // an element whose string is a varint
    public void A_read_of_another_wire_type_is_a_wrong_shape(string read, string problem)
    {
        FenixChannelPush push = PushOf(new FenixChannelProtoWriter()
            .Varint(7, 1)
            .Double(8, 1)
            .String(9, "A")
            .Message(10, new FenixChannelProtoWriter().Varint(1, 1)));
        var reported = new List<string>();
        FenixChannelSdReader reader = FenixChannelSdReader.Of(push, reported.Add);

        switch (read)
        {
            case "double": reader.Double(7); break;
            case "bool": reader.Bool(8); break;
            case "int": reader.Int(9); break;
            case "valve": reader.Valve(8); break;
            case "message": reader.Message(7); break;
            case "strings": reader.Strings(7); break;
            case "strings value": reader.Strings(10); break;
            default: throw new ArgumentOutOfRangeException(nameof(read), read, "no such read");
        }

        Assert.Equal(problem, reader.Problem);
        Assert.Empty(reported);
    }

    [Fact]
    public void A_valve_outside_the_enum_reads_not_available_and_is_reported_without_stopping_the_page()
    {
        // 134.11 holds the fuel valves: 4 arrives as 9, which is no valve state, 1 as 5, DataInvalid, which the SD draws
        // as crosses, 3 as -1, and 2 is left out, which is open.
        FenixChannelPush push = PushOf(new FenixChannelProtoWriter()
            .Message(134, new FenixChannelProtoWriter().Message(11, new FenixChannelProtoWriter()
                .Varint(4, 9)
                .Varint(1, 5)
                .Varint(3, unchecked((ulong)(long)-1)))));
        var reported = new List<string>();
        FenixChannelSdReader valves = FenixChannelSdReader.Of(push, reported.Add).Message(134).Message(11);

        Assert.Equal("not available", valves.Valve(4));
        Assert.Equal("XX", valves.Valve(1));
        Assert.Equal("not available", valves.Valve(3));
        Assert.Equal("open", valves.Valve(2));

        Assert.Null(valves.Problem);
        Assert.Equal(
            new[] { "field 134.11.4 has an unknown value 9", "field 134.11.3 has an unknown value -1" },
            reported);
    }

    // ---- Numbers --------------------------------------------------------------------------------

    [Fact]
    public void Numbers_are_invariant_and_never_negative_zero()
    {
        // de-DE writes a decimal comma, and sv-SE a minus sign of its own (U+2212): what is spoken is neither.
        Assert.Equal("13.9", TestCulture.Run("de-DE", () => FenixChannelSd.OneDecimal(13.94)));
        string[] swedish = TestCulture.Run("sv-SE", () => new[]
        {
            FenixChannelSd.Whole(-52.5), FenixChannelSd.Whole(-0.4), FenixChannelSd.OneDecimal(-0.04),
            FenixChannelSd.Whole(52.5), FenixChannelSd.OneDecimal(-13.94), FenixChannelSd.OneDecimal(14)
        });
        Assert.Equal(new[] { "-53", "0", "0.0", "53", "-13.9", "14.0" }, swedish);

        // NaN and the infinities are the SD's crosses.
        Assert.Equal(
            Enumerable.Repeat("XX", 6),
            new[]
            {
                FenixChannelSd.Whole(double.NaN), FenixChannelSd.Whole(double.PositiveInfinity),
                FenixChannelSd.Whole(double.NegativeInfinity), FenixChannelSd.OneDecimal(double.NaN),
                FenixChannelSd.OneDecimal(double.PositiveInfinity), FenixChannelSd.OneDecimal(double.NegativeInfinity)
            });
    }

    // ---- The window -----------------------------------------------------------------------------

    private static DisplayListBox ListOf(Control form) => form.Controls.OfType<DisplayListBox>().Single();

    private static string[] RowsShown(Control form) => ListOf(form).Items.Cast<string>().ToArray();

    // A whole display push that passes the gate (FenixChannelProtoWriter.GoodPush), with the SD's fields in the same
    // RemoteData message, as a real Fenix sends them.
    private static byte[] PushWith(SdPush sd)
    {
        byte[] good = FenixChannelProtoWriter.GoodPush();
        int at = 1;
        while ((good[at] & 0x80) != 0) at++;
        at++;

        byte[] remote = [.. good[at..], .. sd.RemoteBytes()];
        return new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Raw(remote)).ToArray();
    }

    // A monitor the SD trusts: the data call is connected, the build is checked and one good push, with the SD's fields,
    // has arrived.
    private static FenixChannelMonitor TrustedSdMonitor(MonitorTestKit.Lines lines, SdPush sd)
    {
        FenixChannelMonitor monitor = MonitorTestKit.NewIdleMonitor(lines);
        monitor.OnDataState(FenixChannelState.Connected, "");
        Assert.True(
            SpinWait.SpinUntil(
                () => lines.All.Contains($"build {MonitorTestKit.CheckedBuild}: checked"), MonitorTestKit.Generous),
            "the build was never checked. The log: " + lines);
        monitor.OnDataMessage(PushWith(sd));
        Assert.Equal(new FenixDisplayVerdict(true, "trusted"), monitor.VerdictFor(FenixChannelDisplay.Sd));
        return monitor;
    }

    [Fact]
    public void The_window_is_the_sds_and_reads_the_variant_at_each_refresh()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedSdMonitor(lines, new SdPush());
        int reads = 0;
        using var form = new FenixChannelSdForm(monitor, () =>
        {
            reads++;
            return SdPush.A320;
        }, _ => { });

        Assert.Equal("Fenix SD", form.Text);
        Assert.Equal("SD display", ListOf(form).AccessibleName);

        form.StartLive();
        Assert.Equal(new[] { "page 42" }.Concat(BottomRows), RowsShown(form));
        Assert.Equal(1, reads);

        form.RefreshNow();
        Assert.Equal(2, reads);
    }

    [Fact]
    public void A_window_problem_is_logged_under_the_sds_name()
    {
        var lines = new MonitorTestKit.Lines();
        using FenixChannelMonitor monitor = TrustedSdMonitor(lines, new SdPush { BottomLine = [] });
        using var form = new FenixChannelSdForm(monitor, () => SdPush.A320, _ => { });

        form.StartLive();

        Assert.Equal(new[] { "SD: not available, no bottom line" }, RowsShown(form));
        Assert.Single(lines.All, line => line == "SD: no bottom line");
    }

    [Fact]
    public void The_window_shows_one_status_row_while_the_sd_is_not_trusted()
    {
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        int reads = 0;
        using var form = new FenixChannelSdForm(neverStarted, () =>
        {
            reads++;
            return SdPush.A320;
        }, _ => { });

        form.StartLive();

        Assert.Equal(
            new[] { $"SD: not available, {neverStarted.VerdictFor(FenixChannelDisplay.Sd).Reason}" },
            RowsShown(form));
        Assert.Equal(0, reads);
    }

    [Fact]
    public void The_definition_makes_the_sds_own_window()
    {
        // Fenix windows are tracked one for each type: a display that shared the base class would share its window.
        using FenixChannelMonitor neverStarted = MonitorTestKit.NewIdleMonitor(new MonitorTestKit.Lines());
        using FenixChannelSdForm form = new FenixA320Definition().CreateChannelSdForm(neverStarted, null!, null!);

        Assert.Equal("Fenix SD", form.Text);
        Assert.True(typeof(FenixChannelSdForm).IsSubclassOf(typeof(FenixChannelDisplayForm)));
        Assert.Equal("SD", FenixChannelSd.DisplayName);
    }

    // ---- The switch: the Fenix windows list and Alt+S -------------------------------------------

    [Fact]
    public void The_sd_window_is_listed_once()
    {
        // The list speaks "Name. Description", and the SD's name is the display's short name, as the other displays'
        // are. Whatever opens the entry is the definition's own ShowChannelWindow, which shows the window whatever the
        // verdict, so the entry carries no check of its own.
        FenixWindowEntry entry = Assert.Single(FenixWindowRegistry.Build(), e => e.Name == "SD");

        Assert.Equal(
            "The System Display from the Fenix: the page shown, its values, STATUS and the bottom line",
            entry.Description);
    }

    [Fact]
    public void The_sd_key_opens_the_window_only_through_OpenChannelWindow()
    {
        // ChannelSdKey is a private partial method, so it is pinned by reading its source, as the dispatch is
        // (FenixChannelDispatchTests). Alt+S opens the window through OpenChannelWindow, which opens it while the SD is
        // trusted at the press, or dark (on its one status row), and otherwise leaves the key to the AI read with no
        // extra words (spec 2.4). It never calls ShowChannelWindow, which shows the window whatever the verdict: that is
        // the windows list's way in. The key's answer is OpenChannelWindow's own result for the SD, never a
        // "handled = true" after it, which would swallow Alt+S, and with it the AI read, while the SD is not trusted for
        // any other reason.
        string code = Comment.Replace(
            File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Aircraft", "FenixA320Definition.ChannelSd.cs")),
            "");
        int declaration = code.IndexOf("partial void ChannelSdKey(", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "FenixA320Definition.ChannelSd.cs does not implement ChannelSdKey");
        string body = BlockAfter(code, declaration);

        Assert.Contains("ReadDisplayLowerECAM", body);
        Assert.Matches(OpenedForTheSd, body);
        Assert.DoesNotContain("ShowChannelWindow", body);
    }

    // The key's answer: "handled" is assigned what OpenChannelWindow returns for the SD's display, the window's factory
    // and name coming after.
    private static readonly Regex OpenedForTheSd = new(
        @"handled\s*=\s*OpenChannelWindow\(\s*ctx\s*,\s*(Services\.)?FenixChannelDisplay\.Sd\b",
        RegexOptions.CultureInvariant);

    // ---- Reading the source ---------------------------------------------------------------------

    // A line comment or a block comment: a call named in a comment is no call.
    private static readonly Regex Comment = new(@"//[^\r\n]*|/\*.*?\*/",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // The block that opens at the first "{" after start, braces and all, up to its closing brace.
    private static string BlockAfter(string code, int start)
    {
        int open = code.IndexOf('{', start);
        Assert.True(open >= 0, "no block after the declaration");
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("the block is not closed");
    }
}

/// <summary>
/// Builds what the SD window and its pages are composed from: a RemoteData message, a frame showing <see cref="Page"/>,
/// the loaded Fenix and a trusted snapshot of the first connection. Every field number is written out, never taken from
/// FenixChannelFields, so that a wrong number in the app fails a test. Partial, so that a page's test file can add what
/// its page needs from a file of its own.
/// </summary>
/// <remarks>
/// RemoteData holds the page (10), the weight unit (154) while <see cref="Kg"/>, the STATUS text (175, 176) and the
/// bottom line (178), then whatever a test writes with <see cref="With"/>, in the order written. A singular field read
/// twice reads its last value, so a test can write over one of these; a repeated field reads every occurrence, so a test
/// that changes the bottom line or the STATUS text sets its list instead.
/// </remarks>
internal sealed partial class SdPush
{
    /// <summary>An A320 with CFM engines: the default <see cref="Variant"/>.</summary>
    public static readonly FenixVariant A320 =
        FenixVariant.Unknown with { Type = FenixAircraftType.A320, Engine = FenixEngineFamily.Cfm56 };

    private readonly List<Action<FenixChannelProtoWriter>> _writes = [];

    /// <summary>
    /// The page shown: the frame's FwcPage, and field 10. By default 42, a number that is no page, so the window shows
    /// the page's name and the bottom line alone.
    /// </summary>
    public int Page { get; set; } = 42;

    /// <summary>
    /// The bottom line (178), one element each, in order: a string is the element's field 1, and null an element without
    /// it (a null the contract wraps). Empty writes no bottom line. By default nine elements: TAT and SAT "g+15", the
    /// UTC hour and minute "15" and "06", GW "c--", and the four the window does not read empty.
    /// </summary>
    public string?[] BottomLine { get; set; } = ["g+15", "g+15", "", "", "15", "06", "c--", "", ""];

    /// <summary>
    /// Whether 154, the weight unit, is written set: KG. False leaves it out, as the Fenix does for pounds. True by
    /// default.
    /// </summary>
    public bool Kg { get; set; } = true;

    /// <summary>The STATUS text's left column (175), written as <see cref="BottomLine"/> is. Empty by default.</summary>
    public string?[] StatusLeft { get; set; } = [];

    /// <summary>The STATUS text's right column (176), written as <see cref="BottomLine"/> is. Empty by default.</summary>
    public string?[] StatusRight { get; set; } = [];

    /// <summary>The loaded Fenix the window and the pages are given: <see cref="A320"/> by default.</summary>
    public FenixVariant Variant { get; set; } = A320;

    /// <summary>
    /// Writes more of RemoteData, after the fields above: any field, its number written out, nested messages built with
    /// a <see cref="FenixChannelProtoWriter"/> of their own.
    /// </summary>
    public SdPush With(Action<FenixChannelProtoWriter> write)
    {
        _writes.Add(write);
        return this;
    }

    /// <summary>The RemoteData message's bytes, as they sit inside a display push.</summary>
    public byte[] RemoteBytes()
    {
        var remote = new FenixChannelProtoWriter().Varint(10, unchecked((ulong)(long)Page));
        if (Kg) remote.Bool(154, true);
        WriteNullWrapped(remote, 175, StatusLeft);
        WriteNullWrapped(remote, 176, StatusRight);
        WriteNullWrapped(remote, 178, BottomLine);
        foreach (Action<FenixChannelProtoWriter> write in _writes)
        {
            write(remote);
        }

        return remote.ToArray();
    }

    /// <summary>The RemoteData message, as a snapshot carries it.</summary>
    public FenixChannelPush Remote() => FenixChannelPush.From(RemoteBytes())
        ?? throw new InvalidOperationException("the test's RemoteData is not valid protobuf");

    /// <summary>A frame of a live SD showing <see cref="Page"/>, every shape right, and nothing else in it.</summary>
    public FenixChannelFrame Frame() => new(false, Array.Empty<string>(), FenixFdState.Default,
        Array.Empty<FenixFmaSlot>(), Array.Empty<string>(), Array.Empty<string>(), Page, 0, 0, "")
    {
        SdLive = true
    };

    /// <summary>A snapshot of a trusted SD on the first connection: <see cref="Frame"/> and <see cref="Remote"/>.</summary>
    public FenixChannelSnapshot Snapshot()
        => new(new FenixDisplayVerdict(true, "trusted"), Frame(), Remote(), null, 0, 1);

    /// <summary>The window's rows for <see cref="Snapshot"/> and <see cref="Variant"/>, problems to <paramref name="report"/>.</summary>
    public FenixChannelRows Rows(Action<string> report) => FenixChannelSd.Compose(Snapshot(), Variant, report);

    /// <summary>
    /// What one page's composer is called with: <see cref="Remote"/>, <see cref="Frame"/>, <see cref="Variant"/> and
    /// <paramref name="report"/> as its ReportProblem, for a test that calls the composer directly.
    /// </summary>
    public FenixChannelSdPageContext Context(Action<string> report) => new(Remote(), Frame(), Variant, report);

    private static void WriteNullWrapped(FenixChannelProtoWriter remote, int field, IEnumerable<string?> texts)
    {
        foreach (string? text in texts)
        {
            var element = new FenixChannelProtoWriter();
            if (text is not null) element.String(1, text);
            remote.Message(field, element);
        }
    }
}
