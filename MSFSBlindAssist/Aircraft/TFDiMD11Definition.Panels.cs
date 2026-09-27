using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

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
        // Announced, not a silent read-out: engagement is news, exactly as MD11_AP_STATE is. 1 was
        // measured live with the autothrottle engaged, 0 off; 2 has never been observed and
        // Md11AutoflightState treats anything ≥ 0.5 as on, so it decodes as on here too.
        v["MD11_ATS_STATE"] = Announced("MD11_ATS_STATE", "Autothrottle", new()
        {
            [0] = "off", [1] = "on", [2] = "on",
        });
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

        // ---- Take-off roll callouts --------------------------------------------------
        // Indicated airspeed fed per SIM_FRAME to TakeoffVSpeedCallouts ("V1" / "Rotate" / "V2"
        // as the FMS speeds are reached — the calls the PMDGs play natively; TFDi plays none).
        // G_FORCE's registration pattern: IsAnnounced to be monitored at all, ExcludeFromBatch +
        // HighFrequency for a per-var SIM_FRAME subscription — the 1 Hz batch would call "Rotate"
        // up to a second (~5 kt) late, useless as an action cue. Never spoken itself and hidden
        // from Ctrl+M; the callouts are muted through the V1 / Rotate speed / V2 rows instead,
        // which is why those three exports keep a Ctrl+M row (ExcludeFromMonitorManager means
        // "muted by plumbing" and must never sit on a var whose row silences something).
        v[Md11TakeoffCallouts.IasKey] = new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED",
            DisplayName = "Indicated airspeed",
            Type = SimVarType.SimVar,
            Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ExcludeFromBatch = true,
            HighFrequency = true,
            ExcludeFromMonitorManager = true,
        };
        // All five take-off speeds SPEAK — as the FMS sets them (Md11VSpeedAnnouncer, "V1 145
        // knots") and, for V1 / VR / V2, as the roll reaches them — so all five keep a Ctrl+M row.
        foreach (var row in Md11VSpeeds.Keys)
            v[row].ExcludeFromMonitorManager = false;

        // ---- Minimums / altimeters ---------------------------------------------------
        v["MD11_CAP_MINIMUMS"] = Export("MD11_CAP_MINIMUMS", "Captain minimums");
        v["MD11_FO_MINIMUMS"] = Export("MD11_FO_MINIMUMS", "First officer minimums");
        v["MD11_CAP_ALTIMETER"] = Export("MD11_CAP_ALTIMETER", "Captain altimeter");
        // Unlike the other Export() rows this one SPEAKS — once the setting settles, through
        // Md11AltimeterAnnouncer in ProcessSimVarUpdate — so it keeps its Ctrl+M row: the flag
        // means "muted by plumbing" and must never sit on a var that speaks.
        v["MD11_CAP_ALTIMETER"].ExcludeFromMonitorManager = false;
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
        // NOT silent, unlike every other Export() row: the three N1s drive Md11N1Cue's
        // once-per-roll "N1 70 percent" take-off cue, which MainForm's Ctrl+M wrap mutes exactly like
        // the flap read-out — so they keep their Ctrl+M rows. ExcludeFromMonitorManager means
        // "muted by plumbing" and must never sit on a var that speaks (the MD-11 monitor form
        // honours the flag; a flagged N1 would have made the cue unmutable).
        foreach (var n1 in new[] { "MD11_ENG1_N1", "MD11_ENG2_N1", "MD11_ENG3_N1" })
            v[n1].ExcludeFromMonitorManager = false;

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

        // ---- COM radios --------------------------------------------------------------
        // TFDi's radio panel drives the simulator's own COM radios (COM1 read 135.500 live,
        // 2026-09-06), so the stock variables ARE the frequency on this aircraft. Announced on
        // change from ProcessSimVarUpdate ("COM 1 active 135.500"), standby included so a tuner
        // click and an XFER both read back; the Ctrl+M rows come with IsAnnounced.
        foreach (var key in Md11Radios.Keys)
            v[key] = ComRadio(key);

        // Tuning goes through the sim's own events, which the aircraft honours (probed live
        // 2026-09-06: COM3_STBY_RADIO_SET_HZ held, COM3_RADIO_SWAP swapped). A typed standby
        // (the "_SET" text box) and a Transfer button per radio; HandleUIVariableSet claims both,
        // and the COM announcer speaks the outcome from the variables that then change.
        for (int idx = 1; idx <= 3; idx++)
        {
            v[Md11Radios.StandbySetKey(idx)] = new SimVarDefinition
            {
                Name = Md11Radios.StandbySetEvent(idx),
                DisplayName = $"COM {idx} Standby",
                Type = SimVarType.Event,
                UpdateFrequency = UpdateFrequency.OnRequest,
            };
            v[Md11Radios.SwapKey(idx)] = new SimVarDefinition
            {
                Name = Md11Radios.SwapEvent(idx),
                DisplayName = $"COM {idx} Transfer",
                Type = SimVarType.Event,
                UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsButton = true,
                HelpText = $"Swap COM {idx}'s standby frequency into the active slot",
            };
        }

        // ---- Transponder -------------------------------------------------------------
        // A typed four-digit squawk (text box + Set, the "_SET" convention) replaces the eight
        // keypad buttons on the panel: HandleUIVariableSet presses the aircraft's own digit keys
        // in order and reads the stock TRANSPONDER CODE:1 back to confirm. The Name is the stock
        // XPNDR_SET event only so the registration is a real event; the MD-11 handler always
        // claims the key first, so that event is never sent on this aircraft.
        v[Md11Squawk.SetKey] = new SimVarDefinition
        {
            Name = "XPNDR_SET",
            DisplayName = "Squawk",
            Type = SimVarType.Event,
            UpdateFrequency = UpdateFrequency.OnRequest,
        };
        // Continuous + IsAnnounced so the code speaks on CHANGE whichever way it was set — the
        // A380's XPNDR_CODE / the PMDGs' TRANSPONDER_CODE_SET shape; it was OnRequest (a panel
        // row only) and so never announced an ATC assignment or a hardware transponder. Decoded
        // and spoken by ProcessSimVarUpdate through Md11SquawkAnnouncer; the row stays the panel's
        // read-back, and "Squawk code" is the Ctrl+M row that mutes the call-out.
        v[Md11Squawk.CodeKey] = new SimVarDefinition
        {
            Name = "TRANSPONDER CODE:1",
            DisplayName = "Squawk code",
            Type = SimVarType.SimVar,
            Units = "BCO16",
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
        };

        // ---- Minimums (typed) ---------------------------------------------------------
        // The BARO minimums take a typed value through MD11_EXTCTL_{CAP,FO}_MIN (probed live
        // 2026-09-06: consumed in either mode, applied to the baro minimums only, inbox idles at
        // -9999 — see Md11Minimums). One "_SET" field per side; HandleUIVariableSet claims both,
        // so the LVar/Never registration costs no data definition and is never written generically.
        foreach (var side in Md11Minimums.Sides)
        {
            v[side.SetKey] = new SimVarDefinition
            {
                Name = side.WriteVar,
                DisplayName = $"{side.Name} Minimums",
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                CurrentValueSourceKey = side.ReadKey,   // the box pre-fills with what the display shows
                HelpText = $"Type the {side.Name.ToLowerInvariant()} baro minimums in feet and press Set",
            };
        }

        // A silent, batch-covered MIRROR of each side's Radio/Baro mode switch, read by the minimums
        // rows ("200 feet, radio") and by the typed entry's read-back. The switch's own key stays
        // OnRequest: it is a walkable combo, and a batch-covered var has no individual definition,
        // which downgrades the walker to the legacy cache-poll protocol that can call a real move
        // "did not move". Same shape as MD11_CAP_MINIMUMS beside the minimums cap's read-only row —
        // two keys, one Name, only one of them batched. No ValueDescriptions, so ProcessSimVarUpdate
        // consumes it silently with the other Export-style read-outs; never a panel row.
        foreach (var side in Md11Minimums.Sides)
        {
            v[side.ModeKey] = new SimVarDefinition
            {
                Name = side.ModeSwitch,
                DisplayName = $"{side.Name} minimums mode (mirror)",   // never spoken; distinct from the switch's own label in logs
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,                  // batch-covered; consumed silently (no ValueDescriptions)
                ExcludeFromMonitorManager = true,    // a checkbox here would silence nothing
                RenderAsReadOnlyStatus = true,
            };
        }

        // ---- Speedbrake --------------------------------------------------------------
        // The lever's PULL (0 down, 1 = ground spoilers armed, 2 = auto-extended on landing) is
        // a row of this app's own; the Spoilers row is the map control, re-pointed at the
        // travel var in BuildControlVariable. A combo, so the pilot can arm and disarm; the
        // click that toggles the pull lives on the lever control (HandleUIVariableSet).
        v[Md11SpeedbrakeSystem.ArmKey] = new SimVarDefinition
        {
            Name = Md11SpeedbrakeSystem.ArmVar,
            DisplayName = "Ground spoilers",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = Md11SpeedbrakeSystem.ArmValues,
        };

        // ---- Altimeter STD (Md11StdToggles) ----
        // Two rows of this app's own, each pressing its EFIS altimeter knob's PUSH — the aircraft's
        // STD toggle. The knobs' own rows stay READ-ONLY (their state var is the altimeter export,
        // Md11ExportBacked), so these are separate rows like the COM rows, and a press goes out as
        // the knob's CEVENT pair, never a write. LVar + Never: SetControl claims both keys, so
        // nothing reads them and nothing writes them generically.
        foreach (var (key, label) in new[]
                 {
                     (Md11StdToggles.CaptainKey, Md11StdToggles.CaptainLabel),
                     (Md11StdToggles.FirstOfficerKey, Md11StdToggles.FirstOfficerLabel),
                 })
        {
            v[key] = new SimVarDefinition
            {
                Name = key,
                DisplayName = label,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Never,
                RenderAsButton = true,
            };
        }

        return v;
    }

    /// <summary>One stock COM frequency, in kHz, batch-covered and announced on change.</summary>
    private static SimVarDefinition ComRadio(string key) => new()
    {
        Name = Md11Radios.SimVarName(key),
        DisplayName = Md11Radios.DisplayName(key),
        Type = SimVarType.SimVar,
        Units = "kHz",
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        RenderAsReadOnlyStatus = true,
    };

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
    private Dictionary<string, List<string>>? _panelDisplays;

    /// <summary>
    /// Each panel's Status Display rows (MainForm's read-only list, Ctrl+3): the panel's lamps in
    /// table order, the read-out panels' exported numbers, the COM read-backs, the squawk code.
    /// One list per panel instead of one tab stop per light — the Airbus pattern, and on this
    /// aircraft those lights ARE the instrument panel, so the list is where a pilot scans them.
    /// Row text comes from <see cref="Md11StatusRow"/> through TryGetDisplayOverride.
    /// </summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        BuildPanelsOnce();
        return _panelDisplays!;
    }

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
    /// Derives sections and panels from <see cref="Md11PanelLayout"/> — the curated table is the
    /// ONLY source of panel order (spec §3.8): sections in the order a preparation flows, panels
    /// named as TFDi's Systems Guide names them, controls in physical panel order, a guard cover
    /// immediately before the control it covers, standalone lamps as the panel's Status Display rows.
    ///
    /// A control the table does not name is still appended (never dropped) rather than silently
    /// missing — see <see cref="Md11PanelLayout.Place"/>'s safety net — and logged loudly so a
    /// regenerated map that adds a control is noticed instead of hidden.
    ///
    /// Annunciators the table names are the panel's Status Display rows (Md11StatusRow decides
    /// their words); one the table does not name only announces on change (see
    /// BuildControlVariable). On this aircraft the lamps ARE the instrument panel, so the Status
    /// Display is where a pilot scans them.
    /// </summary>
    private void BuildPanelsOnce()
    {
        if (_panelStructure != null && _panelControls != null) return;

        var placement = Md11PanelLayout.Place(_map);
        if (placement.MissingKeys.Count > 0)
            Log.Warn("MD11", $"Layout names {placement.MissingKeys.Count} keys the map lacks: {string.Join(", ", placement.MissingKeys.Take(10))}");
        if (placement.Unplaced.Count > 0)
            Log.Warn("MD11", $"{placement.Unplaced.Count} controls are not in the layout table and were appended: {string.Join(", ", placement.Unplaced.Take(10))}");

        var displays = placement.Displays;
        AddReadoutPanels(placement.Structure, placement.Controls, displays);
        AddSquawkEntry(placement.Controls, displays);
        AddMinimumsEntries(placement.Controls);
        AddRadiosPanel(placement.Structure, placement.Controls, displays);
        AddSpeedbrakeArm(placement.Controls);
        AddStdToggles(placement.Controls);

        _panelStructure = placement.Structure;
        _panelControls = placement.Controls;
        _panelDisplays = displays;
    }

    /// <summary>
    /// The transponder panel's typed entry and read-back are not map controls, so the layout
    /// table cannot name them: the field goes right after the panel's four selectors (where
    /// the digit keys used to start), and the read-back is the FIRST row of the panel's Status
    /// Display, ahead of its FAIL lamp.
    /// </summary>
    private static void AddSquawkEntry(Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        if (!controls.TryGetValue("Transponder", out var keys)) return;
        int at = keys.IndexOf("MD11_PED_XPNDR_ABV_BLW_SW");
        keys.Insert(at < 0 ? 0 : at + 1, Md11Squawk.SetKey);
        if (!displays.TryGetValue("Transponder", out var rows)) displays["Transponder"] = rows = new List<string>();
        rows.Insert(0, Md11Squawk.CodeKey);
    }

    /// <summary>
    /// The typed minimums field is not a map control, so the layout table cannot name it: it goes
    /// right after the side's own minimums knob on its EFIS panel (see Md11Minimums).
    /// </summary>
    private static void AddMinimumsEntries(Dictionary<string, List<string>> controls)
    {
        foreach (var side in Md11Minimums.Sides)
        {
            if (!controls.TryGetValue(side.PanelName, out var keys)) continue;
            int at = keys.IndexOf(side.AnchorKey);
            keys.Insert(at < 0 ? keys.Count : at + 1, side.SetKey);
        }
    }

    /// <summary>The Ground spoilers row sits right after the lever it belongs to.</summary>
    private static void AddSpeedbrakeArm(Dictionary<string, List<string>> controls)
    {
        if (!controls.TryGetValue("Speedbrake", out var keys)) return;
        int at = keys.IndexOf(Md11SpeedbrakeSystem.LeverKey);
        keys.Insert(at < 0 ? keys.Count : at + 1, Md11SpeedbrakeSystem.ArmKey);
    }

    /// <summary>
    /// Each MSFSBA STD row sits right after its side's altimeter setting row — the knob whose push it
    /// presses — on its EFIS panel (<see cref="Md11StdToggles"/>). The standby display's STD button is
    /// a map control the layout table already places, so only the rows with a panel of their own are
    /// inserted.
    /// </summary>
    private static void AddStdToggles(Dictionary<string, List<string>> controls)
    {
        foreach (var std in Md11StdToggles.Targets)
        {
            if (std.PanelName == null || !controls.TryGetValue(std.PanelName, out var keys)) continue;
            int at = keys.IndexOf(std.Knob);
            keys.Insert(at < 0 ? keys.Count : at + 1, std.Key);
        }
    }

    /// <summary>
    /// The Radios panel is operable (typed standby, transfer), so it belongs on the Pedestal with
    /// the radio control panels rather than among the read-outs; it opens the section because
    /// tuning is what the pilot goes there for. The six COM rows go at the HEAD of the list the
    /// layout table already placed there — the three crew positions' radio control panels (VHF
    /// 1-3, HF 1-2, the MHz and kHz tuners and the transfer, 24 controls) — and never in place of
    /// it: this once assigned the list outright, and those 24 were on no panel at all.
    /// </summary>
    private static void AddRadiosPanel(Dictionary<string, List<string>> structure,
        Dictionary<string, List<string>> controls, Dictionary<string, List<string>> displays)
    {
        if (!controls.TryGetValue("Radios", out var keys)) controls["Radios"] = keys = new List<string>();
        keys.InsertRange(0, Md11Radios.PanelKeys);
        displays["Radios"] = new List<string>(Md11Radios.Keys);
        if (!structure.TryGetValue("Pedestal", out var panels)) structure["Pedestal"] = panels = new List<string>();
        panels.Remove("Radios");
        panels.Insert(0, "Radios");
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
        Dictionary<string, List<string>> controls,
        Dictionary<string, List<string>> displays)
    {
        displays["V-Speeds"] = new List<string> { "MD11_V1", "MD11_VR", "MD11_V2", "MD11_VSR", "MD11_VFR" };
        displays["Minimums and Altimeters"] = new List<string>
        {
            "MD11_CAP_MINIMUMS", "MD11_FO_MINIMUMS",
            "MD11_CAP_ALTIMETER", "MD11_FO_ALTIMETER", "MD11_STBY_ALTIMETER",
        };
        displays["Autoflight Status"] = new List<string>
        {
            "MD11_AP_STATE", "MD11_ATS_STATE",
            "MD11_AFS_SPD", "MD11_AFS_HDG", "MD11_AFS_ALT", "MD11_AFS_VS",
        };
        // The three exported N1s (undocumented but real — see BuildExportVariables). The hotkey
        // guide promised N1 in the Read-outs; until this panel existed nothing carried it.
        displays["Engines"] = new List<string> { "MD11_ENG1_N1", "MD11_ENG2_N1", "MD11_ENG3_N1" };
        displays["APU Status"] = new List<string> { "MD11_APU_STATE", "MD11_APU_N1", "MD11_APU_N2" };
        displays["Fuel Quantity"] = new List<string>
        {
            "MD11_OVHD_TANK_1_VAL", "MD11_OVHD_TANK_2_VAL", "MD11_OVHD_TANK_3_VAL",
            "MD11_OVHD_TANK_AUX_VAL", "MD11_OVHD_TANK_TAIL_VAL",
        };

        var names = new List<string> { "V-Speeds", "Minimums and Altimeters", "Autoflight Status", "Engines", "APU Status", "Fuel Quantity" };
        foreach (var name in names) controls[name] = new List<string>();   // display-only: MainForm needs the (empty) entry
        structure["Read-outs"] = names;
    }
}
