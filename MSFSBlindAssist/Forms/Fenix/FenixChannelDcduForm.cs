using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The DCDU's window ("Fenix DCDU"): the captain's data-link display as <see cref="FenixChannelDcduRows"/> words it, with
/// the DCDU's keys on the keyboard and its brightness switch on two buttons after the list, Bright and Dim. It is a class of
/// its own because Fenix windows are tracked one for each type (<see cref="FenixChannelDisplayForm"/>).
/// </summary>
/// <remarks>
/// <para>Keys. The nine keys of <see cref="FenixChannelDcduKeys"/> (the four soft keys, page, message and print) press the
/// panel's button for them through <see cref="FenixA320Definition.PressDcdu"/>; any other key is the base's, so the arrow
/// keys move through the list, F5 reads it again and Escape closes. They are the form's keys, so they work with a button
/// focused as they do on the list. A soft key is judged on a snapshot taken at the press, never on the last refresh's rows: a
/// message that arrived since relabels a slot. A press that is refused is the one thing the window says itself ("DCDU not
/// available", "No action on that key."); a press that goes ahead is heard in the refreshed list and never by the key's name
/// ([CORE-7]), and what the list does not show, the status and the first info line (SENDING, RECEIVD BY ATC, NO MORE MSG), is
/// read back by <see cref="FenixChannelDcduReadback"/>, which every press tells first.</para>
/// <para>A held key presses once. The base asks the window about every auto-repeat too, and an answer key is pressed once
/// for WILCO and again for SEND, so a held Ctrl+1 must not answer and send: an auto-repeat of one of the nine keys is taken
/// and does nothing, no decision, no speech and no press.</para>
/// <para>Brightness. Bright and Dim step the switch once (<see cref="FenixA320Definition.SetDcduBrightness"/>: about 50 in the
/// Fenix's own count of 510 at full), and it springs back to the middle on its own. <see cref="FenixA320Definition.DcduSpringBackMs"/> after the
/// press, when the switch has gone back and the value has moved, the window reads <c>N_DCDU1_BRIGHTNESS</c> and says it
/// ("Brightness 60 percent"): the spoken value is the press's confirmation ([CORE-7]), as an interrupting call like a refusal.
/// Presses in quick succession restart the wait, so only the last is read back; a read that fails or times out says "Brightness
/// not available"; and a window that is gone says nothing. A DCDU turned all the way down still receives a message (measured
/// 2026-11-03), so the window never reads the brightness for the rows: a screen with nothing drawn reads the same at any level.</para>
/// <para>This window sends nothing itself: every press goes through the definition's handler, and the window never opens the
/// channel ([FNX-21]). It reads one L:var, on request, through the manager.</para>
/// </remarks>
internal sealed class FenixChannelDcduForm : FenixChannelDisplayForm
{
    // Bit 30 of a key message's lParam: set when the key was already down, so the message is an auto-repeat. The base reads
    // the same bit for F5.
    private const long KeyWasDown = 0x40000000;

    // The brightness switch runs 0 Dim, 1 Mid, 2 Bright, and rests on Mid; Bright and Dim step it to an end.
    private const int DimPosition = 0;
    private const int BrightPosition = 2;

    // How long a read of the brightness waits for the sim.
    private const int BrightnessReadTimeoutMs = 1500;

    private const string LogCategory = "Fenix";

    private readonly FenixChannelMonitor _monitor;
    private readonly FenixA320Definition _definition;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Button _bright;
    private readonly Button _dim;
    private readonly System.Windows.Forms.Timer _readBackTimer;

    // Whether the key message being processed is an auto-repeat: set by ProcessCmdKey just before the base asks the window.
    private bool _autoRepeat;

    // How many presses of Bright or Dim there have been: a read-back belongs to the press it was started for, and a newer one
    // replaces it.
    private long _press;

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
        ReadFresh = (key, timeoutMs) => simConnect.ReadFreshAsync(key, timeoutMs);

        // Two plain buttons, read by their Text alone: Bright first, then Dim, after the list.
        _bright = new Button { Text = "Bright" };
        _bright.Click += (_, _) => PressBrightness(BrightPosition);
        AddControlAfterList(_bright);

        _dim = new Button { Text = "Dim" };
        _dim.Click += (_, _) => PressBrightness(DimPosition);
        AddControlAfterList(_dim);

        _readBackTimer = new System.Windows.Forms.Timer { Interval = FenixA320Definition.DcduSpringBackMs };
        _readBackTimer.Tick += (_, _) =>
        {
            _readBackTimer.Stop();
            _ = ReadBackDueAsync();
        };
    }

    /// <summary>
    /// How the window reads an L:var, fresh and on request: the manager's <see cref="SimConnectManager.ReadFreshAsync"/>, which
    /// gives null on a timeout, a disconnect or a key it cannot deliver. A seam for the tests, which have no sim.
    /// </summary>
    internal Func<string, int, Task<double?>> ReadFresh;

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

    /// <summary>
    /// The pilot's press of Bright or Dim: steps the switch once to <paramref name="position"/> and waits for it to come back
    /// and the value to move, then <see cref="ReadBackDueAsync"/> says it. The press itself says nothing, and a press before
    /// the last one's wait is over restarts the wait.
    /// </summary>
    internal void PressBrightness(int position)
    {
        _definition.SetDcduBrightness(position, _simConnect, _announcer);

        _press++;
        _readBackTimer.Stop();
        _readBackTimer.Start();
    }

    /// <summary>
    /// The wait after a press is over: reads the brightness and says it ("Brightness 60 percent", or "Brightness not
    /// available" when it was not read). Says nothing when the window is gone, or when a newer press came while the read was
    /// out, whose own wait will say it. The timer's tick, on the UI thread, where the read resumes. Fire-and-forget, so
    /// nothing may escape it: a throw after the await is logged and says nothing more.
    /// </summary>
    internal async Task ReadBackDueAsync()
    {
        try
        {
            long press = _press;
            double? value = await ReadBrightnessAsync();

            if (IsDisposed || Disposing) return;
            if (press != _press) return;

            _announcer.AnnounceImmediate(FenixChannelDcduBrightness.Spoken(value));
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"DCDU brightness read-back failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ReadFresh gives null on a timeout or a disconnect; a throw is made null too, so a read always has an answer.
    private async Task<double?> ReadBrightnessAsync()
    {
        try
        {
            return await ReadFresh(FenixChannelDcduBrightness.LevelKey, BrightnessReadTimeoutMs);
        }
        catch (Exception ex)
        {
            Log.Debug(LogCategory, $"DCDU brightness read failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing) _readBackTimer.Dispose();
    }
}
