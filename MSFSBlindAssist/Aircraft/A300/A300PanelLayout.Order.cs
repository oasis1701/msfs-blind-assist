namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The order of the A300's panels, and the rows that open a few of them. Which panel a control belongs
/// to comes from iniBuilds' component tree and <see cref="ControlPanels"/>
/// (<see cref="A300PanelLayout.PanelFor"/>); only the order is set here.
/// </summary>
public static partial class A300PanelLayout
{
    /// <summary>
    /// Panel order per section, the fleet's Airbuses' order (design decision, 2026-10-09): the FBW A320's
    /// overhead order where the A300 has the same panel (Electrical, IRS for ADIRS, APU, Oxygen, Fire,
    /// Hydraulics, Fuel, Air Conditioning, Bleed, Pressurization, ...), the A300's own panels beside their
    /// nearest kin, and its Instrument and Pedestal panels by system. A panel this list does not name keeps
    /// its place after the listed ones, so an iniBuilds update is never dropped.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> PanelOrder = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Overhead"] = new[]
        {
            "Electrical", "IRS", "APU", "Oxygen", "Fire", "Hydraulics", "Fuel", "Air Conditioning", "Bleed",
            "Pressurization", "Ventilation", "Anti-Ice", "Window and Probe Heat", "Wipers", "Signs", "Interior Lighting",
            "Exterior Lighting", "Flight Controls", "SAS Control", "Cockpit Door", "Cargo Smoke", "Recorder",
            "Engine Start",
        },
        ["Glareshield"] = new[] { "FCU", "EFIS Captain", "EFIS First Officer" },
        ["Instrument"] = new[]
        {
            "Warnings", "Gear", "Autobrake", "Thrust Rating Panel", "Landing Elevation", "Standby Instruments",
            "Source Switching", "Clock", "GPWS", "Captain Side", "First Officer Side",
        },
        ["Pedestal"] = new[]
        {
            "Engines", "Thrust Levers", "Flaps and Speed Brake", "Parking Brake", "Trim", "ECAM Control Panel",
            "Weather Radar", "Transponder", "VHF Radios", "Navigation Radios", "ADF Radios",
            "Audio Control Panel Captain", "Audio Control Panel First Officer", "MCDU Brightness", "IDC",
            "Pedestal Lighting",
        },
        ["Cockpit"] = new[] { "Yokes", "RAT", "Circuit Breakers", "Cockpit" },
        ["Cargo"] = new[] { "Cargo Door" },
    };

    /// <summary>
    /// Rows that open their panel, in this order, ahead of the checklist's order (<see cref="ChecklistStep"/>);
    /// the panel's other rows follow it. Electrical opens on the batteries, external power and the generators
    /// (the checklist names no generator); Warnings has the captain's two buttons, then the first officer's;
    /// Gear opens on the lever; the levers' panel on the flaps (its rows are not map controls).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> LeadRows = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Electrical"] = new[] { "A300_BATT_1", "A300_BATT_2", "A300_BATT_3", "A300_EXT_PWR", "A300_APU_GEN", "A300_GEN_1", "A300_GEN_2" },
        ["Warnings"] = new[] { "A300_MASTER_WARNING_CPT", "A300_MASTER_CAUTION_CPT", "A300_MASTER_WARNING_FO", "A300_MASTER_CAUTION_FO" },
        ["Gear"] = new[] { "A300_GEAR_LEVER" },
        ["Engines"] = new[] { "A300_ENG1_CUTOFF", "A300_ENG2_CUTOFF" },
        [A300Levers.Panel] = new[] { A300Levers.FlapsKey, A300Levers.SpeedBrakeKey, A300Levers.SpoilersArmKey },
    };

    /// <summary>
    /// The checklist step a row is ordered by: its control's own, or, for a row the checklist does not name, the
    /// step of the group it sits inside when the rows on both sides of it share one step (the checklist sets both
    /// ADFs in one step and never names their transfer buttons, which stay with their radios); else null.
    /// </summary>
    public static int? ChecklistStep(IReadOnlyList<A300PlacedRow> rows, A300PlacedRow row)
    {
        if (row.Control?.Checklist is int own)
            return own;
        int at = rows.ToList().IndexOf(row);
        int? before = rows.Take(at).Reverse().Select(r => r.Control?.Checklist).FirstOrDefault(s => s != null);
        int? after = rows.Skip(at + 1).Select(r => r.Control?.Checklist).FirstOrDefault(s => s != null);
        return before != null && before == after ? before : null;
    }

    /// <summary>The listed names that are present, in list order, then the rest in their own order.</summary>
    public static List<string> Ordered(IEnumerable<string> present, IReadOnlyList<string> order)
    {
        var have = present.ToList();
        var result = order.Where(have.Contains).ToList();
        result.AddRange(have.Where(p => !result.Contains(p)));
        return result;
    }

    private static void ApplyOrder(A300Placement placement)
    {
        foreach (var section in placement.Structure.Keys.ToList())
            if (PanelOrder.TryGetValue(section, out var order))
                placement.Structure[section] = Ordered(placement.Structure[section], order);

        // Each panel follows the aircraft's own checklist (design decision, 2026-10-10): the rows it names, by the
        // first step that names them (the generated map's `checklist`), then the rest in their own order. Stable,
        // so an encoder's two rows and the controls one step names together keep their order.
        foreach (var rows in placement.RowsByPanel.Values)
        {
            var steps = rows.Select(row => ChecklistStep(rows, row)).ToList();
            var sorted = rows.Select((row, i) => (row, i))
                .OrderBy(x => steps[x.i] ?? int.MaxValue).ThenBy(x => x.i)
                .Select(x => x.row).ToList();
            rows.Clear();
            rows.AddRange(sorted);
        }

        foreach (var (panel, keys) in LeadRows)
        {
            if (!placement.RowsByPanel.TryGetValue(panel, out var rows))
                continue;
            var lead = keys.Select(k => rows.Find(r => r.Key == k)).OfType<A300PlacedRow>().ToList();
            rows.RemoveAll(lead.Contains);
            rows.InsertRange(0, lead);
        }
    }
}
