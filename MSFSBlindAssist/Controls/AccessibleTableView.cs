using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MSFSBlindAssist.Controls;

/// <summary>
/// A table the screen reader moves through cell by cell (CORE-20): Up and Down move along a column, Left and
/// Right along a row, and the screen reader's own focus follows the cell, so it reads it as a focus change,
/// never through the app's announcer. Windows' list view (comctl32) draws it, scrolls it and holds its rows,
/// virtual or not; the screen reader is handed this control's own MSAA objects: a table, and a cell per row
/// that reads the row's cell in the current column.
///
/// What a cell reads: arriving in the table, "&lt;column&gt;, &lt;value&gt;, &lt;n&gt; of &lt;rows&gt;"; Up and Down, the
/// value and the position; Left and Right, the column and the value; an empty cell reads "blank". Keys:
/// Up, Down, Page Up, Page Down; Left, Right; Home and End the first and last column, Ctrl+Home and Ctrl+End
/// the first and last row. Ctrl and Alt with an arrow do the same as the arrow alone, so NVDA's table keys
/// (Ctrl+Alt+arrows, which pass through to a table NVDA has no table support for) work too.
///
/// Why not the WinForms ListView's own accessibility: on .NET 10 its UI Automation cells carry no value
/// (dotnet/winforms #15044), so NVDA read only the column header across a row and only the first column
/// down it. Handing the reader comctl32's own list instead (NVDA's sysListView32 support) reads each row
/// whole, but nothing moves across a row: Left and Right only scroll. DataGridView moves cell by cell, but
/// its UI Automation cells are DataItems (fixed inside WinForms), so NVDA says "data item" on every cell,
/// and with UI Automation declined NVDA read a different row on every key. Measured with NVDA 2026.2 in
/// devtools\list-lab and devtools\table-lab (local), 2026-11-05.
///
/// How it works: UI Automation's request (WM_GETOBJECT, UiaRootObjectId) is declined, so NVDA reads the
/// list through MSAA, where this control answers with <see cref="TableAccessibleObject"/>. Its role is a
/// table and its cells' a cell, so NVDA's list-view support, chosen by window class and role, never takes
/// it over. Child n is row n-1, the same numbering comctl32 uses when it raises focus for a row, so the
/// list's own focus events (Up, Down, Tab, a click) land on the right cell. A move that stays on the row (a
/// column, or an edge) raises a name change for the focused cell, which NVDA reads on its focus object; at
/// an edge the name is unchanged and NVDA says nothing.
/// Events go straight to NotifyWinEvent: WinForms' AccessibilityNotifyClients sent none from this control.
///
/// Arriving, NVDA can read the table's name twice before the cell. Windows raises focus for the window and
/// comctl32 then for its row (always: with no focused row it takes the first), the list's window object
/// reports the focus so NVDA accepts both, and NVDA reads both unless they reach it in the same moment.
/// A raw Win32 list view did the same. Not fixable from here without answering for the window object.
/// </summary>
public class AccessibleTableView : ListView
{
    private const int WM_GETOBJECT = 0x003D;
    private const int WM_SETFOCUS = 0x0007;
    private const int UiaRootObjectId = -25;
    private const int LVM_GETSUBITEMRECT = 0x1000 + 56;
    private const int LVM_SCROLL = 0x1000 + 20;
    private const int LVIR_BOUNDS = 0;

    private int _column;
    private bool _sayColumn = true;
    private bool _sayPosition = true;

    public AccessibleTableView()
    {
        View = View.Details;
        FullRowSelect = true;
        HideSelection = false;
        MultiSelect = false;
    }

    /// <summary>The current column, as a position in display order.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CurrentColumn => Math.Clamp(_column, 0, Math.Max(0, Columns.Count - 1));

    private int RowCount => VirtualMode ? VirtualListSize : Items.Count;

    private int CurrentRow => SelectedIndices.Count > 0 ? SelectedIndices[0] : FocusedItem?.Index ?? -1;

