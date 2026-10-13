using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The fault and warning lights the A300 announces. Each is named from the cockpit button it lights
/// (mapped from the cockpit model's emissive nodes to the light variables, 2026-10-04), shown on that
/// panel's status box, given a Ctrl+M row, and spoken both ways ("Engine 1 generator fault light
/// off"). Each streams on its own once-a-second subscription, never the continuous batch (58 more
/// names there pushed the FMA's sources across a batch boundary), and the changes are collected and
/// spoken together when the next continuous batch ends (<see cref="A300LampCallouts"/>). The
/// annunciator light test does not move these variables (measured), so it says nothing.
///
/// Each light names the bus its lamp draws light power from in the aircraft's emissive code (A = AC,
/// D = DC), so it is spoken only as the cockpit shows it (<see cref="A300LampBoard"/>, [A300-23]).
///
/// Left out on purpose: the FCU lamps (already on the FCU buttons' labels); the servo OFF and
/// override supply lights (switch positions the panel already shows); the reverser and altitude alert
/// lights (frequent, and the FMA already speaks); and lights the cockpit model draws nowhere (FADEC, IRS
/// warn). The fire handle and gear lights are read through the lamps the cockpit draws
/// (<see cref="A300PanelLamps"/>).
/// </summary>
public static class A300FaultLights
{
    private const A300LightPower A = A300LightPower.Ac, D = A300LightPower.Dc;

    /// <summary>The lights the aircraft flashes: the autoland warning light, about 0.6 s on and 0.6 s off
    /// (measured on its test switch; the aircraft's other flashing code drives the PFD, not lamps). Sampled once a
    /// second that aliases to about 3 s on, 3 s off, so it streams every change and the off hold
    /// (<see cref="A300LampSpeech.OffHoldMs"/>) sees each dark phase as the short one it is. Declared before
    /// <see cref="All"/>, which reads it as it initialises.</summary>
    private static readonly HashSet<string> FlashingVars = new(StringComparer.Ordinal) { "INI_AUTOLAND_LIGHT" };

