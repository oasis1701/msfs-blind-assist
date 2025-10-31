
namespace MSFSBlindAssist.Forms;
public partial class HotkeyListForm : Form
{
    private TextBox hotkeyTextBox = null!;
    private Button okButton = null!;
    private readonly string aircraftCode;

    public HotkeyListForm(string aircraftCode)
    {
        this.aircraftCode = aircraftCode;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Hotkey List";
        Size = new Size(600, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        // Hotkey TextBox (read-only, multi-line, tabbable)
        hotkeyTextBox = new TextBox
        {
            Text = GetHotkeyListText(),
            Font = new Font("Consolas", 9),
            Location = new Point(20, 20),
            Size = new Size(550, 390),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            BorderStyle = BorderStyle.Fixed3D,
            BackColor = System.Drawing.SystemColors.Control,
            ScrollBars = ScrollBars.Vertical,
            AccessibleName = "Hotkey List",
            AccessibleDescription = "Complete list of all available hotkeys organized by output and input modes"
        };

        // OK Button
        okButton = new Button
        {
            Text = "OK",
            Location = new Point(250, 430),
            Size = new Size(90, 35),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close Hotkey List Dialog",
            AccessibleDescription = "Close the Hotkey List window"
        };
        okButton.Click += OkButton_Click;

        // Add controls to form
        Controls.AddRange(new Control[]
        {
            hotkeyTextBox, okButton
        });

        AcceptButton = okButton;
        CancelButton = okButton;
    }

    private string GetHotkeyListText()
    {
        // Map aircraft codes to hotkey guide filenames
        var filenameMap = new Dictionary<string, string>
        {
            { "A320", "FBW_A320_Hotkeys.txt" },
            { "FENIX_A320CEO", "Fenix_A320_Hotkeys.txt" }
        };

        // Determine which file to load
        string filename = filenameMap.ContainsKey(aircraftCode)
            ? filenameMap[aircraftCode]
            : "FBW_A320_Hotkeys.txt"; // Default fallback

        // Construct file path
        string appPath = AppDomain.CurrentDomain.BaseDirectory;
        string filePath = Path.Combine(appPath, "HotkeyGuides", filename);

        try
        {
            if (File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
            else
            {
                return $"Error: Hotkey guide file not found.\n\nExpected file: {filePath}\n\nPlease ensure hotkey guide files are included in the application directory.";
            }
        }
        catch (Exception ex)
        {
            return $"Error loading hotkey guide:\n\n{ex.Message}\n\nFile: {filePath}";
        }
    }

    private void SetupAccessibility()
    {
        // Set tab order for logical navigation
        hotkeyTextBox.TabIndex = 0;
        okButton.TabIndex = 1;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            hotkeyTextBox.Focus();
        };
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }
}
