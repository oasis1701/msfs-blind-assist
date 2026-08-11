// The pipe protocol is one line per event. A private message can legitimately
// contain tabs and newlines, and either one unescaped desyncs the reader for the
// rest of the session — so the escaping is the load-bearing part, not the split.

using MSFSBlindAssist.VPilot;

namespace MSFSBlindAssist.Tests;

public class VPilotWireFormatTests
{
    [Fact]
    public void Plain_message_round_trips()
    {
        string line = VPilotWireFormat.Encode("private_message", "LON_CTR", "Contact me on 133.60");
        Assert.True(VPilotWireFormat.TryDecode(line, out var type, out var from, out var message));
        Assert.Equal("private_message", type);
        Assert.Equal("LON_CTR", from);
        Assert.Equal("Contact me on 133.60", message);
    }

    [Fact]
    public void Newlines_survive_the_round_trip_and_never_appear_raw_on_the_wire()
    {
        string line = VPilotWireFormat.Encode("private_message", "EGLL_TWR", "Line one\r\nLine two");
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.True(VPilotWireFormat.TryDecode(line, out _, out _, out var message));
        Assert.Equal("Line one\r\nLine two", message);
    }

    [Fact]
    public void Tabs_survive_and_leave_exactly_two_separators()
    {
        string line = VPilotWireFormat.Encode("radio_message", "BAW123", "col1\tcol2");
        Assert.Equal(2, line.Count(c => c == '\t'));
        Assert.True(VPilotWireFormat.TryDecode(line, out _, out var from, out var message));
        Assert.Equal("BAW123", from);
        Assert.Equal("col1\tcol2", message);
    }

    [Fact]
    public void Backslashes_are_not_swallowed()
    {
        string line = VPilotWireFormat.Encode("private_message", "X", @"path\to\n thing");
        Assert.True(VPilotWireFormat.TryDecode(line, out _, out _, out var message));
        Assert.Equal(@"path\to\n thing", message);
    }

    [Fact]
    public void Empty_fields_round_trip_rather_than_collapsing()
    {
        string line = VPilotWireFormat.Encode("disconnected", "", "");
        Assert.True(VPilotWireFormat.TryDecode(line, out var type, out var from, out var message));
        Assert.Equal("disconnected", type);
        Assert.Equal("", from);
        Assert.Equal("", message);
    }

    [Fact]
    public void Null_inputs_encode_as_empty_fields()
    {
        string line = VPilotWireFormat.Encode("selcal", null!, null!);
        Assert.True(VPilotWireFormat.TryDecode(line, out var type, out var from, out var message));
        Assert.Equal("selcal", type);
        Assert.Equal("", from);
        Assert.Equal("", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no_separators_at_all")]
    [InlineData("only\tone")]
    [InlineData("far\ttoo\tmany\tfields")]
    public void Malformed_lines_are_rejected_rather_than_half_read(string line)
    {
        Assert.False(VPilotWireFormat.TryDecode(line, out _, out _, out _));
    }

    [Fact]
    public void A_field_ending_in_a_lone_backslash_keeps_it_rather_than_reading_past_the_end()
    {
        // Unescape's `i == s.Length - 1` guard exists for exactly this: a backslash as
        // the very last character of a field, with no following character to pair it
        // with. Encode() itself always doubles a real trailing backslash (the plain
        // round-trip tests above cover that), so this raw shape should never arrive from
        // a well-behaved sender — but TryDecode must not read past the end of the field
        // when it does, and must keep the backslash rather than dropping it.
        Assert.True(VPilotWireFormat.TryDecode("radio_message\tX\tabc\\", out _, out _, out var message));
        Assert.Equal("abc\\", message);
    }

    [Fact]
    public void An_unrecognised_escape_sequence_is_kept_literally_rather_than_dropped()
    {
        // \q is not one of the recognised escapes (\\, \n, \r, \t). Unescape's default
        // branch keeps BOTH characters rather than silently eating the backslash —
        // load-bearing for an ordinary Windows path landing in a chat message, which is
        // exactly the shape a private message full of backslashes usually has.
        Assert.True(VPilotWireFormat.TryDecode("private_message\tX\tpath\\qthing", out _, out _, out var message));
        Assert.Equal("path\\qthing", message);
    }
}
