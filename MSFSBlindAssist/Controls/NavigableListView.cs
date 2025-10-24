using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Controls;

/// <summary>
/// ListView with DataGridView-like arrow key navigation for full keyboard accessibility
/// Supports Up/Down for row navigation and Left/Right for column navigation
/// Integrates with ScreenReaderAnnouncer for accessibility
/// </summary>
public class NavigableListView : ListView
{
    private readonly ScreenReaderAnnouncer? _announcer;
    private int _currentColumn = 0;
    private bool _isNavigating = false;

    public NavigableListView()
    {
        // Default ListView settings for grid-like behavior
        View = View.Details;
        FullRowSelect = true;
        GridLines = true;
        HideSelection = false;
        MultiSelect = false;

        // Enable double buffering for smoother rendering
        DoubleBuffered = true;
    }

    public NavigableListView(ScreenReaderAnnouncer announcer) : this()
    {
        _announcer = announcer;
    }

    /// <summary>
    /// Sets the screen reader announcer for accessibility
    /// </summary>
    public void SetAnnouncer(ScreenReaderAnnouncer announcer)
    {
        var field = typeof(NavigableListView).GetField("_announcer",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(this, announcer);
    }

    /// <summary>
    /// Gets the current column index for keyboard navigation
    /// </summary>
    public int CurrentColumn => _currentColumn;

    /// <summary>
    /// Override keyboard handling to implement grid-like navigation
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (View != View.Details || Items.Count == 0)
        {
            base.OnKeyDown(e);
            return;
        }

        // Get currently selected item
        var selectedItem = SelectedItems.Count > 0 ? SelectedItems[0] : null;
        if (selectedItem == null)
        {
            base.OnKeyDown(e);
            return;
        }

        _isNavigating = true;
        bool handled = false;

        switch (e.KeyCode)
        {
            case Keys.Left:
                if (_currentColumn > 0)
                {
                    _currentColumn--;
                    AnnounceCurrentCell(selectedItem);
                    EnsureColumnVisible(_currentColumn);
                    handled = true;
                }
                break;

            case Keys.Right:
                if (_currentColumn < Columns.Count - 1)
                {
                    _currentColumn++;
                    AnnounceCurrentCell(selectedItem);
                    EnsureColumnVisible(_currentColumn);
                    handled = true;
                }
                break;

            case Keys.Up:
                if (selectedItem.Index > 0)
                {
                    var newItem = Items[selectedItem.Index - 1];
                    newItem.Selected = true;
                    newItem.Focused = true;
                    newItem.EnsureVisible();
                    AnnounceCurrentCell(newItem);
                    handled = true;
                }
                break;

            case Keys.Down:
                if (selectedItem.Index < Items.Count - 1)
                {
                    var newItem = Items[selectedItem.Index + 1];
                    newItem.Selected = true;
                    newItem.Focused = true;
                    newItem.EnsureVisible();
                    AnnounceCurrentCell(newItem);
                    handled = true;
                }
                break;

            case Keys.Home:
                if (e.Control)
                {
                    // Ctrl+Home: First row, first column
                    _currentColumn = 0;
                    if (Items.Count > 0)
                    {
                        Items[0].Selected = true;
                        Items[0].Focused = true;
                        Items[0].EnsureVisible();
                        AnnounceCurrentCell(Items[0]);
                        handled = true;
                    }
                }
                else
                {
                    // Home: First column of current row
                    _currentColumn = 0;
                    AnnounceCurrentCell(selectedItem);
                    EnsureColumnVisible(_currentColumn);
                    handled = true;
                }
                break;

            case Keys.End:
                if (e.Control)
                {
                    // Ctrl+End: Last row, last column
                    _currentColumn = Columns.Count - 1;
                    if (Items.Count > 0)
                    {
                        var lastItem = Items[Items.Count - 1];
                        lastItem.Selected = true;
                        lastItem.Focused = true;
                        lastItem.EnsureVisible();
                        AnnounceCurrentCell(lastItem);
                        handled = true;
                    }
                }
                else
                {
                    // End: Last column of current row
                    _currentColumn = Columns.Count - 1;
                    AnnounceCurrentCell(selectedItem);
                    EnsureColumnVisible(_currentColumn);
                    handled = true;
                }
                break;

            case Keys.PageUp:
                // Navigate up one page
                int pageSize = ClientSize.Height / (Items.Count > 0 ? Items[0].Bounds.Height : 20);
                int newIndexUp = Math.Max(0, selectedItem.Index - pageSize);
                if (newIndexUp != selectedItem.Index)
                {
                    Items[newIndexUp].Selected = true;
                    Items[newIndexUp].Focused = true;
                    Items[newIndexUp].EnsureVisible();
                    AnnounceCurrentCell(Items[newIndexUp]);
                    handled = true;
                }
                break;

            case Keys.PageDown:
                // Navigate down one page
                int pageSizeDown = ClientSize.Height / (Items.Count > 0 ? Items[0].Bounds.Height : 20);
                int newIndexDown = Math.Min(Items.Count - 1, selectedItem.Index + pageSizeDown);
                if (newIndexDown != selectedItem.Index)
                {
                    Items[newIndexDown].Selected = true;
                    Items[newIndexDown].Focused = true;
                    Items[newIndexDown].EnsureVisible();
                    AnnounceCurrentCell(Items[newIndexDown]);
                    handled = true;
                }
                break;
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

        _isNavigating = false;
    }

    /// <summary>
    /// Handle mouse clicks to update current column position
    /// </summary>
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        // Determine which column was clicked
        ListViewHitTestInfo hitTest = HitTest(e.Location);
        if (hitTest.Item != null && hitTest.SubItem != null)
        {
            _currentColumn = hitTest.Item.SubItems.IndexOf(hitTest.SubItem);

            // Announce the clicked cell
            if (_announcer != null && !_isNavigating)
            {
                AnnounceCurrentCell(hitTest.Item);
            }
        }
    }

