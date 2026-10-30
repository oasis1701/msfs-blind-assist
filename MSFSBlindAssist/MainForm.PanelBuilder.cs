using System.Collections.Concurrent;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.FenixA320;
using MSFSBlindAssist.Forms.PMDG737;
using MSFSBlindAssist.Forms.PMDG777;
using MSFSBlindAssist.Forms.HS787;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Patching;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist;

public partial class MainForm
{
    /// <summary>
    /// Converts a decimal squawk code to BCD (Binary Coded Decimal) format for XPNDR_SET event.
    /// Each digit of the squawk code is encoded in 4 bits.
    /// Example: 0422 -> (0*4096) + (4*256) + (2*16) + (2*1) = 1058
    /// </summary>
    private uint ConvertSquawkToBCD(string squawkCode, out string? errorMessage)
    {
        errorMessage = null;

        // Validate length
        if (squawkCode.Length != 4)
        {
            errorMessage = "Squawk code must be exactly 4 digits";
            return 0;
        }

        // Validate each digit (must be 0-7 for transponder codes)
        foreach (char c in squawkCode)
        {
            if (!char.IsDigit(c))
            {
                errorMessage = "Squawk code must contain only digits";
                return 0;
            }
            int digit = c - '0';
            if (digit > 7)
            {
                errorMessage = "Squawk code digits must be 0-7 only";
                return 0;
            }
        }

        // Convert to BCD format
        uint bcd = 0;
        for (int i = 0; i < 4; i++)
        {
            int digit = squawkCode[i] - '0';
            bcd += (uint)(digit << ((3 - i) * 4)); // Shift each digit by 4 bits per position
        }

        return bcd;
    }

    private void SectionsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (sectionsListBox.SelectedItem == null) return;

        string? newSection = sectionsListBox.SelectedItem.ToString();
        if (newSection == null || newSection == currentSection) return;

        currentSection = newSection;
        
        // Clear panels without triggering events
        panelsListBox.SelectedIndexChanged -= PanelsListBox_SelectedIndexChanged;
        panelsListBox.Items.Clear();
        currentPanel = "";
        
        // Once per section change: the Fenix rebuilds its structure on every call and checks every control's
        // variant to hide its empty panels.
        if (currentAircraft.GetPanelStructure().TryGetValue(currentSection, out var sectionPanels))
        {
            foreach (var panel in sectionPanels)
            {
                panelsListBox.Items.Add(panel);
            }
        }

