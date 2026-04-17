using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps.Performance
{
    /// <summary>
    /// Performance Tool — Landing Enroute page. In-flight calculation using
    /// actual aircraft weight, with operational parameters (reversers,
    /// autobrakes, non-normal conditions, Vref additive). Structurally
    /// mirrors TakeoffPanel / LandingDispatchPanel.
    /// </summary>
    public class LandingEnroutePanel : PerformancePanelBase
    {
        private const string SubPageButtonId = "opt_landinginflight";

        // --- DOM id constants ---
        private const string IdIcao = "opt_landingenroute_airport_icao";
        private const string IdAirportName = "opt_landingenroute_airport_name";
        private const string IdAirportAltitude = "opt_landingenroute_airport_altitude";
        private const string IdAirportAltitudeUnit = "opt_landingenroute_airport_altitude_unit";
        private const string IdRunwayId = "opt_landingenroute_runway_id";
        private const string IdRunwayHeading = "opt_landingenroute_runway_heading";
        private const string IdRunwaySlope = "opt_landingenroute_runway_slope";
        private const string IdRunwayLength = "opt_landingenroute_runway_length";
        private const string IdRunwayLengthUnit = "opt_landingenroute_runway_length_unit";
        private const string IdRunwayCondition = "opt_landingenroute_runway_condition";
        private const string IdImportOfp = "opt_import_ofp_le";
        private const string IdImportAircraft = "opt_landingenroute_get_aircraftdetails";
        private const string IdWeight = "opt_landingenroute_weight";
        private const string IdWeightUnit = "opt_landingenroute_weight_unit";
        private const string IdFlaps = "opt_landingenroute_flaps";
        private const string IdAircon = "opt_landingenroute_aircon";
        private const string IdAntiIce = "opt_landingenroute_anti_ice";
        private const string IdReverser = "opt_landingenroute_reverser_config";
        private const string IdAutobrakes = "opt_landingenroute_autobrakes";
        private const string IdNonNormal = "opt_landingenroute_nonnormal_condition";
        private const string IdVrefAdd = "opt_landingenroute_vref_add";
        private const string IdImportWeather = "opt_landingenroute_get_weather";
        private const string IdWindspeed = "opt_landingenroute_windspeed";
        private const string IdWindspeedUnit = "opt_landingenroute_windspeed_unit";
        private const string IdOat = "opt_landingenroute_oat";
        private const string IdOatUnit = "opt_landingenroute_oat_unit";
        private const string IdBarometer = "opt_landingenroute_barometer";
        private const string IdBarometerUnit = "opt_landingenroute_barometer_unit";
        private const string IdCalculate = "opt_landingenroute_calculate";
        private const string IdClear = "opt_landingenroute_clear";

        // Outputs
        private const string IdOutWeight = "opt_enroute_output_weightLb";
        private const string IdOutWeightUnit = "opt_enroute_output_weightLb_unit";
        private const string IdOutVref = "opt_enroute_output_vref";
        private const string IdOutVrefUnit = "opt_enroute_output_vref_unit";
        private const string IdOutReqLandDist = "opt_enroute_output_landDistRequired";
        private const string IdOutReqLandDistUnit = "opt_enroute_output_landDistRequired_unit";
        private const string IdOutAvailLandDist = "opt_enroute_output_landDistAvailable";
        private const string IdOutAvailLandDistUnit = "opt_enroute_output_landDistAvailable_unit";

        // --- Preference toggle keys ---
        private const string PrefAltitude = "altitude_unit";
        private const string PrefLength = "length_unit";
        private const string PrefTemperature = "temperature_unit";
        private const string PrefPressure = "pressure_unit";
        private const string PrefSpeed = "speed_unit";
        private const string PrefAirspeed = "airspeed_unit";
        private const string PrefWeight = "weight_unit";

        // --- Unit option arrays ---
        private static readonly string[] LengthOpts = { "ft", "m" };
        private static readonly string[] TempOpts = { "C", "F" };
        private static readonly string[] PressureOpts = { "hPa", "inHg" };
        private static readonly string[] SpeedOpts = { "kts", "mps" };
        private static readonly string[] AirspeedOpts = { "kts", "kph" };
        private static readonly string[] WeightOpts = { "lb", "kg" };

        // --- Plain controls ---
        private TextBox icaoBox = null!;
        private TextBox airportNameBox = null!;
        private TextBox runwayHeadingBox = null!;
        private TextBox runwaySlopeBox = null!;
        private ComboBox runwayIdCombo = null!;
        private ComboBox runwayConditionCombo = null!;
        private ComboBox flapsCombo = null!;
        private ComboBox airconCombo = null!;
        private ComboBox antiIceCombo = null!;
        private ComboBox reverserCombo = null!;
        private ComboBox autobrakesCombo = null!;
        private ComboBox nonNormalCombo = null!;
        private ComboBox vrefAddCombo = null!;

        // --- Unit-bearing controls ---
        private UnitField airportAltitudeField = null!;
        private UnitField runwayLengthField = null!;
        private UnitField weightField = null!;
        private UnitField windspeedField = null!;
        private UnitField oatField = null!;
        private UnitField barometerField = null!;
        private UnitField outWeightField = null!;
        private UnitField outVrefField = null!;
        private UnitField outReqLandDistField = null!;
        private UnitField outAvailLandDistField = null!;

        // --- Buttons ---
        private Button importOfpButton = null!;
        private Button importAircraftButton = null!;
        private Button importWeatherButton = null!;
        private Button calculateButton = null!;
        private Button clearButton = null!;
        private Button refreshButton = null!;

        public override Control? InitialFocusControl => icaoBox;

        protected override string ValuesTag => "perf_landingenroute";
        protected override ComboBox RunwayIdCombo => runwayIdCombo;
        protected override string RunwayIdDomId => IdRunwayId;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Landing Enroute Performance";
            AutoScroll = true;

            int y = 10;
            const int labelX = 10;
            const int valueX = 200;
            const int valueWidth = 160;
            const int unitX = valueX + valueWidth + 6;
            const int unitWidth = 80;
            const int rowHeight = 28;
            int tabIdx = 0;

            void SectionHeader(string text)
            {
                Controls.Add(new Label
                {
                    Text = text,
                    Location = new System.Drawing.Point(labelX, y),
                    Size = new System.Drawing.Size(500, 20),
                    Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold),
                    AccessibleName = text + " section"
                });
                y += rowHeight;
            }

            TextBox PlainInputRow(string labelText, string accName, bool readOnly = false)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                var box = new TextBox
                {
                    Location = new System.Drawing.Point(valueX, y),
                    Size = new System.Drawing.Size(valueWidth + unitWidth + 6, 25),
                    AccessibleName = accName,
                    ReadOnly = readOnly,
                    TabIndex = tabIdx++
                };
                Controls.Add(box);
                y += rowHeight + 2;
                return box;
            }

            UnitField UnitInputRow(string labelText, string accName, string valueId, string unitId, string prefKey, string[] options, bool isOutput)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                TextBox box;
                if (isOutput)
                {
                    box = CreateReadOnlyField(accName);
                    box.Location = new System.Drawing.Point(valueX, y);
                    box.Size = new System.Drawing.Size(valueWidth, 22);
                    box.Text = "";
                }
                else
                {
                    box = new TextBox
                    {
                        Location = new System.Drawing.Point(valueX, y),
                        Size = new System.Drawing.Size(valueWidth, 25),
                        AccessibleName = accName
                    };
                }
                box.AccessibleName = accName;
                box.TabIndex = tabIdx++;
                Controls.Add(box);
                var combo = new ComboBox
                {
                    Location = new System.Drawing.Point(unitX, y),
                    Size = new System.Drawing.Size(unitWidth, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = accName + " unit",
                    TabIndex = tabIdx++
                };
                combo.Items.AddRange(options);
                combo.SelectedIndex = 0;
                Controls.Add(combo);
                y += rowHeight + 2;
                return new UnitField
                {
                    ValueId = valueId,
                    UnitId = unitId,
                    PrefKey = prefKey,
                    Options = options,
                    Box = box,
                    UnitCombo = combo,
                    IsOutput = isOutput
                };
            }

            ComboBox ComboRow(string labelText, string accName, string[] items)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                var combo = new ComboBox
                {
                    Location = new System.Drawing.Point(valueX, y),
                    Size = new System.Drawing.Size(valueWidth + unitWidth + 6, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    AccessibleName = accName,
                    TabIndex = tabIdx++
                };
                combo.Items.AddRange(items);
                Controls.Add(combo);
                y += rowHeight + 2;
                return combo;
            }

            Button InlineButton(string text, string accName, int width)
            {
                var btn = new Button
                {
                    Text = text,
                    Location = new System.Drawing.Point(labelX, y),
                    Size = new System.Drawing.Size(width, 30),
                    AccessibleName = accName,
                    TabIndex = tabIdx++
                };
                Controls.Add(btn);
                y += 36;
                return btn;
            }

            // --- Airport ---
            SectionHeader("Airport");
            importOfpButton = InlineButton("Import From OFP", "Import airport and weather from OFP", 260);
            icaoBox = PlainInputRow("Airport ICAO:", "Airport ICAO");
            airportNameBox = PlainInputRow("Airport Name:", "Airport Name");
            airportAltitudeField = UnitInputRow("Elevation:", "Elevation", IdAirportAltitude, IdAirportAltitudeUnit, PrefAltitude, LengthOpts, false);
            y += 4;

            // --- Runway ---
            SectionHeader("Runway");
            Controls.Add(new Label { Text = "Runway ID:", Location = new System.Drawing.Point(labelX, y), AutoSize = true });
            runwayIdCombo = new ComboBox
            {
                Location = new System.Drawing.Point(valueX, y),
                Size = new System.Drawing.Size(valueWidth + unitWidth + 6, 25),
                DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "Runway ID",
                TabIndex = tabIdx++
            };
            Controls.Add(runwayIdCombo);
            y += rowHeight + 2;
            runwayHeadingBox = PlainInputRow("Runway Heading:", "Runway Heading", readOnly: true);
            runwaySlopeBox = PlainInputRow("Runway Slope %:", "Runway Slope percent", readOnly: true);
            runwayLengthField = UnitInputRow("Runway Length:", "Runway Length", IdRunwayLength, IdRunwayLengthUnit, PrefLength, LengthOpts, false);
            runwayConditionCombo = ComboRow("Runway Condition:", "Runway Condition",
                new[] { "Dry", "Good", "Good/Medium", "Medium", "Medium/Poor", "Poor" });
            y += 4;

            // --- Aircraft ---
            SectionHeader("Aircraft");
            importAircraftButton = InlineButton("Import Aircraft Details", "Import weight and config from aircraft", 260);
            weightField = UnitInputRow("Weight:", "Weight", IdWeight, IdWeightUnit, PrefWeight, WeightOpts, false);
            flapsCombo = ComboRow("Flap Config:", "Flap Config", new[] { "25", "30" });
            airconCombo = ComboRow("A/C Config:", "Air Conditioning Config", new[] { "A/C ON", "A/C OFF" });
            antiIceCombo = ComboRow("A/I Config:", "Anti Ice Config", new[] { "Eng+Wings Auto", "Off", "Engines+Wings", "Engines" });
            reverserCombo = ComboRow("Reverser Config:", "Reverser Config", new[] { "NO CREDIT", "ALL OP", "ONE INOP" });
            autobrakesCombo = ComboRow("Autobrakes:", "Autobrakes", new[] { "Max Manual", "Autobrakes1", "Autobrakes2", "Autobrakes3", "Autobrakes4", "Max Auto" });
            nonNormalCombo = ComboRow("Non Normal:", "Non Normal Condition",
                new[] { "None",
                    "ANTISKID INOP (F25)", "ANTISKID INOP (F30)",
                    "ENG SHUTDOWN L/R (F20)", "ENG SHUTDOWN L/R (F30)",
                    "FLAP/SLAT CONTROL (F20)",
                    "FLAPS DRIVE (F5)", "FLAPS DRIVE (F5-F20)", "FLAPS DRIVE (F20)",
                    "FLAPS PRI FAIL (F20)", "FLTCTRL MODE FAIL (F20)",
                    "HYD PRESS SYS C (F20)",
                    "HYD PRESS SYS L (F25)", "HYD PRESS SYS L (F30)", "HYD PRESS SYS L+C (F20)", "HYD PRESS SYS L+R (F20)",
                    "HYD PRESS SYS R (F25)", "HYD PRESS SYS R (F30)", "HYD PRESS SYS R+C (F20)",
                    "PITCH UP AUTHRTY (F15)", "PITCH UP AUTHRTY (F20)",
                    "PRI FLT COMPUTERS (F20)", "SLATS DRIVE (F20)", "STABILIZER (F20)" });
            vrefAddCombo = ComboRow("Vref Add:", "Vref Add",
                new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10",
                        "11", "12", "13", "14", "15", "16", "17", "18", "19", "20" });
            y += 4;

            // --- Weather ---
            SectionHeader("Weather");
            importWeatherButton = InlineButton("Import Weather", "Import Weather", 260);
            windspeedField = UnitInputRow("Wind:", "Wind", IdWindspeed, IdWindspeedUnit, PrefSpeed, SpeedOpts, false);
            oatField = UnitInputRow("OAT:", "Outside Air Temperature", IdOat, IdOatUnit, PrefTemperature, TempOpts, false);
            barometerField = UnitInputRow("QNH:", "QNH", IdBarometer, IdBarometerUnit, PrefPressure, PressureOpts, false);
            y += 4;

            // --- Actions ---
            SectionHeader("Actions");
            calculateButton = new Button
            {
                Text = "Calculate",
                Location = new System.Drawing.Point(labelX, y),
                Size = new System.Drawing.Size(130, 30),
                AccessibleName = "Calculate Landing Enroute",
                TabIndex = tabIdx++
            };
            clearButton = new Button
            {
                Text = "Clear",
                Location = new System.Drawing.Point(labelX + 140, y),
                Size = new System.Drawing.Size(130, 30),
                AccessibleName = "Clear All Inputs",
                TabIndex = tabIdx++
            };
            refreshButton = new Button
            {
                Text = "Refresh",
                Location = new System.Drawing.Point(labelX + 280, y),
                Size = new System.Drawing.Size(130, 30),
                AccessibleName = "Re-read all fields from tablet",
                TabIndex = tabIdx++
            };
            refreshButton.Click += (_, _) =>
            {
                if (BridgeServer.IsBridgeConnected) RequestAllValues();
            };
            Controls.Add(calculateButton);
            Controls.Add(clearButton);
            Controls.Add(refreshButton);
            y += 40;

            // --- Outputs ---
            SectionHeader("Outputs");
            outWeightField = UnitInputRow("Weight:", "Landing weight", IdOutWeight, IdOutWeightUnit, PrefWeight, WeightOpts, true);
            outVrefField = UnitInputRow("VRef:", "VRef speed", IdOutVref, IdOutVrefUnit, PrefAirspeed, AirspeedOpts, true);
            outReqLandDistField = UnitInputRow("Req Landing Dist:", "Required Landing Distance", IdOutReqLandDist, IdOutReqLandDistUnit, PrefLength, LengthOpts, true);
            outAvailLandDistField = UnitInputRow("Avail Landing Dist:", "Available Landing Distance", IdOutAvailLandDist, IdOutAvailLandDistUnit, PrefLength, LengthOpts, true);

            WireHandlers();
        }

        private IEnumerable<UnitField> AllUnitFields()
        {
            yield return airportAltitudeField;
            yield return runwayLengthField;
            yield return weightField;
            yield return windspeedField;
            yield return oatField;
            yield return barometerField;
            yield return outWeightField;
            yield return outVrefField;
            yield return outReqLandDistField;
            yield return outAvailLandDistField;
        }

        private void WireHandlers()
        {
            icaoBox.LostFocus += (_, _) =>
            {
                if (_suppressWrites) return;
                BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
                {
                    ["id"] = IdIcao,
                    ["value"] = icaoBox.Text ?? ""
                });
                ScheduleRunwayRefresh();
            };
            icaoBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !_suppressWrites)
                {
                    BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
                    {
                        ["id"] = IdIcao,
                        ["value"] = icaoBox.Text ?? ""
                    });
                    ScheduleRunwayRefresh();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            WirePlainTextInput(airportNameBox, IdAirportName);

            foreach (var f in AllUnitFields())
            {
                if (f.IsOutput) continue;
                WirePlainTextInput(f.Box, f.ValueId);
            }

            foreach (var f in AllUnitFields())
            {
                var field = f;
                field.UnitCombo.SelectedIndexChanged += (_, _) =>
                {
                    if (_suppressWrites) return;
                    if (field.UnitCombo.SelectedItem == null) return;
                    string targetUnit = field.UnitCombo.SelectedItem.ToString() ?? "";
                    BridgeServer.EnqueueCommand("set_preference", new Dictionary<string, string>
                    {
                        ["key"] = field.PrefKey,
                        ["value"] = targetUnit
                    });
                    ScheduleRefreshAfter(400);
                };
            }

            // Runway combo: debounce — only commit on LostFocus or 2s after
            // last arrow-key scroll, so rapid navigation doesn't fight the user.
            WireRunwayComboDebounced(runwayIdCombo, IdRunwayId);

            WireComboSelect(runwayConditionCombo, IdRunwayCondition, useDataValue: true);
            WireComboSelect(flapsCombo, IdFlaps, useDataValue: true);
            WireComboSelect(airconCombo, IdAircon, useDataValue: true);
            WireComboSelect(antiIceCombo, IdAntiIce, useDataValue: true);
            WireComboSelect(reverserCombo, IdReverser, useDataValue: true);
            WireComboSelect(autobrakesCombo, IdAutobrakes, useDataValue: true);
            WireComboSelect(nonNormalCombo, IdNonNormal, useDataValue: true);
            WireComboSelect(vrefAddCombo, IdVrefAdd, useDataValue: true);

            importOfpButton.Click += (_, _) =>
            {

                BridgeServer.EnqueueCommand("click_by_id",
                    new Dictionary<string, string> { ["id"] = IdImportOfp });
                ScheduleRefreshAfter(700);
                ScheduleRefreshAfter(2500);
                ScheduleRefreshAfter(5000);
            };
            importAircraftButton.Click += (_, _) =>
            {

                BridgeServer.EnqueueCommand("click_by_id",
                    new Dictionary<string, string> { ["id"] = IdImportAircraft });
                ScheduleRefreshAfter(700);
            };
            importWeatherButton.Click += (_, _) =>
            {

                BridgeServer.EnqueueCommand("click_by_id",
                    new Dictionary<string, string> { ["id"] = IdImportWeather });
                ScheduleRefreshAfter(700);
                ScheduleRefreshAfter(2500);
                ScheduleRefreshAfter(5000);
            };
            clearButton.Click += (_, _) =>
            {

                BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = IdClear });
                ScheduleRefreshAfter(500);
            };
            calculateButton.Click += (_, _) =>
            {
                _awaitingCalculation = true;
                BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = IdCalculate });
                ScheduleRefreshAfter(800);
                ScheduleRefreshAfter(2500);
            };
        }

        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            ArmLoadAnnouncement();
            BridgeServer.EnqueueCommand("show_perf_page", new Dictionary<string, string>
            {
                ["buttonId"] = SubPageButtonId,
                ["pageId"] = "LandingEnroute"
            });
            ScheduleRunwayRefresh();
            ScheduleRefreshAfter(1500);
        }

        protected override void RequestAllValues()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            string[] ids = {
                IdIcao, IdAirportName, IdAirportAltitude,
                IdRunwayId, IdRunwayHeading, IdRunwaySlope, IdRunwayLength, IdRunwayCondition,
                IdWeight, IdFlaps, IdAircon, IdAntiIce,
                IdReverser, IdAutobrakes, IdNonNormal, IdVrefAdd,
                IdWindspeed, IdOat, IdBarometer,
                IdAirportAltitudeUnit, IdRunwayLengthUnit, IdWeightUnit,
                IdWindspeedUnit, IdOatUnit, IdBarometerUnit,
                IdOutWeight, IdOutWeightUnit,
                IdOutVref, IdOutVrefUnit,
                IdOutReqLandDist, IdOutReqLandDistUnit,
                IdOutAvailLandDist, IdOutAvailLandDistUnit
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
            switch (e.Type)
            {
                case "values":
                    ApplyValues(e.Data);
                    break;
                case "select_options":
                    if (e.Data.GetValueOrDefault("_id", "") == IdRunwayId)
                        ApplyRunwayOptions(e.Data);
                    break;
            }
        }

        private void ApplyValues(Dictionary<string, string> d)
        {
            _suppressWrites = true;
            try
            {
                AnnounceLoadedIfPending();


                SetTextIfChanged(icaoBox, d.GetValueOrDefault(IdIcao, ""));
                SetTextIfChanged(airportNameBox, d.GetValueOrDefault(IdAirportName, ""));
                SetTextIfChanged(runwayHeadingBox, d.GetValueOrDefault(IdRunwayHeading, ""));
                SetTextIfChanged(runwaySlopeBox, d.GetValueOrDefault(IdRunwaySlope, ""));

                foreach (var f in AllUnitFields())
                {
                    string raw = d.GetValueOrDefault(f.ValueId, "");
                    SetTextIfChanged(f.Box, raw);
                    string tabletUnit = d.GetValueOrDefault(f.UnitId, "");
                    SyncComboToTabletUnit(f.UnitCombo, tabletUnit);
                }

                SelectComboByText(runwayConditionCombo, d.GetValueOrDefault(IdRunwayCondition, ""));
                SelectComboByText(flapsCombo, d.GetValueOrDefault(IdFlaps, ""));
                SelectComboByText(airconCombo, d.GetValueOrDefault(IdAircon, ""));
                SelectComboByText(antiIceCombo, d.GetValueOrDefault(IdAntiIce, ""));
                SelectComboByText(reverserCombo, d.GetValueOrDefault(IdReverser, ""));
                SelectComboByText(autobrakesCombo, d.GetValueOrDefault(IdAutobrakes, ""));
                SelectComboByText(nonNormalCombo, d.GetValueOrDefault(IdNonNormal, ""));
                SelectComboByText(vrefAddCombo, d.GetValueOrDefault(IdVrefAdd, ""));

                string currentRw = d.GetValueOrDefault(IdRunwayId, "");
                if (!string.IsNullOrEmpty(currentRw))
                {
                    if (!runwayIdCombo.Items.Contains(currentRw))
                        runwayIdCombo.Items.Add(currentRw);
                    SelectComboByText(runwayIdCombo, currentRw);
                }

                // Announce results when Calculate completes
                if (_awaitingCalculation)
                {
                    string vref = d.GetValueOrDefault(IdOutVref, "").Trim();
                    if (!string.IsNullOrWhiteSpace(vref))
                    {
                        _awaitingCalculation = false;
                        string reqDist = d.GetValueOrDefault(IdOutReqLandDist, "").Trim();
                        string availDist = d.GetValueOrDefault(IdOutAvailLandDist, "").Trim();
                        string msg = $"VRef {vref}";
                        if (!string.IsNullOrWhiteSpace(reqDist))
                            msg += $", Required Distance {reqDist}";
                        if (!string.IsNullOrWhiteSpace(availDist))
                            msg += $", Available Distance {availDist}";
                        Announcer.Announce(msg);
                    }
                }
            }
            finally
            {
                _suppressWrites = false;
            }
        }

    }
}
