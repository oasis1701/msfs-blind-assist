using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Forms.PMDG777.Apps.Performance;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Performance Tool container. Hosts an inner AccessibleTabControl with
    /// sub-tabs for Takeoff, Landing Dispatch, and Landing Enroute. Each sub-tab
    /// is its own panel which drives the corresponding page on the tablet.
    /// </summary>
    public class PerformancePanel : EfbAppPanelBase
    {
        private AccessibleTabControl innerTabControl = null!;
        private TabPage takeoffSubTab = null!;
        private TabPage landingDispatchSubTab = null!;
        private TabPage landingEnrouteSubTab = null!;
        private TakeoffPanel takeoffPanel = null!;
        private LandingDispatchPanel landingDispatchPanel = null!;
        private LandingEnroutePanel landingEnroutePanel = null!;

        public override Control? InitialFocusControl => innerTabControl;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            AccessibleName = "Performance Tool";

            innerTabControl = new AccessibleTabControl { Dock = DockStyle.Fill };

            takeoffSubTab = new TabPage("Take Off") { Padding = new Padding(2) };
            landingDispatchSubTab = new TabPage("Landing Dispatch") { Padding = new Padding(2) };
            landingEnrouteSubTab = new TabPage("Landing Enroute") { Padding = new Padding(2) };

            takeoffPanel = new TakeoffPanel();
            takeoffPanel.Initialize(BridgeServer, Announcer);
            takeoffSubTab.Controls.Add(takeoffPanel);

            landingDispatchPanel = new LandingDispatchPanel();
            landingDispatchPanel.Initialize(BridgeServer, Announcer);
            landingDispatchSubTab.Controls.Add(landingDispatchPanel);

            landingEnroutePanel = new LandingEnroutePanel();
            landingEnroutePanel.Initialize(BridgeServer, Announcer);
            landingEnrouteSubTab.Controls.Add(landingEnroutePanel);

            innerTabControl.TabPages.Add(takeoffSubTab);
            innerTabControl.TabPages.Add(landingDispatchSubTab);
            innerTabControl.TabPages.Add(landingEnrouteSubTab);

            innerTabControl.SelectedIndexChanged += (_, _) => ActivateCurrentSubPanel();

            Controls.Add(innerTabControl);
        }

        public override void OnActivated()
        {
            // Outer Performance tab just became active — tablet is now showing
            // the Performance Tool app. Tell the currently-selected sub-panel
            // to drive the tablet to its specific sub-page and populate.
            ActivateCurrentSubPanel();
        }

        private void ActivateCurrentSubPanel()
        {
            GetActiveSubPanel()?.OnActivated();
        }

        private EfbAppPanelBase? GetActiveSubPanel()
        {
            if (innerTabControl.SelectedTab == takeoffSubTab) return takeoffPanel;
            if (innerTabControl.SelectedTab == landingDispatchSubTab) return landingDispatchPanel;
            if (innerTabControl.SelectedTab == landingEnrouteSubTab) return landingEnroutePanel;
            return null;
        }
    }
}
