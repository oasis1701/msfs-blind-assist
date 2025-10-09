namespace FBWBA.Forms
{
    partial class DatabaseProgressForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.Label titleLabel;
        private System.Windows.Forms.TextBox progressLabel;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Button closeButton;

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
            this.titleLabel = new System.Windows.Forms.Label();
            this.progressLabel = new System.Windows.Forms.TextBox();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.closeButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // titleLabel
            //
            this.titleLabel.AccessibleName = "Database Build Title";
            this.titleLabel.AutoSize = true;
            this.titleLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Bold);
            this.titleLabel.Location = new System.Drawing.Point(12, 15);
            this.titleLabel.Name = "titleLabel";
            this.titleLabel.Size = new System.Drawing.Size(198, 20);
            this.titleLabel.TabIndex = 0;
            this.titleLabel.Text = "Building Airport Database";
            //
            // progressLabel
            //
            this.progressLabel.AccessibleName = "Progress Status";
            this.progressLabel.AccessibleDescription = "Current status of database building process";
            this.progressLabel.Location = new System.Drawing.Point(12, 50);
            this.progressLabel.Multiline = true;
            this.progressLabel.Name = "progressLabel";
            this.progressLabel.ReadOnly = true;
            this.progressLabel.Size = new System.Drawing.Size(460, 20);
            this.progressLabel.TabIndex = 1;
            this.progressLabel.TabStop = true;
            this.progressLabel.Text = "Initializing database...";
            //
            // progressBar
            //
            this.progressBar.AccessibleName = "Progress Bar";
            this.progressBar.AccessibleDescription = "Visual progress indicator for database building";
            this.progressBar.Location = new System.Drawing.Point(12, 80);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(460, 23);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Marquee;
            this.progressBar.TabIndex = 2;
            this.progressBar.MarqueeAnimationSpeed = 50;
            //
            // closeButton
            //
            this.closeButton.AccessibleName = "Close Dialog";
            this.closeButton.AccessibleDescription = "Close the database progress dialog";
            this.closeButton.Enabled = false;
            this.closeButton.Location = new System.Drawing.Point(397, 120);
            this.closeButton.Name = "closeButton";
            this.closeButton.Size = new System.Drawing.Size(75, 23);
            this.closeButton.TabIndex = 2;
            this.closeButton.TabStop = true;
            this.closeButton.Text = "Please wait...";
            this.closeButton.UseVisualStyleBackColor = true;
            this.closeButton.Click += new System.EventHandler(this.CloseButton_Click);
            //
            // DatabaseProgressForm
            //
            this.AcceptButton = this.closeButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(484, 161);
            this.ControlBox = false;
            this.Controls.Add(this.closeButton);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.progressLabel);
            this.Controls.Add(this.titleLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "DatabaseProgressForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Building Database";
            this.Load += new System.EventHandler(this.DatabaseProgressForm_Load);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}