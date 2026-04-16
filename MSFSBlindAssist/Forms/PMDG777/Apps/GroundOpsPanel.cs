using MSFSBlindAssist.Controls;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps
{
    /// <summary>
    /// Ground Operations panel. Exposes Ground Connections, Service Vehicles,
    /// Door Management, Ground Maintenance, and Automated Ground Ops via an
    /// inner tab control. Toggle items use CheckBoxes (checked = connected/open),
    /// doors use CheckBoxes (checked = open) with bulk Arm/Disarm buttons,
    /// maintenance uses one-shot action buttons.
    /// </summary>
    public class GroundOpsPanel : EfbAppPanelBase
    {
        private const string ValuesTag = "groundops";

        // --- Ground Connections ---
        private const string IdWheelChocks = "groundconnections_wheel_chocks";
        private const string IdAirStart = "groundconnections_air_start_unit";
        private const string IdAirCond = "groundconnections_air_cond_unit";
        private const string IdGroundPower = "groundconnections_ground_power";
        private const string IdGroundPowerType = "groundconnections_ground_power_type";
        private const string IdJetway = "groundconnections_jetway";
        private const string IdPaxEntry = "groundconnections_pax_entree";

        // --- Doors ---
        // Passenger doors cycle: OPEN → DISARM → CLOSE → ARM (action text)
        private static readonly string[] PaxDoorIds = {
            "door_entry1_left", "door_entry1_right",
            "door_entry2_left", "door_entry2_right",
            "door_entry3_left", "door_entry3_right",
            "door_entry4_left", "door_entry4_right"
        };
        private static readonly string[] PaxDoorLabels = {
            "Entry 1 Left", "Entry 1 Right",
            "Entry 2 Left", "Entry 2 Right",
            "Entry 3 Left", "Entry 3 Right",
            "Entry 4 Left", "Entry 4 Right"
        };
        // Cargo/utility doors: simple OPEN/CLOSE toggle
        private static readonly string[] CargoDoorIds = {
            "door_fwd_cargo", "door_aft_cargo", "door_bulk_cargo",
            "door_avionics_access", "door_ee_access"
        };
        private static readonly string[] CargoDoorLabels = {
            "Forward Cargo", "Aft Cargo", "Bulk Cargo",
            "Avionics Access", "EE Access"
        };
        private const string IdOpenAll = "doorsmanagement_open_all_doors";
        private const string IdCloseAll = "doorsmanagement_close_all_doors";
        private const string IdArmAll = "doorsmanagement_arm_all_doors";
        private const string IdDisarmAll = "doorsmanagement_disarm_all_doors";

        // --- Service Vehicles ---
        private static readonly string[] VehicleIds = {
            "aft_cargo", "aft_galley", "bulk_cargo", "cabin_cleaning",
            "fuel_truck", "fwd_cargo", "fwd_galley",
            "lavatory_service", "maintenance_van", "potable_water",
            "stairs_1l", "stairs_2l"
        };
        private static readonly string[] VehicleLabels = {
            "Aft Cargo Loader", "Aft Galley Truck", "Bulk Cargo Loader", "Cabin Cleaning",
            "Fuel Truck", "Forward Cargo Loader", "Forward Galley Truck",
            "Lavatory Service", "Maintenance Van", "Potable Water",
            "Stairs 1L", "Stairs 2L"
        };
        private const string IdRequestAllVehicles = "groundservicevehicles_request_all_vehicles";
        private const string IdReleaseAllVehicles = "groundservicevehicles_release_all_vehicles";

        // --- Maintenance ---
        private static readonly string[] MaintenanceIds = {
            "groundmaintenance_refill_hyd_fluid",
            "groundmaintenance_refill_engine_oil",
            "groundmaintenance_refill_fire_bottles",
            "groundmaintenance_service_scu_motor",
            "groundmaintenance_service_idg_drives",
            "groundmaintenance_replace_tires",
            "groundmaintenance_replace_brakes",
            "groundmaintenance_cool_brakes"
        };
        private static readonly string[] MaintenanceLabels = {
            "Refill Hydraulic Fluid",
            "Refill Engine Oil",
            "Refill Fire Bottles",
            "Service SCU Motor",
            "Service IDG Drives",
            "Replace Tires",
            "Replace Brakes",
            "Cool Brakes"
        };

        // --- Automated Ground Ops ---
        private const string IdTurnType = "turn_type";
        private const string IdAutoWheelChocks = "wheel_chocks";
        private const string IdTargetFuel = "target_fuel";
        private const string IdTargetFuelUnit = "target_fuel_unit";
        private const string IdTargetFuelOk = "target_fuel_ok";
        private const string IdUplift = "uplift_l";
        private const string IdFuelRemaining = "fuel_uplift_remaining_l";
        private const string IdTargetFuelLabel = "target_fuel_l";
        private const string IdTurnTime = "turn_time_l";
        private const string IdTurnTimeRemaining = "turn_time_remaining_l";

        // --- Controls ---
        private AccessibleTabControl innerTabs = null!;

        // Ground Connections
        private CheckBox chocksCheck = null!;
        private CheckBox airStartCheck = null!;
        private CheckBox airCondCheck = null!;
        private CheckBox groundPowerCheck = null!;
        private Button groundPowerTypeButton = null!;
        private Button jetwayButton = null!;
        private Button paxEntryButton = null!;

        // Doors
        private readonly Button[] paxDoorButtons = new Button[PaxDoorIds.Length];
        private readonly CheckBox[] cargoDoorChecks = new CheckBox[CargoDoorIds.Length];

        // Vehicles
        private readonly CheckBox[] vehicleChecks = new CheckBox[VehicleIds.Length];

        // Automated
        private Button turnTypeButton = null!;
        private CheckBox autoChocksCheck = null!;
        private TextBox targetFuelBox = null!;
        private TextBox targetFuelUnitBox = null!;
        private Button targetFuelOkButton = null!;
        private TextBox upliftBox = null!;
        private TextBox fuelRemainingBox = null!;
        private TextBox targetFuelLabelBox = null!;
        private TextBox turnTimeBox = null!;
        private TextBox turnTimeRemainingBox = null!;

        private bool _suppressWrites;
        private DateTime _lastUserAction = DateTime.MinValue;
        private const int UserActionCooldownMs = 4000;
        private System.Windows.Forms.Timer? _monitor;
        private bool _targetFuelDirty;

        public override Control? InitialFocusControl => innerTabs;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            AccessibleName = "Ground Operations";

            innerTabs = new AccessibleTabControl { Dock = DockStyle.Fill };

            innerTabs.TabPages.Add(BuildConnectionsTab());
            innerTabs.TabPages.Add(BuildDoorsTab());
            innerTabs.TabPages.Add(BuildVehiclesTab());
            innerTabs.TabPages.Add(BuildMaintenanceTab());
            innerTabs.TabPages.Add(BuildAutomatedTab());

            Controls.Add(innerTabs);
        }

        // ---------------------------------------------------------------
        // Ground Connections tab
        // ---------------------------------------------------------------
        private TabPage BuildConnectionsTab()
        {
            var page = new TabPage("Ground Connections") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            CheckBox ToggleCheck(string label, string accName)
            {
                var cb = new CheckBox
                {
                    Text = label,
                    Location = new System.Drawing.Point(10, y),
                    AutoSize = true,
                    AccessibleName = accName,
                    TabIndex = tabIdx++
                };
                page.Controls.Add(cb);
                y += 30;
                return cb;
            }

            chocksCheck = ToggleCheck("Wheel Chocks", "Wheel Chocks");
            airStartCheck = ToggleCheck("Air Start Unit", "Air Start Unit");
            airCondCheck = ToggleCheck("Air Conditioning Unit", "Air Conditioning Unit");
            groundPowerCheck = ToggleCheck("Ground Power", "Ground Power");

            groundPowerTypeButton = new Button
            {
                Text = "Power Type: --",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(260, 30),
                AccessibleName = "Ground Power Type",
                TabIndex = tabIdx++
            };
            page.Controls.Add(groundPowerTypeButton);
            y += 36;

            jetwayButton = new Button
            {
                Text = "Jetway Request / Release",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(260, 30),
                AccessibleName = "Jetway Request or Release",
                TabIndex = tabIdx++
            };
            page.Controls.Add(jetwayButton);
            y += 36;

            paxEntryButton = new Button
            {
                Text = "Passenger Entry: --",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(260, 30),
                AccessibleName = "Passenger Entry Mode",
                TabIndex = tabIdx++
            };
            page.Controls.Add(paxEntryButton);

            // Wire handlers
            WireToggleCheck(chocksCheck, IdWheelChocks);
            WireToggleCheck(airStartCheck, IdAirStart);
            WireToggleCheck(airCondCheck, IdAirCond);
            WireToggleCheck(groundPowerCheck, IdGroundPower);
            WireClickButton(groundPowerTypeButton, IdGroundPowerType);
            WireClickButton(jetwayButton, IdJetway);
            WireClickButton(paxEntryButton, IdPaxEntry);

            return page;
        }

        // ---------------------------------------------------------------
        // Door Management tab
        // ---------------------------------------------------------------
        private TabPage BuildDoorsTab()
        {
            var page = new TabPage("Doors") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            // Bulk action buttons
            var btnWidth = 110;
            var openAll = new Button { Text = "Open All", Location = new System.Drawing.Point(10, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Open All Doors", TabIndex = tabIdx++ };
            var closeAll = new Button { Text = "Close All", Location = new System.Drawing.Point(10 + btnWidth + 6, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Close All Doors", TabIndex = tabIdx++ };
            y += 36;
            var armAll = new Button { Text = "Arm All", Location = new System.Drawing.Point(10, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Arm All Doors", TabIndex = tabIdx++ };
            var disarmAll = new Button { Text = "Disarm All", Location = new System.Drawing.Point(10 + btnWidth + 6, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Disarm All Doors", TabIndex = tabIdx++ };
            page.Controls.AddRange(new Control[] { openAll, closeAll, armAll, disarmAll });
            y += 40;

            WireClickButton(openAll, IdOpenAll);
            WireClickButton(closeAll, IdCloseAll);
            WireClickButton(armAll, IdArmAll);
            WireClickButton(disarmAll, IdDisarmAll);

            // Passenger doors — cycle buttons (OPEN → DISARM → CLOSE → ARM)
            // Button text shows the current action, label shows the door name.
            page.Controls.Add(new Label
            {
                Text = "Passenger Doors",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Passenger Doors section"
            });
            y += 26;

            for (int i = 0; i < PaxDoorIds.Length; i++)
            {
                var btn = new Button
                {
                    Text = PaxDoorLabels[i],
                    Location = new System.Drawing.Point(10, y),
                    Size = new System.Drawing.Size(300, 30),
                    AccessibleName = PaxDoorLabels[i],
                    TabIndex = tabIdx++
                };
                page.Controls.Add(btn);
                paxDoorButtons[i] = btn;
                WireClickButton(btn, PaxDoorIds[i]);
                y += 34;
            }

            y += 10;

            // Cargo/utility doors — checkboxes (ticked = open)
            page.Controls.Add(new Label
            {
                Text = "Cargo and Utility Doors",
                Location = new System.Drawing.Point(10, y),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Cargo and Utility Doors section"
            });
            y += 26;

            for (int i = 0; i < CargoDoorIds.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = CargoDoorLabels[i],
                    Location = new System.Drawing.Point(10, y),
                    AutoSize = true,
                    AccessibleName = CargoDoorLabels[i],
                    ThreeState = true,
                    TabIndex = tabIdx++
                };
                page.Controls.Add(cb);
                cargoDoorChecks[i] = cb;
                WireCargoToggle(cb, CargoDoorIds[i]);
                y += 28;
            }

            return page;
        }

        // ---------------------------------------------------------------
        // Ground Service Vehicles tab
        // ---------------------------------------------------------------
        private TabPage BuildVehiclesTab()
        {
            var page = new TabPage("Service Vehicles") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            var btnWidth = 130;
            var reqAll = new Button { Text = "Request All", Location = new System.Drawing.Point(10, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Request All Vehicles", TabIndex = tabIdx++ };
            var relAll = new Button { Text = "Release All", Location = new System.Drawing.Point(10 + btnWidth + 6, y), Size = new System.Drawing.Size(btnWidth, 30), AccessibleName = "Release All Vehicles", TabIndex = tabIdx++ };
            page.Controls.AddRange(new Control[] { reqAll, relAll });
            y += 40;

            WireClickButton(reqAll, IdRequestAllVehicles);
            WireClickButton(relAll, IdReleaseAllVehicles);

            for (int i = 0; i < VehicleIds.Length; i++)
            {
                var cb = new CheckBox
                {
                    Text = VehicleLabels[i],
                    Location = new System.Drawing.Point(10, y),
                    AutoSize = true,
                    AccessibleName = VehicleLabels[i],
                    TabIndex = tabIdx++
                };
                page.Controls.Add(cb);
                vehicleChecks[i] = cb;
                WireToggleCheck(cb, VehicleIds[i]);
                y += 28;
            }

            return page;
        }

        // ---------------------------------------------------------------
        // Ground Maintenance tab
        // ---------------------------------------------------------------
        private TabPage BuildMaintenanceTab()
        {
            var page = new TabPage("Maintenance") { Padding = new Padding(10), AutoScroll = true };
            int y = 10, tabIdx = 0;

            for (int i = 0; i < MaintenanceIds.Length; i++)
            {
                var btn = new Button
                {
                    Text = MaintenanceLabels[i],
                    Location = new System.Drawing.Point(10, y),
                    Size = new System.Drawing.Size(260, 30),
                    AccessibleName = MaintenanceLabels[i],
                    TabIndex = tabIdx++
                };
                page.Controls.Add(btn);
                WireClickButton(btn, MaintenanceIds[i]);
                y += 36;
            }

            return page;
        }

        // ---------------------------------------------------------------
        // Automated Ground Ops tab
        // ---------------------------------------------------------------
        private TabPage BuildAutomatedTab()
        {
            var page = new TabPage("Automated") { Padding = new Padding(10), AutoScroll = true };
            int y = 10;
            const int labelX = 10, valueX = 180, valueWidth = 200;
            int tabIdx = 0;

            // Turn Type is a CYCLE button on the tablet — each click advances
            // to the next state. Mirror it as a button showing current state.
            turnTypeButton = new Button
            {
                Text = "Turn Type",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(260, 30),
                AccessibleName = "Turn Type",
                TabIndex = tabIdx++
            };
            page.Controls.Add(turnTypeButton);
            WireClickButton(turnTypeButton, IdTurnType);
            y += 36;

            autoChocksCheck = new CheckBox
            {
                Text = "Wheel Chocks",
                Location = new System.Drawing.Point(labelX, y),
                AutoSize = true,
                AccessibleName = "Wheel Chocks",
                TabIndex = tabIdx++
            };
            page.Controls.Add(autoChocksCheck);
            WireToggleCheck(autoChocksCheck, IdAutoWheelChocks);
            y += 34;

            page.Controls.Add(new Label { Text = "Target Fuel:", Location = new System.Drawing.Point(labelX, y + 3), AutoSize = true });
            targetFuelBox = new TextBox
            {
                Location = new System.Drawing.Point(valueX, y),
                Size = new System.Drawing.Size(140, 25),
                AccessibleName = "Target Fuel",
                TabIndex = tabIdx++
            };
            targetFuelUnitBox = CreateReadOnlyField("Fuel Unit");
            targetFuelUnitBox.Location = new System.Drawing.Point(valueX + 146, y);
            targetFuelUnitBox.Size = new System.Drawing.Size(50, 22);
            page.Controls.Add(targetFuelBox);
            page.Controls.Add(targetFuelUnitBox);
            y += 32;

            targetFuelOkButton = new Button
            {
                Text = "Set Target Fuel",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(160, 30),
                AccessibleName = "Set Target Fuel",
                TabIndex = tabIdx++
            };
            page.Controls.Add(targetFuelOkButton);
            y += 40;

            // Status fields
            page.Controls.Add(new Label
            {
                Text = "Status",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(400, 20),
                Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                AccessibleName = "Status section"
            });
            y += 26;

            TextBox StatusRow(string label, string accName)
            {
                page.Controls.Add(new Label { Text = label, Location = new System.Drawing.Point(labelX, y + 2), AutoSize = true });
                var box = CreateReadOnlyField(accName);
                box.Location = new System.Drawing.Point(valueX, y);
                box.Size = new System.Drawing.Size(valueWidth, 22);
                box.Text = "";
                box.TabIndex = tabIdx++;
                page.Controls.Add(box);
                y += 28;
                return box;
            }

            upliftBox = StatusRow("Uplift:", "Fuel Uplift");
            fuelRemainingBox = StatusRow("Fuel Remaining:", "Fuel Uplift Remaining");
            targetFuelLabelBox = StatusRow("Target Fuel:", "Target Fuel Status");
            turnTimeBox = StatusRow("Turn Time:", "Turn Time");
            turnTimeRemainingBox = StatusRow("Time Remaining:", "Turn Time Remaining");

            // Mark dirty while editing so the monitor doesn't clobber the
            // user's in-progress typing before they commit.
            targetFuelBox.TextChanged += (_, _) =>
            {
                if (!_suppressWrites) _targetFuelDirty = true;
            };

            void CommitTargetFuel()
            {
                BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
                {
                    ["id"] = IdTargetFuel,
                    ["value"] = targetFuelBox.Text ?? ""
                });
                ClickSimple(IdTargetFuelOk);
                _targetFuelDirty = false;
            }

            targetFuelBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !_suppressWrites)
                {
                    CommitTargetFuel();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            targetFuelOkButton.Click += (_, _) => CommitTargetFuel();

            return page;
        }

        // ---------------------------------------------------------------
        // Shared wiring helpers
        // ---------------------------------------------------------------

        /// <summary>
        /// Wire a CheckBox so that user-initiated changes click the tablet
        /// button. The checkbox state is synced from the tablet on refresh —
        /// "RELEASE" / "OPEN" / "Remove Chocks" text = checked (connected/open).
        /// </summary>
        private void WireToggleCheck(CheckBox cb, string domId)
        {
            cb.CheckedChanged += (_, _) =>
            {
                if (_suppressWrites) return;
                _lastUserAction = DateTime.UtcNow;
                ClickSimple(domId);
            };
        }

        /// <summary>
        /// Wire a three-state cargo door checkbox. User can only toggle
        /// between checked (open) and unchecked (closed). Indeterminate
        /// (opening/closing) is set by the monitor and blocks user input.
        /// </summary>
        private void WireCargoToggle(CheckBox cb, string domId)
        {
            // Prevent user from entering Indeterminate state manually.
            // ThreeState lets us SET it programmatically, but user Space/click
            // should only toggle between Checked and Unchecked.
            cb.CheckStateChanged += (_, _) =>
            {
                if (_suppressWrites) return;
                if (cb.CheckState == CheckState.Indeterminate)
                {
                    // User cycled into Indeterminate — skip to Checked
                    cb.CheckState = CheckState.Checked;
                    return;
                }
                _lastUserAction = DateTime.UtcNow;
                ClickSimple(domId);
            };
        }

        private void WireClickButton(Button btn, string domId)
        {
            btn.Click += (_, _) =>
            {
                ClickSimple(domId);
            };
        }

        /// <summary>
        /// Send a bare .click() without the MouseEvent sequence. Ground Ops
        /// buttons use addEventListener; the full mousedown/mouseup/click
        /// plus .click() fires the handler twice, toggling back to the
        /// original state.
        /// </summary>
        private void ClickSimple(string domId)
        {
            BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string>
            {
                ["id"] = domId,
                ["simple"] = "true"
            });
        }

        // ---------------------------------------------------------------
        // Activation / refresh
        // ---------------------------------------------------------------

        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            ArmLoadAnnouncement();
            // Start continuous monitoring — polls every 1s while the
            // panel is active, catching door animations, connection
            // transitions, and any other state changes automatically.
            StartMonitor();
        }

        public override void OnDeactivated()
        {
            StopMonitor();
        }

        private void StartMonitor()
        {
            StopMonitor();
            _monitor = new System.Windows.Forms.Timer { Interval = 1000 };
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
            // Immediate first read
            ScheduleRefreshAfter(500);
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

        private void ScheduleRefreshAfter(int ms)
        {
            var t = new System.Windows.Forms.Timer { Interval = ms };
            t.Tick += (_, _) =>
            {
                t.Stop(); t.Dispose();
                if (!IsDisposed) RequestAllValues();
            };
            t.Start();
        }

        private void RequestAllValues()
        {
            if (!BridgeServer.IsBridgeConnected) return;

            var ids = new List<string>
            {
                // Connections
                IdWheelChocks, IdAirStart, IdAirCond,
                IdGroundPower, IdGroundPowerType,
                IdJetway, IdPaxEntry,
                // Automated
                IdTurnType, IdAutoWheelChocks,
                IdTargetFuel, IdTargetFuelUnit,
                IdUplift, IdFuelRemaining, IdTargetFuelLabel,
                IdTurnTime, IdTurnTimeRemaining
            };
            ids.AddRange(PaxDoorIds);
            ids.AddRange(CargoDoorIds);
            ids.AddRange(VehicleIds);

            BridgeServer.EnqueueCommand("read_values", new Dictionary<string, string>
            {
                ["tag"] = ValuesTag,
                ["ids"] = string.Join(",", ids)
            });
        }

        // ---------------------------------------------------------------
        // State handling
        // ---------------------------------------------------------------

        protected override void HandleStateUpdate(EFBStateUpdateEventArgs e)
        {
            if (e.Data.GetValueOrDefault("_tag", "") != ValuesTag) return;
            if (e.Type == "values")
                ApplyValues(e.Data);
        }

        private void ApplyValues(Dictionary<string, string> d)
        {
            _suppressWrites = true;
            try
            {
                AnnounceLoadedIfPending();

                // Ground Connections — show actual tablet button text as the
                // label so the user sees the real state (REQUEST, RELEASE,
                // CONNECTING, CHOCKS INHIBIT, etc.). Checked = not REQUEST.
                SyncToggleWithRawLabel(chocksCheck, d.GetValueOrDefault(IdWheelChocks, ""), "Set");
                SyncToggleWithRawLabel(airStartCheck, d.GetValueOrDefault(IdAirStart, ""), "REQUEST");
                SyncToggleWithRawLabel(airCondCheck, d.GetValueOrDefault(IdAirCond, ""), "REQUEST");
                SyncToggleWithRawLabel(groundPowerCheck, d.GetValueOrDefault(IdGroundPower, ""), "REQUEST");

                string powerType = d.GetValueOrDefault(IdGroundPowerType, "");
                groundPowerTypeButton.Text = "Power Type: " + (string.IsNullOrWhiteSpace(powerType) ? "--" : powerType);
                groundPowerTypeButton.AccessibleName = "Ground Power Type: " + (string.IsNullOrWhiteSpace(powerType) ? "unknown" : powerType);

                string paxEntry = d.GetValueOrDefault(IdPaxEntry, "");
                paxEntryButton.Text = "Passenger Entry: " + (string.IsNullOrWhiteSpace(paxEntry) ? "--" : paxEntry);
                paxEntryButton.AccessibleName = "Passenger Entry: " + (string.IsNullOrWhiteSpace(paxEntry) ? "unknown" : paxEntry);

                // Passenger doors — show label + current action text
                for (int i = 0; i < PaxDoorIds.Length; i++)
                {
                    string doorText = d.GetValueOrDefault(PaxDoorIds[i], "");
                    SyncButtonLabel(paxDoorButtons[i], PaxDoorLabels[i], doorText);
                }

                // Cargo doors — three-state: checked=open, unchecked=closed,
                // indeterminate=opening/closing transition
                for (int i = 0; i < CargoDoorIds.Length; i++)
                {
                    string doorText = (d.GetValueOrDefault(CargoDoorIds[i], "") ?? "").Trim().ToUpperInvariant();
                    SyncCargoCheckState(cargoDoorChecks[i], doorText);
                }

                // Vehicles — checked unless text is "REQUEST"
                for (int i = 0; i < VehicleIds.Length; i++)
                {
                    string vehText = d.GetValueOrDefault(VehicleIds[i], "");
                    SyncToggleWithRawLabel(vehicleChecks[i], vehText, "REQUEST");
                }

                // Automated Ground Ops
                string turnText = d.GetValueOrDefault(IdTurnType, "");
                SyncButtonRaw(turnTypeButton, "Turn Type", turnText);
                SyncToggleWithRawLabel(autoChocksCheck, d.GetValueOrDefault(IdAutoWheelChocks, ""), "Set");

                // Only update the target fuel text when the user isn't
                // actively editing (focused OR has uncommitted changes).
                if (!targetFuelBox.Focused && !_targetFuelDirty)
                    targetFuelBox.Text = d.GetValueOrDefault(IdTargetFuel, "");
                targetFuelUnitBox.Text = d.GetValueOrDefault(IdTargetFuelUnit, "");

                upliftBox.Text = d.GetValueOrDefault(IdUplift, "");
                fuelRemainingBox.Text = d.GetValueOrDefault(IdFuelRemaining, "");
                targetFuelLabelBox.Text = d.GetValueOrDefault(IdTargetFuelLabel, "");
                turnTimeBox.Text = d.GetValueOrDefault(IdTurnTime, "");
                turnTimeRemainingBox.Text = d.GetValueOrDefault(IdTurnTimeRemaining, "");
            }
            finally
            {
                _suppressWrites = false;
            }
        }

        /// <summary>
        /// Sync checkbox to tablet state using the raw button text as the label.
        /// Checked when the inactiveKeyword is NOT present in the text.
        /// Shows the actual tablet text so the user sees real state
        /// (REQUEST, RELEASE, CONNECTING, CHOCKS INHIBIT, etc.).
        /// </summary>
        private void SyncToggleWithRawLabel(CheckBox cb, string buttonText, string inactiveKeyword)
        {
            bool isActive = !string.IsNullOrEmpty(buttonText)
                && buttonText.IndexOf(inactiveKeyword, StringComparison.OrdinalIgnoreCase) < 0;
            bool inCooldown = (DateTime.UtcNow - _lastUserAction).TotalMilliseconds < UserActionCooldownMs;
            if (!inCooldown && cb.Checked != isActive)
                cb.Checked = isActive;
            string baseLabel = cb.Tag as string ?? cb.AccessibleName;
            if (cb.Tag == null) cb.Tag = cb.AccessibleName;
            string rawState = string.IsNullOrWhiteSpace(buttonText) ? "" : " " + buttonText;
            string newName = baseLabel + rawState;
            if (cb.AccessibleName != newName) cb.AccessibleName = newName;
            if (cb.Text != newName) cb.Text = newName;
        }

        /// <summary>
        /// Update a button's text and accessible name to show the door/item
        /// label plus the current tablet state (e.g. "Entry 1 Left OPEN").
        /// </summary>
        /// <summary>
        /// Map PMDG's action text to the current door state.
        /// PMDG shows what clicking WILL do, not the current state.
        /// </summary>
        private static string DoorActionToState(string actionText)
        {
            var upper = (actionText ?? "").Trim().ToUpperInvariant();
            switch (upper)
            {
                case "OPEN": return "Closed";
                case "CLOSE": return "Open";
                case "CLOSING": return "Closing";
                case "OPENING": return "Opening";
                case "ARM": return "Disarmed";
                case "DISARM": return "Armed";
                default: return actionText ?? "";
            }
        }

        /// <summary>
        /// Sync a three-state cargo door checkbox.
        /// CLOSE/OPEN = settled states (checked/unchecked).
        /// OPENING/CLOSING = transitional (indeterminate).
        /// </summary>
        private void SyncCargoCheckState(CheckBox cb, string actionText)
        {
            bool inCooldown = (DateTime.UtcNow - _lastUserAction).TotalMilliseconds < UserActionCooldownMs;
            CheckState target;
            string stateLabel;
            if (actionText.IndexOf("OPENING", StringComparison.Ordinal) >= 0)
            {
                target = CheckState.Indeterminate;
                stateLabel = "Opening";
            }
            else if (actionText.IndexOf("CLOSING", StringComparison.Ordinal) >= 0)
            {
                target = CheckState.Indeterminate;
                stateLabel = "Closing";
            }
            else if (actionText == "CLOSE")
            {
                target = CheckState.Checked;
                stateLabel = "Open";
            }
            else
            {
                target = CheckState.Unchecked;
                stateLabel = "Closed";
            }

            // Transitional states (indeterminate) always apply immediately
            // so the user sees the animation. Settled states respect cooldown.
            bool isTransition = target == CheckState.Indeterminate;
            if ((isTransition || !inCooldown) && cb.CheckState != target)
                cb.CheckState = target;

            string baseLabel = cb.Tag as string ?? cb.AccessibleName;
            if (cb.Tag == null) cb.Tag = cb.AccessibleName;
            string newName = baseLabel + " " + stateLabel;
            if (cb.AccessibleName != newName) cb.AccessibleName = newName;
            if (cb.Text != newName) cb.Text = newName;
        }

        /// <summary>
        /// Update a button's label to show baseLabel + raw state text (no
        /// door-action-to-state conversion). Used for cycle buttons whose
        /// text is already the state (Turn Type: Off/Long/Short/Custom).
        /// </summary>
        private static void SyncButtonRaw(Button btn, string baseLabel, string stateText)
        {
            string label = string.IsNullOrWhiteSpace(stateText)
                ? baseLabel
                : baseLabel + " " + stateText;
            if (btn.Text != label) btn.Text = label;
            if (btn.AccessibleName != label) btn.AccessibleName = label;
        }

        private static void SyncButtonLabel(Button btn, string baseLabel, string actionText)
        {
            string state = DoorActionToState(actionText);
            string label = string.IsNullOrWhiteSpace(state)
                ? baseLabel
                : baseLabel + " " + state;
            if (btn.Text != label) btn.Text = label;
            if (btn.AccessibleName != label) btn.AccessibleName = label;
        }
    }
}
