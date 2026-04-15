namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    public class WeightsBalancePanel : EfbAppPanelBase
    {
        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Weights and Balance";

            var label = new Label
            {
                Text = "Weights and Balance — coming soon.\r\n\r\n" +
                       "This tab is deferred pending a field-level spec.",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 80),
                AccessibleName = "Weights and Balance not yet implemented"
            };

            var exploreButton = new Button
            {
                Text = "Capture Page HTML (diagnostic)",
                Location = new System.Drawing.Point(10, 100),
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
