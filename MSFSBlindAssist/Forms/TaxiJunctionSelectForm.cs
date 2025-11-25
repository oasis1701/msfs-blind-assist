using System.Runtime.InteropServices;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Form for selecting a taxiway option at a junction
/// </summary>
public partial class TaxiJunctionSelectForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private ListBox _optionsListBox = null!;
    private Label _headerLabel = null!;
    private Button _selectButton = null!;
    private Button _cancelButton = null!;

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly IntPtr _previousWindow;
    private readonly List<JunctionOption> _options;

    public JunctionOption? SelectedOption { get; private set; }

    public TaxiJunctionSelectForm(List<JunctionOption> options, ScreenReaderAnnouncer announcer, double distanceFeet = 0)
    {
        _previousWindow = GetForegroundWindow();
        _announcer = announcer;
        _options = options;

        InitializeComponent(distanceFeet);
        SetupAccessibility();
        PopulateOptions();
    }

    private void InitializeComponent(double distanceFeet)
    {
        Text = "Junction - Select Taxiway";
        Size = new Size(400, 280);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;

        // Header label
        string headerText = distanceFeet > 0
            ? $"Junction ahead ({(int)distanceFeet} feet). Select direction:"
            : "Junction. Select direction:";

        _headerLabel = new Label
        {
            Text = headerText,
            Location = new Point(20, 15),
            Size = new Size(350, 25),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            AccessibleName = headerText
        };

        // Options ListBox
        _optionsListBox = new ListBox
        {
            Location = new Point(20, 45),
            Size = new Size(350, 140),
            Font = new Font(Font.FontFamily, 10),
            AccessibleName = "Junction Options",
            AccessibleDescription = "Use arrow keys to select a direction, then press Enter"
        };
        _optionsListBox.SelectedIndexChanged += OptionsListBox_SelectedIndexChanged;
        _optionsListBox.KeyDown += OptionsListBox_KeyDown;
        _optionsListBox.DoubleClick += OptionsListBox_DoubleClick;

        // Select button
        _selectButton = new Button
        {
            Text = "Select",
            Location = new Point(215, 200),
            Size = new Size(75, 30),
            Enabled = false,
            AccessibleName = "Select Direction",
            AccessibleDescription = "Confirm the selected direction"
        };
        _selectButton.Click += SelectButton_Click;

        // Cancel button
        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(295, 200),
            Size = new Size(75, 30),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel junction selection"
        };

        Controls.AddRange(new Control[]
        {
            _headerLabel, _optionsListBox, _selectButton, _cancelButton
        });

        AcceptButton = _selectButton;
        CancelButton = _cancelButton;
    }

    private void SetupAccessibility()
    {
        _optionsListBox.TabIndex = 0;
        _selectButton.TabIndex = 1;
        _cancelButton.TabIndex = 2;

        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;

            // Focus on the list and select first item
            _optionsListBox.Focus();
            if (_optionsListBox.Items.Count > 0)
            {
                _optionsListBox.SelectedIndex = 0;
            }
        };

        // Announce for screen reader
        Shown += (sender, e) =>
        {
            string announcement = $"Junction. {_options.Count} options. Use arrow keys to select.";
            _announcer.AnnounceImmediate(announcement);
        };
    }

    private void PopulateOptions()
    {
        _optionsListBox.Items.Clear();

        foreach (var option in _options)
        {
            _optionsListBox.Items.Add(option);
        }
    }

    private void OptionsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_optionsListBox.SelectedItem is JunctionOption option)
        {
            SelectedOption = option;
            _selectButton.Enabled = true;
        }
        else
        {
            SelectedOption = null;
            _selectButton.Enabled = false;
        }
    }

    private void OptionsListBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && _optionsListBox.SelectedItem != null)
        {
            SelectButton_Click(sender, e);
            e.Handled = true;
        }
        else if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            // Number key shortcuts (1-9)
            int index = e.KeyCode - Keys.D1;
            if (index < _optionsListBox.Items.Count)
            {
                _optionsListBox.SelectedIndex = index;
                SelectButton_Click(sender, e);
                e.Handled = true;
            }
        }
        else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad9)
        {
            // Numpad shortcuts (1-9)
            int index = e.KeyCode - Keys.NumPad1;
            if (index < _optionsListBox.Items.Count)
            {
                _optionsListBox.SelectedIndex = index;
                SelectButton_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    private void OptionsListBox_DoubleClick(object? sender, EventArgs e)
    {
        if (_optionsListBox.SelectedItem != null)
        {
            SelectButton_Click(sender, e);
        }
    }

    private void SelectButton_Click(object? sender, EventArgs e)
    {
        if (SelectedOption != null)
        {
            DialogResult = DialogResult.OK;
            Close();

            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);

        if (DialogResult == DialogResult.Cancel && _previousWindow != IntPtr.Zero)
        {
            SetForegroundWindow(_previousWindow);
        }
    }
}
