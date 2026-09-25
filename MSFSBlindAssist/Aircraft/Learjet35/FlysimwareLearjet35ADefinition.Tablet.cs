using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Learjet35;

/// <summary>
/// EFB Tablet → Tablet; Cabin and Ground → Cabin Door, Cabin, Ground Equipment, Payload;
/// Simulation → Aircraft Options. Every tablet checkbox is a plain L:var the tablet re-syncs
/// from each frame, so MSFSBA writes the L:var (measured 2026-09-07).
/// </summary>
public partial class FlysimwareLearjet35ADefinition
{
    private const string TabletPanel = "Tablet";
    private const string CabinDoorPanel = "Cabin Door";
    private const string CabinPanel = "Cabin";
    private const string GroundPanel = "Ground Equipment";
    private const string PayloadPanel = "Payload";
    private const string OptionsPanel = "Aircraft Options";

    private static readonly (string Key, string LVar, string Display, string Off, string On, string? Help)[] TabletSwitches =
    {
        ("LJ35_TABLET_POWER", "EFB_PUSH_POWER", "Tablet Power", "Off", "On", null),
        ("LJ35_TABLET_HIDE", "SHOW_HIDE_EFB", "Tablet Visibility", "Hidden", "Shown", null),
        ("LJ35_PAYLOAD_SYNC", "PAYLOAD_SYNC", "Sync Payload with the Sim Menu", "Off", "On", null),
        ("LJ35_SAVE_FUEL", "SAVE_FUEL_STATE", "Save Fuel State", "Off", "On", null),
        ("LJ35_SHOW_PAX", "SHOW_PASSENGERS", "Show Passengers", "Hidden", "Shown", null),
    };

    private static readonly (string Key, string LVar, string Display, string Off, string On, string? Help)[] OptionSwitches =
    {
        ("LJ35_OPT_METRIC", "METRIC_TRUE", "Tablet Units", "Imperial", "Metric", null),
        ("LJ35_OPT_FUEL_WEIGHT", "FUEL_WEIGHT_TRUE", "Fuel Shown As", "Gallons", "Pounds", null),
        ("LJ35_OPT_TRIM_CLACKER", "TRIM_CLACKER_DISABLED", "Trim Clacker", "Enabled", "Disabled", null),
        ("LJ35_OPT_N2_CLACKER", "N2_CLACKER_DISABLED", "N2 Clacker", "Enabled", "Disabled", null),
        ("LJ35_OPT_INVERTER_SOUND", "EFB_INVERTER_DISABLED", "Inverter Sound", "Enabled", "Disabled", null),
        ("LJ35_OPT_AVIONICS_SOUND", "EFB_AVIONICS_DISABLE", "Avionics Fan Sound", "Enabled", "Disabled", null),
        ("LJ35_OPT_SOUND", "EFB_DISABLE_SOUND", "Tablet Sounds", "Enabled", "Disabled", null),
    };

    private static readonly string[] PassengerLights =
        { "PASS1", "PASS2", "PASS3", "PASS4", "PASS5", "PASS6", "PASS7", "PASS8", "PASS9", "PASS10", "PASS11", "PASS12" };

