// Characterization tests for MSFSBlindAssist.Forms.DisplayText.SetPreserveCaret — the
// common-prefix/suffix minimal-edit + caret-line restore used to update read-only status
// TextBoxes without throwing the screen reader's reading position back to line 0 (CLAUDE.md
// "Diagnostic Logs" area isn't relevant here; this is the ECAM/ISIS/status-box refresh path
// documented in Forms/DisplayText.cs).
//
// A plain WinForms TextBox constructs and responds to GetLineFromCharIndex /
// GetFirstCharIndexFromLine fine in this xUnit host (net10.0-windows, no message pump
// needed for these calls) — no WinForms-in-test limitation was hit for this target.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class DisplayTextSetPreserveCaretTests
{
    private static TextBox MakeBox(string initialText, bool readOnly = false)
    {
        var box = new TextBox { Multiline = true, ReadOnly = readOnly, Text = initialText };
        return box;
    }

    [Fact]
    public void Null_box_is_a_safe_no_op()
    {
        // Must not throw.
        DisplayText.SetPreserveCaret(null, "anything");
    }

    [Fact]
    public void Unchanged_content_is_a_no_op_and_leaves_selection_untouched()
    {
        var box = MakeBox("line one\r\nline two\r\nline three");
        box.SelectionStart = 5;
        box.SelectionLength = 3;

        DisplayText.SetPreserveCaret(box, "line one\r\nline two\r\nline three");

        Assert.Equal("line one\r\nline two\r\nline three", box.Text);
        Assert.Equal(5, box.SelectionStart);
        Assert.Equal(3, box.SelectionLength);
    }

    [Fact]
    public void Null_new_text_is_treated_as_empty_string()
    {
        var box = MakeBox("something");
        DisplayText.SetPreserveCaret(box, null);
        Assert.Equal("", box.Text);
    }

    [Fact]
    public void Full_text_replaces_when_no_common_prefix_or_suffix()
    {
        var box = MakeBox("AAAA");
        DisplayText.SetPreserveCaret(box, "ZZZZ");
        Assert.Equal("ZZZZ", box.Text);
    }

    [Fact]
    public void Middle_only_edit_keeps_common_prefix_and_suffix_and_final_text_matches()
    {
        // Common prefix "Altitude: " and suffix " ft" survive; only the number changes.
        var box = MakeBox("Altitude: 2000 ft");
        DisplayText.SetPreserveCaret(box, "Altitude: 3500 ft");
        Assert.Equal("Altitude: 3500 ft", box.Text);
    }

    [Fact]
    public void Caret_line_is_restored_to_the_same_line_index_after_a_same_line_count_edit()
    {
        // Caret sits on line 1 ("line two"). The refresh changes ONLY line 1's text but
        // keeps 3 total lines — the reader must stay on line 1 (not jump to 0).
        var box = MakeBox("line one\r\nline two\r\nline three");
        int line1Start = box.GetFirstCharIndexFromLine(1);
        box.SelectionStart = line1Start + 2; // inside "line two"
        box.SelectionLength = 0;

        DisplayText.SetPreserveCaret(box, "line one\r\nCHANGED\r\nline three");

        Assert.Equal("line one\r\nCHANGED\r\nline three", box.Text);
        int caretLineAfter = box.GetLineFromCharIndex(box.SelectionStart);
        Assert.Equal(1, caretLineAfter);
    }

    [Fact]
    public void Caret_line_clamps_to_the_last_line_when_the_new_text_has_fewer_lines()
    {
        // Caret on line 2 of a 3-line box; the refreshed text only has 1 line — must clamp
        // to line 0 (the only remaining line), never throw or leave a stale index.
        var box = MakeBox("line one\r\nline two\r\nline three");
        int line2Start = box.GetFirstCharIndexFromLine(2);
        box.SelectionStart = line2Start;
        box.SelectionLength = 0;

        DisplayText.SetPreserveCaret(box, "only one line now");

        Assert.Equal("only one line now", box.Text);
        int caretLineAfter = box.GetLineFromCharIndex(box.SelectionStart);
        Assert.Equal(0, caretLineAfter);
    }

    [Fact]
    public void ReadOnly_box_is_still_updated_and_restored_to_ReadOnly_true()
    {
        var box = MakeBox("Fuel: 5000 kg", readOnly: true);
        DisplayText.SetPreserveCaret(box, "Fuel: 6000 kg");

        Assert.Equal("Fuel: 6000 kg", box.Text);
        Assert.True(box.ReadOnly);
    }

    [Fact]
    public void Growing_text_keeps_caret_on_the_same_line_index_when_a_line_is_appended_after_it()
    {
        var box = MakeBox("line one\r\nline two");
        int line1Start = box.GetFirstCharIndexFromLine(1);
        box.SelectionStart = line1Start;
        box.SelectionLength = 0;

        DisplayText.SetPreserveCaret(box, "line one\r\nline two\r\nline three");

        Assert.Equal("line one\r\nline two\r\nline three", box.Text);
        int caretLineAfter = box.GetLineFromCharIndex(box.SelectionStart);
        Assert.Equal(1, caretLineAfter);
    }
}
