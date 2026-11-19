namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The page grammar's rules from BlueBird's own control names (plan 20) that its structure does not say: which apps have sections,
/// which names make a tab row, the pictures that show a state, the role words of a name, the headings and settings rows BlueBird
/// names otherwise, the value boxes' names and the reading orders. Where on the tablet the pilot is (the section, R1, and a tab
/// row's current tab, R2) comes from BlueBird's selected state since plan 21 (<see cref="Bbs757EfbModelReader"/>), so the table of
/// distinctive names that told a section went. Evidence: docs/b757.md, "The tablet window"
/// ([B757-22]).
/// </summary>
internal static class Bbs757EfbPageRules
{
    /// <summary>The apps whose pages have sections (Systems and Checklist read as their app alone).</summary>
    internal static bool HasSections(string app) => app is "AIRCRAFT" or "NAVIGATION" or "SETTINGS";

    /// <summary>Whether a control name is a tab row's button: BlueBird's <c>_tab_button</c> (Maintenance), <c>_tab_button_N</c>,
    /// <c>_tab_N</c> (Failures), <c>_source_button_N</c> and <c>_type_button_N</c> (Charts), <c>_selector_button_N</c> (Weather). PERF calc's <c>_unit_button_N</c> is
    /// not one: they are three independent switches.</summary>
    internal static bool IsTabName(string name)
    {
        if (name.EndsWith("_tab_button", StringComparison.Ordinal)) return true;
        int us = name.LastIndexOf('_');
        if (us < 0 || us == name.Length - 1 || !name[(us + 1)..].All(char.IsAsciiDigit)) return false;
        string head = name[..us];
        return head.EndsWith("_tab_button", StringComparison.Ordinal) || head.EndsWith("_tab", StringComparison.Ordinal)
            || head.EndsWith("_source_button", StringComparison.Ordinal) || head.EndsWith("_type_button", StringComparison.Ordinal)
            || head.EndsWith("_selector_button", StringComparison.Ordinal);
    }

