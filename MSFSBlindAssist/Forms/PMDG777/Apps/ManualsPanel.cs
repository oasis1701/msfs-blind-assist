using System.Diagnostics;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Manuals panel. The tablet's Manuals page is just a QR code pointing to
    /// manuals.pmdg.com with no interactive content. We expose it as a single
    /// button that opens the URL in the user's default browser.
    /// </summary>
    public class ManualsPanel : EfbAppPanelBase
    {
        private const string ManualsUrl = "https://manuals.pmdg.com";

        private Button openManualsButton = null!;

        public override Control? InitialFocusControl => openManualsButton;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Manuals";

            var label = new Label
            {
                Text = "PMDG Document Hub",
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "PMDG Document Hub"
            };

            var info = new Label
            {
                Text = "All PMDG manuals are hosted at manuals.pmdg.com.\r\n" +
                       "Press the button below to open the manuals page in your default web browser.",
                Location = new System.Drawing.Point(10, 36),
                Size = new System.Drawing.Size(460, 50),
                AccessibleName = "All PMDG manuals are at manuals.pmdg.com. Press the button to open the manuals page in your default web browser."
            };

            openManualsButton = new Button
            {
                Text = "Open PMDG Manuals Page",
                Location = new System.Drawing.Point(10, 96),
                Size = new System.Drawing.Size(280, 32),
                AccessibleName = "Open PMDG Manuals Page in web browser",
                TabIndex = 0
            };
            openManualsButton.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ManualsUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Announcer.Announce("Could not open manuals page: " + ex.Message);
                }
            };

            Controls.AddRange(new Control[] { label, info, openManualsButton });
        }
    }
}
