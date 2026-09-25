using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Cabin and ground: doors and service panels, the ground equipment the vendor EFB's Services
/// page places, payload and fuel loading, and the water system. Every door, panel, cover and
/// cart is a plain L:var in the model (live 2026-09-10: chocks, pitot and AOA covers read 1 at
/// the gate, everything else 0; water 7.5 / 7.5). Payload stations are 16 stock stations whose
/// names are string SimVars the app cannot read, so the panel shows the two crew stations and
/// the derived payload total.
/// </summary>
public partial class SkywardC680Definition
{
    private const double TankCapacityLb = 850 * 6.7;   // 850 US gal a side (flight_model.cfg), Jet-A at 6.7 lb/gal

    private static Dictionary<string, SimVarDefinition> BuildCabinVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- Doors and Service Panels
        AddSwitch(v, "C680_DOOR_MAIN", "SW_SOV_PANELS_Door", "Main Cabin Door", "Closed", "Open");
        AddSwitch(v, "C680_DOOR_MAIN_HANDLE", "SW_SOV_PANELS_Handle_Door", "Main Door Handle", "Latched", "Unlatched");
        AddSwitch(v, "C680_DOOR_BAG", "SW_SOV_PANELS_Door_Baggage", "Baggage Door", "Closed", "Open");
        AddSwitch(v, "C680_DOOR_BAG_HANDLE", "SW_SOV_PANELS_Handle_Baggage", "Baggage Door Handle", "Latched", "Unlatched");
        AddSwitch(v, "C680_PANEL_FUEL", "SW_SOV_PANELS_Fuel_Panel", "Fuel Service Panel", "Closed", "Open");
        AddSwitch(v, "C680_FUEL_LID", "SW_SOV_PANELS_Fuel_Lid_1", "Single-Point Refuel Lid", "Closed", "Open");
        AddSwitch(v, "C680_FUEL_SW_1", "SW_SOV_PANELS_Fuel_Switch_1", "Refuel Switch 1");
        AddSwitch(v, "C680_FUEL_SW_2", "SW_SOV_PANELS_Fuel_Switch_2", "Refuel Switch 2");
        AddSwitch(v, "C680_PANEL_OXY", "SW_SOV_PANELS_Oxygen_Panel", "Oxygen Service Panel", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_HYD", "SW_SOV_PANELS_Hydraulics_Panel", "Hydraulics Service Panel", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_BAT_L", "SW_SOV_PANELS_Bat_L_Panel", "Left Battery Panel", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_BAT_R", "SW_SOV_PANELS_Bat_R_Panel", "Right Battery Panel", "Closed", "Open");
        AddSwitch(v, "C680_BAT_DISC_L", "BAT_DISC_L_2", "Left Battery Disconnect", "Connected", "Disconnected", "Ground only, with the left battery panel open.");
        AddSwitch(v, "C680_BAT_DISC_R", "BAT_DISC_R_2", "Right Battery Disconnect", "Connected", "Disconnected", "Ground only, with the right battery panel open.");
        AddSwitch(v, "C680_PANEL_GPU", "SW_SOV_PANELS_GPU_Door", "GPU Door", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_AVN_L", "SW_SOV_PANELS_Avionics_door_L", "Left Avionics Door", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_AVN_R", "SW_SOV_PANELS_Avionics_door_R", "Right Avionics Door", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_LOO", "SW_SOV_PANELS_Loo_Panel", "Lavatory Drain Panel", "Closed", "Open");
        AddSwitch(v, "C680_PANEL_PUSHER", "SW_SOV_PANELS_Pusher_Door", "Door Pusher", "Stowed", "Out");
        AddFlag(v, "C680_CAS_DOOR", "SW_SOV_CAS_Door_open", "CAS Door Open", "No", "Yes");
        AddFlag(v, "C680_DOOR_LEAK", "SW_SOV_MAIN_DOOR_LEAK", "Main Door Seal", "Sealed", "Leaking");

        // ---- Ground Equipment (the EFB Services cards; each an L:var, some a family written together)
        AddSwitch(v, "C680_CHOCKS", "Static_Gear_Chock_L", "Wheel Chocks", "Removed", "Placed");
        AddSwitch(v, "C680_COVER_PITOT", "Static_Pitot_1", "Pitot Covers", "Removed", "Fitted");
        AddSwitch(v, "C680_COVER_AOA", "Static_AOA_L", "AOA Sensor Covers", "Removed", "Fitted");
        AddSwitch(v, "C680_COVER_ENG", "Static_Engine_Cover_L", "Engine Covers", "Removed", "Fitted");
        AddSwitch(v, "C680_COVER_WING", "Static_Wing_L_1", "Wing Covers", "Removed", "Fitted");
        AddSwitch(v, "C680_COVER_TAIL", "Static_Wing_T_1", "Tail Covers", "Removed", "Fitted");
        AddSwitch(v, "C680_WS_COVER", "SW_SOV_Static_Windshield_Cover", "Windshield Cover", "Removed", "Fitted");
        AddSwitch(v, "C680_GPU_CART", "gpu_cart", "Ground Power Unit", "Away", "Connected");
        AddSwitch(v, "C680_HYD_CART", "SW_SOV_HYDRAULICS_Ground_Active", "Hydraulics Cart", "Away", "Connected");
        AddSwitch(v, "C680_OXY_CART", "Oxygen_Tank_L_Fill_Active", "Oxygen Cart", "Away", "Connected");
        AddSwitch(v, "C680_LAV_CART", "SW_SOV_Water_Waste", "Lavatory Service Cart", "Away", "Connected");
        AddSwitch(v, "C680_FUEL_TRUCK", "Fuel_truck_Visible", "Fuel Truck", "Away", "Connected");
        AddSwitch(v, "C680_VIP_VEHICLE", "VIP_Vehicles", "VIP Vehicle", "Away", "Present");
        AddSwitch(v, "C680_RED_CARPET", "SW_SOV_Carpet", "Red Carpet", "Away", "Out");

        // ---- Payload and Fuel Load
        AddSimReadout(v, "C680_GROSS_WEIGHT", "TOTAL WEIGHT", "Gross Weight", "pounds", "F0");
        AddSimReadout(v, "C680_EMPTY_WEIGHT", "EMPTY WEIGHT", "Basic Empty Weight", "pounds", "F0");
        AddSimReadout(v, "C680_MAX_WEIGHT", "MAX GROSS WEIGHT", "Maximum Gross Weight", "pounds", "F0");
        AddSimReadout(v, "C680_CREW_L", "PAYLOAD STATION WEIGHT:1", "Pilot Station", "pounds", "F0");
        AddSimReadout(v, "C680_CREW_R", "PAYLOAD STATION WEIGHT:2", "Copilot Station", "pounds", "F0");
        AddDerived(v, "C680_PAYLOAD_TOTAL", "Payload");
        AddDerived(v, "C680_WEIGHT_MARGIN", "Weight Margin");
        AddTyped(v, "C680_FUEL_L_SET", "Left Tank Fuel", "pounds", "0 to 5695", currentKey: "C680_FUEL_L_LB");
        AddTyped(v, "C680_FUEL_R_SET", "Right Tank Fuel", "pounds", "0 to 5695", currentKey: "C680_FUEL_R_LB");
        AddTyped(v, "C680_FUEL_TOTAL_SET", "Total Fuel", "pounds", "0 to 11390, split evenly", currentKey: "C680_FUEL_TOTAL_LB");

        // ---- Water and Waste
        AddReadout(v, "C680_WATER_CLEAN", "SW_SOV_Water_Quantity_clean", "Potable Water", "number", "F1");
        AddReadout(v, "C680_WATER_BLUE", "SW_SOV_Water_Blue_Juice", "Blue Juice", "number", "F1");
        AddReadout(v, "C680_WASTE", "SW_SOV_Water_Waste_Purity", "Waste Tank", "number", "F1");
        AddButton(v, "C680_WATER_FILL", "Refill Potable Water", "On the ground with the tank lid open.");
        AddSwitch(v, "C680_WATER_LID", "Water_tank_sink_lid", "Water Tank Lid", "Closed", "Open");

        foreach (var k in new[] { "C680_GROSS_WEIGHT", "C680_EMPTY_WEIGHT", "C680_MAX_WEIGHT" }) Cache(v, k);
        return v;
    }

    private static readonly List<string> DoorsControls = new() { "C680_DOOR_MAIN", "C680_DOOR_MAIN_HANDLE", "C680_DOOR_BAG", "C680_DOOR_BAG_HANDLE", "C680_PANEL_FUEL", "C680_FUEL_LID", "C680_FUEL_SW_1", "C680_FUEL_SW_2", "C680_PANEL_OXY", "C680_PANEL_HYD", "C680_PANEL_BAT_L", "C680_PANEL_BAT_R", "C680_BAT_DISC_L", "C680_BAT_DISC_R", "C680_PANEL_GPU", "C680_PANEL_AVN_L", "C680_PANEL_AVN_R", "C680_PANEL_LOO", "C680_PANEL_PUSHER" };
    private static readonly List<string> DoorsDisplay = new() { "C680_CAS_DOOR", "C680_DOOR_LEAK" };
    private static readonly List<string> GroundControls = new() { "C680_CHOCKS", "C680_COVER_PITOT", "C680_COVER_AOA", "C680_COVER_ENG", "C680_COVER_WING", "C680_COVER_TAIL", "C680_WS_COVER", "C680_GPU_CART", "C680_HYD_CART", "C680_OXY_CART", "C680_LAV_CART", "C680_FUEL_TRUCK", "C680_VIP_VEHICLE", "C680_RED_CARPET" };
    private static readonly List<string> PayloadControls = new() { "C680_FUEL_L_SET", "C680_FUEL_R_SET", "C680_FUEL_TOTAL_SET" };
    private static readonly List<string> PayloadDisplay = new() { "C680_GROSS_WEIGHT", "C680_MAX_WEIGHT", "C680_WEIGHT_MARGIN", "C680_EMPTY_WEIGHT", "C680_PAYLOAD_TOTAL", "C680_CREW_L", "C680_CREW_R", "C680_FUEL_L_LB", "C680_FUEL_R_LB", "C680_FUEL_TOTAL_LB" };
    private static readonly List<string> WaterControls = new() { "C680_WATER_FILL", "C680_WATER_LID" };
    private static readonly List<string> WaterDisplay = new() { "C680_WATER_CLEAN", "C680_WATER_BLUE", "C680_WASTE" };

    /// <summary>Families the EFB card writes as one: every member L:var takes the same value.</summary>
    private static readonly Dictionary<string, string[]> GroundFamilies = new(StringComparer.Ordinal)
    {
        ["C680_CHOCKS"] = new[] { "Static_Gear_Chock_L", "Static_Gear_Chock_R", "Static_Gear_Chock_C" },
        ["C680_COVER_PITOT"] = new[] { "Static_Pitot_1", "Static_Pitot_2", "Static_Pitot_3" },
        ["C680_COVER_AOA"] = new[] { "Static_AOA_L", "Static_AOA_R" },
        ["C680_COVER_ENG"] = new[] { "Static_Engine_Cover_L", "Static_Engine_Cover_R" },
        ["C680_COVER_WING"] = new[] { "Static_Wing_L_1", "Static_Wing_L_2", "Static_Wing_L_3", "Static_Wing_L_4", "Static_Wing_L_5", "Static_Wing_L_6", "Static_Wing_R_1", "Static_Wing_R_2", "Static_Wing_R_3", "Static_Wing_R_4", "Static_Wing_R_5", "Static_Wing_R_6" },
        ["C680_COVER_TAIL"] = new[] { "Static_Wing_T_1", "Static_Wing_T_2", "Static_Wing_T_3", "Static_Wing_T_4", "Static_Wing_T_5", "Static_Wing_T_6" },
    };

    private static readonly HashSet<string> CabinPlainLVars = new(StringComparer.Ordinal)
    {
        "C680_DOOR_MAIN", "C680_DOOR_MAIN_HANDLE", "C680_DOOR_BAG", "C680_DOOR_BAG_HANDLE", "C680_PANEL_FUEL", "C680_FUEL_LID",
        "C680_FUEL_SW_1", "C680_FUEL_SW_2", "C680_PANEL_OXY", "C680_PANEL_HYD", "C680_PANEL_BAT_L", "C680_PANEL_BAT_R",
        "C680_BAT_DISC_L", "C680_BAT_DISC_R", "C680_PANEL_GPU", "C680_PANEL_AVN_L", "C680_PANEL_AVN_R", "C680_PANEL_LOO", "C680_PANEL_PUSHER",
        "C680_WS_COVER", "C680_GPU_CART", "C680_HYD_CART", "C680_OXY_CART", "C680_LAV_CART", "C680_FUEL_TRUCK", "C680_VIP_VEHICLE", "C680_RED_CARPET",
        "C680_WATER_LID"
    };

    private bool HandleCabinSet(string varKey, double value, SimConnectManager sc)
    {
        if (CabinPlainLVars.Contains(varKey))
        {
            sc.SetLVar(GetVariables()[varKey].Name, value);
            return true;
        }
        if (GroundFamilies.TryGetValue(varKey, out var family))
        {
            foreach (var lvar in family) sc.SetLVar(lvar, value);
            return true;
        }
        switch (varKey)
        {
            case "C680_FUEL_L_SET": sc.SetSimVar("FUELSYSTEM TANK LEVEL:1", Math.Clamp(value, 0, TankCapacityLb) / TankCapacityLb * 100, "percent"); return true;
            case "C680_FUEL_R_SET": sc.SetSimVar("FUELSYSTEM TANK LEVEL:2", Math.Clamp(value, 0, TankCapacityLb) / TankCapacityLb * 100, "percent"); return true;
            case "C680_FUEL_TOTAL_SET":
            {
                double pct = Math.Clamp(value / 2, 0, TankCapacityLb) / TankCapacityLb * 100;
                sc.SetSimVar("FUELSYSTEM TANK LEVEL:1", pct, "percent");
                sc.SetSimVar("FUELSYSTEM TANK LEVEL:2", pct, "percent");
                return true;
            }
            case "C680_WATER_FILL": sc.SetLVar("SW_SOV_Water_Quantity_clean", 7.5); return true;
        }
        return false;
    }

    /// <summary>Payload and margin rows from the cached weights.</summary>
    private bool TryCabinDisplay(string varKey, out string displayText)
    {
        switch (varKey)
        {
            case "C680_PAYLOAD_TOTAL":
                if (!Has("C680_GROSS_WEIGHT") || !Has("C680_EMPTY_WEIGHT") || !Has("C680_FUEL_TOTAL_LB")) { displayText = "not yet read"; return true; }
                displayText = $"{Live("C680_GROSS_WEIGHT") - Live("C680_EMPTY_WEIGHT") - Live("C680_FUEL_TOTAL_LB"):0} pounds";
                return true;
            case "C680_WEIGHT_MARGIN":
                if (!Has("C680_GROSS_WEIGHT") || !Has("C680_MAX_WEIGHT")) { displayText = "not yet read"; return true; }
                double margin = Live("C680_MAX_WEIGHT") - Live("C680_GROSS_WEIGHT");
                displayText = margin >= 0 ? $"{margin:0} pounds under maximum gross" : $"{-margin:0} pounds OVER maximum gross";
                return true;
        }
        displayText = string.Empty;
        return false;
    }
}