    public static readonly IReadOnlyList<A300Lamp> All = new (string Var, string Name, string Panel, A300LightPower Power)[]
    {
        ("INI_ENG1_LOOP_A_LIGHT", "Engine 1 loop A fault light", "Fire", D),
        ("INI_ENG1_LOOP_B_LIGHT", "Engine 1 loop B fault light", "Fire", D),
        ("INI_ENG2_LOOP_A_LIGHT", "Engine 2 loop A fault light", "Fire", D),
        ("INI_ENG2_LOOP_B_LIGHT", "Engine 2 loop B fault light", "Fire", D),
        ("INI_APU_LOOP_A_LIGHT", "APU loop A fault light", "Fire", D),
        ("INI_APU_LOOP_B_LIGHT", "APU loop B fault light", "Fire", D),

        ("INI_elec_gen1_fault", "Engine 1 generator fault light", "Electrical", D),
        ("INI_elec_gen2_fault", "Engine 2 generator fault light", "Electrical", D),
        ("INI_elec_standby_gen_fault", "Standby generator fault light", "Electrical", A),
        // The battery buttons' upper legend follows the battery's charge current (measured in flight,
        // 2026-10-06), so the owner had it named a charge light (2026-10-09).
        ("INI_BAT1_light", "Battery 1 charge light", "Electrical", D),
        ("INI_BAT2_light", "Battery 2 charge light", "Electrical", D),
        ("INI_BAT3_light", "Battery 3 charge light", "Electrical", D),

        ("INI_hyd_blue_light", "Engine 1 blue pump fault light", "Hydraulics", D),
        ("INI_hyd_green1_light", "Engine 1 green pump fault light", "Hydraulics", D),
        ("INI_hyd_green2_light", "Engine 2 green pump fault light", "Hydraulics", D),
        ("INI_hyd_yellow_light", "Engine 2 yellow pump fault light", "Hydraulics", D),

        ("INI_SPEEDBRAKE7_FAULT", "Spoiler 7 fault light", "Flight Controls", A),
        ("INI_SPEEDBRAKE6_FAULT", "Spoiler 6 fault light", "Flight Controls", A),
        ("INI_SPEEDBRAKE5_FAULT", "Spoiler 5 fault light", "Flight Controls", A),
        ("INI_SPEEDBRAKE4_1_FAULT", "Spoilers 4 and 1 fault light", "Flight Controls", A),
        ("INI_SPEEDBRAKE3_2_FAULT", "Spoilers 3 and 2 fault light", "Flight Controls", A),
        ("INI_PITCH_FEEL1_FAULT", "Pitch feel system 1 fault light", "Flight Controls", D),
        ("INI_PITCH_FEEL2_FAULT", "Pitch feel system 2 fault light", "Flight Controls", A),
        ("INI_RUDDER_TRAVEL1_FAULT", "Rudder travel limiter system 1 fault light", "Flight Controls", D),
        ("INI_RUDDER_TRAVEL2_FAULT", "Rudder travel limiter system 2 fault light", "Flight Controls", A),
        ("INI_FLAPS_SYS1_FAULT", "Flap system 1 fault light", "Flight Controls", D),
        ("INI_FLAPS_SYS2_FAULT", "Flap system 2 fault light", "Flight Controls", D),
        ("INI_SLATS_SYS1_FAULT", "Slat system 1 fault light", "Flight Controls", D),
        ("INI_SLATS_SYS2_FAULT", "Slat system 2 fault light", "Flight Controls", D),

        ("INI_PACK1_FAULT", "Pack 1 fault light", "Air Conditioning", D),
        ("INI_PACK2_FAULT", "Pack 2 fault light", "Air Conditioning", D),
        ("INI_ISOLATION_VALVE_LEFT_FAULT", "Left isolation valve fault light", "Bleed", A),
        ("INI_ISOLATION_VALVE_RIGHT_FAULT", "Right isolation valve fault light", "Bleed", A),

        ("INI_cabin_sys1_regulator_fault", "Cabin regulator 1 fault light", "Pressurization", A),
        ("INI_cabin_sys2_regulator_fault", "Cabin regulator 2 fault light", "Pressurization", A),
        ("INI_CABIN_RATE_LIGHT", "Excessive cabin rate light", "Pressurization", A),
        ("INI_CABIN_LO_DELTA_PSI_LIGHT", "Low cabin differential pressure light", "Pressurization", A),

        ("INI_ENG1_ANTI_ICE_FAULT", "Engine 1 anti-ice fault light", "Anti-Ice", A),
        ("INI_ENG2_ANTI_ICE_FAULT", "Engine 2 anti-ice fault light", "Anti-Ice", A),
        ("INI_ANTI_ICE_WING_MODE_FAULT", "Wing anti-ice supply mode fault light", "Anti-Ice", A),

        ("INI_WINDOW_HEAT1_FAULT", "Lateral window heat fault light", "Window and Probe Heat", A),
        ("INI_WINDOW_HEAT2_FAULT", "Windshield and lateral window heat fault light", "Window and Probe Heat", A),

        ("INI_APU_FAULT", "APU fault light", "APU", D),
        ("INI_APU_LO_PR", "APU fuel low pressure light", "APU", D),

        ("INI_ENG1_MASTER_SWITCH_LIGHT", "Engine 1 master switch fault light", "Engines", D),
        ("INI_ENG2_MASTER_SWITCH_LIGHT", "Engine 2 master switch fault light", "Engines", D),

        ("INI_engine1_oil_low_press_light", "Engine 1 oil low pressure light", "Engines", A),
        ("INI_engine2_oil_low_press_light", "Engine 2 oil low pressure light", "Engines", A),

        ("INI_AUTOLAND_LIGHT", "Autoland warning light", "EFIS Captain", D),

        ("INI_GPWS_LIGHT", "GPWS warning light", "GPWS", A),
        ("INI_GLIDESLOPE_LIGHT", "Below glide slope light", "GPWS", A),
        ("INI_TERR_MODE_FAULT", "Terrain mode fault light", "GPWS", A),
        ("INI_FMS1_message_light", "MCDU message light", "Captain Side", A),

        ("INI_ECAM_CLR_LIGHT", "ECAM clear light", "ECAM Control Panel", A),

        ("INI_COCKPIT_DOOR_FAULT", "Cockpit door fault light", "Cockpit Door", D),
    }
    .Select(l => new A300Lamp("A300_LAMP_" + l.Var.Substring(4).ToUpperInvariant(), l.Var, l.Name, l.Panel, SpeaksOff: true, l.Power,
        Flashes: FlashingVars.Contains(l.Var)))
    .ToArray();
}

/// <summary>One light's change, for <see cref="A300LampCallouts"/>.</summary>
public readonly record struct A300LampChange(string Name, bool On);

/// <summary>
/// The lights changed since the last continuous batch ended, as one sentence, so an engine start reads as a few
/// sentences rather than a dozen queued lines. A single change reads as its own phrase ("Pack 1
/// fault light on"); several the same way are counted ("3 lights off: Spoiler 7 fault, Spoiler 6
/// fault, Spoiler 5 fault"); lights coming on are read before lights going out. Pure.
/// </summary>
public static class A300LampCallouts
{
    public static string Compose(IReadOnlyList<A300LampChange> changes)
    {
        var parts = new List<string>(2);
        Add(parts, changes.Where(c => c.On).Select(c => c.Name).ToList(), "on");
        Add(parts, changes.Where(c => !c.On).Select(c => c.Name).ToList(), "off");
        return string.Join(". ", parts);
    }

    private static void Add(List<string> parts, List<string> names, string word)
    {
        if (names.Count == 1)
            parts.Add($"{names[0]} {word}");
        else if (names.Count > 1)
            parts.Add($"{names.Count.ToString(CultureInfo.InvariantCulture)} lights {word}: {string.Join(", ", names.Select(Short))}");
    }

    private static string Short(string name) => name.EndsWith(" light", StringComparison.Ordinal) ? name[..^6] : name;
}
