using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>What a panel row does when the pilot operates it.</summary>
public enum A300RowAction { Set, Press, Increase, Decrease, Custom, Typed }

/// <summary>A row after placement: the variable key MSFSBA registers, and what it drives.</summary>
/// <param name="Positions">State value → spoken word, for a Set row's combo; empty for the rest.</param>
public sealed record A300PlacedRow(string Key, string Name, A300RowAction Action, A300Control? Control,
    IReadOnlyDictionary<double, string> Positions);

public sealed class A300Placement
{
    /// <summary>Section → panel names, in cockpit order.</summary>
    public Dictionary<string, List<string>> Structure { get; } = new();
    /// <summary>Panel name → placed rows, in cockpit order.</summary>
    public Dictionary<string, List<A300PlacedRow>> RowsByPanel { get; } = new();
}

/// <summary>
/// The A300's panels: which section and panel a control belongs to comes from the component tree
/// iniBuilds grouped the cockpit by (Overhead, Glareshield, Instrument, Pedestal, Cockpit, Cargo), so
/// an iniBuilds update is picked up by regenerating the map. Where iniBuilds groups by place and the
/// fleet's Airbuses by system, <see cref="ControlPanels"/> sends each control to its system's panel
/// (design decision, 2026-10-09). The ORDER of the panels, and the rows that open a few of them, come
/// from <see cref="PanelOrder"/> and <see cref="LeadRows"/>; everything else keeps the cockpit file's
/// order. The pedestal's radio and audio group (iniBuilds' "STD", 113 controls) is divided by what each
/// control is.
///
/// Left out on purpose: guard covers (they only move the 3D model; the switch under them works with
/// the cover shut, measured from the click code), the IDC's copies of the classic radio panel (the
/// same L:vars, so one copy is enough; the IDC's own keypad comes with the IDC work), cosmetic parts
/// (seats, armrests, visors, yoke bases, the oil-gauge pointer knobs, the tablet brightness buttons)
/// and second buttons that fire the same event as the first (autothrottle disconnect 2, the second RAT
/// button), and every knob that already has a typed box (<see cref="A300TypedValues"/>): the box sets the
/// value outright, so the knob's increase and decrease rows only added rows to step through (design
/// decision, Robin, 2026-10-14). Those are the 14 VHF and NAV frequency knobs, and the FCU speed, heading,
/// altitude and vertical speed knobs, the two decision height knobs, the three altimeter knobs, the three
/// course knobs and the two transponder code knobs, 56 rows in all. Every knob's push and pull stays. The ADF
/// knobs and the altitude bug knobs (no typed box), the flex temperature and landing elevation knobs, the
/// transfer buttons and the squelch switches stay too, and the generated map keeps every control ([A300-2]).
/// The main cargo door switch is placed: the door moves only while it is held, so its row is a spring switch
/// (<see cref="A300CargoDoor"/>), and the 70 and 145 degree buttons only pick how far the door opens.
///
/// Names are what a pilot says: iniBuilds' tooltip titles share names across families ("BATTERY" ×3,
/// "ENG GEN" ×2) and a few are wrong (the vertical speed knob is titled "ALTITUDE KNOB", the first
/// officer's altimeter "CPT BAROMETER"), so <see cref="NameOverrides"/> names every control whose
/// title is unclear or clashes, and the audio panels follow one rule (<see cref="AudioName"/>).
/// <c>VarNameCollisionTests.Panel_rows_do_not_share_a_spoken_name</c> pins that no two rows of a
/// panel share a name.
/// </summary>
public static partial class A300PanelLayout
{
    public static readonly IReadOnlyList<string> SectionOrder = new[]
    {
        "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit", "Cargo",
    };

    private static readonly Dictionary<string, string> AreaSections = new(StringComparer.Ordinal)
    {
        ["OVERHEAD"] = "Overhead",
        ["EFIS"] = "Glareshield",
        ["MIP"] = "Instrument",
        ["PEDESTAL"] = "Pedestal",
        ["COCKPIT"] = "Cockpit",
        ["CABIN"] = "Cargo",
    };

