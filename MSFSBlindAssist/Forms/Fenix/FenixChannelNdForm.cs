using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Forms.Fenix;

/// <summary>
/// The ND's window ("Fenix ND"): the captain's navigation display as <see cref="FenixChannelNdRows"/> words it, and
/// under the list the captain's EFIS controls: Mode and Range combos and the CSTR, WPT, VOR.D, NDB, ARPT and LS
/// buttons (<see cref="FenixChannelNdControls"/>). A class of its own because Fenix windows are tracked one for each
/// type (<see cref="FenixChannelDisplayForm"/>). Alt+N and the Fenix windows list's "ND" entry open it
/// (Aircraft/FenixA320Definition.ChannelNd.cs, FenixWindowRegistry.ChannelNd.cs).
/// </summary>
/// <remarks>
/// <para>The controls follow every refresh from the snapshot the rows are read from (<see cref="OnRefresh"/>). While
/// the ND is not trusted the combos show no selection and the buttons read their bare names. A pick or a press calls
/// the writer with the panel's own key and number and nothing else: a combo's index, or 1 for a button. The full
/// press-release of a button is the writer's work ([A320-3], [FNX-6]), never this window's. A refresh never writes,
/// and leaves alone a combo the pilot is on.</para>
/// <para>Silence ([CORE-7]). The window says nothing itself: the screen reader reads the combos and the buttons'
/// text, and the lights are already announced.</para>
/// </remarks>
internal sealed class FenixChannelNdForm : FenixChannelDisplayForm
{
    private readonly Action<string, double>? _writeControl;
    private readonly List<Button> _buttons = [];

    // True while a refresh sets the controls, so that nothing it sets can reach the writer.
    private bool _refreshing;

    private readonly MapPointsSource _mapPoints;
    private readonly FenixChannelMonitor _channel;

    /// <summary>
    /// The map points' poller, made on the first show and disposed with the window; "no data" until then (the tests,
    /// which never show the window, read it so).
    /// </summary>
    private sealed class MapPointsSource
    {
        private readonly object _lock = new();
        private FenixChannelNdMapPoller? _poller;
        private bool _closed;

        public (FenixNdMapPoints? Points, long ReceivedAtMs, string? Problem) Latest
        {
            get
            {
                FenixChannelNdMapPoller? poller;
                lock (_lock) poller = _poller;
                return poller?.Latest ?? (null, 0, null);
            }
        }

        public void StartOnce(FenixChannelMonitor monitor)
        {
            lock (_lock)
            {
                if (_poller is not null || _closed) return;

                _poller = new FenixChannelNdMapPoller(monitor.RequestOnceAsync, () => Environment.TickCount64);
                _poller.Start();
            }
        }

        public void Close()
        {
            FenixChannelNdMapPoller? poller;
            lock (_lock)
            {
                _closed = true;
                poller = _poller;
                _poller = null;
            }

            poller?.Dispose();
        }
    }

    /// <param name="monitor">The channel monitor the snapshots come from.</param>
    /// <param name="writeControl">Writes a panel control: its variable name and the number the panel takes. With it the
    /// window adds the EFIS controls; without it, it adds none.</param>
    public FenixChannelNdForm(FenixChannelMonitor monitor, Action<string, double>? writeControl = null)
        : this(monitor, writeControl, new MapPointsSource())
    {
    }

    // The composer reads the map points through the source, which exists before the base constructor runs and holds the
    // poller once the window is first shown.
    private FenixChannelNdForm(FenixChannelMonitor monitor, Action<string, double>? writeControl, MapPointsSource source)
        : base("ND", FenixChannelDisplay.Nd, monitor,
            snapshot => FenixChannelNdRows.Compose(
                snapshot, problem => monitor.ReportFeatureProblem(FenixChannelNdRows.Feature, problem),
                source.Latest, Environment.TickCount64))
    {
        _mapPoints = source;
        _channel = monitor;
        _writeControl = writeControl;
        if (writeControl is null) return;

        ModeCombo = NewCombo("Mode", FenixChannelNdControls.Modes, FenixChannelNdControls.ModeKey);
        RangeCombo = NewCombo("Range", FenixChannelNdControls.Ranges, FenixChannelNdControls.RangeKey);

        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        panel.Controls.Add(ModeCombo);
        panel.Controls.Add(RangeCombo);
        foreach ((string name, string key) in FenixChannelNdControls.Buttons)
        {
            // No AccessibleName: the reader takes the button's Text, which changes only when its state does.
            var button = new Button { Text = name, AutoSize = true };
            button.Click += (_, _) => _writeControl!(key, 1);
            _buttons.Add(button);
            panel.Controls.Add(button);
        }

        for (int i = 0; i < panel.Controls.Count; i++) panel.Controls[i].TabIndex = i;
        AddControlAfterList(panel);
    }

    /// <summary>The Mode combo; null without a writer. For the tests.</summary>
    internal ComboBox? ModeCombo { get; }

    /// <summary>The Range combo; null without a writer. For the tests.</summary>
    internal ComboBox? RangeCombo { get; }

    /// <summary>The six buttons in order (<see cref="FenixChannelNdControls.Buttons"/>); empty without a writer.</summary>
    internal IReadOnlyList<Button> EfisButtons => _buttons;

    private ComboBox NewCombo(string name, IReadOnlyList<string> items, string key)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = name };
        combo.Items.AddRange([.. items]);
        // Only the pilot's own pick raises this; setting SelectedIndex in code does not.
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (!_refreshing && combo.SelectedIndex >= 0) _writeControl!(key, combo.SelectedIndex);
        };
        return combo;
    }

    protected override void OnRefresh(FenixChannelSnapshot s) => ApplyControls(s);

    // The map points' poller starts on the first show (the tests never show the window).
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _mapPoints.StartOnce(_channel);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _mapPoints.Close();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _mapPoints.Close();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Sets the controls from one snapshot: with the ND trusted and its header read, the combos select the mode and
    /// range (no selection for a value not in the list) and each button reads its state; otherwise the combos show no
    /// selection (never a stale mode) and the buttons read their bare names. A combo with focus or an open list is
    /// left as the pilot has it.
    /// </summary>
    internal void ApplyControls(FenixChannelSnapshot s)
    {
        if (ModeCombo is null || RangeCombo is null) return;

        FenixNdData? header = null;
        if (s.Verdict.Trusted)
        {
            FenixChannelSection<FenixNdData> section = FenixChannelNd.Read(s);
            if (section.Problem is null) header = section.Value;
        }

        _refreshing = true;
        try
        {
            Select(ModeCombo, FenixChannelNdControls.ModeIndex(header?.Mode));
            Select(RangeCombo, FenixChannelNdControls.RangeIndex(header?.Range));
            for (int i = 0; i < _buttons.Count; i++)
            {
                string text = FenixChannelNdControls.ButtonText(FenixChannelNdControls.Buttons[i].Name, header);
                if (_buttons[i].Text != text) _buttons[i].Text = text;
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static void Select(ComboBox combo, int index)
    {
        if (combo.Focused || combo.DroppedDown) return;
        if (combo.SelectedIndex != index) combo.SelectedIndex = index;
    }
}
