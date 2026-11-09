using System.ComponentModel;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.A300;

/// <summary>
/// The iniBuilds A300's two MCDUs as text (Shift+M). The screens come from the aircraft's own MCDU
/// export (<see cref="SimConnect.A300.A300McduDataManager"/>), the keys go out as the cockpit's own
/// key L:vars (<see cref="IniA300Definition.PressMcduKeys"/>).
///
/// Built on the MD-11 MCDU window's rules: the list is the title, each label above its numbered
/// line and the scratchpad, and the cursor follows the ROW it was on, never its index; it polls only
/// while visible, and a re-show catches up silently (onto the same page it puts the cursor back on
/// the row the pilot left, onto another on the title); a page change is spoken by its title and puts
/// the cursor on line 1; the scratchpad is read back once a burst of keys has settled; the window's
/// keys never take a chord the focused unit selector needs (<see cref="A300McduKeyRouting"/>).
///
/// It keeps the aircraft's MCDU export switched on while it is open: sent on every open, then asked
/// for on every poll (<see cref="IniA300Definition.EnsureMcduExport"/>), which sends only when the
/// export is not known to be on, so a flight load with the window open brings the screens back.
/// </summary>
public sealed class A300McduForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly IniA300Definition _definition;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private IntPtr _previousWindow = IntPtr.Zero;

    private ComboBox _unitSelector = null!;
    private TextBox _statusBox = null!;
    private DisplayListBox _display = null!;
    private TextBox _input = null!;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 250 };

    private A300McduUnit _unit = A300McduUnit.Captain;
    private A300McduScreen? _rendered;
    private IReadOnlyList<A300McduRow>? _rows;
    private readonly Dictionary<A300McduUnit, string> _lastTitle = new();
    private readonly CduScratchpadAnnouncer _scratchpad = new("Scratchpad cleared", stablePolls: 2);
    private readonly A300McduBlankClocks _blankClocks = new();
    private bool _clearing;

    private static readonly A300McduUnit[] Units = { A300McduUnit.Captain, A300McduUnit.FirstOfficer };

    /// <summary>Beyond the last queued key: the aircraft applying it, the client-data delivery and the
    /// next poll. A margin, not a measurement — too short reads an entry half-typed.</summary>
    private const int SettleMarginMs = 400;

    /// <summary>The clear-all's backstop against an unreadable feed.</summary>
    private const int MaxClearPresses = A300McduText.Cols + 4;

    /// <summary>How long the clear-all waits for progress before it gives up: for the scratchpad to change once a
    /// CLR has been released (<see cref="ScratchpadChangedAsync"/>), and for the key queue to shrink while the keys
    /// ahead of a CLR land (<see cref="KeysLandedAsync"/>). A margin, not a measurement: well beyond the key's press and
    /// gap (160 ms) plus the aircraft's redraw and the client-data delivery. A CLR the aircraft ignores, or a queue that
    /// stops shrinking, costs this once, and then the clear stops.</summary>
    public const int ClearConfirmMs = 1500;

    /// <summary>How often the clear-all looks at the scratchpad while it waits.</summary>
    private const int ClearPollMs = 50;

    /// <summary>The one sentence a clear-all that ran out of road says.</summary>
    private const string ClearFailed = "Could not clear the scratchpad";

    // Test seams: what the window reads and the clock it keeps, so a test drives it without a sim.

    /// <summary>The unit's latest screen from the data manager; null while it has none.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<A300McduUnit, A300McduScreen?> Screens { get; set; }

    /// <summary>Whether there is a feed to read at all (the A300's data manager exists on this connection).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<bool> HasFeed { get; set; }

    /// <summary>The time every judgment and hold is made against.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

    /// <summary>The wait between the clear-all's reads of the scratchpad.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<int, Task> Delay { get; set; } = ms => Task.Delay(ms);

    public A300McduForm(IniA300Definition definition, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _definition = definition;
        _sim = sim;
        _announcer = announcer;
        Screens = unit => _sim.A300McduDataManager?.GetScreen(unit);
        HasFeed = () => _sim.A300McduDataManager != null;
        BuildLayout();
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        });
        _poll.Tick += (_, _) => Tick();
        _definition.McduKeyReleased += OnKeyReleased;
    }

    /// <summary>The list's rows as the screen reader reads them.</summary>
    internal IReadOnlyList<string> Lines => _display.Items.Cast<object>().Select(o => o.ToString() ?? "").ToList();

    /// <summary>The status box's text.</summary>
    internal string Status => _statusBox.Text;

    /// <summary>The list's cursor: the row the screen reader is on.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int SelectedRow
    {
        get => _display.SelectedIndex;
        set => _display.SelectedIndex = value;
    }

    /// <summary>Picks the unit as the pilot does in the unit selector (the selector's own change handler follows).</summary>
    internal void SelectUnit(A300McduUnit unit) =>
        _unitSelector.SelectedIndex = unit == A300McduUnit.FirstOfficer ? 1 : 0;

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildLayout()
    {
        SuspendLayout();
        Text = "A300 MCDU";
        AccessibleName = "A300 MCDU";
        ClientSize = new Size(620, 760);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        int y = 10;
        _unitSelector = new ComboBox
        {
            Location = new Point(10, y),
            Size = new Size(220, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "MCDU unit",
        };
        _unitSelector.Items.AddRange(new object[] { "Captain", "First Officer" });
        _unitSelector.SelectedIndex = 0;

        _statusBox = new TextBox
        {
            Text = "Waiting for data",
            Location = new Point(240, y),
            Size = new Size(370, 25),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "MCDU status",
        };
        y += 32;

        _display = new DisplayListBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 290),
            Font = new Font("Consolas", 11f),
            BackColor = Color.Black,
            ForeColor = Color.Lime,
            AccessibleName = "MCDU Display",
            AccessibleDescription = "Current MCDU screen. Use arrow keys to read lines.",
            IntegralHeight = false,
        };
        y += 300;

        _input = new TextBox
        {
            Location = new Point(10, y),
            Size = new Size(600, 25),
            AccessibleName = "MCDU Input",
            AccessibleDescription = "Type text and press Enter to send it to the MCDU scratchpad.",
        };
        y += 34;

        var buttons = new List<Button>();
        int width = 116, height = 30, gap = 5, perRow = 5;
        for (int i = 0; i < A300McduKeys.PageButtons.Length; i++)
        {
            var (label, key) = A300McduKeys.PageButtons[i];
            var button = new Button
            {
                Text = label,
                Location = new Point(10 + (i % perRow) * (width + gap), y + (i / perRow) * (height + gap)),
                Size = new Size(width, height),
            };
            button.Click += (_, _) => Press(key);
            buttons.Add(button);
        }

        Controls.Add(_unitSelector);
        Controls.Add(_statusBox);
        Controls.Add(_display);
        Controls.Add(_input);
        foreach (var b in buttons) Controls.Add(b);

        int tab = 0;
        _display.TabIndex = tab++;
        _input.TabIndex = tab++;
        _statusBox.TabIndex = tab++;
        _unitSelector.TabIndex = tab++;
        foreach (var b in buttons) b.TabIndex = tab++;

        _unitSelector.SelectedIndexChanged += (_, _) =>
        {
            _unit = _unitSelector.SelectedIndex == 1 ? A300McduUnit.FirstOfficer : A300McduUnit.Captain;
            Resync();   // silent: the screen reader already read the selector's new value
        };
        _display.KeyDown += Display_KeyDown;
        _input.KeyDown += Input_KeyDown;
        KeyDown += Form_KeyDown;
        ResumeLayout(false);
    }

    // ---------------------------------------------------------------------------------
    // Keys
    // ---------------------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        var combo = ActiveControl as ComboBox;
        var focus = combo == null ? A300McduKeyRouting.ComboFocus.None
            : combo.DroppedDown ? A300McduKeyRouting.ComboFocus.Dropped
            : A300McduKeyRouting.ComboFocus.Closed;
        var action = A300McduKeyRouting.Resolve(e.KeyCode, e.Alt, e.Control, e.Shift, focus,
            MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys);
        if (action.Kind == A300McduKeyRouting.ActionKind.Pass)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        switch (action.Kind)
        {
            case A300McduKeyRouting.ActionKind.Press:
                Press(action.Key);
                break;
            case A300McduKeyRouting.ActionKind.Close:
                Close();
                break;
            case A300McduKeyRouting.ActionKind.SelectUnit:
                SelectUnit(action.Unit);
                if (!_unitSelector.Focused)
                    _announcer.Announce(_unitSelector.SelectedItem?.ToString() ?? "");
                break;
            case A300McduKeyRouting.ActionKind.FocusInput:
                _input.Focus();
                break;
            case A300McduKeyRouting.ActionKind.FocusDisplay:
                _display.Focus();
                break;
        }
    }

    /// <summary>Backspace (one CLR), Delete (clear the scratchpad) and Right (next page), on the
    /// display only: the input box needs those keys to edit its own text.</summary>
    private void Display_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Alt || e.Control || e.Shift)
            return;
        switch (e.KeyCode)
        {
            case Keys.Back:
                Press("CLR");
                break;
            case Keys.Delete:
                _ = ClearScratchpadAsync();
                break;
            case Keys.Right:
                Press("NEXT");
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return || e.Alt || e.Control)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        string text = _input.Text.ToUpperInvariant();
        if (text.Length == 0)
            return;
        // A refused entry stays in the box, to be edited and sent again.
        if (A300McduKeys.RefusalFor(text) is { } refusal)
        {
            _announcer.Announce(refusal);
            return;
        }
        var keys = text.Select(c => A300McduKeys.ForChar(c)!).ToList();
        if (!Send(_unit, keys))
            return;
        _input.Clear();
    }

    private bool Press(string key) => Send(_unit, new[] { key });

    /// <summary>Per unit, how many keys this window has queued for it: the clear-all compares it with the count it took
    /// after its own CLR to see whether any other key has gone to the unit since.</summary>
    private readonly Dictionary<A300McduUnit, int> _keysSent = new();

    private int KeysSent(A300McduUnit unit) => _keysSent.GetValueOrDefault(unit);

    /// <summary>Per unit, when the window last saw one of its keys released, by its own clock.</summary>
    private readonly Dictionary<A300McduUnit, DateTime> _lastRelease = new();

    /// <summary>A key the definition's pump has released (<see cref="IniA300Definition.McduKeyReleased"/>): the
    /// clear-all's settle margin counts from here and not from the pump's nominal pace, which the real one does not keep.</summary>
    private void OnKeyReleased(A300McduUnit unit) => _lastRelease[unit] = UtcNow();

    /// <summary>Queues keys on <paramref name="unit"/> and, when it is the unit being read, holds the scratchpad
    /// read-back until they have landed, so an entry is read once, settled. Says why when nothing could be sent.
    /// What the input box and the display's keys call; internal so a test can send an entry as they do.</summary>
    internal bool Send(A300McduUnit unit, IReadOnlyList<string> keys)
    {
        if (!_definition.PressMcduKeys(unit, keys))
        {
            _announcer.Announce("MCDU unavailable");
            return false;
        }
        _keysSent[unit] = KeysSent(unit) + keys.Count;
        if (unit == _unit)
        {
            int pending = _definition.McduKeysPending;
            var until = UtcNow().AddMilliseconds(pending * (A300McduKeys.HoldMs + A300McduKeys.GapMs) + SettleMarginMs);
            if (until > _scratchpad.SuppressUntil)
                _scratchpad.SuppressUntil = until;
        }
        return true;
    }

    /// <summary>
    /// Clears the whole scratchpad of the unit it started on: one CLR at a time, stopping the moment it reads empty. It
    /// presses that unit to the end, whichever one the pilot switches to meanwhile. Each round first lets the keys
    /// MSFSBA has sent that unit land (<see cref="KeysLandedAsync"/>: an entry just sent from the input box, a
    /// Backspace pressed during the clear) and gives the last of them <see cref="SettleMarginMs"/> to show on its
    /// screen, counted from the moment the key pump actually released it (the pump runs slower than its nominal pace)
    /// and whether or not the unit is still the one shown, because a change of the scratchpad is the only sign that a
    /// CLR landed and a key ahead of it would read as that sign. Two things that wait cannot cover: a key pressed in
    /// the cockpit itself, which is not in the queue, and an aircraft slower than the margin, which is a margin and not
    /// a measurement; either can still be taken for a CLR's effect. The CLR then goes out alone, and the clear waits
    /// for its release and for the scratchpad to read differently (<see cref="ScratchpadChangedAsync"/>). Never a CLR
    /// on an empty scratchpad — the A300 then puts "CLR" into it — and never a second CLR on one the aircraft ignored:
    /// that stops, saying so once, and so does a key queue that has stopped shrinking. Silent when it works (the
    /// read-back says "Scratchpad cleared").
    /// </summary>
    internal async Task ClearScratchpadAsync()
    {
        if (_clearing)
            return;
        _clearing = true;
        try
        {
            var unit = _unit;
            int ownSent = -1;   // keys sent to the unit as of this clear's last CLR; none yet, so any recent key counts
            for (int presses = 0; ; presses++)
            {
                if (!await KeysLandedAsync(unit, ownSent, waitForMargin: true))
                {
                    GiveUp();
                    return;
                }
                var screen = Screens(unit);
                if (screen == null || screen.IsBlank)
                {
                    GiveUp();
                    return;
                }
                if (string.IsNullOrWhiteSpace(screen.Scratchpad))
                {
                    if (presses == 0)
                        _announcer.Announce("Scratchpad already empty");
                    return;
                }
                if (presses >= MaxClearPresses)
                {
                    GiveUp();
                    return;
                }
                if (!Send(unit, new[] { "CLR" }))
                    return;
                ownSent = KeysSent(unit);
                // The aircraft's time to answer starts when the CLR has been released, not when it was queued.
                if (!await KeysLandedAsync(unit, ownSent, waitForMargin: false)
                    || !await ScratchpadChangedAsync(unit, screen.Scratchpad))
                {
                    GiveUp();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"MCDU scratchpad clear failed: {ex.Message}");
        }
        finally
        {
            _clearing = false;
        }
    }

    /// <summary>The clear-all's one failure sentence; nothing once the window is gone (an aircraft switch).</summary>
    private void GiveUp()
    {
        if (!IsDisposed)
            _announcer.Announce(ClearFailed);
    }

    /// <summary>
    /// Waits until the definition's key queue is empty and, when asked, until <see cref="SettleMarginMs"/> has passed
    /// since the last key sent to <paramref name="unit"/> was released (<see cref="OnKeyReleased"/>, whether or not the
    /// unit is still the one shown): the time the aircraft needs to apply it, deliver the screen and be polled, so what
    /// the keys typed or deleted is on it. The margin is skipped when no key other than this clear's own CLRs has gone
    /// to the unit since <paramref name="ownSent"/>, the count taken after its last CLR: the change of the scratchpad
    /// has already answered that CLR, and waiting out its margin again would cost over half a second a character. False
    /// when the queue has not shrunk for <see cref="ClearConfirmMs"/> (nothing is moving), or the window is gone. The
    /// bound is on progress and not on the queue as it was when the wait began, since more keys can be queued after
    /// that (an entry typed on the other unit) and a slow pump is not a stuck one.
    /// </summary>
    private async Task<bool> KeysLandedAsync(A300McduUnit unit, int ownSent, bool waitForMargin)
    {
        int last = _definition.McduKeysPending;
        var shrankAt = UtcNow();
        while (true)
        {
            int pending = _definition.McduKeysPending;
            if (pending < last)
                shrankAt = UtcNow();
            last = pending;
            if (pending == 0 && !MarginPending(unit, ownSent, waitForMargin))
                return true;
            if (UtcNow() - shrankAt >= TimeSpan.FromMilliseconds(ClearConfirmMs))
                return false;
            await Delay(ClearPollMs);
            if (IsDisposed)
                return false;
        }
    }

    /// <summary>Whether the settle margin after the unit's last released key is still running, for a clear that has
    /// sent the unit <paramref name="ownSent"/> keys as of its last CLR (-1: none yet).</summary>
    private bool MarginPending(A300McduUnit unit, int ownSent, bool waitForMargin) =>
        waitForMargin
        && KeysSent(unit) != ownSent
        && UtcNow() < _lastRelease.GetValueOrDefault(unit).AddMilliseconds(SettleMarginMs);

    /// <summary>
    /// Waits, looking every <see cref="ClearPollMs"/>, until the unit's scratchpad on a page reads other than
    /// <paramref name="before"/>; false once <see cref="ClearConfirmMs"/> has passed without that, or the window is
    /// gone. A blank frame is the page being redrawn, not an answer. Any change counts, so a CLR queued behind other
    /// keys looks like a CLR that has landed, not one the aircraft ignored: the caller has the queue empty before it
    /// reads <paramref name="before"/> and starts this once its CLR has been released (<see cref="KeysLandedAsync"/>).
    /// </summary>
    private async Task<bool> ScratchpadChangedAsync(A300McduUnit unit, string before)
    {
        var deadline = UtcNow().AddMilliseconds(ClearConfirmMs);
        while (true)
        {
            await Delay(ClearPollMs);
            if (IsDisposed)
                return false;
            if (Screens(unit) is { IsBlank: false } screen && screen.Scratchpad != before)
                return true;
            if (UtcNow() >= deadline)
                return false;
        }
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// One poll: keeps the aircraft's MCDU export on, ticks both units' blank clocks and shows the selected unit's
    /// screen. The export is asked for before anything else, whether or not there is a feed yet: a plain ask is free
    /// while the export is known to be on, and it is what brings the screens back after a flight load or a connect
    /// that came after the window opened.
    /// </summary>
    internal void Tick()
    {
        _definition.EnsureMcduExport();
        var now = UtcNow();
        TickBlankClocks(now);
        if (!HasFeed())
        {
            _statusBox.Text = $"{UnitName}: not connected";
            return;
        }
        var screen = Screens(_unit);
        if (screen == null)
        {
            // No frame for this unit (a reconnect's new data manager has delivered none yet): a page drawn
            // before is not true any more, so the waiting row takes its place.
            _rendered = null;
            _rows = null;
            ShowWaiting();
            return;
        }
        // The FMS's blank frame between pages: draw nothing, say nothing, leave the page and the
        // cursor where they are. Judged before the reference check: a blank that stays is delivered
        // once, so only a poll can tell that it has lasted.
        if (_blankClocks.Judge(_unit, screen, now) == A300McduDisplayAction.HoldLastPage)
            return;
        if (!ReferenceEquals(screen, _rendered))
            Render(screen, silent: false);
        if (!screen.IsBlank && _scratchpad.OnPoll(screen.Scratchpad.Trim(), now) is { } say)
            _announcer.Announce(say);
        UpdateAnnunciators(silent: false);
    }

    /// <summary>Both units' blank clocks, from the screens the data manager has cached, whichever unit the window
    /// shows: the aircraft keeps publishing both, so a unit that has stayed blank is believed blank the moment the
    /// window switches to it (<see cref="A300McduBlankClocks"/>).</summary>
    private void TickBlankClocks(DateTime now)
    {
        foreach (var unit in Units)
            _blankClocks.Tick(unit, Screens(unit), now);
    }

    /// <summary>Each unit's lit annunciators when last shown.</summary>
    private readonly Dictionary<A300McduUnit, IReadOnlyList<string>> _lit = new();

    /// <summary>
    /// The unit's lit annunciators in the status box ("Captain MCDU: MSG"), and one coming on spoken, as the
    /// MD-11's MCDU window does: MSG lighting is how the FMS says "read the scratchpad", with no text change
    /// behind it. The captain's MSG is left to its fault light, "MCDU message light", which speaks anywhere
    /// (<see cref="A300McduLights.ComingOn"/>), so it is not said twice. A unit switch or a re-show takes the
    /// lights as they are, silently.
    /// </summary>
    private void UpdateAnnunciators(bool silent)
    {
        var lit = _definition.McduAnnunciators(_unit);
        bool seen = _lit.TryGetValue(_unit, out var before);
        _lit[_unit] = lit;
        if (_rendered is { IsBlank: false })
            _statusBox.Text = A300McduLights.Status(UnitName, lit);
        if (!silent && seen && A300McduLights.ComingOn(before!, lit, _unit) is { } on)
            _announcer.Announce(on);
    }

    private string UnitName => _unit == A300McduUnit.Captain ? "Captain MCDU" : "First Officer MCDU";

    private void Render(A300McduScreen screen, bool silent)
    {
        _rendered = screen;
        if (screen.IsBlank)
        {
            _rows = null;
            _display.SetLines(new[] { $"{UnitName} is blank. It may be unpowered." });
            _statusBox.Text = $"{UnitName}: blank";
            return;
        }
        _statusBox.Text = A300McduLights.Status(UnitName, _definition.McduAnnunciators(_unit));

        var previous = CursorRow();
        var rows = A300McduRows.Build(screen);
        _display.SetLines(rows.Select(r => r.Text).ToList());
        _rows = rows;

        string title = screen.Title.Trim();
        bool changed = !_lastTitle.TryGetValue(_unit, out var last) || last != title;
        _lastTitle[_unit] = title;
        int index = changed ? A300McduRows.PageStart(rows) : A300McduRows.Restore(rows, previous);
        if (changed && !silent && title.Length > 0)
            _announcer.Announce(title);
        if (index < 0 && _display.SelectedIndex < 0)
            index = A300McduRows.PageStart(rows);
        if (index >= 0 && index < _display.Items.Count && _display.SelectedIndex != index)
            _display.SelectedIndex = index;
    }

    private A300McduRow? CursorRow()
    {
        int i = _display.SelectedIndex;
        return _rows != null && i >= 0 && i < _rows.Count ? _rows[i] : null;
    }

    /// <summary>Shows the selected unit's current page without speaking it, and re-seeds the
    /// scratchpad read-back: a unit switch or a re-show is not news.</summary>
    private void Resync()
    {
        _rows = null;
        _rendered = null;
        _scratchpad.Reset();
        var screen = Screens(_unit);
        if (screen == null)
        {
            ShowWaiting();
            return;
        }
        // A blank the unit's clock has not yet judged shows the waiting row, never "blank" (it may be a page
        // change). The clock is not started over here: every poll ticks both units' (TickBlankClocks), so a unit
        // that has been blank for a while is blank at once, and only a re-show starts them again (PrepareToShow).
        if (_blankClocks.Follow(_unit, screen, UtcNow()) == A300McduDisplayAction.ShowWaiting)
            ShowWaiting();
        else
            Render(screen, silent: true);
        // The read-back is seeded from a page only. A blank frame reads an empty scratchpad that is not one:
        // seeded with it, the page that follows would read its whole scratchpad back as news. Poll seeds it
        // from the first page, silently.
        if (!screen.IsBlank)
            _scratchpad.OnPoll(screen.Scratchpad.Trim(), UtcNow());
        UpdateAnnunciators(silent: true);
    }

    private void ShowWaiting()
    {
        _display.SetLines(new[] { $"{UnitName}: waiting for data" });
        _statusBox.Text = $"{UnitName}: waiting for data";
    }

    // ---------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// What <see cref="ShowForm"/> does before it shows a hidden window: catches up silently on whatever the MCDU
    /// shows now, with every blank clock started over (while the window was hidden nothing polled, so how long a
    /// unit had been blank is unknown), and puts the cursor where the pilot will find it. Shown again on the same
    /// page it is back on the row the pilot left; on any other page, and on a first open, it is on the title, which
    /// the screen reader's own read of the focused row names.
    /// </summary>
    internal void PrepareToShow()
    {
        var left = CursorRow();
        _lastTitle.TryGetValue(_unit, out var before);
        _blankClocks.Reset();
        Resync();
        if (_rows == null || _display.Items.Count == 0)
            return;
        bool samePage = before != null && _lastTitle.TryGetValue(_unit, out var now) && now == before;
        int index = samePage ? A300McduRows.Restore(_rows, left) : 0;
        if (index >= 0 && index < _display.Items.Count)
            _display.SelectedIndex = index;
    }

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        // On every call, not only when the window was hidden: the aircraft forgets the export at each flight load
        // and the tablet's setting can be switched off by hand, so what MSFSBA remembers sending is no proof. A
        // pilot who finds the screens stale can press Shift+M again. Idempotent.
        _definition.EnsureMcduExport(force: true);
        if (!Visible)
        {
            PrepareToShow();
            _poll.Start();
        }
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        ActiveControl = _display;
        _display.Focus();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible)
            _poll.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _definition.McduKeyReleased -= OnKeyReleased;
            _poll.Stop();
            _poll.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// The A300 MCDU window's form-wide keys, as a pure function of the chord, what has focus and the
/// line-select layout setting. Same two rules as the MD-11 window's: a chord the focused unit
/// selector needs is never taken (<see cref="MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey"/>),
/// and a binding matches its own chord and nothing more.
/// </summary>
public static class A300McduKeyRouting
{
    public enum ComboFocus { None, Closed, Dropped }

    public enum ActionKind { Pass, Press, Close, SelectUnit, FocusInput, FocusDisplay }

    public readonly record struct Action(ActionKind Kind, string Key = "", A300McduUnit Unit = A300McduUnit.Captain)
    {
        public static readonly Action Pass = new(ActionKind.Pass);
        public static Action Press(string key) => new(ActionKind.Press, key);
    }

    public static Action Resolve(Keys keyCode, bool alt, bool control, bool shift, ComboFocus combo, bool useAlternateLskKeys)
    {
        if (combo != ComboFocus.None
            && MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey(keyCode, alt, control, shift, combo == ComboFocus.Dropped))
            return Action.Pass;

        bool none = !alt && !control && !shift;
        if (keyCode == Keys.Escape && none)
            return new Action(ActionKind.Close);

        // Line-select keys, two layouts (FMC Settings): Ctrl+1..6 left / Alt+1..6 right, or F1..F12.
        if (useAlternateLskKeys)
        {
            if (none && keyCode >= Keys.F1 && keyCode <= Keys.F6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.F1 + 1, right: false));
            if (none && keyCode >= Keys.F7 && keyCode <= Keys.F12)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.F7 + 1, right: true));
        }
        else
        {
            if (control && !alt && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.D1 + 1, right: false));
            if (alt && !control && !shift && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                return Action.Press(A300McduKeys.Lsk(keyCode - Keys.D1 + 1, right: true));
        }

        // Slew: the unmodified page keys move through the content being read, as on every CDU window.
        if (none && keyCode == Keys.PageUp) return Action.Press("UARROW");
        if (none && keyCode == Keys.PageDown) return Action.Press("DOWN");

        // The Alt-only chords. Alt+Up and Alt+Down slew too, as in the MD-11, FBW and Fenix MCDU windows; they sit
        // after the combo guard above because a focused unit selector needs both to open and close its list.
        if (alt && !control && !shift)
        {
            switch (keyCode)
            {
                case Keys.Up: return Action.Press("UARROW");
                case Keys.Down: return Action.Press("DOWN");
                case Keys.Right: return Action.Press("NEXT");
                case Keys.S: return new Action(ActionKind.FocusInput);
                case Keys.Home: return new Action(ActionKind.FocusDisplay);
            }
        }

        if (control && shift && !alt)
        {
            switch (keyCode)
            {
                case Keys.L: return new Action(ActionKind.SelectUnit, Unit: A300McduUnit.Captain);
                case Keys.R: return new Action(ActionKind.SelectUnit, Unit: A300McduUnit.FirstOfficer);
            }
        }
        return Action.Pass;
    }
}
