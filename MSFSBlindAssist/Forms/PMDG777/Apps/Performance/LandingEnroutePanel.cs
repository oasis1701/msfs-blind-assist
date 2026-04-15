namespace MSFSBlindAssist.Forms.PMDG777.Apps.Performance
{
    /// <summary>
    /// Placeholder for Phase 6 — Landing Enroute. Discovery ids already
    /// captured (opt_landingenroute_*); just needs the full panel rewrite.
    /// </summary>
    public class LandingEnroutePanel : EfbAppPanelBase
    {
        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Landing Enroute";
            Controls.Add(new Label
            {
                Text = "Landing Enroute — coming in Phase 6.",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 40),
                AccessibleName = "Landing Enroute not yet implemented"
            });
        }
    }
}
