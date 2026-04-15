using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.PMDG777.Apps.Performance
{
    /// <summary>
    /// Performance Tool — Take Off page. Drives the tablet's opt_takeoff_* DOM
    /// elements. Each unit-bearing row has a local unit combo that, on change,
    /// clicks the corresponding PMDG preference toggle to actually flip the
    /// tablet's unit — PMDG then re-renders all dependent unit spans and we
    /// read the new numbers directly. No local conversion math.
    /// </summary>
    public class TakeoffPanel : EfbAppPanelBase
    {
        private const string ValuesTag = "perf_takeoff";
        private const string SubPageButtonId = "opt_takeoff";

        // --- DOM id constants ---
        private const string IdIcao = "opt_takeoff_airport_icao";
        private const string IdAirportName = "opt_takeoff_airport_name";
        private const string IdAirportAltitude = "opt_takeoff_airport_altitude";
        private const string IdAirportAltitudeUnit = "opt_takeoff_airport_altitude_unit";
        private const string IdRunwayId = "opt_takeoff_runway_id";
        private const string IdRunwayHeading = "opt_takeoff_runway_heading";
        private const string IdRunwaySlope = "opt_takeoff_runway_slope";
        private const string IdRunwayLength = "opt_takeoff_runway_length";
        private const string IdRunwayLengthUnit = "opt_takeoff_runway_length_unit";
        private const string IdRunwayCondition = "opt_takeoff_runway_condition";
        private const string IdFlaps = "opt_takeoff_flaps";
        private const string IdRating = "opt_takeoff_rating";
        private const string IdAntiIce = "opt_takeoff_anti_ice";
        private const string IdAircon = "opt_takeoff_aircon";
        private const string IdWeight = "opt_takeoff_weight";
        private const string IdWeightUnit = "opt_takeoff_weight_unit";
        private const string IdCg = "opt_takeoff_cg";
        private const string IdWindspeed = "opt_takeoff_windspeed";
        private const string IdWindspeedUnit = "opt_takeoff_windspeed_unit";
        private const string IdOat = "opt_takeoff_oat";
        private const string IdOatUnit = "opt_takeoff_oat_unit";
        private const string IdBarometer = "opt_takeoff_barometer";
        private const string IdBarometerUnit = "opt_takeoff_barometer_unit";
        private const string IdCalculate = "opt_takeoff_calculate";
        private const string IdClear = "opt_takeoff_clear";
        private const string IdImportAircraft = "opt_takeoff_get_aircraftdetails";
        private const string IdImportWeather = "opt_takeoff_get_weather";
        private const string IdImportOfpRoute = "opt_import_ofp_to";
        private const string IdImportOfpWeight = "opt_import_ofp_to_wt";
        private const string IdOutFlaps = "opt_takeoff_output_flaps";
        private const string IdOutN1 = "opt_takeoff_output_n1";
        private const string IdOutRtg = "opt_takeoff_output_rtg";
        private const string IdOutTrim = "opt_takeoff_output_trim";
        private const string IdOutV1 = "opt_takeoff_output_v1";
        private const string IdOutV1Unit = "opt_takeoff_output_v1_unit";
        private const string IdOutVr = "opt_takeoff_output_vr";
        private const string IdOutVrUnit = "opt_takeoff_output_vr_unit";
        private const string IdOutV2 = "opt_takeoff_output_v2";
        private const string IdOutV2Unit = "opt_takeoff_output_v2_unit";
        private const string IdOutVref = "opt_takeoff_output_vref";
        private const string IdOutVrefUnit = "opt_takeoff_output_vref_unit";
        private const string IdOutWeight = "opt_takeoff_output_weight";
        private const string IdOutWeightUnit = "opt_takeoff_output_weight_unit";
        private const string IdOutAccelHeight = "opt_takeoff_output_accelHeight";
        private const string IdOutAccelHeightUnit = "opt_takeoff_output_accelHeight_unit";
        private const string IdOutSelTemp = "opt_takeoff_output_selTemp";
        private const string IdOutSelTempUnit = "opt_takeoff_output_selTemp_unit";

        // --- Preference toggle keys (for driving the unit combos) ---
        private const string PrefWeight = "weight_unit";
        private const string PrefAltitude = "altitude_unit";
        private const string PrefLength = "length_unit";
        private const string PrefTemperature = "temperature_unit";
        private const string PrefPressure = "pressure_unit";
        private const string PrefSpeed = "speed_unit";
        private const string PrefAirspeed = "airspeed_unit";

        // --- Unit option arrays (matched to PMDG's binary toggles) ---
        private static readonly string[] WeightOpts = { "lb", "kg" };
        private static readonly string[] LengthOpts = { "ft", "m" };
        private static readonly string[] TempOpts = { "C", "F" };
        private static readonly string[] PressureOpts = { "hPa", "inHg" };
        private static readonly string[] SpeedOpts = { "kph", "mph" };
        private static readonly string[] AirspeedOpts = { "kts", "kph" };

        // --- Inner class holding the triplet for a unit-bearing field ---
        private class UnitField
        {
            public string ValueId = "";
            public string UnitId = "";
            public string PrefKey = "";
            public string[] Options = Array.Empty<string>();
            public TextBox Box = null!;
            public ComboBox UnitCombo = null!;
            public bool IsOutput;
        }

        // --- Plain controls (no unit) ---
        private TextBox icaoBox = null!;
        private TextBox airportNameBox = null!;
        private TextBox runwayHeadingBox = null!;
        private TextBox runwaySlopeBox = null!;
        private TextBox cgBox = null!;
        private ComboBox runwayIdCombo = null!;
        private ComboBox runwayConditionCombo = null!;
        private ComboBox flapsCombo = null!;
        private ComboBox ratingCombo = null!;
        private ComboBox antiIceCombo = null!;
        private ComboBox airconCombo = null!;
        private TextBox outFlapsValue = null!;
        private TextBox outN1Value = null!;
        private TextBox outRtgValue = null!;
        private TextBox outTrimValue = null!;

        // --- Unit-bearing controls ---
        private UnitField airportAltitudeField = null!;
        private UnitField runwayLengthField = null!;
        private UnitField weightField = null!;
        private UnitField windspeedField = null!;
        private UnitField oatField = null!;
        private UnitField barometerField = null!;
        private UnitField outV1Field = null!;
        private UnitField outVrField = null!;
        private UnitField outV2Field = null!;
        private UnitField outVrefField = null!;
        private UnitField outWeightField = null!;
        private UnitField outAccelHeightField = null!;
        private UnitField outSelTempField = null!;

        // --- Buttons ---
        private Button importOfpRouteButton = null!;
        private Button importOfpWeightButton = null!;
        private Button importAircraftButton = null!;
        private Button importWeatherButton = null!;
        private Button calculateButton = null!;
        private Button clearButton = null!;
        private Button refreshButton = null!;

        private bool _suppressWrites;
        private string _lastAnnouncedV1 = "", _lastAnnouncedVr = "", _lastAnnouncedV2 = "";

        public override Control? InitialFocusControl => icaoBox;

        protected override void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(10);
            AccessibleName = "Take Off Performance";
            AutoScroll = true;

            int y = 10;
            const int labelX = 10;
            const int valueX = 200;
            const int valueWidth = 160;
            const int unitX = valueX + valueWidth + 6;  // 366
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

            TextBox PlainOutputRow(string labelText, string accName)
            {
                Controls.Add(new Label { Text = labelText, Location = new System.Drawing.Point(labelX, y), AutoSize = true });
                var box = CreateReadOnlyField(accName);
                box.Location = new System.Drawing.Point(valueX, y);
                box.Size = new System.Drawing.Size(valueWidth + unitWidth + 6, 22);
                box.Text = "";
                box.TabIndex = tabIdx++;
                Controls.Add(box);
                y += rowHeight;
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
            importOfpRouteButton = InlineButton("Import From OFP (Route)", "Import route and weather from OFP", 260);
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
                new[] { "Dry", "Wet", "Standing Water", "Slush", "Compact Snow", "Dry Snow", "Wet Ice" });
            y += 4;

            // --- Aircraft ---
            SectionHeader("Aircraft");
            importOfpWeightButton = InlineButton("Import From OFP", "Import weight from OFP", 260);
            importAircraftButton = InlineButton("Import From Aircraft", "Import weight and config from aircraft", 260);
            flapsCombo = ComboRow("Flap Config:", "Flap Config", new[] { "OPTIMUM", "5", "15", "20" });
            ratingCombo = ComboRow("Rating:", "Rating", new[] { "OPTIMUM", "TO", "TO1", "TO2" });
            antiIceCombo = ComboRow("A/I Config:", "Anti Ice Config", new[] { "Off", "Engines", "Engines+Wings" });
            airconCombo = ComboRow("A/C Config:", "Air Conditioning Config", new[] { "Auto", "Off", "APU To Packs" });
            weightField = UnitInputRow("Weight:", "Weight", IdWeight, IdWeightUnit, PrefWeight, WeightOpts, false);
            cgBox = PlainInputRow("CG:", "Centre of Gravity");
            y += 4;

            // --- Weather ---
            SectionHeader("Weather");
            importWeatherButton = InlineButton("Import Weather", "Import Weather", 260);
            // Wind on the Take Off page uses airspeed_unit (kts/kph), NOT
            // speed_unit (kph/mph). METAR wind is always knots, so PMDG's
            // Import Weather will only land cleanly when this is set to kts.
            windspeedField = UnitInputRow("Wind:", "Wind", IdWindspeed, IdWindspeedUnit, PrefAirspeed, AirspeedOpts, false);
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
                AccessibleName = "Calculate Takeoff Performance",
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
            outAccelHeightField = UnitInputRow("Accel Height:", "Acceleration Height", IdOutAccelHeight, IdOutAccelHeightUnit, PrefAltitude, LengthOpts, true);
            outFlapsValue = PlainOutputRow("Flaps:", "Flaps output");
            outN1Value = PlainOutputRow("%N1:", "N1 percentage");
            outRtgValue = PlainOutputRow("RTG:", "Rating output");
            outSelTempField = UnitInputRow("Sel Temp:", "Selected Temperature", IdOutSelTemp, IdOutSelTempUnit, PrefTemperature, TempOpts, true);
            outTrimValue = PlainOutputRow("Trim:", "Trim output");
            outV1Field = UnitInputRow("V1:", "V1 speed", IdOutV1, IdOutV1Unit, PrefAirspeed, AirspeedOpts, true);
            outVrField = UnitInputRow("VR:", "VR speed", IdOutVr, IdOutVrUnit, PrefAirspeed, AirspeedOpts, true);
            outV2Field = UnitInputRow("V2:", "V2 speed", IdOutV2, IdOutV2Unit, PrefAirspeed, AirspeedOpts, true);
            outVrefField = UnitInputRow("VRef:", "VRef speed", IdOutVref, IdOutVrefUnit, PrefAirspeed, AirspeedOpts, true);
            outWeightField = UnitInputRow("Weight:", "Max weight output", IdOutWeight, IdOutWeightUnit, PrefWeight, WeightOpts, true);

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
            yield return outAccelHeightField;
            yield return outSelTempField;
            yield return outV1Field;
            yield return outVrField;
            yield return outV2Field;
            yield return outVrefField;
            yield return outWeightField;
        }

        private void WireHandlers()
        {
            // ICAO gets a special handler: after writing the value, wait for
            // PMDG to populate the runway dropdown, then fetch its options.
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
            WirePlainTextInput(cgBox, IdCg);

            // Unit field text boxes — plain pass-through write (no conversion)
            foreach (var f in AllUnitFields())
            {
                if (f.IsOutput) continue;
                WirePlainTextInput(f.Box, f.ValueId);
            }

            // Unit combos — on change, send set_preference to flip the PMDG
            // preference toggle, then schedule a refresh to read the newly-
            // formatted numbers PMDG just re-rendered.
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
                    // Give PMDG a beat to re-render dependent unit spans and
                    // numeric conversions, then re-read everything.
                    ScheduleRefreshAfter(400);
                };
            }

            WireComboSelect(runwayIdCombo, IdRunwayId, useDataValue: false);
            WireComboSelect(runwayConditionCombo, IdRunwayCondition, useDataValue: true);
            WireComboSelect(flapsCombo, IdFlaps, useDataValue: true);
            WireComboSelect(ratingCombo, IdRating, useDataValue: true);
            WireComboSelect(antiIceCombo, IdAntiIce, useDataValue: true);
            WireComboSelect(airconCombo, IdAircon, useDataValue: true);

            // Import buttons
            importOfpRouteButton.Click += (_, _) =>
            {
                BridgeServer.EnqueueCommand("click_by_id",
                    new Dictionary<string, string> { ["id"] = IdImportOfpRoute });
                ScheduleRefreshAfter(700);
                ScheduleRefreshAfter(2500);
                ScheduleRefreshAfter(5000);
            };
            importOfpWeightButton.Click += (_, _) =>
            {
                BridgeServer.EnqueueCommand("click_by_id",
                    new Dictionary<string, string> { ["id"] = IdImportOfpWeight });
                ScheduleRefreshAfter(700);
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
                // Weather fetch is async — OAT/QNH land fast but wind can be
                // slow. Triple refresh catches whichever stage it's at.
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
                BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = IdCalculate });
                ScheduleRefreshAfter(800);
                ScheduleRefreshAfter(2500);
            };
        }

        private void WirePlainTextInput(TextBox box, string domId)
        {
            box.LostFocus += (_, _) =>
            {
                if (_suppressWrites || box.ReadOnly) return;
                BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
                {
                    ["id"] = domId,
                    ["value"] = box.Text ?? ""
                });
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && !box.ReadOnly && !_suppressWrites)
                {
                    BridgeServer.EnqueueCommand("set_input_by_id", new Dictionary<string, string>
                    {
                        ["id"] = domId,
                        ["value"] = box.Text ?? ""
                    });
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void WireComboSelect(ComboBox combo, string domId, bool useDataValue)
        {
            combo.SelectedIndexChanged += (_, _) =>
            {
                if (_suppressWrites) return;
                if (combo.SelectedIndex < 0) return;
                string value = useDataValue
                    ? combo.SelectedIndex.ToString()
                    : (combo.SelectedItem?.ToString() ?? "");
                BridgeServer.EnqueueCommand("set_select_by_id", new Dictionary<string, string>
                {
                    ["id"] = domId,
                    ["value"] = value
                });
                ScheduleRefreshAfter(600);
            };
        }

        public override void OnActivated()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            BridgeServer.EnqueueCommand("click_by_id", new Dictionary<string, string> { ["id"] = SubPageButtonId });
            ScheduleRefreshAfter(1500);
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

        private void ScheduleRunwayRefresh()
        {
            var t = new System.Windows.Forms.Timer { Interval = 900 };
            t.Tick += (_, _) =>
            {
                t.Stop(); t.Dispose();
                if (IsDisposed || !BridgeServer.IsBridgeConnected) return;
                BridgeServer.EnqueueCommand("get_select_options", new Dictionary<string, string>
                {
                    ["tag"] = ValuesTag,
                    ["id"] = IdRunwayId
                });
            };
            t.Start();
        }

        private void RequestAllValues()
        {
            if (!BridgeServer.IsBridgeConnected) return;
            string[] ids = {
                IdIcao, IdAirportName, IdAirportAltitude,
                IdRunwayId, IdRunwayHeading, IdRunwaySlope, IdRunwayLength, IdRunwayCondition,
                IdFlaps, IdRating, IdAntiIce, IdAircon, IdWeight, IdCg,
                IdWindspeed, IdOat, IdBarometer,
                IdAirportAltitudeUnit, IdRunwayLengthUnit, IdWeightUnit,
                IdWindspeedUnit, IdOatUnit, IdBarometerUnit,
                IdOutFlaps, IdOutN1, IdOutRtg, IdOutTrim,
                IdOutV1, IdOutV1Unit, IdOutVr, IdOutVrUnit,
                IdOutV2, IdOutV2Unit, IdOutVref, IdOutVrefUnit,
                IdOutWeight, IdOutWeightUnit,
                IdOutAccelHeight, IdOutAccelHeightUnit,
                IdOutSelTemp, IdOutSelTempUnit
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

        private void ApplyRunwayOptions(Dictionary<string, string> d)
        {
            if (!int.TryParse(d.GetValueOrDefault("count", "0"), out int count)) return;
            _suppressWrites = true;
            try
            {
                runwayIdCombo.Items.Clear();
                for (int i = 0; i < count; i++)
                {
                    string v = d.GetValueOrDefault($"option_{i}_value", "");
                    if (string.IsNullOrWhiteSpace(v))
                        v = d.GetValueOrDefault($"option_{i}_text", "");
                    if (!string.IsNullOrWhiteSpace(v))
                        runwayIdCombo.Items.Add(v);
                }
                string selected = d.GetValueOrDefault("selected_text", "");
                if (!string.IsNullOrEmpty(selected))
                    SelectComboByText(runwayIdCombo, selected);
                Announcer.Announce($"{count} runways loaded");
            }
            finally
            {
                _suppressWrites = false;
            }
        }

        private void ApplyValues(Dictionary<string, string> d)
        {
            _suppressWrites = true;
            try
            {
                // Non-unit text inputs
                SetTextIfChanged(icaoBox, d.GetValueOrDefault(IdIcao, ""));
                SetTextIfChanged(airportNameBox, d.GetValueOrDefault(IdAirportName, ""));
                SetTextIfChanged(runwayHeadingBox, d.GetValueOrDefault(IdRunwayHeading, ""));
                SetTextIfChanged(runwaySlopeBox, d.GetValueOrDefault(IdRunwaySlope, ""));
                SetTextIfChanged(cgBox, d.GetValueOrDefault(IdCg, ""));

                // Unit fields: take the raw tablet value verbatim, and sync
                // the unit combo selection to the tablet's currently-rendered
                // unit label so the user sees the reality of what's on the
                // tablet (no conversion, no guessing).
                foreach (var f in AllUnitFields())
                {
                    string raw = d.GetValueOrDefault(f.ValueId, "");
                    SetTextIfChanged(f.Box, raw);
                    string tabletUnit = d.GetValueOrDefault(f.UnitId, "");
                    SyncComboToTabletUnit(f.UnitCombo, tabletUnit);
                }

                // Enum combos (read the selected-option text)
                SelectComboByText(runwayConditionCombo, d.GetValueOrDefault(IdRunwayCondition, ""));
                SelectComboByText(flapsCombo, d.GetValueOrDefault(IdFlaps, ""));
                SelectComboByText(ratingCombo, d.GetValueOrDefault(IdRating, ""));
                SelectComboByText(antiIceCombo, d.GetValueOrDefault(IdAntiIce, ""));
                SelectComboByText(airconCombo, d.GetValueOrDefault(IdAircon, ""));

                // Runway ID combo — reflect whatever the tablet currently has
                string currentRw = d.GetValueOrDefault(IdRunwayId, "");
                if (!string.IsNullOrEmpty(currentRw))
                {
                    if (!runwayIdCombo.Items.Contains(currentRw))
                        runwayIdCombo.Items.Add(currentRw);
                    SelectComboByText(runwayIdCombo, currentRw);
                }

                // Plain outputs (no unit math)
                outFlapsValue.Text = d.GetValueOrDefault(IdOutFlaps, "");
                outN1Value.Text = d.GetValueOrDefault(IdOutN1, "");
                outRtgValue.Text = d.GetValueOrDefault(IdOutRtg, "");
                outTrimValue.Text = d.GetValueOrDefault(IdOutTrim, "");

                // V-speed announcement on fresh calculation
                string v1 = outV1Field.Box.Text, vr = outVrField.Box.Text, v2 = outV2Field.Box.Text;
                if (!string.IsNullOrWhiteSpace(v1) && !string.IsNullOrWhiteSpace(vr) && !string.IsNullOrWhiteSpace(v2))
                {
                    if (_lastAnnouncedV1 != v1 || _lastAnnouncedVr != vr || _lastAnnouncedV2 != v2)
                    {
                        _lastAnnouncedV1 = v1;
                        _lastAnnouncedVr = vr;
                        _lastAnnouncedV2 = v2;
                        Announcer.Announce($"V1 {v1}, VR {vr}, V2 {v2}");
                    }
                }
            }
            finally
            {
                _suppressWrites = false;
            }
        }

        /// <summary>
        /// Match the combo selection to the tablet's currently-rendered unit
        /// label so the user's local combo always reflects reality after a
        /// refresh. Tolerant of case and plural (kts/kt, lbs/lb, HPA/hPa).
        /// </summary>
        private static void SyncComboToTabletUnit(ComboBox combo, string tabletUnit)
        {
            if (string.IsNullOrWhiteSpace(tabletUnit)) return;
            string target = Normalize(tabletUnit);
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string item = Normalize(combo.Items[i]?.ToString() ?? "");
                if (item == target)
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }

        private static string Normalize(string s)
        {
            string n = (s ?? "").Trim().ToLowerInvariant();
            if (n.Length > 1 && n[n.Length - 1] == 's') n = n.Substring(0, n.Length - 1);
            return n;
        }

        private static void SetTextIfChanged(TextBox box, string value)
        {
            if (box.Text != value) box.Text = value;
        }

        private static void SelectComboByText(ComboBox combo, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(combo.Items[i]?.ToString(), text, StringComparison.OrdinalIgnoreCase))
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }
    }
}