    /// <summary>iniBuilds' component name → the panel the pilot sees. Two names on one panel merge. A control
    /// listed in <see cref="ControlPanels"/> goes to its system's panel instead (a split iniBuilds panel).</summary>
    private static readonly Dictionary<string, string> PanelNames = new(StringComparer.Ordinal)
    {
        ["CAB_PRESS"] = "Pressurization",
        ["IRS"] = "IRS",
        ["APU_BACK"] = "APU",
        ["ANTI_ICE"] = "Anti-Ice",
        ["OVERHEAD_WIPERS"] = "Wipers",
        ["WINDOW_HEAT"] = "Window and Probe Heat",
        ["OVERHEAD_BLEED"] = "Air Conditioning",
        ["OVERHEAD_OXYGEN"] = "Oxygen",
        ["OVERHEAD_ELEC_GAUGES"] = "Electrical",
        ["OVERHEAD_ELEC"] = "Electrical",
        ["OVERHEAD_FCTL"] = "Flight Controls",
        ["OVERHEAD_VENT"] = "Ventilation",
        ["OVERHEAD_CDLC"] = "Cockpit Door",
        ["OVERHEAD_FLTRCDR"] = "Recorder",
        ["CVRD_PANEL"] = "Recorder",
        ["OVERHEAD_FUEL"] = "Fuel",
        ["OVERHEAD_HYD"] = "Hydraulics",
        ["OVERHEAD_ENGINE"] = "Engine Start",
        ["OVERHEAD_FIRE"] = "Fire",
        ["OVERHEAD_LEVERS"] = "SAS Control",
        ["OVERHEAD_LIGHTS"] = "Interior Lighting",
        ["STANDBY_COMPASS"] = "Interior Lighting",
        ["FCU"] = "FCU",
        ["EFIS_LEFT"] = "EFIS Captain",
        ["EFIS_RIGHT"] = "EFIS First Officer",
        ["MIP_LEFT"] = "Captain Side",
        ["MIP_CENTER"] = "Standby Instruments",
        ["MIP_RIGHT"] = "First Officer Side",
        ["THROTTLE_QUADRENT"] = "Engines",
        ["GPWS_FLAPS_CONFIG"] = "GPWS",
        ["FMGS"] = "MCDU Brightness",
        ["NAV_AIDS"] = "Navigation Radios",
        ["IDC"] = "IDC",
        ["ECAM_BRT"] = "ECAM Control Panel",
        ["ECAM"] = "ECAM Control Panel",
        ["TRIM"] = "Trim",
        ["MAN_GEAR_HANDLE"] = "Gear",
        ["FCTL"] = "Yokes",
        ["RAT"] = "RAT",
        ["BACK_PANEL"] = "Cockpit",
        ["CB"] = "Circuit Breakers",
        ["CARGO_PANEL"] = "Cargo Door",
    };

    private static readonly Regex Excluded = new(
        @"^AIRLINER_(.*_IDC(_BUTTON)?|TABLET_.*|MAN_GEAR_HANDLE|MAN_GEAR_HANDLE_HIDE|CPT_ARMREST|FO_ARMREST|" +
        @"CPT_YOKE_BASE|FO_YOKE_BASE|CPT_VISOR|FO_VISOR|SEAT_.*|OIL_ENG\d_QTY_LIMIT_MANIP(_PW)?|AT_DISCO2|RAT_FO|" +
        // The VHF and NAV frequency knobs: a typed box replaces each radio's (see the class comment).
        @"(CPT|FO)_VHF[12]_(MHZ|KHZ)|VOR100_(CAPT|FO)|VOR10_(CAPT|FO)|ILS_100|ILS_10|" +
        // The other knobs that have a typed box: the FCU windows' four, the decision height's two, the three altimeters',
        // the three courses' and the squawk's two. The $ keeps every knob's push and pull (SPEED_KNOB_PUSH is a row).
        @"SPEED_KNOB|HEADING_KNOB|ALT_KNOB|VS_KNOB|(CAPT|FO)_DH_SET|(CPT|FO|STBY)_ALTIMETER_KNOB|" +
        @"VOR_CRS_(CAPT|FO)|ILS_CRS|TCAS_(BIG|SMALL))$",
        RegexOptions.CultureInvariant);

