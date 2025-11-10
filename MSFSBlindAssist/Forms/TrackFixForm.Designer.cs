namespace MSFSBlindAssist.Forms
{
    partial class TrackFixForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            // Search mode controls
            waypointLabel = new Label();
            waypointTextBox = new TextBox();
            slotLabel = new Label();
            slotComboBox = new ComboBox();
            trackButton = new Button();

            // Duplicate resolution controls
            duplicateLabel = new Label();
            duplicateListView = new ListView();
            selectButton = new Button();

            SuspendLayout();

            //
            // waypointLabel
            //
            waypointLabel.AutoSize = true;
            waypointLabel.Location = new Point(20, 20);
            waypointLabel.Name = "waypointLabel";
            waypointLabel.Size = new Size(120, 15);
            waypointLabel.TabIndex = 0;
            waypointLabel.Text = "Waypoint Name:";

            //
            // waypointTextBox
            //
            waypointTextBox.Location = new Point(20, 45);
            waypointTextBox.Name = "waypointTextBox";
            waypointTextBox.Size = new Size(340, 23);
            waypointTextBox.TabIndex = 1;
            waypointTextBox.AccessibleName = "Waypoint Name";

            //
            // slotLabel
            //
            slotLabel.AutoSize = true;
            slotLabel.Location = new Point(20, 80);
            slotLabel.Name = "slotLabel";
            slotLabel.Size = new Size(100, 15);
            slotLabel.TabIndex = 2;
            slotLabel.Text = "Tracking Slot:";

            //
            // slotComboBox
            //
            slotComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            slotComboBox.FormattingEnabled = true;
            slotComboBox.Items.AddRange(new object[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" });
            slotComboBox.Location = new Point(20, 105);
            slotComboBox.Name = "slotComboBox";
            slotComboBox.Size = new Size(340, 23);
            slotComboBox.TabIndex = 3;
            slotComboBox.AccessibleName = "Tracking Slot";
            slotComboBox.SelectedIndex = 0;

            //
            // trackButton
            //
            trackButton.Location = new Point(20, 145);
            trackButton.Name = "trackButton";
            trackButton.Size = new Size(340, 30);
            trackButton.TabIndex = 4;
            trackButton.Text = "Track";
            trackButton.UseVisualStyleBackColor = true;
            trackButton.Click += TrackButton_Click;

            //
            // duplicateLabel
            //
            duplicateLabel.AutoSize = true;
            duplicateLabel.Location = new Point(20, 20);
            duplicateLabel.Name = "duplicateLabel";
            duplicateLabel.Size = new Size(300, 15);
            duplicateLabel.TabIndex = 5;
            duplicateLabel.Text = "Multiple waypoints found. Select one:";
            duplicateLabel.Visible = false;

            //
            // duplicateListView
            //
            duplicateListView.FullRowSelect = true;
            duplicateListView.Location = new Point(20, 45);
            duplicateListView.MultiSelect = false;
            duplicateListView.Name = "duplicateListView";
            duplicateListView.Size = new Size(540, 300);
            duplicateListView.TabIndex = 6;
            duplicateListView.UseCompatibleStateImageBehavior = false;
            duplicateListView.View = View.Details;
            duplicateListView.AccessibleName = "Waypoint List";
            duplicateListView.HeaderStyle = ColumnHeaderStyle.None;
            duplicateListView.Columns.Add("", 520);
            duplicateListView.Visible = false;
            duplicateListView.KeyDown += DuplicateListView_KeyDown;

            //
            // selectButton
            //
            selectButton.Location = new Point(20, 360);
            selectButton.Name = "selectButton";
            selectButton.Size = new Size(540, 30);
            selectButton.TabIndex = 7;
            selectButton.Text = "Select";
            selectButton.UseVisualStyleBackColor = true;
            selectButton.Visible = false;
            selectButton.Click += SelectButton_Click;

            //
            // TrackFixForm
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(384, 211);
            Controls.Add(selectButton);
            Controls.Add(duplicateListView);
            Controls.Add(duplicateLabel);
            Controls.Add(trackButton);
            Controls.Add(slotComboBox);
            Controls.Add(slotLabel);
            Controls.Add(waypointTextBox);
            Controls.Add(waypointLabel);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            Name = "TrackFixForm";
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Track Fix Window";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label waypointLabel;
        private TextBox waypointTextBox;
        private Label slotLabel;
        private ComboBox slotComboBox;
        private Button trackButton;
        private Label duplicateLabel;
        private ListView duplicateListView;
        private Button selectButton;
    }
}
