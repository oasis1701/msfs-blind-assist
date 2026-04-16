using MSFSBlindAssist.Controls;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Weights &amp; Balance panel. Inner tabs for Overview (weights/CG +
    /// SimBrief/mode buttons), Quick Load (random payload + fuel uplifts),
    /// and Precise Load (individual pax/cargo/fuel). Matches the tablet's
    /// mode split where Quick and Precise are mutually exclusive UI modes.
    /// </summary>
    public class WeightsBalancePanel : EfbAppPanelBase
    {
        private const string ValuesTag = "wb";

        // Weight & CG fields
        private const string IdTakeoffWeight = "wb_takeoff_weight";
        private const string IdTakeoffWeightUnit = "wb_takeoff_weight_unit";
        private const string IdZfwWeight = "wb_zfw_weight";
        private const string IdZfwWeightUnit = "wb_zfw_weight_unit";
        private const string IdLandingWeight = "wb_landing_weight";
        private const string IdLandingWeightUnit = "wb_landing_weight_unit";
        private const string IdTakeoffCg = "wb_takeoff_cg";
        private const string IdZfwCg = "wb_zfw_cg";
        private const string IdLandingCg = "wb_landing_cg";

        // Load mode + Simbrief
        private const string IdLoadSimbrief = "wb_load_from_simbrief";
        private const string IdQuickLoad = "wb_quick_load";
        private const string IdManualLoad = "wb_manual_load";

        // Quick load panel
        private const string IdPaxLevel = "wb_pax_level_percent";
        private const string IdPaxRandomize = "wb_pax_level_randomize";
        private const string IdOverallLevel = "wb_overall_load_level_percent";
        private const string IdOverallRandomize = "wb_overall_load_level_randomize";
        private const string IdCargoLevel = "wb_cargo_level_percent";
        private const string IdCargoRandomize = "wb_cargo_level_randomize";
        private const string IdFuelLongRange = "wb_fuel_longrange";
        private const string IdFuelMedRange = "wb_fuel_medrange";
        private const string IdFuelShortRange = "wb_fuel_shortrange";

        // Precise load: pax
        private const string IdPaxFirst = "wb_pax_first";
        private const string IdPaxBusiness = "wb_pax_business";
        private const string IdPaxEconomy = "wb_pax_economy";

        // Precise load: cargo
        private const string IdCargoFwd = "wb_cargo_fwd";
        private const string IdCargoFwdUnit = "wb_cargo_fwd_unit";
        private const string IdCargoAft = "wb_cargo_aft";
        private const string IdCargoAftUnit = "wb_cargo_aft_unit";
        private const string IdCargoBulk = "wb_cargo_bulk";
        private const string IdCargoBulkUnit = "wb_cargo_bulk_unit";

        // Precise fuel (also available in precise mode)
        private const string IdFuelTotal = "wb_fuel_total_lbs";
        private const string IdFuelTotalUnit = "wb_fuel_total_lbs_unit";
        private const string IdFuelLevel = "wb_fuel_level_percent";
        private const string IdFuelDensity = "wb_fuel_density";

        // --- Controls ---
        private AccessibleTabControl innerTabs = null!;

        // Overview
        private TextBox takeoffWeightBox = null!, takeoffWeightUnitBox = null!;
        private TextBox zfwWeightBox = null!, zfwWeightUnitBox = null!;
        private TextBox landingWeightBox = null!, landingWeightUnitBox = null!;
        private TextBox takeoffCgBox = null!;
        private TextBox zfwCgBox = null!;
        private TextBox landingCgBox = null!;
        private Button loadSimbriefButton = null!;
        private Button quickLoadButton = null!;
        private Button manualLoadButton = null!;

        // Quick
        private TextBox paxLevelBox = null!;
        private Button paxRandomizeButton = null!;
        private TextBox overallLevelBox = null!;
        private Button overallRandomizeButton = null!;
        private TextBox cargoLevelBox = null!;
        private Button cargoRandomizeButton = null!;
        private Button fuelLongRangeButton = null!;
        private Button fuelMedRangeButton = null!;
        private Button fuelShortRangeButton = null!;

        // Precise
        private TextBox paxFirstBox = null!, paxBusinessBox = null!, paxEconomyBox = null!;
        private TextBox cargoFwdBox = null!, cargoFwdUnitBox = null!;
        private TextBox cargoAftBox = null!, cargoAftUnitBox = null!;
        private TextBox cargoBulkBox = null!, cargoBulkUnitBox = null!;
        private TextBox fuelTotalBox = null!, fuelTotalUnitBox = null!;
        private TextBox fuelLevelBox = null!;
        private TextBox fuelDensityBox = null!;

        private bool _suppressWrites;
        private readonly HashSet<TextBox> _dirtyBoxes = new();
        private System.Windows.Forms.Timer? _monitor;

        public override Control? InitialFocusControl => innerTabs;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            AccessibleName = "Weights and Balance";

            innerTabs = new AccessibleTabControl { Dock = DockStyle.Fill };
            innerTabs.TabPages.Add(BuildOverviewTab());
            innerTabs.TabPages.Add(BuildQuickLoadTab());
            innerTabs.TabPages.Add(BuildPreciseLoadTab());
            Controls.Add(innerTabs);

            WireHandlers();
        }

        // --------------------------------------------------------------
        // Shared helpers
        // --------------------------------------------------------------
        private const int LabelX = 10, ValueX = 200, ValueWidth = 140, UnitX = 346, UnitWidth = 50;
        private const int RowHeight = 28;

        private TextBox NumberRow(TabPage page, ref int y, ref int tabIdx, string labelText, string accName, bool hasUnit)
        {
            page.Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(LabelX, y + 3), AutoSize = true });
            var box = new TextBox
            {
                Location = new System.Drawing.Point(ValueX, y),
                Size = new System.Drawing.Size(ValueWidth, 25),
                AccessibleName = accName,
                TabIndex = tabIdx++
            };
            page.Controls.Add(box);
            if (hasUnit)
            {
                var unitBox = CreateReadOnlyField(accName + " unit");
                unitBox.Location = new System.Drawing.Point(UnitX, y);
                unitBox.Size = new System.Drawing.Size(UnitWidth, 22);
                unitBox.Text = "";
                page.Controls.Add(unitBox);
                box.Tag = unitBox;
            }
            y += RowHeight + 2;
            return box;
        }

        private static Button FullButton(TabPage page, ref int y, ref int tabIdx, string text, string accName)
        {
            var btn = new Button
            {
                Text = text,
                Location = new System.Drawing.Point(LabelX, y),
                Size = new System.Drawing.Size(380, 30),
                AccessibleName = accName,
                TabIndex = tabIdx++
            };
            page.Controls.Add(btn);
            y += 36;
            return btn;
        }

        private static void SectionHeader(TabPage page, ref int y, string text)
        {
            page.Controls.Add(new Label
            {
                Text = text,
                Location = new System.Drawing.Point(LabelX, y),
                Size = new System.Drawing.Size(500, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = text + " section"
            });
            y += RowHeight;
        }

        // --------------------------------------------------------------
        // Overview tab: weights, CGs, SimBrief load, mode toggles
        // --------------------------------------------------------------
        private TabPage BuildOverviewTab()
        {
            var page = new TabPage("Overview") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            SectionHeader(page, ref y, "Weights and Centre of Gravity");
            takeoffWeightBox = NumberRow(page, ref y, ref tabIdx, "Takeoff Weight:", "Takeoff Weight", true);
            takeoffWeightUnitBox = (TextBox)takeoffWeightBox.Tag!;
            zfwWeightBox = NumberRow(page, ref y, ref tabIdx, "Zero Fuel Weight:", "Zero Fuel Weight", true);
            zfwWeightUnitBox = (TextBox)zfwWeightBox.Tag!;
            landingWeightBox = NumberRow(page, ref y, ref tabIdx, "Landing Weight:", "Landing Weight", true);
            landingWeightUnitBox = (TextBox)landingWeightBox.Tag!;
            takeoffCgBox = NumberRow(page, ref y, ref tabIdx, "Takeoff CG %MAC:", "Takeoff Centre of Gravity percent MAC", false);
            zfwCgBox = NumberRow(page, ref y, ref tabIdx, "Zero Fuel CG %MAC:", "Zero Fuel Centre of Gravity percent MAC", false);
            landingCgBox = NumberRow(page, ref y, ref tabIdx, "Landing CG %MAC:", "Landing Centre of Gravity percent MAC", false);
            y += 6;

            SectionHeader(page, ref y, "Loading");
            loadSimbriefButton = FullButton(page, ref y, ref tabIdx, "Load Plane from Simbrief", "Load Plane from Simbrief");
            quickLoadButton = FullButton(page, ref y, ref tabIdx, "Switch to Quick Loading Mode", "Switch to Quick Loading Mode on tablet");
            manualLoadButton = FullButton(page, ref y, ref tabIdx, "Switch to Precise Loading Mode", "Switch to Precise Loading Mode on tablet");

            return page;
        }

        // --------------------------------------------------------------
        // Quick Load tab: random payload percentages + fuel uplift presets
        // --------------------------------------------------------------
        private TabPage BuildQuickLoadTab()
        {
            var page = new TabPage("Quick Load") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            SectionHeader(page, ref y, "Random Payload");
            paxLevelBox = NumberRow(page, ref y, ref tabIdx, "Pax Level %:", "Passenger Load Level Percent", false);
            paxRandomizeButton = FullButton(page, ref y, ref tabIdx, "Randomize Passengers", "Randomize Passenger Load");
            overallLevelBox = NumberRow(page, ref y, ref tabIdx, "Overall Level %:", "Overall Load Level Percent", false);
            overallRandomizeButton = FullButton(page, ref y, ref tabIdx, "Randomize Overall", "Randomize Overall Load");
            cargoLevelBox = NumberRow(page, ref y, ref tabIdx, "Cargo Level %:", "Cargo Load Level Percent", false);
            cargoRandomizeButton = FullButton(page, ref y, ref tabIdx, "Randomize Cargo", "Randomize Cargo Load");
            y += 6;

            SectionHeader(page, ref y, "Fuel Uplift Presets");
            fuelLongRangeButton = FullButton(page, ref y, ref tabIdx, "Uplift Long Range Fuel", "Uplift Long Range Fuel");
            fuelMedRangeButton = FullButton(page, ref y, ref tabIdx, "Uplift Medium Range Fuel", "Uplift Medium Range Fuel");
            fuelShortRangeButton = FullButton(page, ref y, ref tabIdx, "Uplift Short Range Fuel", "Uplift Short Range Fuel");

            return page;
        }

        // --------------------------------------------------------------
        // Precise Load tab: individual pax/cargo/fuel values
        // --------------------------------------------------------------
        private TabPage BuildPreciseLoadTab()
        {
            var page = new TabPage("Precise Load") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            SectionHeader(page, ref y, "Passengers");
            paxFirstBox = NumberRow(page, ref y, ref tabIdx, "First Class:", "First Class Passengers", false);
            paxBusinessBox = NumberRow(page, ref y, ref tabIdx, "Business Class:", "Business Class Passengers", false);
            paxEconomyBox = NumberRow(page, ref y, ref tabIdx, "Economy Class:", "Economy Class Passengers", false);
            y += 6;

            SectionHeader(page, ref y, "Cargo");
            cargoFwdBox = NumberRow(page, ref y, ref tabIdx, "Forward Cargo:", "Forward Cargo Weight", true);
            cargoFwdUnitBox = (TextBox)cargoFwdBox.Tag!;
            cargoAftBox = NumberRow(page, ref y, ref tabIdx, "Aft Cargo:", "Aft Cargo Weight", true);
            cargoAftUnitBox = (TextBox)cargoAftBox.Tag!;
            cargoBulkBox = NumberRow(page, ref y, ref tabIdx, "Bulk Cargo:", "Bulk Cargo Weight", true);
            cargoBulkUnitBox = (TextBox)cargoBulkBox.Tag!;
            y += 6;

            SectionHeader(page, ref y, "Fuel");
            fuelTotalBox = NumberRow(page, ref y, ref tabIdx, "Total Fuel:", "Total Fuel Weight", true);
            fuelTotalUnitBox = (TextBox)fuelTotalBox.Tag!;
            fuelLevelBox = NumberRow(page, ref y, ref tabIdx, "Fuel Level %:", "Fuel Level Percent", false);
            fuelDensityBox = NumberRow(page, ref y, ref tabIdx, "Fuel Density:", "Fuel Density", false);

            return page;
        }

        // --------------------------------------------------------------
        // Wiring
        // --------------------------------------------------------------
        private void WireHandlers()
        {
            WireTextInput(takeoffWeightBox, IdTakeoffWeight);
            WireTextInput(zfwWeightBox, IdZfwWeight);
            WireTextInput(landingWeightBox, IdLandingWeight);
            WireTextInput(takeoffCgBox, IdTakeoffCg);
            WireTextInput(zfwCgBox, IdZfwCg);
            WireTextInput(landingCgBox, IdLandingCg);

            WireTextInput(paxLevelBox, IdPaxLevel);
            WireTextInput(overallLevelBox, IdOverallLevel);
            WireTextInput(cargoLevelBox, IdCargoLevel);

            WireTextInput(paxFirstBox, IdPaxFirst);
            WireTextInput(paxBusinessBox, IdPaxBusiness);
            WireTextInput(paxEconomyBox, IdPaxEconomy);

            WireTextInput(cargoFwdBox, IdCargoFwd);
            WireTextInput(cargoAftBox, IdCargoAft);
            WireTextInput(cargoBulkBox, IdCargoBulk);

            WireTextInput(fuelTotalBox, IdFuelTotal);
            WireTextInput(fuelLevelBox, IdFuelLevel);
            WireTextInput(fuelDensityBox, IdFuelDensity);

            WireClickButton(loadSimbriefButton, IdLoadSimbrief);
            WireClickButton(quickLoadButton, IdQuickLoad);
            WireClickButton(manualLoadButton, IdManualLoad);
            WireClickButton(paxRandomizeButton, IdPaxRandomize);
            WireClickButton(overallRandomizeButton, IdOverallRandomize);
            WireClickButton(cargoRandomizeButton, IdCargoRandomize);
            WireClickButton(fuelLongRangeButton, IdFuelLongRange);
            WireClickButton(fuelMedRangeButton, IdFuelMedRange);
            WireClickButton(fuelShortRangeButton, IdFuelShortRange);
        }

        private void WireTextInput(TextBox box, string domId)
        {
            box.TextChanged += (_, _) =>
            {
                if (!_suppressWrites) _dirtyBoxes.Add(box);
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !_suppressWrites)
                {
                    CommitBox(box, domId);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            box.LostFocus += (_, _) =>
            {
                if (_suppressWrites) return;
                if (_dirtyBoxes.Contains(box))
                    CommitBox(box, domId);
            };
        }

        private void CommitBox(TextBox box, string domId)
        {
            BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
            {
                ["id"] = domId,
                ["value"] = box.Text ?? ""
            });
            _dirtyBoxes.Remove(box);
        }

        private void WireClickButton(Button btn, string domId)
        {
            btn.Click += (_, _) =>
            {
                BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string>
                {
                    ["id"] = domId,
                    ["simple"] = "true"
                });
            };
        }

        // --------------------------------------------------------------
        // Activation / monitor
        // --------------------------------------------------------------
        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            ArmLoadAnnouncement();
            StartMonitor();
        }

        public override void OnDeactivated()
        {
            StopMonitor();
        }

        private void StartMonitor()
        {
            StopMonitor();
            _monitor = new System.Windows.Forms.Timer { Interval = 1500 };
            _monitor.Tick += (_, _) =>
            {
                if (IsDisposed || !BridgeServer.IsBridgeConnected)
                {
                    StopMonitor();
                    return;
                }
                RequestAllValues();
            };
            _monitor.Start();
            var t = new System.Windows.Forms.Timer { Interval = 500 };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); if (!IsDisposed) RequestAllValues(); };
            t.Start();
        }

        private void StopMonitor()
        {
            _monitor?.Stop();
            _monitor?.Dispose();
            _monitor = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopMonitor();
            base.Dispose(disposing);
        }

        private void RequestAllValues()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            var ids = new[] {
                IdTakeoffWeight, IdTakeoffWeightUnit,
                IdZfwWeight, IdZfwWeightUnit,
                IdLandingWeight, IdLandingWeightUnit,
                IdTakeoffCg, IdZfwCg, IdLandingCg,
                IdPaxLevel, IdOverallLevel, IdCargoLevel,
                IdPaxFirst, IdPaxBusiness, IdPaxEconomy,
                IdCargoFwd, IdCargoFwdUnit,
                IdCargoAft, IdCargoAftUnit,
                IdCargoBulk, IdCargoBulkUnit,
                IdFuelTotal, IdFuelTotalUnit,
                IdFuelLevel, IdFuelDensity
            };
            BridgeServer.EnqueueCommand("read_values", new Dictionary<string, string>
            {
                ["tag"] = ValuesTag,
                ["ids"] = string.Join(",", ids)
            });
        }

        protected override void HandleStateUpdate(EFBStateUpdateEventArgs e)
        {
            if (e.Data.GetValueOrDefault("_tag", "") != ValuesTag) return;
            if (e.Type == "values") ApplyValues(e.Data);
        }

        private void ApplyValues(Dictionary<string, string> d)
        {
            _suppressWrites = true;
            try
            {
                AnnounceLoadedIfPending();

                SyncBox(takeoffWeightBox, d.GetValueOrDefault(IdTakeoffWeight, ""));
                takeoffWeightUnitBox.Text = d.GetValueOrDefault(IdTakeoffWeightUnit, "");
                SyncBox(zfwWeightBox, d.GetValueOrDefault(IdZfwWeight, ""));
                zfwWeightUnitBox.Text = d.GetValueOrDefault(IdZfwWeightUnit, "");
                SyncBox(landingWeightBox, d.GetValueOrDefault(IdLandingWeight, ""));
                landingWeightUnitBox.Text = d.GetValueOrDefault(IdLandingWeightUnit, "");

                SyncBox(takeoffCgBox, d.GetValueOrDefault(IdTakeoffCg, ""));
                SyncBox(zfwCgBox, d.GetValueOrDefault(IdZfwCg, ""));
                SyncBox(landingCgBox, d.GetValueOrDefault(IdLandingCg, ""));

                SyncBox(paxLevelBox, d.GetValueOrDefault(IdPaxLevel, ""));
                SyncBox(overallLevelBox, d.GetValueOrDefault(IdOverallLevel, ""));
                SyncBox(cargoLevelBox, d.GetValueOrDefault(IdCargoLevel, ""));

                SyncBox(paxFirstBox, d.GetValueOrDefault(IdPaxFirst, ""));
                SyncBox(paxBusinessBox, d.GetValueOrDefault(IdPaxBusiness, ""));
                SyncBox(paxEconomyBox, d.GetValueOrDefault(IdPaxEconomy, ""));

                SyncBox(cargoFwdBox, d.GetValueOrDefault(IdCargoFwd, ""));
                cargoFwdUnitBox.Text = d.GetValueOrDefault(IdCargoFwdUnit, "");
                SyncBox(cargoAftBox, d.GetValueOrDefault(IdCargoAft, ""));
                cargoAftUnitBox.Text = d.GetValueOrDefault(IdCargoAftUnit, "");
                SyncBox(cargoBulkBox, d.GetValueOrDefault(IdCargoBulk, ""));
                cargoBulkUnitBox.Text = d.GetValueOrDefault(IdCargoBulkUnit, "");

                SyncBox(fuelTotalBox, d.GetValueOrDefault(IdFuelTotal, ""));
                fuelTotalUnitBox.Text = d.GetValueOrDefault(IdFuelTotalUnit, "");
                SyncBox(fuelLevelBox, d.GetValueOrDefault(IdFuelLevel, ""));
                SyncBox(fuelDensityBox, d.GetValueOrDefault(IdFuelDensity, ""));
            }
            finally
            {
                _suppressWrites = false;
            }
        }

        private void SyncBox(TextBox box, string value)
        {
            if (box.Focused) return;
            if (_dirtyBoxes.Contains(box)) return;
            if (box.Text != value) box.Text = value;
        }
    }
}
