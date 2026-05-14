using MSFSBlindAssist.Forms.PMDG777.Apps;

namespace MSFSBlindAssist.Forms.PMDG777
{
    partial class PMDG777EFBForm
    {
        private System.ComponentModel.IContainer? components = null;

        // Outer nested tab control
        private Controls.AccessibleTabControl? outerTabControl;
        private TabPage? efbTab;
        private TabPage? performanceTab;
        private TabPage? groundOpsTab;
        private TabPage? weightsBalanceTab;
        private TabPage? manualsTab;
        private TabPage? displayTab;

        // Inner Electronic Flight Bag tab control
        private Controls.AccessibleTabControl? efbInnerTabControl;
        private TabPage? dashboardSubTab;
        private TabPage? preferencesSubTab;
        private TabPage? navdataSubTab;

        // App panels
        private DashboardPanel? dashboardPanel;
        private PrefsPanel? prefsPanel;
        private NavdataPanel? navdataPanel;
        private PerformancePanel? performancePanel;
        private GroundOpsPanel? groundOpsPanel;
        private WeightsBalancePanel? weightsBalancePanel;
        private ManualsPanel? manualsPanel;

        // Display tab (WebView2 debug)
        private Microsoft.Web.WebView2.WinForms.WebView2? displayWebView;
        private Button? displayRefreshButton;

        // Top bar
        private TextBox? connectionStatusText;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.Text = "PMDG 777 EFB";
            this.Size = new System.Drawing.Size(560, 620);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.AccessibleName = "PMDG 777 EFB";

            // === Outer tab control ===
            outerTabControl = new Controls.AccessibleTabControl { Dock = DockStyle.Fill };

            efbTab = new TabPage("Electronic Flight Bag") { Padding = new Padding(2) };
            performanceTab = new TabPage("Performance Tool") { Padding = new Padding(2) };
            groundOpsTab = new TabPage("Ground Operations") { Padding = new Padding(2) };
            weightsBalanceTab = new TabPage("Weights and Balance") { Padding = new Padding(2) };
            manualsTab = new TabPage("Manuals") { Padding = new Padding(2) };
            displayTab = new TabPage("Display") { Padding = new Padding(10) };

            // === Inner EFB tab control ===
            efbInnerTabControl = new Controls.AccessibleTabControl { Dock = DockStyle.Fill };

            dashboardSubTab = new TabPage("Dashboard") { Padding = new Padding(2) };
            preferencesSubTab = new TabPage("Preferences") { Padding = new Padding(2) };
            navdataSubTab = new TabPage("Navigation Data") { Padding = new Padding(2) };

            dashboardPanel = new DashboardPanel();
            dashboardPanel.Initialize(_bridgeServer, _announcer);
            dashboardSubTab.Controls.Add(dashboardPanel);

            prefsPanel = new PrefsPanel();
            prefsPanel.Initialize(_bridgeServer, _announcer);
            preferencesSubTab.Controls.Add(prefsPanel);

            navdataPanel = new NavdataPanel();
            navdataPanel.Initialize(_bridgeServer, _announcer);
            navdataSubTab.Controls.Add(navdataPanel);

            efbInnerTabControl.TabPages.Add(dashboardSubTab);
            efbInnerTabControl.TabPages.Add(preferencesSubTab);
            efbInnerTabControl.TabPages.Add(navdataSubTab);
            efbTab.Controls.Add(efbInnerTabControl);

            // Performance / Ground Ops / W&B / Manuals — single placeholder panels
            performancePanel = new PerformancePanel();
            performancePanel.Initialize(_bridgeServer, _announcer);
            performanceTab.Controls.Add(performancePanel);

            groundOpsPanel = new GroundOpsPanel();
            groundOpsPanel.Initialize(_bridgeServer, _announcer);
            groundOpsTab.Controls.Add(groundOpsPanel);

            weightsBalancePanel = new WeightsBalancePanel();
            weightsBalancePanel.Initialize(_bridgeServer, _announcer);
            weightsBalanceTab.Controls.Add(weightsBalancePanel);

            manualsPanel = new ManualsPanel();
            manualsPanel.Initialize(_bridgeServer, _announcer);
            manualsTab.Controls.Add(manualsPanel);

            // Display tab — WebView2 loading accessible page from local server
            displayWebView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(520, 470),
                AccessibleName = "EFB Display",
                AccessibleDescription = "EFB tablet display. Tab to navigate items. Enter to click. F5 to refresh."
            };
            displayRefreshButton = new Button
            {
                Text = "Refresh Display (F5)",
                Location = new System.Drawing.Point(10, 485),
                Size = new System.Drawing.Size(160, 30),
                AccessibleName = "Refresh EFB Display"
            };
            displayTab.Controls.Add(displayWebView);
            displayTab.Controls.Add(displayRefreshButton);

            outerTabControl.TabPages.Add(efbTab);
            outerTabControl.TabPages.Add(performanceTab);
            outerTabControl.TabPages.Add(groundOpsTab);
            outerTabControl.TabPages.Add(weightsBalanceTab);
            outerTabControl.TabPages.Add(manualsTab);
            outerTabControl.TabPages.Add(displayTab);

            this.Controls.Add(outerTabControl);

            connectionStatusText = new TextBox
            {
                Text = "Not connected",
                Dock = DockStyle.Top,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.SystemColors.Control,
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Connection Status",
                TabIndex = 0,
                Height = 25
            };
            this.Controls.Add(connectionStatusText);
        }
    }
}
