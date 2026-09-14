using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Center Console → Fuel System (DA40-XLS).
///
/// A conventional light-single fuel system: two wing tanks, a LEFT / RIGHT / OFF selector on
/// the centre console, one electric pump, and the engine-driven pump behind it. Nothing the
/// NG's Main/Auxiliary transfer-pump system has, and the NG's panel is not reused; the
/// refuelling TRANSACTION is, because the filler caps are on the wings on both aeroplanes.
///
/// Every number here is measured (docs/da40-xls-variables.md, Fuel):
///
///  • <c>FUEL_SELECTOR</c> is 0 LEFT · 1 RIGHT · 2 OFF (the component's ANIMTIPs), and a
///    write to it is what the Logic reads to drive the stock selector: 0 → tank 2, 1 → 3,
///    2 → 1. The same L:var the NG's valve uses, with different positions.
///  • <c>ENG_FUEL_PRESS</c> is in BAR; the G1000 draws <c>DISP_FP_PROBE</c> in psi — 1.616
///    against 23.43, 14.5 psi per bar. The arcs (AFM 2.5) are 14–35 psi, green only.
///  • <c>FUEL_QUANT_PROBE:1/2</c> is the gauge: the true quantity with a slosh term, and
///    CLAMPED to 15.25 between 15.25 and 18.5 gal — the model's version of the AFM's "max
///    indicated fuel quantity 15 US gal per tank". Reported as the indication, flagged on
///    the flat spot, with the measured quantity beside it. Same design as the NG's 14-gal cap.
///  • Tanks are 2 × 20.6 gal with 0.5 unusable each (AFM 2.14.2): 20.1 usable a side. The
///    sim's own tank is 25 gal, the long-range size, and refuelling clamps to the AFM figure
///    rather than the sim's.
///  • Maximum permissible difference between tanks is 10 gal on the XLS (9 on the NG).
///  • Vapour lock is <c>FUEL_TEMP_BOIL_FAC</c>: it multiplies the fuel pressure, so a value
///    below 1 is pressure being lost to vapour. The electric pump is the model's cure.
///  • The pump SWITCH and the pump RUNNING are different facts, as on the NG: a pulled
///    <c>CB_FUP</c> or a dead bus leaves the switch on and the pump still. Both are rows.
/// </summary>
public partial class CowsDA40Definition
{
    /// <summary>AFM 2.14.2: 20.6 gal a side, 0.5 unusable.</summary>
    private const double XlsTankUsableGal = 20.1;
    /// <summary>AFM 2.14.2: maximum permissible difference between the tanks.</summary>
    private const double XlsMaxTankDifferenceGal = 10.0;
    /// <summary>The Logic clamps the probe to this across the gauge's flat spot.</summary>
    private const double XlsGaugeFlatSpotGal = 15.25;
    private const double XlsGaugeFlatSpotTopGal = 18.5;
    /// <summary>AFM: the LOW FUEL annunciation threshold.</summary>
    private const double XlsLowFuelGal = 3.0;
    /// <summary>ENG_FUEL_PRESS bar → the gauge's psi, measured 23.43 / 1.616.</summary>
    private const double BarToPsi = 14.504;

    private static Dictionary<string, SimVarDefinition> BuildXlsFuelVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---------- Controls ----------

        v["DA40_XLS_FUEL_SELECTOR"] = new SimVarDefinition
        {
            Name = "FUEL_SELECTOR",
            DisplayName = "Fuel Selector",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            {
                [0] = "Left",
                [1] = "Right",
                [2] = "Off"
            }
        };

        v["DA40_XLS_FUEL_PUMP"] = new SimVarDefinition
        {
            Name = "GENERAL ENG FUEL PUMP SWITCH:1",
            DisplayName = "Electric Fuel Pump",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        };

        // Refuelling: the transaction, not a cockpit control (see Fuel.cs). Left and right,
        // because that is what the XLS's tanks are.
        foreach (var (key, label) in new[] { ("DA40_XLS_FUEL_LEFT_LOAD_SET", "Left Tank Fuel"), ("DA40_XLS_FUEL_RIGHT_LOAD_SET", "Right Tank Fuel") })
        {
            v[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = label,
                Type = SimVarType.LVar,
                Units = "gallons",
                UpdateFrequency = UpdateFrequency.Never,
                IsAnnounced = false,
                Format = "0.0"
            };
        }

