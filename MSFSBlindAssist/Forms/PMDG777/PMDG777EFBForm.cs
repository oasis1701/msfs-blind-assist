using System.Diagnostics;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777
{
    public partial class PMDG777EFBForm : Form
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly EFBBridgeServer _bridgeServer;
        private readonly ScreenReaderAnnouncer _announcer;
        private IntPtr _previousWindow = IntPtr.Zero;
        private bool _simbriefLoaded = false;
        private readonly Dictionary<string, string?[]> _htmlChunkBuffers = new();

        public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;

            InitializeComponent();
            SetupEventHandlers();
        }

        public void ShowForm()
        {
            _previousWindow = GetForegroundWindow();
            Show();
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;

            fetchSimbriefButton?.Focus();
        }

        private void SetupEventHandlers()
        {
            _bridgeServer.StateUpdated += OnStateUpdated;

            fetchSimbriefButton!.Click += (_, _) =>
            {
                simbriefStatusText!.Text = "Fetching...";
                _bridgeServer.EnqueueCommand("fetch_simbrief");
            };

            sendToFmcButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("send_to_fmc");
            };

            navigraphSignInButton!.Click += (_, _) =>
            {
                navigraphStatusText!.Text = "Awaiting code...";
                authCodeTextBox!.Text = "";
                _bridgeServer.EnqueueCommand("start_navigraph_auth");
            };

            navigraphSignOutButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("sign_out_navigraph");
            };

            checkNavdataButton!.Click += (_, _) =>
            {
                navdataProgressText!.Text = "Checking...";
                _announcer.Announce("Checking for navigation data updates");
                _bridgeServer.EnqueueCommand("get_navdata_status");
            };

            downloadNavdataButton!.Click += (_, _) =>
            {
                navdataProgressText!.Text = "Starting download...";
                downloadNavdataButton.Enabled = false;
                _announcer.Announce("Starting navigation data download");
                _bridgeServer.EnqueueCommand("start_navdata_update");
            };

            savePreferencesButton!.Click += OnSavePreferences;

            // Display tab — WebView2 loads from our local server
            displayRefreshButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("get_display_elements");
                EnsureDisplayWebViewNavigated();
            };

            // Hot-inject on form load, then init WebView2
            this.Load += async (_, _) =>
            {
                HotInjectBridgeUpdates();
                await InitializeWebViewAsync();
            };

            tabControl!.SelectedIndexChanged += (_, _) =>
            {
                if (tabControl.SelectedTab == preferencesTab)
                {
                    _bridgeServer.EnqueueCommand("get_preferences");
                }
                else if (tabControl.SelectedTab?.Text == "Display")
                {
                    _bridgeServer.EnqueueCommand("get_display_elements");
                    EnsureDisplayWebViewNavigated();
                }
            };
        }

        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            System.Diagnostics.Debug.WriteLine($"[EFB Form] State update: type={e.Type}, keys={string.Join(",", e.Data.Keys)}");
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "efb_debug.log"), $"{DateTime.Now:HH:mm:ss} type={e.Type} keys=[{string.Join(",", e.Data.Keys)}] vals=[{string.Join(",", e.Data.Values.Select(v => v?.Length > 50 ? v.Substring(0, 50) + "..." : v))}]\r\n"); } catch { }

            switch (e.Type)
            {
                case "connected":
                    _announcer.Announce("EFB bridge connected");
                    break;

                case "simbrief_loaded":
                    _simbriefLoaded = true;
                    UpdateFlightDetails(e.Data);
                    simbriefStatusText!.Text = "Loaded";
                    sendToFmcButton!.Enabled = true;
                    string origin = e.Data.GetValueOrDefault("origin_icao", "");
                    string dest = e.Data.GetValueOrDefault("dest_icao", "");
                    _announcer.Announce($"SimBrief flight plan loaded: {origin} to {dest}");
                    break;

                case "simbrief_fetch_result":
                    bool success = bool.TryParse(e.Data.GetValueOrDefault("success", "false"), out var s) && s;
                    string message = e.Data.GetValueOrDefault("message", "");
                    if (success)
                    {
                        _announcer.Announce($"FMC file transfer complete: {message}");
                    }
                    else if (!string.IsNullOrEmpty(message))
                    {
                        _announcer.Announce($"FMC transfer result: {message}");
                    }
                    break;

                case "fmc_upload_started":
                    _announcer.Announce("Flight plan sent to FMC");
                    break;

                case "navigraph_code":
                    string code = e.Data.GetValueOrDefault("code", "");
                    string url = e.Data.GetValueOrDefault("url", "https://navigraph.com/code");
                    authCodeTextBox!.Text = code;
                    _announcer.Announce($"Navigraph sign-in code: {code}. Opening browser.");
                    try
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                    }
                    catch { }
                    break;

                case "navigraph_auth_state":
                    bool authenticated = e.Data.GetValueOrDefault("authenticated", "false") == "true";
                    string username = e.Data.GetValueOrDefault("username", "");
                    if (authenticated)
                    {
                        navigraphStatusText!.Text = $"Authenticated as: {username}";
                        navigraphSignInButton!.Enabled = false;
                        navigraphSignOutButton!.Enabled = true;
                        _announcer.Announce($"Signed in to Navigraph as {username}");
                    }
                    else
                    {
                        navigraphStatusText!.Text = "Not authenticated";
                        navigraphSignInButton!.Enabled = true;
                        navigraphSignOutButton!.Enabled = false;
                        authCodeTextBox!.Text = "";
                        if (!string.IsNullOrEmpty(username))
                        {
                            _announcer.Announce("Signed out of Navigraph");
                        }
                    }
                    break;

                case "preferences":
                    PopulatePreferences(e.Data);
                    break;

                case "navdata_status":
                    HandleNavdataStatus(e.Data);
                    break;

                case "navdata_progress":
                    HandleNavdataProgress(e.Data);
                    break;

                case "navdata_complete":
                    navdataProgressText!.Text = "Update complete!";
                    downloadNavdataButton!.Enabled = false;
                    _announcer.Announce("Navigation data update complete");
                    // Refresh status
                    _bridgeServer.EnqueueCommand("get_navdata_status");
                    break;

                case "navdata_error":
                    string navError = e.Data.GetValueOrDefault("message", "Unknown error");
                    navdataProgressText!.Text = $"Error: {navError}";
                    downloadNavdataButton!.Enabled = true;
                    _announcer.Announce($"Navigation data error: {navError}");
                    break;

                case "page_text":
                    string pageText = e.Data.GetValueOrDefault("text", "(empty)");
                    if (pageTextDiagnostic != null)
                        pageTextDiagnostic.Text = pageText;
                    // Fallback: if page_html hasn't arrived, render text in WebView2
                    if (displayWebView != null && _webViewInitialized)
                    {
                        string textHtml = $"<html><body style='background:#1a1a2e;color:#e0e0e0;font-family:Consolas,monospace;padding:10px;white-space:pre-wrap'>{System.Net.WebUtility.HtmlEncode(pageText)}</body></html>";
                        displayWebView.NavigateToString(textHtml);
                    }
                    _announcer.Announce("Display refreshed");
                    break;

                case "display_elements":
                    HandleDisplayElements(e.Data);
                    break;

                case "page_html":
                    HandlePageHtml(e.Data);
                    break;

                case "page_html_chunk":
                    HandlePageHtmlChunk(e.Data);
                    break;

                case "eval_result":
                    string evalResult = e.Data.GetValueOrDefault("result", e.Data.GetValueOrDefault("error", ""));
                    _announcer.Announce($"Eval: {evalResult}");
                    break;

                case "navigraph_exploration":
                    // Discovery results — log and announce what was found
                    string methods = e.Data.GetValueOrDefault("packageMethods", "none");
                    bool hasPkgs = e.Data.GetValueOrDefault("hasPackages", "false") == "True";
                    navdataProgressText!.Text = hasPkgs ? $"Packages API found: {methods}" : "Packages API not available";
                    _announcer.Announce(navdataProgressText.Text);
                    break;

                case "error":
                    string errorMsg = e.Data.GetValueOrDefault("message", "Unknown error");
                    simbriefStatusText!.Text = $"Error: {errorMsg}";
                    _announcer.Announce($"EFB error: {errorMsg}");
                    break;
            }
        }

        private void UpdateFlightDetails(Dictionary<string, string> data)
        {
            callsignValue!.Text = data.GetValueOrDefault("callsign", "\u2014");
            originValue!.Text = data.GetValueOrDefault("origin_icao", "\u2014");
            destValue!.Text = data.GetValueOrDefault("dest_icao", "\u2014");
            altValue!.Text = data.GetValueOrDefault("alt_icao", "\u2014");
            cruiseAltValue!.Text = data.GetValueOrDefault("cruise_alt", "\u2014");
            costIndexValue!.Text = data.GetValueOrDefault("cost_index", "\u2014");
            zfwValue!.Text = data.GetValueOrDefault("zfw", "\u2014");
            fuelValue!.Text = data.GetValueOrDefault("fuel_total", "\u2014");
            windValue!.Text = data.GetValueOrDefault("avg_wind", "\u2014");
        }

        private int _lastAnnouncedNavdataPercent = -1;

        private void HandleNavdataStatus(Dictionary<string, string> data)
        {
            string error = data.GetValueOrDefault("error", "");
            if (!string.IsNullOrEmpty(error))
            {
                navdataProgressText!.Text = $"Error: {error}";
                _announcer.Announce($"Navdata check error: {error}");
                return;
            }

            // Show available methods for debugging
            string methods = data.GetValueOrDefault("availableMethods", "");
            var infoLines = new List<string>();

            // Extract available cycle
            string availableCycle = data.GetValueOrDefault("available_cycle", "");
            // Fallback: try old format
            if (string.IsNullOrEmpty(availableCycle))
            {
                string rawPkgs = data.GetValueOrDefault("packages", "");
                if (!string.IsNullOrEmpty(rawPkgs) && rawPkgs.StartsWith("["))
                {
                    try
                    {
                        var pkgs = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(rawPkgs);
                        if (pkgs.ValueKind == System.Text.Json.JsonValueKind.Array && pkgs.GetArrayLength() > 0)
                            if (pkgs[0].TryGetProperty("cycle", out var cyc))
                                availableCycle = cyc.GetString() ?? "";
                    }
                    catch { }
                }
            }

            if (!string.IsNullOrEmpty(availableCycle))
            {
                navdataCycleValue!.Text = $"AIRAC {availableCycle}";
                infoLines.Add($"AIRAC {availableCycle}");
            }

            // Package count and formats
            string pkgCount = data.GetValueOrDefault("package_count", "");
            string formats = data.GetValueOrDefault("formats", "");
            if (!string.IsNullOrEmpty(pkgCount))
                infoLines.Add($"{pkgCount} packages");
            if (!string.IsNullOrEmpty(formats))
                infoLines.Add($"Formats: {formats}");

            // Installation state detection
            string installedFormats = data.GetValueOrDefault("installed_formats", "");
            string notInstalledFormats = data.GetValueOrDefault("not_installed_formats", "");
            bool needsUpdate = data.GetValueOrDefault("needs_update", "false") == "true";

            if (!string.IsNullOrEmpty(installedFormats) || !string.IsNullOrEmpty(notInstalledFormats))
            {
                if (needsUpdate)
                {
                    navdataAvailableValue!.Text = "Update available — Navigraph navdata not installed";
                    infoLines.Add($"Not installed: {notInstalledFormats}");
                    if (installedFormats != "none")
                        infoLines.Add($"Installed: {installedFormats}");
                }
                else
                {
                    navdataAvailableValue!.Text = $"Navigraph AIRAC {availableCycle} installed";
                    infoLines.Add("All Navigraph packages installed");
                }
            }

            if (!string.IsNullOrEmpty(methods))
                infoLines.Add($"Methods: {methods}");

            // Enable download when update is needed
            downloadNavdataButton!.Enabled = needsUpdate;

            navdataProgressText!.Text = string.Join(" | ", infoLines);

            if (needsUpdate)
                _announcer.Announce($"Navigraph AIRAC {availableCycle} available but not installed. Press Download to install.");
            else if (!string.IsNullOrEmpty(availableCycle))
                _announcer.Announce($"Navigraph AIRAC {availableCycle} installed and up to date");
            else
                _announcer.Announce("Status received");
        }

        private void HandleNavdataProgress(Dictionary<string, string> data)
        {
            string status = data.GetValueOrDefault("status", "");
            string percentStr = data.GetValueOrDefault("percent", "0");
            int.TryParse(percentStr, out int percent);

            navdataProgressText!.Text = $"{status} {percent}%";

            // Announce at 25% milestones
            int milestone = (percent / 25) * 25;
            if (milestone > _lastAnnouncedNavdataPercent && milestone > 0)
            {
                _lastAnnouncedNavdataPercent = milestone;
                _announcer.Announce($"{milestone} percent");
            }
        }

        /// <summary>
        /// Hot-injects updated JS functions into the running bridge via eval_js.
        /// This avoids needing a sim restart when JS bridge code changes.
        /// </summary>
        private void HotInjectBridgeUpdates()
        {
            // Hot-inject: improved display element scanner that captures ALL visible content.
            // Also adds click_by_index handler for routing clicks to real EFB elements.
            string injectCode = @"
(function() {
    _efb._hotInjectVersion = (_efb._hotInjectVersion || 0) + 1;

    // Override cmdGetDisplayElements with a comprehensive scanner
    // that captures ALL visible text and interactive elements
    _efb.cmdGetDisplayElements = function() {
        try {
            var items = [];
            _efb._displayElements = [];
            var seen = {}; // Deduplicate by text+depth

            var walk = function(el, depth) {
                if (!el || depth > 20) return;
                if (el.nodeType !== 1) return;
                var tag = (el.tagName || '').toLowerCase();
                if (tag === 'script' || tag === 'style' || tag === 'link' || tag === 'meta' || tag === 'noscript') return;

                // Check visibility
                try {
                    var cs = window.getComputedStyle(el);
                    if (cs.display === 'none' || cs.visibility === 'hidden' || cs.opacity === '0') return;
                } catch(e) {}

                // Get DIRECT text content (not from children)
                var directText = '';
                for (var n = el.firstChild; n; n = n.nextSibling) {
                    if (n.nodeType === 3) directText += n.textContent;
                }
                directText = directText.trim();

                // Also check aria-label, title, alt, placeholder
                var label = el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('alt') || el.getAttribute('placeholder') || '';

                var text = directText || label;

                // For icon-only elements (FontAwesome icons with no text), derive name from element ID
                if (!text && el.id) {
                    var idMap = {
                        'efb_dashboard_button': 'Dashboard',
                        'efb_paperwork_button': 'Plan',
                        'efb_charts_button': 'Charts',
                        'efb_authenticate_button': 'Navigraph Auth',
                        'efb_preferences_button': 'Preferences',
                        'efb_navdata_update_button': 'Navigation Data',
                        'efb_information_button': 'Information',
                        'statusbar_home': 'Home'
                    };
                    text = idMap[el.id] || '';
                    // Also check tooltip spans inside the button
                    if (!text) {
                        var tooltip = el.querySelector('.tooltip-text');
                        if (tooltip) text = tooltip.textContent.trim();
                    }
                }

                // Special handling for PMDG preferences — skip individual items,
                // we handle them as paired label+control below
                if (el.closest && el.closest('#efb_preferences_labels, #efb_preferences_values')) {
                    var kids = el.children;
                    if (kids) { for (var k = 0; k < kids.length; k++) { walk(kids[k], depth + 1); } }
                    return;
                }

                // Performance tool — pair opt-label with its control in the same opt-col.
                // This MUST run before any skip logic so pairing happens first.
                if (el.className && typeof el.className === 'string' && el.className.indexOf('opt-label') >= 0 && el.className.indexOf('opt-output-label') < 0) {
                    var labelText2 = el.textContent.trim();
                    var parent2 = el.closest ? el.closest('.opt-col') : el.parentElement;
                    if (!parent2) parent2 = el.parentElement;
                    if (labelText2 && parent2) {
                        var sibInput = parent2.querySelector('input[type=""text""], input.opt-input');
                        var sibSelect = parent2.querySelector('.opt-select, .custom-select');
                        var sibOutput = parent2.querySelector('.opt-output');
                        var controlEl = sibInput || sibSelect || sibOutput;
                        if (controlEl && controlEl !== el) {
                            var idx2 = _efb._displayElements.length;
                            _efb._displayElements.push(controlEl);
                            var pItem = { index: idx2, text: labelText2, clickable: true, tag: controlEl.tagName.toLowerCase(), role: '' };
                            if (sibInput) {
                                pItem.controlType = 'text';
                                pItem.controlValue = sibInput.value || '';
                                pItem.controlId = sibInput.id || '';
                            } else if (sibSelect) {
                                pItem.controlType = 'select';
                                var so = sibSelect.querySelector('.selected-option');
                                pItem.controlValue = so ? so.textContent.trim() : '';
                                pItem.controlId = sibSelect.id || '';
                                var sOpts = sibSelect.querySelectorAll('.option');
                                if (sOpts.length > 0) {
                                    pItem.controlOptions = [];
                                    for (var si = 0; si < sOpts.length; si++) pItem.controlOptions.push(sOpts[si].textContent.trim());
                                }
                            } else if (sibOutput) {
                                var outVal = sibOutput.textContent.trim();
                                var unitSpan = parent2.querySelector('.output-unit, [class*=""_unit""]');
                                if (unitSpan) outVal += ' ' + unitSpan.textContent.trim();
                                pItem.text = labelText2 + ': ' + (outVal || '--');
                                pItem.clickable = false;
                                delete pItem.controlType;
                            }
                            var key2 = labelText2 + '|perf|' + (pItem.controlType || '');
                            if (!seen[key2]) { seen[key2] = true; items.push(pItem); }
                            return; // Paired — don't recurse
                        }
                    }
                }

                // Performance output labels — pair with their output value
                if (el.className && typeof el.className === 'string' && el.className.indexOf('opt-output-label') >= 0) {
                    var outLabelText = el.textContent.trim();
                    if (outLabelText) {
                        var outParent = el.parentElement;
                        var outSibling = outParent ? outParent.querySelector('.opt-output') : null;
                        var outVal2 = outSibling ? outSibling.textContent.trim() : '';
                        var unitSpan2 = outParent ? outParent.querySelector('[class*=""_unit""]') : null;
                        if (unitSpan2) outVal2 += ' ' + unitSpan2.textContent.trim();
                        var oidx = _efb._displayElements.length;
                        _efb._displayElements.push(outSibling || el);
                        var okey = outLabelText + '|perfout';
                        if (!seen[okey]) { seen[okey] = true; items.push({ index: oidx, text: outLabelText + ': ' + (outVal2 || '--'), clickable: false, tag: 'p', role: '' }); }
                        return;
                    }
                }

                // Skip non-label elements inside opt-col (already captured by label pairing above)
                if (el.closest && el.closest('.opt-col')) {
                    var ecn = el.className || '';
                    if (typeof ecn === 'string' && (ecn.indexOf('opt-input') >= 0)) {
                        return;
                    }
                    // Also skip custom-select inside opt-col (captured via label pairing)
                    if (typeof ecn === 'string' && ecn.indexOf('opt-select') >= 0) {
                        return;
                    }
                }

                // Detect form control type and capture rich data
                var controlType = '';  // text, checkbox, select, or empty for non-controls
                var controlValue = '';
                var controlOptions = null;
                var controlId = el.id || '';

                if (tag === 'input') {
                    var inputType = (el.getAttribute('type') || 'text').toLowerCase();
                    if (inputType === 'text' || inputType === 'password' || inputType === 'email' || inputType === 'number') {
                        controlType = 'text';
                        controlValue = el.value || '';
                        // Find label from preceding sibling or parent label
                        if (!text) {
                            var prev = el.parentElement && el.parentElement.previousElementSibling;
                            if (prev) text = prev.textContent.trim();
                        }
                        if (!text) text = label || el.getAttribute('name') || 'Input';
                    } else if (inputType === 'checkbox') {
                        controlType = 'checkbox';
                        controlValue = el.checked ? 'true' : 'false';
                        if (!text) text = label || el.getAttribute('name') || 'Toggle';
                    } else if (inputType === 'range') {
                        controlType = 'range';
                        controlValue = el.value || '50';
                        if (!text) text = label || 'Slider';
                    }
                }

                // Custom PMDG select (div.custom-select with .option children)
                if (!controlType && el.className && typeof el.className === 'string' && el.className.indexOf('custom-select') >= 0) {
                    controlType = 'select';
                    var selectedOpt = el.querySelector('.selected-option');
                    controlValue = selectedOpt ? selectedOpt.textContent.trim() : '';
                    var optionEls = el.querySelectorAll('.option');
                    if (optionEls.length > 0) {
                        controlOptions = [];
                        for (var oi = 0; oi < optionEls.length; oi++) {
                            controlOptions.push(optionEls[oi].textContent.trim());
                        }
                    }
                    if (!text) text = label || el.id || 'Select';
                }

                // For non-form elements, use original text logic
                if (controlType && !text) {
                    text = label || controlId || controlType;
                }
                if (!controlType && (tag === 'input' || tag === 'select' || tag === 'textarea')) {
                    var val = el.value || '';
                    var lbl = label || el.getAttribute('name') || tag;
                    text = lbl + (val ? ': ' + val : '');
                }

                // Determine if clickable
                var isClickable = (tag === 'button' || tag === 'a'
                    || el.getAttribute('role') === 'button' || el.getAttribute('role') === 'tab'
                    || el.getAttribute('role') === 'link' || el.getAttribute('role') === 'menuitem'
                    || el.onclick != null || el.getAttribute('onclick')
                    || (el.className && typeof el.className === 'string' && (el.className.indexOf('btn') >= 0 || el.className.indexOf('clickable') >= 0 || el.className.indexOf('nav-link') >= 0 || el.className.indexOf('icon') >= 0))
                    || el.getAttribute('tabindex') === '0'
                    || el.style.cursor === 'pointer');

                // Form controls are always interactive
                if (controlType) isClickable = true;

                // Add this element if it has text or is a form control
                if ((text && text.length > 0 && text.length < 300) || controlType) {
                    if (!text) text = controlType;
                    var key = text.substring(0, 50) + '|' + depth + '|' + controlType;
                    if (!seen[key]) {
                        seen[key] = true;
                        var idx = _efb._displayElements.length;
                        _efb._displayElements.push(el);
                        var item = {
                            index: idx,
                            text: text.replace(/\n/g, ' ').replace(/\s+/g, ' ').trim().substring(0, 150),
                            clickable: isClickable,
                            tag: tag,
                            role: el.getAttribute('role') || ''
                        };
                        if (controlType) {
                            item.controlType = controlType;
                            item.controlValue = controlValue;
                            item.controlId = controlId;
                            if (controlOptions) item.controlOptions = controlOptions;
                        }
                        items.push(item);
                    }
                }

                // Recurse children
                var kids = el.children;
                if (kids) {
                    for (var k = 0; k < kids.length; k++) {
                        walk(kids[k], depth + 1);
                    }
                }
            };

            walk(document.body, 0);
            // Special: pair PMDG preferences labels with their controls
            var prefLabels = document.getElementById('efb_preferences_labels');
            var prefValues = document.getElementById('efb_preferences_values');
            if (prefLabels && prefValues) {
                // Use direct children only — nested .row inside custom-selects must be skipped
                var labelRows = [];
                var valueRows = [];
                for (var li = 0; li < prefLabels.children.length; li++) {
                    if (prefLabels.children[li].classList && prefLabels.children[li].classList.contains('row'))
                        labelRows.push(prefLabels.children[li]);
                }
                for (var vi = 0; vi < prefValues.children.length; vi++) {
                    if (prefValues.children[vi].classList && prefValues.children[vi].classList.contains('row'))
                        valueRows.push(prefValues.children[vi]);
                }
                var count = Math.min(labelRows.length, valueRows.length);
                // Unit toggle names for checkbox→combo conversion
                // Unit toggle mapping: [unchecked_value, checked_value]
                // PMDG convention: unchecked = first unit, checked = second unit
                // Swapped based on user testing — PMDG inverts some of these
                var unitNames = {
                    'efb_preferences_distance_unit': ['km','nm'],
                    'efb_preferences_altitude_unit': ['m','ft'],
                    'efb_preferences_length_unit': ['m','ft'],
                    'efb_preferences_speed_unit': ['mph','kph'],
                    'efb_preferences_airspeed_unit': ['kph','kts'],
                    'efb_preferences_temperature_unit': ['F','C'],
                    'efb_preferences_pressure_unit': ['inHg','hPa'],
                    'efb_preferences_weight_unit': ['lb','kg']
                };
                for (var pi = 0; pi < count; pi++) {
                    var labelText = labelRows[pi].textContent.trim();
                    if (!labelText) continue;
                    var valRow = valueRows[pi];
                    var input = valRow.querySelector('input[type=""text""]');
                    var checkbox = valRow.querySelector('input[type=""checkbox""]');
                    var range = valRow.querySelector('input[type=""range""]');
                    var customSelect = valRow.querySelector('.custom-select');
                    var btn = valRow.querySelector('button');

                    var idx = _efb._displayElements.length;
                    var item = { index: idx, text: labelText, clickable: true, tag: 'div', role: '' };

                    if (input) {
                        _efb._displayElements.push(input);
                        item.controlType = 'text';
                        item.controlValue = input.value || '';
                        item.controlId = input.id || '';
                    } else if (customSelect) {
                        _efb._displayElements.push(customSelect);
                        var selOpt = customSelect.querySelector('.selected-option');
                        item.controlType = 'select';
                        item.controlValue = selOpt ? selOpt.textContent.trim() : '';
                        item.controlId = customSelect.id || '';
                        var opts = customSelect.querySelectorAll('.option');
                        item.controlOptions = [];
                        for (var opi = 0; opi < opts.length; opi++) {
                            item.controlOptions.push(opts[opi].textContent.trim());
                        }
                    } else if (checkbox) {
                        // Convert unit toggles to combo boxes with meaningful labels
                        var cbId = checkbox.id || '';
                        if (unitNames[cbId]) {
                            _efb._displayElements.push(checkbox);
                            item.controlType = 'select';
                            item.controlOptions = unitNames[cbId];
                            item.controlValue = checkbox.checked ? unitNames[cbId][1] : unitNames[cbId][0];
                            item.controlId = cbId;
                        } else {
                            _efb._displayElements.push(checkbox);
                            item.controlType = 'checkbox';
                            item.controlValue = checkbox.checked ? 'true' : 'false';
                            item.controlId = cbId;
                        }
                    } else if (range) {
                        _efb._displayElements.push(range);
                        item.controlType = 'text';
                        item.controlValue = range.value || '50';
                        item.controlId = range.id || '';
                        item.text = labelText + ' (0-100)';
                    } else if (btn) {
                        _efb._displayElements.push(btn);
                        item.text = btn.textContent.trim() || labelText;
                        item.tag = 'button';
                    } else {
                        _efb._displayElements.push(valRow);
                        item.text = labelText + ': ' + valRow.textContent.trim();
                    }
                    items.push(item);
                }
                // Also add the Save Preferences button if present
                var saveBtn = document.getElementById('efb_preferences_save_tablet_prefs');
                if (saveBtn) {
                    var sIdx = _efb._displayElements.length;
                    _efb._displayElements.push(saveBtn);
                    items.push({ index: sIdx, text: 'Save Preferences', clickable: true, tag: 'button', role: '' });
                }
            }

            // Capture any remaining opt-output-panel results not caught by label pairing
            var outputPanels = document.querySelectorAll('.opt-output-panel');
            for (var opi = 0; opi < outputPanels.length; opi++) {
                var panel = outputPanels[opi];
                var rows = panel.children;
                for (var ri = 0; ri < rows.length; ri++) {
                    var row = rows[ri];
                    var outLabel = row.querySelector('.opt-output-label');
                    var outValue = row.querySelector('.opt-output');
                    if (outLabel) {
                        var lt = outLabel.textContent.trim();
                        var vt = outValue ? outValue.textContent.trim() : '';
                        var ut = row.querySelector('[class*=""_unit""]');
                        if (ut) vt += ' ' + ut.textContent.trim();
                        var okey2 = lt + '|output-panel';
                        if (lt && !seen[okey2]) {
                            seen[okey2] = true;
                            var oidx2 = _efb._displayElements.length;
                            _efb._displayElements.push(outValue || outLabel);
                            items.push({ index: oidx2, text: lt + ': ' + (vt || '--'), clickable: false, tag: 'p', role: '' });
                        }
                    }
                }
            }

            _efb.postState('display_elements', { count: items.length, items: JSON.stringify(items) });
        } catch(e) {
            _efb.postState('error', { message: 'DisplayElements: ' + e.message });
        }
    };

    // Override handleCommand to add click_by_index (for /display-click routing)
    var origHandler = _efb.handleCommand;
    _efb.handleCommand = function(command, payload) {
        if (command === 'click_by_index') {
            var idx = parseInt((payload && payload.idx) ? payload.idx : '-1');
            if (idx >= 0) {
                try {
                    var allEls = document.body.querySelectorAll('*');
                    if (idx < allEls.length) {
                        allEls[idx].click();
                    }
                } catch(e) { _efb.postState('error', { message: 'Click: ' + e.message }); }
            }
            return;
        }
        origHandler.call(_efb, command, payload);
    };

    // Add set_element_value command to set form control values in the real EFB
    var origHandler2 = _efb.handleCommand;
    _efb.handleCommand = function(command, payload) {
        if (command === 'set_element_value') {
            var idx = parseInt((payload && payload.index) ? payload.index : '-1');
            var val = (payload && payload.value) ? payload.value : '';
            var ctype = (payload && payload.controlType) ? payload.controlType : '';
            if (idx >= 0 && _efb._displayElements && idx < _efb._displayElements.length) {
                try {
                    var el = _efb._displayElements[idx];
                    if (ctype === 'text') {
                        // Set text input value and trigger input/change events
                        var nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                        nativeSetter.call(el, val);
                        el.dispatchEvent(new Event('input', {bubbles: true}));
                        el.dispatchEvent(new Event('change', {bubbles: true}));
                    } else if (ctype === 'checkbox') {
                        var wantChecked = (val === 'true');
                        if (el.checked !== wantChecked) {
                            el.click();
                        }
                    } else if (ctype === 'select') {
                        // Check if this is actually a checkbox presented as select (unit toggles)
                        if (el.tagName === 'INPUT' && el.type === 'checkbox') {
                            var optIdx = parseInt((payload && payload.optionIndex) ? payload.optionIndex : '0');
                            var wantChecked = (optIdx > 0);
                            if (el.checked !== wantChecked) {
                                el.click();
                            }
                        } else {
                            // Custom PMDG select — find and click the matching option
                            var options = el.querySelectorAll('.option');
                            for (var oi = 0; oi < options.length; oi++) {
                                if (options[oi].textContent.trim() === val) {
                                    options[oi].click();
                                    break;
                                }
                            }
                        }
                    }
                } catch(e) { _efb.postState('error', {message: 'SetValue: ' + e.message}); }
            }
            return;
        }
        if (command === 'click_by_index') {
            var idx2 = parseInt((payload && payload.idx) ? payload.idx : '-1');
            if (idx2 >= 0) {
                try {
                    var allEls = document.body.querySelectorAll('*');
                    if (idx2 < allEls.length) { allEls[idx2].click(); }
                } catch(e) { _efb.postState('error', {message: 'Click: ' + e.message}); }
            }
            return;
        }
        origHandler2.call(_efb, command, payload);
    };

    console.log('[EFB Hot-Inject] v' + _efb._hotInjectVersion + ' — full scanner + form controls');
    return 'injected v' + _efb._hotInjectVersion;
})()
";
            _bridgeServer.EnqueueCommand("eval_js", new Dictionary<string, string> { ["code"] = injectCode });
        }

        // Legacy HandlePageHtml — no longer renders to WebView2 (server-based display now)
        private void HandlePageHtml(Dictionary<string, string> data)
        {
            // Page HTML is no longer rendered directly — the Display tab uses /display endpoint.
            // Just save for debugging.
            string rawHtml = data.GetValueOrDefault("html", "");
            if (!string.IsNullOrEmpty(rawHtml))
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "efb_captured.html"), rawHtml); } catch { }
            }
        }

        private void HandlePageHtmlChunk(Dictionary<string, string> data)
        {
            string id = data.GetValueOrDefault("id", "");
            if (!int.TryParse(data.GetValueOrDefault("index", ""), out int index)) return;
            if (!int.TryParse(data.GetValueOrDefault("total", ""), out int total)) return;
            string chunk = data.GetValueOrDefault("chunk", "");

            if (string.IsNullOrEmpty(id) || total <= 0) return;

            if (!_htmlChunkBuffers.TryGetValue(id, out var buffer))
            {
                buffer = new string?[total];
                _htmlChunkBuffers[id] = buffer;
            }

            if (index >= 0 && index < total)
                buffer[index] = chunk;

            // Check if all chunks received
            if (buffer.All(c => c != null))
            {
                _htmlChunkBuffers.Remove(id);
                string fullHtml = string.Concat(buffer);
                System.Diagnostics.Debug.WriteLine($"[EFB] Reassembled {total} chunks → {fullHtml.Length} chars");
                // Dump to file for debugging
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "efb_captured.html"), fullHtml); } catch { }
                HandlePageHtml(new Dictionary<string, string> { ["html"] = fullHtml });
            }
        }

        private bool _webViewInitialized;

        private async Task InitializeWebViewAsync()
        {
            if (_webViewInitialized || displayWebView == null) return;
            try
            {
                await displayWebView.EnsureCoreWebView2Async();
                _webViewInitialized = true;
                System.Diagnostics.Debug.WriteLine("[EFB] WebView2 initialized");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EFB] WebView2 init failed: {ex.Message}");
            }
        }

        private void EnsureDisplayWebViewNavigated()
        {
            if (!_webViewInitialized || displayWebView == null) return;

            string currentUrl = displayWebView.Source?.ToString() ?? "";
            if (!currentUrl.Contains("localhost:19777/display"))
            {
                displayWebView.Source = new Uri("http://localhost:19777/display");
            }
        }

        /// <summary>
        /// Caches display elements in the server for the /display-data endpoint,
        /// and ensures WebView2 is navigated to the display page.
        /// </summary>
        private void HandleDisplayElements(Dictionary<string, string> data)
        {
            if (!data.TryGetValue("items", out string? itemsJson) || string.IsNullOrEmpty(itemsJson))
                return;

            // Cache in server for /display-data endpoint
            _bridgeServer.UpdateDisplayElements(itemsJson);

            // Ensure WebView2 is pointed at our server
            EnsureDisplayWebViewNavigated();
        }

        private void PopulatePreferences(Dictionary<string, string> data)
        {
            if (data.TryGetValue("simbrief_id", out string? simbriefId))
                simbriefAliasTextBox!.Text = simbriefId;

            SetComboValue(weatherSourceCombo!, data.GetValueOrDefault("weather_source", ""));
            SetComboValue(weightUnitCombo!, data.GetValueOrDefault("weight_unit", ""));
            SetComboValue(distanceUnitCombo!, data.GetValueOrDefault("distance_unit", ""));
            SetComboValue(altitudeUnitCombo!, data.GetValueOrDefault("altitude_unit", ""));
            SetComboValue(temperatureUnitCombo!, data.GetValueOrDefault("temperature_unit", ""));
        }

        private static void SetComboValue(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private void OnSavePreferences(object? sender, EventArgs e)
        {
            if (!_bridgeServer.IsBridgeConnected)
            {
                _announcer.Announce("EFB bridge not connected. Preferences cannot be saved while the EFB tablet is not active in the simulator.");
                return;
            }

            _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                { { "key", "simbrief_id" }, { "value", simbriefAliasTextBox!.Text ?? "" } });

            EnqueueComboPreference(weatherSourceCombo!, "weather_source");
            EnqueueComboPreference(weightUnitCombo!, "weight_unit");
            EnqueueComboPreference(distanceUnitCombo!, "distance_unit");
            EnqueueComboPreference(altitudeUnitCombo!, "altitude_unit");
            EnqueueComboPreference(temperatureUnitCombo!, "temperature_unit");

            _bridgeServer.EnqueueCommand("save_preferences");
            _announcer.Announce("Preferences saved");
        }

        private void EnqueueComboPreference(ComboBox combo, string key)
        {
            string? value = combo.SelectedItem?.ToString();
            if (value != null)
            {
                _bridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    { { "key", key }, { "value", value } });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _bridgeServer.StateUpdated -= OnStateUpdated;
            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
            base.OnFormClosing(e);
        }
    }
}
