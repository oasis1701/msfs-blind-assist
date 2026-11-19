using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.BBS757;

// The 757 EFB settings window's content (BBS757EfbForm.cs builds the window and calls BuildSettingsContent): every setting
// of BlueBird's tablet, read from BlueBird's own files (efb_settings.json and each registration's
// liveries\<registration>-opt.json) and changed in them while no simulator process runs. BlueBird reads the files once,
// when the 757 loads, and writes its own copy back over them while it is loaded, so a change takes effect at the next
// load and none can be made while a simulator runs (measured 2026-10-27, [B757-17]). The window holds
// nothing else: the Navigraph sign-in, the nav data update and the SimBrief load are on the tablet window's own pages
// (Bbs757EfbExtras), where the simulator runs.
//
// Top to bottom: the status box (Bbs757SimGuard.StatusText; a read-only TextBox, VAT-13), the Section list (the tablet's
// pages, and ACARS) and the chosen section's rows (Bbs757SettingsRows): a check box for a switch, a label and a choice
// list for the tablet's words, a label and a slider for the random failure frequency, a label and a box for a number or
// text. The EFB colors theme is the one setting offered as no choice: a label and a read-only box with its stored choice
// and the note that non-default color schemes are not supported (B757-21, the owner's rulings of 2026-10-30). Beside the
// box, only while BlueBird's file holds another theme, a Set to Dark blue button writes Dark blue (ui_theme 0) and nothing
// else: the window's one write of ui_theme, and its one button. A registration section starts with the registrations its
// rows apply to. Picking a section rebuilds only the rows' panel; everything else (a save, the simulator closing, the window
// opening) updates the rows in place, so no control is recreated and focus never moves, but from the Set to Dark blue button
// when it goes: to the theme's note.
//
// The settings are writable only while Bbs757SimGuard.SettingsWritable holds, checked when the window opens, on every 1 s
// guard tick while the window shows, and by every write. While it does not, every input stays focusable and readable
// but does not change (a check box does not toggle, a choice list and the slider swallow what would move them, a box is
// read-only, the Set to Dark blue button writes nothing) and an attempt says why, at once (ReadOnlyAttemptText). So does
// a row that waits for another (the announcement categories) or that cannot change for its own state (unreadable,
// automatic). A box is saved by Enter; one left with a change not saved shows the stored value again. A box that can be
// cleared says in its description what empty means (Bbs757EfbSettings.EmptyHint). A save is Bbs757SettingsSave.Run's:
// every key checked first, the writes in order through Bbs757SettingsFile (which logs each save and refusal), the first
// not done everywhere stopping the rest, and the wording; this window reads the files for it, says the outcome and reads
// the files again.
//
// Speech (CORE-7): a check box, a choice list or the slider that saved says nothing, as the reader has said its new
// state; so does the Set to Dark blue button, which goes once its save took, focus moving to the theme's note, which the
// reader then reads as Dark blue; a number or text box says "<label> saved", and "<label> not saved" when left with a
// change not saved; every failure speaks, at once ("Couldn't save <label>: <reason>"); each callout a save switched off
// that showed on is named ("<label> turned off"); the simulator starting or closing while the window shows, or the app no
// longer able to tell whether it runs, is said once (SimChangeText). The pilot's SimBrief ID, Hoppie logon code and
// SayIntentions ACARS key are shown in plain text and never logged: no value, write or row goes into a log line or an
// exception message here.
public partial class BBS757EfbForm
{
    // The rows inside the section panel: the label column, the input column, one row's height and the rows' width.
    private const int RowLeft = 4;
    private const int RowInputLeft = 205;
    private const int RowStep = 30;
    private const int RowWidth = 412;

    // An input's width, from the input column to the rows' right edge.
    private const int RowInputWidth = RowWidth - (RowInputLeft - RowLeft);

    // The EFB colors theme's Set to Dark blue button at the right of its row, while it shows (B757-21): its width, and the gap the
    // note leaves before it.
    private const int DarkBlueWidth = 112;
    private const int DarkBlueGap = 6;

    // A switch's choices are Off, then On (Bbs757SettingKind.OnOff; On's index is Bbs757SettingsRows.OnChoiceIndex).
    private const int OffChoice = 0;

    // How long "<label> not saved" waits after focus leaves a box, so it follows the reader's reading of the new focus.
    private const int NotSavedDelayMs = 400;

    private static readonly Bbs757SettingSection[] Sections = Enum.GetValues<Bbs757SettingSection>();

    // Asked by every write just before it reads and again just before it writes: the simulator may have started since
    // the last tick. A settings file that has gone is the file layer's to refuse.
    private static readonly Func<bool> MayWriteSettings =
        () => Bbs757SimGuard.SettingsWritable(Bbs757SimGuard.Check(), true);

    // The safety copy of each file, made before this window's first write to it.
    private readonly Bbs757SettingsBackup _settingsBackup = new(Bbs757SettingsBackup.DefaultFolder);

    // The rows of the section shown, in the tablet's order (Bbs757EfbSettings.InSection).
    private readonly List<SettingControl> _settingControls = new();

