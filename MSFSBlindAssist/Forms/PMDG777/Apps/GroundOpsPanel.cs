namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    public class GroundOpsPanel : EfbAppPanelBase
    {
        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Ground Operations";

            var label = new Label
            {
                Text = "Ground Operations — coming soon.\r\n\r\n" +
                       "This tab will expose Ground Connections, Service Vehicles, Door Management, " +
                       "Ground Maintenance, and Automated Ground Ops in accessible form.",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 100),
                AccessibleName = "Ground Operations not yet implemented"
            };

            var exploreButton = new Button
            {
                Text = "Capture Page HTML (diagnostic)",
                Location = new System.Drawing.Point(10, 120),
                Size = new System.Drawing.Size(220, 30),
                AccessibleName = "Capture Page HTML"
            };
            exploreButton.Click += (_, _) =>
            {
                BridgeServer.EnqueueCommand("get_page_html");
            };

            Controls.AddRange(new Control[] { label, exploreButton });
        }
    }
}
