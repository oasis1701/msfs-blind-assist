using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.PMDG777.Apps;
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
        private readonly EfbAppNavigator _navigator;
        private IntPtr _previousWindow = IntPtr.Zero;
        private bool _wasConnected;
        private System.Windows.Forms.Timer? _connectionCheckTimer;
        private readonly Dictionary<string, string?[]> _htmlChunkBuffers = new();
        private bool _webViewInitialized;

        public PMDG777EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;
            _navigator = new EfbAppNavigator(bridgeServer);
            _navigator.NavigationCompleted += OnNavigationCompleted;

            InitializeComponent();
            WireEventHandlers();

            _connectionCheckTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            _connectionCheckTimer.Tick += OnConnectionCheck;
            _connectionCheckTimer.Start();
            OnConnectionCheck(this, EventArgs.Empty);
        }

        public void ShowForm()
        {
            _previousWindow = GetForegroundWindow();
            Show();
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;

            dashboardPanel?.InitialFocusControl?.Focus();
        }

        private void WireEventHandlers()
        {
            _bridgeServer.StateUpdated += OnStateUpdated;
            _bridgeServer.Error += OnBridgeServerError;

            displayRefreshButton!.Click += (_, _) =>
            {
                _bridgeServer.EnqueueCommand("get_display_elements");
                EnsureDisplayWebViewNavigated();
            };

            this.Load += async (_, _) =>
            {
                HotInjectBridgeUpdates();
                await InitializeWebViewAsync();
            };

            outerTabControl!.SelectedIndexChanged += (_, _) => HandleTabChanged();
            efbInnerTabControl!.SelectedIndexChanged += (_, _) => HandleTabChanged();

            this.KeyDown += OnFormKeyDown;
        }

        private void OnFormKeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+Shift+C — capture current tablet page HTML from anywhere in the form.
            if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                _bridgeServer.EnqueueCommand("get_page_html");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // Ctrl+Shift+R — hot-reload the EFB bridge JavaScript from disk without
            // restarting the sim. Reads the current pmdg-efb-accessibility-bridge.js
            // from the output Resources folder, tears down the previous instance's
            // timers, then evals the fresh script inside the running tablet.
            if (e.Control && e.Shift && e.KeyCode == Keys.R)
            {
                HotReloadBridgeJs();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // Ctrl+Shift+D — dump every Settings key PMDG exposes with its raw
            // value and JS type, plus DOM state of each toggle, into a
            // MessageBox. Diagnostic for preference round-trip bugs (e.g. the
            // weight kg/lb value returned by PMDG may not be the literal string
            // we expect).
            if (e.Control && e.Shift && e.KeyCode == Keys.D)
            {
                _bridgeServer.EnqueueCommand("dump_preferences");
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            // Ctrl+Shift+E — dump the structural tree of a DOM element by id.
            // Prompts for the id and shows the result in a MessageBox.
            // Diagnostic for runtime DOM structures that differ from captures.
            if (e.Control && e.Shift && e.KeyCode == Keys.E)
            {
                PromptDumpElement();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
        }

        private void PromptDumpElement()
        {
            using var dialog = new Form
            {
                Text = "Dump DOM element",
                Size = new System.Drawing.Size(420, 140),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                MaximizeBox = false
            };
            var lbl = new Label { Text = "Element id:", Location = new System.Drawing.Point(10, 15), AutoSize = true };
            var box = new TextBox
            {
                Location = new System.Drawing.Point(100, 12),
                Size = new System.Drawing.Size(290, 25),
                Text = "opt_takeoff_runway_id",
                AccessibleName = "Element id"
            };
            var ok = new Button
            {
                Text = "Dump",
                Location = new System.Drawing.Point(220, 50),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.OK,
                AccessibleName = "Dump"
            };
            var cancel = new Button
            {
                Text = "Cancel",
                Location = new System.Drawing.Point(310, 50),
                Size = new System.Drawing.Size(80, 30),
                DialogResult = DialogResult.Cancel,
                AccessibleName = "Cancel"
            };
            dialog.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(box.Text))
            {
                string id = box.Text.Trim();
                _bridgeServer.EnqueueCommand("dump_element", new Dictionary<string, string> { ["id"] = id });
                _announcer.Announce($"Dumping element {id}");
                // If no response within 3s, warn the user — bridge may be stale.
                _dumpElementTimeout?.Stop();
                _dumpElementTimeout?.Dispose();
                _dumpElementTimeout = new System.Windows.Forms.Timer { Interval = 3000 };
                _dumpElementTimeout.Tick += (_, _) =>
                {
                    _dumpElementTimeout?.Stop();
                    _dumpElementTimeout?.Dispose();
                    _dumpElementTimeout = null;
                    MessageBox.Show(this,
                        $"No response for dump of '{id}'.\n\n" +
                        "Most likely the bridge JS running in the tablet is older than the build " +
                        "and doesn't know the dump_element command. Press Ctrl+Shift+R in the EFB " +
                        "form to hot-reload the bridge, then retry.",
                        "No dump response",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                _dumpElementTimeout.Start();
            }
        }

        private System.Windows.Forms.Timer? _dumpElementTimeout;

        private void HotReloadBridgeJs()
        {
            string path = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Resources", "pmdg-efb-accessibility-bridge.js");
            if (!System.IO.File.Exists(path))
            {
                _announcer.Announce($"Hot-reload failed: bridge script not found at {path}");
                return;
            }
            string js;
            try
            {
                js = System.IO.File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                _announcer.Announce($"Hot-reload failed: {ex.Message}");
                return;
            }

            // Tear down the previous instance's timers/subscribers before re-evaluating.
            const string teardown =
                "try {" +
                "  if (typeof _efb !== 'undefined' && _efb) {" +
                "    if (_efb.commandPollTimer) clearInterval(_efb.commandPollTimer);" +
                "    if (_efb.heartbeatTimer) clearInterval(_efb.heartbeatTimer);" +
                "    if (_efb.fmsTransferTimeout) clearTimeout(_efb.fmsTransferTimeout);" +
                "    console.log('[EFB Bridge] Hot-reload: previous instance timers cleared');" +
                "  }" +
                "} catch (e) { console.error('[EFB Bridge] Hot-reload teardown failed:', e); }\n";

            string fullScript = teardown + js;

            // CRITICAL: wrap in indirect eval `(0, eval)("…")` so the script
            // runs in GLOBAL scope. Without this, `var _efb = {...}` inside
            // the bridge would create a local _efb in handleCommand's scope
            // (because eval inside a function is direct eval), and the global
            // _efb would never be replaced. The pong would then keep
            // reporting the old version forever.
            //
            // The script content needs to be JSON-encoded into a JS string
            // literal so it can be passed as the argument to (0, eval)(…).
            string jsLiteral = System.Text.Json.JsonSerializer.Serialize(fullScript);
            string indirectEvalCode = "(0, eval)(" + jsLiteral + ");";

            _bridgeServer.EnqueueCommand("eval_js", new Dictionary<string, string> { ["code"] = indirectEvalCode });

            // Re-inject the Display tab's DOM scanner. HotInjectBridgeUpdates()
            // is normally called once on form load and extends the _efb object
            // with display-scanner helpers — but those extensions live on the
            // old _efb and disappear when the fresh bridge JS replaces it.
            // Without this, the Display tab degrades to rendering every
            // element as a generic button on the next hot-reload.
            HotInjectBridgeUpdates();

            // Extract the BRIDGE_JS_VERSION marker from the source so the
            // announcement reflects what we just shipped. No magic — just a
            // comment the bridge itself carries near the top.
            string versionFromSource = "?";
            var match = System.Text.RegularExpressions.Regex.Match(
                js, @"BRIDGE_JS_VERSION:\s*([A-Za-z0-9._\-]+)");
            if (match.Success) versionFromSource = match.Groups[1].Value;

            _announcer.Announce($"Bridge JS hot-reloaded, {js.Length / 1024} KB, source version {versionFromSource}");

            // Immediately ping the running bridge so we can confirm the NEW
            // JS is actually executing (eval may have silently failed on a
            // syntax error). Pong arrives as a state update and gets shown.
            _bridgeServer.EnqueueCommand("ping");
        }

        // Form-level access keys — Alt+letter jumps to a tab from anywhere,
        // including when focus is inside a textbox on one of the panels.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.Alt) == Keys.Alt)
            {
                Keys key = keyData & Keys.KeyCode;
                // Outer tabs
                switch (key)
                {
                    case Keys.E: outerTabControl!.SelectedTab = efbTab; return true;
                    case Keys.P: outerTabControl!.SelectedTab = performanceTab; return true;
                    case Keys.G: outerTabControl!.SelectedTab = groundOpsTab; return true;
                    case Keys.W: outerTabControl!.SelectedTab = weightsBalanceTab; return true;
                    case Keys.M: outerTabControl!.SelectedTab = manualsTab; return true;
                    case Keys.D: outerTabControl!.SelectedTab = displayTab; return true;
                }
                // Inner EFB sub-tabs — only meaningful when EFB is the outer tab.
                // Switching to them auto-switches the outer tab too.
                switch (key)
                {
                    case Keys.H:
                        outerTabControl!.SelectedTab = efbTab;
                        efbInnerTabControl!.SelectedTab = dashboardSubTab;
                        return true;
                    case Keys.R:
                        outerTabControl!.SelectedTab = efbTab;
                        efbInnerTabControl!.SelectedTab = preferencesSubTab;
                        return true;
                    case Keys.N:
                        outerTabControl!.SelectedTab = efbTab;
                        efbInnerTabControl!.SelectedTab = navdataSubTab;
                        return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void HandleTabChanged()
        {
            if (outerTabControl!.SelectedTab == displayTab)
            {
                // Display is a debug mirror — don't drive the tablet, just refresh.
                _bridgeServer.EnqueueCommand("get_display_elements");
                EnsureDisplayWebViewNavigated();
                return;
            }

            var targetApp = GetTargetApp();
            _navigator.NavigateAsync(targetApp);
        }

        private EfbApp GetTargetApp()
        {
            if (outerTabControl!.SelectedTab == efbTab)
            {
                if (efbInnerTabControl!.SelectedTab == dashboardSubTab) return EfbApp.Dashboard;
                if (efbInnerTabControl.SelectedTab == preferencesSubTab) return EfbApp.Preferences;
                if (efbInnerTabControl.SelectedTab == navdataSubTab) return EfbApp.Navdata;
                return EfbApp.Dashboard;
            }
            if (outerTabControl.SelectedTab == performanceTab) return EfbApp.Performance;
            if (outerTabControl.SelectedTab == groundOpsTab) return EfbApp.GroundOps;
            if (outerTabControl.SelectedTab == weightsBalanceTab) return EfbApp.WeightsBalance;
            if (outerTabControl.SelectedTab == manualsTab) return EfbApp.Manuals;
            return EfbApp.Home;
        }

        private void OnNavigationCompleted(EfbApp app)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<EfbApp>(OnNavigationCompleted), app); } catch { } return; }
            GetActivePanel()?.OnActivated();
        }

        private EfbAppPanelBase? GetActivePanel()
        {
            if (outerTabControl!.SelectedTab == efbTab)
            {
                if (efbInnerTabControl!.SelectedTab == dashboardSubTab) return dashboardPanel;
                if (efbInnerTabControl.SelectedTab == preferencesSubTab) return prefsPanel;
                if (efbInnerTabControl.SelectedTab == navdataSubTab) return navdataPanel;
            }
            if (outerTabControl.SelectedTab == performanceTab) return performancePanel;
            if (outerTabControl.SelectedTab == groundOpsTab) return groundOpsPanel;
            if (outerTabControl.SelectedTab == weightsBalanceTab) return weightsBalancePanel;
            if (outerTabControl.SelectedTab == manualsTab) return manualsPanel;
            return null;
        }

        private void OnBridgeServerError(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _announcer.Announce(message);
        }

        private void OnConnectionCheck(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            bool connected = _bridgeServer.IsBridgeConnected;

            connectionStatusText!.Text = connected
                ? "Connected"
                : "Not connected \u2014 EFB tablet must be open in simulator";

            if (connected && !_wasConnected)
            {
                _announcer.Announce("EFB bridge connected");
                UpdateConnectedState(true);
                // Prime the preferences cache so weight/unit formatting in panels
                // like Dashboard has correct values before the user ever visits Prefs.
                _bridgeServer.EnqueueCommand("get_preferences");
                // Re-post any SimBrief payload already loaded on the tablet so the
                // dashboard auto-populates without requiring Fetch.
                _bridgeServer.EnqueueCommand("replay_simbrief");
                // Actively probe Navigraph auth — onAuthStateChanged doesn't fire
                // for the "already signed in at load time" case, so without this
                // the UI shows "signed out" for a session that's actually authed.
                _bridgeServer.EnqueueCommand("check_navigraph_auth");
            }
            else if (!connected && _wasConnected)
            {
                _announcer.Announce("EFB bridge disconnected");
                UpdateConnectedState(false);
            }

            _wasConnected = connected;
        }

        private void UpdateConnectedState(bool connected)
        {
            dashboardPanel?.SetConnected(connected);
            prefsPanel?.SetConnected(connected);
            navdataPanel?.SetConnected(connected);
        }

        /// <summary>
        /// Main form only handles a few "chrome" state updates (display, page html
        /// reassembly, eval results). Panel-level state flows straight to each
        /// panel via their own subscriptions in <see cref="EfbAppPanelBase"/>.
        /// </summary>
        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;

            switch (e.Type)
            {
                case "preferences":
                    PreferencesCache.Update(e.Data);
                    break;

                case "preferences_debug":
                    ShowPreferencesDebug(e.Data);
                    break;

                case "element_dump":
                    _dumpElementTimeout?.Stop();
                    _dumpElementTimeout?.Dispose();
                    _dumpElementTimeout = null;
                    ShowElementDump(e.Data);
                    break;

                case "pong":
                    string version = e.Data.GetValueOrDefault("version", "?");
                    string hasDump = e.Data.GetValueOrDefault("has_dump_element", "?");
                    _announcer.Announce($"Bridge running version {version}, dump_element: {hasDump}");
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
                    System.Diagnostics.Debug.WriteLine($"[EFB] eval_result: {evalResult}");
                    break;
            }
        }

        private void ShowPreferencesDebug(Dictionary<string, string> data)
        {
            var lines = new List<string>();
            foreach (var kvp in data.OrderBy(k => k.Key))
                lines.Add($"{kvp.Key}: {kvp.Value}");
            string text = string.Join(Environment.NewLine, lines);
            MessageBox.Show(this, text, "Preferences debug dump",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowElementDump(Dictionary<string, string> data)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"id: {data.GetValueOrDefault("id", "?")}");
            if (data.TryGetValue("error", out var err))
            {
                sb.AppendLine($"ERROR: {err}");
            }
            else if (data.TryGetValue("info", out var info))
            {
                // New outerHTML path — the real data went to page_html_chunk
                // and was saved to %TEMP%. Show that status plus the newest
                // capture filename (regardless of whose timestamp it is).
                sb.AppendLine($"info: {info}");
                sb.AppendLine();
                try
                {
                    string tempDir = System.IO.Path.GetTempPath();
                    var latest = new System.IO.DirectoryInfo(tempDir)
                        .GetFiles("efb_captured_*.html")
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();
                    if (latest != null)
                    {
                        sb.AppendLine($"Latest capture: {latest.FullName}");
                        sb.AppendLine($"Size: {latest.Length} bytes");
                        sb.AppendLine($"Modified: {latest.LastWriteTime:HH:mm:ss}");
                    }
                    else
                    {
                        sb.AppendLine("No efb_captured_*.html file found in TEMP.");
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"Could not scan TEMP: {ex.Message}");
                }
            }
            else
            {
                // Legacy tree-walk path (kept for safety).
                sb.AppendLine($"tag: {data.GetValueOrDefault("tag", "?")}");
                sb.AppendLine($"class: {data.GetValueOrDefault("class", "")}");
                sb.AppendLine($"display: {data.GetValueOrDefault("computed_display", "")}");
                sb.AppendLine();
                sb.AppendLine("ATTRIBUTES:");
                foreach (var kvp in data.Where(k => k.Key.StartsWith("attr_")).OrderBy(k => k.Key))
                    sb.AppendLine($"  {kvp.Key.Substring(5)}={kvp.Value}");
                sb.AppendLine();
                sb.AppendLine("TREE:");
                foreach (var kvp in data.Where(k => k.Key.StartsWith("line_")).OrderBy(k => k.Key))
                    sb.AppendLine(kvp.Value);
                sb.AppendLine();
                sb.AppendLine($"line_count: {data.GetValueOrDefault("line_count", "0")}");
            }
            // Use a resizable form with a multiline textbox so the user can copy
            // the full dump. MessageBox truncates and doesn't scroll.
            using var dialog = new Form
            {
                Text = "DOM element dump",
                Size = new System.Drawing.Size(720, 560),
                StartPosition = FormStartPosition.CenterParent
            };
            var box = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new System.Drawing.Font("Consolas", 9f),
                Text = sb.ToString(),
                AccessibleName = "Element dump"
            };
            dialog.Controls.Add(box);
            dialog.ShowDialog(this);
        }

        private void HandlePageHtml(Dictionary<string, string> data)
        {
            string rawHtml = data.GetValueOrDefault("html", "");
            if (!string.IsNullOrEmpty(rawHtml))
            {
                SavePageHtmlCapture(rawHtml);
            }
        }

        private void SavePageHtmlCapture(string rawHtml)
        {
            try
            {
                string stamp = DateTime.Now.ToString("HHmmss");
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"efb_captured_{stamp}.html");
                System.IO.File.WriteAllText(path, rawHtml);
                // Also write the latest-stable filename for ad-hoc checks.
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "efb_captured.html"), rawHtml);
                _announcer.Announce($"Page HTML captured: efb_captured_{stamp}.html, {rawHtml.Length} characters");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EFB] Capture save failed: {ex.Message}");
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

            if (buffer.All(c => c != null))
            {
                _htmlChunkBuffers.Remove(id);
                string fullHtml = string.Concat(buffer);
                SavePageHtmlCapture(fullHtml);
            }
        }

        private async Task InitializeWebViewAsync()
        {
            if (_webViewInitialized || displayWebView == null) return;
            try
            {
                await displayWebView.EnsureCoreWebView2Async();
                _webViewInitialized = true;
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
                displayWebView.Source = new Uri("http://localhost:19777/display");
        }

        private void HandleDisplayElements(Dictionary<string, string> data)
        {
            if (!data.TryGetValue("items", out string? itemsJson) || string.IsNullOrEmpty(itemsJson))
                return;
            _bridgeServer.UpdateDisplayElements(itemsJson);
            EnsureDisplayWebViewNavigated();
        }

        /// <summary>
        /// Hot-injects the display scanner + set_element_value handler into the
        /// running bridge. Kept so the Display tab keeps working until Phase 9.
        /// </summary>
        private void HotInjectBridgeUpdates()
        {
            // Body kept verbatim from pre-refactor form — Display tab still depends on it.
            string injectCode = HotInjectScript.Code;
            _bridgeServer.EnqueueCommand("eval_js", new Dictionary<string, string> { ["code"] = injectCode });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _connectionCheckTimer?.Stop();
            _connectionCheckTimer?.Dispose();
            _navigator.NavigationCompleted -= OnNavigationCompleted;
            _navigator.Dispose();
            _bridgeServer.StateUpdated -= OnStateUpdated;
            _bridgeServer.Error -= OnBridgeServerError;
            if (_previousWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_previousWindow);
            }
            base.OnFormClosing(e);
        }
    }
}
