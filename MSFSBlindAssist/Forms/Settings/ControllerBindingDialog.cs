using MSFSBlindAssist.Hotkeys;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Assigns one controller button: pick a command, then press "Capture" and press the
/// button. The capture reads the controllers with its own <see cref="JoystickButtonMonitor"/>
/// while MainForm's is stopped (the Settings dialog stops it), so the press that is being
/// captured never also runs the command it was bound to before.
///
/// What was captured is spoken through <c>speak</c> as well as written to the status box: the
/// press happens on the yoke, not on a control the screen reader is watching, so nothing else
/// would tell the pilot that it registered.</summary>
public class ControllerBindingDialog : Form
{
    private const int CaptureTimeoutMs = 15000;

    private readonly Action<string>? _speak;
    private readonly IReadOnlyList<ControllerBinding> _existing;
    private readonly IReadOnlyList<KeyValuePair<HotkeyAction, string>> _actions = ControllerActionCatalog.All();

    private JoystickButtonMonitor? _capture;
    private readonly System.Windows.Forms.Timer _captureTimeout = new() { Interval = CaptureTimeoutMs };
    private ControllerButtonPressedEventArgs? _captured;

    private ComboBox commandCombo = null!;
    private Button captureButton = null!;
    private TextBox statusBox = null!;
    private Button okButton = null!;

    /// <summary>The new binding once the dialog closes with OK.</summary>
    public ControllerBinding? Result { get; private set; }

    public ControllerBindingDialog(Action<string>? speak, IReadOnlyList<ControllerBinding> existing)
    {
        _speak = speak;
        _existing = existing;
        _captureTimeout.Tick += (_, _) => CaptureTimedOut();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "Assign Controller Button";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        ClientSize = new Size(560, 250);

        var commandLabel = new Label
        {
            Text = "&Command:",
            Location = new Point(20, 22),
            Size = new Size(90, 20),
            TabIndex = 0,
        };

        commandCombo = new ComboBox
        {
            Location = new Point(115, 19),
            Size = new Size(425, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Command",
            AccessibleDescription = "The command the button runs. Each name ends with the keyboard keys that run the same command.",
            TabIndex = 1,
        };
        foreach (var entry in _actions)
            commandCombo.Items.Add(entry.Value);
        int taxiStatus = _actions.ToList().FindIndex(a => a.Key == HotkeyAction.TaxiStatus);
        commandCombo.SelectedIndex = Math.Max(0, taxiStatus);

        captureButton = new Button
        {
            Text = "C&apture controller button",
            Location = new Point(20, 60),
            Size = new Size(220, 32),
            AccessibleDescription = "Press, then press the button on your controller within 15 seconds. Escape cancels.",
            TabIndex = 2,
        };
        captureButton.Click += (_, _) => StartCapture();

        // Read-only TextBox, not a Label (VAT-13).
        statusBox = new TextBox
        {
            Location = new Point(20, 105),
            Size = new Size(520, 70),
            Multiline = true,
            ReadOnly = true,
            AccessibleName = "Captured button",
            Text = "No button captured yet.",
            TabIndex = 3,
        };

        okButton = new Button { Text = "OK", Location = new Point(350, 200), Size = new Size(90, 32), TabIndex = 4 };
        okButton.Click += (_, _) => Accept();
        var cancelButton = new Button
        {
            Text = "Cancel", Location = new Point(450, 200), Size = new Size(90, 32),
            DialogResult = DialogResult.Cancel, TabIndex = 5,
        };

        Controls.AddRange(new Control[] { commandLabel, commandCombo, captureButton, statusBox, okButton, cancelButton });
        CancelButton = cancelButton;
    }

    private void StartCapture()
    {
        StopCapture();
        _capture = new JoystickButtonMonitor();
        _capture.ButtonPressed += OnCaptured;
        _capture.Start();
        _captureTimeout.Start();
        SetStatus("Waiting. Press the button on your controller now, or Escape to cancel.");
    }

    private void OnCaptured(object? sender, ControllerButtonPressedEventArgs e)
    {
        StopCapture();
        _captured = e;
        string what = $"{e.DeviceName}, {ControllerBinding.ButtonLabel(e.Button)}";
        var current = _existing.FirstOrDefault(b => b.Matches(e.VendorId, e.ProductId, e.Button));
        string note = current != null && current.TryGetAction(out var action)
            ? $" This button already runs {ControllerActionCatalog.NameOf(action)}; OK replaces it."
            : "";
        SetStatus($"Captured {what}.{note}");
    }

    private void CaptureTimedOut()
    {
        StopCapture();
        SetStatus("No controller button was pressed. Check that the controller is plugged in, then capture again.");
    }

    private void StopCapture()
    {
        _captureTimeout.Stop();
        if (_capture == null) return;
        _capture.ButtonPressed -= OnCaptured;
        _capture.Dispose();
        _capture = null;
    }

    private void SetStatus(string text)
    {
        statusBox.Text = text;
        _speak?.Invoke(text);
    }

    private void Accept()
    {
        if (_captured == null)
        {
            MessageBox.Show(this, "Capture a controller button first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            captureButton.Focus();
            return;
        }
        var action = _actions[commandCombo.SelectedIndex].Key;
        Result = new ControllerBinding
        {
            VendorId = _captured.VendorId,
            ProductId = _captured.ProductId,
            DeviceName = _captured.DeviceName,
            Button = _captured.Button,
            Action = action.ToString(),
        };
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Escape during a capture cancels the capture only; otherwise it closes the dialog.
        if (keyData == Keys.Escape && _capture != null)
        {
            StopCapture();
            SetStatus("Capture cancelled.");
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopCapture();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopCapture();
            _captureTimeout.Dispose();
        }
        base.Dispose(disposing);
    }
}
