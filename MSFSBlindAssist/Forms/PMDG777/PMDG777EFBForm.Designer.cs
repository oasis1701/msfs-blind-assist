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
        private Label? simbriefStatusLabel;
        private Label? callsignLabel;
        private Label? callsignValue;
        private Label? originLabel;
        private Label? originValue;
        private Label? destLabel;
        private Label? destValue;
        private Label? altLabel;
        private Label? altValue;
        private Label? cruiseAltLabel;
        private Label? cruiseAltValue;
        private Label? costIndexLabel;
        private Label? costIndexValue;
        private Label? zfwLabel;
        private Label? zfwValue;
        private Label? fuelLabel;
        private Label? fuelValue;
        private Label? windLabel;
        private Label? windValue;
        private Button? sendToFmcButton;

        private Label? navigraphStatusLabel;
        private Button? navigraphSignInButton;
        private Label? authCodeLabel;
        private TextBox? authCodeTextBox;
        private Button? navigraphSignOutButton;

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

            simbriefTab = new TabPage("SimBrief");
            simbriefTab.Padding = new Padding(10);
            int y = 10;
            const int labelX = 10;
            const int valueX = 160;
            const int rowHeight = 28;

            simbriefStatusLabel = new Label { Text = "Ready", Location = new System.Drawing.Point(labelX, y), AutoSize = true, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Status" };
            y += rowHeight + 5;
            fetchSimbriefButton = new Button { Text = "Fetch SimBrief", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Fetch SimBrief" };
            sendToFmcButton = new Button { Text = "Send to FMC", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Send to FMC" };
            y += 40;
            callsignLabel = new Label { Text = "Callsign:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            callsignValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Callsign" };
            y += rowHeight;
            originLabel = new Label { Text = "Origin:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            originValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Origin" };
            y += rowHeight;
            destLabel = new Label { Text = "Destination:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            destValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Destination" };
            y += rowHeight;
            altLabel = new Label { Text = "Alternate:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            altValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Alternate" };
            y += rowHeight;
            cruiseAltLabel = new Label { Text = "Cruise Altitude:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            cruiseAltValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Cruise Altitude" };
            y += rowHeight;
            costIndexLabel = new Label { Text = "Cost Index:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            costIndexValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Cost Index" };
            y += rowHeight;
            zfwLabel = new Label { Text = "ZFW:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            zfwValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Zero Fuel Weight" };
            y += rowHeight;
            fuelLabel = new Label { Text = "Total Fuel:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            fuelValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Total Fuel" };
            y += rowHeight;
            windLabel = new Label { Text = "Average Wind:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            windValue = new Label { Text = "\u2014", Location = new System.Drawing.Point(valueX, y), AutoSize = true, AccessibleName = "Average Wind" };

            simbriefTab.Controls.AddRange(new Control[] {
                simbriefStatusLabel, fetchSimbriefButton, sendToFmcButton,
                callsignLabel, callsignValue, originLabel, originValue,
                destLabel, destValue, altLabel, altValue,
                cruiseAltLabel, cruiseAltValue, costIndexLabel, costIndexValue,
                zfwLabel, zfwValue, fuelLabel, fuelValue,
                windLabel, windValue
            });

            navigraphTab = new TabPage("Navigraph");
            navigraphTab.Padding = new Padding(10);
            y = 10;
            navigraphStatusLabel = new Label { Text = "Not authenticated", Location = new System.Drawing.Point(labelX, y), AutoSize = true, Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold), AccessibleName = "Navigraph Status" };
            y += rowHeight + 5;
            navigraphSignInButton = new Button { Text = "Sign In", Location = new System.Drawing.Point(labelX, y), Size = new System.Drawing.Size(140, 30), AccessibleName = "Sign In to Navigraph" };
            navigraphSignOutButton = new Button { Text = "Sign Out", Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(140, 30), Enabled = false, AccessibleName = "Sign Out of Navigraph" };
            y += 40;
            authCodeLabel = new Label { Text = "Auth Code:", Location = new System.Drawing.Point(labelX, y), AutoSize = true };
            authCodeTextBox = new TextBox { Location = new System.Drawing.Point(valueX, y), Size = new System.Drawing.Size(200, 25), ReadOnly = true, AccessibleName = "Navigraph Auth Code" };

            navigraphTab.Controls.AddRange(new Control[] {
                navigraphStatusLabel, navigraphSignInButton, navigraphSignOutButton,
                authCodeLabel, authCodeTextBox
            });

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

            tabControl.TabPages.Add(simbriefTab);
            tabControl.TabPages.Add(navigraphTab);
            tabControl.TabPages.Add(preferencesTab);
            this.Controls.Add(tabControl);

            fetchSimbriefButton.TabIndex = 0;
            sendToFmcButton.TabIndex = 1;
            navigraphSignInButton.TabIndex = 0;
            navigraphSignOutButton.TabIndex = 1;
            authCodeTextBox.TabIndex = 2;
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
