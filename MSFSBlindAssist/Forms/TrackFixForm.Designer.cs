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
            crossingAltLabel = new Label();
            crossingAltTextBox = new TextBox();
            constraintLabel = new Label();
            constraintComboBox = new ComboBox();
            upperAltLabel = new Label();
            upperAltTextBox = new TextBox();
            courseLabel = new Label();
            courseTextBox = new TextBox();
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
            // crossingAltLabel
            //
            crossingAltLabel.AutoSize = true;
            crossingAltLabel.Location = new Point(20, 140);
            crossingAltLabel.Name = "crossingAltLabel";
            crossingAltLabel.Size = new Size(250, 15);
            crossingAltLabel.TabIndex = 4;
            crossingAltLabel.Text = "Crossing Altitude (feet MSL, optional):";

            //
            // crossingAltTextBox
            //
            crossingAltTextBox.Location = new Point(20, 165);
            crossingAltTextBox.Name = "crossingAltTextBox";
            crossingAltTextBox.Size = new Size(340, 23);
            crossingAltTextBox.TabIndex = 5;
            crossingAltTextBox.AccessibleName = "Crossing Altitude in feet, optional, for the Flight Director vertical guidance";

            //
            // constraintLabel
            //
            constraintLabel.AutoSize = true;
            constraintLabel.Location = new Point(20, 200);
            constraintLabel.Name = "constraintLabel";
            constraintLabel.Size = new Size(150, 15);
            constraintLabel.TabIndex = 6;
            constraintLabel.Text = "Altitude Constraint:";

            //
            // constraintComboBox
            //
            constraintComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            constraintComboBox.FormattingEnabled = true;
            constraintComboBox.Items.AddRange(new object[] { "None", "At", "At or above", "At or below", "Between" });
            constraintComboBox.Location = new Point(20, 225);
            constraintComboBox.Name = "constraintComboBox";
            constraintComboBox.Size = new Size(340, 23);
            constraintComboBox.TabIndex = 7;
            constraintComboBox.AccessibleName = "Altitude Constraint type";
            constraintComboBox.SelectedIndex = 0;

            //
            // upperAltLabel
            //
            upperAltLabel.AutoSize = true;
            upperAltLabel.Location = new Point(20, 260);
            upperAltLabel.Name = "upperAltLabel";
            upperAltLabel.Size = new Size(280, 15);
            upperAltLabel.TabIndex = 8;
            upperAltLabel.Text = "Upper Altitude (feet MSL, for Between):";

            //
            // upperAltTextBox
            //
            upperAltTextBox.Location = new Point(20, 285);
            upperAltTextBox.Name = "upperAltTextBox";
            upperAltTextBox.Size = new Size(340, 23);
            upperAltTextBox.TabIndex = 9;
            upperAltTextBox.AccessibleName = "Upper Altitude in feet, used only for the Between constraint";

            //
            // courseLabel
            //
            courseLabel.AutoSize = true;
            courseLabel.Location = new Point(20, 320);
            courseLabel.Name = "courseLabel";
            courseLabel.Size = new Size(280, 15);
            courseLabel.TabIndex = 10;
            courseLabel.Text = "Course to track through fix (magnetic, optional):";

            //
            // courseTextBox
            //
            courseTextBox.Location = new Point(20, 345);
            courseTextBox.Name = "courseTextBox";
            courseTextBox.Size = new Size(340, 23);
            courseTextBox.TabIndex = 11;
            courseTextBox.AccessibleName = "Course to track through the fix in magnetic degrees, optional. Leave blank to fly direct to the fix; set it to capture and hold this course or radial through the fix.";

            //
            // trackButton
            //
            trackButton.Location = new Point(20, 385);
            trackButton.Name = "trackButton";
            trackButton.Size = new Size(340, 30);
            trackButton.TabIndex = 12;
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
            ClientSize = new Size(384, 435);
            Controls.Add(selectButton);
            Controls.Add(duplicateListView);
            Controls.Add(duplicateLabel);
            Controls.Add(trackButton);
            Controls.Add(courseTextBox);
            Controls.Add(courseLabel);
            Controls.Add(upperAltTextBox);
            Controls.Add(upperAltLabel);
            Controls.Add(constraintComboBox);
            Controls.Add(constraintLabel);
            Controls.Add(crossingAltTextBox);
            Controls.Add(crossingAltLabel);
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
        private Label crossingAltLabel;
        private TextBox crossingAltTextBox;
        private Label constraintLabel;
        private ComboBox constraintComboBox;
        private Label upperAltLabel;
        private TextBox upperAltTextBox;
        private Label courseLabel;
        private TextBox courseTextBox;
        private Button trackButton;
        private Label duplicateLabel;
        private ListView duplicateListView;
        private Button selectButton;
    }
}
