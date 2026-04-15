namespace MSFSBlindAssist.Forms.PMDG777.Apps.Performance
{
    /// <summary>
    /// Placeholder for Phase 6 — Landing Dispatch. Discovery ids already
    /// captured (opt_landingdispatch_*); just needs the full panel rewrite.
    /// </summary>
    public class LandingDispatchPanel : EfbAppPanelBase
    {
        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Landing Dispatch";
            Controls.Add(new Label
            {
                Text = "Landing Dispatch — coming in Phase 6.",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 40),
                AccessibleName = "Landing Dispatch not yet implemented"
            });
        }
    }
}
