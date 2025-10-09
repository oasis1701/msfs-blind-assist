using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FBWBA.Database;
using FBWBA.Accessibility;

namespace FBWBA.Forms
{
    public partial class DatabaseProgressForm : Form
    {
        private readonly DatabaseBuilder _builder;
        private readonly string _makeRwysFolder;
        private readonly ScreenReaderAnnouncer _announcer;

        public DatabaseProgressForm(DatabaseBuilder builder, string makeRwysFolder, ScreenReaderAnnouncer announcer)
        {
            _builder = builder;
            _makeRwysFolder = makeRwysFolder;
            _announcer = announcer;
            InitializeComponent();

            _builder.ProgressUpdate += OnProgressUpdate;
        }

        private void DatabaseProgressForm_Load(object sender, EventArgs e)
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false; // Flash to bring to front

            _announcer?.Announce("Building Airport Database. Please wait.");

            // Focus the progress text box so screen readers can read it
            progressLabel.Focus();

            StartDatabaseBuild();
        }

        private async void StartDatabaseBuild()
        {
            try
            {
                progressBar.Style = ProgressBarStyle.Marquee;

                await Task.Run(() =>
                {
                    _builder.BuildDatabase(_makeRwysFolder);
                });

                var stats = _builder.GetDatabaseStats();
                var completionMessage = $"Completed successfully. {stats}";
                progressLabel.Text = completionMessage;
                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = 100;

                _announcer?.Announce(completionMessage);

                closeButton.Enabled = true;
                closeButton.Text = "Close";
                closeButton.Focus();
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error: {ex.Message}";
                progressLabel.Text = errorMessage;
                progressBar.Style = ProgressBarStyle.Blocks;
                progressBar.Value = 0;

                _announcer?.Announce(errorMessage);

                closeButton.Enabled = true;
                closeButton.Text = "Close";
                closeButton.Focus();
            }
        }

        private void OnProgressUpdate(object sender, string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<object, string>(OnProgressUpdate), sender, message);
                return;
            }

            progressLabel.Text = message;
            _announcer?.Announce(message);
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeButton.Enabled)
            {
                _builder.ProgressUpdate -= OnProgressUpdate;
            }
            else
            {
                e.Cancel = true;
            }

            base.OnFormClosing(e);
        }
    }
}