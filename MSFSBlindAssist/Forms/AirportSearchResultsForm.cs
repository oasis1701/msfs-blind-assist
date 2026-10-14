using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// The list the EFB's Airport Lookup opens when a name or city search matches more than one
/// airport: one line per airport ("ESGG, Landvetter, Goteborg"), best match first. Enter loads the
/// selected airport, Escape closes without changing anything.
/// </summary>
public sealed class AirportSearchResultsForm : Form
{
    private readonly IReadOnlyList<AirportSearchEntry> _results;
    private readonly ListBox _resultsListBox;

    public AirportSearchEntry? SelectedAirport { get; private set; }

    public AirportSearchResultsForm(string query, IReadOnlyList<AirportSearchEntry> results)
    {
        _results = results;

        Text = $"{results.Count} airports match {query}";
        Size = new Size(520, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var listLabel = new Label { Text = "Matching airports:", Location = new Point(20, 15), Width = 200 };
        _resultsListBox = new ListBox
        {
            Location = new Point(20, 40),
            Size = new Size(460, 220),
            AccessibleName = "Matching airports",
            AccessibleDescription = "Press Enter to load the selected airport"
        };
        foreach (var entry in results)
            _resultsListBox.Items.Add(AirportNameSearch.Describe(entry));
        if (_resultsListBox.Items.Count > 0)
            _resultsListBox.SelectedIndex = 0;

        var loadButton = new Button
        {
            Text = "Load",
            Location = new Point(320, 275),
            Size = new Size(75, 30),
            AccessibleName = "Load Airport"
        };
        loadButton.Click += (s, e) => Accept();

        var cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(405, 275),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel"
        };

        Controls.AddRange(new Control[] { listLabel, _resultsListBox, loadButton, cancelButton });
        AcceptButton = loadButton;
        CancelButton = cancelButton;

        _resultsListBox.DoubleClick += (s, e) => Accept();
        Shown += (s, e) => _resultsListBox.Focus();
    }

    private void Accept()
    {
        int index = _resultsListBox.SelectedIndex;
        if (index < 0 || index >= _results.Count)
            return;
        SelectedAirport = _results[index];
        DialogResult = DialogResult.OK;
        Close();
    }
}