    /// <summary>
    /// Announces the current cell content to screen reader
    /// </summary>
    private void AnnounceCurrentCell(ListViewItem item)
    {
        if (_announcer == null || Columns.Count == 0)
            return;

        string columnName = Columns[_currentColumn].Text;
        string value = _currentColumn == 0
            ? item.Text
            : (_currentColumn < item.SubItems.Count ? item.SubItems[_currentColumn].Text : "");

        // Build announcement string
        string announcement;

        if (string.IsNullOrEmpty(value) || value == "-")
        {
            // Empty cell - just announce column name
            announcement = $"{columnName}, blank";
        }
        else
        {
            // Cell has value - announce column name and value
            announcement = $"{columnName}, {value}";
        }

        _announcer.Announce(announcement);
    }

    /// <summary>
    /// Ensures the specified column is visible by scrolling horizontally if needed
    /// </summary>
    private void EnsureColumnVisible(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count)
            return;

        // Calculate total width of columns before the target column
        int leftPosition = 0;
        for (int i = 0; i < columnIndex; i++)
        {
            leftPosition += Columns[i].Width;
        }

        int rightPosition = leftPosition + Columns[columnIndex].Width;
        int clientWidth = ClientSize.Width;

        // Scroll if column is not fully visible
        if (leftPosition < 0 || rightPosition > clientWidth)
        {
            // ListView doesn't have direct horizontal scroll control
            // It auto-scrolls when items are selected, but we can help by
            // ensuring the selected item is visible
            if (SelectedItems.Count > 0)
            {
                SelectedItems[0].EnsureVisible();
            }
        }
    }

    /// <summary>
    /// Resets the current column to the first column
    /// Call this when refreshing the list or changing data
    /// </summary>
    public void ResetColumnPosition()
    {
        _currentColumn = 0;
    }

    /// <summary>
    /// Selects a specific row and column programmatically
    /// </summary>
    public void SelectCell(int rowIndex, int columnIndex)
    {
        if (rowIndex < 0 || rowIndex >= Items.Count)
            return;

        if (columnIndex < 0 || columnIndex >= Columns.Count)
            return;

        _currentColumn = columnIndex;
        Items[rowIndex].Selected = true;
        Items[rowIndex].Focused = true;
        Items[rowIndex].EnsureVisible();
        EnsureColumnVisible(columnIndex);
    }
}
