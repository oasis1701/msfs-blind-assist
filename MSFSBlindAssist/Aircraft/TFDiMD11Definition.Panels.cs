using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panel layout + the documented export/read-out surface.
/// </summary>
public partial class TFDiMD11Definition
{
    // =================================================================================
    // Export variables — TFDi's documented integration surface
    // =================================================================================

    /// <summary>
    /// Read-outs that matter enough to announce on change, with their spoken wording and
    /// decoding. Everything else in <c>export_vars</c> is registered as a silent OnRequest cache
    /// (the hotkeys read it) rather than narrated.
    ///
    /// These carry disproportionate weight on this aircraft: the DUs are WASM-rendered and
    /// unreadable, so for a blind pilot these L:vars ARE the instruments. V-speeds in particular
    /// have no other source — there is no speed tape to read.
    /// </summary>
    private static Dictionary<string, SimVarDefinition> BuildExportVariables()
    {
        var v = new Dictionary<string, SimVarDefinition>();

        // ---- FCP (Flight Control Panel) selected values -----------------------------
        // -999 / -9999 are TFDi's "readout is dashed" sentinels, per the Variables doc.
        v["MD11_AFS_SPD"] = Export("MD11_AFS_SPD", "Selected speed");
        v["MD11_AFS_HDG"] = Export("MD11_AFS_HDG", "Selected heading");
        v["MD11_AFS_ALT"] = Export("MD11_AFS_ALT", "Selected altitude");
        v["MD11_AFS_VS"] = Export("MD11_AFS_VS", "Selected vertical speed");

        // ---- Autoflight state --------------------------------------------------------
        v["MD11_AP_STATE"] = Announced("MD11_AP_STATE", "Autopilot", new()
        {
            [0] = "off", [1] = "AP 1", [2] = "AP 2", [3] = "AP 1 and 2",
        });
        v["MD11_ATS_STATE"] = Export("MD11_ATS_STATE", "Autothrottle state");
        v["MD11_ATS_CLAMP"] = Export("MD11_ATS_CLAMP", "Autothrottle clamp");

        // Unit/mode toggles — these decide how the FCP windows above are SPOKEN, so they are
        // cached but never narrated in their own right (a bare "1" means nothing aloud).
        v["MD11_AP_IAS_MACH"] = Export("MD11_AP_IAS_MACH", "Speed unit");
        v["MD11_AP_HDG_TRK"] = Export("MD11_AP_HDG_TRK", "Heading or track");
        v["MD11_AP_VS_FPA"] = Export("MD11_AP_VS_FPA", "Vertical mode");
        v["MD11_AP_FT_M"] = Export("MD11_AP_FT_M", "Altitude unit");

        // ---- V-speeds ----------------------------------------------------------------
        // No speed tape to read them off; these are the only source.
        v["MD11_V1"] = Export("MD11_V1", "V1");
        v["MD11_VR"] = Export("MD11_VR", "Rotate speed");
        v["MD11_V2"] = Export("MD11_V2", "V2");
        v["MD11_VSR"] = Export("MD11_VSR", "Slat retraction speed");
        v["MD11_VFR"] = Export("MD11_VFR", "Flap retraction speed");

        // ---- Minimums / altimeters ---------------------------------------------------
        v["MD11_CAP_MINIMUMS"] = Export("MD11_CAP_MINIMUMS", "Captain minimums");
        v["MD11_FO_MINIMUMS"] = Export("MD11_FO_MINIMUMS", "First officer minimums");
        v["MD11_CAP_ALTIMETER"] = Export("MD11_CAP_ALTIMETER", "Captain altimeter");
        v["MD11_FO_ALTIMETER"] = Export("MD11_FO_ALTIMETER", "First officer altimeter");
        v["MD11_STBY_ALTIMETER"] = Export("MD11_STBY_ALTIMETER", "Standby altimeter");

        // ---- APU ---------------------------------------------------------------------
        v["MD11_APU_STATE"] = Announced("MD11_APU_STATE", "APU", new()
        {
            [0] = "off", [1] = "starting", [2] = "running", [3] = "stopping",
        });
        v["MD11_APU_N1"] = Export("MD11_APU_N1", "APU N1");
        v["MD11_APU_N2"] = Export("MD11_APU_N2", "APU N2");

        // ---- Main engines ------------------------------------------------------------
        // UNDOCUMENTED, and not in the control map's export list — TFDi's Variables page lists the
        // APU's N1 but not the engines'. They are real all the same: found as registered L:vars in
        // md11host.wasm's DWARF, then CONFIRMED on a live aircraft (2026-07-17) reading 25.396,
        // 25.396 and 25.396 with per-engine variation — real data, not a constant or a miss (a
        // nonexistent L:var reads a flat 0, which is what the same probe returned for an invented
        // name and for MD11_ENG1_N2/EGT/FF — so those three do NOT exist; do not add them back).
        //
        // Worth having precisely because the EAD is WASM-rendered and unreadable: this is the only
        // way a blind pilot gets engine N1 on this aircraft. Silent, like the other numeric
        // read-outs — N1 narrated on every change through a whole take-off would be unusable.
        v["MD11_ENG1_N1"] = Export("MD11_ENG1_N1", "Engine 1 N1");
        v["MD11_ENG2_N1"] = Export("MD11_ENG2_N1", "Engine 2 N1");
        v["MD11_ENG3_N1"] = Export("MD11_ENG3_N1", "Engine 3 N1");

        // ---- Fuel --------------------------------------------------------------------
        v["MD11_OVHD_TANK_1_VAL"] = Export("MD11_OVHD_TANK_1_VAL", "Tank 1");
        v["MD11_OVHD_TANK_2_VAL"] = Export("MD11_OVHD_TANK_2_VAL", "Tank 2");
        v["MD11_OVHD_TANK_3_VAL"] = Export("MD11_OVHD_TANK_3_VAL", "Tank 3");
        v["MD11_OVHD_TANK_AUX_VAL"] = Export("MD11_OVHD_TANK_AUX_VAL", "Auxiliary tank");
        v["MD11_OVHD_TANK_TAIL_VAL"] = Export("MD11_OVHD_TANK_TAIL_VAL", "Tail tank");

        // ---- Flap system -------------------------------------------------------------
        // FLAPS_MOVING is announced: on an aircraft whose flap gauge cannot be read, "flaps
        // moving" → "flaps set" is the only confirmation a selection actually took effect.
        v[Md11FlapSystem.FlapsMovingVar] = Announced(Md11FlapSystem.FlapsMovingVar, "Flaps", new()
        {
            [0] = "set", [1] = "moving",
        });

        return v;
    }

