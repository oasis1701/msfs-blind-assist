namespace MSFSBlindAssist.Aircraft;

// Fenix v3 variants: the controls only some Fenix airframes have (read from Fenix 3.0.0.7426 on
// 2026-10-21), and hiding the existing controls the loaded variant lacks.
public partial class FenixA320Definition
{
    internal const string EconFlowName = "Econ Flow";
    internal const string ActPumpName = "ACT Pump";

    private const string EconFlowKey = "S_OH_PNEUMATIC_ECON_FLOW";
    private const string ActKey = "S_OH_FUEL_ACT";
    // The lights that only mirror their switch (measured 2026-10-24): each switch names its light as
    // UiEchoVariable, so the light stays quiet after the pilot's own pick and speaks a change from elsewhere.
    private const string EconFlowLowerLightKey = "I_OH_PNEUMATIC_ECON_FLOW_L";
    private const string ActLowerLightKey = "I_OH_FUEL_ACT_L";
    private const string CallsMidKey = "S_OH_CALLS_MID";
    private const string CallsExitKey = "S_OH_CALLS_EXIT";

    internal const string ClockPanel = "Clock";
    private const string HeadsetKey = "A320_Sound:Headset_On";
    private const string ChrKey = "S_MIP_CLOCK_CHR";
    private const string ChrRstKey = "S_MIP_CLOCK_RST";
    private const string DateKey = "S_MIP_CLOCK_SET";
    private const string EtKey = "S_MIP_CLOCK_ET";
    private const string UtcKey = "S_MIP_CLOCK_UTC";
    private const string IsisBaroPushKey = "S_MIP_ISFD_BARO_BUTTON";
    private const string CargoTempKey = "A_OH_PNEUMATIC_CARGO_AFT_TEMP";

