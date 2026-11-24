using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The BlueBird 757's two CDUs as text (Shift+M). A clone of the HS787 FMC window (same list
/// layout, same keys), fed by the shared <see cref="CoherentBbs757TabletClient"/>, which reads the
/// screens off the aircraft's Comm Bus and presses keys as held L:vars. Ctrl+Tab switches between
/// the captain's and the first officer's CDU, Ctrl+Shift+L and Ctrl+Shift+R select one by side; the
/// two are independent, as on the aircraft, and the window title names the side shown
/// (<see cref="TitleFor"/>). Alt+C clears the scratchpad (<see cref="Bbs757CduKeys.ClearScratchpad"/>).
///
/// The window never disposes the client: the definition owns it (<c>BBS757Definition.EnsureTabletClient</c>;
/// the page service and the EFB window share it). The window
/// only subscribes, and unsubscribes in Dispose.
///
/// Speech (CORE-7): a page title is spoken whenever it changes (ACT turning into MOD included, as on
/// every other aircraft's CDU window), and the scratchpad is read back by <see cref="CduScratchpadAnnouncer"/>
/// once every queued key has had its cycle, which is the state the screen reader cannot know. An entry typed
/// in the scratchpad box is checked as it is read back (<see cref="Bbs757TypedEntryCheck"/>): the scratchpad
/// is read when it holds the entry, and "Scratchpad shows …" is said when the CDU missed a key (plan 08); a
/// key pressed before that goes out at once and leaves it to the ordinary read-back (Alt+C's clear is heard
/// that way too: "Cleared", or "DELETE" when it pressed DEL). A key the window cannot deliver is refused
/// aloud. Ctrl+Tab and the side keys say only which CDU is shown, the CDU already shown included.
/// While the window is hidden two things speak. Keys the pilot sent that were dropped because the link
/// went ("CDU connection lost, keys not sent"). And a message the FMC posts on the captain's scratchpad
/// ("CDU: TAKEOFF DATA UPLINK", R72): the hidden client still scrapes the captain's CDU once a second
/// (<see cref="OnVisibleChanged"/> points it there), <see cref="Bbs757CduMessageWatch"/> tells a message
/// from an entry typed in the window (which its own keys explain) and the Ctrl+M row "CDU Messages While
/// Closed" (<see cref="Bbs757CduRows.MessagesMonitorKey"/>, shown here as a check box) turns it off.
/// While the window is shown the scratchpad read-back above speaks the scratchpad, messages included, so
/// a message is never said twice.
/// </summary>
public partial class BBS757CduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public const string WaitingText = "Waiting for the 757's CDU. Is the aircraft powered?";
    public const string WaitingFirstOfficerText =
        "Waiting for the first officer's CDU. It shows once its screen changes: press a page key, such as Alt+R for RTE.";
    public const string NotReachableText = "The 757's CDU is not reachable.";
    public const string KeysDroppedText = "CDU connection lost, keys not sent";
    public const string SpeakMessagesText = "Speak CDU messages when closed";

    private const int PollTickMs = 350;
    // After the last queued key, how long the scratchpad waits before it is read back.
    private const int ScratchpadSettleMs = 300;

    private readonly CoherentBbs757TabletClient _client;
    private readonly ScreenReaderAnnouncer _announcer;

    // The latest screen per CDU (index 1 captain, 2 first officer); null until one arrives.
    private readonly Bbs757CduScreen?[] _screens = new Bbs757CduScreen?[3];
    private int _cdu = 1;
    // The last title row shown (raw, line 0), and the rows the list shows (their identities).
    private string _previousTitle = "";
    private IReadOnlyList<Bbs757CduRow> _rows = Array.Empty<Bbs757CduRow>();
    private readonly CduScratchpadAnnouncer _scratchpad = new();

    // When each CDU's latest screen arrived (index 1 captain, 2 first officer).
    private readonly DateTime[] _screenAt = new DateTime[3];

    // An entry typed in the scratchpad box, waiting to be read back (plan 08).
    private readonly Bbs757TypedEntryCheck _check = new();

    // The captain's scratchpad while the window is hidden: which change is a CDU message (R72).
    private readonly Bbs757CduMessageWatch _watch = new();
    // True while the check box is being set from the settings, which must not write them back.
    private bool _syncingSpeakMessages;

    // True while the key now being handled is an auto-repeat (held down): one physical press is
    // one CDU key, so a held Backspace never queues a run of CLRs.
    private bool _keyRepeat;

    private IntPtr _previousWindow = IntPtr.Zero;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private bool _disposed;

    public BBS757CduForm(CoherentBbs757TabletClient client, ScreenReaderAnnouncer announcer)
    {
        _client = client;
        _announcer = announcer;

        InitializeComponent();
        SetupEventHandlers();

        // Status and scratchpad read-back run on every tick while the window is shown; screens
        // themselves are pushed by the client when they change.
        _pollTimer = new System.Windows.Forms.Timer { Interval = PollTickMs };
        _pollTimer.Tick += (_, _) => OnPollTick();

        _client.ScreenUpdated += Client_ScreenUpdated;
        _client.ScreenLost += Client_ScreenLost;
        _client.KeysDropped += Client_KeysDropped;
    }

    // ------------------------------------------------------------------
    // Event wiring
    // ------------------------------------------------------------------

    private void SetupEventHandlers()
    {
        Load += (s, e) => cduDisplay.Focus();

        FormClosing += (s, e) =>
        {
            // Let real app/OS shutdown through (an unconditional cancel stalls Application.Exit);
            // everything else hides, as the HS787 window does.
            if (e.CloseReason is CloseReason.ApplicationExitCall
                or CloseReason.WindowsShutDown
                or CloseReason.TaskManagerClosing)
            {
                return;
            }

            e.Cancel = true;
            HideAndRestore();
        };

        this.KeyDown += Form_KeyDown;
        scratchpadInput.KeyDown += ScratchpadInput_KeyDown;

        btnInitRef.Click  += (s, e) => SendKey(Bbs757CduKeys.Init);
        btnRte.Click      += (s, e) => SendKey(Bbs757CduKeys.Rte);
        btnDepArr.Click   += (s, e) => SendKey(Bbs757CduKeys.DepArr);
        btnAtc.Click      += (s, e) => SendKey(Bbs757CduKeys.Atc);
        btnVnav.Click     += (s, e) => SendKey(Bbs757CduKeys.Vnav);
        btnFix.Click      += (s, e) => SendKey(Bbs757CduKeys.Fix);
        btnLegs.Click     += (s, e) => SendKey(Bbs757CduKeys.Legs);
        btnHold.Click     += (s, e) => SendKey(Bbs757CduKeys.Hold);
        btnFmcComm.Click  += (s, e) => SendKey(Bbs757CduKeys.FmcComm);
        btnProg.Click     += (s, e) => SendKey(Bbs757CduKeys.Prog);
        btnMenu.Click     += (s, e) => SendKey(Bbs757CduKeys.Menu);
        btnNavRad.Click   += (s, e) => SendKey(Bbs757CduKeys.NavRad);
        btnPrevPage.Click += (s, e) => SendKey(Bbs757CduKeys.Prev);
        btnNextPage.Click += (s, e) => SendKey(Bbs757CduKeys.Next);
        btnExec.Click     += (s, e) => SendKey(Bbs757CduKeys.Exec);
        btnClr.Click      += (s, e) => SendKey(Bbs757CduKeys.Clr);
        btnDel.Click      += (s, e) => SendKey(Bbs757CduKeys.Del);
        btnSwitchCdu.Click += (s, e) => SelectCdu(OtherCdu);

        // The check box is the Ctrl+M row's twin: shown from it when the window comes up (a tick in
        // Ctrl+M while the window stays open is picked up when it is activated again), written to it on a tick.
        chkSpeakMessages.CheckedChanged += (s, e) => OnSpeakMessagesChanged();
        Activated += (s, e) => RefreshSpeakMessages();
    }

    // ------------------------------------------------------------------
    // Client pushes (posted to the UI context; marshalled again so a push that lands while the
    // handle is being destroyed is dropped instead of thrown)
    // ------------------------------------------------------------------

    private void Client_ScreenUpdated(int cdu, Bbs757CduScreen screen) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _screens[cdu] = screen;
        _screenAt[cdu] = DateTime.UtcNow;
        // Hidden, the client reads the captain's CDU only: a message on its scratchpad is spoken (R72).
        if (!Visible && cdu == 1) SpeakCduMessage(screen);
        if (cdu == _cdu) Render();
    });

    private void Client_ScreenLost(int cdu) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _screens[cdu] = null;
        if (!Visible && cdu == 1) _watch.Seed(null);   // the next screen seeds it silently
        if (cdu != _cdu) return;
        _scratchpad.Reset();   // the next screen seeds it silently
        Render();
    });

    // Keys the pilot sent were thrown away when the link went: a background change he did not
    // see coming, so it is said (once per outage, the client's guarantee), window shown or not.
    private void Client_KeysDropped(int count) => SafeBeginInvoke(() =>
    {
        if (_disposed) return;
        _check.Cancel();   // its keys are gone: KeysDroppedText says so
        _announcer.Announce(KeysDroppedText);
    });

    // ObjectDisposedException derives from InvalidOperationException, so one catch covers both;
    // the IsHandleCreated check alone races a handle destroy ([A380C-6]).
    private void SafeBeginInvoke(Action action)
    {
        try { if (IsHandleCreated) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    // ------------------------------------------------------------------
    // CDU messages while the window is closed (R72)
    // ------------------------------------------------------------------

    // Whether Ctrl+M has "CDU Messages While Closed" unticked. This speech comes from the client's push,
    // outside MainForm's mute wrap, so the window checks the key itself ([VAR-8]).
    private static bool MessagesMuted =>
        SettingsManager.Current.BBS757DisabledMonitorVariablesSet.Contains(Bbs757CduRows.MessagesMonitorKey);

    /// <summary>
    /// Speaks what the hidden window's push of the captain's screen holds, if it is a CDU message
    /// (<see cref="Bbs757CduMessageWatch"/>): queued behind the screen reader's own speech, as any
    /// background event is. A message muted in Ctrl+M is dropped, not kept for later. The client's pending
    /// count is passed as it is: it covers both CDUs' keys (the page service's reads of the first officer's
    /// CDU too), so the watch honours it only inside the settle window of this window's own captain keys
    /// (<see cref="NoteKeysQueued"/>).
    /// </summary>
    private void SpeakCduMessage(Bbs757CduScreen screen)
    {
        if (_watch.OnScreen(screen.Scratchpad, _client.PendingKeyCount > 0, DateTime.UtcNow, MessagesMuted) is not { } say) return;
        Log.Debug("BBS757", $"CDU message: {say}");
        _announcer.Announce(say);
    }

    /// <summary>
    /// Tells the watch the window just queued keys for the selected CDU that take about
    /// <paramref name="queuedMs"/> ms to go out, behind the <paramref name="pendingBefore"/> already in the
    /// client's queue (one queue for both CDUs, each counted as a standard cycle: a long CLR is longer, but
    /// a push that still sees keys pending extends the window itself). Their echo on the scratchpad is then
    /// no message, even when the last key lands between two of the hidden client's scrapes. Keys for the
    /// first officer's CDU change nothing on the captain's scratchpad, so they are left out.
    /// </summary>
    private void NoteKeysQueued(int pendingBefore, int queuedMs)
    {
        if (_cdu != 1) return;
        _watch.KeysQueued(DateTime.UtcNow, pendingBefore * CoherentBbs757TabletClient.KeyCycleMs + queuedMs);
    }

    // Ticked = spoken = the Ctrl+M row is ticked. The set is rebuilt by every Save, Ctrl+M's included.
    private void RefreshSpeakMessages()
    {
        _syncingSpeakMessages = true;
        try { chkSpeakMessages.Checked = !MessagesMuted; }
        finally { _syncingSpeakMessages = false; }
    }

    // The pilot ticked or unticked the check box: the same edit Ctrl+M's tick makes. The screen reader says
    // the new state; nothing is said here ([CORE-7]).
    private void OnSpeakMessagesChanged()
    {
        if (_syncingSpeakMessages) return;
        var disabled = SettingsManager.Current.BBS757DisabledMonitorVariables;
        if (chkSpeakMessages.Checked)
            disabled.Remove(Bbs757CduRows.MessagesMonitorKey);
        else if (!disabled.Contains(Bbs757CduRows.MessagesMonitorKey))
            disabled.Add(Bbs757CduRows.MessagesMonitorKey);
        SettingsManager.Save();
    }

    // ------------------------------------------------------------------
    // Display
    // ------------------------------------------------------------------

    private void OnPollTick()
    {
        UpdateStatus();
        var now = DateTime.UtcNow;
        // While keys are still queued or in their cycle, the scratchpad is mid-entry: hold its
        // read-back, so typing is read back once, settled, ScratchpadSettleMs after the last key.
        if (_client.PendingKeyCount > 0)
        {
            var until = now.AddMilliseconds(ScratchpadSettleMs);
            if (until > _scratchpad.SuppressUntil) _scratchpad.SuppressUntil = until;
        }
        // A typed entry is read back by its check: as the scratchpad shows it when it landed, "Scratchpad
        // shows …" when the CDU missed a key (plan 08). Either way the read-back is then re-seeded silently
        // on what the CDU shows, so it does not say it again.
        if (_check.Cdu is int cdu)
        {
            if (_check.Poll(now, _client.PendingKeyCount, _screens[cdu], _screenAt[cdu]) is { } verdict)
            {
                if (verdict.Say != null)
                {
                    Log.Debug("BBS757", $"CDU {cdu} typed entry: {verdict.Say}");
                    if (verdict.Missed) _announcer.AnnounceImmediate(verdict.Say);
                    else _announcer.Announce(verdict.Say);
                }
                ReseedScratchpad();
            }
            return;
        }
        var screen = _screens[_cdu];
        if (screen != null && _scratchpad.OnPoll(screen.Scratchpad, now) is { } say)
            _announcer.Announce(say);
    }

    private void UpdateStatus()
    {
        string desired;
        if (!_client.IsReadable)
            desired = WaitingText;
        else if (_screens[_cdu] == null)
            desired = _cdu == 1 ? WaitingText : WaitingFirstOfficerText;
        else
            desired = CduName(_cdu);

        if (statusBox.Text != desired)
            statusBox.Text = desired;
    }

    private static string CduName(int cdu) => cdu == 1 ? "Captain CDU" : "First officer CDU";

    /// <summary>
    /// The window title, naming the side of the CDU shown (1 captain, 2 first officer): a screen reader
    /// reads a window's title as it comes forward, so the pilot knows whose CDU he has before he reads a line.
    /// </summary>
    public static string TitleFor(int cdu) =>
        cdu == 1 ? "BlueBird 757 CDU (Captain)" : "BlueBird 757 CDU (First officer)";

    // The CDU Ctrl+Tab and the Switch CDU button select.
    private int OtherCdu => _cdu == 1 ? 2 : 1;

    private string WaitingTextFor(int cdu) => cdu == 1 ? WaitingText : WaitingFirstOfficerText;

    /// <summary>
    /// Shows the selected CDU's screen. Another page (<see cref="Bbs757CduRows.IsPageChange"/>: a new
    /// page, or NEXT PAGE) speaks its title (while the window is shown) and moves the cursor to line 1's
    /// value row. A new title on the same page (ACT turning into MOD after an entry, 1/2 becoming 1/3) is
    /// spoken too, as every other aircraft's CDU window does, but the cursor stays; anything else keeps the
    /// cursor on the pilot's row by its identity (<see cref="Bbs757CduRows.Restore"/>), so a label
    /// appearing or going above it does not move it (plan 08). <paramref name="announceTitle"/> false
    /// shows the screen as it is and leaves the title to the next push (the CDU switch).
    /// </summary>
    private void Render(bool announceTitle = true)
    {
        UpdateStatus();

        var screen = _screens[_cdu];
        if (screen == null)
        {
            Forms.DisplayList.UpdateInPlace(cduDisplay, new[] { WaitingTextFor(_cdu) });
            if (cduDisplay.Items.Count > 0 && cduDisplay.SelectedIndex != 0) cduDisplay.SelectedIndex = 0;
            _previousTitle = "";   // the first page that arrives is announced
            _rows = Array.Empty<Bbs757CduRow>();
            return;
        }

        int savedIndex = cduDisplay.SelectedIndex;
        var before = _rows;
        var rows = Bbs757CduRows.Build(screen, _cdu);
        _rows = rows;
        Forms.DisplayList.UpdateInPlace(cduDisplay, rows.Select(r => r.Text).ToList());

        string titleRow = screen.Lines[0];
        int target;
        if (!announceTitle)
        {
            target = savedIndex;
        }
        else if (screen.Title.Length > 0 && Bbs757CduRows.IsPageChange(_previousTitle, titleRow))
        {
            if (Visible) _announcer.Announce(Bbs757CduTitle.Spoken(titleRow));
            _previousTitle = titleRow;
            target = Bbs757CduRows.PageStart(rows);
        }
        else
        {
            if (screen.Title.Length > 0)
            {
                // ACT -> MOD, 1/2 -> 1/3: the same page with a new title, said; the cursor stays.
                if (Visible && Bbs757CduRows.IsTitleChange(_previousTitle, titleRow))
                    _announcer.Announce(Bbs757CduTitle.Spoken(titleRow));
                _previousTitle = titleRow;
            }
            target = Bbs757CduRows.Restore(before, savedIndex, rows);
        }
        if (target >= 0 && target < cduDisplay.Items.Count && cduDisplay.SelectedIndex != target)
            cduDisplay.SelectedIndex = target;
    }

    /// <summary>Re-seeds the scratchpad read-back silently on what the selected CDU shows now.</summary>
    private void ReseedScratchpad()
    {
        _scratchpad.Reset();
        var screen = _screens[_cdu];
        if (screen != null) _scratchpad.OnPoll(screen.Scratchpad, DateTime.UtcNow);
    }

    // ------------------------------------------------------------------
    // CDU switch
    // ------------------------------------------------------------------

    /// <summary>
    /// Ctrl+Tab and the Switch CDU button (the other CDU), Ctrl+Shift+L and Ctrl+Shift+R (a side): shows
    /// <paramref name="cdu"/> (1 captain, 2 first officer), renames the window after its side
    /// (<see cref="TitleFor"/>) and says only which CDU that is. Selecting the CDU already shown says its
    /// name again and changes nothing else. The page title is spoken by the fresh push the client sends for it (setting
    /// ActiveCdu asks for one), never from the copy held here, which can be older; until then the
    /// held copy is shown as it is. A CDU with no screen yet says so in its list row and the status
    /// box. A typed entry still being checked is left unjudged: the client reads only the shown CDU,
    /// so the other one's screen would be judged frozen.
    /// </summary>
    private void SelectCdu(int cdu)
    {
        if (cdu == _cdu)
        {
            _announcer.AnnounceImmediate(CduName(_cdu));
            return;
        }
        _check.Cancel();
        _cdu = cdu;
        Text = TitleFor(_cdu);
        _client.ActiveCdu = _cdu;
        _announcer.AnnounceImmediate(CduName(_cdu));
        _previousTitle = "";   // so the fresh push speaks its title
        ReseedScratchpad();
        Render(announceTitle: false);
    }

    // ------------------------------------------------------------------
    // Keys to the CDU
    // ------------------------------------------------------------------

    /// <summary>
    /// Queues one key for the selected CDU, or says why it cannot. It goes out at once, after any keys
    /// already queued; a typed entry still being checked is then left to the ordinary scratchpad
    /// read-back (a line select key may take the entry out of the scratchpad), as on every other aircraft.
    /// </summary>
    private void SendKey(string key) => QueueKeys(new[] { (key, CoherentBbs757TabletClient.KeyHoldMs) });

    /// <summary>
    /// Alt+C: clears the selected CDU's scratchpad with <see cref="Bbs757CduKeys.ClearScratchpad"/> (two long
    /// CLRs, or one DEL when the last screen read shows it empty and no key is queued: the screen is the state
    /// before queued keys, so with any the scratchpad is not known empty; the client has one queue for both
    /// CDUs, and a key queued for the other one counts too, which only ever chooses the CLRs). Refused aloud
    /// when the link is down like every key. Nothing is said here: the scratchpad read-back says "Cleared" or
    /// "DELETE" once the keys have had their cycle ([CORE-7]).
    /// </summary>
    private void ClearScratchpad() =>
        QueueKeys(Bbs757CduKeys.ClearScratchpad(_screens[_cdu]?.Scratchpad, keysQueued: _client.PendingKeyCount > 0));

    /// <summary>
    /// Queues these keys, each with its hold, for the selected CDU in one go: the refusal is said once
    /// whatever their number, and the typed-entry check ends once.
    /// </summary>
    private void QueueKeys(IReadOnlyList<(string Key, int HoldMs)> keys)
    {
        if (!_client.IsReadable)
        {
            _announcer.AnnounceImmediate(NotReachableText);
            return;
        }
        _check.Cancel();
        int pendingBefore = _client.PendingKeyCount;
        foreach (var (key, holdMs) in keys) _client.PressKey(_cdu, key, holdMs);
        NoteKeysQueued(pendingBefore, keys.Sum(k => CoherentBbs757TabletClient.CycleMs(k.HoldMs, CoherentBbs757TabletClient.KeyGapMs)));
    }

    /// <summary>
    /// Types the scratchpad box's text into the selected CDU (<see cref="Bbs757CduTyping.Keys"/>;
    /// characters with no key are skipped) at the measured typing timing, then reads the scratchpad
    /// back (<see cref="Bbs757TypedEntryCheck"/>). A second entry sent before the first was read back is
    /// checked with it, as one.
    /// </summary>
    private void SendText(string text)
    {
        var keys = Bbs757CduTyping.Keys(text);
        if (keys.Count == 0) return;
        if (!_client.IsReadable)
        {
            _announcer.AnnounceImmediate(NotReachableText);
            return;
        }
        int pendingBefore = _client.PendingKeyCount;
        _check.Typed(_cdu, text, _screens[_cdu]?.Scratchpad ?? "", otherKeysQueued: pendingBefore > 0);
        foreach (var key in keys) _client.PressKey(_cdu, key, Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs);
        NoteKeysQueued(pendingBefore, keys.Count * CoherentBbs757TabletClient.CycleMs(Bbs757CduTyping.HoldMs, Bbs757CduTyping.GapMs));
    }

    // ------------------------------------------------------------------
    // Keyboard shortcuts
    // ------------------------------------------------------------------

    // The repeat bit of the key message that is about to reach Form_KeyDown (KeyPreview raises
    // the form's KeyDown from here).
    protected override bool ProcessKeyPreview(ref Message m)
    {
        const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
        if (m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN)
            _keyRepeat = ((long)m.LParam & 0x40000000) != 0;
        return base.ProcessKeyPreview(ref m);
    }

    // Ctrl+Tab is a dialog key: it never reaches KeyDown, so it is caught here.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Tab && (keyData & Keys.Control) != 0 && (keyData & Keys.Alt) == 0)
        {
            bool repeat = ((long)msg.LParam & 0x40000000) != 0;
            if (!repeat) SelectCdu(OtherCdu);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        string? key = CduKeyFor(e);
        if (key != null)
        {
            if (!_keyRepeat) SendKey(key);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Ctrl+Shift+L / Ctrl+Shift+R: the captain's / the first officer's CDU
        if (Bbs757CduKeys.SideFor(e.KeyData) is int side)
        {
            if (!_keyRepeat) SelectCdu(side);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+C: clear the scratchpad (ForAltLetter has no C: this is no page key)
        if (e.Alt && !e.Shift && !e.Control && e.KeyCode == Keys.C)
        {
            if (!_keyRepeat) ClearScratchpad();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+S: focus scratchpad
        if (e.Alt && !e.Shift && !e.Control && e.KeyCode == Keys.S)
        {
            scratchpadInput.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        // Alt+Home: focus display
        if (e.Alt && e.KeyCode == Keys.Home)
        {
            cduDisplay.Focus();
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift)
        {
            HideAndRestore();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    /// <summary>
    /// The CDU key a window shortcut presses, or null. The HS787 window's layout:
    /// Ctrl+1-6 / Alt+1-6 the left and right line select keys (F1-F6 / F7-F12 in the alternate
    /// layout, read on every press), PageUp / PageDown (or Alt+Up / Alt+Down) PREV / NEXT PAGE,
    /// Ctrl+Enter or Alt+E EXEC, Alt+letters the page keys, and with the display focused
    /// Backspace CLR and Delete DEL.
    /// </summary>
    private string? CduKeyFor(KeyEventArgs e)
    {
        if (SettingsManager.Current.MCDUUseAlternateLSKKeys)
        {
            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6)
                return Bbs757CduKeys.Lsk(right: false, e.KeyCode - Keys.F1 + 1);
            if (!e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.F7 && e.KeyCode <= Keys.F12)
                return Bbs757CduKeys.Lsk(right: true, e.KeyCode - Keys.F7 + 1);
        }
        else
        {
            if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                return Bbs757CduKeys.Lsk(right: false, e.KeyCode - Keys.D1 + 1);
            if (e.Alt && !e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D6)
                return Bbs757CduKeys.Lsk(right: true, e.KeyCode - Keys.D1 + 1);
        }

        if ((e.KeyCode == Keys.PageUp && !e.Control && !e.Alt) || (e.Alt && e.KeyCode == Keys.Up))
            return Bbs757CduKeys.Prev;
        if ((e.KeyCode == Keys.PageDown && !e.Control && !e.Alt) || (e.Alt && e.KeyCode == Keys.Down))
            return Bbs757CduKeys.Next;
        if (e.Control && !e.Alt && e.KeyCode == Keys.Return)
            return Bbs757CduKeys.Exec;

        if (ActiveControl == cduDisplay && !e.Control && !e.Alt && !e.Shift)
        {
            if (e.KeyCode == Keys.Back) return Bbs757CduKeys.Clr;
            if (e.KeyCode == Keys.Delete) return Bbs757CduKeys.Del;
        }

        if (e.Alt && !e.Control && !e.Shift)
            return Bbs757CduKeys.ForAltLetter(e.KeyCode);
        return null;
    }

    private void ScratchpadInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Return && !e.Control && !e.Alt)
        {
            string text = scratchpadInput.Text;
            scratchpadInput.Clear();
            e.Handled = true;
            e.SuppressKeyPress = true;
            SendText(text);
        }
        else if (scratchpadInput.Text.Length == 0 && !e.Control && !e.Alt && !e.Shift
                 && (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete))
        {
            // An empty box: Backspace is CLR and Delete is DEL, as on the display.
            if (!_keyRepeat) SendKey(e.KeyCode == Keys.Back ? Bbs757CduKeys.Clr : Bbs757CduKeys.Del);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        _client.ActiveCdu = _cdu;   // asks for a fresh copy of its screen
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        cduDisplay.Focus();
        ReseedScratchpad();
        Render();
    }

    /// <summary>
    /// Starts the closed-window watch on a window the pilot has not opened yet, so a CDU message is spoken
    /// from the moment the 757 loads and not only after the first Shift+M. The client's pushes are marshalled
    /// with BeginInvoke, which needs the window's handle: a window never shown has none, so it is made here.
    /// The watch starts knowing nothing, so the first captain screen only seeds it (what the scratchpad holds
    /// at load is no news). A no-op on a window that is shown or was shown before (hiding it set all this).
    /// </summary>
    public void StartHidden()
    {
        if (_disposed || Visible || IsHandleCreated) return;
        _ = Handle;
        _watch.Seed(null);
        _client.SetActive(false);
        _client.ActiveCdu = 1;
    }

    private void HideAndRestore()
    {
        Hide();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    // The client scrapes at its visible rate only while the window is shown; the read-back timer runs
    // only then too. Hidden, the window speaks keys it lost and a CDU message on the captain's scratchpad
    // (R72): the client reads the captain's CDU then, whichever one the window was left on, and the watch
    // starts from what the pilot saw last, so a message already heard in the open window is not said again.
    // Left on the first officer's CDU, the window has not seen the captain's scratchpad since it switched
    // (the client read the other CDU), so the watch knows nothing and the first push only seeds it.
    // ShowForm points the client back at the selected CDU.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_disposed) return;
        _client.SetActive(Visible);
        if (Visible)
        {
            RefreshSpeakMessages();
            _pollTimer.Start();
        }
        else
        {
            _pollTimer.Stop();
            _check.Cancel();
            _watch.Seed(_cdu == 1 ? _screens[1]?.Scratchpad : null);
            _client.ActiveCdu = 1;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _pollTimer.Stop();
            _pollTimer.Dispose();
            _client.ScreenUpdated -= Client_ScreenUpdated;
            _client.ScreenLost -= Client_ScreenLost;
            _client.KeysDropped -= Client_KeysDropped;
            _client.SetActive(false);
            // The client is the definition's, shared with the EFB window: never disposed here.
        }
        base.Dispose(disposing);
    }
}