    private List<ColumnHeader> ColumnsInDisplayOrder() => Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex).ToList();

    protected override AccessibleObject CreateAccessibilityInstance() => new TableAccessibleObject(this);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_GETOBJECT && (int)(long)m.LParam == UiaRootObjectId)
        {
            DefWndProc(ref m); // no UI Automation provider: the reader takes MSAA, answered below
            return;
        }
        if (m.Msg == WM_SETFOCUS)
        {
            // Arriving reads the column and the position. comctl32 raises focus for its focused row as it takes
            // the focus (row 1 when none is), so that row is made the current one first, while comctl32 does not
            // yet count itself focused and raises nothing for the change.
            _sayColumn = true;
            _sayPosition = true;
            int row = CurrentRow;
            if (row >= 0 && FocusedItem?.Index != row) Items[row].Focused = true;
        }
        base.WndProc(ref m);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Columns.Count == 0 || RowCount == 0 || e.Shift)
        {
            base.OnKeyDown(e);
            return;
        }
        int row = CurrentRow;
        int page = Math.Max(1, ClientSize.Height / Math.Max(1, RowHeight()) - 1);
        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.Left: MoveColumn(CurrentColumn - 1); break;
            case Keys.Right: MoveColumn(CurrentColumn + 1); break;
            case Keys.Home when e.Control && !e.Alt: MoveRow(0); break;
            case Keys.End when e.Control && !e.Alt: MoveRow(RowCount - 1); break;
            case Keys.Home: MoveColumn(0); break;
            case Keys.End: MoveColumn(Columns.Count - 1); break;
            case Keys.Up: MoveRow(row - 1); break;
            case Keys.Down: MoveRow(row + 1); break;
            case Keys.PageUp: MoveRow(row - page); break;
            case Keys.PageDown: MoveRow(row + page); break;
            default: handled = false; break;
        }
        if (handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else
        {
            base.OnKeyDown(e);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var hit = HitTest(e.Location);
        if (hit.Item is null || hit.SubItem is null) return;
        int index = hit.Item.SubItems.IndexOf(hit.SubItem);
        int position = ColumnsInDisplayOrder().FindIndex(c => c.Index == index);
        if (position >= 0 && position != CurrentColumn)
        {
            _column = position;
            _sayColumn = true;
            _sayPosition = false;
            RaiseCellChange();
        }
    }

    /// <summary>To a column of the current row; past the first or last column it stays.</summary>
    private void MoveColumn(int position)
    {
        position = Math.Clamp(position, 0, Columns.Count - 1);
        _column = position;
        _sayColumn = true;
        _sayPosition = false;
        ScrollColumnIntoView(CurrentRow);
        RaiseCellChange();
    }

    /// <summary>To a row in the current column: the list raises focus for it; past the first or last row it stays.</summary>
    private void MoveRow(int row)
    {
        row = Math.Clamp(row, 0, RowCount - 1);
        _sayColumn = false;
        _sayPosition = true;
        if (row == CurrentRow)
        {
            RaiseCellChange();
            return;
        }
        SelectedIndices.Clear();
        SelectedIndices.Add(row);
        Items[row].Focused = true;
        EnsureVisible(row);
        ScrollColumnIntoView(row);
    }

    private void RaiseCellChange()
    {
        int row = CurrentRow;
        if (row >= 0 && IsHandleCreated && Focused) NotifyWinEvent(EVENT_OBJECT_NAMECHANGE, Handle, OBJID_CLIENT, row + 1);
    }

    // Raised straight to Windows: WinForms' AccessibilityNotifyClients sent nothing from this control (no event
    // reached a WinEvent hook, 2026-11-05).
    private const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    private const int OBJID_CLIENT = -4;

    [DllImport("user32.dll")]
    private static extern void NotifyWinEvent(uint eventId, IntPtr hwnd, int idObject, int idChild);

    /// <summary>What the cell at this row in the current column reads.</summary>
    private string CellSpeech(int row)
    {
        var columns = ColumnsInDisplayOrder();
        if (columns.Count == 0 || row < 0 || row >= RowCount) return "";
        var column = columns[CurrentColumn];
        string value = CellText(row, column.Index);
        if (string.IsNullOrWhiteSpace(value)) value = "blank";
        var parts = new List<string>(3);
        if (_sayColumn) parts.Add(column.Text);
        parts.Add(value);
        if (_sayPosition) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{row + 1} of {RowCount}"));
        return string.Join(", ", parts);
    }

    private string CellText(int row, int columnIndex)
    {
        var item = Items[row];
        return columnIndex < item.SubItems.Count ? item.SubItems[columnIndex].Text : "";
    }

    private int RowHeight() => RowCount > 0 ? GetItemRect(Math.Max(0, TopItemIndex())).Height : Font.Height;

    private int TopItemIndex() => TopItem?.Index ?? 0;

    private Rectangle CellBounds(int row)
    {
        var columns = ColumnsInDisplayOrder();
        if (!IsHandleCreated || columns.Count == 0 || row < 0 || row >= RowCount) return Rectangle.Empty;
        var rect = new RECT { top = columns[CurrentColumn].Index, left = LVIR_BOUNDS };
        if (SendMessage(Handle, LVM_GETSUBITEMRECT, row, ref rect) == IntPtr.Zero) return Rectangle.Empty;
        var r = Rectangle.FromLTRB(rect.left, rect.top, rect.right, rect.bottom);
        if (columns[CurrentColumn].Index == 0 && columns.Count > 1)
            r.Width = columns[CurrentColumn].Width; // the first column's rectangle is the whole row
        return r;
    }

    private void ScrollColumnIntoView(int row)
    {
        var cell = CellBounds(row);
        if (cell.IsEmpty) return;
        int dx = cell.Left < 0 ? cell.Left : cell.Right > ClientSize.Width ? Math.Min(cell.Left, cell.Right - ClientSize.Width) : 0;
        if (dx != 0) SendMessage(Handle, LVM_SCROLL, dx, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, ref RECT lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, IntPtr lParam);

    /// <summary>The table as MSAA hands it to the screen reader: child n is row n-1's cell in the current column.</summary>
    private sealed class TableAccessibleObject : ControlAccessibleObject
    {
        private readonly AccessibleTableView _table;

        public TableAccessibleObject(AccessibleTableView table) : base(table) => _table = table;

        public override AccessibleRole Role => AccessibleRole.Table;

        public override string? Name => _table.AccessibleName;

        public override int GetChildCount() => _table.RowCount;

        // One object per row, so every request for a row hands the screen reader the same object and it can
        // tell an event for the cell it already has. Kept small: only the rows asked for lately.
        private readonly Dictionary<int, CellAccessibleObject> _cells = new();

        public override AccessibleObject? GetChild(int index)
        {
            if (index < 0 || index >= _table.RowCount) return null;
            if (!_cells.TryGetValue(index, out var cell))
            {
                if (_cells.Count >= 256) _cells.Clear();
                _cells[index] = cell = new CellAccessibleObject(_table, this, index);
            }
            return cell;
        }

        public override AccessibleObject? GetFocused()
        {
            // The current cell, whether or not WinForms counts the list focused yet: NVDA asks while handling the
            // window's focus event, before it does, and a null answer handed it comctl32's row number, so it read
            // the cell a second time (traced 2026-11-05).
            int row = _table.CurrentRow;
            return row >= 0 ? GetChild(row) : this;
        }

        public override AccessibleObject? HitTest(int x, int y)
        {
            var point = _table.PointToClient(new Point(x, y));
            var item = _table.GetItemAt(point.X, point.Y);
            return item is not null ? GetChild(item.Index) : base.HitTest(x, y);
        }
    }

    private sealed class CellAccessibleObject : AccessibleObject
    {
        private readonly AccessibleTableView _table;
        private readonly AccessibleObject _parent;
        private readonly int _row;

        public CellAccessibleObject(AccessibleTableView table, AccessibleObject parent, int row)
        {
            _table = table;
            _parent = parent;
            _row = row;
        }

        public override string? Name => _table.CellSpeech(_row);

        public override AccessibleRole Role => AccessibleRole.Cell;

        public override AccessibleObject? Parent => _parent;

        public override AccessibleStates State
        {
            get
            {
                // No Selectable or Selected: NVDA said "selected" on every row (one row is always selected).
                var state = AccessibleStates.Focusable;
                if (_row == _table.CurrentRow && _table.Focused) state |= AccessibleStates.Focused;
                return state;
            }
        }

        public override Rectangle Bounds
        {
            get
            {
                var cell = _table.CellBounds(_row);
                return cell.IsEmpty ? Rectangle.Empty : _table.RectangleToScreen(cell);
            }
        }
    }
}
