using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps.Performance
{
    /// <summary>
    /// Shared base class for the three Performance Tool panels (Takeoff,
    /// Landing Dispatch, Landing Enroute). Holds the common wiring patterns:
    /// UnitField control triplet, plain/unit/combo/runway wire helpers,
    /// refresh scheduling, and the runway combo debounce timer lifecycle.
    /// </summary>
    public abstract class PerformancePanelBase : EfbAppPanelBase
    {
        /// <summary>
        /// Control triplet for a measurement input: the text box with its
        /// sibling unit combo, plus the PMDG preference key that flips the
        /// corresponding tablet-wide unit setting when the combo changes.
        /// </summary>
        protected sealed class UnitField
        {
            public string ValueId = "";
            public string UnitId = "";
            public string PrefKey = "";
            public string[] Options = Array.Empty<string>();
            public TextBox Box = null!;
            public ComboBox UnitCombo = null!;
            public bool IsOutput;
        }

        protected bool _suppressWrites;
        protected bool _awaitingCalculation;
        private System.Windows.Forms.Timer? _runwayDebounce;
        private readonly HashSet<TextBox> _dirtyBoxes = new();

        /// <summary>
        /// Bridge tag used by this panel for read_values and select_options
        /// responses. Lets each perf panel share the state channel without
        /// collisions.
        /// </summary>
        protected abstract string ValuesTag { get; }

        /// <summary>
        /// The runway-selection combo on this panel. Used by the debounced
        /// runway wiring and ApplyRunwayOptions.
        /// </summary>
        protected abstract ComboBox RunwayIdCombo { get; }

        /// <summary>
        /// DOM id of the runway select on the tablet for this panel.
        /// </summary>
        protected abstract string RunwayIdDomId { get; }

        /// <summary>
        /// Request a fresh read of every field this panel cares about.
        /// Implementations enqueue a read_values bridge command.
        /// </summary>
        protected abstract void RequestAllValues();

        // ---------------------------------------------------------------
        // Wire helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Plain text input that writes on LostFocus or Enter key — but only
        /// if the user actually typed something. Tabbing through an unchanged
        /// field must NOT re-send its value: set_input_by_id types the value
        /// character-by-character, and PMDG's wind field rejects non-numeric
        /// keys, so re-sending "VRB/5" would strip the letters and leave "/5",
        /// breaking Calculate on the tablet.
        /// </summary>
        protected void WirePlainTextInput(TextBox box, string domId)
        {
            box.TextChanged += (_, _) =>
            {
                if (!_suppressWrites && !box.ReadOnly) _dirtyBoxes.Add(box);
            };
            box.LostFocus += (_, _) =>
            {
                if (_suppressWrites || box.ReadOnly) return;
                if (!_dirtyBoxes.Contains(box)) return;
                CommitTextInput(box, domId);
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !box.ReadOnly && !_suppressWrites)
                {
                    if (_dirtyBoxes.Contains(box))
                        CommitTextInput(box, domId);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void CommitTextInput(TextBox box, string domId)
        {
            BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
            {
                ["id"] = domId,
                ["value"] = box.Text ?? ""
            });
            _dirtyBoxes.Remove(box);
            ScheduleRefreshAfter(600);
        }

        /// <summary>
        /// ComboBox that sends set_select_by_id on every SelectedIndexChanged.
        /// Fine for small lists (runway condition, flaps, rating, etc.) where
        /// arrow-key scanning isn't problematic.
        /// </summary>
        protected void WireComboSelect(ComboBox combo, string domId, bool useDataValue)
        {
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressWrites) return;
                if (combo.SelectedIndex < 0) return;
                string value = useDataValue
                    ? combo.SelectedIndex.ToString()
                    : (combo.SelectedItem?.ToString() ?? "");
                BridgeServer.EnqueueCommand("set_select_by_id", new Dictionary<string, string>
                {
                    ["id"] = domId,
                    ["value"] = value
                });
                ScheduleRefreshAfter(600);
            };
        }

        /// <summary>
        /// Runway combo with a 2-second debounce — commits on LostFocus
        /// immediately, or 2 seconds after the last arrow-key scroll. Prevents
        /// the combo from fighting the user during rapid navigation.
        /// </summary>
        protected void WireRunwayComboDebounced(ComboBox combo, string domId)
        {
            void CommitRunway()
            {
                if (_suppressWrites || combo.SelectedIndex < 0) return;
                string value = combo.SelectedItem?.ToString() ?? "";
                if (string.IsNullOrEmpty(value)) return;
                BridgeServer.EnqueueCommand("set_select_by_id", new Dictionary<string, string>
                {
                    ["id"] = domId,
                    ["value"] = value
                });
                ScheduleRefreshAfter(600);
            }

            combo.LostFocus += (_, _) =>
            {
                _runwayDebounce?.Stop();
                _runwayDebounce?.Dispose();
                _runwayDebounce = null;
                CommitRunway();
            };

            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressWrites) return;
                _runwayDebounce?.Stop();
                _runwayDebounce?.Dispose();
                _runwayDebounce = new System.Windows.Forms.Timer { Interval = 2000 };
                _runwayDebounce.Tick += (_, _) =>
                {
                    _runwayDebounce?.Stop();
                    _runwayDebounce?.Dispose();
                    _runwayDebounce = null;
                    CommitRunway();
                };
                _runwayDebounce.Start();
            };
        }

        // ---------------------------------------------------------------
        // Refresh scheduling
        // ---------------------------------------------------------------

        /// <summary>
        /// One-shot timer to call <see cref="RequestAllValues"/> after a delay.
        /// Used to give the tablet time to settle after a user interaction
        /// before re-reading everything.
        /// </summary>
        protected void ScheduleRefreshAfter(int ms)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (_, _) =>
            {
                t.Stop(); t.Dispose();
                if (!IsDisposed) RequestAllValues();
            };
            t.Start();
        }

        /// <summary>
        /// Fetch the current options list for the runway combo after a short
        /// delay. Used after ICAO changes so the runway list is populated
        /// before the refresh tries to select the current runway.
        /// </summary>
        protected void ScheduleRunwayRefresh()
        {
            var t = new System.Windows.Forms.Timer { Interval = 900 };
            t.Tick += (_, _) =>
            {
                t.Stop(); t.Dispose();
                if (IsDisposed || !BridgeServer.IsBridgeConnected) return;
                BridgeServer.EnqueueCommand("get_select_options", new Dictionary<string, string>
                {
                    ["tag"] = ValuesTag,
                    ["id"] = RunwayIdDomId
                });
            };
            t.Start();
        }

        // ---------------------------------------------------------------
        // State application helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Populate the runway combo from a select_options state update.
        /// Clears existing items, adds each option, restores the selection
        /// based on the tablet's current value, and announces the count.
        /// </summary>
        protected void ApplyRunwayOptions(Dictionary<string, string> d)
        {
            if (!int.TryParse(d.GetValueOrDefault("count", "0"), out int count)) return;
            _suppressWrites = true;
            try
            {
                RunwayIdCombo.Items.Clear();
                for (int i = 0; i < count; i++)
                {
                    string v = d.GetValueOrDefault($"option_{i}_value", "");
                    if (string.IsNullOrWhiteSpace(v))
                        v = d.GetValueOrDefault($"option_{i}_text", "");
                    if (!string.IsNullOrWhiteSpace(v))
                        RunwayIdCombo.Items.Add(v);
                }
                string selected = d.GetValueOrDefault("selected_text", "");
                if (!string.IsNullOrEmpty(selected))
                    SelectComboByText(RunwayIdCombo, selected);
                Announcer.Announce($"{count} runways loaded");
            }
            finally
            {
                _suppressWrites = false;
            }
        }

        /// <summary>
        /// Match the combo selection to the tablet's currently-rendered unit
        /// label so the user's local combo always reflects reality after a
        /// refresh. Tolerant of case and plural (kts/kt, lbs/lb, HPA/hPa).
        /// </summary>
        protected static void SyncComboToTabletUnit(ComboBox combo, string tabletUnit)
        {
            if (string.IsNullOrWhiteSpace(tabletUnit)) return;
            string target = Normalize(tabletUnit);
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string item = Normalize(combo.Items[i]?.ToString() ?? "");
                if (item == target)
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }

        protected static string Normalize(string s)
        {
            string n = (s ?? "").Trim().ToLowerInvariant();
            if (n.Length > 1 && n[n.Length - 1] == 's') n = n.Substring(0, n.Length - 1);
            return n;
        }

        protected static void SetTextIfChanged(TextBox box, string value)
        {
            if (box.Text != value) box.Text = value;
        }

        protected static void SelectComboByText(ComboBox combo, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }

        // ---------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _runwayDebounce?.Stop();
                _runwayDebounce?.Dispose();
                _runwayDebounce = null;
            }
            base.Dispose(disposing);
        }
    }
}
