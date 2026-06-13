
namespace MSFSBlindAssist.Forms;
public partial class HotkeyListForm : Form
{
    private const string AllCategoriesLabel = "All Categories";
    private const string ShowAllModesLabel = "All hotkeys";
    private const string ShowOutputModeLabel = "Output hotkeys";
    private const string ShowInputModeLabel = "Input hotkeys";

    private Label modeLabel = null!;
    private ComboBox modeComboBox = null!;
    private Label searchLabel = null!;
    private ComboBox searchComboBox = null!;
    private TextBox hotkeyTextBox = null!;
    private Button okButton = null!;
    private readonly string aircraftCode;
    private string fullHotkeyListText = string.Empty;
    private readonly List<CategorySection> categorySections = new();
    private bool updatingSearchChoices;

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

        modeLabel = new Label
        {
            Location = new Point(20, 20),
            Size = new Size(80, 20),
            Text = "Show:"
        };

        modeComboBox = new ComboBox
        {
            Location = new Point(105, 17),
            Size = new Size(465, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Show Hotkey Mode",
            AccessibleDescription = "Choose whether to show all hotkeys, output hotkeys, or input hotkeys"
        };
        modeComboBox.Items.AddRange(new object[]
        {
            ShowAllModesLabel,
            ShowOutputModeLabel,
            ShowInputModeLabel
        });
        modeComboBox.SelectedIndex = 0;
        modeComboBox.SelectedIndexChanged += FilterControls_Changed;

        searchLabel = new Label
        {
            Location = new Point(20, 52),
            Size = new Size(80, 20),
            Text = "Search:"
        };

        searchComboBox = new ComboBox
        {
            Location = new Point(105, 49),
            Size = new Size(465, 25),
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            AccessibleName = "Search by category or keyword",
            AccessibleDescription = "Type a category or command keyword to filter the hotkey list"
        };
        searchComboBox.Items.Add(AllCategoriesLabel);
        AddSearchChoicesForMode(selectedMode: null);
        searchComboBox.SelectedIndex = 0;
        // TextChanged fires for both typing and dropdown selections (Text is
        // updated when SelectedIndex changes), so a single subscription covers
        // both paths without filtering twice on dropdown picks.
        searchComboBox.TextChanged += FilterControls_Changed;
        searchComboBox.KeyDown += SearchComboBox_KeyDown;

        // Hotkey TextBox (read-only, multi-line, tabbable)
        hotkeyTextBox = new TextBox
        {
            Text = fullHotkeyListText,
            Font = new Font("Consolas", 9),
            Location = new Point(20, 82),
            Size = new Size(550, 328),
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
            modeLabel, modeComboBox, searchLabel, searchComboBox, hotkeyTextBox, okButton
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
            { "PMDG_777", "PMDG_777_Hotkeys.txt" },
            { "PMDG_737", "PMDG_737_Hotkeys.txt" },
            { "HS_787", "HS787_Hotkeys.txt" }
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
        modeComboBox.TabIndex = 0;
        searchComboBox.TabIndex = 1;
        hotkeyTextBox.TabIndex = 2;
        okButton.TabIndex = 3;

        // Focus and bring window to front when opened
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front
            searchComboBox.Focus();
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
            searchComboBox.Focus();
            searchComboBox.SelectAll();
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

    private void FilterControls_Changed(object? sender, EventArgs e)
    {
        if (updatingSearchChoices)
        {
            return;
        }

        if (sender == modeComboBox)
        {
            UpdateSearchChoicesForSelectedMode();
        }

        ApplyFilters();
    }

    private void SearchComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            ApplyFilters();
            hotkeyTextBox.Focus();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void ApplyFilters()
    {
        string search = searchComboBox.Text.Trim();
        HotkeyMode? selectedMode = GetSelectedMode();

        // Empty, exact match, or any case-insensitive prefix of "All Categories"
        // at least 3 chars long (covers "all", "all c", "All Categ", etc.).
        // The 3-char floor prevents "a"/"al" from short-circuiting filters
        // for categories that start with those letters (e.g. Altitude, Airspeed).
        bool isAllCategoriesSentinel =
            string.IsNullOrEmpty(search)
            || (search.Length >= 3
                && AllCategoriesLabel.StartsWith(search, StringComparison.OrdinalIgnoreCase));

        if (isAllCategoriesSentinel && selectedMode is null)
        {
            hotkeyTextBox.Text = fullHotkeyListText;
            hotkeyTextBox.SelectionStart = 0;
            hotkeyTextBox.SelectionLength = 0;
            hotkeyTextBox.ScrollToCaret();
            return;
        }

        var matchingSections = categorySections
            .Where(section => selectedMode is null || section.Mode == selectedMode)
            .Where(section => isAllCategoriesSentinel || section.Matches(search))
            .OrderBy(section => section.ModeOrder)
            .ThenBy(section => section.CategoryName)
            .ToList();

        if (matchingSections.Count == 0)
        {
            // Mention the active mode so a keyword that exists only in the
            // other mode doesn't read as "no such hotkey anywhere".
            string modeSuffix = selectedMode switch
            {
                HotkeyMode.Output => " in output hotkeys",
                HotkeyMode.Input => " in input hotkeys",
                _ => string.Empty
            };
            hotkeyTextBox.Text = $"No hotkeys match \"{search}\"{modeSuffix}.";
            return;
        }

        hotkeyTextBox.Text = string.Join(
            "\r\n\r\n",
            matchingSections.Select(section => isAllCategoriesSentinel
                ? section.SectionText
                : section.GetFilteredText(search)));
        hotkeyTextBox.SelectionStart = 0;
        hotkeyTextBox.SelectionLength = 0;
        hotkeyTextBox.ScrollToCaret();
    }

    private HotkeyMode? GetSelectedMode()
    {
        return modeComboBox.Text switch
        {
            ShowOutputModeLabel => HotkeyMode.Output,
            ShowInputModeLabel => HotkeyMode.Input,
            _ => null
        };
    }

    private void UpdateSearchChoicesForSelectedMode()
    {
        string currentSearch = searchComboBox.Text.Trim();
        HotkeyMode? selectedMode = GetSelectedMode();
        bool currentSearchIsCategory = categorySections.Any(section =>
            section.DisplayName.Equals(currentSearch, StringComparison.OrdinalIgnoreCase));
        bool currentSearchIsCompatible = categorySections.Any(section =>
            (selectedMode is null || section.Mode == selectedMode)
            && section.DisplayName.Equals(currentSearch, StringComparison.OrdinalIgnoreCase));

        updatingSearchChoices = true;
        try
        {
            // Turn autocomplete off around the Items mutation. Clearing and
            // repopulating Items while AutoCompleteSource.ListItems is active is
            // a known WinForms instability (it can throw or corrupt the
            // suggestion list); restoring the mode rebinds it to the new items.
            AutoCompleteMode previousAutoComplete = searchComboBox.AutoCompleteMode;
            searchComboBox.AutoCompleteMode = AutoCompleteMode.None;
            try
            {
                searchComboBox.Items.Clear();
                searchComboBox.Items.Add(AllCategoriesLabel);
                AddSearchChoicesForMode(selectedMode);
            }
            finally
            {
                searchComboBox.AutoCompleteMode = previousAutoComplete;
            }

            if (string.IsNullOrEmpty(currentSearch)
                || AllCategoriesLabel.Equals(currentSearch, StringComparison.OrdinalIgnoreCase)
                || (currentSearchIsCategory && !currentSearchIsCompatible))
            {
                searchComboBox.SelectedIndex = 0;
            }
            else
            {
                searchComboBox.Text = currentSearch;
            }
        }
        finally
        {
            updatingSearchChoices = false;
        }
    }

    private void AddSearchChoicesForMode(HotkeyMode? selectedMode)
    {
        foreach (var section in categorySections
            .Where(section => selectedMode is null || section.Mode == selectedMode)
            .OrderBy(section => section.ModeOrder)
            .ThenBy(section => section.CategoryName))
        {
            searchComboBox.Items.Add(section.DisplayName);
        }
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

        public string SearchText => $"{DisplayName} {CategoryName} {SectionText}";

        public bool Matches(string searchText)
        {
            return SearchText.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        }

        public string GetFilteredText(string searchText)
        {
            if (DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || CategoryName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            {
                return SectionText;
            }

            var lines = SectionText.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0)
            {
                return SectionText;
            }

            var filteredLines = new List<string> { lines[0] };
            foreach (var block in GetCommandBlocks(lines))
            {
                if (block.Any(line => line.Contains(searchText, StringComparison.OrdinalIgnoreCase)))
                {
                    filteredLines.AddRange(block);
                }
            }

            return NormalizeLineEndings(string.Join("\n", filteredLines));
        }

        private static IEnumerable<List<string>> GetCommandBlocks(string[] lines)
        {
            var currentBlock = new List<string>();

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentBlock.Count > 0)
                    {
                        yield return currentBlock;
                        currentBlock = new List<string>();
                    }

                    continue;
                }

                if (StartsNewHotkeyLine(line))
                {
                    if (currentBlock.Count > 0)
                    {
                        yield return currentBlock;
                    }

                    currentBlock = new List<string> { line };
                    continue;
                }

                if (currentBlock.Count > 0)
                {
                    currentBlock.Add(line);
                }
                else
                {
                    currentBlock = new List<string> { line };
                }
            }

            if (currentBlock.Count > 0)
            {
                yield return currentBlock;
            }
        }

