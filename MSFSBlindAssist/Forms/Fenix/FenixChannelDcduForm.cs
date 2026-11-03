using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The DCDU's window ("Fenix DCDU"): the captain's data-link display as <see cref="FenixChannelDcduRows"/> words it, with
/// the DCDU's keys on the keyboard and its brightness switch in a combo after the list. It is a class of its own because
/// Fenix windows are tracked one for each type (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
/// <remarks>
/// <para>Keys. The nine keys of <see cref="FenixChannelDcduKeys"/> (the four soft keys, page, message and print) press the
/// panel's button for them through <see cref="FenixA320Definition.PressDcdu"/>; any other key is the base's, so the arrow
/// keys move through the list, F5 reads it again and Escape closes. A soft key is judged on a snapshot taken at the press,
/// never on the last refresh's rows: a message that arrived since relabels a slot. A press that is refused is the one thing
/// the window says ("DCDU not available", "No action on that key."); a press that goes ahead is heard in the refreshed list
/// and never by the key's name ([CORE-7]).</para>
/// <para>A held key presses once. The base asks the window about every auto-repeat too, and an answer key is pressed once
/// for WILCO and again for SEND, so a held Ctrl+1 must not answer and send: an auto-repeat of one of the nine keys is taken
/// and does nothing, no decision, no speech and no press.</para>
/// <para>Brightness. The combo is the switch's own items on Mid. A pick of Dim or Bright steps the switch once
/// (<see cref="FenixA320Definition.SetDcduBrightness"/>), which springs back to the middle on its own, and the combo shows Mid
/// again once it has, <see cref="FenixA320Definition.DcduSpringBackMs"/> after the pick, without writing anything.</para>
/// <para>This window sends nothing itself: every press goes through the definition's handler, and the window never opens the
/// channel ([FNX-21]).</para>
/// </remarks>
internal sealed class FenixChannelDcduForm : FenixChannelDisplayForm
{
    // Bit 30 of a key message's lParam: set when the key was already down, so the message is an auto-repeat. The base reads
    // the same bit for F5.
    private const long KeyWasDown = 0x40000000;

    private const string BrightnessName = "DCDU Brightness";

    // The brightness switch runs 0 Dim, 1 Mid, 2 Bright, and the combo lists it in that order, so an item's index is its
    // position. Mid is where the switch rests.
    private const int MidIndex = 1;

    private readonly FenixChannelMonitor _monitor;
    private readonly FenixA320Definition _definition;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly ComboBox _brightness;
    private readonly System.Windows.Forms.Timer _returnTimer;

    // Whether the key message being processed is an auto-repeat: set by ProcessCmdKey just before the base asks the window.
    private bool _autoRepeat;

    public FenixChannelDcduForm(
        FenixChannelMonitor monitor,
        FenixA320Definition definition,
        SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
        : base(FenixChannelDcdu.DisplayName, FenixChannelDisplay.Dcdu, monitor,
            snapshot => FenixChannelDcduRows.Compose(
                snapshot, problem => monitor.ReportFeatureProblem(FenixChannelDcdu.DisplayName, problem)))
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(simConnect);
        ArgumentNullException.ThrowIfNull(announcer);

        _monitor = monitor;
        _definition = definition;
        _simConnect = simConnect;
        _announcer = announcer;

        _brightness = new ComboBox
        {
            Name = BrightnessName,
            AccessibleName = BrightnessName,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (KeyValuePair<double, string> position in
                 definition.GetVariables()[FenixA320Definition.DcduBrightnessKey].ValueDescriptions.OrderBy(p => p.Key))
        {
            _brightness.Items.Add(position.Value);
        }

        _brightness.SelectedIndex = MidIndex;
        _brightness.SelectionChangeCommitted += OnBrightnessCommitted;
        AddControlAfterList(_brightness);

        _returnTimer = new System.Windows.Forms.Timer { Interval = FenixA320Definition.DcduSpringBackMs };
        _returnTimer.Tick += (_, _) => ReturnToMid();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        _autoRepeat = (msg.LParam.ToInt64() & KeyWasDown) != 0;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override bool HandleWindowKey(Keys keyData)
    {
        if (FenixChannelDcduKeys.Map(keyData, SettingsManager.Current.MCDUUseAlternateLSKKeys) is not { } key) return false;

        // A held key repeats, and each repeat is asked about too: it is the window's and does nothing at all, so that one
        // press never becomes two.
        if (_autoRepeat) return true;

        // Judged on a snapshot of this moment.
        FenixChannelSnapshot snapshot = _monitor.Snapshot(FenixChannelDisplay.Dcdu);
        FenixDcduDecision decision = FenixChannelDcduKeys.Decide(key, snapshot);

        if (decision.Refusal is { } refusal)
            _announcer.AnnounceImmediate(refusal);
        else if (decision.VariableKey is not null)
            _definition.PressDcdu(key, _simConnect, _announcer);

        return true;
    }

    // The pilot's pick: Dim or Bright steps the switch once, and the combo follows it back to the middle. Mid is where the
    // switch already rests, so a pick of it steps nothing. SelectionChangeCommitted is raised only for the pilot's own pick,
    // never when the timer assigns SelectedIndex in ReturnToMid, so the return needs no guard against stepping the switch
    // again (and the form listens to no SelectedIndexChanged, which is raised for both).
    private void OnBrightnessCommitted(object? sender, EventArgs e)
    {
        int position = _brightness.SelectedIndex;
        if (position < 0 || position == MidIndex) return;

        _definition.SetDcduBrightness(position, _simConnect, _announcer);

        // A pick before the last one's spring-back has finished waits for its own.
        _returnTimer.Stop();
        _returnTimer.Start();
    }

    private void ReturnToMid()
    {
        _returnTimer.Stop();
        if (IsDisposed || Disposing) return;

        // A display change only: it writes nothing.
        _brightness.SelectedIndex = MidIndex;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _returnTimer.Dispose();
        base.Dispose(disposing);
    }
}
