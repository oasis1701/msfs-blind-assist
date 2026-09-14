using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// Cabin → Seating and Payload. Both variants.
///
/// The five loading stations the aeroplane declares in flight_model.cfg, plus the two crew
/// figures. The station names and the baggage limit are the aeroplane's own — station 5 is
/// declared "REAR BAGGAGE (Max 66lbs/30kg)" — so the limit reported here is not a number
/// anyone chose.
///
/// WEIGHT IS WRITTEN TO THE SIMVAR, not through an event. Verified live: the indexed
/// `180 2 (>K:2:PAYLOAD_STATION_WEIGHT_SET)` left the station at 170, while
/// `180 (>A:PAYLOAD STATION WEIGHT:2, pounds)` moved it.
///
/// THE CREW FIGURES ARE TRI-STATE, not on/off. L:FORCE_PILOT and L:FORCE_COPILOT run
/// -1 / 0 / +1, and the model's own tooltip spells them "Pilot OFF" / "Pilot Normal" /
/// "Pilot ON" — Normal being the aeroplane deciding for itself. A two-position control
/// would have thrown that middle position away.
///
/// The scan is the loading sheet a sighted pilot reads off the chart: gross weight against
/// the maximum, what is left, the centre of gravity, and whether the baggage is legal.
/// A blind pilot cannot read the AFM's loading envelope, and this is the part of it the
/// simulation actually computes.
/// </summary>
public partial class CowsDA40Definition
{
    private const string PayloadPanel = "Seating and Payload";

    /// <summary>The aeroplane's own declared baggage limit, from flight_model.cfg.</summary>
    private const double BaggageMaxLb = 66.0;

    private const double KgPerLb = 0.45359237;

    // Control key -> PAYLOAD STATION WEIGHT index. SimConnect is 1-based where the cfg's
    // station_load lines are 0-based, so cfg station_load.0 is index 1.
    private static readonly Dictionary<string, int> PayloadStationIndex = new()
    {
        ["DA40_PAYLOAD_PILOT_SET"] = 1,
        ["DA40_PAYLOAD_FRONT_PAX_SET"] = 2,
        ["DA40_PAYLOAD_REAR_LEFT_SET"] = 3,
        ["DA40_PAYLOAD_REAR_RIGHT_SET"] = 4,
        ["DA40_PAYLOAD_BAGGAGE_SET"] = 5
    };

    private static Dictionary<string, SimVarDefinition> BuildPayloadVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        AddStation(v, "DA40_PAYLOAD_PILOT_SET", 1, "Pilot");
        AddStation(v, "DA40_PAYLOAD_FRONT_PAX_SET", 2, "Front Passenger");
        AddStation(v, "DA40_PAYLOAD_REAR_LEFT_SET", 3, "Rear Passenger Left");
        AddStation(v, "DA40_PAYLOAD_REAR_RIGHT_SET", 4, "Rear Passenger Right");
        AddStation(v, "DA40_PAYLOAD_BAGGAGE_SET", 5, "Rear Baggage");

        AddCrewFigure(v, "DA40_PAYLOAD_PILOT_FIGURE", "FORCE_PILOT", "Pilot Figure");
        AddCrewFigure(v, "DA40_PAYLOAD_COPILOT_FIGURE", "FORCE_COPILOT", "Copilot Figure");

        // ---------- Status ----------

        // Gross weight is the SHARED key the W readout already uses - one variable, one
        // home - so it is listed here rather than defined again.

        v["DA40_PAYLOAD_MARGIN"] = new SimVarDefinition
        {
            Name = "TOTAL WEIGHT",
            DisplayName = "Weight Margin",
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        v["DA40_PAYLOAD_CG"] = new SimVarDefinition
        {
            Name = "CG PERCENT",
            DisplayName = "Centre of Gravity",
            Type = SimVarType.SimVar,
            Units = "percent",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true,
            Format = "F1"
        };

        v["DA40_PAYLOAD_BAGGAGE_CHECK"] = new SimVarDefinition
        {
            Name = "PAYLOAD STATION WEIGHT:5",
            DisplayName = "Baggage Limit",
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.OnRequest,
            IsAnnounced = false,
            RenderAsReadOnlyStatus = true
        };

        return v;
    }

