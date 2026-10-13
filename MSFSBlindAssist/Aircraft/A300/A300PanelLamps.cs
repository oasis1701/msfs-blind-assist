using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>A cockpit light read through its own rule: the lamp node it is, and what a pilot calls it.</summary>
/// <param name="Node">The lamp's node in the generated map (<see cref="A300ControlMap.Lamps"/>).</param>
/// <param name="Name">Its button plus its legend plus "light" ([VAR-1]).</param>
public sealed record A300PanelLamp(string Node, string Name, string Panel)
{
    /// <summary>Its MSFSBA variable key, which also carries its Ctrl+M row.</summary>
    public string Key => "A300_LT_" + Node;
}

/// <summary>A named lamp with its generated rule: the variable its own subscription reads (the rule's
/// first input), the others' shared keys, and its bus's light power.</summary>
public sealed class A300ResolvedLamp
{
    public A300ResolvedLamp(A300PanelLamp lamp, A300LampRule rule, A300LightPower power)
    {
        Lamp = lamp;
        Rule = rule;
        Power = power;
    }

    public A300PanelLamp Lamp { get; }
    public A300LampRule Rule { get; }
    public A300LightPower Power { get; }

    /// <summary>The variable the lamp's own subscription reads, delivered under its own key.</summary>
    public A300LampInput Primary => Rule.Inputs[0];

    /// <summary>The key an input of this lamp arrives under.</summary>
    public string KeyFor(A300LampInput input) => input.Id == Primary.Id ? Lamp.Key : A300PanelLamps.InputKey(input);
}