    /// <summary>A silent cached read-out: continuously updated, never narrated on its own.</summary>
    private static SimVarDefinition Export(string name, string display) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        // Consumed by ProcessSimVarUpdate / the hotkey read-outs rather than spoken per change —
        // a raw stream of "Selected heading: 271" on every knob detent would be unusable. Hidden
        // from Ctrl+M because a checkbox that silences an already-silent var does nothing.
        ExcludeFromMonitorManager = true,
        RenderAsReadOnlyStatus = true,
    };

    /// <summary>A read-out that DOES narrate on change, with decoded wording.</summary>
    private static SimVarDefinition Announced(string name, string display, Dictionary<double, string> values) => new()
    {
        Name = name,
        DisplayName = display,
        Type = SimVarType.LVar,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ValueDescriptions = values,
        RenderAsReadOnlyStatus = true,
    };

    // =================================================================================
    // Panels
    // =================================================================================

    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    /// <summary>
    /// Derives sections and panels from the control map.
    ///
    /// Section = the cockpit area the generator assigned from the node id ("Overhead",
    /// "Pedestal", …). Panel = the SUBSYSTEM token, i.e. the third token of
    /// <c>MD11_&lt;AREA&gt;_&lt;SUBSYSTEM&gt;_…</c>. Both come from TFDi's own naming.
    ///
    /// The subsystem split is not cosmetic — it is what makes the aircraft navigable. The
    /// Overhead alone holds 429 controls; as one flat panel that is unusable with a screen
    /// reader. Split by subsystem it becomes Electrical (74), Pneumatic (74), Fuel (69),
    /// Lights (58), Hydraulics (37), Flight Controls (28)… i.e. the panels a pilot already has
    /// a mental model of.
    ///
    /// Annunciators are excluded: they announce on change instead (see BuildControlVariable).
    /// A panel is for operating controls, not for scanning 532 lamp rows.
    /// </summary>
    private void BuildPanelsOnce()
    {
        if (_panelStructure != null && _panelControls != null) return;

        var structure = new Dictionary<string, List<string>>();
        var controls = new Dictionary<string, List<string>>();

        // area -> subsystem -> keys, preserving the map's (area, node_id) sort order.
        var grouped = new Dictionary<string, Dictionary<string, List<string>>>();

        foreach (var c in _map.Controls)
        {
            if (c.Kind == Md11Kinds.Annunciator) continue;

            var area = string.IsNullOrWhiteSpace(c.Area) ? "Other" : c.Area;
            var sub = SubsystemLabel(c);

            if (!grouped.TryGetValue(area, out var subs))
                grouped[area] = subs = new Dictionary<string, List<string>>();
            if (!subs.TryGetValue(sub, out var keys))
                subs[sub] = keys = new List<string>();
            keys.Add(c.NodeId);
        }

        // Panel names are the app's global key for a panel, so they must be unique across every
        // section — "CPT" legitimately occurs under both Pedestal and the Captain audio panel.
        // Qualify only on an actual collision, so the common case stays a short, spoken-friendly
        // name rather than "Overhead — Electrical" everywhere.
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var subs in grouped.Values)
            foreach (var sub in subs.Keys)
                nameCounts[sub] = nameCounts.GetValueOrDefault(sub) + 1;

        foreach (var (area, subs) in grouped.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            var panelNames = new List<string>();
            foreach (var (sub, keys) in subs.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            {
                var panelName = nameCounts[sub] > 1 ? $"{sub} ({area})" : sub;
                panelNames.Add(panelName);
                controls[panelName] = keys;
            }
            structure[area] = panelNames;
        }

        AddReadoutPanels(structure, controls);

        _panelStructure = structure;
        _panelControls = controls;
    }

    /// <summary>
    /// Read-out panels that exist only on this aircraft, and only because its glass cannot be
    /// read. On any other airframe a pilot gets V-speeds off the PFD speed tape and minimums off
    /// the PFD; here the DUs are rendered inside the WASM with no DOM behind them, so these
    /// exported L:vars are the ONLY source. Surfacing them as read-only panels means they are at
    /// least reachable by keyboard even where no hotkey exists (there is no V1/VR/V2 HotkeyAction
    /// in the shared enum — adding one is a follow-up).
    /// </summary>
    private static void AddReadoutPanels(
        Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls)
    {
        controls["V-Speeds"] = new List<string> { "MD11_V1", "MD11_VR", "MD11_V2", "MD11_VSR", "MD11_VFR" };
        controls["Minimums and Altimeters"] = new List<string>
        {
            "MD11_CAP_MINIMUMS", "MD11_FO_MINIMUMS",
            "MD11_CAP_ALTIMETER", "MD11_FO_ALTIMETER", "MD11_STBY_ALTIMETER",
        };
        controls["Autoflight Status"] = new List<string>
        {
            "MD11_AP_STATE", "MD11_ATS_STATE",
            "MD11_AFS_SPD", "MD11_AFS_HDG", "MD11_AFS_ALT", "MD11_AFS_VS",
        };
        controls["APU Status"] = new List<string> { "MD11_APU_STATE", "MD11_APU_N1", "MD11_APU_N2" };
        controls["Fuel Quantity"] = new List<string>
        {
            "MD11_OVHD_TANK_1_VAL", "MD11_OVHD_TANK_2_VAL", "MD11_OVHD_TANK_3_VAL",
            "MD11_OVHD_TANK_AUX_VAL", "MD11_OVHD_TANK_TAIL_VAL",
        };

        structure["Read-outs"] = new List<string>
        {
            "V-Speeds", "Minimums and Altimeters", "Autoflight Status", "APU Status", "Fuel Quantity",
        };
    }

    /// <summary>
    /// The subsystem token, expanded to something a screen reader should say. TFDi's tokens are
    /// terse ("PNEU", "AICE", "FLTCTL"); a reader spelling those out letter-by-letter is worse
    /// than useless, so the common ones are expanded to the panel names a pilot would use.
    /// Unknown tokens fall through title-cased rather than being dropped.
    /// </summary>
    private static string SubsystemLabel(Md11Control c)
    {
        var parts = c.NodeId.Split('_');
        var token = parts.Length >= 3 ? parts[2] : string.Empty;
        if (string.IsNullOrEmpty(token)) return "General";

        if (SubsystemNames.TryGetValue(token, out var name)) return name;
        // A pure digit or single letter is an MCDU key row etc. — group them together rather
        // than emitting a panel called "7".
        if (token.Length <= 1 || token.All(char.IsDigit)) return "Keys";
        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }

    private static readonly Dictionary<string, string> SubsystemNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ELEC"] = "Electrical",
        ["PNEU"] = "Pneumatic",
        ["FUEL"] = "Fuel",
        ["LTS"] = "Lights",
        ["HYD"] = "Hydraulics",
        ["FLTCTL"] = "Flight Controls",
        ["ENG"] = "Engines",
        ["AICE"] = "Anti-Ice",
        ["GEN"] = "Generators",
        ["WNDSHLD"] = "Windshield",
        ["IRS"] = "IRS",
        ["AIL"] = "Aileron",
        ["GALLEY"] = "Galley",
        ["ANNUNLT"] = "Annunciator Lights",
        ["APU"] = "APU",
        ["GPWS"] = "GPWS",
        ["EVAC"] = "Evacuation",
        ["EMER"] = "Emergency",
        ["CRGSMK"] = "Cargo Smoke",
        ["APUFIRE"] = "APU Fire",
        ["ENG1FIRE"] = "Engine 1 Fire",
        ["ENG2FIRE"] = "Engine 2 Fire",
        ["ENG3FIRE"] = "Engine 3 Fire",
        ["CPT"] = "Captain",
        ["FO"] = "First Officer",
        ["OBS"] = "Observer",
        ["SD"] = "System Display",
        ["XPNDR"] = "Transponder",
        ["WXR"] = "Weather Radar",
        ["CKPTDOOR"] = "Cockpit Door",
        ["AUDIO"] = "Audio",
        ["BAROSET"] = "Barometer",
        ["MINIMUMS"] = "Minimums",
        ["MAGTRU"] = "Magnetic/True",
        ["TCAS"] = "TCAS",
        ["LSK"] = "Line Select Keys",
        ["OXY"] = "Oxygen",
        ["TIMER"] = "Timer",
        ["INP"] = "Input Panel",
        ["DOOR"] = "Doors",
        ["TRIM"] = "Trim",
        ["AP"] = "Autopilot",
        ["HDG"] = "Heading",
        ["ALT"] = "Altitude",
        ["VS"] = "Vertical Speed",
        ["NAV"] = "Navigation",
        ["SLAT"] = "Slats",
        ["ANTISKID"] = "Antiskid",
        ["AUTOBRAKE"] = "Autobrake",
        ["GEAR"] = "Gear",
        ["PARK"] = "Parking Brake",
        ["LONG"] = "Longitudinal Trim",
        ["GA"] = "Go Around",
        ["WHEEL"] = "Dial-A-Flap",
        ["LATCH"] = "Flap Handle",
        ["HANDLE"] = "Speedbrake Handle",
        ["MST"] = "Master",
        ["GS"] = "Ground Service",
        ["ISFD"] = "Standby Display",
        ["FLOOD"] = "Floodlights",
        ["PNL"] = "Panel Lights",
        ["DOME"] = "Dome Light",
        ["MAP"] = "Map Light",
        ["BRT"] = "Brightness",
    };
}
