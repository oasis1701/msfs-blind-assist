namespace FBWBA
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.MenuStrip menuStrip;
        private System.Windows.Forms.ToolStripMenuItem fileMenuItem;
        private System.Windows.Forms.ToolStripMenuItem buildDatabaseMenuItem;
        private System.Windows.Forms.ToolStripMenuItem announcementSettingsMenuItem;
        private System.Windows.Forms.ToolStripMenuItem geoNamesSettingsMenuItem;
        private System.Windows.Forms.ToolStripMenuItem hotkeyListMenuItem;
        private System.Windows.Forms.ToolStripMenuItem aboutMenuItem;
        private System.Windows.Forms.ListBox sectionsListBox;
        private System.Windows.Forms.ListBox panelsListBox;
        private System.Windows.Forms.Panel controlsContainer;
        private System.Windows.Forms.Label statusLabel;

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
            this.buildDatabaseMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.announcementSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.geoNamesSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.hotkeyListMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.aboutMenuItem = new System.Windows.Forms.ToolStripMenuItem();
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
            this.fileMenuItem});
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
            this.buildDatabaseMenuItem,
            this.announcementSettingsMenuItem,
            this.geoNamesSettingsMenuItem,
            this.hotkeyListMenuItem,
            this.aboutMenuItem});
            this.fileMenuItem.Name = "fileMenuItem";
            this.fileMenuItem.Size = new System.Drawing.Size(46, 24);
            this.fileMenuItem.Text = "&File";
            //
            // buildDatabaseMenuItem
            //
            this.buildDatabaseMenuItem.AccessibleName = "Build Airport Database";
            this.buildDatabaseMenuItem.AccessibleDescription = "Build airport database from MakeRwys files";
            this.buildDatabaseMenuItem.Name = "buildDatabaseMenuItem";
            this.buildDatabaseMenuItem.Size = new System.Drawing.Size(220, 26);
            this.buildDatabaseMenuItem.Text = "&Build Airport Database";
            this.buildDatabaseMenuItem.Click += new System.EventHandler(this.BuildDatabaseMenuItem_Click);
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
            // hotkeyListMenuItem
            //
            this.hotkeyListMenuItem.AccessibleName = "Hotkey List";
            this.hotkeyListMenuItem.AccessibleDescription = "View complete list of all available hotkeys";
            this.hotkeyListMenuItem.Name = "hotkeyListMenuItem";
            this.hotkeyListMenuItem.Size = new System.Drawing.Size(220, 26);
            this.hotkeyListMenuItem.Text = "&Hotkey List";
            this.hotkeyListMenuItem.Click += new System.EventHandler(this.HotkeyListMenuItem_Click);
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