/// <summary>
/// The cockpit lights read through their own rule, generated from the aircraft's emissive code
/// (<see cref="A300LampRule"/>, [A300-25]), named here panel by panel (design decision, 2026-10-09: every
/// light that can be read is spoken). A switch's OFF or ON legend that only mirrors its
/// position is left out: the row already says the position. The older fault lights stay in
/// <see cref="A300FaultLights"/>; a lamp named here never repeats one.
/// </summary>
public static class A300PanelLamps
{
    public static readonly IReadOnlyList<A300PanelLamp> All = new A300PanelLamp[]
    {
        // Electrical: the external power and override supply buttons, the IDG buttons, and the bus lights.
        new("EXT_PWR_SEQ1_LIGHT", "External power available light", "Electrical"),
        new("IDG_1_SEQ1_LIGHT", "IDG 1 disconnected light", "Electrical"),
        new("IDG_2_SEQ1_LIGHT", "IDG 2 disconnected light", "Electrical"),
        new("OVR_SUP_1_SEQ2_LIGHT", "Override supply 1 flow light", "Electrical"),
        new("OVR_SUP_2_SEQ2_LIGHT", "Override supply 2 flow light", "Electrical"),
        new("AC_BUS_1_OFF_LIGHT", "AC bus 1 off light", "Electrical"),
        new("AC_BUS_2_OFF_LIGHT", "AC bus 2 off light", "Electrical"),
        new("AC_ESS_BUS_OFF_LIGHT", "AC essential bus off light", "Electrical"),
        new("AC_EMER_ON_INV_LIGHT", "AC emergency bus on inverter light", "Electrical"),
        new("DC_NORM_BUS_LIGHT", "DC normal bus off light", "Electrical"),
        new("DC_ESS_ONBAT_LIGHT", "DC essential bus on battery light", "Electrical"),

        // IRS: each mode panel's four lights. iniBuilds' second and third panel nodes read IRS 3 and IRS 2,
        // in another order, so each is named from the IRS its rule reads.
        new("IRS_1_009_LIGHT", "IRS 1 align light", "IRS"),
        new("IRS_1_010_LIGHT", "IRS 1 on battery light", "IRS"),
        new("IRS_1_011_LIGHT", "IRS 1 battery fault light", "IRS"),
        new("IRS_1_012_LIGHT", "IRS 1 fault light", "IRS"),
        new("IRS_3_012_LIGHT", "IRS 2 align light", "IRS"),
        new("IRS_3_011_LIGHT", "IRS 2 on battery light", "IRS"),
        new("IRS_3_010_LIGHT", "IRS 2 battery fault light", "IRS"),
        new("IRS_3_009_LIGHT", "IRS 2 fault light", "IRS"),
        new("IRS_2_009_LIGHT", "IRS 3 align light", "IRS"),
        new("IRS_2_010_LIGHT", "IRS 3 on battery light", "IRS"),
        new("IRS_2_011_LIGHT", "IRS 3 battery fault light", "IRS"),
        new("IRS_2_012_LIGHT", "IRS 3 fault light", "IRS"),

        // APU: the available light, the start button's two legends and the fuel shutoff valve indicator.
        new("APU_AVAIL_SEQ1_LIGHT", "APU available light", "APU"),
        new("APU_START_SEQ2_LIGHT", "APU start on light", "APU"),
        new("APU_START_SEQ1_LIGHT", "APU starting light", "APU"),
        new("APU_FUELSHUT_SEQ1_LIGHT", "APU fuel shutoff valve open light", "APU"),

        // Fire: each handle's light (its lamp reads INI_*_FIRE_TEST, which the fire test and the aircraft's
        // fire logic set) and each agent button's SQUIB legend. The DISCH legend is the agent row's position.
        new("FIRE_HANDLE_ENG1_LIGHT", "Engine 1 fire handle light", "Fire"),
        new("FIRE_HANDLE_ENG2_LIGHT", "Engine 2 fire handle light", "Fire"),
        new("FIRE_HANDLE_APU_LIGHT", "APU fire handle light", "Fire"),
        new("FIRE_1_AG1_SEQ1_LIGHT", "Engine 1 agent 1 squib light", "Fire"),
        new("FIRE_1_AG2_SEQ1_LIGHT", "Engine 1 agent 2 squib light", "Fire"),
        new("FIRE_2_AG1_SEQ1_LIGHT", "Engine 2 agent 1 squib light", "Fire"),
        new("FIRE_2_AG2_SEQ1_LIGHT", "Engine 2 agent 2 squib light", "Fire"),
        new("FIRE_APU_AG_SEQ1_LIGHT", "APU agent squib light", "Fire"),

        // Fuel: each pump's LO PR legend and the trim tank isolation valve's flow bar (the valve, which the
        // trim logic opens; the four tank isolation valves' bars follow their switches, so they are left out).
        // iniBuilds wires the left center pump's lamp to the right inner pump 1's flag: read as it lights.
        new("OUT_TK_PMP_L1_SEQ1_LIGHT", "Left outer tank pump 1 low pressure light", "Fuel"),
        new("OUT_TK_PMP_L2_SEQ1_LIGHT", "Left outer tank pump 2 low pressure light", "Fuel"),
        new("OUT_TK_PMP_R1_SEQ1_LIGHT", "Right outer tank pump 1 low pressure light", "Fuel"),
        new("OUT_TK_PMP_R2_SEQ1_LIGHT", "Right outer tank pump 2 low pressure light", "Fuel"),
        new("INR_TK_PMP_L1_SEQ1_LIGHT", "Left inner tank pump 1 low pressure light", "Fuel"),
        new("INR_TK_PMP_L2_SEQ1_LIGHT", "Left inner tank pump 2 low pressure light", "Fuel"),
        new("INR_TK_PMP_R1_SEQ1_LIGHT", "Right inner tank pump 1 low pressure light", "Fuel"),
        new("INR_TK_PMP_R2_SEQ1_LIGHT", "Right inner tank pump 2 low pressure light", "Fuel"),
        new("CTR_TK_PMP_L_SEQ1_LIGHT", "Left center tank pump low pressure light", "Fuel"),
        new("CTR_TK_PMP_R_SEQ1_LIGHT", "Right center tank pump low pressure light", "Fuel"),
        new("TRMTK_1_PMP_SEQ1_LIGHT", "Left trim tank pump low pressure light", "Fuel"),
        new("TRMTK_2_PMP_SEQ1_LIGHT", "Right trim tank pump low pressure light", "Fuel"),
        new("TRMTK_ISO_SEQ1_LIGHT", "Trim tank isolation valve flow bar light", "Fuel"),

        // Air conditioning: the pack flow bars show the pack valves, which close with no bleed air while their
        // buttons stay on; the ram air light is the valve, which travels after its switch. The other legends
        // here are positions, or faults nothing in the aircraft writes.
        new("PACK_1_VALVE_IND_003_LIGHT", "Pack 1 flow bar light", "Air Conditioning"),
        new("PACK_2_VALVE_IND_004_LIGHT", "Pack 2 flow bar light", "Air Conditioning"),
        new("RAM_AIR_SEQ1_LIGHT", "Ram air valve open light", "Air Conditioning"),

        // Bleed: an indicator with no button of its own, named from its node (its legend is in no file); it
        // followed the APU bleed (2026-10-10). The other bleed legends are positions or never-written faults.
        new("GND_BLEED_VLVE_SEQ1_LIGHT", "Ground bleed valve light", "Bleed"),

        // Pressurization: the two system buttons pick one system or the other, so they are buttons
        // (A300PanelLayout.ButtonRows) labelled by these lights; the lights speak a press and an automatic
        // change. The regulator and outflow legends are positions; the fault lights are A300FaultLights.
        new("PRESS_SYS_1_SEQ1_LIGHT", "Pressurization system 1 light", "Pressurization"),
        new("PRESS_SYS_2_SEQ1_LIGHT", "Pressurization system 2 light", "Pressurization"),

        // Cockpit door: the status indicator's OPEN legend (its other legend is the cockpit door fault light).
        // The door button's own two lamps are wired to the ground cooling control's variables: left out.
        new("COCKPIT_DOOR_STATUS_SEQ1_LIGHT", "Cockpit door open light", "Cockpit Door"),

        // Cargo smoke: the three main deck smoke lights and each detector's lit legend (in no file, so named
        // from its button); the smoke test lights all nine while held.
        new("DECK_MID1_SMOKE_LIGHT", "Main deck mid 1 smoke light", "Cargo Smoke"),
        new("DECK_MID2_SMOKE_LIGHT", "Main deck mid 2 smoke light", "Cargo Smoke"),
        new("DECK_AFT_SMOKE_LIGHT", "Main deck aft smoke light", "Cargo Smoke"),
        new("SMOKE_MID1_1_SEQ1_LIGHT", "Main deck smoke detector mid 1 left light", "Cargo Smoke"),
        new("SMOKE_MID1_2_SEQ1_LIGHT", "Main deck smoke detector mid 1 right light", "Cargo Smoke"),
        new("SMOKE_MID2_1_SEQ1_LIGHT", "Main deck smoke detector mid 2 left light", "Cargo Smoke"),
        new("SMOKE_MID2_2_SEQ1_LIGHT", "Main deck smoke detector mid 2 right light", "Cargo Smoke"),
        new("SMOKE_AFT_1_SEQ1_LIGHT", "Main deck smoke detector aft left light", "Cargo Smoke"),
        new("SMOKE_AFT_2_SEQ1_LIGHT", "Main deck smoke detector aft right light", "Cargo Smoke"),

        // Engine start: each starter button's blue OPEN legend (the start valve, which closes by itself at about
        // 49 percent N2) and its armed legend (an engine armed for start and not yet running).
        new("ENG_1_START_SEQ1_LIGHT", "Engine 1 start valve open light", "Engine Start"),
        new("ENG_2_START_SEQ1_LIGHT", "Engine 2 start valve open light", "Engine Start"),
        new("ENG_1_START_SEQ2_LIGHT", "Engine 1 starter armed light", "Engine Start"),
        new("ENG_2_START_SEQ2_LIGHT", "Engine 2 starter armed light", "Engine Start"),

        // EFIS: each side's map filter, FPA, decision height and ATC message buttons are lit while selected, so
        // they label their buttons (ByButton); the stop rudder input light has its own variable, not the row's.
        new("EFIS_CSTR_CPT_SEQ1_LIGHT", "Captain constraints light", "EFIS Captain"),
        new("EFIS_WPT_CPT_SEQ1_LIGHT", "Captain waypoints light", "EFIS Captain"),
        new("EFIS_VOR_CPT_SEQ1_LIGHT", "Captain VORs light", "EFIS Captain"),
        new("EFIS_NDB_CPT_SEQ1_LIGHT", "Captain NDBs light", "EFIS Captain"),
        new("EFIS_ARPT_CPT_SEQ1_LIGHT", "Captain airports light", "EFIS Captain"),
        new("CPT_FPA_SEQ1_LIGHT", "Captain FPA light", "EFIS Captain"),
        new("DH_CPT_SEQ1_LIGHT", "Captain decision height light", "EFIS Captain"),
        new("ATC_CPT_SEQ1_LIGHT", "Captain ATC message light", "EFIS Captain"),
        new("STOP_CAPT_SEQ1_LIGHT", "Captain stop rudder input light", "EFIS Captain"),
        new("EFIS_FO_CSTR_SEQ1_LIGHT", "First officer constraints light", "EFIS First Officer"),
        new("EFIS_FO_WPT_SEQ1_LIGHT", "First officer waypoints light", "EFIS First Officer"),
        new("EFIS_FO_VOR_SEQ1_LIGHT", "First officer VORs light", "EFIS First Officer"),
        new("EFIS_FO_NDB_SEQ1_LIGHT", "First officer NDBs light", "EFIS First Officer"),
        new("EFIS_FO_ARPT_SEQ1_LIGHT", "First officer airports light", "EFIS First Officer"),
        new("FO_FPA_SEQ1_LIGHT", "First officer FPA light", "EFIS First Officer"),
        new("FO_DH_SEQ1_LIGHT", "First officer decision height light", "EFIS First Officer"),
        new("ATC_FO_SEQ1_LIGHT", "First officer ATC message light", "EFIS First Officer"),
        new("STOP_FO_SEQ1_LIGHT", "First officer stop rudder input light", "EFIS First Officer"),

        // Gear (design decision 2026-10-09: spoken): each gear's green down light (the overhead gear panel), and
        // its unlocked and door open lights (the main panel's; the overhead repeats them). Stock gear index 0 is
        // the nose, 1 left, 2 right; iniBuilds' GEAR1 is the nose and GEAR2 the left, as its overhead pairs them.
        new("GEAR_2_UNLK_SEQ2_LIGHT", "Nose gear down light", "Gear"),
        new("GEAR_1_UNLK_SEQ2_LIGHT", "Left gear down light", "Gear"),
        new("GEAR_3_UNLK_SEQ2_LIGHT", "Right gear down light", "Gear"),
        new("INDICATOR_LOWER_UNLK2_LIGHT", "Nose gear unlocked light", "Gear"),
        new("INDICATOR_LOWER_UNLK1_LIGHT", "Left gear unlocked light", "Gear"),
        new("INDICATOR_LOWER_UNLK3_LIGHT", "Right gear unlocked light", "Gear"),
        new("INDICATOR_LOWER_DOOR2_LIGHT", "Nose gear door open light", "Gear"),
        new("INDICATOR_LOWER_DOOR1_LIGHT", "Left gear door open light", "Gear"),
        new("INDICATOR_LOWER_DOOR3_LIGHT", "Right gear door open light", "Gear"),

        // The flap and slat indicator's speed brake light, and the brakes: the autobrake panel's two indicators
        // (named from their nodes; neither lit with autobrake armed) and the brake fan button's HOT legend.
        new("INDICATOR_SPDBRK_LIGHT", "Speed brake light", "Flaps and Speed Brake"),
        new("INDICATOR_AUTOBRK_LIGHT", "Autobrake light", "Autobrake"),
        new("INDICATOR_BRKFAIL_LIGHT", "Brake fail light", "Autobrake"),
        new("BRAKE_FAN_SEQ1_LIGHT", "Brakes hot light", "Autobrake"),

        // Source switching: each button carries both pilots' legends, crossed; each pilot's own labels their button.
        new("CPT_ATT_HDG_SEQ2_LIGHT", "Captain attitude and heading to IRS 3 light", "Source Switching"),
        new("CPT_ADC_INST_SEQ2_LIGHT", "Captain air data to system 2 light", "Source Switching"),
        new("CPT_FD_PUSH_SEQ2_LIGHT", "Captain flight director to system 2 light", "Source Switching"),
        new("CPT_EFIS_SGU_SEQ2_LIGHT", "Captain EFIS to SGU 3 light", "Source Switching"),
        new("FO_SW_ATT_SEQ2_LIGHT", "First officer attitude and heading to IRS 3 light", "Source Switching"),
        new("FO_SW_ADC_SEQ2_LIGHT", "First officer air data to system 2 light", "Source Switching"),
        new("FO_SW_FD_SEQ2_LIGHT", "First officer flight director to system 2 light", "Source Switching"),
        new("FO_SW_EFIS_SEQ2_LIGHT", "First officer EFIS to SGU 3 light", "Source Switching"),

        // The reversers (design decision 2026-10-09: spoken): each engine's REV and REV UNLK lights on the main
        // panel; and the rudder trim reset button's light, lit while it resets (it labels the button).
        new("INDICATOR_REVLK1_LIGHT", "Engine 1 reverser deployed light", "Thrust Levers"),
        new("INDICATOR_REVLK2_LIGHT", "Engine 2 reverser deployed light", "Thrust Levers"),
        new("INDICATOR_REV1_LIGHT", "Engine 1 reverser unlocked light", "Thrust Levers"),
        new("INDICATOR_REV2_LIGHT", "Engine 2 reverser unlocked light", "Thrust Levers"),
        new("TRIM_KORRY_SEQ2_LIGHT", "Rudder trim reset light", A300Trim.Panel),

        // ECAM control panel: each page button is lit while its page is the one the lower ECAM shows, picked or
        // automatic, so it labels its button; a press of the lit one goes back to the automatic page.
        new("ECAM_ENG_SEQ2_LIGHT", "Engine page light", "ECAM Control Panel"),
        new("ECAM_HYD_SEQ2_LIGHT", "Hydraulic page light", "ECAM Control Panel"),
        new("ECAM_AC_SEQ2_LIGHT", "AC electrical page light", "ECAM Control Panel"),
        new("ECAM_DC_SEQ2_LIGHT", "DC electrical page light", "ECAM Control Panel"),
        new("ECAM_BLEED_SEQ2_LIGHT", "Bleed page light", "ECAM Control Panel"),
        new("ECAM_COND_SEQ2_LIGHT", "Air conditioning page light", "ECAM Control Panel"),
        new("ECAM_PRESS_SEQ2_LIGHT", "Pressurization page light", "ECAM Control Panel"),
        new("ECAM_FUEL_SEQ2_LIGHT", "Fuel page light", "ECAM Control Panel"),
        new("ECAM_APU_SEQ2_LIGHT", "APU page light", "ECAM Control Panel"),
        new("ECAM_FCTL_SEQ2_LIGHT", "Flight controls page light", "ECAM Control Panel"),
        new("ECAM_DOOR_SEQ2_LIGHT", "Doors page light", "ECAM Control Panel"),
        new("ECAM_WHEEL_SEQ2_LIGHT", "Wheels page light", "ECAM Control Panel"),

        // Audio control panels: the VHF transmit key picked on each side is lit, and labels its key.
        new("CPT_VHF1_LX_LIGHT", "Captain VHF 1 transmit light", "Audio Control Panel Captain"),
        new("CPT_VHF2_LX_LIGHT", "Captain VHF 2 transmit light", "Audio Control Panel Captain"),
        new("CPT_VHF3_LX_LIGHT", "Captain VHF 3 transmit light", "Audio Control Panel Captain"),
        new("FO_VHF1_LX_LIGHT", "First officer VHF 1 transmit light", "Audio Control Panel First Officer"),
        new("FO_VHF2_LX_LIGHT", "First officer VHF 2 transmit light", "Audio Control Panel First Officer"),
        new("FO_VHF3_LX_LIGHT", "First officer VHF 3 transmit light", "Audio Control Panel First Officer"),
    };