    /// <summary>Tablet payload: every seat, the exclusive pairs as one combo each.</summary>
    private static readonly (string Key, string Display, string[] LVars, string[] Labels)[] PayloadSeats =
    {
        ("LJ35_PAX_PILOT", "Pilot", new[] { "PILOT" }, new[] { "Empty", "Pilot" }),
        ("LJ35_PAX_COPILOT", "Copilot", new[] { "COPILOT" }, new[] { "Empty", "Copilot" }),
        ("LJ35_PAX_SEAT1", "Seat 1", new[] { "JOHN", "MARY" }, new[] { "Empty", "John", "Mary" }),
        ("LJ35_PAX_SEAT2", "Seat 2", new[] { "RICKB", "RICKN" }, new[] { "Empty", "Rick B", "Rick N" }),
        ("LJ35_PAX_SEAT3", "Seat 3", new[] { "MARK", "DORTHY" }, new[] { "Empty", "Mark", "Dorthy" }),
        ("LJ35_PAX_SEAT4", "Seat 4", new[] { "ALICIA", "JAKE" }, new[] { "Empty", "Alicia", "Jake" }),
        ("LJ35_PAX_SEAT5", "Seat 5", new[] { "CINDYB", "CINDYT" }, new[] { "Empty", "Cindy B", "Cindy T" }),
        ("LJ35_PAX_JULIE", "Julie", new[] { "JULIE" }, new[] { "Empty", "Julie" }),
        ("LJ35_PAX_WILL", "Will", new[] { "WILL" }, new[] { "Empty", "Will" }),
        ("LJ35_PAX_BOBBY", "Bobby", new[] { "BOBBY" }, new[] { "Empty", "Bobby" }),
        ("LJ35_PAX_MEDEVAC", "Medevac", new[] { "MEDEVAC_PATIENT" }, new[] { "No patient", "Patient" }),
        ("LJ35_PAX_MEDEVAC_CREW", "Medevac Crew", new[] { "MEDEVAC_JULIE", "MEDEVAC_WILL" }, new[] { "None", "Julie", "Will" }),
    };

