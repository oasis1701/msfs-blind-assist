using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Aircraft;

// =============================================================================
// PMDG 777 — Accessible System Display (synoptic) read-out boxes.
//
// Mirrors the FlyByWire A380 "System Display Page" pattern (a single combo whose
// TryGetDisplayOverride returns the page name + a decoded "Label: value" block),
// but the values come from STOCK SimVars that PMDG drives (oil/hyd/eng/elec/etc.,
// none of which are in the PMDG SDK broadcast) plus a few SDK fields. The status
// box + 3 s auto-refresh + caret-preserving write are all generic MainForm infra
// (any panel listed in GetPanelDisplayVariables gets them), so this is entirely
// self-contained in the aircraft definition. No EICAS message text (WASM-only).
//
// Boeing-realistic: the page set mirrors the real Display Select Panel synoptics
// (ENG / STAT / ELEC / HYD / FUEL / AIR / DOOR / GEAR / F-CTL).
// Read-only: NO auto-announce — the combo announces the page NAME; the content is
// read on demand in the box.
// =============================================================================
public partial class PMDG777Definition
{
    // The synthetic L:var that backs the page-selector combo AND the status box.
    // Written + read-back via the calculator path so its value lands in the cache
    // (so the display override fires), exactly as the A380 does with the real
    // ECAM SD page index.
    internal const string SdPageKey = "PMDG777_SD_PAGE";
    private const string SdPageLVar = "PMDG777_MSFSBA_SD_PAGE";

    private string _sdContent = "";
    private int _sdRefreshSeq;   // "latest request wins" guard
    private int _sdPage;         // currently selected page index

    private static readonly Dictionary<int, string> _sdPageNames = new()
    {
        [0] = "Engine", [1] = "Status", [2] = "Electrical", [3] = "Hydraulics",
        [4] = "Fuel", [5] = "Air", [6] = "Doors", [7] = "Gear", [8] = "Flight Controls"
    };

    // PMDG SDK-broadcast fields (Type = PMDGVar). Everything else in the rows is a
    // stock SimVar (space/colon name → Type = SimVar).
    private static readonly HashSet<string> _sdPmdgVars = new()
    {
        "FUEL_QtyLeft", "FUEL_QtyCenter", "FUEL_QtyRight",
        "AIR_DuctPress_0", "AIR_DuctPress_1", "BRAKES_BrakePressNeedle",
        "IRS_aligned", "APURunning",
        "DOOR_state_0","DOOR_state_1","DOOR_state_2","DOOR_state_3","DOOR_state_4",
        "DOOR_state_5","DOOR_state_6","DOOR_state_7","DOOR_state_8","DOOR_state_9",
        "DOOR_state_10","DOOR_state_11","DOOR_state_13","DOOR_state_14","DOOR_state_15"
    };

