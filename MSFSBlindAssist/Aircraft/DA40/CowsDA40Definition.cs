using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Aircraft.DA40;

/// <summary>
/// COWS DA40 Series (DA40-NG and DA40-XLS) — MSFSBA's first GA airframe and its first
/// aircraft built on the stock Working Title G1000.
///
/// STUDY-LEVEL, NOT SIMPLIFIED. Every control a sighted pilot can reach is exposed
/// here: all circuit breakers individually, every switch, every lever, the complete
/// failure set. Derived readouts are additions, never substitutions for the raw value.
/// MSFSBA reports; it does not decide, gate, or auto-complete a pilot action.
///
/// TRANSPORTS (all verified live against a powered NG and XLS, 2026-08):
///   • SimVar          — airframe basics: flaps, trim, doors, lights, radios, XPDR, AP.
///   • L-var (MobiFlight) — everything COWS simulates: DISP_*, ELEC_*, STATE_*, CB_*.
///   • K:/H: events    — switches, levers, failure injection, G1000 bezel.
///   • Coherent CDP    — the G1000 DOM: CAS text, FMA, page menu, softkeys, checklist.
///
/// THREE AIRFRAME RULES THAT BREAK THE OBVIOUS DESIGN (all measured, do not "simplify"):
///   1. STATE_* L-vars are READ-ONLY MIRRORS. The model behaviour recomputes them every
///      frame, so a write is silently discarded (STATE_LIGHT_ICE=1 read back 0). Read
///      STATE_* (or the SimVar); WRITE the K: event or the real INPUT_*/CB_* L-var.
///   2. Momentary controls need a REPEATING write (~40 ms), not set-then-clear. A single
///      write is discarded; re-writing ATT_CAGE=1 every 40 ms held it and drove
///      ATT_GYRO_CAGE_SET 0→1. Hold durations: ECU test ~10 s, fuel-selector wire ~3 s.
///   3. The NG fuel valve is INTERLOCKED. Logic.xml forces it back to Normal every frame
///      while the copper break wire is intact, so FUEL_SELECTOR=1 reads back 0. Cut
///      FUEL_SELECTOR_WIRE_CUT first, then set the valve, then verify the readback.
///
/// This file currently defines the PANEL STRUCTURE ONLY. Controls are added panel by
/// panel in later passes; every panel below therefore has an (empty) BuildPanelControls
/// entry, because MainForm returns early for any panel missing from GetPanelControls()
/// and the panel would otherwise render completely blank (the HS787 Flight Data trap,
/// docs/hs787.md).
/// </summary>
public partial class CowsDA40Definition : BaseAircraftDefinition
{
    private readonly DA40Variant _variant;

    public CowsDA40Definition(DA40Variant variant = DA40Variant.NG)
    {
        _variant = variant;
    }

    /// <summary>Which airframe this instance is driving.</summary>
    public DA40Variant Variant => _variant;

    private bool IsNG => _variant == DA40Variant.NG;

    public override string AircraftName =>
        IsNG ? "COWS Diamond DA40-NG" : "COWS Diamond DA40-XLS";

    public override string AircraftCode =>
        IsNG ? "COWS_DA40NG" : "COWS_DA40XLS";

    // ==================================================================================
    // Panel structure — Sections → Panels
    // ==================================================================================

    /// <summary>
    /// Append rows to a panel, creating the entry when it has none yet.
    ///
    /// ⚠️ d[panel].AddRange(...) THROWS when that panel has no list for this variant, and
    /// several do: PowerPanel's display is XLS-only, the failure panels are NG-only. An
    /// exception there takes out GetPanelDisplayVariables entirely, which is how a panel
    /// wiring mistake turns into every panel disappearing at once.
    /// </summary>
    private static void AddRows(Dictionary<string, List<string>> map, string panel,
        List<string> rows)
    {
        if (!map.TryGetValue(panel, out var list))
        {
            list = new List<string>();
            map[panel] = list;
        }
        list.AddRange(rows);
    }

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        var structure = new Dictionary<string, List<string>>
        {
            ["Instrument Panel"] = new List<string>
            {
                "Electrical",
                "Engine Start",
                "Lighting Switches",
                "Ice and Pitot",
                "Standby Instruments",
                "Annunciators",
                "Radios",
                "ELT"
            },
            ["Center Console"] = new List<string>
            {
                "Power and Levers",
                "Fuel System",
                "Flaps",
                "Elevator Trim",
                "Flight Controls",
                "Brakes",
                "Cabin Heat and Vent",
                "Audio"
            },
            // The section already says "Circuit Breakers"; repeating "CB" on every panel
            // just makes each one longer to hear. "Bus and Power" / "Airframe Systems"
            // avoid colliding with the Instrument Panel's "Electrical" and the Center
            // Console's panels — panel names key a FLAT dictionary, so a duplicate would
            // silently collapse the two into one (covered by a test).
            // The six groups the 34 breakers actually fall into. There is no "Copilot"
            // set — the planning sketch had one and the aeroplane does not.
            ["Circuit Breakers"] = new List<string>
            {
                "Engine and Fuel",
                "Flight Instruments",
                "Avionics",
                "Bus and Power",
                "Lighting",
                "Airframe Systems"
            },
            // No "G1000 PFD" or "G1000 MFD" panels. Everything they would have carried is
            // ON the displays and reachable there: the PFD and MFD are clickable and
            // driveable over the Coherent debugger, the CAS window is scrapeable, and the
            // radios and transponder are tuned with the bezel knobs and softkeys — there
            // is no other way to tune them on this aeroplane. So the G1000 gets a display
            // WINDOW opened by a hotkey, the way the A380X E/WD, SD, ND, PFD and ISIS
            // windows already work, and a panel would only be a worse copy of it.
            ["Autopilot"] = new List<string>
            {
                "GFC 700",
                "Flight Director"
            },
            ["Cabin"] = new List<string>
            {
                "Doors and Windows",
                "Seating and Payload"
            },
            // The failure set is built from the aircraft's own L:vars, grouped the way
            // the vendor's Failures.txt groups them. The NG-only panels are filtered out
            // on the XLS by FailurePanels(isNg).
            ["Simulation"] = new List<string>
            {
                "Engine Failures",
                "FADEC and Sensors",
                "Fuel Failures",
                "Electrical Failures",
                "Indication Failures",
                "Flight System Failures",
                "Light Failures",
                "Brake Failures",
                // Thirty breaker POPS, which the aeroplane models and MSFSBA never
                // offered. Separate from the Breakers panel: that is where a pilot pulls
                // one, this is where one trips on its own.
                "Breaker Trips",
                "Engine Damage",
                "Reset",
                // The COWS options. They live on the MFD Engine page's own Page Menu,
                // which no page-walk ever opens, which is how they were missed.
                "Aircraft Options"
            }
        };