        // Hotkey entries sit at the shallow base indent (the guides use two
        // spaces). Wrapped description text is indented far deeper to align
        // under the description column (~13 spaces). This ceiling separates the
        // two with headroom for guides that indent keys a little more.
        private const int MaxHotkeyLineIndent = 6;

        private static bool StartsNewHotkeyLine(string line)
        {
            string trimmedLine = line.TrimStart();
            if (line.Length == 0 || !char.IsWhiteSpace(line[0]) || trimmedLine.Length == 0)
            {
                return false;
            }

            // Requiring a shallow indent keeps a capitalized continuation line
            // ("Approach button shows...", "Hand Fly mode does...") attached to
            // its hotkey's block instead of being split into an orphan block.
            int indent = line.Length - trimmedLine.Length;
            if (indent > MaxHotkeyLineIndent)
            {
                return false;
            }

            // The first token must also look like a key — a digit, an uppercase
            // letter, or a chord/separator (+, /) — so base-indent prose notes
            // and bullets ("(Open the form ...)", "- MCP value changes") stay
            // with the preceding block. Note guides aren't uniform: some hotkey
            // lines use a single space before the description ("M Read Mach
            // Number."), so don't gate on the key/description gap width.
            string firstToken = trimmedLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            char firstChar = firstToken[0];

            return char.IsDigit(firstChar)
                || char.IsUpper(firstChar)
                || firstToken.Contains('+')
                || firstToken.Contains('/');
        }
    }
}
