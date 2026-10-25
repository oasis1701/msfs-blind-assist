using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// Asks for the condition that arms one Fenix failure, with the Fenix EFB's own fields: above IAS, above and
/// below altitude AMSL, an event and the seconds after it. Pre-filled from the failure's current condition.
/// "Arm" validates (FenixFailureRequests.TryBuildCondition) and closes with OK and <see cref="Condition"/>
/// set; a refused field is spoken and focused. The dialog itself never writes: the Failures window confirms
/// and saves.
/// </summary>
public sealed class FenixFailuresArmDialog : Form
{
    private const string SecondsCaption = "&Seconds until failure";
    private const string SecondsFromEventCaption = "&Seconds from event until failure";

    // Combo order: "None", "V1", "Take-off", "Landing" -> the wire values.
    private static readonly string?[] EventValues = { null, "V1", "Takeoff", "Landing" };

    private readonly ScreenReaderAnnouncer _announcer;
    private readonly TextBox _ias;
    private readonly TextBox _altAbove;
    private readonly TextBox _altBelow;
    private readonly TextBox _seconds;
    private readonly Label _secondsLabel;
    private readonly ComboBox _event;

    /// <summary>The validated condition; set only when the dialog closes with OK.</summary>
    public FenixFailureCondition? Condition { get; private set; }

    public FenixFailuresArmDialog(FenixFailure failure, ScreenReaderAnnouncer announcer)
    {
        _announcer = announcer;

        Text = "Arm " + failure.Title;
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
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
        };
        int tab = 0;

        FenixFailureCondition? current = failure.Condition;
        _ias = AddField(layout, "Above &IAS", Number(current?.Ias), ref tab, out _);
        _altAbove = AddField(layout, "Above &altitude AMSL", Number(current?.AltAbove), ref tab, out _);
        _altBelow = AddField(layout, "&Below altitude AMSL", Number(current?.AltBelow), ref tab, out _);

        var eventLabel = new Label { Text = "After &event", AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = tab++ };
        _event = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "After event",
            Width = 160,
            TabIndex = tab++,
        };
        _event.Items.AddRange(new object[] { "None", "V1", "Take-off", "Landing" });
        int eventIndex = Array.IndexOf(EventValues, current?.AfterEvent);
        _event.SelectedIndex = eventIndex < 0 ? 0 : eventIndex;
        layout.Controls.Add(eventLabel);
        layout.Controls.Add(_event);

        _seconds = AddField(layout, SecondsCaption, Number(current?.AfterEventSeconds), ref tab, out _secondsLabel);
        _event.SelectedIndexChanged += (_, _) => UpdateSecondsCaption();
        UpdateSecondsCaption();

        var arm = new Button { Text = "Arm", AutoSize = true, TabIndex = tab++ };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel, TabIndex = tab++ };
        arm.Click += (_, _) => OnArm();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, TabIndex = tab++ };
        buttons.Controls.Add(arm);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        AcceptButton = arm;
        CancelButton = cancel;
    }

    private static TextBox AddField(TableLayoutPanel layout, string caption, string text, ref int tab, out Label label)
    {
        label = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, TabIndex = tab++ };
        var box = new TextBox { Text = text, AccessibleName = caption.Replace("&", ""), Width = 160, TabIndex = tab++ };
        layout.Controls.Add(label);
        layout.Controls.Add(box);
        return box;
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    private string? SelectedEvent => _event.SelectedIndex is >= 0 and < 4 ? EventValues[_event.SelectedIndex] : null;

    private void UpdateSecondsCaption()
    {
        string caption = SelectedEvent == null ? SecondsCaption : SecondsFromEventCaption;
        _secondsLabel.Text = caption;
        _seconds.AccessibleName = caption.Replace("&", "");
    }

    private void OnArm()
    {
        if (FenixFailureRequests.TryBuildCondition(_ias.Text, _altAbove.Text, _altBelow.Text, SelectedEvent, _seconds.Text,
                out FenixFailureCondition? condition, out FenixFailureConditionField errorField, out string? error))
        {
            Condition = condition;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        // The refused field takes focus first, then the error speaks (CORE-7): NVDA cancels speech on a focus
        // change, so speaking first would lose the error.
        TextBox target = errorField switch
        {
            FenixFailureConditionField.AltAbove => _altAbove,
            FenixFailureConditionField.AltBelow => _altBelow,
            FenixFailureConditionField.Seconds => _seconds,
            _ => _ias,
        };
        target.Focus();
        target.SelectAll();
        _announcer.AnnounceImmediate(error ?? "Enter at least one condition.");
    }
}