    private static Dictionary<string, SimVarDefinition> BuildTabletVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        foreach (var s in TabletSwitches.Concat(OptionSwitches))
            AddLVarState(v, s.Key, s.LVar, s.Display, new Dictionary<double, string> { [0] = s.Off, [1] = s.On }, s.Help);
        v["LJ35_GPS_UNIT"] = new SimVarDefinition
        {
            Name = "SHOW_GTN750", DisplayName = "GPS Unit", Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "GNS 530 and 430", [1] = "PMS50 GTN 750 (not installed)" },
            HelpText = "Only the GNS 530 fit is supported by MSFSBA; the GTN slots are empty on this machine."
        };

        // Cabin door: the handles drive INTERACTIVE POINT GOAL:2 in steps (Cabin.xml).
        AddSimState(v, "LJ35_CABIN_DOOR", "INTERACTIVE POINT GOAL:2", "Cabin Door",
            new Dictionary<double, string> { [0] = "Closed", [17.5] = "Upper unlatched", [50] = "Upper open", [66.5] = "Lower unlatched", [100] = "Fully open" },
            units: "percent", help: "The DOOR lamp lights and the GPU drops when it opens.");
        AddSwitch(v, "LJ35_DOOR_MOTOR", "LEAR_DOORHANDLE_MOTOR", "Door Seal Motor", "Off", "Sealing");
        AddLVarState(v, "LJ35_COCKPIT_DIVIDER", "COCKPIT_DOOR_DIVIDER", "Cockpit Divider",
            new Dictionary<double, string> { [0] = "Open", [1] = "Closed" });
        AddSimReadout(v, "LJ35_DOOR_OPEN_PCT", "INTERACTIVE POINT OPEN:2", "Cabin Door Open", "percent", "F0");
        AddFlag(v, "LJ35_EXIT_OPEN", "EXIT OPEN:0", "Exit", "Closed", "Open", simvar: true);
        Cache(v, "LJ35_DOOR_OPEN_PCT");

        // Cabin
        for (int i = 1; i <= 3; i++)
            AddSwitch(v, "LJ35_TABLE_" + i, "LEAR_TABLE_FOLD_" + i, "Cabin Table " + i, "Folded", "Extended");
        AddLVarState(v, "LJ35_SHADES", "GENERIC_LEAR_CURTAIN1", "Window Shades",
            new Dictionary<double, string> { [0] = "Open", [1] = "Closed" }, "All eleven shades together.");
        AddSwitch(v, "LJ35_LT_CABIN", "LEAR_SW_LTS_CABIN", "Cabin Lights");
        AddSwitch(v, "LJ35_LT_CABIN_DIM", "LEAR_SW_LTS_DIM", "Cabin Lights Dim", "Bright", "Dim");
        AddSwitch(v, "LJ35_LT_BAGGAGE", "LEAR_SW_LTS_BAGGAGE", "Baggage Light");
        AddSwitch(v, "LJ35_LT_STEP", "LEAR_SW_LTS_STEP", "Door Step Light");
        for (int i = 0; i < PassengerLights.Length; i++)
            AddSwitch(v, "LJ35_LT_" + PassengerLights[i], "LEAR_SW_LTS_" + PassengerLights[i], "Reading Light " + (i + 1));

        // Ground equipment
        AddLVarState(v, "LJ35_CHOCKS", "EFB_PUSH_CHOCKS", "Wheel Chocks",
            new Dictionary<double, string> { [0] = "Removed", [1] = "In place" },
            "Cleared automatically when the parking brake is released, above 1 knot, or with an engine running.");
        AddLVarState(v, "LJ35_ENGINE_COVERS", "EFB_PUSH_ECOVER", "Engine Covers", new Dictionary<double, string> { [0] = "Removed", [1] = "Fitted" });
        AddLVarState(v, "LJ35_PITOT_COVERS", "EFB_PUSH_PCOVER", "Pitot Covers", new Dictionary<double, string> { [0] = "Removed", [1] = "Fitted" });
        AddLVarState(v, "LJ35_FUEL_COVERS", "EFB_PUSH_FCOVER", "Fuel Caps and Covers", new Dictionary<double, string> { [0] = "Removed", [1] = "Fitted" });

        // Payload
        foreach (var seat in PayloadSeats)
        {
            var desc = new Dictionary<double, string>();
            for (int i = 0; i < seat.Labels.Length; i++) desc[i] = seat.Labels[i];
            AddLVarState(v, seat.Key, seat.LVars[0], seat.Display, desc);
            v[seat.Key].UpdateFrequency = UpdateFrequency.OnRequest;
            v[seat.Key].IsAnnounced = false;
        }
        for (int i = 1; i <= 5; i++)
            AddLVarState(v, "LJ35_LUGGAGE_" + i, "LUGGAGE_" + i, "Luggage " + i, new Dictionary<double, string> { [0] = "None", [1] = "Loaded" });
        for (int i = 1; i <= 3; i++)
            AddLVarState(v, "LJ35_CARGO_" + i, "CARGO_" + i, "Cargo " + i, new Dictionary<double, string> { [0] = "None", [1] = "Loaded" });
        AddSimReadout(v, "LJ35_TOTAL_WEIGHT", "TOTAL WEIGHT", "Total Weight", "pounds", "F0");
        AddSimReadout(v, "LJ35_CG_PCT", "CG PERCENT", "Centre of Gravity", "percent", "F1");
        AddDerived(v, "LJ35_WEIGHT_MARGIN", "Weight Limits");
        Cache(v, "LJ35_TOTAL_WEIGHT");
        return v;
    }

    private static List<string> TabletControls => TabletSwitches.Select(s => s.Key).Append("LJ35_GPS_UNIT").ToList();
    private static List<string> OptionsControls => OptionSwitches.Select(s => s.Key).ToList();
    private static readonly List<string> CabinDoorControls = new() { "LJ35_CABIN_DOOR", "LJ35_DOOR_MOTOR", "LJ35_COCKPIT_DIVIDER" };
    private static readonly List<string> CabinDoorDisplay = new() { "LJ35_DOOR_OPEN_PCT", "LJ35_EXIT_OPEN" };
    private static List<string> CabinControls
    {
        get
        {
            var c = new List<string> { "LJ35_LT_CABIN", "LJ35_LT_CABIN_DIM", "LJ35_LT_BAGGAGE", "LJ35_LT_STEP", "LJ35_SHADES", "LJ35_TABLE_1", "LJ35_TABLE_2", "LJ35_TABLE_3" };
            c.AddRange(PassengerLights.Select(p => "LJ35_LT_" + p));
            return c;
        }
    }
    private static readonly List<string> GroundControls = new() { "LJ35_CHOCKS", "LJ35_ENGINE_COVERS", "LJ35_PITOT_COVERS", "LJ35_FUEL_COVERS" };
    private static List<string> PayloadControls
    {
        get
        {
            var c = PayloadSeats.Select(s => s.Key).ToList();
            for (int i = 1; i <= 5; i++) c.Add("LJ35_LUGGAGE_" + i);
            for (int i = 1; i <= 3; i++) c.Add("LJ35_CARGO_" + i);
            return c;
        }
    }
    private static readonly List<string> PayloadDisplay = new() { "LJ35_TOTAL_WEIGHT", "LJ35_CG_PCT", "LJ35_FUEL_TOTAL", "LJ35_WEIGHT_MARGIN" };

    private static bool HandleTabletSet(string varKey, double value, SimConnectManager sc)
    {
        foreach (var s in TabletSwitches.Concat(OptionSwitches))
            if (s.Key == varKey) { sc.SetLVar(s.LVar, value); return true; }

        switch (varKey)
        {
            case "LJ35_GPS_UNIT":
                sc.SetLVar("SHOW_GNS530", value < 0.5 ? 1 : 0);
                sc.SetLVar("SHOW_GTN750", value < 0.5 ? 0 : 1);
                sc.SetLVar("SHOW_GTN750XI", 0);
                return true;
            case "LJ35_CABIN_DOOR": sc.ExecuteCalculatorCode($"{Rpn(value)} (>A:INTERACTIVE POINT GOAL:2, Percent)"); return true;
            case "LJ35_DOOR_MOTOR": sc.SetLVar("GENERIC_LEAR_DOORHANDLE_MOTOR", value); return true;
            case "LJ35_COCKPIT_DIVIDER": sc.SetLVar("COCKPIT_DOOR_DIVIDER", value); return true;
            case "LJ35_SHADES":
                for (int i = 1; i <= 11; i++) sc.SetLVar("GENERIC_LEAR_CURTAIN" + i, value);
                return true;
            case "LJ35_LT_CABIN": sc.SetLVar("GENERIC_LEAR_SW_LTS_CABIN", value); return true;
            case "LJ35_LT_CABIN_DIM": sc.SetLVar("GENERIC_LEAR_SW_LTS_DIM", value); return true;
            case "LJ35_LT_BAGGAGE": sc.SetLVar("GENERIC_LEAR_SW_LTS_BAGGAGE", value); return true;
            case "LJ35_LT_STEP": sc.SetLVar("GENERIC_LEAR_SW_LTS_STEP", value); return true;
            case "LJ35_CHOCKS": sc.SetLVar("EFB_PUSH_CHOCKS", value); return true;
            case "LJ35_ENGINE_COVERS": sc.SetLVar("EFB_PUSH_ECOVER", value); return true;
            case "LJ35_PITOT_COVERS": sc.SetLVar("EFB_PUSH_PCOVER", value); return true;
            case "LJ35_FUEL_COVERS": sc.SetLVar("EFB_PUSH_FCOVER", value); return true;
        }
        for (int i = 1; i <= 3; i++)
            if (varKey == "LJ35_TABLE_" + i) { sc.SetLVar("GENERIC_LEAR_TABLE_FOLD_" + i, value); return true; }
        foreach (var p in PassengerLights)
            if (varKey == "LJ35_LT_" + p) { sc.SetLVar("GENERIC_LEAR_SW_LTS_" + p, value); return true; }
        for (int i = 1; i <= 5; i++)
            if (varKey == "LJ35_LUGGAGE_" + i) { sc.SetLVar("LUGGAGE_" + i, value); return true; }
        for (int i = 1; i <= 3; i++)
            if (varKey == "LJ35_CARGO_" + i) { sc.SetLVar("CARGO_" + i, value); return true; }
        foreach (var seat in PayloadSeats)
        {
            if (seat.Key != varKey) continue;
            int pick = (int)Math.Round(value);
            for (int i = 0; i < seat.LVars.Length; i++) sc.SetLVar(seat.LVars[i], pick == i + 1 ? 1 : 0);
            return true;
        }
        return false;
    }

    /// <summary>A seat combo shows whichever of its L:vars is set; the panel reads only the first.</summary>
    private static readonly Dictionary<string, double> _seatState = new(StringComparer.Ordinal);
}
