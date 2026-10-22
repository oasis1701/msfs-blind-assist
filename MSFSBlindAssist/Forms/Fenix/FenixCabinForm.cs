using System.Runtime.InteropServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The Fenix "Cabin" window (Fenix windows list, Ctrl+Shift+F in input mode): what the Fenix's flight
/// attendant panel (FAP) shows and does, on five pages: Status, Audio, Doors, Temperature and Water. A
/// read-only status box at the top says whether the Fenix answers and whether the cabin system has
/// started; each page lists its values one per line. Ctrl+Tab moves between the pages, Escape closes.
/// This file is the shell and the Status page; <c>FenixCabinForm.Audio.cs</c> (plan 08 Task 8) and
/// <c>FenixCabinForm.Controls.cs</c> (Task 9) build the other pages through the partial methods below.
/// </summary>
/// <remarks>
/// <para>Data: a <see cref="FenixDataPoller"/> reads every cabin name once a second (ruling R3: the data
/// service's subscription does not stream every name). Its values arrive on a pool thread ([FNX-2]) and
/// reach <see cref="_cache"/> through <see cref="SafeBeginInvoke"/> ([A380C-6]). Every value is applied,
/// Unknown included, so a gateway that stops answering turns the lines to "no data" by itself ([FNX-1]).
/// The FAP's audio state is read every 2 s. A 300 ms timer rebuilds the lines when something changed,
/// and on every tick collects the read-backs.</para>
/// <para>Speech (CORE-7): nothing on opening; a refusal or failure interrupts (<see cref="Refuse"/>); a
/// read-back after a press is queued. The app-wide call that the cabin is ready belongs to
/// <see cref="FenixCabinReadyMonitor"/> alone: the window only shows the value.</para>
/// <para>Writes go only through the bundle builders (<see cref="FenixCabinWrites"/>,
/// <see cref="FenixCabinPram"/>) and only while the Fenix answers.</para>
/// </remarks>
public sealed partial class FenixCabinForm : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int WM_KEYDOWN = 0x0100;
    private const string LogCategory = "FenixCabin";

    // The Fenix settings the layout is resolved from (FenixCabinLayout.Resolve).
    private const string ExitsSettingKey = "emergency_exits_type";
    private const string VariantSettingKey = "aircraft_variant";
    private const string SlideMessageSettingKey = "fap_slide_disarmed_message";

    private const string NotAnswering = "The Fenix is not answering.";
    private const string NotAccepted = "The Fenix did not accept that.";

    /// <summary>How long a read-back waits for its line to change: 5 s for every press but a door's.</summary>
    private static readonly TimeSpan ReadbackWindow = TimeSpan.FromSeconds(5);

    // After the pilot changes a check box or a combo, the render leaves the controls alone this long. A
    // render from values read before the write landed would flip the control back for a moment, and the
    // reader would say the old state and then the new one.
    private static readonly TimeSpan ControlHold = TimeSpan.FromSeconds(3);

    // The cabin-lights combo's levels, in its order.
    private static readonly FenixCabinLightLevel[] LightLevels =
    {
        FenixCabinLightLevel.Bright, FenixCabinLightLevel.Dim1, FenixCabinLightLevel.Dim2, FenixCabinLightLevel.Off
    };

    private readonly FenixWindowContext _context;
    private readonly FenixCabinData _cache = new();
    private readonly FenixCabinReadback _readback = new();
    private readonly System.Windows.Forms.Timer _renderTimer;
    private readonly System.Windows.Forms.Timer _pramTimer;
    private readonly Dictionary<TabPage, FenixCabinSection> _pageSections = new();
    private readonly List<(FenixCabinSection Section, DisplayListBox List)> _lineLists = new();

    private FenixCabinLayout _layout;
    private FenixCabinPramState? _pram;
    // The lines of the last render, every page's, in page order.
    private IReadOnlyList<FenixCabinLine> _lines = Array.Empty<FenixCabinLine>();
    // Set while the render sets the controls: their change handlers return at once.
    private bool _updatingControls;
    private bool _dirty = true;

    private TextBox _statusBox = null!;
    private TabControl _tabs = null!;
    private ListBox _statusList = null!;
    private CheckBox _cabinReadyBox = null!;
    private ComboBox _lightsCombo = null!;

    private FenixDataPoller? _poller;
    private string? _exitsSetting;
    private string? _variantSetting;
    private string? _slideMessageSetting;
    private bool _wasAnswering;
    private bool _pramInFlight;
    private bool _pramAgain;
    private bool _controlsHeld;
    private DateTime _controlsHeldUntil;
    private bool _renderFailureLogged;
    private bool _started;
    private bool _tornDown;
    private IntPtr _previousWindow;

    public FenixCabinForm(FenixWindowContext context)
    {
        _context = context;
        // ShowForm resolves it for the loaded airframe; until then the FAP's own start, an A320.
        _layout = FenixCabinLayout.Resolve(FenixAircraftType.Unknown, null, null, null);
        _renderTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _renderTimer.Tick += (_, _) => OnRenderTick();
        _pramTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _pramTimer.Tick += (_, _) => _ = RefreshPramAsync();
        Build();
    }

    // ---- Hooks for the other pages ----------------------------------------------------------------

    /// <summary>Task 8 (<c>FenixCabinForm.Audio.cs</c>) builds the Audio page; called once, from the constructor.</summary>
    partial void BuildAudioTab(TabPage page);

    /// <summary>Task 9 (<c>FenixCabinForm.Controls.cs</c>) builds the Doors, Temperature and Water pages; called once, from the constructor.</summary>
    partial void BuildControlTabs(TabPage doors, TabPage temperature, TabPage water);

    /// <summary>
    /// Sets the Audio page's announcement and memo lists from <see cref="_pram"/>; called by every render, also
    /// while the controls are held: a list is data the presses act on, not a control that flips back.
    /// </summary>
    partial void UpdateAudioLists();

    /// <summary>Sets the Audio page's controls from <see cref="_pram"/>; called by the render with <see cref="_updatingControls"/> set.</summary>
    partial void UpdateAudioControls();

    /// <summary>Sets the Doors, Temperature and Water pages' controls; called by the render with <see cref="_updatingControls"/> set.</summary>
    partial void UpdateControlTabs();

    // ---- Building -------------------------------------------------------------------------------

    private void Build()
    {
        Text = "Fenix cabin";
        AccessibleName = "Fenix cabin";
        Size = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;

        // Read-only TextBox, never a Label, so it is in the tab order ([VAT-13]).
        _statusBox = new TextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Top,
            AccessibleName = "Cabin system status",
            TabIndex = 0
        };

        _tabs = new TabControl { Dock = DockStyle.Fill, TabIndex = 1 };
        TabPage status = AddPage("Status", FenixCabinSection.Status);
        TabPage audio = AddPage("Audio", FenixCabinSection.Audio);
        TabPage doors = AddPage("Doors", FenixCabinSection.Doors);
        TabPage temperature = AddPage("Temperature", FenixCabinSection.Temperature);
        TabPage water = AddPage("Water", FenixCabinSection.Water);

        // Docking starts from the control added last: the status box takes the top, the pages the rest.
        Controls.Add(_tabs);
        Controls.Add(_statusBox);

        BuildStatusTab(status);
        BuildAudioTab(audio);
        BuildControlTabs(doors, temperature, water);

        FormClosing += (_, _) =>
        {
            TearDown();
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        };
    }

    // No AccessibleName (ruling R12): the reader reads the page's text.
    private TabPage AddPage(string text, FenixCabinSection section)
    {
        var page = new TabPage(text);
        _tabs.TabPages.Add(page);
        _pageSections[page] = section;
        return page;
    }

    /// <summary>
    /// Adds a page's value list: a <see cref="DisplayListBox"/> filling the page, which the render keeps
    /// equal to the texts of that page's lines (<see cref="FenixCabinLine.Section"/>), in order, rewriting
    /// only the rows that changed and keeping the selection. Controls the page adds dock around it.
    /// </summary>
    private ListBox AddLineList(TabPage page, string accessibleName)
    {
        var list = new DisplayListBox { Dock = DockStyle.Fill, AccessibleName = accessibleName, TabIndex = 0 };
        page.Controls.Add(list);
        // The first control in z-order is docked last, so the list fills what the page's other controls leave.
        list.BringToFront();
        _lineLists.Add((_pageSections[page], list));
        return list;
    }

    private void BuildStatusTab(TabPage page)
    {
        _statusList = AddLineList(page, "Cabin status");

        // No AccessibleName (ruling R12): the reader reads the check box's text.
        _cabinReadyBox = new CheckBox { Text = "Cabin &ready", AutoSize = true, TabIndex = 0 };
        _cabinReadyBox.CheckedChanged += OnCabinReadyChanged;

        var lightsLabel = new Label { Text = "Cabin &lights", AutoSize = true, Margin = new Padding(12, 7, 3, 3), TabIndex = 1 };
        _lightsCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Cabin lights",
            Width = 160,
            TabIndex = 2
        };
        foreach (FenixCabinLightLevel level in LightLevels)
            _lightsCombo.Items.Add(FenixCabinWrites.LightName(level));
        _lightsCombo.SelectionChangeCommitted += OnLightsCommitted;

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            TabIndex = 1
        };
        controls.Controls.Add(_cabinReadyBox);
        controls.Controls.Add(lightsLabel);
        controls.Controls.Add(_lightsCombo);
        page.Controls.Add(controls);
    }

    // ---- Opening and closing --------------------------------------------------------------------

    public void ShowForm()
    {
        if (!Visible) _previousWindow = GetForegroundWindow();
        bool first = !_started;
        if (first)
        {
            _started = true;
            _context.Definition.VariantChanged += OnVariantChanged;
            ResolveLayout();
            // Every line reads "no data" until the first answer; the status list is where the pilot lands.
            Render();
            ActiveControl = _statusList;
        }
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;

        // The pilot's own open: the only focus calls in the form.
        Show();
        BringToFront();
        Activate();
        if (first)
        {
            _statusList.Focus();
            StartReading();
        }
    }

    // Once, after Show, so the window's handle exists for the first answer: the once-a-second read of
    // every cabin name (its first read is immediate), the settings, the audio state and both timers.
    private void StartReading()
    {
        _poller = new FenixDataPoller(FenixCabinNames.All,
            (names, ct) => _context.Data.ReadManyAsync(names, ct),
            (n, v) => SafeBeginInvoke(() => { if (_cache.Apply(n, v)) MarkDirty(); }));
        _poller.Start();
        _ = LoadSettingsAsync();
        RequestPramRefresh();
        _renderTimer.Start();
        _pramTimer.Start();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Escape closes, unless it is closing a combo's open list.
        if (msg.Msg == WM_KEYDOWN && keyData == Keys.Escape && ActiveControl is not ComboBox { DroppedDown: true })
        {
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Stops the poll and both timers and lets go of the definition; safe to call twice. After it, an
    // answer still on its way changes nothing the pilot hears: the async paths check _tornDown, and a
    // late polled value only marks a render that never runs.
    private void TearDown()
    {
        if (_tornDown) return;
        _tornDown = true;
        // Never throws: a read in flight is cancelled and its answer dropped.
        _poller?.Dispose();
        _poller = null;
        _renderTimer.Stop();
        _pramTimer.Stop();
        if (_started) _context.Definition.VariantChanged -= OnVariantChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TearDown();
            _renderTimer.Dispose();
            _pramTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Marshals to the UI thread, tolerating the form being torn down meanwhile (closed, or the
    /// aircraft switched). Same pattern as MainForm.SafeBeginInvoke ([A380C-6]).</summary>
    private void SafeBeginInvoke(Action action)
    {
        // ObjectDisposedException derives from InvalidOperationException, so one catch covers both.
        try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
        catch (InvalidOperationException) { }
    }

    // ---- Layout ---------------------------------------------------------------------------------

    // Reads the three layout settings. On open, and again each time the Fenix starts answering, so a
    // window opened before the Fenix loaded still gets its doors and lavatories. Continues on the UI thread.
    private async Task LoadSettingsAsync()
    {
        try
        {
            IReadOnlyList<FenixSetting> settings = await _context.Data.GetSettingsAsync();
            if (_tornDown || IsDisposed) return;
            // An empty answer (the Fenix did not answer) keeps what an earlier read found.
            if (settings.Count == 0) return;
            _exitsSetting = SettingValue(settings, ExitsSettingKey);
            _variantSetting = SettingValue(settings, VariantSettingKey);
            _slideMessageSetting = SettingValue(settings, SlideMessageSettingKey);
            ResolveLayout();
            MarkDirty();
        }
        catch (Exception ex)
        {
            // GetSettingsAsync answers empty rather than throw; logged in case it ever does.
            Log.Debug(LogCategory, $"Cabin settings read failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? SettingValue(IReadOnlyList<FenixSetting> settings, string key)
        => settings.FirstOrDefault(setting => string.Equals(setting.Key, key, StringComparison.Ordinal))?.Value;

    // Each resolve holds fresh lists, so a new layout is never compared with the old one: the render runs again.
    private void ResolveLayout()
        => _layout = FenixCabinLayout.Resolve(_context.Definition.Variant.Type, _exitsSetting, _variantSetting, _slideMessageSetting);

    // Raised on the UI thread when the loaded airframe changes.
    private void OnVariantChanged(object? sender, FenixVariant variant)
    {
        if (_tornDown || IsDisposed) return;
        ResolveLayout();
        MarkDirty();
    }

    // ---- Audio state ----------------------------------------------------------------------------

    // Reads the audio state now, or right after the read in flight, whose answer may predate a press.
    private void RequestPramRefresh()
    {
        if (_pramInFlight) _pramAgain = true;
        else _ = RefreshPramAsync();
    }

    // One read of the FAP's audio state, skipped while one is in flight. Continues on the UI thread.
    private async Task RefreshPramAsync()
    {
        if (_tornDown || _pramInFlight) return;
        _pramInFlight = true;
        try
        {
            var answer = await _context.Data.QueryAsync(FenixCabinPram.StateQuery);
            if (_tornDown || IsDisposed) return;
            // No answer parses as null: the Audio page then says it has no data from the Fenix.
            FenixCabinPramState? pram = FenixCabinPram.Parse(answer);
            if (!SamePram(_pram, pram))
            {
                _pram = pram;
                MarkDirty();
            }
        }
        catch (Exception ex)
        {
            // The data service answers null rather than throw; logged in case it ever does.
            Log.Debug(LogCategory, $"Cabin audio state read failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _pramInFlight = false;
            if (_pramAgain && !_tornDown && !IsDisposed)
            {
                _pramAgain = false;
                _ = RefreshPramAsync();
            }
        }
    }

    /// <summary>
    /// True when two audio states say the same: every field, and every list item by item. The record's
    /// own equality compares the lists by reference, and each read parses fresh ones.
    /// </summary>
    internal static bool SamePram(FenixCabinPramState? a, FenixCabinPramState? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.MusicOn == b.MusicOn
            && a.SelectedChannel == b.SelectedChannel
            && a.Volume == b.Volume
            && string.Equals(a.NowAnnouncing, b.NowAnnouncing, StringComparison.Ordinal)
            && a.PlayingAllInMemo == b.PlayingAllInMemo
            && a.PreferredVariant == b.PreferredVariant
            && a.MaxVariant == b.MaxVariant
            && a.Channels.SequenceEqual(b.Channels)
            && a.Memo.SequenceEqual(b.Memo)
            && a.Announcements.SequenceEqual(b.Announcements);
    }

    // ---- Rendering ------------------------------------------------------------------------------

    /// <summary>Asks the next render tick (at most 300 ms away) to rebuild the lines and the controls.</summary>
    private void MarkDirty() => _dirty = true;

    private void OnRenderTick()
    {
        if (_tornDown || IsDisposed) return;
        if (_controlsHeld && DateTime.UtcNow >= _controlsHeldUntil)
        {
            _controlsHeld = false;
            _dirty = true;
        }
        if (_dirty)
        {
            _dirty = false;
            try
            {
                Render();
            }
            catch (Exception ex)
            {
                // A 300 ms timer: logged once, and the window carries on.
                if (!_renderFailureLogged)
                {
                    _renderFailureLogged = true;
                    Log.Error(LogCategory, $"Cabin window render failed: {ex.GetType().Name}: {ex.Message}", ex);
                }
            }
        }
        // Every tick, so a numeric press whose window has ended answers "unchanged" at once.
        foreach (string text in _readback.Collect(_lines, DateTime.UtcNow))
            _context.Announcer.Announce(text);
    }

    /// <summary>For the tests: the values the window shows, as the poll applies them.</summary>
    internal FenixCabinData Cache => _cache;

    // Rebuilds every line, the status box, the lists and, unless the pilot has just changed one, the
    // controls. UI thread; never focuses, never speaks. Internal for the tests.
    internal void Render()
    {
        bool answering = _cache.AnyKnown;
        if (answering && !_wasAnswering) _ = LoadSettingsAsync();
        _wasAnswering = answering;

        _lines = FenixCabinLines.Build(_cache, _pram, _layout);

        string status = FenixCabinLines.ConnectionStatus(answering, _cache);
        if (_statusBox.Text != status) _statusBox.Text = status;

        foreach ((FenixCabinSection section, DisplayListBox list) in _lineLists)
        {
            list.SetLines(SectionLines(section).Select(line => line.Text).ToArray());
            // A list with no selection reads no current line ([MON-6]'s reasoning).
            if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
        }

        // The audio lists follow the Fenix on every render: a press acts on the entry the list shows.
        UpdateAudioLists();

        if (_controlsHeld) return;
        _updatingControls = true;
        try
        {
            UpdateStatusControls();
            UpdateAudioControls();
            UpdateControlTabs();
        }
        finally
        {
            _updatingControls = false;
        }
    }

    /// <summary>A page's lines from the last render, in order.</summary>
    private IReadOnlyList<FenixCabinLine> SectionLines(FenixCabinSection section)
        => _lines.Where(line => line.Section == section).ToArray();

    /// <summary>The text of the line with this key in the last render, or null when there is none.</summary>
    private string? LineText(string key)
        => _lines.FirstOrDefault(line => string.Equals(line.Key, key, StringComparison.Ordinal))?.Text;

    private void UpdateStatusControls()
    {
        bool ready = _cache.Bool(FenixCabinNames.CabinReady) == true;
        if (_cabinReadyBox.Checked != ready) _cabinReadyBox.Checked = ready;

        // An unknown or unexpected level selects none, never a level ([FNX-1]).
        int light = FenixCabinWrites.ParseLight(_cache.Text(FenixCabinNames.CabinLights)) is FenixCabinLightLevel level
            ? Array.IndexOf(LightLevels, level)
            : -1;
        // Left alone while the pilot has its list open.
        if (!_lightsCombo.DroppedDown && _lightsCombo.SelectedIndex != light) _lightsCombo.SelectedIndex = light;
    }

    // ---- Status page controls -------------------------------------------------------------------

    private void OnCabinReadyChanged(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        bool isChecked = _cabinReadyBox.Checked;
        // The Fenix down first (R13): every control gives that reason, not the availability it cannot know.
        if (!_cache.AnyKnown)
        {
            SetCabinReadyBox(_cache.Bool(FenixCabinNames.CabinReady) == true);
            Refuse(NotAnswering);
            return;
        }
        // The FAP greys its button unless the Fenix says cabin ready is available (D5); unknown is not.
        if (_cache.Bool(FenixCabinNames.CabinReadyAvailable) != true)
        {
            SetCabinReadyBox(_cache.Bool(FenixCabinNames.CabinReady) == true);
            Refuse("Cabin ready is not available yet.");
            return;
        }
        HoldControls();
        // No read-back: the reader has said the new state, and the app-wide call is the monitor's.
        _ = RunWriteAsync(FenixCabinWrites.CabinReady(isChecked), null, ReadbackWindow, speakIfUnchanged: false);
    }

    private void SetCabinReadyBox(bool isChecked)
    {
        _updatingControls = true;
        try
        {
            _cabinReadyBox.Checked = isChecked;
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private void OnLightsCommitted(object? sender, EventArgs e)
    {
        if (_updatingControls) return;
        int index = _lightsCombo.SelectedIndex;
        if (index < 0 || index >= LightLevels.Length) return;
        HoldControls();
        // No read-back: the reader has said the new level.
        _ = RunWriteAsync(FenixCabinWrites.CabinLights(LightLevels[index]), null, ReadbackWindow, speakIfUnchanged: false);
    }

    // ---- Presses --------------------------------------------------------------------------------

    /// <summary>
    /// Call from a check box's or combo's own change handler, before its write: the render leaves the
    /// controls alone for <see cref="ControlHold"/>, until the Fenix has had time to show the change. A
    /// refused or failed write lets go at once.
    /// </summary>
    private void HoldControls()
    {
        _controlsHeld = true;
        _controlsHeldUntil = DateTime.UtcNow + ControlHold;
    }

    // A refused or failed write: the next tick sets the controls from the Fenix's values again.
    private void ReleaseControls()
    {
        _controlsHeld = false;
        MarkDirty();
    }

    /// <summary>Says why a press did nothing, interrupting: a validation or failure message.</summary>
    private void Refuse(string message) => _context.Announcer.AnnounceImmediate(message);

    /// <summary>
    /// Sends one data-name write (<see cref="FenixCabinWrites"/>). While the Fenix does not answer nothing
    /// is sent and the pilot hears "The Fenix is not answering."; a write it refuses says "The Fenix did
    /// not accept that."; an accepted one with a <paramref name="readbackKey"/> waits
    /// <paramref name="window"/> for that line to change (<see cref="FenixCabinReadback.Expect"/>). UI thread.
    /// The result is true when the Fenix accepted the write; a press that sends several stops at the first false.
    /// </summary>
    private Task<bool> RunWriteAsync(FenixCabinWrite w, string? readbackKey, TimeSpan window, bool speakIfUnchanged)
        => RunAsync(() => _context.Data.SendAsync(w), readbackKey, window, speakIfUnchanged, refreshPram: false);

    /// <summary>
    /// Sends one FAP audio mutation (<see cref="FenixCabinPram"/>), as <see cref="RunWriteAsync"/> sends a
    /// write, with a 5 s read-back window; once accepted, the audio state is read again at once.
    /// </summary>
    private Task<bool> RunMutationAsync(FenixCabinMutation m, string? readbackKey, bool speakIfUnchanged)
        => RunAsync(() => _context.Data.SendAsync(m), readbackKey, ReadbackWindow, speakIfUnchanged, refreshPram: true);

    private async Task<bool> RunAsync(Func<Task<bool>> send, string? readbackKey, TimeSpan window, bool speakIfUnchanged, bool refreshPram)
    {
        if (!_cache.AnyKnown)
        {
            ReleaseControls();
            Refuse(NotAnswering);
            return false;
        }

        // The line as the pilot had it at the press: a change from this text is the press's answer, also
        // when the poll shows it before the write's own answer is back.
        string? pressText = readbackKey == null ? null : LineText(readbackKey);
        bool accepted;
        try
        {
            accepted = await send();
        }
        catch (Exception ex)
        {
            // The senders answer false rather than throw; logged in case one ever does.
            Log.Debug(LogCategory, $"Cabin write failed: {ex.GetType().Name}: {ex.Message}");
            accepted = false;
        }
        // Closed meanwhile: nothing is said once the window has gone.
        if (_tornDown || IsDisposed) return false;

        if (!accepted)
        {
            ReleaseControls();
            Refuse(NotAccepted);
            return false;
        }
        if (readbackKey != null)
            _readback.Expect(readbackKey, pressText, DateTime.UtcNow, window, speakIfUnchanged);
        if (refreshPram) RequestPramRefresh();
        return true;
    }
}