        v["DA40_XLS_FUEL_FILL"] = new SimVarDefinition
        {
            Name = "DA40_XLS_FUEL_FILL",
            DisplayName = "Fill Both Tanks",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Never,
            RenderAsButton = true,
            SuppressRestingButtonState = true,
            IsAnnounced = false
        };

        // ---------- Status ----------

        // The measured quantities. OnRequest TWINS of the batched DA40_FUEL_MAIN/AUX_ACTUAL
        // keys, which carry the NG's names; only one key per SimVar may be batched.
        v["DA40_XLS_FUEL_LEFT"] = Twin("FUEL TANK LEFT MAIN QUANTITY", "Left Tank Measured", SimVarType.SimVar, "gallons");
        v["DA40_XLS_FUEL_RIGHT"] = Twin("FUEL TANK RIGHT MAIN QUANTITY", "Right Tank Measured", SimVarType.SimVar, "gallons");
        v["DA40_XLS_FUEL_LEFT_IND"] = Twin("FUEL_QUANT_PROBE:1", "Left Tank Indicated", SimVarType.LVar, "gallons");
        v["DA40_XLS_FUEL_RIGHT_IND"] = Twin("FUEL_QUANT_PROBE:2", "Right Tank Indicated", SimVarType.LVar, "gallons");
        v["DA40_XLS_FUEL_DIFFERENCE"] = Twin("FUEL TANK LEFT MAIN QUANTITY", "Tank Difference", SimVarType.SimVar, "gallons");
        v["DA40_XLS_FUEL_FEED"] = Twin("FUEL_FEED_QUANTITY", "Feeding", SimVarType.LVar, "gallons");

        v["DA40_XLS_FUEL_PRESSURE"] = new SimVarDefinition
        {
            // THE INDICATION. DISP_FP is the gauge's own psi - measured 27.48 against
            // ENG_FUEL_PRESS 1.895 bar times 14.5 - and it zeroes with FAILURES_DISP_FP,
            // so reading the model's bar showed a perfect pressure off a dead gauge. The
            // arcs move with it, taken from the XLS's own panel.xml rather than converted:
            // scale 0 to 40, red below 14, green 14 to 35, red above. No yellow.
            Name = "DISP_FP",
            DisplayName = "Fuel Pressure",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            // An L:var carries no unit; "psi" here would be handed to SimConnect on the
            // continuous path and converted from a base it does not have. The override
            // names the unit instead.
            Units = "number",
            Format = "F0"
        };

        v["DA40_XLS_FUEL_PUMP_RUNNING"] = new SimVarDefinition
        {
            Name = "GENERAL ENG FUEL PUMP ON:1",
            DisplayName = "Electric Pump",
            Type = SimVarType.SimVar,
            Units = "bool",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not running", [1] = "Running" }
        };

        v["DA40_XLS_FUEL_VAPOUR"] = new SimVarDefinition
        {
            Name = "FUEL_TEMP_BOIL_FAC",
            DisplayName = "Vapour Lock",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F2"
        };