    // Read unit per stock SimVar (so the cached value is already in engineering units).
    private static readonly Dictionary<string, string> _sdUnits = new()
    {
        ["TURB ENG N1:1"] = "percent", ["TURB ENG N1:2"] = "percent",
        ["TURB ENG N2:1"] = "percent", ["TURB ENG N2:2"] = "percent",
        ["ENG EXHAUST GAS TEMPERATURE:1"] = "celsius", ["ENG EXHAUST GAS TEMPERATURE:2"] = "celsius",
        ["TURB ENG FUEL FLOW PPH:1"] = "pounds per hour", ["TURB ENG FUEL FLOW PPH:2"] = "pounds per hour",
        ["GENERAL ENG OIL PRESSURE:1"] = "psi", ["GENERAL ENG OIL PRESSURE:2"] = "psi",
        ["GENERAL ENG OIL TEMPERATURE:1"] = "celsius", ["GENERAL ENG OIL TEMPERATURE:2"] = "celsius",
        ["ENG OIL QUANTITY:1"] = "percent", ["ENG OIL QUANTITY:2"] = "percent",
        ["ENG VIBRATION:1"] = "number", ["ENG VIBRATION:2"] = "number",
        ["GENERAL ENG FUEL USED SINCE START:1"] = "pounds", ["GENERAL ENG FUEL USED SINCE START:2"] = "pounds",
        ["ELECTRICAL MAIN BUS VOLTAGE"] = "volts", ["ELECTRICAL BATTERY VOLTAGE"] = "volts",
        ["ELECTRICAL GENALT BUS VOLTAGE:1"] = "volts", ["ELECTRICAL GENALT BUS VOLTAGE:2"] = "volts",
        ["HYDRAULIC PRESSURE:1"] = "psi", ["HYDRAULIC PRESSURE:2"] = "psi", ["HYDRAULIC PRESSURE:3"] = "psi",
        ["HYDRAULIC RESERVOIR PERCENT:1"] = "percent", ["HYDRAULIC RESERVOIR PERCENT:2"] = "percent", ["HYDRAULIC RESERVOIR PERCENT:3"] = "percent",
        ["HYDRAULIC SYSTEM INTEGRITY"] = "percent",
        ["FUEL TOTAL QUANTITY WEIGHT"] = "pounds",
        ["PRESSURIZATION CABIN ALTITUDE"] = "feet", ["PRESSURIZATION CABIN ALTITUDE RATE"] = "feet per minute",
        ["PRESSURIZATION PRESSURE DIFFERENTIAL"] = "psi",
        ["GEAR LEFT POSITION"] = "percent", ["GEAR CENTER POSITION"] = "percent", ["GEAR RIGHT POSITION"] = "percent",
        ["AILERON LEFT DEFLECTION PCT"] = "percent", ["ELEVATOR DEFLECTION PCT"] = "percent", ["RUDDER DEFLECTION PCT"] = "percent",
        ["ELEVATOR TRIM PCT"] = "percent", ["RUDDER TRIM PCT"] = "percent", ["AILERON TRIM PCT"] = "percent",
        ["TRAILING EDGE FLAPS LEFT ANGLE"] = "degrees", ["SPOILERS LEFT POSITION"] = "percent",
        ["TOTAL WEIGHT"] = "pounds", ["CG PERCENT"] = "percent", ["CG PERCENT LATERAL"] = "percent",
        ["TOTAL AIR TEMPERATURE"] = "celsius", ["AMBIENT TEMPERATURE"] = "celsius"
    };