    // Registered with Add, never the indexer: a second registration of one key must fail at load.
    partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition> variables)
    {
        void Switch(string name, string label, string echoLight) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = OffOn,
            UiEchoVariable = echoLight,
        });
        void Button(string name, string label) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsButton = true,
        });
        void Light(string name, string label) => variables.Add(name, new SimConnect.SimVarDefinition
        {
            Name = name,
            DisplayName = label,
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = OffOn,
        });

        void Combo(string name, string label, Dictionary<double, string> values) => variables.Add(name,
            new SimConnect.SimVarDefinition
            {
                Name = name,
                DisplayName = label,
                Type = SimConnect.SimVarType.LVar,
                UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
                ValueDescriptions = values,
            });

        // Cockpit controls the app did not offer (owner request 2026-10-21).
        Combo(HeadsetKey, "Headset", OffOn);
        Button(ChrKey, "Chronometer Start/Stop");
        Button(ChrRstKey, "Chronometer Reset");
        Button(DateKey, "Date");
        Button(IsisBaroPushKey, "ISIS Baro Push");
        Combo(EtKey, "Elapsed Time", new Dictionary<double, string> { [0] = "Run", [1] = "Stop", [2] = "Reset" });
        Combo(UtcKey, "UTC Selector", new Dictionary<double, string> { [0] = "GPS", [1] = "Internal", [2] = "Set" });
        Combo(CargoTempKey, "Aft Cargo Temperature", new Dictionary<double, string>(variables["A_OH_PNEUMATIC_AFT_TEMP"].ValueDescriptions!));

        Switch(EconFlowKey, EconFlowName, EconFlowLowerLightKey);
        Switch(ActKey, ActPumpName, ActLowerLightKey);
        Button(CallsMidKey, "Calls Mid");
        Button(CallsExitKey, "Calls Exit");

        Light(EconFlowLowerLightKey, EconFlowName + " Lower");
        // The upper ACT light never lit with the switch (measured 2026-10-24), and its legend is unknown: it keeps
        // its position name and no echo ([FNX-7]).
        Light("I_OH_FUEL_ACT_U", ActPumpName + " Upper");
        Light(ActLowerLightKey, ActPumpName + " Lower");
        Light("I_OH_ENG_N1_MODE_1_L", variables["S_OH_ENG_N1_MODE_1"].DisplayName + " Lower");
        Light("I_OH_ENG_N1_MODE_2_L", variables["S_OH_ENG_N1_MODE_2"].DisplayName + " Lower");

        VariantChanged -= OnVariantKnown;
        VariantChanged += OnVariantKnown;
    }

    // The loaded variant is spoken once. The config values arrive one per ProcessSimVarUpdate call,
    // so VariantChanged fires several times in a row; speech waits for a 1 s quiet period, then compares the
    // sentence, so one load is spoken once and a field the sentence does not mention never re-speaks it.
    private const int VariantSpeechQuietMs = 1000;
    private string? _spokenVariantText;
    private System.Windows.Forms.Timer? _variantSpeechTimer;

    /// <summary>The text to speak for <paramref name="now"/>: its description when it is known and differs
    /// from <paramref name="lastSpokenText"/>, else null.</summary>
    internal static string? VariantSpeech(string? lastSpokenText, FenixVariant now)
    {
        if (!now.IsKnown) return null;
        string text = now.Describe();
        return text == lastSpokenText ? null : text;
    }

    // Raised on the UI thread. Does not speak: it restarts the quiet-period timer.
    private void OnVariantKnown(object? sender, FenixVariant variant)
    {
        if (_variantSpeechTimer == null)
        {
            _variantSpeechTimer = new System.Windows.Forms.Timer { Interval = VariantSpeechQuietMs };
            _variantSpeechTimer.Tick += (_, _) =>
            {
                _variantSpeechTimer?.Stop();
                string? text = VariantSpeech(_spokenVariantText, Variant);
                if (text == null) return;
                _spokenVariantText = text;
                // Queued, not immediate: a background change the pilot did not trigger (CORE-7).
                lastAnnouncer?.Announce(text);
            };
        }
        _variantSpeechTimer.Stop();
        _variantSpeechTimer.Start();
    }

    partial void OnFenixSwitchedAway()
    {
        VariantChanged -= OnVariantKnown;
        _variantSpeechTimer?.Stop();
        _variantSpeechTimer?.Dispose();
        _variantSpeechTimer = null;
        _spokenVariantText = null;
    }

    partial void AddVariantPanelControls(Dictionary<string, List<string>> panels)
    {
        static void After(Dictionary<string, List<string>> panels, string panel, string anchor, string key)
        {
            var list = panels[panel];
            int i = list.IndexOf(anchor);
            if (i < 0)
                throw new InvalidOperationException($"Fenix panel \"{panel}\" has no {anchor} to place {key} after.");
            list.Insert(i + 1, key);
        }

        After(panels, "Air Conditioning and Pressurization", "S_OH_PNEUMATIC_PACK_FLOW", EconFlowKey);
        After(panels, "Calls", "S_OH_CALLS_FWD", CallsMidKey);
        After(panels, "Calls", CallsMidKey, CallsExitKey);
        After(panels, "Fuel", "S_OH_FUEL_CENTER_2", ActKey);

        panels[ClockPanel] = new List<string> { ChrKey, ChrRstKey, EtKey, UtcKey, DateKey };
        After(panels, "ISIS", "E_MIP_ISFD_BARO_DEC", IsisBaroPushKey);
        After(panels, "Air Conditioning and Pressurization", "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO", CargoTempKey);
        // First in the tab order: the panel has no physical order, and the headset is its most-used control.
        panels["Cockpit Other"].Insert(0, HeadsetKey);
    }

    partial void AddVariantPanelStructure(Dictionary<string, List<string>> structure)
    {
        var list = structure["Main Instrument Panel"];
        int i = list.IndexOf("Landing Gear");
        if (i < 0)
            throw new InvalidOperationException("Fenix \"Main Instrument Panel\" has no Landing Gear to place Clock after.");
        list.Insert(i + 1, ClockPanel);

        // A panel whose every control the loaded livery lacks is not listed (DCDU Left without a DCDU, ISIS
        // with the round standby gauges), so the arrow keys through the panels list never stop on an empty panel. Built on
        // every call, never cached: the answer follows the variant as it is read, and an unread field hides
        // nothing ([FNX-11]). The section keys stay, so the sections list MainForm builds once never loses one.
        // GetPanelControls is the base class's cached dictionary, not rebuilt here.
        var controls = GetPanelControls();
        foreach (var panels in structure.Values)
            panels.RemoveAll(panel => controls.TryGetValue(panel, out var keys) && HasNoVisibleControl(keys));
    }

    // True for a panel that has controls and shows none of them. A panel with no controls at all (a
    // display-only one) is never hidden by this rule.
    private bool HasNoVisibleControl(List<string> keys)
        => keys.Count > 0 && !keys.Any(key => IsPanelControlVisible(key, null));

    partial void AddVariantPanelDisplayVariables(Dictionary<string, List<string>> displayVariables)
        => displayVariables[ClockPanel] = new List<string> { "N_MIP_CLOCK_CHRONO", "N_MIP_CLOCK_ELAPSED" };

    // The FBW A320's Clock wording. The Fenix chronometer counts whole seconds, elapsed time whole minutes
    // (measured 2026-10-21); a negative value is the blank display.
    internal static string ChronometerText(double seconds)
    {
        if (seconds < 0) return "Reset";
        int t = (int)seconds;
        return $"{t / 60} minutes {t % 60} seconds";
    }

    internal static string ElapsedTimeText(double minutes)
    {
        if (minutes < 0) return "Reset";
        int t = (int)minutes;
        return $"{t / 60} hours {t % 60} minutes";
    }

    partial void TryVariantDisplayOverride(string varKey, double value, ref string? displayText)
    {
        if (varKey == "N_MIP_CLOCK_CHRONO") displayText = ChronometerText(value);
        else if (varKey == "N_MIP_CLOCK_ELAPSED") displayText = ElapsedTimeText(value);
    }

    /// <summary>
    /// True unless the variant's own field for this control says the airframe lacks it. An unread field
    /// (null, Unknown) never hides anything; a key not listed below is always fitted.
    /// </summary>
    internal static bool IsFitted(string varKey, FenixVariant variant)
    {
        if (varKey.StartsWith("S_OH_ADIRS_KEY_", StringComparison.Ordinal))
            return variant.HasGnadirs != false;
        if (varKey.StartsWith("S_DCDU1_", StringComparison.Ordinal))
            return variant.HasDcdu != false;

        return varKey switch
        {
            EconFlowKey or CallsMidKey or CallsExitKey
                => variant.Type is FenixAircraftType.Unknown or FenixAircraftType.A321,
            "S_OH_PNEUMATIC_PACK_FLOW" => variant.Type != FenixAircraftType.A321,
            ActKey => variant.ActCount is null or > 0,
            "S_OH_ENG_N1_MODE_1" or "S_OH_ENG_N1_MODE_2" => variant.Engine != FenixEngineFamily.Cfm56,
            "S_OH_NAV_DATA_DISP" or "S_OH_NAV_SYS_DISP" => variant.HasGnadirs != false,
            "S_OH_NAV_IR1_SWITCH" or "S_OH_NAV_IR2_SWITCH" or "S_OH_NAV_IR3_SWITCH"
                => variant.HasGnadirs != true,
            "S_DCDU2_DIM_BRT" => variant.HasDcdu != false,
            "S_MIP_ISFD_BUGS" or "S_MIP_ISFD_LS" or "S_MIP_ISFD_PLUS" or "S_MIP_ISFD_MINUS"
                or "S_MIP_ISFD_RST" or "E_MIP_ISFD_BARO_INC" or "E_MIP_ISFD_BARO_DEC"
                or IsisBaroPushKey
                => variant.HasIsis != false,
            "S_STANDBY_ATTITUDE_CAGE" => variant.HasIsis != true,
            "S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO" or CargoTempKey => variant.HasCargoHeat != false,
            _ => true,
        };
    }

    partial void IsVariantControlVisible(string varKey, ref bool visible)
    {
        if (!IsFitted(varKey, Variant)) visible = false;
    }

    internal const string NotFittedMessage = "Not fitted on this aircraft.";

    /// <summary>
    /// Speaks "Not fitted on this aircraft." and returns true when the variant lacks the control (a
    /// validation error, so CORE-7 lets it speak); silent and false otherwise.
    /// </summary>
    internal static bool TryRefuseNotFitted(string varKey, FenixVariant variant,
        Accessibility.ScreenReaderAnnouncer announcer)
    {
        if (IsFitted(varKey, variant)) return false;
        announcer.AnnounceImmediate(NotFittedMessage);
        return true;
    }

    // The plain writes: ECON FLOW and ACT PUMP hold their value (the model's latched-button template), and so
    // do the headset, UTC selector, ET Run/Stop and the cargo knob: one SetLVar write, nothing spoken. ET Reset is not
    // plain (the knob springs back, see HandleVariantUIVariableSet). The writer is passed in so a test can
    // see the write without a SimConnect.
    internal static bool TryWritePlain(string varKey, double value, Action<string, double> write)
    {
        if (varKey is not (EconFlowKey or ActKey or HeadsetKey or UtcKey or CargoTempKey or EtKey)) return false;
        if (varKey == EtKey && value == 2) return false;
        write(varKey, value);
        return true;
    }

    partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef,
        SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled)
    {
        // Also covers existing keys: PACK FLOW on an A321 never reaches the main handler.
        if (TryRefuseNotFitted(varKey, Variant, announcer))
        {
            handled = true;
            return;
        }

        if (TryWritePlain(varKey, value, simConnect.SetLVar))
        {
            handled = true;
            return;
        }

        switch (varKey)
        {
            case EtKey: // Reset (value 2): the real knob springs back to Stop, a written 2 stays 2.
                // PulseSpringSwitch writes 2, returns to the middle (1 = Stop) after the hold and forces a
                // read, so the open combo shows Stop again shortly after Reset.
                PulseSpringSwitch(EtKey, 2, simConnect);
                handled = true;
                break;
            case ChrKey:
            case ChrRstKey:
            case DateKey:
            case IsisBaroPushKey:
                if (value == 1)
                    ExecuteButtonTransition(varKey, varDef.DisplayName, simConnect, announcer, confirmPress: true);
                handled = true;
                break;
            case CallsMidKey:
            case CallsExitKey:
                // Same as CALLS MECH/ALL/FWD/AFT: the press-release pulse, then "<name> pressed".
                if (value == 1)
                    ExecuteButtonTransition(varKey, varDef.DisplayName, simConnect, announcer, confirmPress: true);
                handled = true;
                break;
        }
    }
}