        return v;
    }

    private static SimVarDefinition Twin(string name, string displayName, SimVarType type, string units) => new()
    {
        Name = name,
        DisplayName = displayName,
        Type = type,
        Units = units,
        UpdateFrequency = UpdateFrequency.OnRequest,
        IsAnnounced = false,
        RenderAsReadOnlyStatus = true,
        Format = "F1"
    };

    private static readonly List<string> XlsFuelControls = new()
    {
        "DA40_XLS_FUEL_SELECTOR",
        "DA40_XLS_FUEL_PUMP",
        "DA40_XLS_FUEL_LEFT_LOAD_SET",
        "DA40_XLS_FUEL_RIGHT_LOAD_SET",
        "DA40_XLS_FUEL_FILL"
    };

    // What is in the tanks, what the gauge says, the difference, what the engine is drawing
    // on, and whether it is getting it.
    private static readonly List<string> XlsFuelDisplay = new()
    {
        "DA40_XLS_FUEL_LEFT",
        "DA40_XLS_FUEL_RIGHT",
        "DA40_XLS_FUEL_LEFT_IND",
        "DA40_XLS_FUEL_RIGHT_IND",
        "DA40_XLS_FUEL_DIFFERENCE",
        "DA40_XLS_FUEL_FEED",
        "DA40_XLS_FUEL_PRESSURE",
        "DA40_XLS_FUEL_FLOW",
        "DA40_XLS_FUEL_PUMP_RUNNING",
        "DA40_XLS_FUEL_VAPOUR",
        "DA40_PRIME_FUEL_TEMP"
    };

    private double _xlsFuelLeftGal;
    private double _xlsFuelRightGal;

    private bool HandleXlsFuelSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        switch (varKey)
        {
            case "DA40_XLS_FUEL_SELECTOR":
                int pos = Math.Clamp((int)Math.Round(value), 0, 2);
                // The same position twice running is a byte-identical string.
                simConnect.ExecuteCalculatorCodeUnique($"{pos} (>L:FUEL_SELECTOR)");
                return true;

            case "DA40_XLS_FUEL_PUMP":
                // The stock set event, measured to land: the pump reported ON and stock fuel
                // pressure rose to 6.9 psi with the engine stopped.
                simConnect.ExecuteCalculatorCodeUnique($"{(value >= 0.5 ? 1 : 0)} (>K:ELECT_FUEL_PUMP1_SET)");
                return true;

            case "DA40_XLS_FUEL_LEFT_LOAD_SET":
                return Refuel(simConnect, announcer, value, null, XlsTankUsableGal, PrimeEngineRunning);

            case "DA40_XLS_FUEL_RIGHT_LOAD_SET":
                return Refuel(simConnect, announcer, null, value, XlsTankUsableGal, PrimeEngineRunning);

            case "DA40_XLS_FUEL_FILL":
                return Refuel(simConnect, announcer, XlsTankUsableGal, XlsTankUsableGal, XlsTankUsableGal, PrimeEngineRunning);
        }

        return false;
    }

    private bool TryGetXlsFuelDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = string.Empty;

        switch (varKey)
        {
            case "DA40_XLS_FUEL_LEFT":
                _xlsFuelLeftGal = value;
                displayText = DualUnitFuel(value);
                return true;

            case "DA40_XLS_FUEL_RIGHT":
                _xlsFuelRightGal = value;
                displayText = DualUnitFuel(value);
                return true;

            case "DA40_XLS_FUEL_LEFT_IND":
            case "DA40_XLS_FUEL_RIGHT_IND":
            {
                if (!TryUnitText("gallons", value, "0.0", out string quantity)) quantity = $"{value:0.0} gallons";
                displayText = value < XlsLowFuelGal ? quantity + ", low fuel" : quantity;

                // On the flat spot the gauge has stopped measuring: the probe is pinned at
                // 15.25 while the tank holds anything up to 18.5. Say so, and say what the
                // tank actually holds - the measured row is the AFM's dipstick.
                double actual = varKey == "DA40_XLS_FUEL_LEFT_IND" ? _xlsFuelLeftGal : _xlsFuelRightGal;
                if (Math.Abs(value - XlsGaugeFlatSpotGal) < 0.05 && actual > XlsGaugeFlatSpotGal + 0.2)
                {
                    displayText += $", on the gauge's flat spot - between {XlsGaugeFlatSpotGal:0} and {XlsGaugeFlatSpotTopGal:0.#}, measured {actual:0.0}";
                }
                return true;
            }

            case "DA40_XLS_FUEL_DIFFERENCE":
            {
                double diff = Math.Abs(_xlsFuelLeftGal - _xlsFuelRightGal);
                displayText = diff > XlsMaxTankDifferenceGal
                    ? $"{diff:0.0} gallons, OVER the {XlsMaxTankDifferenceGal:0} gallon limit"
                    : $"{diff:0.0} gallons, limit {XlsMaxTankDifferenceGal:0}";
                return true;
            }

            case "DA40_XLS_FUEL_FEED":
                displayText = value <= 0.005 ? "Nothing - selector off or tank empty"
                    : (TryUnitText("gallons", value, "0.0", out string feed) ? feed : $"{value:0.0} gallons");
                return true;

            case "DA40_XLS_FUEL_PRESSURE":
                // Unpowered, the model's fuel logic does not tick and this holds a frozen
                // number (measured 19.75 bar after a reload - 286 psi if rendered). Say why.
                // DISP_FP is drawn from the same frozen chain, so the guard still applies.
                displayText = DA40StartReadiness.FrozenReason(_startMasterOn)
                    // Already the gauge's psi now that the source is the indication.
                    ?? DA40InstrumentBands.Annotate(varKey, value, $"{value:F0} psi");
                return true;

            case "DA40_XLS_FUEL_VAPOUR":
                displayText = value >= 0.995 ? "None"
                    : $"Vapour lock, fuel pressure at {value * 100:0} percent - electric pump on";
                return true;
        }

        return false;
    }
}