    private static void AddStation(Dictionary<string, SimVarDefinition> v, string key,
        int index, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = $"PAYLOAD STATION WEIGHT:{index}",
            DisplayName = display,
            Type = SimVarType.SimVar,
            Units = "pounds",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = false,
            Format = "F0"
        };
    }

    private static void AddCrewFigure(Dictionary<string, SimVarDefinition> v, string key,
        string lvar, string display)
    {
        v[key] = new SimVarDefinition
        {
            Name = lvar,
            DisplayName = display,
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            // Tri-state, and the wording is the model's own tooltip.
            ValueDescriptions = new Dictionary<double, string>
            {
                [-1] = "Off",
                [0] = "Normal",
                [1] = "On"
            }
        };
    }

    private static readonly List<string> PayloadControls = new()
    {
        "DA40_PAYLOAD_PILOT_SET",
        "DA40_PAYLOAD_FRONT_PAX_SET",
        "DA40_PAYLOAD_REAR_LEFT_SET",
        "DA40_PAYLOAD_REAR_RIGHT_SET",
        "DA40_PAYLOAD_BAGGAGE_SET",
        "DA40_PAYLOAD_PILOT_FIGURE",
        "DA40_PAYLOAD_COPILOT_FIGURE"
    };

    private static readonly List<string> PayloadDisplay = new()
    {
        "DA40_GROSS_WEIGHT",
        "DA40_PAYLOAD_MARGIN",
        "DA40_PAYLOAD_CG",
        "DA40_PAYLOAD_BAGGAGE_CHECK"
    };

    private bool HandlePayloadSet(string varKey, double value, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer)
    {
        if (PayloadStationIndex.TryGetValue(varKey, out int station))
        {
            double lb = Math.Max(0, value);

            // The SimVar, not an event: the indexed PAYLOAD_STATION_WEIGHT_SET event was
            // tried live and left the station untouched.
            simConnect.ExecuteCalculatorCode(
                $"{lb.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)} " +
                $"(>A:PAYLOAD STATION WEIGHT:{station}, pounds)");

            // A typed numeric entry confirms, in both units - the AFM quotes both and the
            // aeroplane is loaded in whichever the operator uses.
            announcer.AnnounceImmediate($"{lb:0} pounds, {lb * KgPerLb:0} kilograms");
            return true;
        }

        switch (varKey)
        {
            case "DA40_PAYLOAD_PILOT_FIGURE":
                simConnect.SetLVar("FORCE_PILOT", value);
                return true;

            case "DA40_PAYLOAD_COPILOT_FIGURE":
                simConnect.SetLVar("FORCE_COPILOT", value);
                return true;
        }

        return false;
    }

    /// <summary>
    /// The loading sheet. A sighted pilot reads the envelope off a chart in the AFM; this
    /// is the part of it the simulation actually computes, in words.
    /// </summary>
    private bool TryGetPayloadDisplayOverride(string varKey, double value, out string displayText)
    {
        displayText = "";

        switch (varKey)
        {
            case "DA40_PAYLOAD_MARGIN":
            {
                double maxLb = IsNG ? 2888 : 2646;
                double left = maxLb - value;

                displayText = left < 0
                    ? $"OVERWEIGHT by {-left:0} pounds, {-left * KgPerLb:0} kilograms"
                    : $"{left:0} pounds left, {left * KgPerLb:0} kilograms";
                return true;
            }

            case "DA40_PAYLOAD_BAGGAGE_CHECK":
                // ⚠️ BOTH UNITS, LIKE EVERY OTHER WEIGHT ON THIS PANEL. This row alone read
                // "Baggage Limit: 0 of 66 pounds" while Gross Weight and Weight Margin beside
                // it gave pounds AND kilograms - so the one number a pilot compares against a
                // bag on a scale was in the unit they had not chosen.
                displayText = value > BaggageMaxLb
                    ? $"{value:0} pounds, {value * KgPerLb:0} kilograms — OVER the " +
                      $"{BaggageMaxLb:0} pound, {BaggageMaxLb * KgPerLb:0} kilogram limit"
                    : $"{value:0} of {BaggageMaxLb:0} pounds, " +
                      $"{value * KgPerLb:0} of {BaggageMaxLb * KgPerLb:0} kilograms";
                return true;

            case "DA40_GROSS_WEIGHT":
                displayText = $"{value:0} pounds, {value * KgPerLb:0} kilograms";
                return true;
        }

        return false;
    }
}