    private TextBox _settingsStatusBox = null!;
    private ComboBox _sectionCombo = null!;
    private Panel _sectionPanel = null!;
    // The registrations line of a registration section; null in the others.
    private TextBox? _registrationsBox;
    private Bbs757SettingSection _shownSection;

    // BlueBird's work folder (the newest settings file's, Bbs757WorkFolder.Find), found again when the window opens and
    // when the simulator closes, and the simulator it belongs to, named only for a pilot with more than one work folder.
    private string? _settingsFolder;
    private string? _settingsSimulatorName;
    // The work folder last written to the log, so a change of folder is logged once.
    private string? _loggedFolder;
    private bool _folderLogged;

    // What the last check found; read-only until a check says otherwise.
    private bool _settingsWritable;
    private bool _settingsFileExists;

    // The simulator state the last check saw; null until the window's first check after it shows, which says nothing (a
    // change while the window was hidden is not news). Never compared with a default: default(Bbs757SimState) is
    // Unknown, on purpose.
    private Bbs757SimState? _lastSimState;

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the window's content on the window itself, top to bottom in tab order: the status box, the Section list and the
    /// panel of the chosen section's rows (General first). Nothing comes before them and no control follows: there are no
    /// tabs, and no buttons but General's Set to Dark blue, in the EFB colors theme's row (B757-21).
    /// </summary>
    private void BuildSettingsContent()
    {
        const int left = 15, fullW = 440, h = 32, gap = 8, statusH = 64;
        int y = 15, tab = 0;

        Controls.Add(new Label { Text = "Status", Location = new Point(left, y), AutoSize = true, TabIndex = tab++ });
        y += 20;
        _settingsStatusBox = new TextBox
        {
            Location = new Point(left, y),
            Size = new Size(fullW, statusH),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Settings status",
            TabIndex = tab++,
        };
        Controls.Add(_settingsStatusBox);
        y += statusH + gap + 4;

        Controls.Add(new Label { Text = "Section", Location = new Point(left, y + 6), AutoSize = true, TabIndex = tab++ });
        _sectionCombo = new ComboBox
        {
            Location = new Point(left + 90, y + 2),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Section",
            TabIndex = tab++,
        };
        foreach (Bbs757SettingSection section in Sections)
            _sectionCombo.Items.Add(Bbs757EfbSettings.SectionName(section));
        _sectionCombo.SelectedIndex = 0;
        _sectionCombo.SelectedIndexChanged += (_, _) => OnSectionPicked();
        Controls.Add(_sectionCombo);
        y += h + gap;

        int panelTop = y;
        _sectionPanel = new Panel
        {
            Location = new Point(left, panelTop),
            Size = new Size(fullW, 300),
            AutoScroll = true,
            TabIndex = tab++,
        };
        Controls.Add(_sectionPanel);
        // The rows take what the window leaves below the section list: now, and whenever the window's size changes.
        void FitSectionPanel() => _sectionPanel.Height = Math.Max(120, ClientSize.Height - panelTop - 10);
        FitSectionPanel();
        Resize += (_, _) => FitSectionPanel();

        ShowSection(Sections[0]);
    }

    // The pilot picked a section: only the rows' panel is rebuilt, and focus stays on the list.
    private void OnSectionPicked()
    {
        if (_disposed) return;
        int index = _sectionCombo.SelectedIndex;
        if (index < 0 || index >= Sections.Length || Sections[index] == _shownSection) return;
        ShowSection(Sections[index]);
    }

    /// <summary>
    /// Replaces the panel's rows with <paramref name="section"/>'s, in the tablet's order, and fills them from the
    /// files. Every section holds its settings' rows and nothing else. The only place rows are made; called when the window
    /// is built and when the pilot picks a section.
    /// </summary>
    private void ShowSection(Bbs757SettingSection section)
    {
        _shownSection = section;
        // Focus is on the list that picked the section; were it on a row (the wheel over the list), it moves there
        // rather than onto a control about to go.
        if (_sectionPanel.ContainsFocus) _sectionCombo.Focus();
        _sectionPanel.SuspendLayout();
        try
        {
            Control[] old = _sectionPanel.Controls.Cast<Control>().ToArray();
            _sectionPanel.Controls.Clear();
            foreach (Control control in old) control.Dispose();
            _settingControls.Clear();
            _registrationsBox = null;
            // The new rows are placed from the top: a panel left scrolled down would shift every one of them.
            _sectionPanel.AutoScrollPosition = Point.Empty;

            IReadOnlyList<Bbs757Setting> settings = Bbs757EfbSettings.InSection(section);
            int y = 4, tab = 0;
            if (settings.Any(s => s.File == Bbs757SettingFile.Registration))
            {
                _sectionPanel.Controls.Add(new Label { Text = "Registrations", Location = new Point(RowLeft, y), AutoSize = true, TabIndex = tab++ });
                _registrationsBox = new TextBox
                {
                    Location = new Point(RowLeft, y + 18),
                    Size = new Size(RowWidth, 25),
                    ReadOnly = true,
                    TabStop = true,
                    AccessibleName = "Registrations",
                    TabIndex = tab++,
                };
                _sectionPanel.Controls.Add(_registrationsBox);
                y += 18 + RowStep;
            }
            foreach (Bbs757Setting setting in settings)
            {
                _settingControls.Add(AddRow(setting, y, ref tab));
                y += RowStep;
            }
        }
        finally
        {
            _sectionPanel.ResumeLayout();
        }
        RefreshSettingValues();
    }

