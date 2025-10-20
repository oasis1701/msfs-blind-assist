using FBWBA.Accessibility;
using FBWBA.Database;
using FBWBA.Settings;

namespace FBWBA.Forms;
public partial class DatabaseSettingsForm : Form
{
    private Label statusLabel = null!;
    private Button buildFs2020Button = null!;
    private Button buildFs2024Button = null!;
    private Button verifyButton = null!;
    private Button closeButton = null!;
    private Label titleLabel = null!;
    private Label infoLabel = null!;

    private readonly ScreenReaderAnnouncer announcer;
    private readonly MainForm? mainForm;

    public DatabaseSettingsForm(ScreenReaderAnnouncer announcer, MainForm? mainForm = null)
    {
        this.announcer = announcer;
        this.mainForm = mainForm;
        InitializeComponent();
        SetupAccessibility();
        UpdateDatabaseStatus();
    }

    private void InitializeComponent()
    {
        Text = "Database Management";
        Size = new Size(600, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        int yPos = 20;

        // Title Label
        titleLabel = new Label
        {
            Text = "Database Management",
            Location = new Point(20, yPos),
            Size = new Size(550, 25),
            AccessibleName = "Database Management Title",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold)
        };
        yPos += 40;

        // Info Label
        infoLabel = new Label
        {
            Text = "The active database is automatically selected based on the running simulator.\n" +
                   "Use the buttons below to build or rebuild databases for each version.\n" +
                   "Databases are stored in: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FBWBA", "databases"),
            Location = new Point(40, yPos),
            Size = new Size(520, 70),
            AccessibleName = "Database information",
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 8)
        };
        yPos += 80;

        // Status Label
        statusLabel = new Label
        {
            Text = "Active Database: Checking...",
            Location = new Point(40, yPos),
            Size = new Size(520, 30),
            AccessibleName = "Active database status",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            ForeColor = Color.DarkBlue
        };
        yPos += 45;

        // Build FS2020 Database Button
        buildFs2020Button = new Button
        {
            Text = "Build FS2020 Database",
            Location = new Point(40, yPos),
            Size = new Size(180, 35),
            AccessibleName = "Build Flight Simulator 2020 database",
            AccessibleDescription = "Build airport database for Flight Simulator 2020 using navdatareader",
            Font = new Font(Font.FontFamily, 9)
        };
        buildFs2020Button.Click += (s, e) => BuildDatabaseForSimulator("FS2020");

        // Build FS2024 Database Button
        buildFs2024Button = new Button
        {
            Text = "Build FS2024 Database",
            Location = new Point(230, yPos),
            Size = new Size(180, 35),
            AccessibleName = "Build Flight Simulator 2024 database",
            AccessibleDescription = "Build airport database for Flight Simulator 2024 using navdatareader",
            Font = new Font(Font.FontFamily, 9)
        };
        buildFs2024Button.Click += (s, e) => BuildDatabaseForSimulator("FS2024");
        yPos += 50;

        // Verify Button
        verifyButton = new Button
        {
            Text = "Verify Active Database",
            Location = new Point(40, yPos),
            Size = new Size(180, 35),
            AccessibleName = "Verify active database",
            AccessibleDescription = "Check which database is currently active and how many airports it contains",
            Font = new Font(Font.FontFamily, 9)
        };
        verifyButton.Click += VerifyButton_Click;
        yPos += 60;

        // Close Button
        closeButton = new Button
        {
            Text = "Close",
            Location = new Point(480, yPos),
            Size = new Size(80, 30),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close window",
            AccessibleDescription = "Close database management window"
        };

        // Add all controls to form
        Controls.AddRange(new Control[]
        {
            titleLabel, infoLabel, statusLabel,
            buildFs2020Button, buildFs2024Button,
            verifyButton, closeButton
        });

        AcceptButton = closeButton;
        CancelButton = closeButton;
    }

    private void SetupAccessibility()
    {
        // Set tab order
        int tabIndex = 0;
        titleLabel.TabIndex = tabIndex++;
        infoLabel.TabIndex = tabIndex++;
        statusLabel.TabIndex = tabIndex++;
        buildFs2020Button.TabIndex = tabIndex++;
        buildFs2024Button.TabIndex = tabIndex++;
        verifyButton.TabIndex = tabIndex++;
        closeButton.TabIndex = tabIndex++;

        // Focus on load
        Load += (sender, e) =>
        {
            BringToFront();
            Activate();
            TopMost = true;
            TopMost = false;
            buildFs2020Button.Focus();
        };
    }

    private void UpdateDatabaseStatus()
    {
        var settings = SettingsManager.Current;
        string activeVersion = settings.SimulatorVersion ?? "FS2020";
        string navdataPath = NavdataReaderBuilder.GetDefaultDatabasePath(activeVersion);

        if (!File.Exists(navdataPath))
        {
            statusLabel.Text = $"Active Database: {activeVersion} (Not Built)";
            statusLabel.ForeColor = Color.DarkOrange;
            return;
        }

        try
        {
            var provider = new LittleNavMapProvider(navdataPath, activeVersion);
            int airportCount = provider.GetAirportCount();
            statusLabel.Text = $"Active Database: {activeVersion} - {airportCount:N0} airports";
            statusLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Active Database: {activeVersion} (Error: {ex.Message})";
            statusLabel.ForeColor = Color.Red;
        }
    }

    private void VerifyButton_Click(object? sender, EventArgs e)
    {
        UpdateDatabaseStatus();
        // Announce the status directly without extra prefix
        announcer?.AnnounceImmediate(statusLabel.Text);
    }

    private void BuildDatabaseForSimulator(string simulatorVersion)
    {
        // Build simulator-specific warning message
        string requirementMessage;
        if (simulatorVersion == "FS2024")
        {
            requirementMessage =
                "IMPORTANT: Flight Simulator 2024 MUST be running and loaded to the main menu.\n" +
                "Navdatareader uses SimConnect to retrieve scenery data from the running simulator.\n\n";
        }
        else // FS2020
        {
            requirementMessage =
                "IMPORTANT: Flight Simulator 2020 should be CLOSED.\n" +
                "Navdatareader reads scenery files directly from disk.\n\n";
        }

        // Confirm with user
        var result = MessageBox.Show(
            requirementMessage +
            $"This will build the {simulatorVersion} airport database using navdatareader.\n\n" +
            "This process may take 2-5 minutes depending on your system.\n\n" +
            "Do you want to continue?",
            $"Build {simulatorVersion} Database",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
            return;

        // Close database connections before building to avoid file locking
        mainForm?.CloseDatabaseConnections();

        try
        {
            // Show progress dialog
            using (var progressForm = new DatabaseBuildProgressForm(simulatorVersion, announcer))
            {
                var dialogResult = progressForm.ShowDialog(this);

                if (dialogResult == DialogResult.OK)
                {
                    // Build succeeded
                    announcer?.AnnounceImmediate($"{simulatorVersion} database built successfully");
                    MessageBox.Show(
                        $"{simulatorVersion} database has been built successfully!",
                        "Build Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    // Build failed or was cancelled
                    MessageBox.Show(
                        $"Database build was cancelled or failed.\n\nCheck the error message for details.",
                        "Build Not Completed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // Refresh status
                UpdateDatabaseStatus();
            }
        }
        finally
        {
            // Reopen database connections after build completes (success or failure)
            mainForm?.ReopenDatabaseConnections();
        }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        return base.ProcessDialogKey(keyData);
    }
}