    /// <summary>The state a picture shows, from its file's name (the part after the last <c>/</c>), as the tablet's tiles use pictures
    /// for what their words do not say: the chocks tile's picture is <c>bbs_ldg_gr_chocks.png</c> with the chocks in place and
    /// <c>bbs_ldg_gr.png</c> with them removed (measured 2026-11-06). Null for any other picture.</summary>
    internal static string? PictureState(string text)
    {
        string file = text[(Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\')) + 1)..];
        return file.ToLowerInvariant() switch
        {
            "bbs_ldg_gr_chocks.png" => "IN PLACE",
            "bbs_ldg_gr.png" => "REMOVED",
            _ => null,
        };
    }

    /// <summary>What a name's role words make of its control, among the labels one line is read from.</summary>
    internal enum NameRole { None, Label, Value, Unit }

    /// <summary>A control name read as the page grammar reads it (plan 20, R3 and R4): its role, the parts left once the role words
    /// are taken out (a label and the value it pairs with have the same core), and whether it is a card's title.</summary>
    internal sealed record NameInfo(NameRole Role, IReadOnlyCollection<string> Core, bool IsTitle)
    {
        /// <summary>The core as one string, parts in a fixed order: how two names with the same core are matched.</summary>
        internal string CoreKey => string.Join(' ', Core.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>The headings BlueBird gives no title name: Cockpit presets' section labels, which the page draws as <c>*_label</c>
    /// (the exceptions table of the page grammar, plan 20 D5). Per app, the control names.</summary>
    private static readonly (string App, string Name)[] HeadingNames =
    {
        ("AIRCRAFT", "presets_label"), ("AIRCRAFT", "quick_start_label"), ("AIRCRAFT", "circuits_state_label"),
        ("AIRCRAFT", "ground_label"), ("AIRCRAFT", "inflight_label"), ("AIRCRAFT", "custom_label"),
    };

    /// <summary>Whether the table names the control as a heading of its app's page.</summary>
    internal static bool IsHeadingName(string app, string name) => HeadingNames.Contains((app, name));

    /// <summary>The settings rows BlueBird does not name <c>*_setting_grid</c> or <c>*_row</c>: the AIRAC page's Navigraph status row,
    /// which holds its status label and its LOGOUT.</summary>
    private static readonly (string App, string Name)[] RowGridNames = { ("SETTINGS", "auth_status_grid") };

    /// <summary>Whether a grid is a settings row, whose first label that is no value names the buttons in it (R5's fourth source,
    /// the final review's I8: "SIMBRIEF ID NUMBER" names its LOGOUT, "LATEST CYCLE:" its REMOVE): BlueBird's <c>*_setting_grid</c> and
    /// <c>*_row</c> grids, and the table's.</summary>
    internal static bool IsRowGridName(string app, string name) =>
        name.EndsWith("_setting_grid", StringComparison.Ordinal) || name.EndsWith("_row", StringComparison.Ordinal)
        || RowGridNames.Contains((app, name));

    private static readonly HashSet<string> CardWords = new(StringComparer.Ordinal) { "canvas", "card", "panel", "grid", "box" };

    /// <summary>The core a container's own title has (<see cref="NameInfo.CoreKey"/>): its name less its container word
    /// (<c>maintenance_gear_doors_canvas</c> is titled by <c>maintenance_gear_doors_title</c>); "" for a name that is no card's.</summary>
    internal static string TitleKeyOf(string containerName)
    {
        var parts = NameParts(containerName);
        return parts.Length >= 2 && CardWords.Contains(parts[^1])
            ? string.Join(' ', parts[..^1].OrderBy(p => p, StringComparer.Ordinal)) : "";
    }

    /// <summary>Whether a control's name says it is a separator (<c>planning_weather_primary_info_separator_value_0</c>, a "|"
    /// between two readouts): marks with no word in them that are no value, whatever its role words say.</summary>
    internal static bool IsSeparatorName(string name) => NameParts(name).Contains("separator");

    private static readonly HashSet<string> LabelWords = new(StringComparer.Ordinal) { "label", "title", "name", "lbl", "len", "heading", "field" };

    /// <summary>The parts of a control's name that say what kind of control it is, not what it is about (plan 20, R5): taken out
    /// of a control's name and of a label's before the two are compared.</summary>
    internal static readonly HashSet<string> ControlWords = new(StringComparer.Ordinal)
        { "btn", "button", "slider", "toggle", "box", "action", "setup", "input", "dropdown", "textbox" };

    /// <summary>A control name's parts once role words and control words are taken out, as one string in a fixed order: how a
    /// control and the label that names it are matched (R5). Empty when nothing is left.</summary>
    internal static string ControlKey(string name) =>
        string.Join(' ', NameParts(name).Where(p => !LabelWords.Contains(p) && !ValueWords.Contains(p) && !ControlWords.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal));

    /// <summary>A label's core once control words are taken out too (the other half of <see cref="ControlKey"/>).</summary>
    internal static string ControlKey(NameInfo label) =>
        string.Join(' ', label.Core.Where(p => !ControlWords.Contains(p)).OrderBy(p => p, StringComparer.Ordinal));

    /// <summary>Whether a name is an input the reader cannot read: a box the tablet types into or picks from, named
    /// <c>*_input</c>, <c>*_dropdown</c> or <c>*_textbox</c>, or PERF calc's four bare field names. Only for a control read as "other"
    /// (a tree captured before plan 21 read the text box and drop-down classes): one read by its class is a text box or a drop-down.</summary>
    internal static bool IsUnreadInputName(string name) =>
        name.EndsWith("_input", StringComparison.Ordinal) || name.EndsWith("_dropdown", StringComparison.Ordinal)
        || name.EndsWith("_textbox", StringComparison.Ordinal)
        || name is "planning_perf_calc_weight" or "planning_perf_calc_wind" or "planning_perf_calc_temp" or "planning_perf_calc_press";

    /// <summary>The name of a value box on Fuel and Weight &amp; balance (R7), from the box's control name; null for any other
    /// box.</summary>
    internal static string? FieldName(string name)
    {
        bool fuel = name.StartsWith("fuel_", StringComparison.Ordinal) || name.StartsWith("total_fuel_", StringComparison.Ordinal);
        bool wb = name.StartsWith("wb_", StringComparison.Ordinal);
        if (fuel && name.EndsWith("_qty_box", StringComparison.Ordinal)) return "QUANTITY";
        if (fuel && name.EndsWith("_weight_box", StringComparison.Ordinal)) return "WEIGHT";
        if (wb && (name.EndsWith("_weight_box", StringComparison.Ordinal) || name.EndsWith("_value_box", StringComparison.Ordinal))) return "WEIGHT";
        return null;
    }

    /// <summary>The name of the label holding a Fuel slider's percent (R8): <c>fuel_slider_left</c> reads
    /// <c>fuel_left_tank_label</c>, <c>fuel_slider_total</c> reads <c>total_fuel_perc_label</c>; null for any other slider.</summary>
    internal static string? SliderValueName(string sliderName)
    {
        const string prefix = "fuel_slider_";
        if (!sliderName.StartsWith(prefix, StringComparison.Ordinal)) return null;
        string side = sliderName[prefix.Length..];
        return side == "total" ? "total_fuel_perc_label" : side.Length > 0 ? "fuel_" + side + "_tank_label" : null;
    }

    // ---- R9: reading order ------------------------------------------------------

    private static readonly string[] WeightBalanceTopRow =
    {
        "wb_empty_weight_title", "wb_zero_fuel_weight_title", "wb_zero_fuel_cg_title",
        "wb_block_fuel_title", "wb_takeoff_weight_title", "wb_landing_weight_title",
    };

    private static readonly string[] FuelTanks = { "left", "center", "right", "total" };

    /// <summary>The items in the order the page reads (plan 20, R9). Weight &amp; balance puts the six lines BlueBird draws across the
    /// top (EMPTY, ZFW, ZFW CG, BLK FUEL, TAKE-OFF, LANDING) first, in tree order; Fuel reads tank by tank (LEFT, CENTER, RIGHT, TOTAL
    /// FUEL), each a level 2 heading, its slider, its quantity box and its weight box, matched on the tank part of BlueBird's names
    /// (<c>fuel_left_slider_title</c>, <c>fuel_slider_left</c>, <c>fuel_left_qty_box</c>, <c>fuel_left_weight_box</c>; the total's are
    /// <c>fuel_slider_total</c>, <c>total_fuel_qty_box</c>, <c>total_fuel_weight_box</c>); Cockpit presets reads by where its headings
    /// are drawn (<see cref="ByPresetSections"/>). Every other page keeps tree order.</summary>
    internal static IReadOnlyList<Bbs757EfbItem> Reorder(string app, string section, IReadOnlyList<Bbs757EfbItem> items)
    {
        if (app != "AIRCRAFT") return items;
        if (section == "WEIGHT & BALANCE")
        {
            var top = items.Where(i => i.Kind == Bbs757EfbKind.Row && WeightBalanceTopRow.Contains(i.Name)).ToList();
            return top.Count == 0 ? items : top.Concat(items.Where(i => !top.Contains(i))).ToList();
        }
        if (section == "FUEL") return ByTank(items);
        if (section == "COCKPIT PRESETS") return ByPresetSections(items);
        return items;
    }

    // ---- Cockpit presets (the final review's M3) ------------------------------------------------------------------

    /// <summary>Cockpit presets' section headings, and the three columns of its PRESETS section.</summary>
    private static readonly string[] PresetSections = { "presets_label", "quick_start_label", "circuits_state_label" };
    private static readonly string[] PresetColumns = { "ground_label", "inflight_label", "custom_label" };

    /// <summary>
    /// Cockpit presets in the order a sighted pilot reads it, from where BlueBird draws it: BlueBird's tree gives its six headings
    /// first and its 17 buttons after them. Each section heading (PRESETS, QUICK START, CIRCUITS STATE, level 2, top to bottom) is
    /// followed by what is drawn below it down to the next; in PRESETS, its three columns (ON GROUND, IN FLIGHT, CUSTOM) become level
    /// 3 headings, left to right, each followed by what is drawn nearest below it. Within a group, top to bottom, and what shares a row
    /// left to right (ALL BREAKERS PUSHED IN, then PUSH ALL CBs). Tree order when the page draws none of the section headings.
    /// </summary>
    private static IReadOnlyList<Bbs757EfbItem> ByPresetSections(IReadOnlyList<Bbs757EfbItem> items)
    {
        bool IsOf(Bbs757EfbItem i, string[] names) => i.Kind == Bbs757EfbKind.Heading && names.Contains(i.Name);
        var sections = items.Where(i => IsOf(i, PresetSections)).OrderBy(i => i.Box.Top).ToList();
        if (sections.Count == 0) return items;
        var columns = items.Where(i => IsOf(i, PresetColumns)).Select(i => i with { Level = 3 }).ToList();
        var rest = items.Where(i => !IsOf(i, PresetSections) && !IsOf(i, PresetColumns)).ToList();
        int SectionIndex(Bbs757EfbItem i)
        {
            int s = 0;
            for (int k = 0; k < sections.Count; k++) if (sections[k].Box.Top <= i.Box.Top) s = k;
            return s;
        }
        static int Middle(Bbs757EfbItem i) => i.Box.Left + i.Box.Width / 2;
        var result = new List<Bbs757EfbItem>(items.Count);
        for (int s = 0; s < sections.Count; s++)
        {
            result.Add(sections[s]);
            var mine = rest.Where(i => SectionIndex(i) == s).ToList();
            var cols = columns.Where(c => SectionIndex(c) == s).OrderBy(Middle).ToList();
            if (cols.Count == 0)
            {
                result.AddRange(ByRows(mine));
                continue;
            }
            foreach (var col in cols)
            {
                result.Add(col);
                result.AddRange(ByRows(mine.Where(i => cols.OrderBy(c => Math.Abs(Middle(c) - Middle(i))).First() == col)));
            }
        }
        return result;
    }

    /// <summary>Items top to bottom, those drawn on one row (their boxes overlapping the row's first one up and down) left to right.</summary>
    private static IEnumerable<Bbs757EfbItem> ByRows(IEnumerable<Bbs757EfbItem> items)
    {
        var left = items.OrderBy(i => i.Box.Top).ThenBy(i => i.Box.Left).ToList();
        while (left.Count > 0)
        {
            var first = left[0];
            var row = left.Where(i => i.Box.Top < first.Box.Bottom && i.Box.Bottom > first.Box.Top).OrderBy(i => i.Box.Left).ToList();
            if (row.Count == 0) row.Add(first);   // a box with no height
            foreach (var i in row) yield return i;
            left.RemoveAll(i => row.Contains(i));
        }
    }

    private static string? TankOf(Bbs757EfbItem item)
    {
        if (item.Kind is not (Bbs757EfbKind.Heading or Bbs757EfbKind.Slider or Bbs757EfbKind.Field)) return null;
        if (!item.Name.StartsWith("fuel_", StringComparison.Ordinal) && !item.Name.StartsWith("total_fuel_", StringComparison.Ordinal)) return null;
        var parts = NameParts(item.Name);
        return FuelTanks.FirstOrDefault(parts.Contains);
    }

    private static int RankInTank(Bbs757EfbItem item) =>
        item.Kind == Bbs757EfbKind.Heading ? 0 : item.Kind == Bbs757EfbKind.Slider ? 1 : NameParts(item.Name).Contains("qty") ? 2 : 3;

    private static IReadOnlyList<Bbs757EfbItem> ByTank(IReadOnlyList<Bbs757EfbItem> items)
    {
        var groups = FuelTanks.ToDictionary(t => t, t => items.Where(i => TankOf(i) == t).OrderBy(RankInTank).ToList());
        foreach (var tank in FuelTanks)
        {
            var g = groups[tank];
            // The total's title is BlueBird's total_fuel_label, which names the slider: the heading is made from the slider.
            if (g.Count > 0 && !g.Any(i => i.Kind == Bbs757EfbKind.Heading) && g.FirstOrDefault(i => i.Kind == Bbs757EfbKind.Slider) is { } slider)
            {
                string name = tank == "total" ? "total_fuel_label" : "fuel_" + tank + "_slider_title";
                g.Insert(0, new Bbs757EfbItem(Bbs757EfbKind.Heading, slider.Text, slider.Box, slider.Hit, false, name, Level: 2, Name: name));
            }
        }
        var result = new List<Bbs757EfbItem>(items.Count + 1);
        bool placed = false;
        foreach (var item in items)
        {
            if (TankOf(item) is null) { result.Add(item); continue; }
            if (placed) continue;
            placed = true;
            foreach (var tank in FuelTanks) result.AddRange(groups[tank]);
        }
        return result;
    }

    /// <summary>Whether a control's words are none, or only one state word or action word.</summary>
    internal static bool IsStateOrAction(string text)
    {
        string t = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return t.Length == 0 || Bbs757EfbScreen.StateWords.Contains(t, StringComparer.Ordinal) || Bbs757EfbScreen.ActionWords.Contains(t, StringComparer.Ordinal);
    }

    /// <summary>A button's name from its own control name (R11): the parts without <c>btn</c>/<c>button</c>, when two or more
    /// remain and all are letters ("PUSHBACK LEFT"); null otherwise.</summary>
    internal static string? NameFromParts(string name)
    {
        var parts = NameParts(name).Where(p => p is not ("btn" or "button")).ToList();
        return parts.Count >= 2 && parts.All(p => p.All(char.IsLetter)) ? string.Join(' ', parts).ToUpperInvariant() : null;
    }
    private static readonly HashSet<string> ValueWords = new(StringComparer.Ordinal) { "value", "val", "saved" };

    /// <summary>A control name split on <c>_</c> and spaces, lower-case invariant.</summary>
    internal static string[] NameParts(string name) =>
        name.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.ToLowerInvariant()).ToArray();

    /// <summary>
    /// The role of a name by its last role word: <c>label</c>, <c>title</c>, <c>name</c>, <c>lbl</c>, <c>len</c> make a label,
    /// <c>heading</c> (BlueBird's Callouts headings) makes a label, <c>value</c>, <c>val</c>, <c>saved</c> a value, and <c>value_label</c> or <c>saved_label</c> is a value (the value's own
    /// label), while <c>length_value_title</c> is a title. A name ending in <c>unit</c> is a unit. A title is a label whose name ends
    /// in <c>title</c>, <c>heading</c> or <c>title_label</c>. A name with no role word is none: a bare number or word.
    /// </summary>
    internal static NameInfo ReadName(string name)
    {
        var parts = NameParts(name);
        bool IsRole(string p) => LabelWords.Contains(p) || ValueWords.Contains(p);
        var core = parts.Where(p => !IsRole(p)).ToList();
        if (parts.Length > 1 && parts[^1] == "unit")
            return new NameInfo(NameRole.Unit, core.Where(p => p != "unit").ToList(), false);
        var roleAt = Enumerable.Range(0, parts.Length).Where(i => IsRole(parts[i])).ToList();
        if (roleAt.Count == 0) return new NameInfo(NameRole.None, parts, false);
        int last = roleAt[^1];
        bool value = ValueWords.Contains(parts[last])
            || (parts[last] == "label" && roleAt.Count >= 2 && roleAt[^2] == last - 1 && ValueWords.Contains(parts[last - 1]));
        if (value) return new NameInfo(NameRole.Value, core, false);
        bool title = parts[^1] is "title" or "heading" || (parts[^1] == "label" && parts.Length >= 2 && parts[^2] == "title");
        return new NameInfo(NameRole.Label, core, title);
    }
}
