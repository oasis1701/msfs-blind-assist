
namespace MSFSBlindAssist.Forms;
public partial class HotkeyListForm : Form
{
    private const string AllCategoriesLabel = "All Categories";

    private ComboBox categoryComboBox = null!;
    private TextBox hotkeyTextBox = null!;
    private Button okButton = null!;
    private readonly string aircraftCode;
    private string fullHotkeyListText = string.Empty;
    private readonly List<CategorySection> categorySections = new();

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

        fullHotkeyListText = GetHotkeyListText();
        PopulateCategorySections(fullHotkeyListText);

        categoryComboBox = new ComboBox
        {
            Location = new Point(20, 20),
            Size = new Size(550, 25),
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            AccessibleName = "Hotkey Category Search",
            AccessibleDescription = "Type or choose a hotkey category to filter the list"
        };
        categoryComboBox.Items.Add(AllCategoriesLabel);
        foreach (var section in categorySections
            .OrderBy(section => section.ModeOrder)
            .ThenBy(section => section.CategoryName))
        {
            categoryComboBox.Items.Add(section.DisplayName);
        }
        categoryComboBox.SelectedIndex = 0;
        // TextChanged fires for both typing and dropdown selections (Text is
        // updated when SelectedIndex changes), so a single subscription covers
        // both paths without filtering twice on dropdown picks.
        categoryComboBox.TextChanged += CategoryComboBox_TextChanged;
        categoryComboBox.KeyDown += CategoryComboBox_KeyDown;

        // Hotkey TextBox (read-only, multi-line, tabbable)
        hotkeyTextBox = new TextBox
        {
            Text = fullHotkeyListText,
            Font = new Font("Consolas", 9),
            Location = new Point(20, 55),
            Size = new Size(550, 355),
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
            categoryComboBox, hotkeyTextBox, okButton
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
            { "FBW_A380", "FBW_A380_Hotkeys.txt" },
            { "FENIX_A320CEO", "Fenix_A320_Hotkeys.txt" },
            { "PMDG_777", "PMDG_777_Hotkeys.txt" }
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
                string text = File.ReadAllText(filePath);
                // Normalize line endings for Windows Forms TextBox
                return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
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
        categoryComboBox.TabIndex = 0;
        hotkeyTextBox.TabIndex = 1;
        okButton.TabIndex = 2;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            categoryComboBox.Focus();
        };
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            categoryComboBox.Focus();
            categoryComboBox.SelectAll();
            return true;
        }

        // Handle Escape key
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }

    private void CategoryComboBox_TextChanged(object? sender, EventArgs e)
    {
        ApplyCategoryFilter(categoryComboBox.Text);
    }

    private void CategoryComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyCategoryFilter(categoryComboBox.Text);
            hotkeyTextBox.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ApplyCategoryFilter(string categoryText)
    {
        string category = categoryText.Trim();

        // Empty, exact match, or any case-insensitive prefix of "All Categories"
        // at least 3 chars long (covers "all", "all c", "All Categ", etc.).
        // The 3-char floor prevents "a"/"al" from short-circuiting filters
        // for categories that start with those letters (e.g. Altitude, Airspeed).
        bool isAllCategoriesSentinel =
            string.IsNullOrEmpty(category)
            || (category.Length >= 3
                && AllCategoriesLabel.StartsWith(category, StringComparison.OrdinalIgnoreCase));

        if (isAllCategoriesSentinel)
        {
            hotkeyTextBox.Text = fullHotkeyListText;
            hotkeyTextBox.SelectionStart = 0;
            hotkeyTextBox.SelectionLength = 0;
            hotkeyTextBox.ScrollToCaret();
            return;
        }

        var matchingSections = categorySections
            .Where(section => section.SearchText.Contains(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(section => section.ModeOrder)
            .ThenBy(section => section.CategoryName)
            .ToList();

        if (matchingSections.Count == 0)
        {
            hotkeyTextBox.Text = $"No hotkey categories match \"{category}\".";
            return;
        }

        // SectionText already starts with the category heading line (e.g.
        // "FCU Controls:"). The dropdown shows the activation-mode prefix
        // (Output ] / Input [) when the user picks, so prepending DisplayName
        // here would duplicate the heading in the text body.
        hotkeyTextBox.Text = string.Join(
            "\r\n\r\n",
            matchingSections.Select(section => section.SectionText));
        hotkeyTextBox.SelectionStart = 0;
        hotkeyTextBox.SelectionLength = 0;
        hotkeyTextBox.ScrollToCaret();
    }

    private void PopulateCategorySections(string hotkeyText)
    {
        categorySections.Clear();

        string[] lines = hotkeyText.Replace("\r\n", "\n").Split('\n');
        string? currentCategory = null;
        HotkeyMode currentMode = HotkeyMode.General;
        var currentSectionLines = new List<string>();

        foreach (string line in lines)
        {
            if (IsOutputModeMarker(line))
            {
                AddCurrentSection();
                currentMode = HotkeyMode.Output;
                continue;
            }

            if (IsInputModeMarker(line))
            {
                AddCurrentSection();
                currentMode = HotkeyMode.Input;
                continue;
            }

            if (IsCategoryHeading(line))
            {
                AddCurrentSection();
                currentCategory = line.Trim().TrimEnd(':');
                currentSectionLines.Add(line);
                continue;
            }

            currentSectionLines.Add(line);
        }

        AddCurrentSection();

        void AddCurrentSection()
        {
            if (currentCategory is null)
            {
                currentSectionLines.Clear();
                return;
            }

            string sectionText = NormalizeLineEndings(string.Join("\n", currentSectionLines).Trim());
            if (!string.IsNullOrWhiteSpace(sectionText))
            {
                categorySections.Add(new CategorySection(currentMode, currentCategory, sectionText));
            }

            currentSectionLines.Clear();
        }
    }

    private static bool IsCategoryHeading(string line)
    {
        // Real categories start at column 0. The Fenix guide contains indented
        // sub-section headings ending in ':' (e.g. "  Right Side (Alt + number):")
        // that would otherwise be promoted to top-level categories.
        if (line.Length == 0 || char.IsWhiteSpace(line[0]))
        {
            return false;
        }

        return line.Length > 1
            && line.EndsWith(':')
            && !line.StartsWith('-')
            && !char.IsDigit(line[0]);
    }

    private static bool IsOutputModeMarker(string line)
    {
        return line.TrimStart().StartsWith("OUTPUT MODE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInputModeMarker(string line)
    {
        return line.TrimStart().StartsWith("INPUT MODE", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private enum HotkeyMode
    {
        Output,
        Input,
        General
    }

    private sealed class CategorySection
    {
        public CategorySection(HotkeyMode mode, string categoryName, string sectionText)
        {
            Mode = mode;
            CategoryName = categoryName;
            SectionText = sectionText;
        }

        public HotkeyMode Mode { get; }
        public string CategoryName { get; }
        public string SectionText { get; }

        public int ModeOrder => Mode switch
        {
            HotkeyMode.Output => 0,
            HotkeyMode.Input => 1,
            _ => 2
        };

        public string DisplayName => Mode switch
        {
            HotkeyMode.Output => $"Output ] - {CategoryName}",
            HotkeyMode.Input => $"Input [ - {CategoryName}",
            _ => $"General - {CategoryName}"
        };

        public string SearchText => $"{DisplayName} {CategoryName}";
    }
}
