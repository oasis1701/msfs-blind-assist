namespace MSFSBlindAssist
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.MenuStrip menuStrip = null!;
        private System.Windows.Forms.ToolStripMenuItem fileMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem databaseSettingsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem announcementSettingsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem geoNamesSettingsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem simbriefSettingsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem geminiApiKeySettingsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem handFlyOptionsMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem hotkeyListMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem updateApplicationMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem aboutMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem aircraftMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem flyByWireA320MenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem fenixA320MenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem taxiMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem taxiSelectAirportMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem taxiStartGuidanceMenuItem = null!;
        private System.Windows.Forms.ToolStripMenuItem taxiStopGuidanceMenuItem = null!;
        private System.Windows.Forms.ListBox sectionsListBox = null!;
        private System.Windows.Forms.ListBox panelsListBox = null!;
        private System.Windows.Forms.Panel controlsContainer = null!;
        private System.Windows.Forms.Label statusLabel = null!;

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
            this.menuStrip = new System.Windows.Forms.MenuStrip();
            this.fileMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.databaseSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.announcementSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.geoNamesSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.simbriefSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.geminiApiKeySettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.handFlyOptionsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hotkeyListMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.updateApplicationMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aircraftMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.flyByWireA320MenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.fenixA320MenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.taxiMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.taxiSelectAirportMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.taxiStartGuidanceMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.taxiStopGuidanceMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.sectionsListBox = new System.Windows.Forms.ListBox();
            this.panelsListBox = new System.Windows.Forms.ListBox();
            this.controlsContainer = new System.Windows.Forms.Panel();
            this.statusLabel = new System.Windows.Forms.Label();
            this.menuStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // menuStrip
            //
            this.menuStrip.AccessibleName = "Main menu";
            this.menuStrip.AccessibleDescription = "Main application menu";
            this.menuStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.fileMenuItem,
            this.aircraftMenuItem,
            this.taxiMenuItem});
            this.menuStrip.Location = new System.Drawing.Point(0, 0);
            this.menuStrip.Name = "menuStrip";
            this.menuStrip.Size = new System.Drawing.Size(870, 28);
            this.menuStrip.TabIndex = 0;
            this.menuStrip.Text = "menuStrip";
            //
            // fileMenuItem
            //
            this.fileMenuItem.AccessibleName = "File menu";
            this.fileMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.databaseSettingsMenuItem,
            this.announcementSettingsMenuItem,
            this.geoNamesSettingsMenuItem,
            this.simbriefSettingsMenuItem,
            this.geminiApiKeySettingsMenuItem,
            this.handFlyOptionsMenuItem,
            this.hotkeyListMenuItem,
            this.updateApplicationMenuItem,
            this.aboutMenuItem});
            this.fileMenuItem.Name = "fileMenuItem";
            this.fileMenuItem.Size = new System.Drawing.Size(46, 24);
            this.fileMenuItem.Text = "&File";
            //
            // databaseSettingsMenuItem
            //
            this.databaseSettingsMenuItem.AccessibleName = "Database Settings";
            this.databaseSettingsMenuItem.AccessibleDescription = "Configure database provider and paths for FS2020 and FS2024";
            this.databaseSettingsMenuItem.Name = "databaseSettingsMenuItem";
            this.databaseSettingsMenuItem.Size = new System.Drawing.Size(220, 26);
            this.databaseSettingsMenuItem.Text = "&Database Settings";
            this.databaseSettingsMenuItem.Click += new System.EventHandler(this.DatabaseSettingsMenuItem_Click);
            //
            // announcementSettingsMenuItem
            //
            this.announcementSettingsMenuItem.AccessibleName = "Announcement Settings";
            this.announcementSettingsMenuItem.AccessibleDescription = "Configure how aircraft state announcements are delivered";
            this.announcementSettingsMenuItem.Name = "announcementSettingsMenuItem";
            this.announcementSettingsMenuItem.Size = new System.Drawing.Size(220, 26);
            this.announcementSettingsMenuItem.Text = "&Announcement Settings";
            this.announcementSettingsMenuItem.Click += new System.EventHandler(this.AnnouncementSettingsMenuItem_Click);
            //
            // geoNamesSettingsMenuItem
            //
            this.geoNamesSettingsMenuItem.AccessibleName = "GeoNames Settings";
            this.geoNamesSettingsMenuItem.AccessibleDescription = "Configure GeoNames API key and location information settings";
            this.geoNamesSettingsMenuItem.Name = "geoNamesSettingsMenuItem";
            this.geoNamesSettingsMenuItem.Size = new System.Drawing.Size(220, 26);
            this.geoNamesSettingsMenuItem.Text = "Define &GeoNames API Key";
            this.geoNamesSettingsMenuItem.Click += new System.EventHandler(this.GeoNamesSettingsMenuItem_Click);
            //
            // simbriefSettingsMenuItem
            //
            this.simbriefSettingsMenuItem.AccessibleName = "SimBrief Settings";
            this.simbriefSettingsMenuItem.AccessibleDescription = "Configure SimBrief username for flight plan integration";
            this.simbriefSettingsMenuItem.Name = "simbriefSettingsMenuItem";
            this.simbriefSettingsMenuItem.Size = new System.Drawing.Size(220, 26);
            this.simbriefSettingsMenuItem.Text = "Define &SimBrief Username";
            this.simbriefSettingsMenuItem.Click += new System.EventHandler(this.SimBriefSettingsMenuItem_Click);
            //
            // geminiApiKeySettingsMenuItem
            //
            this.geminiApiKeySettingsMenuItem.AccessibleName = "Gemini API Key Settings";
            this.geminiApiKeySettingsMenuItem.AccessibleDescription = "Configure Google Gemini API key for AI-powered display reading";
            this.geminiApiKeySettingsMenuItem.Name = "geminiApiKeySettingsMenuItem";
            this.geminiApiKeySettingsMenuItem.Size = new System.Drawing.Size(280, 26);
            this.geminiApiKeySettingsMenuItem.Text = "Ge&mini API Key Settings";
            this.geminiApiKeySettingsMenuItem.Click += new System.EventHandler(this.GeminiApiKeySettingsMenuItem_Click);
            //
            // handFlyOptionsMenuItem
            //
            this.handFlyOptionsMenuItem.AccessibleName = "Hand Fly Options";
            this.handFlyOptionsMenuItem.AccessibleDescription = "Configure hand fly mode audio tones and announcement settings";
            this.handFlyOptionsMenuItem.Name = "handFlyOptionsMenuItem";
            this.handFlyOptionsMenuItem.Size = new System.Drawing.Size(280, 26);
            this.handFlyOptionsMenuItem.Text = "&Hand Fly Options";
            this.handFlyOptionsMenuItem.Click += new System.EventHandler(this.HandFlyOptionsMenuItem_Click);
            //
            // hotkeyListMenuItem
            //
            this.hotkeyListMenuItem.AccessibleName = "Hotkey List";
            this.hotkeyListMenuItem.AccessibleDescription = "View complete list of all available hotkeys";
            this.hotkeyListMenuItem.Name = "hotkeyListMenuItem";
            this.hotkeyListMenuItem.Size = new System.Drawing.Size(220, 26);
            this.hotkeyListMenuItem.Text = "&Hotkey List";
            this.hotkeyListMenuItem.Click += new System.EventHandler(this.HotkeyListMenuItem_Click);
            //
            // updateApplicationMenuItem
            //
            this.updateApplicationMenuItem.AccessibleName = "Update Application";
            this.updateApplicationMenuItem.AccessibleDescription = "Check for and install application updates from GitHub";
            this.updateApplicationMenuItem.Name = "updateApplicationMenuItem";
            this.updateApplicationMenuItem.Size = new System.Drawing.Size(220, 26);
            this.updateApplicationMenuItem.Text = "&Update Application";
            this.updateApplicationMenuItem.Click += new System.EventHandler(this.UpdateApplicationMenuItem_Click);
            //
            // aboutMenuItem
            //
            this.aboutMenuItem.AccessibleName = "About";
            this.aboutMenuItem.AccessibleDescription = "Show application version and information";
            this.aboutMenuItem.Name = "aboutMenuItem";
            this.aboutMenuItem.Size = new System.Drawing.Size(220, 26);
            this.aboutMenuItem.Text = "&About";
            this.aboutMenuItem.Click += new System.EventHandler(this.AboutMenuItem_Click);
            //
            // aircraftMenuItem
            //
            this.aircraftMenuItem.AccessibleName = "Aircraft menu";
            this.aircraftMenuItem.AccessibleDescription = "Select aircraft model";
            this.aircraftMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.flyByWireA320MenuItem,
            this.fenixA320MenuItem});
            this.aircraftMenuItem.Name = "aircraftMenuItem";
            this.aircraftMenuItem.Size = new System.Drawing.Size(75, 24);
            this.aircraftMenuItem.Text = "&Aircraft";
            //
            // flyByWireA320MenuItem
            //
            this.flyByWireA320MenuItem.AccessibleName = "FlyByWire Airbus A320neo";
            this.flyByWireA320MenuItem.AccessibleDescription = "Switch to FlyByWire Airbus A320neo";
            this.flyByWireA320MenuItem.Name = "flyByWireA320MenuItem";
            this.flyByWireA320MenuItem.Size = new System.Drawing.Size(240, 26);
            this.flyByWireA320MenuItem.Text = "FlyByWire Airbus &A320neo";
            this.flyByWireA320MenuItem.Checked = true;
            this.flyByWireA320MenuItem.Click += new System.EventHandler(this.FlyByWireA320MenuItem_Click);
            //
            // fenixA320MenuItem
            //
            this.fenixA320MenuItem.AccessibleName = "Fenix A320 CEO";
            this.fenixA320MenuItem.AccessibleDescription = "Switch to Fenix A320 CEO";
            this.fenixA320MenuItem.Name = "fenixA320MenuItem";
            this.fenixA320MenuItem.Size = new System.Drawing.Size(240, 26);
            this.fenixA320MenuItem.Text = "Fenix A320 &CEO";
            this.fenixA320MenuItem.Checked = false;
            this.fenixA320MenuItem.Click += new System.EventHandler(this.FenixA320MenuItem_Click);
            //
            // taxiMenuItem
            //
            this.taxiMenuItem.AccessibleName = "Taxi menu";
            this.taxiMenuItem.AccessibleDescription = "Taxiway guidance options";
            this.taxiMenuItem.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.taxiSelectAirportMenuItem,
            this.taxiStartGuidanceMenuItem,
            this.taxiStopGuidanceMenuItem});
            this.taxiMenuItem.Name = "taxiMenuItem";
            this.taxiMenuItem.Size = new System.Drawing.Size(46, 24);
            this.taxiMenuItem.Text = "&Taxi";
            //
            // taxiSelectAirportMenuItem
            //
            this.taxiSelectAirportMenuItem.AccessibleName = "Select Airport";
            this.taxiSelectAirportMenuItem.AccessibleDescription = "Select an airport for taxiway guidance";
            this.taxiSelectAirportMenuItem.Name = "taxiSelectAirportMenuItem";
            this.taxiSelectAirportMenuItem.Size = new System.Drawing.Size(180, 26);
            this.taxiSelectAirportMenuItem.Text = "Select &Airport...";
            this.taxiSelectAirportMenuItem.Click += new System.EventHandler(this.TaxiSelectAirportMenuItem_Click);
            //
            // taxiStartGuidanceMenuItem
            //
            this.taxiStartGuidanceMenuItem.AccessibleName = "Start Guidance";
            this.taxiStartGuidanceMenuItem.AccessibleDescription = "Start taxiway guidance";
            this.taxiStartGuidanceMenuItem.Name = "taxiStartGuidanceMenuItem";
            this.taxiStartGuidanceMenuItem.Size = new System.Drawing.Size(180, 26);
            this.taxiStartGuidanceMenuItem.Text = "&Start Guidance";
            this.taxiStartGuidanceMenuItem.Enabled = false;
            this.taxiStartGuidanceMenuItem.Click += new System.EventHandler(this.TaxiStartGuidanceMenuItem_Click);
            //
            // taxiStopGuidanceMenuItem
            //
            this.taxiStopGuidanceMenuItem.AccessibleName = "Stop Guidance";
            this.taxiStopGuidanceMenuItem.AccessibleDescription = "Stop taxiway guidance";
            this.taxiStopGuidanceMenuItem.Name = "taxiStopGuidanceMenuItem";
            this.taxiStopGuidanceMenuItem.Size = new System.Drawing.Size(180, 26);
            this.taxiStopGuidanceMenuItem.Text = "S&top Guidance";
            this.taxiStopGuidanceMenuItem.Enabled = false;
            this.taxiStopGuidanceMenuItem.Click += new System.EventHandler(this.TaxiStopGuidanceMenuItem_Click);
            //
            // sectionsListBox
            // 
            this.sectionsListBox.AccessibleName = "Aircraft sections";
            this.sectionsListBox.AccessibleDescription = "Select aircraft section with arrow keys";
            this.sectionsListBox.FormattingEnabled = true;
            this.sectionsListBox.ItemHeight = 16;
            this.sectionsListBox.Location = new System.Drawing.Point(12, 65);
            this.sectionsListBox.Name = "sectionsListBox";
            this.sectionsListBox.Size = new System.Drawing.Size(180, 400);
            this.sectionsListBox.TabIndex = 0;
            this.sectionsListBox.SelectedIndexChanged += new System.EventHandler(this.SectionsListBox_SelectedIndexChanged);
            // 
            // panelsListBox
            // 
            this.panelsListBox.AccessibleName = "Panel list";
            this.panelsListBox.AccessibleDescription = "Select panel with arrow keys";
            this.panelsListBox.FormattingEnabled = true;
            this.panelsListBox.ItemHeight = 16;
            this.panelsListBox.Location = new System.Drawing.Point(210, 65);
            this.panelsListBox.Name = "panelsListBox";
            this.panelsListBox.Size = new System.Drawing.Size(180, 400);
            this.panelsListBox.TabIndex = 1;
            this.panelsListBox.SelectedIndexChanged += new System.EventHandler(this.PanelsListBox_SelectedIndexChanged);
            // 
            // controlsContainer
            // 
            this.controlsContainer.AutoScroll = true;
            this.controlsContainer.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.controlsContainer.Location = new System.Drawing.Point(410, 65);
            this.controlsContainer.Name = "controlsContainer";
            this.controlsContainer.Size = new System.Drawing.Size(440, 400);
            this.controlsContainer.TabIndex = 2;
            this.controlsContainer.TabStop = false; // Don't allow tab stop on the container
            // 
            // statusLabel
            // 
            this.statusLabel.AccessibleName = "Connection status";
            this.statusLabel.AutoSize = true;
            this.statusLabel.Location = new System.Drawing.Point(12, 35);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Size = new System.Drawing.Size(140, 17);
            this.statusLabel.TabIndex = 3;
            this.statusLabel.Text = "Waiting for simulator";
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(870, 460);
            this.Controls.Add(this.statusLabel);
            this.Controls.Add(this.controlsContainer);
            this.Controls.Add(this.panelsListBox);
            this.Controls.Add(this.sectionsListBox);
            this.Controls.Add(this.menuStrip);
            this.MainMenuStrip = this.menuStrip;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "FBW A320";
            this.menuStrip.ResumeLayout(false);
            this.menuStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