    /// <summary>Decoded rows for one synoptic page: (label, registration-key/var-name, formatter).</summary>
    private List<(string label, string var, Func<double, string> fmt)> PMDG777SdRows(int page)
    {
        string Pct(double v) => $"{v:0} percent";
        string Pct1(double v) => $"{v:0.0} percent";
        string V(double v) => $"{v:0} volts";
        string Psi(double v) => $"{v:0} psi";
        string Cdeg(double v) => $"{v:0} degrees C";
        string Lbs(double v) => $"{v:0} pounds";
        string Pph(double v) => $"{v:0} pounds per hour";
        string GearPos(double v) => v >= 99 ? "down" : v <= 1 ? "up" : $"in transit {v:0} percent";
        string DoorState(double v) => (int)Math.Round(v) switch
        {
            0 => "open", 1 => "closed", 2 => "closed and armed",
            3 => "closing", 4 => "opening", _ => "unknown"
        };

        var r = new List<(string, string, Func<double, string>)>();
        switch (page)
        {
            case 0: // ENG
                r.Add(("Engine 1 N1", "TURB ENG N1:1", Pct1));
                r.Add(("Engine 2 N1", "TURB ENG N1:2", Pct1));
                r.Add(("Engine 1 N2", "TURB ENG N2:1", Pct1));
                r.Add(("Engine 2 N2", "TURB ENG N2:2", Pct1));
                r.Add(("Engine 1 EGT", "ENG EXHAUST GAS TEMPERATURE:1", Cdeg));
                r.Add(("Engine 2 EGT", "ENG EXHAUST GAS TEMPERATURE:2", Cdeg));
                r.Add(("Engine 1 Fuel Flow", "TURB ENG FUEL FLOW PPH:1", Pph));
                r.Add(("Engine 2 Fuel Flow", "TURB ENG FUEL FLOW PPH:2", Pph));
                r.Add(("Engine 1 Oil Pressure", "GENERAL ENG OIL PRESSURE:1", Psi));
                r.Add(("Engine 2 Oil Pressure", "GENERAL ENG OIL PRESSURE:2", Psi));
                r.Add(("Engine 1 Oil Temperature", "GENERAL ENG OIL TEMPERATURE:1", Cdeg));
                r.Add(("Engine 2 Oil Temperature", "GENERAL ENG OIL TEMPERATURE:2", Cdeg));
                r.Add(("Engine 1 Oil Quantity", "ENG OIL QUANTITY:1", Pct));
                r.Add(("Engine 2 Oil Quantity", "ENG OIL QUANTITY:2", Pct));
                r.Add(("Engine 1 Vibration", "ENG VIBRATION:1", v => $"{v:0.0}"));
                r.Add(("Engine 2 Vibration", "ENG VIBRATION:2", v => $"{v:0.0}"));
                r.Add(("Engine 1 Fuel Used", "GENERAL ENG FUEL USED SINCE START:1", Lbs));
                r.Add(("Engine 2 Fuel Used", "GENERAL ENG FUEL USED SINCE START:2", Lbs));
                break;
            case 1: // STAT
                r.Add(("Gross Weight", "TOTAL WEIGHT", Lbs));
                r.Add(("Center of Gravity", "CG PERCENT", v => $"{v:0.0} percent MAC"));
                r.Add(("Total Air Temperature", "TOTAL AIR TEMPERATURE", Cdeg));
                r.Add(("Outside Air Temperature", "AMBIENT TEMPERATURE", Cdeg));
                r.Add(("IRS Aligned", "IRS_aligned", v => v > 0.5 ? "aligned" : "not aligned"));
                r.Add(("APU", "APURunning", v => v > 0.5 ? "running" : "off"));
                break;
            case 2: // ELEC
                r.Add(("Main Bus Voltage", "ELECTRICAL MAIN BUS VOLTAGE", V));
                r.Add(("Battery Voltage", "ELECTRICAL BATTERY VOLTAGE", V));
                r.Add(("Generator 1 Voltage", "ELECTRICAL GENALT BUS VOLTAGE:1", V));
                r.Add(("Generator 2 Voltage", "ELECTRICAL GENALT BUS VOLTAGE:2", V));
                break;
            case 3: // HYD
                r.Add(("Left System Pressure", "HYDRAULIC PRESSURE:1", Psi));
                r.Add(("Center System Pressure", "HYDRAULIC PRESSURE:2", Psi));
                r.Add(("Right System Pressure", "HYDRAULIC PRESSURE:3", Psi));
                r.Add(("Left Reservoir", "HYDRAULIC RESERVOIR PERCENT:1", Pct));
                r.Add(("Center Reservoir", "HYDRAULIC RESERVOIR PERCENT:2", Pct));
                r.Add(("Right Reservoir", "HYDRAULIC RESERVOIR PERCENT:3", Pct));
                r.Add(("System Integrity", "HYDRAULIC SYSTEM INTEGRITY", Pct));
                break;
            case 4: // FUEL
                r.Add(("Total Fuel", "FUEL TOTAL QUANTITY WEIGHT", Lbs));
                r.Add(("Left Tank", "FUEL_QtyLeft", Lbs));
                r.Add(("Center Tank", "FUEL_QtyCenter", Lbs));
                r.Add(("Right Tank", "FUEL_QtyRight", Lbs));
                break;
            case 5: // AIR / PRESS
                r.Add(("Cabin Altitude", "PRESSURIZATION CABIN ALTITUDE", v => $"{v:0} feet"));
                r.Add(("Cabin Climb Rate", "PRESSURIZATION CABIN ALTITUDE RATE", v => $"{v:0} feet per minute"));
                r.Add(("Differential Pressure", "PRESSURIZATION PRESSURE DIFFERENTIAL", v => $"{v:0.0} psi"));
                r.Add(("Left Duct Pressure", "AIR_DuctPress_0", Psi));
                r.Add(("Right Duct Pressure", "AIR_DuctPress_1", Psi));
                break;
            case 6: // DOOR
                r.Add(("Entry 1 Left", "DOOR_state_0", DoorState));
                r.Add(("Entry 1 Right", "DOOR_state_1", DoorState));
                r.Add(("Entry 2 Left", "DOOR_state_2", DoorState));
                r.Add(("Entry 2 Right", "DOOR_state_3", DoorState));
                r.Add(("Entry 3 Left", "DOOR_state_4", DoorState));
                r.Add(("Entry 3 Right", "DOOR_state_5", DoorState));
                r.Add(("Entry 4 Left", "DOOR_state_6", DoorState));
                r.Add(("Entry 4 Right", "DOOR_state_7", DoorState));
                r.Add(("Entry 5 Left", "DOOR_state_8", DoorState));
                r.Add(("Entry 5 Right", "DOOR_state_9", DoorState));
                r.Add(("Cargo Forward", "DOOR_state_10", DoorState));
                r.Add(("Cargo Aft", "DOOR_state_11", DoorState));
                r.Add(("Cargo Bulk", "DOOR_state_13", DoorState));
                r.Add(("Avionics Access", "DOOR_state_14", DoorState));
                r.Add(("Electronics Access", "DOOR_state_15", DoorState));
                break;
            case 7: // GEAR
                r.Add(("Left Gear", "GEAR LEFT POSITION", GearPos));
                r.Add(("Center Gear", "GEAR CENTER POSITION", GearPos));
                r.Add(("Right Gear", "GEAR RIGHT POSITION", GearPos));
                r.Add(("Brake Accumulator", "BRAKES_BrakePressNeedle", v => $"{v * 40:0} psi"));
                break;
            case 8: // F-CTL
                r.Add(("Aileron", "AILERON LEFT DEFLECTION PCT", Pct));
                r.Add(("Elevator", "ELEVATOR DEFLECTION PCT", Pct));
                r.Add(("Rudder", "RUDDER DEFLECTION PCT", Pct));
                r.Add(("Elevator Trim", "ELEVATOR TRIM PCT", Pct));
                r.Add(("Rudder Trim", "RUDDER TRIM PCT", Pct));
                r.Add(("Aileron Trim", "AILERON TRIM PCT", Pct));
                r.Add(("Flaps Angle", "TRAILING EDGE FLAPS LEFT ANGLE", v => $"{v:0.0} degrees"));
                r.Add(("Spoilers", "SPOILERS LEFT POSITION", Pct));
                break;
        }
        return r;
    }

