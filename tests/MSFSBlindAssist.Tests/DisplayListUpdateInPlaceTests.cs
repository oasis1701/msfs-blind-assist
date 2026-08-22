// Characterization tests for MSFSBlindAssist.Forms.DisplayList.UpdateInPlace — the shared
// in-place ListBox reconciler documented in Forms/DisplayList.cs: rewrites only changed rows,
// grows/shrinks the tail in place (never Clear()), and restores selection BY ROW CONTENT so a
// row inserted/removed above the cursor doesn't silently strand the reader on a different
// parameter.
//
// A plain WinForms ListBox constructs and its Items collection responds fine in this xUnit
// host — no WinForms-in-test limitation was hit for this target. TopIndex is intentionally
// not asserted: an unshown/unsized control reports 0 visible rows, so TopIndex round-tripping
// is not meaningfully testable headless; that behavior is secondary to the documented
// selection-preservation contract this suite pins.

using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class DisplayListUpdateInPlaceTests
{
    [Fact]
    public void Null_listbox_is_a_safe_no_op()
    {
        DisplayList.UpdateInPlace(null!, new[] { "a" });
    }

    [Fact]
    public void Null_lines_is_a_safe_no_op()
    {
        var lb = new ListBox();
        DisplayList.UpdateInPlace(lb, null!);
        Assert.Empty(lb.Items);
    }

    [Fact]
    public void Disposed_listbox_is_a_safe_no_op()
    {
        var lb = new ListBox();
        lb.Dispose();
        DisplayList.UpdateInPlace(lb, new[] { "a" });
    }

    [Fact]
    public void First_populate_adds_all_lines_in_order()
    {
        var lb = new ListBox();
        DisplayList.UpdateInPlace(lb, new[] { "one", "two", "three" });

        Assert.Equal(3, lb.Items.Count);
        Assert.Equal("one", lb.Items[0]);
        Assert.Equal("two", lb.Items[1]);
        Assert.Equal("three", lb.Items[2]);
    }

    [Fact]
    public void First_populate_with_zero_lines_leaves_an_empty_list_untouched()
    {
        var lb = new ListBox();
        DisplayList.UpdateInPlace(lb, Array.Empty<string>());
        Assert.Empty(lb.Items);
    }

    [Fact]
    public void Identical_content_is_a_no_op_and_leaves_selection_untouched()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "a", "b", "c" });
        lb.SelectedIndex = 1;

        DisplayList.UpdateInPlace(lb, new[] { "a", "b", "c" });

        Assert.Equal(3, lb.Items.Count);
        Assert.Equal(1, lb.SelectedIndex);
    }

    [Fact]
    public void Only_the_changed_row_text_is_rewritten()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "a", "b", "c" });

        DisplayList.UpdateInPlace(lb, new[] { "a", "CHANGED", "c" });

        Assert.Equal("a", lb.Items[0]);
        Assert.Equal("CHANGED", lb.Items[1]);
        Assert.Equal("c", lb.Items[2]);
    }

    [Fact]
    public void Growing_tail_appends_new_rows_in_place()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "a", "b" });

        DisplayList.UpdateInPlace(lb, new[] { "a", "b", "c", "d" });

        Assert.Equal(4, lb.Items.Count);
        Assert.Equal("c", lb.Items[2]);
        Assert.Equal("d", lb.Items[3]);
    }

    [Fact]
    public void Shrinking_tail_removes_rows_from_the_end()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "a", "b", "c", "d" });

        DisplayList.UpdateInPlace(lb, new[] { "a", "b" });

        Assert.Equal(2, lb.Items.Count);
        Assert.Equal("a", lb.Items[0]);
        Assert.Equal("b", lb.Items[1]);
    }

    [Fact]
    public void Selection_follows_its_row_when_the_row_set_shifts_above_it()
    {
        // Cursor is on "B". A row is inserted ABOVE it (e.g. a conditional status row
        // appearing) — the selection must follow "B" to its new index, not stay pinned to
        // the old numeric index (which would now point at a different row's text).
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "A", "B", "C" });
        lb.SelectedIndex = 1; // "B"

        DisplayList.UpdateInPlace(lb, new[] { "A", "X", "B", "C" });

        Assert.Equal(2, lb.SelectedIndex);
        Assert.Equal("B", lb.Items[lb.SelectedIndex]);
    }

    [Fact]
    public void Selection_on_duplicate_text_follows_the_occurrence_nearest_the_old_index()
    {
        // Old: ["--","Alpha","--","Bravo"], cursor on index 0's "--". A couple of header rows
        // are inserted above, pushing the two "--" separators to indices 2 and 4 — the
        // reconciler must NOT teleport the cursor to the far occurrence; it must pick the one
        // nearest the old index (2, not 4).
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "--", "Alpha", "--", "Bravo" });
        lb.SelectedIndex = 0;

        DisplayList.UpdateInPlace(lb, new[] { "Header1", "Header2", "--", "Alpha", "--" });

        Assert.Equal(2, lb.SelectedIndex);
    }

    [Fact]
    public void Selection_clamps_to_the_old_index_when_its_text_disappears_entirely()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "A", "B", "C" });
        lb.SelectedIndex = 2; // "C"

        DisplayList.UpdateInPlace(lb, new[] { "X", "Y" }); // "C" is gone; list also shrank

        Assert.Equal(1, lb.SelectedIndex); // Math.Min(2, n-1) = Math.Min(2, 1) = 1
    }

    [Fact]
    public void No_selection_before_the_update_means_no_selection_is_introduced()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "A", "B" });
        Assert.Equal(-1, lb.SelectedIndex);

        DisplayList.UpdateInPlace(lb, new[] { "A", "B", "C" });

        Assert.Equal(-1, lb.SelectedIndex);
    }

    [Fact]
    public void Selection_at_the_same_index_with_unchanged_text_is_kept_without_rescan()
    {
        var lb = new ListBox();
        lb.Items.AddRange(new object[] { "A", "B", "C" });
        lb.SelectedIndex = 1; // "B"

        // "B" stays at index 1 even though other rows around it change.
        DisplayList.UpdateInPlace(lb, new[] { "A2", "B", "C2" });

        Assert.Equal(1, lb.SelectedIndex);
    }
}