    /// <summary>A button's own light, which labels it ("Pressurization system 1: On"): row key → lamp key.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByButton = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A300_PRESS_SYS_1"] = "A300_LT_PRESS_SYS_1_SEQ1_LIGHT",
        ["A300_PRESS_SYS_2"] = "A300_LT_PRESS_SYS_2_SEQ1_LIGHT",
        ["A300_EFIS_CSTR_CPT"] = "A300_LT_EFIS_CSTR_CPT_SEQ1_LIGHT",
        ["A300_EFIS_WPT_CPT"] = "A300_LT_EFIS_WPT_CPT_SEQ1_LIGHT",
        ["A300_EFIS_VOR_CPT"] = "A300_LT_EFIS_VOR_CPT_SEQ1_LIGHT",
        ["A300_EFIS_NDB_CPT"] = "A300_LT_EFIS_NDB_CPT_SEQ1_LIGHT",
        ["A300_EFIS_ARPT_CPT"] = "A300_LT_EFIS_ARPT_CPT_SEQ1_LIGHT",
        ["A300_CPT_FPA"] = "A300_LT_CPT_FPA_SEQ1_LIGHT",
        ["A300_DH_CPT"] = "A300_LT_DH_CPT_SEQ1_LIGHT",
        ["A300_ATC_CPT"] = "A300_LT_ATC_CPT_SEQ1_LIGHT",
        ["A300_EFIS_FO_CSTR"] = "A300_LT_EFIS_FO_CSTR_SEQ1_LIGHT",
        ["A300_EFIS_FO_WPT"] = "A300_LT_EFIS_FO_WPT_SEQ1_LIGHT",
        ["A300_EFIS_FO_VOR"] = "A300_LT_EFIS_FO_VOR_SEQ1_LIGHT",
        ["A300_EFIS_FO_NDB"] = "A300_LT_EFIS_FO_NDB_SEQ1_LIGHT",
        ["A300_EFIS_FO_ARPT"] = "A300_LT_EFIS_FO_ARPT_SEQ1_LIGHT",
        ["A300_FO_FPA"] = "A300_LT_FO_FPA_SEQ1_LIGHT",
        ["A300_FO_DH"] = "A300_LT_FO_DH_SEQ1_LIGHT",
        ["A300_ATC_FO"] = "A300_LT_ATC_FO_SEQ1_LIGHT",
        ["A300_STOP_CAPT"] = "A300_LT_STOP_CAPT_SEQ1_LIGHT",
        ["A300_STOP_FO"] = "A300_LT_STOP_FO_SEQ1_LIGHT",
        ["A300_CPT_ATT_HDG"] = "A300_LT_CPT_ATT_HDG_SEQ2_LIGHT",
        ["A300_CPT_ADC_INST"] = "A300_LT_CPT_ADC_INST_SEQ2_LIGHT",
        ["A300_CPT_FD_PUSH"] = "A300_LT_CPT_FD_PUSH_SEQ2_LIGHT",
        ["A300_CPT_EFIS_SGU"] = "A300_LT_CPT_EFIS_SGU_SEQ2_LIGHT",
        ["A300_FO_SW_ATT"] = "A300_LT_FO_SW_ATT_SEQ2_LIGHT",
        ["A300_FO_SW_ADC"] = "A300_LT_FO_SW_ADC_SEQ2_LIGHT",
        ["A300_FO_SW_FD"] = "A300_LT_FO_SW_FD_SEQ2_LIGHT",
        ["A300_FO_SW_EFIS"] = "A300_LT_FO_SW_EFIS_SEQ2_LIGHT",
        ["A300_TRIM_KORRY"] = "A300_LT_TRIM_KORRY_SEQ2_LIGHT",
        ["A300_ECAM_ENG"] = "A300_LT_ECAM_ENG_SEQ2_LIGHT",
        ["A300_ECAM_HYD"] = "A300_LT_ECAM_HYD_SEQ2_LIGHT",
        ["A300_ECAM_AC"] = "A300_LT_ECAM_AC_SEQ2_LIGHT",
        ["A300_ECAM_DC"] = "A300_LT_ECAM_DC_SEQ2_LIGHT",
        ["A300_ECAM_BLEED"] = "A300_LT_ECAM_BLEED_SEQ2_LIGHT",
        ["A300_ECAM_COND"] = "A300_LT_ECAM_COND_SEQ2_LIGHT",
        ["A300_ECAM_PRESS"] = "A300_LT_ECAM_PRESS_SEQ2_LIGHT",
        ["A300_ECAM_FUEL"] = "A300_LT_ECAM_FUEL_SEQ2_LIGHT",
        ["A300_ECAM_APU"] = "A300_LT_ECAM_APU_SEQ2_LIGHT",
        ["A300_ECAM_FCTL"] = "A300_LT_ECAM_FCTL_SEQ2_LIGHT",
        ["A300_ECAM_DOOR"] = "A300_LT_ECAM_DOOR_SEQ2_LIGHT",
        ["A300_ECAM_WHEEL"] = "A300_LT_ECAM_WHEEL_SEQ2_LIGHT",
        ["A300_ECAM_CLR"] = "A300_LAMP_ECAM_CLR_LIGHT",   // a fault light, lit while there is something to clear
        ["A300_CPT_VHF1_PUSH"] = "A300_LT_CPT_VHF1_LX_LIGHT",
        ["A300_CPT_VHF2_PUSH"] = "A300_LT_CPT_VHF2_LX_LIGHT",
        ["A300_CPT_VHF3_PUSH"] = "A300_LT_CPT_VHF3_LX_LIGHT",
        ["A300_FO_VHF1_PUSH"] = "A300_LT_FO_VHF1_LX_LIGHT",
        ["A300_FO_VHF2_PUSH"] = "A300_LT_FO_VHF2_LX_LIGHT",
        ["A300_FO_VHF3_PUSH"] = "A300_LT_FO_VHF3_LX_LIGHT",
    };

    /// <summary>Each named lamp with its rule from the shipped map; a lamp the map lacks is left out, loudly
    /// (an iniBuilds update renamed it: regenerate and rename here).</summary>
    public static readonly IReadOnlyList<A300ResolvedLamp> Resolved = Resolve(A300ControlMap.Load());

    /// <summary>Every input some lamp reads that is not that lamp's own subscription, once each.</summary>
    public static IReadOnlyList<A300LampInput> SharedInputs =>
        Resolved.SelectMany(l => l.Rule.Inputs.Where(i => i.Id != l.Primary.Id))
                .GroupBy(i => i.Id).Select(g => g.First()).ToList();

    /// <summary>The key a shared input arrives under: <c>A300_IN_</c> plus its id, e.g.
    /// <c>A300_IN_A_EXTERNAL_POWER_ON_1_BOOL</c>.</summary>
    public static string InputKey(A300LampInput input) =>
        "A300_IN_" + Regex.Replace(input.Id.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');

    private static IReadOnlyList<A300ResolvedLamp> Resolve(A300ControlMap map)
    {
        var byNode = map.Lamps.GroupBy(l => l.Node).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var resolved = new List<A300ResolvedLamp>();
        foreach (var lamp in All)
        {
            if (!byNode.TryGetValue(lamp.Node, out var mapLamp))
            {
                Utils.Logging.Log.Warn("A300", $"Lamp {lamp.Node} ({lamp.Name}) is not in the control map; it is not read.");
                continue;
            }
            resolved.Add(new A300ResolvedLamp(lamp, A300LampRule.Parse(mapLamp.State), mapLamp.Power));
        }
        return resolved;
    }
}