    /// <summary>
    /// Register the page-selector combo + every SD-row read var (OnRequest). Called
    /// from GetVariables after the PMDG vars are merged. Stock SimVars (space/colon
    /// names) register as SimVar with their engineering unit; the listed SDK fields
    /// register as PMDGVar; never force a stock-SimVar name through the L:var path.
    /// </summary>
    private void RegisterSystemDisplayVars(Dictionary<string, SimConnect.SimVarDefinition> vars)
    {
        if (!vars.ContainsKey(SdPageKey))
        {
            vars[SdPageKey] = new SimConnect.SimVarDefinition
            {
                Name = SdPageLVar,
                DisplayName = "System Display Page",
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = new Dictionary<double, string>(
                    _sdPageNames.ToDictionary(kv => (double)kv.Key, kv => kv.Value))
            };
        }

        for (int page = 0; page <= 8; page++)
        {
            foreach (var (_, rowVar, _) in PMDG777SdRows(page))
            {
                // PMDG SDK-broadcast fields are read live via PMDGDataManager.GetFieldValue
                // in RefreshSystemDisplayAsync — NOT through SimConnect registration (an
                // OnRequest PMDGVar never gets a value into the cache → reads "--").
                if (_sdPmdgVars.Contains(rowVar)) continue;
                if (vars.ContainsKey(rowVar)) continue;
                vars[rowVar] = new SimConnect.SimVarDefinition
                {
                    Name = rowVar,
                    DisplayName = rowVar,
                    Type = SimConnect.SimVarType.SimVar,
                    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                    Units = _sdUnits.TryGetValue(rowVar, out var u) ? u : "number"
                };
            }
        }
    }

