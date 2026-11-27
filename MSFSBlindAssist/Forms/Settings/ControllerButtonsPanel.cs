using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Controller Buttons section of the Settings dialog: buttons on a yoke, joystick or
/// throttle that run a hotkey command directly, without the ] or [ step. The panel edits a
/// working copy; the dialog's OK writes it (<see cref="ApplyTo"/>) and MainForm restarts the
/// controller reading from the saved list.</summary>
public class ControllerButtonsPanel : UserControl, ISettingsPanel
{
    private readonly Action<string>? _speak;
    private readonly List<ControllerBinding> _bindings = new();

    private Label introLabel = null!;
    private Label listLabel = null!;
    private ListBox bindingsList = null!;
    private Button addButton = null!;
    private Button removeButton = null!;
    private TextBox statusBox = null!;

    public string TabTitle => "Controller Buttons";

    public ControllerButtonsPanel(Action<string>? speak = null)
    {
        _speak = speak;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        introLabel = new Label
        {
            Text = "Assign buttons on a yoke, joystick or throttle to Blind Assist commands. "
                + "A button runs its command directly, with no ] or [ key first, whichever window is in front.",
            Location = new Point(20, 20),
            Size = new Size(600, 40),
            TabIndex = 0,
        };

        listLabel = new Label
        {
            Text = "Assigned buttons:",
            Location = new Point(20, 70),
            Size = new Size(300, 20),
            TabIndex = 1,
        };

        bindingsList = new ListBox
        {
            Location = new Point(20, 92),
            Size = new Size(600, 240),
            AccessibleName = "Assigned buttons",
            AccessibleDescription = "Each entry is a command, then the controller and button that runs it. Press Delete to remove the selected entry.",
            TabIndex = 2,
        };
        bindingsList.SelectedIndexChanged += (_, _) => UpdateButtons();
        bindingsList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
        };

        addButton = new Button
        {
            Text = "&Add button...",
            Location = new Point(20, 345),
            Size = new Size(140, 32),
            AccessibleDescription = "Choose a command and press the controller button that should run it",
            TabIndex = 3,
        };
        addButton.Click += (_, _) => AddBinding();

        removeButton = new Button
        {
            Text = "&Remove",
            Location = new Point(170, 345),
            Size = new Size(110, 32),
            AccessibleDescription = "Remove the selected assignment",
            TabIndex = 4,
        };
        removeButton.Click += (_, _) => RemoveSelected();

        // Read-only TextBox, not a Label (VAT-13): a screen reader reaches it with Tab.
        statusBox = new TextBox
        {
            Location = new Point(20, 390),
            Size = new Size(600, 60),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            AccessibleName = "Controller buttons status",
            TabIndex = 5,
        };

        Controls.AddRange(new Control[] { introLabel, listLabel, bindingsList, addButton, removeButton, statusBox });
    }

    private void AddBinding()
    {
        using var dialog = new ControllerBindingDialog(_speak, _bindings);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.Result == null) return;

        var added = dialog.Result;
        // The dialog already asked: one button runs one command, so a reassigned button replaces.
        _bindings.RemoveAll(b => b.Matches(added.VendorId, added.ProductId, added.Button));
        _bindings.Add(added);
        RefreshList(select: added);
        bindingsList.Focus();
    }

    private void RemoveSelected()
    {
        if (bindingsList.SelectedIndex < 0 || bindingsList.SelectedIndex >= _bindings.Count) return;
        int index = bindingsList.SelectedIndex;
        var removed = _bindings[index];
        _bindings.RemoveAt(index);
        RefreshList(select: _bindings.Count == 0 ? null : _bindings[Math.Min(index, _bindings.Count - 1)]);
        // The list moves on to another entry, which the reader reads; it cannot say what went.
        _speak?.Invoke($"Removed {Describe(removed)}");
        if (_bindings.Count == 0) addButton.Focus();
    }

    private void RefreshList(ControllerBinding? select)
    {
        bindingsList.BeginUpdate();
        bindingsList.Items.Clear();
        foreach (var binding in _bindings)
            bindingsList.Items.Add(Describe(binding));
        bindingsList.EndUpdate();
        if (select != null) bindingsList.SelectedIndex = _bindings.IndexOf(select);

        statusBox.Text = _bindings.Count switch
        {
            0 => "No controller buttons assigned. Controllers are not read until a button is assigned.",
            1 => "1 controller button assigned.",
            _ => $"{_bindings.Count} controller buttons assigned.",
        };
        UpdateButtons();
    }

    private void UpdateButtons() => removeButton.Enabled = bindingsList.SelectedIndex >= 0;

    internal static string Describe(ControllerBinding binding)
    {
        string command = binding.TryGetAction(out var action)
            ? ControllerActionCatalog.NameOf(action)
            : $"{binding.Action} (no longer available)";
        return $"{command}: {binding.DeviceName}, {ControllerBinding.ButtonLabel(binding.Button)}";
    }

    public void LoadFrom(UserSettings settings)
    {
        _bindings.Clear();
        foreach (var b in settings.ControllerBindings)
        {
            _bindings.Add(new ControllerBinding
            {
                VendorId = b.VendorId, ProductId = b.ProductId, DeviceName = b.DeviceName,
                Button = b.Button, Action = b.Action,
            });
        }
        RefreshList(select: _bindings.Count > 0 ? _bindings[0] : null);
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.ControllerBindings = _bindings.ToList();
    }

    public void OnLeaving() { }
}