        // Variant-specific panels. The NG has a FADEC/ECU and a Main/Auxiliary fuel
        // system with a transfer pump; the XLS has prop and mixture levers, magnetos,
        // priming and lean assist. Neither set is meaningful on the other airframe.
        if (IsNG)
        {
            structure["Instrument Panel"].Insert(2, "ECU");

            // There is deliberately NO separate "Fuel Transfer" panel. The planning pass
            // sketched one, and building the Fuel System panel folded the transfer pump
            // into it where it belongs — the transfer switch, the fuel valve and the
            // pumps are one system and splitting them would make the pilot hunt. The
            // sketch was left in the structure with nothing behind it and rendered as an
            // empty panel, which is indistinguishable from a broken one. Pinned by
            // EveryPanelInTheStructureIsBuiltOrKnownUnbuilt.
        }
        else
        {
            structure["Center Console"].Insert(1, "Mixture and Propeller");
            structure["Instrument Panel"].Insert(2, "Magnetos");
            structure["Center Console"].Insert(3, "Priming");

            // ⚠️ ONLY THE FADEC PANEL GOES. The other three used to be removed with it and
            // that was wrong: a Lycoming has no ECU, but it HAS engine failures, fuel
            // failures and engine damage - more of them than the Austro, including
            // per-cylinder and per-PLUG magneto failures and three levers that can each come
            // adrift. Removing the panels made every one of those unreachable.
            structure["Simulation"].Remove("FADEC and Sensors");

            // ⚠️ THE ENGINE'S OWN BUILD, and the one page that can explain an aeroplane
            // that will not start with everything set correctly: POH p.5's performance
            // variation. An all-zero set is zero fuel pressure, structurally, with no CAS
            // message and no failure flag - so the numbers have to be scannable.
            structure["Simulation"].Insert(
                structure["Simulation"].IndexOf("Engine Damage") + 1, VariationPanel);

            // No "Lean Assist" panel. It is an MFD PAGE, reached with the softkeys, so it
            // belongs to the G1000 display window like every other page.
        }