    private static readonly Regex Audio = new(
        @"^AIRLINER_(?:CPT|FO)_(VHF\d|HF\d|INT|PA|MKR|VOR\d|ILS|ADF\d)_(VOL_BUTTON|VOL|PUSH)$",
        RegexOptions.CultureInvariant);

    /// <summary>Whether a map control is placed on a panel at all.</summary>
    public static bool IsPlaced(A300Control control) =>
        control.Kind is not (A300Kinds.Cover or A300Kinds.None) && !Excluded.IsMatch(control.Id);

    public static string SectionFor(A300Control control) =>
        PanelSections.TryGetValue(PanelFor(control), out var moved) ? moved
        : AreaSections.TryGetValue(control.Area, out var section) ? section : "Cockpit";

    public static string PanelFor(A300Control control)
    {
        if (ControlPanels.TryGetValue(Short(control.Id), out var system))
            return system;
        if (control.Panel == "STD")
            return PedestalPanelFor(Short(control.Id));
        return PanelNames.TryGetValue(control.Panel, out var panel) ? panel : SpokenWord(control.Panel.Replace('_', ' '));
    }

    /// <summary>The pedestal's radio and audio group, split by what each control is.</summary>
    private static string PedestalPanelFor(string id)
    {
        if (id.StartsWith("WER_", StringComparison.Ordinal))
            return "Weather Radar";
        if (id is "PEDESTAL_LIGHT_KNOB" or "OVERHEAD_LIGHT_KNOB")
            return "Pedestal Lighting";
        if (id.StartsWith("TCAS_", StringComparison.Ordinal) || id == "XPDR_SWITCH")
            return "Transponder";
        if (Regex.IsMatch(id, @"ADF\d_(BIG|MED|SMALL|TFR|ANT|TONE)$", RegexOptions.CultureInvariant))
            return "ADF Radios";
        if (Regex.IsMatch(id, @"_VHF_(TFR|SQL)$", RegexOptions.CultureInvariant))
            return "VHF Radios";
        if (id.StartsWith("CPT_", StringComparison.Ordinal))
            return "Audio Control Panel Captain";
        if (id.StartsWith("FO_", StringComparison.Ordinal))
            return "Audio Control Panel First Officer";
        return "Pedestal";
    }

    /// <summary>The input event id without its <c>AIRLINER_</c> prefix.</summary>
    public static string Short(string id) =>
        id.StartsWith("AIRLINER_", StringComparison.OrdinalIgnoreCase) ? id.Substring("AIRLINER_".Length) : id;

    /// <summary>The spoken name of a placed control.</summary>
    public static string NameFor(A300Control control)
    {
        string id = Short(control.Id);
        if (NameOverrides.TryGetValue(id, out var name))
            return name;
        if (AudioName(control.Id) is { } audio)
            return audio;
        string spoken = SpokenWord(string.IsNullOrWhiteSpace(control.Title) ? id.Replace('_', ' ') : control.Title);
        if (control.Kind == A300Kinds.Button && id.EndsWith("_PUSH", StringComparison.Ordinal))
            return spoken + " push";
        if (control.Kind == A300Kinds.Button && id.EndsWith("_PULL", StringComparison.Ordinal))
            return spoken + " pull";
        return spoken;
    }

    /// <summary>"VHF 1 receiver", "Interphone volume", "HF 2 transmit": the audio control panels
    /// carry one receiver switch, one volume knob and (for the transmitters) one transmit key per
    /// radio, all three titled alike by iniBuilds.</summary>
    public static string? AudioName(string id)
    {
        var m = Audio.Match(id);
        if (!m.Success)
            return null;
        string radio = m.Groups[1].Value switch
        {
            "INT" => "Interphone",
            "PA" => "PA",
            "MKR" => "Marker",
            "ILS" => "ILS",
            var r => Regex.Replace(r, @"(\D+)(\d)", "$1 $2", RegexOptions.CultureInvariant),
        };
        string what = m.Groups[2].Value switch
        {
            "VOL_BUTTON" => "receiver",
            "VOL" => "volume",
            _ => "transmit",
        };
        return $"{radio} {what}";
    }