        // Re-enable event
        panelsListBox.SelectedIndexChanged += PanelsListBox_SelectedIndexChanged;
    }

    // The Fenix reads its variant a few seconds into a flight, possibly after the pilot opened a section or a
    // panel. Its panels list and the open panel's controls then follow the variant (FNX-11) without waiting
    // for the pilot to change section. VariantChanged comes in bursts as the config values arrive one by one,
    // so the refresh waits for a quiet second.
    private const int PanelVariantRefreshQuietMs = 1000;
    private FenixA320Definition? _panelVariantSource;
    private System.Windows.Forms.Timer? _panelVariantTimer;

    // The open panel's controls its last build left out as not fitted (IsPanelControlVisible).
    private readonly HashSet<string> _panelHiddenKeys = new(StringComparer.Ordinal);

    /// <summary>Follows the current aircraft's variant changes, if it has any: called whenever
    /// <c>currentAircraft</c> is set.</summary>
    private void WatchPanelVariant()
    {
        _panelVariantTimer?.Stop();
        if (_panelVariantSource != null) _panelVariantSource.VariantChanged -= OnPanelVariantChanged;
        _panelVariantSource = currentAircraft as FenixA320Definition;
        if (_panelVariantSource != null) _panelVariantSource.VariantChanged += OnPanelVariantChanged;
    }

    // Raised on the UI thread (ProcessSimVarUpdate's).
    private void OnPanelVariantChanged(object? sender, FenixVariant variant)
    {
        if (!ReferenceEquals(sender, currentAircraft)) return;
        if (_panelVariantTimer == null)
        {
            _panelVariantTimer = new System.Windows.Forms.Timer { Interval = PanelVariantRefreshQuietMs };
            _panelVariantTimer.Tick += (_, _) =>
            {
                _panelVariantTimer?.Stop();
                RefreshPanelsForVariant();
            };
        }
        _panelVariantTimer.Stop();
        _panelVariantTimer.Start();
    }

    private void RefreshPanelsForVariant()
    {
        if (string.IsNullOrEmpty(currentSection)) return;

        var panels = currentAircraft.GetPanelStructure().TryGetValue(currentSection, out var list)
            ? list : new List<string>();
        var shown = panelsListBox.Items.Cast<object>().Select(o => o.ToString() ?? "").ToList();
        bool listChanged = !panels.SequenceEqual(shown);
        bool panelOpen = !string.IsNullOrEmpty(currentPanel);
        bool panelGone = listChanged && panelOpen && !panels.Contains(currentPanel);
        bool controlsChanged = panelOpen && !panelGone && OpenPanelFittedControlsChanged();
        if (!listChanged && !controlsChanged) return;

        // Never under the pilot's focus: rebuilding the list they are in loses their place, and clearing the
        // controls they are in drops the focus to nothing. Tried again each second until the focus has moved
        // on; meanwhile a control the variant lacks answers "Not fitted on this aircraft.".
        if ((listChanged && panelsListBox.ContainsFocus)
            || ((panelGone || controlsChanged) && controlsContainer.ContainsFocus))
        {
            _panelVariantTimer?.Start();
            return;
        }

        // The panels list, keeping the selected panel. Events are off, so no panel load or debounce starts
        // from here.
        if (listChanged)
        {
            Log.Debug("MainForm", $"Fenix variant changed the '{currentSection}' panels list");
            panelsListBox.SelectedIndexChanged -= PanelsListBox_SelectedIndexChanged;
            try
            {
                panelsListBox.BeginUpdate();
                panelsListBox.Items.Clear();
                foreach (var panel in panels) panelsListBox.Items.Add(panel);
                panelsListBox.EndUpdate();
                if (!string.IsNullOrEmpty(currentPanel))
                {
                    int index = panels.IndexOf(currentPanel);
                    if (index >= 0)
                    {
                        panelsListBox.SelectedIndex = index;
                    }
                    else
                    {
                        // The open panel is one the variant lacks: nothing of it stays on screen.
                        currentPanel = "";
                        _panelHiddenKeys.Clear();
                        controlsContainer.Controls.Clear();
                        currentControls.Clear();
                        stateDependents.Clear();
                        return;
                    }
                }
            }
            finally
            {
                panelsListBox.SelectedIndexChanged += PanelsListBox_SelectedIndexChanged;
            }
        }

        // The open panel, rebuilt when the variant changed which of its controls are fitted.
        if (!controlsChanged) return;
        Log.Debug("MainForm", $"Fenix variant changed the '{currentPanel}' panel's controls; rebuilding it");
        _pendingPanelLoad = currentPanel;
        PanelLoadTimer_Tick(null, EventArgs.Empty);
    }

    // True when the open panel's last build left out a different set of controls than the variant now hides.
    private bool OpenPanelFittedControlsChanged()
    {
        if (currentAircraft is not BaseAircraftDefinition baseDef) return false;
        if (!currentAircraft.GetPanelControls().TryGetValue(currentPanel, out var keys)) return false;
        var variables = currentAircraft.GetVariables();
        var hidden = keys.Where(k => variables.ContainsKey(k) && !baseDef.IsPanelControlVisible(k, simConnectManager));
        return !_panelHiddenKeys.SetEquals(hidden);
    }

    private void PanelsListBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (panelsListBox.SelectedItem == null) return;

        string? newPanel = panelsListBox.SelectedItem.ToString();
        if (newPanel == null || newPanel == currentPanel) return;

        // Update currentPanel IMMEDIATELY so screen reader sees it
        // This allows NVDA to announce the panel name instantly
        currentPanel = newPanel;

        // DEBOUNCE MECHANISM: Don't load panel immediately during rapid arrow navigation
        //
        // PROBLEM: When user rapidly arrows through panels, each SelectedIndexChanged
        // would queue expensive operations (variable requests, control creation), causing:
        // - NVDA to get overwhelmed and announce "panel list view list"
        // - Operation queue buildup leading to lag/silence
        //
        // SOLUTION: Use timer-based debouncing:
        // - Stop any pending timer (cancel previous panel load)
        // - Store which panel to load
        // - Start fresh timer (150ms)
        // - Only load panel when timer fires (user stopped arrowing)
        //
        // RESULT: User can rapidly arrow through 20 panels hearing each name instantly,
        // then only the FINAL selection loads its controls.

        if (_panelLoadTimer != null)
        {
            _panelLoadTimer.Stop(); // Cancel any pending load
            _pendingPanelLoad = newPanel; // Remember which panel to load
            _panelLoadTimer.Start(); // Start fresh timer
            Log.Debug("MainForm", $"Debouncing load for '{newPanel}' panel");
        }
    }

    /// <summary>
    /// Reverse index for composed-state controls: dependency key → control keys whose label
    /// must be recomputed through IAircraftDefinition.TryDescribeControlState when it updates.
    /// Rebuilt once per panel build from each control's SimVarDefinition.StateVariables; a control
    /// that lists any dependency is also a dependent of its own key.
    /// </summary>
    private readonly Dictionary<string, List<string>> stateDependents = new(StringComparer.Ordinal);

    /// <summary>Sentinel dependent standing for the current panel's Status Display list as a whole.</summary>
    private const string DisplayDependent = "_DISPLAY_";

    private void RebuildStateDependents()
    {
        stateDependents.Clear();
        var vars = currentAircraft.GetVariables();
        foreach (var key in currentControls.Keys)
        {
            if (!vars.TryGetValue(key, out var def) || def.StateVariables == null) continue;
            AddStateDependent(key, key);
            foreach (var dep in def.StateVariables) AddStateDependent(dep, key);
        }
        // Status Display rows have dependencies too — the MD-11's lamp rows read the DC-power
        // gate, which is not a row itself — and the list is one control: any dependency change
        // schedules ONE coalesced repaint, which re-reads every row from the cache.
        // The MD-11's lamps also list their OWN key here, and that self-entry is load-bearing:
        // the definition consumes every lamp update in ProcessSimVarUpdate, so OnSimVarUpdated
        // never reaches the ordinary display-var repaint — without the self-entry a lamp row
        // would lag by up to the 1 s tick.
        if (GetPanelDisplayVarsCached().TryGetValue(currentPanel, out var displayVars))
        {
            foreach (var key in displayVars)
            {
                if (!vars.TryGetValue(key, out var def) || def.StateVariables == null) continue;
                foreach (var dep in def.StateVariables) AddStateDependent(dep, DisplayDependent);
            }
        }
    }

    private void AddStateDependent(string source, string dependent)
    {
        if (!stateDependents.TryGetValue(source, out var list))
            stateDependents[source] = list = new List<string>();
        if (!list.Contains(dependent)) list.Add(dependent);
    }

    /// <summary>Relabels every control whose composed state depends on <paramref name="varKey"/>.</summary>
    private void RelabelStateDependents(string varKey)
    {
        if (!stateDependents.TryGetValue(varKey, out var dependents)) return;
        foreach (var key in dependents) RefreshDescribedState(key);
    }

    /// <summary>
    /// Pure label refresh — no user-action handler can fire from a Button.Text or a read-only
    /// TextBox.Text assignment, so this is safe on the def-handled path too.
    /// </summary>
    private void RefreshDescribedState(string key)
    {
        if (key == DisplayDependent)
        {
            // The list repaint re-reads every row from the cache (UpdateDisplayText), so a
            // dependency change needs nothing more than the repaint it already coalesces.
            ScheduleDisplayRepaint();
            return;
        }
        if (!currentControls.TryGetValue(key, out var control)) return;
        // The type BEFORE composing: composing is not free (the MD-11 composes a control's legend
        // lamps, its latch and the DC gate), and this runs for EVERY dependent of an update — on the
        // MD-11 every stateful row of the open panel whenever the DC gate moves. A control that
        // cannot show a composed state must not pay for one.
        if (!ShowsDescribedState(control)) return;
        if (!currentAircraft.GetVariables().TryGetValue(key, out var def)) return;
        if (!currentAircraft.TryDescribeControlState(key, out var state)) return;

        switch (control)
        {
            case Button btn:
            {
                string label = $"{def.DisplayName}: {state}";
                if (btn.Text != label) { btn.Text = label; btn.AccessibleName = label; }
                break;
            }
            case TextBox tb:                  // read-only: ShowsDescribedState admitted no other TextBox
                if (tb.Text != state) tb.Text = state;
                break;
        }
    }

    /// <summary>
    /// The controls a composed state can be shown on: a Button (its label) or a read-only TextBox
    /// (its text). Anything else — an entry box, a combo, a slider — shows its own value, so
    /// <see cref="RefreshDescribedState"/> composes nothing for it. Pinned by DescribedStateTargetTests.
    /// </summary>
    internal static bool ShowsDescribedState(Control control) => control is Button or TextBox { ReadOnly: true };

    /// <summary>
    /// Timer callback: Load panel controls after debounce delay.
    /// Only called when user stops arrowing through panels.
    /// </summary>
    private void PanelLoadTimer_Tick(object? sender, EventArgs e)
    {
        if (_panelLoadTimer != null)
        {
            _panelLoadTimer.Stop(); // Stop timer
        }

        string? panelToLoad = _pendingPanelLoad;
        if (panelToLoad == null) return;

        _pendingPanelLoad = null;

        // Now do the actual heavy work (deferred to UI thread to avoid blocking)
        BeginInvoke(new Action(() =>
        {
            Log.Debug("MainForm", $"Loading controls and requesting variables for '{panelToLoad}' panel");

            // Gate all combo selection-change handlers off for the duration of this build.
            // Also schedule a post-build clear so that any deferred SIC events that WinForms
            // queues during handle creation (which run after this method returns, on the
            // message loop) still see the flag set.
            _buildingPanel = true;

            // Request variables first
            if (simConnectManager != null && simConnectManager.IsConnected)
            {
                simConnectManager.RequestPanelVariables(panelToLoad, $"{panelToLoad} panel opened");

                // Also request StateVariable LVars for buttons in this panel
                var panelControls = currentAircraft.GetPanelControls();
                var variables = currentAircraft.GetVariables();
                if (panelControls.ContainsKey(panelToLoad))
                {
                    foreach (string varKey in panelControls[panelToLoad])
                    {
                        if (!variables.ContainsKey(varKey)) continue;
                        if (!string.IsNullOrEmpty(variables[varKey].StateVariable))
                        {
                            simConnectManager.RequestVariable(variables[varKey].StateVariable!);
                        }
                        // Composed-state dependencies (MD-11 latch vars, lamps, DC gate). Batch-covered
                        // keys no-op inside RequestVariable; OnRequest keys get a fresh read.
                        if (variables[varKey].StateVariables is { } deps)
                        {
                            foreach (var dep in deps) simConnectManager.RequestVariable(dep);
                        }
                    }
                }
            }

            // Clear and reload controls
            controlsContainer.Controls.Clear();
            currentControls.Clear();
            stateDependents.Clear();
            _panelHiddenKeys.Clear();

            if (!currentAircraft.GetPanelControls().ContainsKey(currentPanel))
                return;

        // Create a TableLayoutPanel for better layout
        TableLayoutPanel layout = new TableLayoutPanel();
        layout.ColumnCount = 2;
        layout.RowCount = 0;
        // Column 0 sizes to its longest label, capped at labelColumnMaxWidth. It was a hard
        // 150px against a fixed 140px label with no ellipsis and no tooltip, so any longer
        // name was silently cut - the PMDG 777's "Forward Outflow Valve Manual Selector"
        // lost the very word that distinguishes it from "Forward Outflow Valve Mode". The
        // cap matters too: controlsContainer is a fixed 440px AutoScroll panel that is never
        // resized, so an unbounded label column pushes the 250px control column past its
        // right edge at 100% display scaling (10 + 140 + margins 15 + 250 fits; a 38-char
        // label did not). A label longer than the cap WRAPS and its row grows instead.
        const int labelColumnMaxWidth = 140;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        layout.AutoSize = true;
        layout.Location = new Point(10, 10);
        // PERF: build the whole panel with layout suspended. This TableLayoutPanel
        // is AutoSize, so every Controls.Add otherwise forces a full re-layout — an
        // O(N^2) thrash that lagged large panels (the A380 overhead has dozens of
        // controls). Suspend now, resume once after all rows are added (below).
        layout.SuspendLayout();

        foreach (var varKey in currentAircraft.GetPanelControls()[currentPanel])
        {
            if (!currentAircraft.GetVariables().ContainsKey(varKey))
                continue;

            // Variant-aware visibility: skip controls the running airframe doesn't
            // have (e.g. PMDG 777 freighter cargo temp knobs on a passenger variant).
            // Re-evaluated on every panel build, so late variant detection corrects
            // itself the next time the panel is opened.
            if (currentAircraft is BaseAircraftDefinition baseDef &&
                !baseDef.IsPanelControlVisible(varKey, simConnectManager))
            {
                _panelHiddenKeys.Add(varKey);
                continue;
            }

            var varDef = currentAircraft.GetVariables()[varKey];
            
            // Create label. MaximumSize.Width makes an AutoSize label wrap instead of widening
            // the column (see the ColumnStyle comment above).
            Label label = new Label();
            label.Text = varDef.DisplayName + ":";
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoSize = true;
            label.MaximumSize = new Size(labelColumnMaxWidth, 0);
            label.Anchor = AnchorStyles.Left;
            label.Margin = new Padding(3, 3, 12, 3);

            // Add a new row (sliders need a little more height for the TrackBar). A label that
            // wrapped needs a taller row than the fixed 35/48, or its lower lines are clipped.
            int rowIndex = layout.RowCount++;
            int rowHeight = Math.Max(varDef.RenderAsSlider ? 48 : 35,
                label.GetPreferredSize(new Size(labelColumnMaxWidth, 0)).Height + label.Margin.Vertical);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            layout.Controls.Add(label, 0, rowIndex);

            // Create control based on type.
            // Accessible SLIDER (TrackBar) for continuous axis controls — checked first.
            if (varDef.RenderAsSlider)
            {
                double smin = varDef.SliderMin, smax = varDef.SliderMax;
                double span = (smax - smin) == 0 ? 1 : (smax - smin);
                TrackBar tb = new TrackBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    SmallChange = 1,
                    LargeChange = 10,
                    TickStyle = TickStyle.None,
                    Size = new Size(240, 40),
                    Name = varKey,
                    AccessibleName = varDef.DisplayName
                };
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    int pct = (int)Math.Round((currentSimVarValues[varKey] - smin) / span * 100.0);
                    tb.Value = Math.Max(0, Math.Min(100, pct));
                }
                tb.ValueChanged += (s2, e2) =>
                {
                    if (updatingFromSim) return;   // change came from the sim, don't write back
                    double mapped = smin + (tb.Value / 100.0) * span;
                    bool handled = currentAircraft.HandleUIVariableSet(varKey, mapped, varDef, simConnectManager!, announcer);
                    if (!handled) simConnectManager?.SetLVar(varDef.Name, mapped);
                };
                layout.Controls.Add(tb, 1, rowIndex);
                currentControls[varKey] = tb;
            }
            // Check RenderAsButton next — buttons may have no ValueDescriptions
            else if (varDef.RenderAsButton)
            {
                // Render as button (momentary pushbutton, action button, etc.)
                // If StateVariable is set, show on/off state from the indicator LVar
                string buttonText = varDef.DisplayName;
                if (currentAircraft.TryDescribeControlState(varKey, out string describedState))
                {
                    // Definition-composed state (MD-11 legend lamps + latch + DC gate).
                    buttonText = $"{varDef.DisplayName}: {describedState}";
                }
                else if (!string.IsNullOrEmpty(varDef.StateVariable) && currentSimVarValues.ContainsKey(varDef.StateVariable))
                {
                    double stateVal = currentSimVarValues[varDef.StateVariable];
                    buttonText = $"{varDef.DisplayName}: {varDef.DescribeStateLight(stateVal)}";
                }
                else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 2 && currentSimVarValues.ContainsKey(varKey))
                {
                    // Fallback for non-Fenix buttons that still use ValueDescriptions.
                    // Resting-state (value 0 = Off/Idle) suppression is OPT-IN via
                    // SuppressRestingButtonState, set by the momentary-button defs of the FBW
                    // A320/A380, the iFly 737 (Btn, McpModeStyleWarning) and the MD-11
                    // (BuildControlVariable) — a momentary push-button has no meaningful
                    // resting value, so appending
                    // it read as noise ("Chronometer Start / Stop: Idle, button"). By DEFAULT
                    // the value-0 label shows: PMDG 777 MCP buttons ("LNAV: Off") and the
                    // HS787 Baro STD ("QNH") use value-0 descriptions that ARE meaningful state.
                    double val = currentSimVarValues[varKey];
                    if ((val != 0 || !varDef.SuppressRestingButtonState)
                        && varDef.ValueDescriptions.TryGetValue(val, out string? stateText))
                        buttonText = $"{varDef.DisplayName}: {stateText}";
                }

                Button controlButton = new Button();
                controlButton.Text = buttonText;
                controlButton.Size = new Size(240, 25);
                controlButton.Name = varKey;
                controlButton.AccessibleName = buttonText;

                controlButton.Click += (s2, e2) =>
                {
                    // A button that flips its own light (the Fenix FD and LS): the reader says the new label,
                    // so the light is marked before the press goes out and does not repeat it (CORE-7).
                    MarkStateLightEchoForPress(varDef);
                    bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager!, announcer);
                    if (!handled)
                    {
                        simConnectManager?.SetLVar(varDef.Name, 1);
                        announcer.Announce($"{varDef.DisplayName} pressed");
                    }
                    // Request state variable update after button press
                    if (!string.IsNullOrEmpty(varDef.StateVariable) && simConnectManager != null)
                    {
                        System.Windows.Forms.Timer stateTimer = new System.Windows.Forms.Timer();
                        stateTimer.Interval = 500; // Wait for transition to complete
                        stateTimer.Tick += (ts, te) =>
                        {
                            stateTimer.Stop();
                            stateTimer.Dispose();
                            simConnectManager.RequestVariable(varDef.StateVariable);
                        };
                        stateTimer.Start();
                    }
                };

                layout.Controls.Add(controlButton, 1, rowIndex);
                currentControls[varKey] = controlButton;
            }
            else if (varDef.RenderAsReadOnlyStatus &&
                     (varDef.ValueDescriptions == null || varDef.ValueDescriptions.Count == 0) &&
                     !string.IsNullOrEmpty(varDef.Units) &&
                     !Utils.PanelRowRules.IsReadOnlyStatusRow(varDef))
            {
                // Continuous-numeric read-only TextBox. Used for cockpit gauges
                // exposed by the PMDG NG3 SDK as float fields (cabin altitude,
                // DP, duct pressure, APU EGT, fuel temp, etc.). Text is
                // "{value:Format} {Units}" and is silently refreshed on each
                // continuous broadcast via UpdateControlFromSimVar — the user
                // reads the current value by Tab-focusing the field. A row whose
                // state the definition composes is never one, whatever its count:
                // it is the status box below (Utils.PanelRowRules).
                TextBox readoutBox = new TextBox();
                readoutBox.ReadOnly = true;
                readoutBox.TabStop = true;
                readoutBox.Size = new Size(240, 25);
                readoutBox.Name = varKey;
                readoutBox.AccessibleName = varDef.DisplayName;

                string initial = "—";
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    double cur = currentSimVarValues[varKey] * varDef.Scale + varDef.Offset;
                    // The placeholder unit "number" is never spoken — see Utils.ReadoutFormat.
                    initial = Utils.ReadoutFormat.WithUnit(
                        cur.ToString(varDef.Format, System.Globalization.CultureInfo.InvariantCulture), varDef.Units);
                }
                readoutBox.Text = initial;

                layout.Controls.Add(readoutBox, 1, rowIndex);
                currentControls[varKey] = readoutBox;
            }
            else if (varDef.ValueDescriptions != null && Utils.PanelRowRules.IsReadOnlyStatusRow(varDef))
            {
                // Read-only status field (annunciators, door state, etc.).
                // ValueDescriptions still drive the text; the user can focus the
                // field for the screen reader to read it, but cannot change it.
                // A row whose state the definition composes (StateVariables) is one
                // whatever its description count, and its text is
                // TryDescribeControlState's: the MD-11's Elevator Feel knob has ONE
                // description, and by count alone it fell to the plain Button at the
                // end of this chain, whose click writes 1 into the row's var. Every
                // other row keeps the old rule (Utils.PanelRowRules); the null check
                // is the old condition's own, kept because the body reads the map.
                TextBox statusBox = new TextBox();
                statusBox.ReadOnly = true;
                statusBox.TabStop = true;
                statusBox.Size = new Size(240, 25);
                statusBox.Name = varKey;
                statusBox.AccessibleName = varDef.DisplayName;

                // Seed initial text from cached value, falling back to numeric string
                // and finally to "—" if no value is known yet.
                string initial = "—";
                if (currentAircraft.TryDescribeControlState(varKey, out string describedStatus))
                {
                    initial = describedStatus;
                }
                else if (currentSimVarValues.ContainsKey(varKey))
                {
                    // Through the definition's value→key classifier (identity unless set), like the
                    // two combo sites and the status refresh — a var keyed on positions over a
                    // travel-valued reading must seed with its position, not the raw number.
                    double cur = currentSimVarValues[varKey];
                    initial = varDef.ValueDescriptions.TryGetValue(varDef.DescriptionKeyFor(cur), out string? desc)
                        ? desc
                        : cur.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                statusBox.Text = initial;

                layout.Controls.Add(statusBox, 1, rowIndex);
                currentControls[varKey] = statusBox;
            }
            else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 1)
            {
                // Check if variable should be rendered as button instead of combo box (aircraft-specific)
                // (Legacy path — kept for backward compat with Fenix buttons that have ValueDescriptions)
                if (varDef.RenderAsButton)
                {
                    Button controlButton = new Button();
                    controlButton.Text = varDef.DisplayName;
                    controlButton.Size = new Size(240, 25);
                    controlButton.Name = varKey;
                    controlButton.AccessibleName = varDef.DisplayName;
                    controlButton.AccessibleDescription = varDef.HelpText ?? $"Press {varDef.DisplayName}";

                    // Handle button click - send value 1 which triggers HandleUIVariableSet
                    controlButton.Click += (s2, e2) =>
                    {
                        // Let aircraft handle special cases first (custom button logic, transitions, etc.)
                        bool handled = currentAircraft.HandleUIVariableSet(varKey, 1, varDef, simConnectManager!, announcer);
                        if (handled)
                        {
                            currentSimVarValues[varKey] = 1;
                            // NOTE: We do NOT call RequestRelatedVariables here because:
                            // - Most buttons are independent (pressing one doesn't affect others)
                            // - Variables are refreshed when the panel opens (see PanelsListBox_SelectedIndexChanged)
                            // - Requesting all panel variables after each button press is wasteful
                            return; // Aircraft handled it
                        }

                        // Generic handling if aircraft didn't handle it
                        simConnectManager?.SetLVar(varDef.Name, 1);
                        currentSimVarValues[varKey] = 1;
                        announcer.Announce($"{varDef.DisplayName} pressed");
                        // NOTE: No RequestRelatedVariables - see comment above
                    };

                    layout.Controls.Add(controlButton, 1, rowIndex);
                    currentControls[varKey] = controlButton;
                }
                // Special handling for ENGINE_MODE_SELECTOR
                else if (varKey == "ENGINE_MODE_SELECTOR")
                {
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items
                    combo.Items.Add("CRANK");
                    combo.Items.Add("NORM");
                    combo.Items.Add("IGN");
                    
                    // Set initial value from sim if we have it.
                    // Cache is keyed by the DICT KEY (SimVarUpdated carries VarName = varKey),
                    // not the SimVar Name — on the A380 that key is ENGINE_MODE_SELECTOR.
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = currentSimVarValues[varKey];
                        combo.SelectedIndex = (int)currentValue;
                    }
                    else
                    {
                        combo.SelectedIndex = 1; // Default to NORM
                    }

                    // Handle selection change - set both engines
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            uint mode = (uint)combo.SelectedIndex;
                            // Set both engines to the same mode. The combo reads back the
                            // stock ignition simvar (TURB ENG IGNITION SWITCH EX1:1), which
                            // these events DO move — so unlike the A380's old bug it never
                            // went stale. Also nudge the FBW knob-position L:var so the
                            // cockpit/EWD display matches (the events don't touch it), the
                            // same display-sync the A380 fix added.
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET1", mode);
                            simConnectManager?.SendEvent("TURBINE_IGNITION_SWITCH_SET2", mode);
                            simConnectManager?.ExecuteCalculatorCode($"{mode} (>L:XMLVAR_ENG_MODE_SEL)");
                            // Suppress the echo under the SAME identifier the monitor uses:
                            // SimVarUpdated carries VarName = varKey (the dict key), NOT
                            // varDef.Name — storing under the Name never matched, so the
                            // monitor re-announced every user combo change on the A380.
                            currentSimVarValues[varKey] = mode;
                            MarkUiSet(varKey, mode);
                        }
                    };
                    
                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                }
                // Special handling for Lighting controls
                else if (varKey == "LIGHTING_LANDING_1" || varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3" ||
                         varKey == "LIGHTING_STROBE_0" || varKey == "LIGHT BEACON" || varKey == "LIGHT WING" ||
                         varKey == "LIGHT TAXI:2")
                {
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items in order (reverse if ReverseDisplayOrder is set)
                    var sortedValues = varDef.ReverseDisplayOrder
                        ? varDef.ValueDescriptions.OrderByDescending(x => x.Key).ToList()
                        : varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
                    foreach (var kvp in sortedValues)
                    {
                        combo.Items.Add(kvp.Value);
                    }

                    // Set initial value from sim if we have it
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = currentSimVarValues[varKey];

                        // Debug logging for landing lights
                        if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                        {
                            Log.Debug("MainForm", $"{varKey} received value: {currentValue}");
                        }

                        // One classifier call, as at the sibling site below: DescriptionKeyFor is a
                        // definition-supplied delegate, and this runs per combo row per panel build.
                        double descriptionKey = varDef.DescriptionKeyFor(currentValue);
                        if (varDef.ValueDescriptions.ContainsKey(descriptionKey))
                        {
                            string description = varDef.ValueDescriptions[descriptionKey];
                            combo.SelectedItem = description;

                            // Additional debug for landing lights
                            if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                            {
                                Log.Debug("MainForm", $"{varKey} set to: {description} (value {currentValue})");
                            }
                        }
                        else
                        {
                            // Debug if value doesn't match any description
                            if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                            {
                                Log.Debug("MainForm", $"{varKey} value {currentValue} not found in descriptions!");
                            }
                        }
                    }
                    else
                    {
                        combo.SelectedIndex = 0; // Default to first item (typically "Off")

                        // Debug if no value found
                        if (varKey == "LIGHTING_LANDING_2" || varKey == "LIGHTING_LANDING_3")
                        {
                            Log.Debug("MainForm", $"{varKey} not found in currentSimVarValues, defaulting to index 0");
                        }
                    }

                    // Handle selection change
                    // Capture varKey to avoid nullable reference warnings in closure
                    string capturedVarKey = varKey;
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;
                            // Suppress the echo under the SAME identifier the monitor uses:
                            // SimVarUpdated carries VarName = varKey (the dict key), NOT
                            // varDef.Name. They coincide for plain L:vars but differ for stock
                            // simvars given a clean key (SEATBELT_SIGN -> "CABIN SEATBELTS ALERT
                            // SWITCH"), which is why those combos double-announced.
                            MarkUiSet(capturedVarKey, selectedValue);

                            // Capture the ACTUAL current cached state BEFORE the lines below
                            // overwrite currentSimVarValues with the new selection. The
                            // circuit-toggle branches (RWY turn-off) need this: ELECTRICAL_CIRCUIT_TOGGLE
                            // is toggle-only, so they must compare desired vs actual. The old code
                            // read currentSimVarValues AFTER the overwrite, so current == selected
                            // always, and the toggle never fired (RWY turn-off appeared dead).
                            double priorCachedState = currentSimVarValues.ContainsKey(capturedVarKey)
                                ? currentSimVarValues[capturedVarKey] : -1;

                            // Let the aircraft handle this SimVar-backed combo first
                            // (e.g. an A380 valve/exit combo whose STATE is a SimVar
                            // but whose CONTROL is a K-event — engine masters,
                            // crossfeed, doors, jetway). Mirrors the LVar-combo path.
                            if (currentAircraft.HandleUIVariableSet(capturedVarKey, selectedValue, varDef, simConnectManager!, announcer))
                            {
                                currentSimVarValues[capturedVarKey] = selectedValue;
                                return;
                            }

                            // NOTE (2026-07-12): for the FBW A32NX, every exterior-light key in this
                            // branch is now handled ABOVE by FlyByWireA320Definition.HandleUIVariableSet
                            // (the single source of truth, shared with the First Officer executor), so
                            // the fallthrough mechanisms below no longer run for the A32NX. They remain
                            // only as a generic fallback for any aircraft whose def does NOT claim these
                            // keys. If you change a light's actuator, edit HandleUIVariableSet, not here.

                            // Send the main LVar
                            simConnectManager?.SetLVar(capturedVarKey, selectedValue);
                            currentSimVarValues[capturedVarKey] = selectedValue;

                            // Landing/nose lights (CORRECTED 2026-07 — the old comment claimed the
                            // Asobo template polls LIGHTING_LANDING_x and manages the circuits; it
                            // does NOT on the current FBW build. Live-verified: an L:var write holds
                            // (nose) or reverts (wing, template-derived) and the lamp never changes.
                            // The working actuator is the indexed stock event, sent verbatim in the
                            // FBW template's own RPN form "<value> <index> r (>K:2:...)". NOTE: RPN
                            // "r" swaps the top two stack entries (SDK-documented), so this is
                            // stack-EQUIVALENT to "<index> <value>" — an earlier live test blamed
                            // the index-first form for a no-op, but the forms cannot differ; that
                            // failure had some other cause. Keep the template-verbatim form.
                            if (capturedVarKey == "LIGHTING_LANDING_1") // Nose Light (T.O.=0/Taxi=1/Off=2)
                            {
                                // L:var mirror already written by the generic SetLVar above (it holds
                                // — the nose switch position var is not template-derived). Drive the
                                // lamps: nose takeoff = LIGHT LANDING:1, nose taxi = LIGHT TAXI:1;
                                // TAXI stays on in T.O. ("allow TAXI LT with TO LT", per the
                                // SWITCH_OVHD_EXTLT_NOSE template).
                                int pos = (int)Math.Round((double)selectedValue);
                                int takeoff = pos == 0 ? 1 : 0;
                                int taxi = (pos == 0 || pos == 1) ? 1 : 0;
                                simConnectManager?.ExecuteCalculatorCode(
                                    $"{takeoff} 1 r (>K:2:LANDING_LIGHTS_SET) {taxi} 1 r (>K:2:TAXI_LIGHTS_SET)");
                            }
                            else if (capturedVarKey == "LIGHTING_LANDING_2" || capturedVarKey == "LIGHTING_LANDING_3")
                            {
                                // L/R Landing Light, 0=On/1=Off/2=Retract. LIGHTING_LANDING_2/3 is
                                // DERIVED per-frame by the retractable-switch template from the lamp
                                // + retract state (an external write reverts — live-verified), so the
                                // SetLVar above is a no-op for it. Drive the lamp via the indexed
                                // stock event (LIGHT LANDING:2 = left, :3 = right) + the retract
                                // animation L:var; the template then converges the position var by
                                // itself (live-verified: event + retract write → L:var followed).
                                int idx = capturedVarKey == "LIGHTING_LANDING_2" ? 2 : 3;
                                int on = selectedValue == 0 ? 1 : 0;
                                simConnectManager?.SetLVar($"LANDING_{idx}_RETRACTED", selectedValue == 2 ? 1 : 0);
                                simConnectManager?.ExecuteCalculatorCode($"{on} {idx} r (>K:2:LANDING_LIGHTS_SET)");
                            }
                            else if (capturedVarKey == "LIGHTING_STROBE_0") // Strobe Lights
                            {
                                if (selectedValue == 2) // Off
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SendEvent("STROBES_OFF", 0);
                                }
                                else if (selectedValue == 0) // On
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 0);
                                    simConnectManager?.SendEvent("STROBES_ON", 0);
                                }
                                else // Auto (1)
                                {
                                    simConnectManager?.SetLVar("STROBE_0_AUTO", 1);
                                    // STROBE_0_AUTO=1 is sufficient — FBW FMGC manages strobe state
                                }
                            }
                            else if (capturedVarKey == "LIGHT BEACON") // Beacon Light
                            {
                                simConnectManager?.SendEvent("BEACON_LIGHTS_SET", (uint)selectedValue);
                            }
                            else if (capturedVarKey == "LIGHT WING") // Wing Lights
                            {
                                simConnectManager?.SendEvent("WING_LIGHTS_SET", (uint)selectedValue);
                            }
                            else if (capturedVarKey == "LIGHT TAXI:2") // Runway Turn Off Lights (single switch -> both sides)
                            {
                                // The real A320 has ONE RWY TURN OFF switch (SWITCH_OVHD_EXTLT_RWY)
                                // driving BOTH lights (SIMVAR_INDEX_1=2 / SIMVAR_INDEX_2=3,
                                // TOGGLE_EVENT=TAXI_LIGHTS_SET) so LIGHT TAXI:2/3, FBW presets, and
                                // the EFB all stay in sync. The old ELECTRICAL_CIRCUIT_TOGGLE path
                                // drove circuits 21/22 directly, which desynchronised LIGHT TAXI:2/3.
                                // RPN form 2026-07: verbatim the nose-light form in FBW's
                                // A320_NEO_INTERIOR.xml, "<value> <index> r (>K:2:TAXI_LIGHTS_SET)" — live-verified to set
                                // exactly the right index. (RPN "r" swaps the top two stack entries,
                                // so this is stack-equivalent to "<index> <value>"; an earlier test
                                // that read the index-first form as a no-op had some other confound
                                // — FBW's own Airbus.xml switch template uses index-first, no r.)
                                int on = selectedValue == 1 ? 1 : 0;
                                simConnectManager?.ExecuteCalculatorCode($"{on} 2 r (>K:2:TAXI_LIGHTS_SET)");
                                simConnectManager?.ExecuteCalculatorCode($"{on} 3 r (>K:2:TAXI_LIGHTS_SET)");
                                // Refresh both LIGHT TAXI state vars so an external (cockpit) change
                                // can't leave the next read stale.
                                simConnectManager?.RequestVariable("LIGHT TAXI:2", forceUpdate: true);
                                simConnectManager?.RequestVariable("LIGHT TAXI:3", forceUpdate: true);
                            }
                        }
                    };

                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                }
                else
                {
                    // Normal ComboBox for other multi-value controls
                    ComboBox combo = new ComboBox();
                    combo.DropDownStyle = ComboBoxStyle.DropDownList;
                    combo.Size = new Size(240, 25);
                    combo.Name = varKey;
                    combo.AccessibleName = varDef.DisplayName;

                    // Add items in order (reverse if ReverseDisplayOrder is set).
                    // Each distinct label is added ONCE: a composite-encoded field
                    // (e.g. the iFly ACP receiver switches, whose raw 0-5 value folds
                    // a redundant lamp state onto a 2-position switch) collapses to
                    // fewer combo positions by giving several keys the same label,
                    // while every raw key still resolves for the read-back paths.
                    var sortedValues = varDef.ReverseDisplayOrder
                        ? varDef.ValueDescriptions.OrderByDescending(x => x.Key).ToList()
                        : varDef.ValueDescriptions.OrderBy(x => x.Key).ToList();
                    foreach (var kvp in sortedValues)
                    {
                        if (!combo.Items.Contains(kvp.Value))
                            combo.Items.Add(kvp.Value);
                    }
                    
                    // Set initial value from sim if we have it. The lookup goes through the
                    // definition's value→key classifier (identity unless set): a var whose value
                    // is a travel but whose keys are positions (the MD-11 gear lever, 0-25 against
                    // {0 Up, 1 Down}) would otherwise never match and the combo would open blank.
                    if (currentSimVarValues.ContainsKey(varKey))
                    {
                        double currentValue = varDef.DescriptionKeyFor(currentSimVarValues[varKey]);
                        if (varDef.ValueDescriptions.ContainsKey(currentValue))
                        {
                            string description = varDef.ValueDescriptions[currentValue];
                            int index = combo.Items.IndexOf(description);
                            if (index >= 0)
                            {
                                combo.SelectedIndex = index;
                            }
                        }
                    }
                    else if (combo.Items.Count > 0)
                    {
                        combo.SelectedIndex = 0; // Default to first item
                    }

                    // Handle selection change
                    // SelectionChangeCommitted fires only on user-initiated changes (mouse click,
                    // arrow key commit, Enter). SelectedIndexChanged ALSO fires on programmatic
                    // assignment AND on the deferred replay that happens when the combo is
                    // parented and its native handle is created — which was firing phantom user-
                    // action writes during panel build, toggling state-sensing SimVars (battery,
                    // generator, ext-pwr, avionics master) and cascading the WT 787 electrical bus.
                    combo.SelectionChangeCommitted += (s2, e2) =>
                    {
                        if (!updatingFromSim && !_buildingPanel && combo.SelectedIndex >= 0)
                        {
                            // Items are per-LABEL (duplicates collapsed at add time), so the
                            // item index cannot index sortedValues; resolve the selected label
                            // to its first key in display order. Identical to the old
                            // positional lookup when every label is unique.
                            string selectedText = (string)combo.SelectedItem!;
                            var selectedValue = sortedValues.First(kv => kv.Value == selectedText).Key;
                            // Echo-suppress under varKey — the monitor's VarName is the dict key,
                            // not varDef.Name (fixes double-announce on key!=Name combos like the
                            // A380 seat-belt sign / "CABIN SEATBELTS ALERT SWITCH").
                            MarkUiSet(varKey, selectedValue);

                            // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                            bool aircraftHandled = currentAircraft.HandleUIVariableSet(varKey, selectedValue, varDef, simConnectManager!, announcer);
                            if (aircraftHandled)
                            {
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: We do NOT call RequestRelatedVariables here because:
                                // - Most combo boxes are independent switches (changing one doesn't affect others)
                                // - Variables are refreshed when the panel opens (see PanelsListBox_SelectedIndexChanged)
                                // - Requesting all panel variables after each combo change is wasteful
                                return; // Aircraft handled it
                            }

                            // Generic handling follows if aircraft didn't handle it
                            if (varDef.Type == SimVarType.PMDGVar)
                            {
                                bool handled = currentAircraft.HandleUIVariableSet(varKey, selectedValue, varDef, simConnectManager!, announcer);
                                if (!handled)
                                    Log.Debug("MainForm", $"Unhandled PMDGVar set: {varKey}");
                                currentSimVarValues[varKey] = selectedValue;
                            }
                            else if (varKey == "CABIN SEATBELTS ALERT SWITCH") // Seat Belts Signs
                            {
                                // Send the toggle event to change the state
                                simConnectManager?.SendEvent("CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", 0);

                                // Update our stored value to match the selection
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: No RequestRelatedVariables - see comment above
                            }
                            else if (varDef.Type == SimVarType.LVar)
                            {
                                simConnectManager?.SetLVar(varDef.Name, selectedValue);
                                currentSimVarValues[varKey] = selectedValue;
                                // NOTE: No RequestRelatedVariables - see comment above
                            }
                        }
                    };
                    
                    layout.Controls.Add(combo, 1, rowIndex);
                    currentControls[varKey] = combo;
                    
                    // Request current value for this control if not automatically monitored by Important tier
                    if (varDef.Type == SimVarType.LVar && varDef.UpdateFrequency != UpdateFrequency.Continuous)
                    {
                        simConnectManager?.RequestVariable(varKey);
                    }
                }
            }
            else if (varKey.Contains("_SET") && !varDef.PreventTextInput)
            {
                // A typed field, applied by Enter (unless aircraft prevents text input). It had a Set
                // button beside it until 2026-10-18; Enter already did the same, so the button only
                // added a stop to the Tab order (the owner's call), and it was removed.
                TextBox textBox = new TextBox();
                textBox.Size = new Size(240, 25);
                textBox.AccessibleName = $"{varDef.DisplayName} value";
                // With no Set button after the field, this is the reader's only cue that Enter applies it.
                textBox.AccessibleDescription = "Type a value and press Enter to set";

                // If this _SET field declares a current-value source, pre-fill it
                // from the cached value on creation AND on focus-in, then select
                // all, so a screen reader reads the current value when the user
                // tabs in and overtyping replaces it. Refreshing only on focus-in
                // (not continuously) avoids clobbering text the user is typing.
                if (!string.IsNullOrEmpty(varDef.CurrentValueSourceKey))
                {
                    string srcKey = varDef.CurrentValueSourceKey;
                    Action seedFromCurrent = () =>
                    {
                        if (currentSimVarValues.TryGetValue(srcKey, out double cur))
                        {
                            textBox.Text = ((int)Math.Round(cur)).ToString(
                                System.Globalization.CultureInfo.InvariantCulture);
                            textBox.SelectAll();
                        }
                    };
                    seedFromCurrent();
                    textBox.GotFocus += (s3, e3) => seedFromCurrent();
                }

                // Enter in the box applies the value, as every dedicated dialog does through
                // AcceptButton. A bare TextBox swallows Enter, so a blind pilot who typed a squawk and
                // pressed Enter heard nothing at all and flew on the old code (found in review
                // 2026-09-07). SuppressKeyPress stops the WinForms ding.
                textBox.KeyDown += (s4, e4) =>
                {
                    if (e4.KeyCode != Keys.Enter) return;
                    e4.SuppressKeyPress = true;
                    e4.Handled = true;
                    ApplyTyped();
                };

                void ApplyTyped()
                {
                    // Aircraft delegation: let the loaded aircraft claim _SET keys
                    // (e.g., PMDG 737's EFIS_MinsValueFt_*_SET vars need RST-then-rotate
                    // dispatch). We pass the double value when parseable, else 0 -- or NaN
                    // for a field that opted in (UnparseableTextAsNaN), where 0 is a real value
                    // (an A32NX FCU heading of 0 is north) and the handler refuses NaN. Every
                    // other field still receives the historical 0: their handlers were written
                    // against it, and several would forward a NaN to the sim. TryParseTyped takes
                    // either decimal separator on any Windows culture and refuses a thousands group
                    // ("1,500" must not become an FPA of 1.5); a COM frequency "118,275" still reads.
                    bool parsed = Utils.NumberParsing.TryParseTyped(textBox.Text, out double value);
                    if (currentAircraft.HandleUIVariableSet(
                            varKey, parsed ? value : varDef.UnparseableTextAsNaN ? double.NaN : 0,
                            varDef, simConnectManager!, announcer))
                    {
                        return;
                    }

                    // Special handling for transponder code (requires BCD encoding)
                    if (varKey == "TRANSPONDER_CODE_SET")
                    {
                        string squawkCode = textBox.Text.Trim();

                        // Pad with leading zeros if needed (e.g., "422" -> "0422")
                        if (squawkCode.Length < 4 && squawkCode.All(char.IsDigit))
                        {
                            squawkCode = squawkCode.PadLeft(4, '0');
                        }

                        uint bcdValue = ConvertSquawkToBCD(squawkCode, out string? errorMessage);

                        if (errorMessage != null)
                        {
                            announcer.Announce(errorMessage);
                        }
                        else
                        {
                            simConnectManager?.SendEvent("XPNDR_SET", bcdValue);
                            announcer.Announce($"Squawk set to {squawkCode}");
                        }
                    }
                    // The aircraft declined the value above; the generic handling uses the same parse.
                    else if (parsed)
                    {
                        // Generic handling follows - COM frequencies, transponder, etc.
                        if (varKey.StartsWith("COM_") && varKey.Contains("FREQUENCY_SET"))
                        {
                            // Validate COM frequency range (118.000 - 136.975 MHz)
                            if (value >= 118.0 && value <= 136.975)
                            {
                                // Convert MHz to Hz (simple multiplication, no BCD16 needed)
                                uint frequencyHz = (uint)Math.Round(value * 1000000);

                                // Determine which COM radio (1, 2, or 3)
                                string comIndex = "1"; // Default to COM1
                                if (varKey.Contains(":2")) comIndex = "2";
                                else if (varKey.Contains(":3")) comIndex = "3";

                                // Always set standby first, then swap if setting active.
                                // The COM1 standby-set event is the un-numbered "COM_STBY_RADIO_SET_HZ";
                                // COM2/COM3 use the numbered form so a COM2 set doesn't write COM1's standby.
                                string setEvent = comIndex == "1" ? "COM_STBY_RADIO_SET_HZ" : $"COM{comIndex}_STBY_RADIO_SET_HZ";
                                string swapEvent = $"COM{comIndex}_RADIO_SWAP";

                                // For active frequency: set standby then swap
                                if (varKey.Contains("ACTIVE"))
                                {
                                    simConnectManager?.SendEvent(setEvent, frequencyHz);
                                    System.Threading.Thread.Sleep(100); // Small delay for sim to process
                                    simConnectManager?.SendEvent(swapEvent);
                                    announcer.Announce($"Active frequency set to {value:F3}");
                                }
                                // For standby frequency: just set it
                                else
                                {
                                    simConnectManager?.SendEvent(setEvent, frequencyHz);
                                    announcer.Announce($"Standby frequency set to {value:F3}");
                                }
                            }
                            else
                            {
                                announcer.Announce($"Invalid COM frequency. Range: 118.000 to 136.975 MHz");
                            }
                        }
                        else if (varKey == "TRANSPONDER_CODE_SET")
                        {
                            // Validate transponder code (0000-7777 in octal)
                            int code = (int)value;
                            if (code >= 0 && code <= 7777)
                            {
                                // Verify each digit is 0-7 (octal)
                                string codeStr = code.ToString("D4");
                                bool valid = true;
                                foreach (char c in codeStr)
                                {
                                    if (c < '0' || c > '7')
                                    {
                                        valid = false;
                                        break;
                                    }
                                }

                                if (valid)
                                {
                                    // Convert to BCD16 format expected by XPNDR_SET
                                    uint bcd = (uint)(
                                        ((code / 1000) << 12) |
                                        (((code / 100) % 10) << 8) |
                                        (((code / 10) % 10) << 4) |
                                        (code % 10)
                                    );
                                    simConnectManager?.SendEvent("XPNDR_SET", bcd);
                                    announcer.AnnounceImmediate($"Transponder code set to {codeStr}");
                                }
                                else
                                {
                                    announcer.Announce("Invalid transponder code. Each digit must be 0-7.");
                                }
                            }
                            else
                            {
                                announcer.Announce("Invalid transponder code. Range: 0000 to 7777.");
                            }
                        }
                        // All A32NX-specific handling (VS/FPA, baro) now done by HandleUIVariableSet
                        else
                        {
                            simConnectManager?.SendEvent(varKey, (uint)value);
                            announcer.Announce($"{varDef.DisplayName} set to {value}");
                        }
                    }
                    else
                    {
                        // A validation error always speaks: "10,000" used to be sent as 10.
                        announcer.Announce("Invalid number format");
                    }
                }

                layout.Controls.Add(textBox, 1, rowIndex);
                currentControls[varKey] = textBox;
            }
            else
            {
                // Button for simple operations
                Button button = new Button();
                button.Text = varDef.DisplayName;
                button.Size = new Size(240, 25);
                button.AccessibleName = varDef.DisplayName;
                
                button.Click += (s2, e2) =>
                {
                    if (varDef.Type == SimVarType.Event)
                    {
                        // Before the send, so an echo armed here can never lose the race to the sim.
                        currentAircraft.OnPanelButtonFiring(varKey);

                        // Special handling for events that don't take parameters
                        if (varDef.Name == "A32NX.AUTOBRAKE_SET_DISARM")
                        {
                            // This event doesn't take any parameters
                            simConnectManager?.SendEvent(varDef.Name, 0);
                        }
                        else
                        {
                            // Use EventParam if it's set, otherwise use 0
                            uint param = varDef.EventParam > 0 ? varDef.EventParam : 0;
                            simConnectManager?.SendEvent(varDef.Name, param);
                        }

                        // Handle button state announcements for all panels
                        HandleButtonStateAnnouncement(varKey);
                        // Aircraft-specific post-press read-out (e.g. FCU push/pull
                        // buttons speak the resulting value like their hotkeys do).
                        currentAircraft.OnPanelButtonFired(varKey, simConnectManager!, announcer);
                    }
                    else if (varDef.Type == SimVarType.HVar)
                    {
                        // Handle H-variable buttons (MobiFlight WASM)
                        if (!string.IsNullOrEmpty(varDef.PressEvent) && !string.IsNullOrEmpty(varDef.ReleaseEvent))
                        {
                            // Automatic press/release sequence
                            simConnectManager?.SendButtonPressRelease(varDef.PressEvent, varDef.ReleaseEvent, varDef.PressReleaseDelay);

                            // Request LED state after button action using existing LVar system
                            if (!string.IsNullOrEmpty(varDef.LedVariable))
                            {
                                System.Windows.Forms.Timer ledCheckTimer = new System.Windows.Forms.Timer();
                                ledCheckTimer.Interval = varDef.PressReleaseDelay + 300; // Wait for press/release + 300ms
                                ledCheckTimer.Tick += (ts, te) =>
                                {
                                    ledCheckTimer.Stop();
                                    ledCheckTimer.Dispose();
                                    // Use existing LVar request system to read LED state
                                    simConnectManager?.RequestVariable(varDef.LedVariable);
                                    Log.Debug("MainForm", $"Requesting LED state: {varDef.LedVariable}");
                                };
                                ledCheckTimer.Start();
                            }
                        }
                        else if (!string.IsNullOrEmpty(varDef.PressEvent))
                        {
                            // Single H-variable execution
                            simConnectManager?.SendHVar(varDef.PressEvent);
                        }

                        Log.Debug("MainForm", $"H-variable button pressed: {varDef.DisplayName}");
                    }
                    else if (varDef.Type == SimVarType.LVar)
                    {
                        // Special handling for clear warning buttons - send 0 to turn off
                        if (varKey == "CLEAR_MASTER_WARNING" || varKey == "CLEAR_MASTER_CAUTION")
                        {
                            simConnectManager?.SetLVar(varDef.Name, 0);
                        }
                        else
                        {
                            simConnectManager?.SetLVar(varDef.Name, 1);

                            // Handle momentary buttons - auto-reset to 0 after a short delay
                            if (varDef.IsMomentary)
                            {
                                System.Windows.Forms.Timer momentaryTimer = new System.Windows.Forms.Timer();
                                momentaryTimer.Interval = 150; // 150ms delay
                                momentaryTimer.Tick += (ts, te) =>
                                {
                                    momentaryTimer.Stop();
                                    momentaryTimer.Dispose();
                                    simConnectManager?.SetLVar(varDef.Name, 0);
                                };
                                momentaryTimer.Start();
                            }
                        }
                    }
                };
                
                layout.Controls.Add(button, 1, rowIndex);
                currentControls[varKey] = button;
            }
        }

        // Add display field if this panel has display variables
        if (GetPanelDisplayVarsCached().ContainsKey(currentPanel))
        {
            // Standard display for other panels
            // Add separator row
            int separatorRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));

            // Add display row
            int displayRow = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));

            Label displayLabel = new Label();
            displayLabel.Text = "Status Display:";
            displayLabel.TextAlign = ContentAlignment.TopLeft;
            displayLabel.AutoSize = false;
            displayLabel.Size = new Size(140, 25);
            layout.Controls.Add(displayLabel, 0, displayRow);

            // Panel to hold the status list
            Panel displayPanel = new Panel();
            displayPanel.Size = new Size(240, 150);

            // Read-only NAVIGABLE LIST for the status display — one row per item. A live
            // refresh updates ONLY the items whose value changed, and a ListBox item-text
            // update never touches a caret or selection, so the screen-reader cursor stays
            // on the row the user is reading. (A multiline TextBox couldn't do this: every
            // in-place edit has to move the selection to replace text, and NVDA follows
            // those caret events, throwing the review cursor around.) Arrow up/down reads
            // each row; only a row whose value actually changes re-announces while focused.
            //
            // No Refresh button: the list updates itself every second (SdAutoRefreshTimer_Tick),
            // and the button and the "(F5 to refresh now)" in this name were from before it did.
            // F5 still refreshes at once from anywhere in the panel, the list included
            // (ProcessCmdKey -> RefreshDisplayNow), and the hotkey guides list it.
            ListBox displayList = new ListBox();
            // The panel this list shows: currentPanel moves as soon as the panel selection does,
            // while the debounced load still shows this list (RefreshDisplayNow checks it).
            displayList.Tag = currentPanel;
            displayList.Size = new Size(240, 150);
            displayList.Location = new Point(0, 0);
            displayList.IntegralHeight = false;
            displayList.SelectionMode = SelectionMode.One;
            displayList.HorizontalScrollbar = true;
            displayList.TabStop = true;
            displayList.AccessibleName = "Status display, updates live";

            // When the user moves focus TO the list, pull current content. The live auto-refresh
            // timer keeps it current while focused too (updating only the changed rows, so the
            // cursor stays put), but it skips while a SELECTOR COMBO is focused so it can't fight
            // the combo's announcement — so this GotFocus pull is what brings the list current the
            // instant the user moves from the page combo onto it to read.
            displayList.GotFocus += (s2, e2) => RebuildDisplaySnapshot("GotFocus");

            displayPanel.Controls.Add(displayList);
            layout.Controls.Add(displayPanel, 1, displayRow);

            // Store reference to the display list: the live auto-refresh runs while it exists, and
            // F5 in ProcessCmdKey refreshes it from anywhere in the panel.
            currentControls["_DISPLAY_"] = displayList;
        }

            RebuildStateDependents();

            // Resume + lay out ONCE now that every row exists, then attach.
            layout.ResumeLayout(true);
            controlsContainer.SuspendLayout();
            controlsContainer.Controls.Add(layout);
            controlsContainer.ResumeLayout(true);

            // Auto-populate a multi-page status box (e.g. the SD-page combo) with the
            // combo's CURRENT page, so the user doesn't have to cycle it to get content
            // on first display. No-op for panels without such a box.
            RebuildDisplaySnapshot("initial show");

            // Start (or stop) the live status-box auto-refresh for THIS panel. Only panels
            // that actually built a status display (have a "_DISPLAY_" list) get the timer;
            // everything else stops it so we don't poll in the background on a static panel.
            StartOrStopSdAutoRefresh();

            // For PMDG aircraft, populate controls with current data from the data manager
            if (currentAircraft is IPMDGAircraft && simConnectManager?.PMDGDataManager != null)
            {
                var dm = simConnectManager.PMDGDataManager;
                foreach (var varKey in currentAircraft.GetPanelControls()[currentPanel])
                {
                    if (!currentAircraft.GetVariables().ContainsKey(varKey)) continue;
                    var varDef = currentAircraft.GetVariables()[varKey];

                    // Only PMDG broadcast fields carry data in the data manager. Reading any
                    // other type (LVar/SimVar/Event) returns GetFieldValue's 0.0 "unknown
                    // field" sentinel, which force-resets the control (e.g. the System Display
                    // page combo to page 0) and logs spurious "unknown field" lines. Non-PMDG
                    // controls are populated via combo-creation + continuous monitoring instead.
                    if (varDef.Type != SimVarType.PMDGVar) continue;

                    // Read current value from PMDG data manager using the struct field name
                    double value = dm.GetFieldValue(varDef.Name);
                    currentSimVarValues[varKey] = value;

                    // Update the UI control
                    UpdateControlFromSimVar(varKey, value);
                }
            }
            // Note: a previous attempt to "force-refresh" all panel variables here caused
            // duplicate-announce oscillation (on, then off) for HS787 vars whose
            // ProcessSimVarUpdate handler announces on transitions. Reverted; rely on the
            // initial-value read at combo creation (line 4297-4314) plus continuous
            // monitoring to keep combo state in sync with the sim.
            // Clear the flag asynchronously so any handle-creation-replay SIC events that
            // got queued while we built controls also see _buildingPanel = true. 200 ms is
            // generous; the actual replay window is sub-frame on a modern machine.
            var clearTimer = new System.Windows.Forms.Timer { Interval = 200 };
            clearTimer.Tick += (_, __) =>
            {
                clearTimer.Stop();
                clearTimer.Dispose();
                _buildingPanel = false;
            };
            clearTimer.Start();
        })); // End BeginInvoke - deferred control creation
    } // End PanelLoadTimer_Tick

    /// <summary>
    /// Rebuilds any aircraft-managed SNAPSHOT content of the shown panel (the A380/A32NX/PMDG
    /// SD-page box). That content lives in the definition and is ONLY regenerated by
    /// OnDisplayPanelShown, which re-reads the underlying SimVars (FOB, engine N1-N3, per-tank
    /// fuel, …); the display-var reads render it but never rebuild it, so without this a refresh
    /// re-printed the SAME stale snapshot. Fire-and-forget: the definition pushes its own repaint
    /// when its fresh read completes. A throw is logged: it would otherwise leave the snapshot
    /// stale with no clue why, a known confusing-bug shape here (values that "never move").
    /// </summary>
    private void RebuildDisplaySnapshot(string reason)
    {
        if (currentAircraft == null || simConnectManager == null) return;
        try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager); }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"OnDisplayPanelShown ({reason}) failed for panel '{currentPanel}': {ex.Message}");
        }
    }

    /// <summary>
    /// Brings the shown panel's status display current: rebuilds any SD-page snapshot
    /// (unless <paramref name="rebuildSnapshot"/> is false because the caller just did),
    /// force-reads the panel's display vars and schedules ONE coalesced repaint. The 1 s tick
    /// runs it (<see cref="SdAutoRefreshTimer_Tick"/>), and so do F5 anywhere in the panel and
    /// Ctrl+3 onto a list that is still empty. It never clears <c>displayValues</c>: the repaint
    /// reads the cache first ([SIM-15]), and for a row the cache never holds (the iFly's
    /// SDK-fed rows) the stored value is the only one there is. Focus never moves. Returns false,
    /// doing nothing, while <paramref name="displayList"/> is not the shown panel's list yet.
    /// On the UI thread.
    /// </summary>
    private bool RefreshDisplayNow(ListBox displayList, string reason, bool rebuildSnapshot = true)
    {
        // The panel selection moves currentPanel at once; the debounced load still shows the
        // previous panel's list, which must not be filled with the next panel's rows.
        if (!Equals(displayList.Tag, currentPanel)) return false;

        // First populate only (empty list): show a Loading placeholder so it isn't silent.
        if (displayList.Items.Count == 0)
            displayList.Items.Add("Loading...");

        if (rebuildSnapshot) RebuildDisplaySnapshot(reason);

        // Force-read the panel's own display vars so the cache is fresh (covers the non-override
        // panels whose display vars ARE the content). forceUpdate=true bypasses the suppression
        // that drops SimVarUpdated for an unchanged announced var. Each response pushes through
        // OnSimVarUpdated, which schedules a COALESCED repaint, so the burst collapses into ONE
        // list rebuild; scheduling once here repaints even when no value changed.
        if (simConnectManager != null && GetPanelDisplayVarsCached().TryGetValue(currentPanel, out var displayVars))
        {
            var allVars = currentAircraft.GetVariables();
            foreach (var varKey in displayVars)
                if (allVars.ContainsKey(varKey))
                    simConnectManager.RequestVariable(varKey, forceUpdate: true);
        }

        ScheduleDisplayRepaint();
        return true;
    }

    // Starts the live status-box auto-refresh when the currently-shown panel has a status
    // display ("_DISPLAY_" list); stops it otherwise. Called every time a panel is shown.
    private void StartOrStopSdAutoRefresh()
    {
        bool hasDisplay = currentControls != null && currentControls.ContainsKey("_DISPLAY_");
        if (!hasDisplay)
        {
            _sdAutoRefreshTimer?.Stop();
            _displayRepaintDebounce?.Stop();   // no pending repaint into a panel that's gone
            return;
        }
        if (_sdAutoRefreshTimer == null)
        {
            // 1s for live monitoring. The tick force-reads the page vars and schedules a single
            // coalesced repaint (no TaskCompletionSource/2s-timeout dance), so ticks can't stack
            // and a changed value surfaces within ~one read round-trip instead of several seconds.
            _sdAutoRefreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _sdAutoRefreshTimer.Tick += SdAutoRefreshTimer_Tick;
        }
        _sdAutoRefreshTimer.Stop();
        _sdAutoRefreshTimer.Start();
    }

    private void SdAutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Panel changed out from under us (or app tearing down) → stop polling.
            if (currentAircraft == null || string.IsNullOrEmpty(currentPanel) ||
                currentControls == null || !currentControls.TryGetValue("_DISPLAY_", out var dc) ||
                dc is not ListBox displayList)
            {
                _sdAutoRefreshTimer?.Stop();
                return;
            }
            if (simConnectManager == null || !simConnectManager.IsConnected) return;

            // We refresh while the user is reading the status LIST now — full live monitoring.
            // The reconcile (UpdateDisplayText -> Forms.DisplayList.UpdateInPlace) rewrites only
            // the rows whose value actually changed and never disturbs the selection, so the
            // screen-reader cursor stays on the row being read; a stable value is never
            // re-touched (so it never re-announces).

            // Skip ONLY while the user is on a SELECTOR COMBO in this panel (e.g. the SD page
            // picker). The refresh re-requests the page var — UpdateControlFromSimVar can then
            // re-set the combo's SelectedIndex to a lagging value, fighting the user's arrowing —
            // which steps on NVDA's page-selection announcement. The list is brought current when
            // the user moves focus TO it (the list's GotFocus refresh).
            foreach (var kv in currentControls)
                if (kv.Value is ComboBox cb && cb.IsHandleCreated && cb.Focused)
                    return;

            // Rebuild any snapshot SD-page content (FOB, engine, fuel, control surfaces, …) and
            // force-read the panel's display vars, all silent: the list updates its changed rows
            // in place within ~one read round-trip.
            RefreshDisplayNow(displayList, "auto-refresh");
        }
        catch { /* best-effort live refresh; never let a tick crash the UI */ }
    }
}
