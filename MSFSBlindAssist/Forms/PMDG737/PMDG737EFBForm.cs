using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Forms.PMDG777;
using MSFSBlindAssist.Forms.PMDG777.Apps;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG737
{
    /// <summary>
    /// Focused accessible EFB form for the PMDG 737-800. The 738 runs the same EFB
    /// app as the 777, so it reuses the 777 panel controls and navigator verbatim.
    /// First cut: Dashboard (SimBrief fetch + Send to FMC) and Preferences (incl.
    /// SimBrief alias + Navigraph sign-in). Performance/Ground Ops/etc. can be added
    /// later by hosting more of the existing app panels.
    /// </summary>
    public class PMDG737EFBForm : Form
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly EFBBridgeServer _bridgeServer;
        private readonly ScreenReaderAnnouncer _announcer;
        private readonly EfbAppNavigator _navigator;
        private readonly TabControl _tabs;
        private readonly TabPage _dashboardTab;
        private readonly TabPage _preferencesTab;
        private readonly Label _connectionStatus;
        private readonly DashboardPanel _dashboardPanel;
        private readonly PrefsPanel _prefsPanel;
        private readonly System.Windows.Forms.Timer _connectionCheckTimer;
        private IntPtr _previousWindow = IntPtr.Zero;
        private bool _wasConnected;

        public PMDG737EFBForm(EFBBridgeServer bridgeServer, ScreenReaderAnnouncer announcer)
        {
            _bridgeServer = bridgeServer;
            _announcer = announcer;
            _navigator = new EfbAppNavigator(bridgeServer);
            _navigator.NavigationCompleted += OnNavigationCompleted;

            Text = "PMDG 737 EFB";
            Width = 560;
            Height = 640;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            _connectionStatus = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                AccessibleName = "EFB connection status",
                Text = "Not connected — EFB tablet must be open in simulator"
            };

            _dashboardPanel = new DashboardPanel { Dock = DockStyle.Fill };
            _dashboardPanel.Initialize(bridgeServer, announcer);
            _prefsPanel = new PrefsPanel { Dock = DockStyle.Fill };
            _prefsPanel.Initialize(bridgeServer, announcer);

            _dashboardTab = new TabPage("&Dashboard");
            _dashboardTab.Controls.Add(_dashboardPanel);
            _preferencesTab = new TabPage("&Preferences");
            _preferencesTab.Controls.Add(_prefsPanel);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _tabs.TabPages.Add(_dashboardTab);
            _tabs.TabPages.Add(_preferencesTab);
            _tabs.SelectedIndexChanged += (_, _) => HandleTabChanged();

            // Add the Fill control first, then the Top-docked status label, so the
            // label takes the top edge and the tabs fill the remainder.
            Controls.Add(_tabs);
            Controls.Add(_connectionStatus);

            _bridgeServer.StateUpdated += OnStateUpdated;
            _bridgeServer.Error += OnBridgeError;
            KeyDown += OnFormKeyDown;

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
            _dashboardPanel.InitialFocusControl?.Focus();
        }

        private void HandleTabChanged()
        {
            _navigator.NavigateAsync(_tabs.SelectedTab == _preferencesTab
                ? EfbApp.Preferences : EfbApp.Dashboard);
        }

        private void OnNavigationCompleted(EfbApp app)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<EfbApp>(OnNavigationCompleted), app); } catch { }
                return;
            }
            GetActivePanel().OnActivated();
        }

        private EfbAppPanelBase GetActivePanel()
            => _tabs.SelectedTab == _preferencesTab ? _prefsPanel : _dashboardPanel;

        private void OnConnectionCheck(object? sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            bool connected = _bridgeServer.IsBridgeConnected;
            _connectionStatus.Text = connected
                ? "Connected"
                : "Not connected — EFB tablet must be open in simulator";

            if (connected && !_wasConnected)
            {
                _announcer.Announce("EFB bridge connected");
                _dashboardPanel.SetConnected(true);
                _prefsPanel.SetConnected(true);
                // Prime the bridge: cache preferences (so weight/unit formatting is
                // correct), replay any SimBrief OFP already on the tablet, and probe
                // Navigraph auth (onAuthStateChanged doesn't fire for already-signed-in).
                _bridgeServer.EnqueueCommand("get_preferences");
                _bridgeServer.EnqueueCommand("replay_simbrief");
                _bridgeServer.EnqueueCommand("check_navigraph_auth");
            }
            else if (!connected && _wasConnected)
            {
                _announcer.Announce("EFB bridge disconnected");
                _dashboardPanel.SetConnected(false);
                _prefsPanel.SetConnected(false);
            }
            _wasConnected = connected;
        }

        private void OnStateUpdated(object? sender, EFBStateUpdateEventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            // Panel-level state flows straight to each panel via their own
            // subscriptions; the form only mirrors "chrome" state. Keep the
            // preferences cache fresh so weight formatting is correct everywhere.
            if (e.Type == "preferences") PreferencesCache.Update(e.Data);
        }

        private void OnBridgeError(string message)
        {
            if (IsDisposed || !IsHandleCreated) return;
            _announcer.Announce(message);
        }

        private void OnFormKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _connectionCheckTimer?.Stop();
                _connectionCheckTimer?.Dispose();
                _bridgeServer.StateUpdated -= OnStateUpdated;
                _bridgeServer.Error -= OnBridgeError;
                _navigator.Dispose();
                if (_previousWindow != IntPtr.Zero) { try { SetForegroundWindow(_previousWindow); } catch { } }
            }
            base.Dispose(disposing);
        }
    }
}