    /// <summary>Compose the selected SD page's text into _sdContent (paint from cache now, force-read + repaint ~0.4 s later).</summary>
    private async void RefreshSystemDisplayAsync(SimConnect.SimConnectManager simConnect, int pageIndex)
    {
        try
        {
            var rows = PMDG777SdRows(pageIndex);
            if (rows.Count == 0) { _sdContent = ""; return; }
            int seq = ++_sdRefreshSeq;
            void Paint()
            {
                var dm = simConnect.PMDGDataManager;
                var sb = new System.Text.StringBuilder();
                foreach (var row in rows)
                {
                    double? cv;
                    try
                    {
                        // PMDG SDK-broadcast fields read LIVE from the data snapshot
                        // (an OnRequest PMDGVar never lands in the cache); stock SimVars
                        // read from the cache (populated by the forceUpdate reads below).
                        cv = _sdPmdgVars.Contains(row.var)
                            ? dm?.GetFieldValue(row.var)
                            : simConnect.GetCachedVariableValue(row.var);
                    }
                    catch { cv = null; }
                    sb.AppendLine(cv.HasValue ? $"{row.label}: {row.fmt(cv.Value)}" : $"{row.label}: --");
                }
                _sdContent = sb.ToString().TrimEnd();
                // Re-push the page var so MainForm re-renders the box from TryGetDisplayOverride.
                simConnect.RequestVariable(SdPageKey, forceUpdate: true);
            }
            Paint();   // immediate (stock from cache, PMDG from live snapshot)
            foreach (var row in rows)
                if (!_sdPmdgVars.Contains(row.var))
                    simConnect.RequestVariable(row.var, forceUpdate: true);
            await System.Threading.Tasks.Task.Delay(400);
            if (seq != _sdRefreshSeq) return;
            Paint();
        }
        catch { /* best-effort live refresh */ }
    }

    /// <summary>Called by MainForm when a display panel is shown (and by the 3 s auto-refresh).</summary>
    public override void OnDisplayPanelShown(string panelKey, SimConnect.SimConnectManager simConnect)
    {
        if (panelKey != "System Display" || simConnect == null || !simConnect.IsConnected) return;
        RefreshSystemDisplayAsync(simConnect, _sdPage);
    }

    /// <summary>Page selector changed: store the page, mirror it to the L:var (so it reads back), refresh.</summary>
    private bool HandleSystemDisplaySelect(double value, SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _sdPage = (int)Math.Round(value);
        // Mirror the selection to the backing L:var so its value lands in the cache and
        // the status box (TryGetDisplayOverride on SdPageKey) renders the content.
        simConnect.ExecuteCalculatorCode($"{_sdPage} (>L:{SdPageLVar})");
        simConnect.RequestVariable(SdPageKey, forceUpdate: true);
        RefreshSystemDisplayAsync(simConnect, _sdPage);
        // No speech here — the combo's own value change announces the page NAME.
        return true;
    }

    /// <summary>Render the System Display status box: selected page name + decoded content.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (varKey == SdPageKey)
        {
            int pi = (int)Math.Round(value);
            string pname = _sdPageNames.TryGetValue(pi, out var pn) ? pn : $"Page {pi}";
            displayText = string.IsNullOrEmpty(_sdContent)
                ? $"{pname} page (select a page to load its content)"
                : $"{pname} page\r\n{_sdContent}";
            return true;
        }
        displayText = "";
        return false;
    }
}