    // One setting's row: a check box for a switch; a label and a choice list for the tablet's words; a label and a slider
    // for a number the tablet sets along a slider; a label and a box for a number or text; for the EFB colors theme, which
    // is never a choice (B757-21), a label, its note in a read-only box and, beside it, the Set to Dark blue button, shown only
    // while the files hold another theme. Each input carries the tablet's label: a check box as its text, the others as their
    // AccessibleName after a label (never a mnemonic: "Boarding & load rate" keeps its "&").
    private SettingControl AddRow(Bbs757Setting setting, int y, ref int tab)
    {
        if (setting.Kind == Bbs757SettingKind.OnOff)
        {
            var check = new CheckBox
            {
                Text = setting.Label,
                UseMnemonic = false,
                AutoSize = true,
                Location = new Point(RowLeft, y + 2),
                TabIndex = tab++,
            };
            var switchRow = new SettingControl(setting, check) { Check = check };
            check.Click += (_, _) => OnSwitchClicked(switchRow);
            _sectionPanel.Controls.Add(check);
            return switchRow;
        }

        _sectionPanel.Controls.Add(new Label
        {
            Text = setting.Label,
            UseMnemonic = false,
            AutoEllipsis = true,
            Location = new Point(RowLeft, y + 4),
            Size = new Size(RowInputLeft - RowLeft - 6, 20),
            TabIndex = tab++,
        });

        if (setting.Path == Bbs757EfbSettings.ThemePath)
        {
            // The EFB colors theme: the stored choice and the note, in a read-only box (VAT-13) with no handler, which no lock
            // opens (ApplyLock passes it by) and no key saves. The tablet reader reads only the default theme, so a light one
            // would leave the pilot without the tablet's controls (B757-21): the window writes ui_theme only through the button
            // beside the note, and only as Dark blue. The button carries its own words, which the reader says (no name over them);
            // ShowDarkBlue shows it while the files hold another theme.
            var note = new TextBox
            {
                Location = new Point(RowInputLeft, y),
                Size = new Size(RowInputWidth, 25),
                ReadOnly = true,
                TabStop = true,
                AccessibleName = setting.Label,
                TabIndex = tab++,
            };
            _sectionPanel.Controls.Add(note);
            var darkBlue = new Button
            {
                Text = Bbs757SettingsRows.SetDarkBlueText,
                UseMnemonic = false,
                Location = new Point(RowLeft + RowWidth - DarkBlueWidth, y - 1),
                Size = new Size(DarkBlueWidth, 27),
                Visible = false,
                TabIndex = tab++,
            };
            var themeRow = new SettingControl(setting, note) { Note = note, DarkBlue = darkBlue };
            darkBlue.Click += (_, _) => OnSetDarkBlueClicked(themeRow);
            _sectionPanel.Controls.Add(darkBlue);
            return themeRow;
        }

        if (setting.Kind == Bbs757SettingKind.Choice)
        {
            var combo = new Bbs757LockableComboBox
            {
                Location = new Point(RowInputLeft, y),
                Size = new Size(RowWidth - (RowInputLeft - RowLeft), 25),
                DropDownWidth = 280,
                AccessibleName = setting.Label,
                TabIndex = tab++,
            };
            foreach (Bbs757SettingChoice choice in setting.Choices) combo.Items.Add(choice.Word);
            var choiceRow = new SettingControl(setting, combo) { Combo = combo };
            combo.SelectionChangeCommitted += (_, _) => OnChoiceCommitted(choiceRow);
            combo.LockedAttempt += (_, _) => SayCannotChange(choiceRow);
            // A row is not shown into an open list (it would move the pilot's highlight): it is shown once the list closes.
            combo.DropDownClosed += (_, _) => ShowRow(choiceRow);
            _sectionPanel.Controls.Add(combo);
            return choiceRow;
        }

        if (setting.Kind == Bbs757SettingKind.Slider)
        {
            IReadOnlyList<double> steps = setting.Steps is { Count: > 0 } held
                ? held
                : throw new InvalidOperationException($"The slider '{setting.Path}' has no steps.");
            var slider = new Bbs757LockableSlider
            {
                Location = new Point(RowInputLeft, y - 2),
                Size = new Size(RowWidth - (RowInputLeft - RowLeft), 28),
                Minimum = 0,
                Maximum = steps.Count - 1,
                StepTexts = steps.Select(step => Bbs757EfbSettings.SliderText(setting, Bbs757Value.OfFloat(step))).ToArray(),
                AccessibleName = setting.Label,
                TabIndex = tab++,
            };
            var sliderRow = new SettingControl(setting, slider) { Slider = slider };
            slider.StepChosen += (_, _) => OnStepChosen(sliderRow);
            slider.LockedAttempt += (_, _) => SayCannotChange(sliderRow);
            _sectionPanel.Controls.Add(slider);
            return sliderRow;
        }

        // A number or a text: saved by Enter in the box; left with a change not saved, the stored value comes back.
        var box = new TextBox
        {
            Location = new Point(RowInputLeft, y),
            Size = new Size(RowWidth - (RowInputLeft - RowLeft), 25),
            MaxLength = setting.Kind == Bbs757SettingKind.Number ? 10 : 64,
            AccessibleName = setting.Label,
            TabIndex = tab++,
        };
        var boxRow = new SettingControl(setting, box) { Box = box };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return && !e.Alt && !e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveBox(boxRow);
            }
        };
        box.Leave += (_, _) => OnBoxLeft(boxRow);
        _sectionPanel.Controls.Add(box);
        return boxRow;
    }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    // The window shows (ShowForm): BlueBird's files are read afresh, and no simulator change is news until the window's next
    // check. A box change not saved, in a window that was still open, is dropped and said first.
    private void OpenSettings()
    {
        DropUnsavedEntry();
        _lastSimState = null;
        ReadSettingsAfresh();
    }

    // The window opens: BlueBird's work folder found again, the simulator checked and the files read.
    private void ReadSettingsAfresh()
    {
        if (_disposed) return;
        ReadSettingsFolder();
        UpdateSettingsGuard();
        RefreshSettingValues();
    }

    private void ReadSettingsFolder()
    {
        IReadOnlyList<string> roots = _workRoots();
        _settingsFolder = ResolveWorkFolder(roots);
        // Named only for a pilot with more than one work folder, so they know which one changes.
        _settingsSimulatorName = _settingsFolder != null && Bbs757WorkFolder.ExistingFolders(roots).Count > 1
            ? Bbs757WorkFolder.SimulatorOf(_settingsFolder)
            : null;
    }

    /// <summary>The work folder now (newest settings file, <see cref="Bbs757WorkFolder.Find(IReadOnlyList{string})"/>); a change is logged once, never a file's content.</summary>
    private string? ResolveWorkFolder(IReadOnlyList<string> roots)
    {
        string? folder = Bbs757WorkFolder.Find(roots);
        if (!_folderLogged || !string.Equals(folder, _loggedFolder, StringComparison.OrdinalIgnoreCase))
        {
            _folderLogged = true;
            _loggedFolder = folder;
            Log.Info("BBS757", folder == null ? "BlueBird work folder: none found." : $"BlueBird work folder: {folder}");
        }
        return folder;
    }

    /// <summary>
    /// Reads BlueBird's files again and shows what they hold in the rows of the section shown, in place: each control's
    /// value, its description (an inactive row's <see cref="Bbs757SettingsRows.InactiveText"/>, a box's
    /// <see cref="Bbs757EfbSettings.EmptyHint"/>) and whether it may change. No control is recreated and focus does not
    /// move. After every save, when the window opens and when the simulator closes.
    /// </summary>
    private void RefreshSettingValues()
    {
        if (_disposed) return;
        using SettingsFiles files = SettingsFiles.Read(_settingsFolder);
        Bbs757SettingsSnapshot snapshot = files.Snapshot;
        IReadOnlyList<Bbs757SettingRow> rows = Bbs757SettingsRows.For(_shownSection, snapshot.Global, snapshot.Registrations);
        for (int i = 0; i < rows.Count && i < _settingControls.Count; i++)
        {
            SettingControl control = _settingControls[i];
            Bbs757SettingRow row = rows[i];
            // Both are the section's settings in the catalog's order; never a row on another setting's control.
            if (row.Setting.Path != control.Setting.Path) continue;
            control.Row = row;
            control.StateReason = Bbs757SettingsSave.CannotChangeReason(row, snapshot);
            ShowRow(control);
        }
        if (_registrationsBox != null)
        {
            string line = Bbs757SettingsRows.RegistrationsLine(files.RegistrationNames);
            if (_registrationsBox.Text != line) _registrationsBox.Text = line;
        }
    }

    // Shows the control's row: its value, its description and whether it may change. Only what differs is set.
    private void ShowRow(SettingControl control)
    {
        if (_disposed || control.Row is not { } row) return;
        string? description = null;
        if (control.Check is { } check)
        {
            CheckState state = row.State != Bbs757RowState.Value ? CheckState.Indeterminate
                : row.ChoiceIndex == Bbs757SettingsRows.OnChoiceIndex ? CheckState.Checked
                : CheckState.Unchecked;
            if (check.CheckState != state) check.CheckState = state;
            // A box that holds neither word (unreadable, or different in the registrations) says what it holds here.
            if (row.State != Bbs757RowState.Value) description = row.Text;
        }
        else if (control.Combo is { } combo)
        {
            if (!combo.DroppedDown) ShowChoice(combo, row);
        }
        else if (control.Box is { } box)
        {
            // Out of the Value state the box shows that state's text, read-only, and it is never parsed.
            if (box.Text != row.Text) box.Text = row.Text;
        }
        else if (control.Slider is { } slider)
        {
            // Out of the Value state the slider stays where it is and reads that state's text.
            slider.ShowStep(row.State == Bbs757RowState.Value ? row.ChoiceIndex : slider.Value, row.Text);
        }
        else if (control.Note is { } note)
        {
            // The EFB colors theme's note, with the stored choice when the files read one, and its way back to Dark blue
            // (B757-21). The note's words come first, so a focus that moves to it reads them.
            string text = Bbs757SettingsRows.ThemeNoteText(row);
            if (note.Text != text) note.Text = text;
            if (control.DarkBlue is { } darkBlue) ShowDarkBlue(note, darkBlue, Bbs757SettingsRows.OffersDarkBlue(row));
        }
        if (row.Inactive && EnabledBy(row.Setting) is { } enabledBy)
            description = JoinDescription(description, Bbs757SettingsRows.InactiveText(enabledBy));
        // What an empty box means, after any other description: off (TOD pause distance) or not set (the identifiers).
        if (control.Box != null && Bbs757EfbSettings.EmptyHint(control.Setting) is { } hint)
            description = JoinDescription(description, hint);
        if (control.Input.AccessibleDescription != description) control.Input.AccessibleDescription = description;
        ApplyLock(control);
    }

    // The EFB colors theme's Set to Dark blue (B757-21): shown beside the note only while the files hold another theme
    // (Bbs757SettingsRows.OffersDarkBlue), the note narrowed to make room for it. When it goes while it is the window's active
    // control (its own press saved Dark blue), the note becomes the active control first, so the reader reads the note, now Dark
    // blue, and focus does not jump on to the next control, where WinForms moves it from a focused control that is hidden. The
    // note keeps that place in a window in the background too, and takes the focus when the window is active again.
    private void ShowDarkBlue(TextBox note, Button button, bool offered)
    {
        if (!offered && ActiveControl == button) ActiveControl = note;
        button.Visible = offered;
        int width = offered ? RowInputWidth - DarkBlueWidth - DarkBlueGap : RowInputWidth;
        if (note.Width != width) note.Width = width;
    }

    // Two descriptions said as one: ". " between them, a space alone after one that ends with its own full stop.
    private static string JoinDescription(string? first, string next) =>
        first == null ? next : first.EndsWith('.') ? $"{first} {next}" : $"{first}. {next}";

    // The tablet's words, and when the row holds none of them (Other, Differs, Unreadable) its text as one more item,
    // selected; that item goes once a word is selected, so the selection never passes through none.
    private static void ShowChoice(ComboBox combo, Bbs757SettingRow row)
    {
        int words = row.Setting.Choices.Count;
        bool extra = row.State != Bbs757RowState.Value || row.ChoiceIndex < 0 || row.ChoiceIndex >= words;
        if (extra)
        {
            if (combo.Items.Count <= words) combo.Items.Add(row.Text);
            else if (!Equals(combo.Items[words], row.Text)) combo.Items[words] = row.Text;
        }
        int selected = extra ? words : row.ChoiceIndex;
        if (combo.SelectedIndex != selected) combo.SelectedIndex = selected;
        while (combo.Items.Count > (extra ? words + 1 : words)) combo.Items.RemoveAt(combo.Items.Count - 1);
    }

    private void ApplyLocks()
    {
        foreach (SettingControl control in _settingControls) ApplyLock(control);
    }

    // Whether the control takes a change now: the settings writable, the row not waiting for another and holding what can
    // be replaced (Bbs757SettingRow.CanEdit), and a box only with its own value in it. A control that does not stays
    // focusable: a check box does not toggle, a choice list and the slider are locked, a box is read-only.
    private void ApplyLock(SettingControl control)
    {
        bool editable = control.Row is { } row && row.CanEdit(_settingsWritable)
            && (control.Box == null || row.State == Bbs757RowState.Value);
        if (control.Check is { } check) check.AutoCheck = editable;
        else if (control.Combo is { } combo) combo.Locked = !editable;
        else if (control.Slider is { } slider) slider.Locked = !editable;
        else if (control.Box is { } box) box.ReadOnly = !editable;
    }

    // ------------------------------------------------------------------
    // The guard
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether BlueBird's settings may change now, on every 1 s guard tick while the window shows and when the window
    /// opens: the simulator checked (<see cref="Bbs757SimGuard.Check()"/>), the settings file looked for, the status box
    /// set and every input locked or unlocked. The first check after the window shows only remembers the simulator's
    /// state. After it, a change is said once (<see cref="SimChangeText"/>): a simulator that starts, a check that starts
    /// failing (the status box's words for it), and a simulator that closes, which also has BlueBird's folder and files
    /// read again and is said only when the settings can then be changed.
    /// </summary>
    private void UpdateSettingsGuard()
    {
        if (_disposed) return;
        Bbs757SimState state = Bbs757SimGuard.Check();
        Bbs757SimState? before = _lastSimState;
        bool changed = before is { } was && was != state;
        _lastSimState = state;
        bool closed = changed && state == Bbs757SimState.NotRunning;
        // BlueBird may have written its files while the simulator ran, or made a work folder's first.
        if (closed) ReadSettingsFolder();

        bool fileExists = _settingsFolder != null && File.Exists(Path.Combine(_settingsFolder, Bbs757WorkFolder.SettingsFile));
        bool writable = Bbs757SimGuard.SettingsWritable(state, fileExists);
        string status = Bbs757SimGuard.StatusText(state, fileExists, _settingsSimulatorName);
        if (_settingsStatusBox.Text != status) _settingsStatusBox.Text = status;

        bool lockChanged = writable != _settingsWritable;
        _settingsWritable = writable;
        _settingsFileExists = fileExists;
        if (closed) RefreshSettingValues();
        else if (lockChanged) ApplyLocks();

        // A background change, said once and queued behind what the reader is saying (CORE-7).
        if (before is { } previous && SimChangeText(previous, state, writable) is { } said) _announcer.Announce(said);
    }

    // The status box's words while the process check fails: also what a change into that state and a blocked attempt say.
    private static readonly string CannotTellText = Bbs757SimGuard.StatusText(Bbs757SimState.Unknown, true, null);

    /// <summary>
    /// What a change of the simulator's state from <paramref name="before"/> to <paramref name="now"/> says, once, or
    /// null: <see cref="Bbs757SimGuard.SimStartedText"/> for a simulator that starts; for a check that starts failing the
    /// status box's words for it ("The app can't tell whether the simulator is running, so BlueBird's settings are
    /// read-only."); <see cref="Bbs757SimGuard.SimClosedText"/> for one that closes, from running or from a failed check,
    /// only when the settings can then be changed (<paramref name="writable"/>: not while BlueBird has made no settings
    /// file yet). No change says nothing. Pure; internal for the tests.
    /// </summary>
    internal static string? SimChangeText(Bbs757SimState before, Bbs757SimState now, bool writable)
    {
        if (before == now) return null;
        return now switch
        {
            Bbs757SimState.Running => Bbs757SimGuard.SimStartedText,
            Bbs757SimState.NotRunning => writable ? Bbs757SimGuard.SimClosedText : null,
            // Unknown, or a state that is none of the three: the check failed, as the status box reads it too.
            _ => CannotTellText,
        };
    }

    /// <summary>
    /// What a change tried while BlueBird's settings are read-only says, at once: with no settings file, the status box's
    /// words for that (closing the simulator is not what is missing); while the process check fails, the status box's words
    /// for that ("The app can't tell whether the simulator is running, so BlueBird's settings are read-only."); with a
    /// simulator running, <see cref="Bbs757SimGuard.LockedText"/>. Pure; internal for the tests.
    /// </summary>
    internal static string ReadOnlyAttemptText(Bbs757SimState state, bool settingsFileExists)
    {
        if (!settingsFileExists) return Bbs757SimGuard.StatusText(Bbs757SimState.NotRunning, false, null);
        return state is Bbs757SimState.Running or Bbs757SimState.NotRunning ? Bbs757SimGuard.LockedText : CannotTellText;
    }

    // Why a change was not made, said at once: the settings read-only, the row waiting for another, or its own state.
    private void SayCannotChange(SettingControl control)
    {
        if (_disposed) return;
        string text;
        if (!_settingsWritable)
        {
            // A state never checked is not known: read-only as a failed check is (Bbs757SimState's default, Unknown).
            text = ReadOnlyAttemptText(_lastSimState ?? Bbs757SimState.Unknown, _settingsFileExists);
        }
        else if (control.Row is { Inactive: true } && EnabledBy(control.Setting) is { } enabledBy)
        {
            text = Bbs757SettingsRows.InactiveText(enabledBy);
        }
        else
        {
            text = Bbs757SettingsSave.CouldNotSave(control.Setting.Label,
                control.StateReason ?? Bbs757SettingsFile.NotInFileReason);
        }
        _announcer.AnnounceImmediate(text);
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    // A check box pressed. One that may change has toggled itself already (AutoCheck); one that may not has not.
    private void OnSwitchClicked(SettingControl control)
    {
        if (_disposed || control.Check is not { } check) return;
        if (!check.AutoCheck)
        {
            SayCannotChange(control);
            return;
        }
        int choice = check.CheckState == CheckState.Checked ? Bbs757SettingsRows.OnChoiceIndex : OffChoice;
        SaveSetting(control.Setting, control.Setting.Choices[choice].Stored);
    }

    // The EFB colors theme's Set to Dark blue pressed (Space or Enter, as on any button, or a click): Dark blue (ui_theme 0)
    // saved through the window's own save, the one write the window makes of the theme (B757-21; B757-17 governs it as every
    // save). Locked as the window's other inputs are: while the settings are read-only a press writes nothing and says why, at
    // once, as a check box that does not toggle does. Its outcome is worded as a choice list's (Bbs757SettingsSave.Run): a
    // failure said at once, a save nothing more (CORE-7), as the button goes and the focus moves to the note, which the reader
    // reads as Dark blue (ShowDarkBlue). A press on a row that no longer offers it (a hidden button) does nothing.
    private void OnSetDarkBlueClicked(SettingControl control)
    {
        if (_disposed || control.Row is not { } row || !Bbs757SettingsRows.OffersDarkBlue(row)) return;
        if (!row.CanEdit(_settingsWritable))
        {
            SayCannotChange(control);
            return;
        }
        SaveSetting(control.Setting, Bbs757EfbSettings.DarkBlueTheme);
    }

    // A choice committed: an arrow key on the closed list, or an item picked from the open one.
    private void OnChoiceCommitted(SettingControl control)
    {
        if (_disposed || control.Combo is not { } combo) return;
        if (combo.Locked)
        {
            // An item picked past the lock (from a list that was open when it came on): the stored value back, and why.
            ShowRow(control);
            SayCannotChange(control);
            return;
        }
        int index = combo.SelectedIndex;
        // The extra item is what the files hold now, not a value to write.
        if (index < 0 || index >= control.Setting.Choices.Count) return;
        SaveSetting(control.Setting, control.Setting.Choices[index].Stored);
    }

    // The slider moved by the pilot: its step saved. A move that got past the lock all the same (a backstop: the slider
    // refuses keys, the wheel and clicks while locked, and lets a drag go when the lock comes on): the stored value back,
    // and why.
    private void OnStepChosen(SettingControl control)
    {
        if (_disposed || control.Slider is not { } slider || control.Setting.Steps is not { } steps) return;
        if (slider.Locked)
        {
            ShowRow(control);
            SayCannotChange(control);
            return;
        }
        if (slider.Value < 0 || slider.Value >= steps.Count) return;
        SaveSetting(control.Setting, Bbs757Value.OfFloat(steps[slider.Value]));
    }

    // Focus left a box, or the window closes or opens again with its focus in one: a change not saved with Enter is
    // dropped, the stored value shown again, and that is said. Said a moment later, queued: the screen reader cuts speech
    // off when it reads where focus went, and that reading comes after the box's Leave.
    private async void OnBoxLeft(SettingControl control)
    {
        if (_disposed || control.Box is not { } box) return;
        if (UnsavedEditText(control.Row, box.Text) is not { } said) return;
        ShowRow(control);
        try
        {
            await Task.Delay(NotSavedDelayMs, Lifetime);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!_disposed) _announcer.Announce(said);
    }

    // The window closes (Escape, Alt+F4, its close box) or opens again over itself (Shift+T, which reads BlueBird's files
    // afresh): the box the window's focus is in raises no Leave, so a change not saved in it is dropped, and said, here.
    // The window's ActiveControl, not Focused: a window in the background has no focus but keeps its control.
    private void DropUnsavedEntry()
    {
        if (_disposed) return;
        if (_settingControls.FirstOrDefault(control => control.Box != null && ActiveControl == control.Box) is { } active)
            OnBoxLeft(active);
    }

    /// <summary>
    /// What leaving a number or text box says when its text is not the stored value's: "&lt;label&gt; not saved" (the
    /// box then shows the stored value again). Null when there is nothing to drop: the text is the stored value's, give
    /// or take spaces at its ends, or the row is in another state than <see cref="Bbs757RowState.Value"/> (its box is
    /// read-only and shows that state's text), or nothing has been read yet. Pure; internal for the tests.
    /// </summary>
    internal static string? UnsavedEditText(Bbs757SettingRow? row, string text) =>
        row is { State: Bbs757RowState.Value } && text.Trim() != row.Text.Trim() ? $"{row.Setting.Label} not saved" : null;

    // Enter in a number or text box: the box's text read as the tablet takes it, then saved.
    private void SaveBox(SettingControl control)
    {
        if (_disposed || control.Box is not { } box) return;
        if (box.ReadOnly)
        {
            SayCannotChange(control);
            return;
        }
        if (!Bbs757EfbSettings.TryParseEntry(control.Setting, box.Text, out Bbs757Value value, out string? reason))
        {
            _announcer.AnnounceImmediate(
                Bbs757SettingsSave.CouldNotSave(control.Setting.Label, reason ?? "type another value"));
            return;
        }
        SaveSetting(control.Setting, value);
    }

    /// <summary>
    /// Saves <paramref name="setting"/> as <paramref name="value"/> through <see cref="Bbs757SettingsSave.Run"/>, which
    /// checks every key first, makes the writes in order and words the outcome; each write asks the simulator guard again
    /// (<see cref="MayWriteSettings"/>) and makes the safety copy first. Then the rows are read again, in place, and the
    /// outcome is said: a failure at once, anything else queued behind the reader; a check box, a choice list or the
    /// slider that saved says nothing more (CORE-7).
    /// </summary>
    private void SaveSetting(Bbs757Setting setting, Bbs757Value value)
    {
        if (_disposed) return;
        Bbs757SettingSaveOutcome outcome;
        try
        {
            outcome = WriteSetting(setting, value);
        }
        catch (Exception ex)
        {
            // The type and where it was thrown, never the message: a message about a value could carry the pilot's codes.
            Log.Error("BBS757",
                $"settings: {setting.Path} not saved: {ex.GetType().Name}{Environment.NewLine}{ex.StackTrace}");
            outcome = new Bbs757SettingSaveOutcome(
                Bbs757SettingsSave.CouldNotSave(setting.Label, Bbs757SettingsFile.UnreadableReason), true, Array.Empty<string>());
        }
        // What the files hold now: a change that failed shows the stored value again.
        RefreshSettingValues();
        if (outcome.Spoken is not { } spoken) return;
        if (outcome.Immediate) _announcer.AnnounceImmediate(spoken);
        else _announcer.Announce(spoken);
    }

    // The files read just before the writes, the rows as shown, and the two writes for the work folder.
    private Bbs757SettingSaveOutcome WriteSetting(Bbs757Setting setting, Bbs757Value value)
    {
        if (_settingsFolder is not { } folder)
        {
            return new Bbs757SettingSaveOutcome(
                Bbs757SettingsSave.CouldNotSave(setting.Label, Bbs757SettingsFile.MissingReason), true, Array.Empty<string>());
        }
        string globalFile = Path.Combine(folder, Bbs757WorkFolder.SettingsFile);
        Bbs757SettingRow[] shown = _settingControls.Select(control => control.Row).OfType<Bbs757SettingRow>().ToArray();
        using SettingsFiles files = SettingsFiles.Read(folder);
        return Bbs757SettingsSave.Run(setting, value, files.Snapshot, shown,
            (path, written) => Bbs757SettingsFile.Save(globalFile, path, written, MayWriteSettings, _settingsBackup,
                MayLogKey(setting, path)),
            (path, written) => Bbs757SettingsFile.SaveToRegistrations(folder, path, written, MayWriteSettings,
                _settingsBackup, MayLogKey(setting, path)),
            line => Log.Info("BBS757", line));
    }

    // Whether a write's value may be logged: the saved setting's and the key's own setting's both may (the linked units
    // is no setting of its own, and goes as Units does). The pilot's identifiers never do.
    private static bool MayLogKey(Bbs757Setting saved, string path) =>
        saved.MayLog && (Bbs757EfbSettings.ByPath(path)?.MayLog ?? true);

    private static Bbs757Setting? EnabledBy(Bbs757Setting setting) =>
        setting.EnabledBy is { } path ? Bbs757EfbSettings.ByPath(path) : null;

    // ------------------------------------------------------------------
    // Rows and files
    // ------------------------------------------------------------------

    // One setting's controls in the window and what they show. Never logged or put in a message: its row's text can be
    // the pilot's codes.
    private sealed class SettingControl
    {
        public SettingControl(Bbs757Setting setting, Control input)
        {
            Setting = setting;
            Input = input;
        }

        public Bbs757Setting Setting { get; }

        // The control the screen reader reads for the setting: the check box, the choice list, the slider or the box.
        public Control Input { get; }

        public CheckBox? Check { get; init; }
        public Bbs757LockableComboBox? Combo { get; init; }
        public Bbs757LockableSlider? Slider { get; init; }
        public TextBox? Box { get; init; }

        // The EFB colors theme's read-only note (B757-21): no Box, so neither a lock nor Enter ever reaches it.
        public TextBox? Note { get; init; }

        // The EFB colors theme's Set to Dark blue, beside its note (B757-21): the window's one button, its lock checked at the press.
        public Button? DarkBlue { get; init; }

        // What the files held at the last read; null before the first.
        public Bbs757SettingRow? Row { get; set; }

        // Why the row cannot change for its own state (unreadable, automatic), in words that follow
        // "Couldn't save <label>: "; null when it can.
        public string? StateReason { get; set; }
    }

    // One read of BlueBird's files, the settings file and every registration's, parsed: for the rows, for why a row
    // cannot change and for the checks before a save (Snapshot). Disposed at once, after which Snapshot is not used;
    // nothing of it is kept or logged.
    private sealed class SettingsFiles : IDisposable
    {
        private readonly JsonDocument? _global;
        private readonly List<JsonDocument> _registrations = new();

        private SettingsFiles(string? folder)
        {
            RegistrationNames = Array.Empty<string>();
            bool globalExists = false;
            if (folder != null)
            {
                string globalFile = Path.Combine(folder, Bbs757WorkFolder.SettingsFile);
                globalExists = File.Exists(globalFile);
                _global = Bbs757SettingsFile.Read(globalFile);
                IReadOnlyList<(string Registration, string Path)> files = Bbs757WorkFolder.RegistrationFiles(folder);
                RegistrationNames = files.Select(f => f.Registration).ToArray();
                foreach (var (_, file) in files)
                {
                    if (Bbs757SettingsFile.Read(file) is { } document) _registrations.Add(document);
                }
            }
            Snapshot = new Bbs757SettingsSnapshot(_global?.RootElement, globalExists,
                _registrations.Select(document => document.RootElement).ToArray(), RegistrationNames.Count);
        }

        /// <summary>The files of the work folder, none when there is no folder.</summary>
        public static SettingsFiles Read(string? folder) => new(folder);

        /// <summary>Every registration BlueBird has a file for, readable or not: a save goes to each.</summary>
        public IReadOnlyList<string> RegistrationNames { get; }

        /// <summary>What the read found, valid until this is disposed.</summary>
        public Bbs757SettingsSnapshot Snapshot { get; }

        public void Dispose()
        {
            _global?.Dispose();
            foreach (JsonDocument document in _registrations) document.Dispose();
        }
    }
}