        return structure;
    }

    // ==================================================================================
    // Panel controls
    //
    // Populated panel by panel in later passes. EVERY panel named in GetPanelStructure
    // MUST appear here even while empty: MainForm's panel build returns early for a
    // panel absent from GetPanelControls(), and the panel then renders nothing at all.
    // ==================================================================================

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        var controls = new Dictionary<string, List<string>>();

        foreach (var panels in GetPanelStructure().Values)
        {
            foreach (var panel in panels)
            {
                controls[panel] = new List<string>();
            }
        }

        // Populated panels override their empty placeholder.
        controls[ElectricalPanel] = new List<string>(ElectricalControls);
        controls[LightingPanel] = new List<string>(LightingControls);
        controls[IcePitotPanel] = new List<string>(IcePitotControls);
        controls[StandbyPanel] = new List<string>(StandbyControls);
        if (IsNG) controls[PowerPanel] = new List<string>(PowerControls);
        if (IsNG) controls[FuelPanel] = new List<string>(FuelControls);
        controls[FlapsPanel] = new List<string>(FlapsControls);
        controls[TrimPanel] = new List<string>(TrimControls);
        controls[BrakesPanel] = new List<string>(BrakeControls);
        controls[AudioPanel] = new List<string>(AudioControls);

        controls[DoorsPanel] = new List<string>(DoorControls);
        controls[EltPanel] = new List<string>(EltControls);
        controls[CabinAirPanel] = new List<string>(CabinAirControls);
        controls[RadiosPanel] = new List<string>(RadioControls);
        controls[AutopilotPanel] = new List<string>(AutopilotControls);
        controls[FlightDirectorPanel] = new List<string>(FlightDirectorControls);
        controls[PayloadPanel] = new List<string>(PayloadControls);

        foreach (var kv in OptionPanels())
        {
            controls[kv.Key] = kv.Value;
        }

        foreach (var kv in FailurePanels(IsNG))
        {
            controls[kv.Key] = kv.Value;
        }

        foreach (var kv in BreakerPanelsFor())
        {
            controls[kv.Key] = new List<string>(kv.Value);
        }
        if (IsNG) controls[EngineStartPanel] = new List<string>(EngineStartControls);
        if (IsNG) controls[EcuPanel] = new List<string>(EcuControls);
        if (!IsNG) controls[MagnetosPanel] = new List<string>(MagnetoControls);
        if (!IsNG) controls[PowerPanel] = new List<string>(XlsPowerControls);
        if (!IsNG) controls[PrimingPanel] = new List<string>(PrimingControls);
        if (!IsNG) controls[FuelPanel] = new List<string>(XlsFuelControls);
        if (!IsNG) controls[EngineStartPanel] = new List<string>(XlsStartControls);
        if (!IsNG) controls[MixturePanel] = new List<string>(XlsMixtureControls);

        RemoveNgOnlyRows(controls);

        return controls;
    }

    protected override Dictionary<string, SimConnect.SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();

        foreach (var kv in BuildElectricalVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildLightingVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildIcePitotVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildStandbyVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildAnnunciatorVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildSharedReadoutVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        // Nine readouts found by diffing FS Copilot's own definition for this airframe
        // against everything above. See CowsDA40Definition.FsCopilotFinds.cs.
        foreach (var kv in BuildFsCopilotFindVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildFsCopilotSecondPassVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildFsCopilotThirdPassVariables())
        {
            vars[kv.Key] = kv.Value;
        }


        foreach (var kv in BuildFlapsVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildAutopilotVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildFlightControlVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildTrimVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildBrakeVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildAudioVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        if (!IsNG)
        {
            foreach (var kv in BuildXlsFailureVariables()) vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildBreakerVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildDoorVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildEltVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildCabinAirVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildRadioVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildPayloadVariables())
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildOptionVariables(IsNG))
        {
            vars[kv.Key] = kv.Value;
        }

        foreach (var kv in BuildFailureVariables(IsNG))
        {
            vars[kv.Key] = kv.Value;
        }

        if (IsNG)
        {
            foreach (var kv in BuildPowerVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildFuelVariables())
            {
                vars[kv.Key] = kv.Value;
            }
        }

        if (IsNG)
        {
            foreach (var kv in BuildEngineStartVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildEcuVariables())
            {
                vars[kv.Key] = kv.Value;
            }
        }

        // The XLS's Lycoming: built one panel at a time against the live aircraft, and only
        // as far as each has been measured (docs/da40-xls-variables.md).
        if (!IsNG)
        {
            foreach (var kv in BuildMagnetoVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildXlsPowerVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildPrimingVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildXlsFuelVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildXlsStartVariables())
            {
                vars[kv.Key] = kv.Value;
            }

            foreach (var kv in BuildXlsMixtureVariables())
            {
                vars[kv.Key] = kv.Value;
            }
        }

        PromoteHotkeyReadouts(vars);

        // The four light CIRCUITS. They exist only so a switched-on light that is not lit
        // can be told from one that is - see CowsDA40Definition.LampWatch.
        AddLampCircuits(vars);
        AddWaypointMonitorRow(vars);
        AddVnavReadouts(vars);

        // ⚠️ HOW DAMAGED THE ENGINE IS, which nothing could say. The aeroplane accumulates
        // damage and it survives a reload; MSFSBA had the switch that enables the model and
        // the button that resets it, and nothing about the state in between. NG only - the
        // damage model is the Austro's.
        if (IsNG) AddEngineHealth(vars);

        // The XLS engine detail FS Copilot's own definition lists and this one never read:
        // the engine's per-airframe variation, the priming charge line by line, per-plug
        // fouling power, the oil cooler, and the damage the XLS spells without an index.
        // The POH's own performance tables need the conditions they are entered with, and
        // one row per airframe to report them on.
        foreach (var kv in BuildPerfTableVariables(IsNG)) vars[kv.Key] = kv.Value;

        if (!IsNG)
        {
            foreach (var kv in BuildXlsVariationVariables()) vars[kv.Key] = kv.Value;
            foreach (var kv in BuildXlsPrimingDetailVariables()) vars[kv.Key] = kv.Value;
            foreach (var kv in BuildXlsEngineDetailVariables()) vars[kv.Key] = kv.Value;
        }

        // Last, after every shared builder has run: the NG's Austro variables do not exist on
        // the XLS. See CowsDA40Definition.VariantScope.
        RemoveNgOnlyVariables(vars);

        return vars;
    }

    /// <summary>
    /// The readouts a HOTKEY answers from, promoted into the continuous batch.
    ///
    /// ⚠️ A HOTKEY CAN ONLY READ WHAT IS IN THE CACHE. Batch membership is Continuous AND
    /// IsAnnounced AND not ExcludeFromBatch; an OnRequest variable is never polled, so
    /// GetCachedVariableValue returns null and the key answers "not available yet" for
    /// ever. That is exactly what Alt+S and Shift+F did on their first flight: every
    /// variable behind them was OnRequest, so the engine-at-a-glance key said nothing was
    /// available while the engine was running, and the fuel key reported a flow of zero.
    ///
    /// They are IsAnnounced only to earn the batch place, so every one is also in
    /// <see cref="SilentCachedReadouts"/> and excluded from the Monitor Manager - a row a
    /// pilot can un-tick that was never going to speak is a row that lies.
    ///
    /// ⚠️ NEVER PROMOTE TWO KEYS THAT SHARE ONE SimVar NAME. The continuous batch sorts by
    /// name, so a duplicate shifts every later variable's struct slot and quietly corrupts
    /// the whole read. Four of these have OnRequest twins on the same SimVar
    /// (DA40_START_LOAD, DA40_START_RPM, DA40_ECU_PROP_SENSED, DA40_ECU_PRE_GEARBOX,
    /// DA40_FUEL_FLOW) and those twins must STAY OnRequest. Two more - standby airspeed and
    /// standby altitude - are deliberately absent from this list because their twins
    /// (DA40_AIRSPEED, INDICATED_ALTITUDE) are already batched, and the hotkey reads those.
    /// </summary>
    private static readonly string[] HotkeyCachedReadouts =
    {
        // Alt+S, the engine at a glance.
        "DA40_POWER_LOAD",
        "DA40_POWER_RPM",
        "DA40_START_OIL_PRESSURE",
        "DA40_START_OIL_TEMP",
        "DA40_START_COOLANT_TEMP",
        "DA40_START_GEARBOX_TEMP",
        "DA40_POWER_FUEL_FLOW",
        "DA40_ELEC_BUS_MAIN_VOLT",
        "DA40_ELEC_DISP_AMPS",

        // The XLS's answers to the same keys. Only the ones a variant defines are promoted,
        // so listing both sets here is what lets one hotkey serve two engines.
        "DA40_XLS_RPM",
        "DA40_XLS_MAP",
        "DA40_XLS_FUEL_FLOW",
        "DA40_XLS_OIL_PRESSURE",
        "DA40_XLS_OIL_TEMP",

        // Alt+I, the standby instruments.
        "DA40_STBY_COMPASS",
        "DA40_STBY_GYRO_PITCH",
        "DA40_STBY_GYRO_BANK",

        // P and E, the single-value engine keys. DA40_POWER_LEVER_SET is already silenced
        // in SilentCachedReadouts for the panels; naming it here as well is harmless (the
        // two lists are unioned) and keeps this list a complete record of what the hotkeys
        // depend on.
        "DA40_POWER_TARGET_RPM",
        "DA40_POWER_LEVER_SET",

        // The Hobbs meter, which a pilot writes down before and after every flight.
        "DA40_HOBBS"
    };

    /// <summary>
    /// Cached for the same reason, and then NOT silenced.
    ///
    /// These two are STATES, not moving numbers. A standby gyro that has been caged, or has
    /// toppled, is showing something that is not the aeroplane's attitude - it interrupts a
    /// sighted pilot the moment they look at it, so by this aircraft's own announcement rule
    /// it should interrupt a blind one too. They keep their Monitor Manager row for the same
    /// reason: a row that can actually speak is a row worth offering.
    /// </summary>
    /// <summary>
    /// States that INTERRUPT a sighted pilot, and were sitting in a panel scan.
    ///
    /// This aeroplane's rule is that switches announce and numbers do not, and it is a good
    /// rule — but it left a class of DESCRIBED STATES silent that no sighted pilot would
    /// have to go looking for. Each of these was found by listing every variable with value
    /// descriptions that never announces and asking, one at a time, whether a pilot in the
    /// left seat would notice it without trying:
    ///
    ///   the engine stopping                    — the emergency, and nothing else said it
    ///   an ECU fault LATCHING                  — the difference between "try the voter
    ///                                            switch" and "land as soon as practical",
    ///                                            and the CAS only ever says ECU FAIL
    ///   the ECU test's own RESULT              — the whole reason for running it
    ///   the transfer pump stopping itself      — documented behaviour above ~14 gallons,
    ///                                            and the aeroplane's Tips page tells the
    ///                                            pilot to set a timer because of it
    ///
    /// ⚠️ A TRIM RUNAWAY IS DELIBERATELY ABSENT and is already covered. DA40_TRIM_RUNAWAY
    /// shares its SimVar with DA40_FAIL_AFCS_TRIM_RUN, which is already batched and
    /// announced — so it speaks, and promoting the second copy would put two keys with one
    /// SimVar name into a batch that sorts by name and corrupt every later variable's slot.
    /// That is the trap NoTwoBatchedVariablesShareOneSimVarName exists for, sprung and
    /// caught while writing this list.
    /// </summary>
    private static readonly string[] AlarmStates =
    {
        "DA40_START_COMBUSTION",
        "DA40_ECU_LATCH_A",
        "DA40_ECU_LATCH_B",
        "DA40_ECU_TEST_FAIL_A",
        "DA40_ECU_TEST_FAIL_B",
        "DA40_FUEL_TRANSFER_RUNNING"
    };

    public static IReadOnlyCollection<string> AlarmStateKeys => AlarmStates;

    private static readonly string[] HotkeyCachedFlags =
    {
        "DA40_STBY_GYRO_CAGED",
        "DA40_STBY_GYRO_TOPPLE"
    };

    public static IReadOnlyCollection<string> HotkeyCachedReadoutKeys => HotkeyCachedReadouts;

    private static void PromoteHotkeyReadouts(Dictionary<string, SimConnect.SimVarDefinition> vars)
    {
        foreach (string key in HotkeyCachedReadouts)
        {
            if (!vars.TryGetValue(key, out var def)) continue;

            def.UpdateFrequency = SimConnect.UpdateFrequency.Continuous;
            def.IsAnnounced = true;
            def.ExcludeFromBatch = false;
            def.ExcludeFromMonitorManager = true;
        }

        foreach (string key in HotkeyCachedFlags.Concat(AlarmStates))
        {
            if (!vars.TryGetValue(key, out var def)) continue;

            def.UpdateFrequency = SimConnect.UpdateFrequency.Continuous;
            def.IsAnnounced = true;
            def.ExcludeFromBatch = false;
        }


    }

    /// <summary>
    /// Status-display contents, reached with Ctrl+3 and refreshed in place with F5.
    /// These are the PRIMARY readout for this aircraft: a sighted pilot flies the DA40
    /// by scanning gauges when they choose to look, so MSFSBA reproduces that scan
    /// rather than narrating values at the pilot. Announcements stay reserved for what
    /// interrupts a sighted pilot too (CAS messages, a breaker popping).
    /// </summary>
    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        var d = new Dictionary<string, List<string>>
        {
            [ElectricalPanel] = new List<string>(ElectricalDisplay),
            [LightingPanel] = new List<string>(LightingDisplay),
            [IcePitotPanel] = new List<string>(IcePitotDisplay),
            [StandbyPanel] = new List<string>(StandbyDisplay),
            [AnnunciatorsPanel] = new List<string>(AnnunciatorsDisplay)
        };

        if (IsNG) d[PowerPanel] = new List<string>(PowerDisplay);
        if (IsNG) d[FuelPanel] = new List<string>(FuelDisplay);
        d[FlapsPanel] = new List<string>(FlapsDisplay);
        d[TrimPanel] = new List<string>(TrimDisplay);
        d[FlightControlsPanel] = new List<string>(FlightControlDisplay);
        // The GFC 700 gets a display entry now, and only now: for a long time every item on
        // that panel was a CONTROL, so there was no read-only state to sweep and an empty
        // Status Display field reads as broken rather than as absent. The autopilot's own
        // health - its self test, its pre-flight test and whether it has failed - is the
        // first thing here that is genuinely read-only.
        d[AutopilotPanel] = new List<string>(AutopilotDisplayRows);
        d[FlightDirectorPanel] = new List<string>(FlightDirectorDisplay);
        d[BrakesPanel] = new List<string>(BrakeDisplay);
        d[AudioPanel] = new List<string>(AudioDisplay);

        d[CbEngineFuelPanel] = new List<string>(CbEngineFuelDisplay);
        d[CbFlightInstrumentsPanel] = new List<string>(CbFlightInstrumentsDisplay);
        d[CbAvionicsPanel] = new List<string>(CbAvionicsDisplay);
        d[CbBusPowerPanel] = new List<string>(CbBusPowerDisplay);
        d[CbLightingPanel] = new List<string>(CbLightingDisplay);
        d[CbAirframeSystemsPanel] = new List<string>(CbAirframeSystemsDisplay);
        d[DoorsPanel] = new List<string>(DoorDisplay);
        d[CabinAirPanel] = CabinAirDisplayFor();
        d[RadiosPanel] = new List<string>(RadioDisplay);
        d[PayloadPanel] = new List<string>(PayloadDisplay);

        if (IsNG) d[EngineStartPanel] = new List<string>(EngineStartDisplay);
        if (IsNG) d[EcuPanel] = new List<string>(EcuDisplay);
        if (!IsNG) d[MagnetosPanel] = new List<string>(MagnetoDisplay);
        if (!IsNG) d[PowerPanel] = new List<string>(XlsPowerDisplay);
        if (!IsNG) d[PrimingPanel] = new List<string>(PrimingDisplay);
        if (!IsNG) d[FuelPanel] = new List<string>(XlsFuelDisplay);

        // ⚠️ The nine FS Copilot finds, appended AFTER every panel exists so the source
        // lists above stay readable as the aircraft's own inventory.
        //
        // ⚠️ NG ONLY for the fuel and pitot rows. The totaliser, the two fuel temperatures
        // and PITOT_TEMP were read live on the NG; the XLS is a different engine with a
        // different fuel system and its own YAML, and claiming a reading exists on an
        // airframe it was never measured on is the failure mode this whole exercise exists
        // to avoid. The XLS gets its own pass against COWS_DA40XLS.yaml.
        if (IsNG)
        {
            AddRows(d, FuelPanel, FsCopilotFuelRows);
            AddRows(d, IcePitotPanel, FsCopilotIcePitotRows);
        }

        // Electrical and the autopilot are shared: same battery model, same GFC 700.
        AddRows(d, ElectricalPanel, FsCopilotElectricalRows);
        AddRows(d, AutopilotPanel, FsCopilotAutopilotRows);

        // ⚠️ THE SECOND AND THIRD PASSES ARE NG-ONLY, ALL OF THEM. Every one was verified
        // against the NG's own package and read on the NG airframe; the XLS is a different
        // engine with its own YAML and gets its own pass. Binding an NG reading on the XLS
        // would put a row on a panel that can only ever sit at zero.
        if (IsNG)
        {
            AddRows(d, PowerPanel, FsCopilotEngineRows);
            AddRows(d, PowerPanel, FsCopilotEngineRows2);
            AddRows(d, EcuPanel, FsCopilotEcuRows);
            AddRows(d, EcuPanel, FsCopilotEcuRows2);
            AddRows(d, EngineStartPanel, FsCopilotStartRows);
            AddRows(d, TrimPanel, FsCopilotTrimRows);
            AddRows(d, IcePitotPanel, FsCopilotIcePitotRows2);
            AddRows(d, SimDamagePanel, FsCopilotDamageRows);
            AddRows(d, SimDamagePanel, FsCopilotDamageRows2);
            AddRows(d, SimFuelPanel, FsCopilotFuelFailureRows);
            AddRows(d, SimEnginePanel, FsCopilotEngineFailureRows);
            AddRows(d, SimEnginePanel, FsCopilotEngineFailureRows2);
        }

        // The autopilot servo forces and the TO/GA read-back are GFC 700, shared.
        AddRows(d, AutopilotPanel, FsCopilotApRows2);

        // The NG's book minimum load, on the panel its own lever lives on. The POH asks
        // for this check before every departure and a blind pilot could not make it.
        if (IsNG) AddRows(d, PowerPanel, new List<string> { "DA40_NG_MIN_LOAD", "DA40_PERF_OAT" });
        // The XLS's own engine-damage readings. Its damage panel has no controls, so these
        // display rows are the whole panel.
        if (!IsNG) AddRows(d, SimDamagePanel, XlsDamageDisplay());
        if (!IsNG) d[EngineStartPanel] = new List<string>(XlsStartDisplay);
        if (!IsNG) d[MixturePanel] = new List<string>(XlsMixtureDisplay);

        // ⚠️ THESE APPEND AND MUST STAY LAST. Every XLS line above ASSIGNS its panel's
        // list outright, so an AddRows placed earlier is silently overwritten.
        if (!IsNG)
        {
            d[VariationPanel] = XlsVariationDisplay();
            AddRows(d, PrimingPanel, XlsPrimingDetailDisplay());
            AddRows(d, MagnetosPanel, XlsMagnetoDetailDisplay());
            AddRows(d, PowerPanel, XlsOilCoolerDisplay());
            AddRows(d, StandbyPanel, XlsStandbyVariationDisplay());
            AddRows(d, PowerPanel, new List<string> { "DA40_XLS_CRUISE_TABLE", "DA40_PERF_OAT" });
            AddRows(d, SimDamagePanel, XlsDamageDetailDisplay());
        }

        RemoveNgOnlyRows(d);

        return d;
    }

    public override Dictionary<string, string> GetButtonStateMapping()
        => new Dictionary<string, string>();

    /// <summary>
    /// Ctrl+M opens this aircraft's Monitor Manager, where the pilot ticks and un-ticks
    /// which background changes speak. Everything else falls through to the base.
    /// </summary>
    public override bool HandleHotkeyAction(
        Hotkeys.HotkeyAction action,
        SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm,
        Hotkeys.HotkeyManager hotkeyManager)
    {
        if (action == Hotkeys.HotkeyAction.MonitorManager)
        {
            (parentForm as MainForm)?.ShowCowsDA40MonitorManagerDialog();
            return true;
        }

        if (HandleDA40Readout(action, simConnect, announcer, parentForm)) return true;

        // Alt+P and Alt+N. The display windows are DIRECT Alt keys app-wide - the A380
        // uses Alt+E, Alt+S, Alt+N, Alt+P and Alt+I for exactly this - so the DA40 follows
        // the same convention rather than inventing a parallel output-mode chord. The MFD
        // borrows the ND key, which this aeroplane has no other use for.
        if (action == Hotkeys.HotkeyAction.ReadDisplayPFD)
        {
            hotkeyManager?.ExitOutputHotkeyMode();
            // KEYED BY DISPLAY, not by form type: both windows are the same class, so
            // the type-keyed tracker handed Alt+N the PFD window it had already opened.
            ShowTrackedWindow("DA40_PFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 PFD", "AS1000_PFD", "PFD", simConnect, announcer, this),
                w => w.Show());
            return true;
        }

        // Alt+M and Alt+N both open it. M is the obvious letter on a G1000 - the aeroplane
        // has a PFD and an MFD and no ND at all - and N is kept because the docs have said
        // so since the window was built.
        if (action == Hotkeys.HotkeyAction.ReadDisplayND ||
            action == Hotkeys.HotkeyAction.ReadDisplayMFD)
        {
            hotkeyManager?.ExitOutputHotkeyMode();
            ShowTrackedWindow("DA40_MFD",
                () => new Forms.DA40.CowsDA40DisplayForm(
                    "G1000 MFD", "AS1000_MFD", "MFD", simConnect, announcer, this),
                w => w.Show());
            return true;
        }

        if (action == Hotkeys.HotkeyAction.SetNavRadios)
        {
            hotkeyManager?.ExitInputHotkeyMode();
            return HandleDA40NavRadios(simConnect, announcer, parentForm);
        }

        if (action == Hotkeys.HotkeyAction.FCUSetBaro)
        {
            hotkeyManager?.ExitInputHotkeyMode();
            return HandleDA40BaroSet(simConnect, announcer, parentForm);
        }

        // ⚠️ THE GFC 700 HAD NO KEYBOARD AT ALL. Every other aircraft answers input mode's
        // Ctrl+A/S/H/V/P and this one answered only Ctrl+B, so a pilot could read the
        // autopilot and never set it without opening a panel. The actions are app-wide,
        // which is exactly why the gap was invisible - see the Ctrl+W retraction.
        if (hotkeyManager != null && parentForm != null)
        {
            if (action is Hotkeys.HotkeyAction.FCUSetAltitude or Hotkeys.HotkeyAction.FCUSetSpeed
                       or Hotkeys.HotkeyAction.FCUSetHeading or Hotkeys.HotkeyAction.FCUSetVS)
                return ShowApValueDialog(action, simConnect, announcer, parentForm, hotkeyManager);

            if (action == Hotkeys.HotkeyAction.FCUSetAutopilot)
                return ShowApButtonsDialog(simConnect, announcer, parentForm, hotkeyManager);
        }

        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    /// <summary>
    /// Per-panel display text overrides, for fields MSFSBA computes rather than reads.
    /// </summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetXlsStartDisplayOverride(varKey, value, out displayText)) return true;
        // ⚠️ This was listed TWICE. Harmless - the second call can never run - but it is
        // dead code in the one place a reader checks to find out which override owns a key.
        if (TryGetXlsMixtureDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetStandbyDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetFsCopilotDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetPerfTableDisplayOverride(varKey, out displayText)) return true;
        if (TryGetXlsPowerDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetPrimingDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetXlsFuelDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetEngineHealthDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetEcuDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetFuelDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetFlapsDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetTrimDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetFlightControlDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetBrakeDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetAudioDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetBreakerDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetDoorDisplayOverride(varKey, value, out displayText)) return true;
        if (TryGetPayloadDisplayOverride(varKey, value, out displayText)) return true;

        // Gauges with published arcs report the arc alongside the number. A sighted pilot
        // does not read "87 degrees" off the oil temperature gauge — they see the needle in
        // the green, and that is the reading. See DA40InstrumentBands (AFM section 2.5).
        var band = DA40InstrumentBands.For(varKey);
        if (band != null && GetVariables().TryGetValue(varKey, out var def))
        {
            // ⚠️ THE NUMBER IS CONVERTED AND THE BAND IS NOT. An arc is a physical fact
            // about the engine - the green on the oil-temperature gauge is the same span of
            // heat whether it is read in celsius or fahrenheit - so the band is looked up
            // from the RAW value and only the figure spoken beside it changes.
            if (!TryUnitText(def.Units, value, def.Format, out string withUnits))
            {
                string number = value.ToString(def.Format);
                withUnits = string.IsNullOrWhiteSpace(def.Units) ? number : $"{number} {def.Units}";
            }

            displayText = DA40InstrumentBands.Annotate(varKey, value, withUnits);
            return true;
        }

        // EVERY OTHER DA40 READOUT, rendered here rather than by MainForm's generic
        // formatter, because that formatter cannot do it. Its numeric branch is a
        // hardcoded `switch (varDef.Units)` knowing only volts, millibars, inHg and kHz;
        // everything else falls to `$"{value:F0}"` — whole numbers, no unit, and
        // `def.Format` NEVER CONSULTED AT ALL.
        //
        // That is the true cause of two things reported live and is a correction to an
        // earlier note in this file's history: "Fuel Flow: 9" with no unit, and COM
        // frequencies reading "128" despite Format being set to F3. Format was not being
        // defaulted to F0 — it was being ignored.
        //
        // Changing the shared formatter would silently re-render every other aircraft's
        // status rows, so the DA40 renders its own instead.
        if (varKey.StartsWith("DA40_")
            && GetVariables().TryGetValue(varKey, out var readout)
            && readout.RenderAsReadOnlyStatus
            // ValueDescriptions is NEVER null - SimVarDefinition initialises it to an empty
            // dictionary - so this must test COUNT. Written as "== null" it is always
            // false, and this whole fallback was dead from the day it was added: every
            // readout without its own override kept rendering as a bare whole number, which
            // is exactly how "COM 1 Active: 133" was reported.
            && readout.ValueDescriptions is not { Count: > 0 }
            && !string.IsNullOrWhiteSpace(readout.Units))
        {
            // The pilot's chosen units first; SpokenUnit is the fallback for every
            // dimension the G1000 has no setting for - volts, amperes, RPM, hours.
            if (!TryUnitText(readout.Units, value, readout.Format, out displayText))
            {
                // TrimEnd, because a unitless quantity must not leave a trailing space for
                // the screen reader to pause on.
                displayText =
                    $"{value.ToString(readout.Format)} {SpokenUnit(readout.Units)}".TrimEnd();
            }
            return true;
        }

        if (varKey == "DA40_AP_FD_PITCH" || varKey == "DA40_AP_FD_BANK")
        {
            displayText = DescribeFlightDirector(varKey, value);
            return true;
        }

        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>
    /// Panel writes that need more than a plain set — chiefly the switches that expose
    /// only a toggle event and must be written conditionally so a combo is idempotent.
    /// Each panel adds its own handler; anything unclaimed falls through to the generic path.
    /// </summary>
    public override bool HandleUIVariableSet(string varKey, double value,
        SimConnect.SimVarDefinition varDef, SimConnect.SimConnectManager simConnect,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (HandleElectricalSet(varKey, value, simConnect)) return true;
        if (HandleLightingSet(varKey, value, simConnect)) return true;
        if (HandleIcePitotSet(varKey, value, simConnect)) return true;
        if (HandleStandbySet(varKey, value, simConnect, announcer)) return true;
        if (HandlePowerSet(varKey, value, simConnect, announcer)) return true;
        if (HandleFuelSet(varKey, value, simConnect, announcer)) return true;
        if (HandleFlapsSet(varKey, value, simConnect, announcer)) return true;
        if (HandleTrimSet(varKey, value, simConnect, announcer)) return true;
        if (HandleBrakeSet(varKey, value, simConnect, announcer)) return true;
        if (HandleAudioSet(varKey, value, simConnect, announcer)) return true;
        if (HandleBreakerSet(varKey, value, simConnect, announcer)) return true;
        if (HandleDoorSet(varKey, value, simConnect, announcer)) return true;
        if (HandleEltSet(varKey, value, simConnect, announcer)) return true;
        if (HandleCabinAirSet(varKey, value, simConnect, announcer)) return true;
        if (HandleRadioSet(varKey, value, simConnect, announcer)) return true;
        if (HandleAutopilotSet(varKey, value, simConnect, announcer)) return true;
        if (HandlePayloadSet(varKey, value, simConnect, announcer)) return true;
        if (HandleFailureSet(varKey, value, simConnect, announcer)) return true;
        if (HandleEngineStartSet(varKey, value, simConnect, announcer)) return true;
        if (HandleMagnetoSet(varKey, value, simConnect, announcer)) return true;
        if (HandleXlsPowerSet(varKey, value, simConnect, announcer)) return true;
        if (HandlePrimingSet(varKey, value, simConnect, announcer)) return true;
        if (HandleXlsFuelSet(varKey, value, simConnect, announcer)) return true;
        if (HandleXlsStartSet(varKey, value, simConnect, announcer)) return true;
        if (HandleXlsMixtureSet(varKey, value, simConnect, announcer)) return true;
        if (HandleXlsMixtureSet(varKey, value, simConnect, announcer)) return true;
        if (HandleOptionSet(varKey, value, simConnect)) return true;
        if (HandleEcuSet(varKey, value, simConnect, announcer)) return true;

        return base.HandleUIVariableSet(varKey, value, varDef, simConnect, announcer);
    }

    // ==================================================================================
    // GFC 700 autopilot control types
    //
    // Measured on the aircraft, not assumed: there is NO absolute altitude set.
    //   AP_ALT_VAR_SET_ENGLISH  ignores its parameter and STEPS the preselect. ⚠️ The step
    //   is not fixed: measured +1000 ft once (700→1700, 1800→2800) and +100 ft on a later
    //   pass from 0. Both readings agree on the part that matters - the value asked for is
    //   discarded - and neither is worth trusting as a step size.
    //   AP_ALT_VAR_INC / _DEC   ±100 ft
    // so altitude is inherently increment/decrement. Heading and vertical speed follow
    // the same GFC 700 knob model. There is no autothrottle on either airframe
    // (systems.cfg: autothrottle_available = 0), so speed is nominally unused.
    // ==================================================================================

    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    // ==================================================================================
    // Visual landing guidance — light GA, not a transport jet
    //
    // The inherited defaults are A320 numbers (Vref 140 kt, ±6° tone range) and are badly
    // wrong for an airframe with a 66–77 kt Vref. Figures below are from the COWS POH
    // p19-20 (NG, 2888 lb). The glideslope bias is the ILS reference datum height only:
    // the DA40's SimConnect datum sits essentially at the GS antenna, so there is no
    // meaningful airframe offset to add on top.
    // ==================================================================================

    public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
    {
        TypicalApproachAoaDeg     = 5.0,
        ReferenceVrefKnots        = IsNG ? 72.0 : 73.0,
        MaxPitchRateDegPerSec     = 5.0,    // light airframe: far more pitch authority
        MaxBankRateDegPerSec      = 8.0,
        TonePitchRangeDeg         = 10.0,   // wider GA attitude envelope than a jet
        ToneBankRangeDeg          = 10.0,
        GlideslopeAltitudeBiasFt  = 50.0    // ICAO TCH only
    };

    // Short wheelbase and a castoring nosewheel — the DA40 pivots far faster than any
    // airliner, so the rollout lead is well below the A320's 1.2 s default.
    public override double TaxiTurnLeadSeconds => 0.8;

    /// <summary>
    /// A unit as it should be HEARD. Most SimConnect unit names already read correctly,
    /// but a few are abbreviations a screen reader spells out letter by letter.
    /// </summary>
    private static string SpokenUnit(string units) => units switch
    {
        "inHg" => "inches",
        "celsius" => "degrees celsius",
        "rpm" => "R P M",
        "gallons per hour" => "gallons per hour",
        // ⚠️ "number" IS A REGISTRATION, NOT A UNIT, AND IT WAS BEING READ ALOUD. An L:var
        // must be registered Units = "number" - it holds a raw value and any other unit
        // makes SimConnect convert from a base unit it does not have (the standby subscale
        // read ZERO for exactly that reason) - but this fall-through then spoke the word.
        // The panel said "ECU Battery Capacity: 149.1 number", "Block Damage: 0.0 number",
        // "Trim Axis Input: 0.0 number", a dozen rows of it. A quantity whose unit the
        // package never names is honest as a bare number; it is not a "number number".
        "number" => "",
        _ => units
    };
}
