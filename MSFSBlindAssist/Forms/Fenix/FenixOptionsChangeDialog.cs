using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// Asks what one Fenix option should be, for this simulator session (F2) or permanently (Shift+F2): a
/// drop-down list of its choices, on the current value (on nothing when the value matches no choice). The
/// combo's accessible name carries the option and which of the two it is, so there is no label for the reader
/// to say twice. OK closes with <see cref="SelectedValue"/> set to the chosen choice's value; Cancel, and OK
/// with nothing chosen, leave it null. The dialog itself never writes: the Fenix options window confirms and
/// sends.
/// </summary>
public sealed class FenixOptionsChangeDialog : Form
{
    private readonly IReadOnlyList<FenixOptionChoice> _choices;
    private readonly ComboBox _combo;

    /// <summary>The value of the chosen choice; set only when the dialog closes with OK.</summary>
    public string? SelectedValue { get; private set; }

    public FenixOptionsChangeDialog(string optionLabel, IReadOnlyList<FenixOptionChoice> choices, string? currentValue,
        bool permanent = false)
    {
        _choices = choices;

        Text = permanent ? "Make a Fenix option permanent" : "Change Fenix option";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        int tab = 0;

        // A drop-down list, never an editable box: only the Fenix's own choices can be sent.
        _combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = permanent ? $"{optionLabel}, permanent" : $"{optionLabel}, for this simulator session",
            Width = 280,
            TabIndex = tab++,
        };
        foreach (FenixOptionChoice choice in choices) _combo.Items.Add(choice.Label);
        // On the current value; on nothing when it matches no choice, so OK cannot send the first choice by
        // accident (OK with nothing chosen hands back nothing).
        _combo.SelectedIndex = IndexOf(choices, currentValue);
        layout.Controls.Add(_combo);

        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK, TabIndex = tab++ };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, TabIndex = tab++ };
        ok.Click += (_, _) => SelectedValue = Chosen()?.Value;
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, TabIndex = tab++ };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        ActiveControl = _combo;
    }

    // The choice the current value matches (an on/off value in any spelling), else -1.
    private static int IndexOf(IReadOnlyList<FenixOptionChoice> choices, string? currentValue)
    {
        for (int i = 0; i < choices.Count; i++)
            if (FenixOptionCatalog.SameValue(choices[i].Value, currentValue)) return i;
        return -1;
    }

    private FenixOptionChoice? Chosen()
        => _combo.SelectedIndex >= 0 && _combo.SelectedIndex < _choices.Count ? _choices[_combo.SelectedIndex] : null;
}
