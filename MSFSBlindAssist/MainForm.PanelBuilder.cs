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
        
        if (currentAircraft.GetPanelStructure().ContainsKey(currentSection))
        {
            foreach (var panel in currentAircraft.GetPanelStructure()[currentSection])
            {
                panelsListBox.Items.Add(panel);
            }
        }
        
        // Re-enable event
        panelsListBox.SelectedIndexChanged += PanelsListBox_SelectedIndexChanged;
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
                        if (variables.ContainsKey(varKey) && !string.IsNullOrEmpty(variables[varKey].StateVariable))
                        {
                            simConnectManager.RequestVariable(variables[varKey].StateVariable!);
                        }
                    }
                }
            }

            // Clear and reload controls
            controlsContainer.Controls.Clear();
            currentControls.Clear();

            if (!currentAircraft.GetPanelControls().ContainsKey(currentPanel))
                return;

        // Create a TableLayoutPanel for better layout
        TableLayoutPanel layout = new TableLayoutPanel();
        layout.ColumnCount = 2;
        layout.RowCount = 0;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
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

            var varDef = currentAircraft.GetVariables()[varKey];
            
            // Add a new row (sliders need a little more height for the TrackBar).
            int rowIndex = layout.RowCount++;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, varDef.RenderAsSlider ? 48 : 35));

            // Create label
            Label label = new Label();
            label.Text = varDef.DisplayName + ":";
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoSize = false;
            label.Size = new Size(140, 25);
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
                if (!string.IsNullOrEmpty(varDef.StateVariable) && currentSimVarValues.ContainsKey(varDef.StateVariable))
                {
                    double stateVal = currentSimVarValues[varDef.StateVariable];
                    buttonText = $"{varDef.DisplayName}: {(stateVal != 0 ? "On" : "Off")}";
                }
                else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count >= 2 && currentSimVarValues.ContainsKey(varKey))
                {
                    // Fallback for non-Fenix buttons that still use ValueDescriptions.
                    // Resting-state (value 0 = Off/Idle) suppression is OPT-IN via
                    // SuppressRestingButtonState, set only by the FBW momentary-button helpers
                    // — a momentary push-button has no meaningful resting value, so appending
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
                     !string.IsNullOrEmpty(varDef.Units))
            {
                // Continuous-numeric read-only TextBox. Used for cockpit gauges
                // exposed by the PMDG NG3 SDK as float fields (cabin altitude,
                // DP, duct pressure, APU EGT, fuel temp, etc.). Text is
                // "{value:Format} {Units}" and is silently refreshed on each
                // continuous broadcast via UpdateControlFromSimVar — the user
                // reads the current value by Tab-focusing the field.
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
                    initial = $"{cur.ToString(varDef.Format, System.Globalization.CultureInfo.InvariantCulture)} {varDef.Units}";
                }
                readoutBox.Text = initial;

                layout.Controls.Add(readoutBox, 1, rowIndex);
                currentControls[varKey] = readoutBox;
            }
            else if (varDef.ValueDescriptions != null && varDef.ValueDescriptions.Count > 1 &&
                     (varDef.RenderAsReadOnlyStatus || varDef.OnlyAnnounceValueDescriptionMatches))
            {
                // Read-only status field (annunciators, door state, etc.).
                // ValueDescriptions still drive the text; the user can focus the
                // field for the screen reader to read it, but cannot change it.
                TextBox statusBox = new TextBox();
                statusBox.ReadOnly = true;
                statusBox.TabStop = true;
                statusBox.Size = new Size(240, 25);
                statusBox.Name = varKey;
                statusBox.AccessibleName = varDef.DisplayName;

                // Seed initial text from cached value, falling back to numeric string
                // and finally to "—" if no value is known yet.
                string initial = "—";
                if (currentSimVarValues.ContainsKey(varKey))
                {
                    double cur = currentSimVarValues[varKey];
                    initial = varDef.ValueDescriptions.TryGetValue(cur, out string? desc)
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

                        if (varDef.ValueDescriptions.ContainsKey(currentValue))
                        {
                            string description = varDef.ValueDescriptions[currentValue];
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
                                // RPN form 2026-07: the FBW template-verbatim
                                // "<value> <index> r (>K:2:TAXI_LIGHTS_SET)" — live-verified to set
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
                            var selectedValue = sortedValues[combo.SelectedIndex].Key;
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
                // Panel for TextBox and Button (unless aircraft prevents text input)
                Panel inputPanel = new Panel();
                inputPanel.Size = new Size(240, 25);
                
                TextBox textBox = new TextBox();
                textBox.Location = new Point(0, 0);
                textBox.Size = new Size(100, 25);
                textBox.AccessibleName = $"{varDef.DisplayName} value";

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

                Button button = new Button();
                button.Text = "Set";
                button.Location = new Point(110, 0);
                button.Size = new Size(60, 23);
                button.AccessibleName = $"Set {varDef.DisplayName}";
                
                button.Click += (s2, e2) =>
                {
                    // Aircraft delegation: let the loaded aircraft claim _SET keys
                    // (e.g., PMDG 737's EFIS_MinsValueFt_*_SET vars need RST-then-rotate
                    // dispatch). The aircraft parses textBox.Text itself; we pass the
                    // double value when parseable, else 0.
                    double parsedValue = 0;
                    double.TryParse(
                        textBox.Text.Replace(',', '.'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out parsedValue);
                    if (currentAircraft.HandleUIVariableSet(
                            varKey, parsedValue, varDef, simConnectManager!, announcer))
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
                    else if (double.TryParse(textBox.Text, out double value))
                    {
                        // Let aircraft handle special cases first (validation, conversion, multi-step logic)
                        if (currentAircraft.HandleUIVariableSet(varKey, value, varDef, simConnectManager!, announcer))
                        {
                            return; // Aircraft handled it
                        }

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
                };
                
                inputPanel.Controls.Add(textBox);
                inputPanel.Controls.Add(button);
                layout.Controls.Add(inputPanel, 1, rowIndex);
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

            // Panel to hold the status list + refresh button
            Panel displayPanel = new Panel();
            displayPanel.Size = new Size(240, 150);

            // Read-only NAVIGABLE LIST for the status display — one row per item. A live
            // refresh updates ONLY the items whose value changed, and a ListBox item-text
            // update never touches a caret or selection, so the screen-reader cursor stays
            // on the row the user is reading. (A multiline TextBox couldn't do this: every
            // in-place edit has to move the selection to replace text, and NVDA follows
            // those caret events, throwing the review cursor around.) Arrow up/down reads
            // each row; only a row whose value actually changes re-announces while focused.
            ListBox displayList = new ListBox();
            displayList.Size = new Size(240, 120);
            displayList.Location = new Point(0, 0);
            displayList.IntegralHeight = false;
            displayList.SelectionMode = SelectionMode.One;
            displayList.HorizontalScrollbar = true;
            displayList.TabStop = true;
            displayList.AccessibleName = "Status display, updates live (F5 to refresh now)";

            // Refresh button
            Button refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.Size = new Size(80, 23);
            refreshButton.Location = new Point(0, 122);
            refreshButton.AccessibleName = "Refresh status";

            // F5 on the list triggers the same refresh action as the button — convenient
            // for blind users who don't want to tab to the button.
            displayList.KeyDown += (s2, e2) =>
            {
                if (e2.KeyCode == Keys.F5)
                {
                    e2.SuppressKeyPress = true;
                    refreshButton.PerformClick();
                }
            };

            // When the user moves focus TO the list, pull current content. The live auto-refresh
            // timer keeps it current while focused too (updating only the changed rows, so the
            // cursor stays put), but it skips while a SELECTOR COMBO is focused so it can't fight
            // the combo's announcement — so this GotFocus pull is what brings the list current the
            // instant the user moves from the page combo onto it to read.
            displayList.GotFocus += (s2, e2) =>
            {
                try { currentAircraft?.OnDisplayPanelShown(currentPanel, simConnectManager!); }
                catch (Exception ex)
                {
                    // A throw here silently leaves the SD-page snapshot stale with no clue why —
                    // a known confusing-bug shape for this codebase (values that "never move").
                    Log.Debug("MainForm", $"OnDisplayPanelShown (GotFocus) failed for panel '{currentPanel}': {ex.Message}");
                }
            };

            refreshButton.Click += async (s2, e2) =>
            {
                // Where to return focus when the refresh finishes (set by the F5 handler
                // before PerformClick moves focus onto this button). Fall back to the list
                // if it's somehow still focused here.
                Control? focusReturn = _refreshFocusReturn ?? (displayList.Focused ? displayList : null);
                _refreshFocusReturn = null;

                // First populate only (empty list): show a Loading placeholder so it isn't silent.
                if (displayList.Items.Count == 0)
                    displayList.Items.Add("Loading...");
                displayValues.Clear();  // Clear old values for this panel

                // Get the display variables for this panel
                var displayVars = GetPanelDisplayVarsCached()[currentPanel];

                // Create a task completion source for each variable
                var pendingValues = new Dictionary<string, TaskCompletionSource<bool>>();
                foreach (var varKey in displayVars)
                {
                    pendingValues[varKey] = new TaskCompletionSource<bool>();
                }

                // Store the pending values temporarily
                pendingDisplayRequests = pendingValues;

                // Rebuild any aircraft-managed SNAPSHOT content (the A380/A32NX/PMDG SD-page
                // box). That content lives in the aircraft def's _sdPageContent and is ONLY
                // regenerated by OnDisplayPanelShown -> RefreshSdPageDisplayAsync, which
                // re-reads the underlying SimVars (FOB, engine N1-N3, per-tank fuel, …).
                // The display-var re-request below renders that string but never rebuilds
                // it, so WITHOUT this call a manual F5 / Refresh re-printed the SAME stale
                // snapshot and values like "FOB 13400 KG" never moved. Fire-and-forget: it
                // pushes its own UpdateDisplayText when the fresh read completes (~0.6s).
                try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager!); }
                catch (Exception ex)
                {
                    // A throw here silently leaves the SD-page snapshot stale with no clue why —
                    // a known confusing-bug shape for this codebase (values that "never move").
                    Log.Debug("MainForm", $"OnDisplayPanelShown (Refresh) failed for panel '{currentPanel}': {ex.Message}");
                }

                // Request all values. forceUpdate=true bypasses the
                // ProcessIndividualVariableResponse suppression that drops
                // SimVarUpdated for unchanged announced variables — without it,
                // a Refresh on a stable announced var (e.g. IRS state held at
                // Aligning for minutes) silently no-ops and the display falls
                // through to "--" after the 2-second timeout.
                foreach (var varKey in displayVars)
                {
                    if (currentAircraft.GetVariables().ContainsKey(varKey))
                    {
                        simConnectManager?.RequestVariable(varKey, forceUpdate: true);
                    }
                }

                // Wait for all responses or timeout after 2 seconds
                var allTasks = pendingValues.Values.Select(tcs => tcs.Task).ToArray();
                var timeoutTask = Task.Delay(2000);
                await Task.WhenAny(Task.WhenAll(allTasks), timeoutTask);

                // Clear pending requests
                pendingDisplayRequests = null;

                // Update display - NO announcement, user will read with NVDA
                UpdateDisplayText(displayList);

                // Restore focus to the status list if the refresh moved it (onto the Refresh
                // button). Only refocuses when it actually left — a deliberate click on the
                // Refresh button won't bounce focus back to the list.
                if (focusReturn != null && focusReturn.IsHandleCreated && focusReturn.CanFocus && !focusReturn.Focused)
                    focusReturn.Focus();
            };

            displayPanel.Controls.Add(displayList);
            displayPanel.Controls.Add(refreshButton);
            layout.Controls.Add(displayPanel, 1, displayRow);

            // Store reference to display list + refresh button (F5 in ProcessCmdKey
            // performs the refresh from anywhere in the panel).
            currentControls["_DISPLAY_"] = displayList;
            currentControls["_REFRESH_"] = refreshButton;
        }

            // Resume + lay out ONCE now that every row exists, then attach.
            layout.ResumeLayout(true);
            controlsContainer.SuspendLayout();
            controlsContainer.Controls.Add(layout);
            controlsContainer.ResumeLayout(true);

            // Auto-populate a multi-page status box (e.g. the SD-page combo) with the
            // combo's CURRENT page, so the user doesn't have to cycle it to get content
            // on first display. No-op for panels without such a box.
            try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager!); }
            catch (Exception ex)
            {
                // A throw here silently leaves the SD-page snapshot stale with no clue why —
                // a known confusing-bug shape for this codebase (values that "never move").
                Log.Debug("MainForm", $"OnDisplayPanelShown (initial show) failed for panel '{currentPanel}': {ex.Message}");
            }

            // Start (or stop) the live status-box auto-refresh for THIS panel. Only panels
            // that actually built a status display (have a "_REFRESH_" button) get the timer;
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

    // Starts the live status-box auto-refresh when the currently-shown panel has a status
    // display ("_REFRESH_" button); stops it otherwise. Called every time a panel is shown.
    private void StartOrStopSdAutoRefresh()
    {
        bool hasDisplay = currentControls != null && currentControls.ContainsKey("_REFRESH_");
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
                currentControls == null || !currentControls.ContainsKey("_REFRESH_"))
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

            // (a) Rebuild any snapshot SD-page content (FOB, engine, fuel, control surfaces, …) —
            //     silent; OnDisplayPanelShown force-reads the row vars and re-pushes the page var,
            //     which drives UpdateDisplayText -> the list updates its changed rows in place.
            try { currentAircraft.OnDisplayPanelShown(currentPanel, simConnectManager); }
            catch (Exception ex)
            {
                // A throw here silently leaves the SD-page snapshot stale with no clue why —
                // a known confusing-bug shape for this codebase (values that "never move").
                Log.Debug("MainForm", $"OnDisplayPanelShown (auto-refresh) failed for panel '{currentPanel}': {ex.Message}");
            }

            // (b) Force-read the panel's own display vars so the cache is fresh (covers the
            //     non-override panels whose display vars ARE the content). Each force-read response
            //     pushes through OnSimVarUpdated, which schedules a COALESCED repaint — so the whole
            //     burst of responses collapses into ONE list rebuild (instead of one per var), and a
            //     changed row appears within ~one read round-trip. We also schedule once here so the
            //     list still repaints when no value changed (e.g. the very first populate).
            if (GetPanelDisplayVarsCached().TryGetValue(currentPanel, out var liveVars))
                foreach (var vk in liveVars)
                    if (currentAircraft.GetVariables().ContainsKey(vk))
                        simConnectManager.RequestVariable(vk, forceUpdate: true);

            ScheduleDisplayRepaint();
        }
        catch { /* best-effort live refresh; never let a tick crash the UI */ }
    }
}
