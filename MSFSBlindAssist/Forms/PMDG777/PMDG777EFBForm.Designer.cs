namespace MSFSBlindAssist.Forms.PMDG777
{
    partial class PMDG777EFBForm
    {
        private System.ComponentModel.IContainer? components = null;

        private Controls.AccessibleTabControl? tabControl;
        private TabPage? simbriefTab;
        private TabPage? navigraphTab;
        private TabPage? preferencesTab;

        private Button? fetchSimbriefButton;
        private TextBox? simbriefStatusText;
        private Label? callsignLabel;
        private TextBox? callsignValue;
        private Label? originLabel;
        private TextBox? originValue;
        private Label? destLabel;
        private TextBox? destValue;
        private Label? altLabel;
        private TextBox? altValue;
        private Label? cruiseAltLabel;
        private TextBox? cruiseAltValue;
        private Label? costIndexLabel;
        private TextBox? costIndexValue;
        private Label? zfwLabel;
        private TextBox? zfwValue;
        private Label? fuelLabel;
        private TextBox? fuelValue;
        private Label? windLabel;
        private TextBox? windValue;
        private Button? sendToFmcButton;

        private TextBox? navigraphStatusText;
        private Button? navigraphSignInButton;
        private Label? authCodeLabel;
        private TextBox? authCodeTextBox;
        private Button? navigraphSignOutButton;

        // Navigation Data controls
        private Label? navdataCycleLabel;
        private TextBox? navdataCycleValue;
        private Label? navdataAvailableLabel;
        private TextBox? navdataAvailableValue;
        private Button? checkNavdataButton;
        private Button? downloadNavdataButton;
        private TextBox? navdataProgressText;
        private TextBox? pageTextDiagnostic;

        // Display tab controls — WebView2 loading from local server
        private Microsoft.Web.WebView2.WinForms.WebView2? displayWebView;
        private Button? displayRefreshButton;

        private Label? simbriefAliasLabel;
        private TextBox? simbriefAliasTextBox;
        private Label? weatherSourceLabel;
        private ComboBox? weatherSourceCombo;
        private Label? weightUnitLabel;
        private ComboBox? weightUnitCombo;
        private Label? distanceUnitLabel;
        private ComboBox? distanceUnitCombo;
        private Label? altitudeUnitLabel;
        private ComboBox? altitudeUnitCombo;
        private Label? temperatureUnitLabel;
        private ComboBox? temperatureUnitCombo;
        private Button? savePreferencesButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private TextBox CreateReadOnlyField(string accessibleName, System.Drawing.Point location, System.Drawing.Size size)
        {
            return new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = System.Drawing.SystemColors.Control,
                Location = location,
                Size = size,
                AccessibleName = accessibleName,
                Text = "\u2014"
            };
        }

        private void InitializeComponent()
        {
            this.Text = "PMDG 777 EFB";
            this.Size = new System.Drawing.Size(500, 520);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.KeyPreview = true;
            this.AccessibleName = "PMDG 777 EFB";

            tabControl = new Controls.AccessibleTabControl();
            tabControl.Dock = DockStyle.Fill;

            // === SimBrief Tab ===
            simbriefTab = new TabPage("SimBrief");
            simbriefTab.Padding = new Padding(10);
            int y = 10;
            const int labelX = 10;
            const int valueX = 160;
            const int valueWidth = 300;
            const int rowHeight = 28;

            simbriefStatusText = new TextBox { Text = "Ready", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(valueWidth + valueX - labelX, 22), ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = System.Drawing.SystemColors.Control, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Status" };
            y += rowHeight + 5;
            fetchSimbriefButton = new Button { Text = "Fetch SimBrief", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Fetch SimBrief" };
            sendToFmcButton = new Button { Text = "Send to FMC", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Send to FMC" };
            y += 40;
            callsignLabel = new Label { Text = "Callsign:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            callsignValue = CreateReadOnlyField("Callsign", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            originLabel = new Label { Text = "Origin:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            originValue = CreateReadOnlyField("Origin", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            destLabel = new Label { Text = "Destination:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            destValue = CreateReadOnlyField("Destination", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            altLabel = new Label { Text = "Alternate:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            altValue = CreateReadOnlyField("Alternate", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            cruiseAltLabel = new Label { Text = "Cruise Altitude:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            cruiseAltValue = CreateReadOnlyField("Cruise Altitude", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            costIndexLabel = new Label { Text = "Cost Index:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            costIndexValue = CreateReadOnlyField("Cost Index", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            zfwLabel = new Label { Text = "ZFW:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            zfwValue = CreateReadOnlyField("Zero Fuel Weight", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            fuelLabel = new Label { Text = "Total Fuel:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            fuelValue = CreateReadOnlyField("Total Fuel", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            windLabel = new Label { Text = "Average Wind:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            windValue = CreateReadOnlyField("Average Wind", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));

            simbriefTab.Controls.AddRange(new Control[] {
                simbriefStatusText, fetchSimbriefButton, sendToFmcButton,
                callsignLabel, callsignValue, originLabel, originValue,
                destLabel, destValue, altLabel, altValue,
                cruiseAltLabel, cruiseAltValue, costIndexLabel, costIndexValue,
                zfwLabel, zfwValue, fuelLabel, fuelValue,
                windLabel, windValue
            });

            // === Navigraph Tab ===
            navigraphTab = new TabPage("Navigraph");
            navigraphTab.Padding = new Padding(10);
            y = 10;
            navigraphStatusText = new TextBox { Text = "Not authenticated", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(valueWidth + valueX - labelX, 22), ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = System.Drawing.SystemColors.Control, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Navigraph Status" };
            y += rowHeight + 5;
            navigraphSignInButton = new Button { Text = "Sign In", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Sign In to Navigraph" };
            navigraphSignOutButton = new Button { Text = "Sign Out", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Sign Out of Navigraph" };
            y += 40;
            authCodeLabel = new Label { Text = "Auth Code:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            authCodeTextBox = new TextBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), ReadOnly = true, AccessibleName = "Navigraph Auth Code" };
            y += rowHeight + 15;

            // --- Navigation Data Section ---
            var navdataSeparator = new Label { Text = "Navigation Data", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(valueWidth + valueX - labelX, 20), Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Navigation Data section" };
            y += rowHeight;
            navdataCycleLabel = new Label { Text = "Navigraph Latest:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            navdataCycleValue = CreateReadOnlyField("Navigraph Latest AIRAC", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight;
            navdataAvailableLabel = new Label { Text = "Package Detail:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            navdataAvailableValue = CreateReadOnlyField("Package Detail", new System.Drawing.Point(valueX, y), new System.Drawing.Size(valueWidth, 22));
            y += rowHeight + 5;
            checkNavdataButton = new Button { Text = "Check for Updates", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Check for Navigation Data Updates" };
            downloadNavdataButton = new Button { Text = "Download Update", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Download Navigation Data Update" };
            y += 40;
            navdataProgressText = CreateReadOnlyField("Update Progress", new System.Drawing.Point(labelX, y), new System.Drawing.Size(valueWidth + valueX - labelX, 22));
            navdataProgressText.Text = "";
            y += rowHeight + 10;

            // Page text diagnostic — shows raw text visible on the EFB tablet
            var pageTextLabel = new Label { Text = "EFB Page Text:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            y += 18;
            pageTextDiagnostic = new TextBox
            {
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(valueWidth + valueX - labelX, 120),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                AccessibleName = "EFB Page Text",
                AccessibleDescription = "Shows the raw text content currently visible on the EFB tablet"
            };
            y += 125;
            var refreshPageTextButton = new Button { Text = "Refresh Page Text", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(160, 30), AccessibleName = "Refresh EFB Page Text" };
            refreshPageTextButton.Click += (s, e) => _bridgeServer.EnqueueCommand("get_page_text");

            navigraphTab.Controls.AddRange(new Control[] {
                navigraphStatusText, navigraphSignInButton, navigraphSignOutButton,
                authCodeLabel, authCodeTextBox,
                navdataSeparator, navdataCycleLabel, navdataCycleValue,
                navdataAvailableLabel, navdataAvailableValue,
                checkNavdataButton, downloadNavdataButton, navdataProgressText,
                pageTextLabel, pageTextDiagnostic, refreshPageTextButton
            });

            // === Preferences Tab ===
            preferencesTab = new TabPage("Preferences");
            preferencesTab.Padding = new Padding(10);
            y = 10;
            simbriefAliasLabel = new Label { Text = "SimBrief Alias:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            simbriefAliasTextBox = new TextBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), AccessibleName = "SimBrief Alias" };
            y += rowHeight + 5;
            weatherSourceLabel = new Label { Text = "Weather Source:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            weatherSourceCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Weather Source" };
            weatherSourceCombo.Items.AddRange(new object[] { "SIM", "REAL-WORLD" });
            y += rowHeight + 5;
            weightUnitLabel = new Label { Text = "Weight Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            weightUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Weight Unit" };
            weightUnitCombo.Items.AddRange(new object[] { "lb", "kg" });
            y += rowHeight + 5;
            distanceUnitLabel = new Label { Text = "Distance Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            distanceUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Distance Unit" };
            distanceUnitCombo.Items.AddRange(new object[] { "nm", "km" });
            y += rowHeight + 5;
            altitudeUnitLabel = new Label { Text = "Altitude Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            altitudeUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Altitude Unit" };
            altitudeUnitCombo.Items.AddRange(new object[] { "ft", "m" });
            y += rowHeight + 5;
            temperatureUnitLabel = new Label { Text = "Temperature Unit:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            temperatureUnitCombo = new ComboBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Temperature Unit" };
            temperatureUnitCombo.Items.AddRange(new object[] { "C", "F" });
            y += rowHeight + 10;
            savePreferencesButton = new Button { Text = "Save Preferences", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Save Preferences" };

            preferencesTab.Controls.AddRange(new Control[] {
                simbriefAliasLabel, simbriefAliasTextBox,
                weatherSourceLabel, weatherSourceCombo,
                weightUnitLabel, weightUnitCombo,
                distanceUnitLabel, distanceUnitCombo,
                altitudeUnitLabel, altitudeUnitCombo,
                temperatureUnitLabel, temperatureUnitCombo,
                savePreferencesButton
            });

            // === Display Tab — WebView2 loading accessible page from local server ===
            var displayTab = new TabPage("Display");
            displayTab.Padding = new Padding(10);

            displayWebView = new Microsoft.Web.WebView2.WinForms.WebView2
            {
                Location = new System.Drawing.Point(10, 10),
                Size = new System.Drawing.Size(460, 370),
                AccessibleName = "EFB Display",
                AccessibleDescription = "EFB tablet display. Tab to navigate items. Enter to click. F5 to refresh."
            };
            displayRefreshButton = new Button
            {
                Text = "Refresh Display (F5)",
                Location = new System.Drawing.Point(10, 385),
                Size = new System.Drawing.Size(160, 30),
                AccessibleName = "Refresh EFB Display"
            };
            displayTab.Controls.AddRange(new Control[] { displayWebView, displayRefreshButton });

            tabControl.TabPages.Add(simbriefTab);
            tabControl.TabPages.Add(navigraphTab);
            tabControl.TabPages.Add(displayTab);
            tabControl.TabPages.Add(preferencesTab);
            this.Controls.Add(tabControl);

            // Tab order — SimBrief
            simbriefStatusText.TabIndex = 0;
            fetchSimbriefButton.TabIndex = 1;
            sendToFmcButton.TabIndex = 2;
            callsignValue.TabIndex = 3;
            originValue.TabIndex = 4;
            destValue.TabIndex = 5;
            altValue.TabIndex = 6;
            cruiseAltValue.TabIndex = 7;
            costIndexValue.TabIndex = 8;
            zfwValue.TabIndex = 9;
            fuelValue.TabIndex = 10;
            windValue.TabIndex = 11;

            // Tab order — Navigraph
            navigraphStatusText.TabIndex = 0;
            navigraphSignInButton.TabIndex = 1;
            navigraphSignOutButton.TabIndex = 2;
            authCodeTextBox.TabIndex = 3;

            // Tab order — Preferences
            simbriefAliasTextBox.TabIndex = 0;
            weatherSourceCombo.TabIndex = 1;
            weightUnitCombo.TabIndex = 2;
            distanceUnitCombo.TabIndex = 3;
            altitudeUnitCombo.TabIndex = 4;
            temperatureUnitCombo.TabIndex = 5;
            savePreferencesButton.TabIndex = 6;
        }
    }
}