    /// <summary>State value → spoken word for a Set row: the layout's override where there is one,
    /// else iniBuilds' word in sentence case, else "Position n".</summary>
    public static IReadOnlyDictionary<double, string> PositionWords(A300Control control)
    {
        var overrides = ParsePositions(PositionOverrides.TryGetValue(Short(control.Id), out var spec) ? spec : null);
        var words = new Dictionary<double, string>();
        foreach (var (position, label) in control.OrderedPositions())
        {
            string word = overrides.TryGetValue(position, out var o) ? o
                : string.IsNullOrWhiteSpace(label) ? $"Position {position.ToString(CultureInfo.InvariantCulture)}"
                : SpokenWord(label);
            words[control.StateForPosition(position)] = word;
        }
        return words;
    }

    /// <summary>Parses <c>"0=Off;1=Arm;2=On"</c>.</summary>
    public static IReadOnlyDictionary<double, string> ParsePositions(string? spec)
    {
        var result = new Dictionary<double, string>();
        if (string.IsNullOrWhiteSpace(spec))
            return result;
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            result[double.Parse(part.Substring(0, eq), NumberStyles.Float, CultureInfo.InvariantCulture)] = part.Substring(eq + 1).Trim();
        }
        return result;
    }

    /// <summary>Abbreviations pilots say as letters, kept in capitals by <see cref="SpokenWord"/>.</summary>
    private static readonly HashSet<string> SpokenAsLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "ADC", "ADF", "ADI", "APU", "ATC", "ATS", "CVR", "CWS", "DC", "DH", "ECAM", "EFIS", "FD", "FPA", "FPV",
        "GPWS", "HF", "HP", "IDC", "IDG", "ILS", "IRS", "MCDU", "MCU", "ND", "NDB", "PA", "PFD", "PTU", "QNH", "RA", "RAT",
        "SGU", "STD", "TA", "TCAS", "TOGA", "TRP", "VHF", "VOR", "WX", "WXR", "XPDR",
    };

    /// <summary>
    /// Spoken form of one of iniBuilds' upper-case tooltip words: sentence case ("STORM LIGHT" →
    /// "Storm light"), except that a single letter ("Loop A"), a token with a digit ("SYS1", "N1")
    /// and an abbreviation said as letters ("TA/RA", "APU master") keep their capitals. Tokens are
    /// separated by spaces, '/', '-', '(', ')' and '&amp;', which are kept as they were.
    /// </summary>
    public static string SpokenWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;
        string trimmed = word.Trim();
        var spoken = new StringBuilder(trimmed.Length);
        bool first = true;
        int i = 0;
        while (i < trimmed.Length)
        {
            if (IsWordSeparator(trimmed[i]))
            {
                spoken.Append(trimmed[i++]);
                continue;
            }
            int start = i;
            while (i < trimmed.Length && !IsWordSeparator(trimmed[i]))
                i++;
            spoken.Append(SpokenToken(trimmed.Substring(start, i - start), first));
            first = false;
        }
        return spoken.ToString();
    }

    private static bool IsWordSeparator(char c) => c is ' ' or '/' or '-' or '(' or ')' or '&';

    private static string SpokenToken(string token, bool first)
    {
        if (token.Any(char.IsAsciiDigit))
            return token.ToUpperInvariant();
        if ((token.Length == 1 && char.IsLetter(token[0])) || SpokenAsLetters.Contains(token))
            return token.ToUpperInvariant();
        string lower = token.ToLowerInvariant();
        return first ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
    }

    /// <summary>
    /// Places every placed map control, in map (cockpit file) order, then the hand-written levers
    /// (<see cref="A300Levers"/>) on the throttle quadrant, and finally applies
    /// <see cref="PanelOrder"/> and <see cref="LeadRows"/>. Encoders become two rows
    /// ("… increase", "… decrease").
    /// </summary>
    public static A300Placement Place(A300ControlMap map)
    {
        var placement = new A300Placement();
        foreach (var section in SectionOrder)
            placement.Structure[section] = new List<string>();

        foreach (var control in map.Controls)
        {
            if (!IsPlaced(control))
                continue;
            var rows = RowsFor(placement, SectionFor(control), PanelFor(control));
            string name = NameFor(control);
            switch (control.Kind)
            {
                case A300Kinds.Command or A300Kinds.Toggle when ButtonRows.Contains(Short(control.Id)):
                    rows.Add(new A300PlacedRow(control.Key, name, A300RowAction.Press, control, new Dictionary<double, string>()));
                    break;
                case A300Kinds.Toggle:
                case A300Kinds.Command:
                case A300Kinds.Selector:
                case A300Kinds.Spring:
                    rows.Add(new A300PlacedRow(control.Key, name, A300RowAction.Set, control, PositionWords(control)));
                    break;
                case A300Kinds.Knob:
                    rows.Add(new A300PlacedRow(control.Key, name, A300RowAction.Set, control, new Dictionary<double, string>()));
                    break;
                case A300Kinds.Button:
                case A300Kinds.Hold:
                    rows.Add(new A300PlacedRow(control.Key, name, A300RowAction.Press, control, new Dictionary<double, string>()));
                    break;
                case A300Kinds.Encoder:
                    rows.Add(new A300PlacedRow(control.Key + "#INC", name + " increase", A300RowAction.Increase, control, new Dictionary<double, string>()));
                    rows.Add(new A300PlacedRow(control.Key + "#DEC", name + " decrease", A300RowAction.Decrease, control, new Dictionary<double, string>()));
                    break;
            }
        }

        // A guard comes just before the switch it covers (the cockpit file lists it after).
        foreach (var rows in placement.RowsByPanel.Values)
            MoveGuardsBeforeTheirSwitch(rows);

        // Typed values end the panel they belong to (A300TypedValues).
        foreach (var typed in A300TypedValues.All)
            if (placement.RowsByPanel.TryGetValue(typed.Panel, out var rows))
                rows.Add(new A300PlacedRow(typed.Key, typed.Name, A300RowAction.Typed, null, new Dictionary<double, string>()));

        var levers = RowsFor(placement, "Pedestal", A300Levers.Panel);
        levers.Add(new A300PlacedRow(A300Levers.FlapsKey, A300Levers.Name(A300Levers.FlapsKey), A300RowAction.Custom, null, A300Levers.FlapPositions));
        levers.Add(new A300PlacedRow(A300Levers.SpoilersArmKey, A300Levers.Name(A300Levers.SpoilersArmKey), A300RowAction.Custom, null, A300Levers.ArmPositions));
        levers.Add(new A300PlacedRow(A300Levers.SpeedBrakeKey, A300Levers.Name(A300Levers.SpeedBrakeKey), A300RowAction.Custom, null, new Dictionary<double, string>()));

        ApplyOrder(placement);

        foreach (var section in placement.Structure.Where(s => s.Value.Count == 0).Select(s => s.Key).ToList())
            placement.Structure.Remove(section);
        return placement;
    }

    private static void MoveGuardsBeforeTheirSwitch(List<A300PlacedRow> rows)
    {
        foreach (var guard in rows.Where(r => r.Key.EndsWith("_GUARD", StringComparison.Ordinal)).ToList())
        {
            string switchKey = guard.Key.Substring(0, guard.Key.Length - "_GUARD".Length);
            int at = rows.FindIndex(r => r.Key == switchKey);
            if (at < 0)
                continue;
            rows.Remove(guard);
            rows.Insert(rows.FindIndex(r => r.Key == switchKey), guard);
        }
    }

    private static List<A300PlacedRow> RowsFor(A300Placement placement, string section, string panel)
    {
        if (!placement.RowsByPanel.TryGetValue(panel, out var rows))
        {
            rows = new List<A300PlacedRow>();
            placement.RowsByPanel[panel] = rows;
            if (!placement.Structure.TryGetValue(section, out var panels))
                placement.Structure[section] = panels = new List<string>();
            panels.Add(panel);
        }
        return rows;
    }
}
